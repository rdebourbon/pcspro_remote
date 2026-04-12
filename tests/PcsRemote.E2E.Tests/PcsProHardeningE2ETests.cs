using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;

namespace PcsRemote.E2E.Tests;

/// <summary>
/// Playwright E2E tests covering H-SC-3 through H-SC-8:
/// error display and retry, multi-browser operation locking, and manual mode.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PcsProHardeningE2ETests
{
    // All four static fields are guaranteed non-null after [ClassInitialize] completes,
    // which MSTest guarantees runs before any [TestMethod] in this class.
    private static PcsProWebApplicationFactory _factory = null!;
    private static string _serverAddress = null!;
    private static IPlaywright _playwright = null!;
    private static IBrowser _browser = null!;

    // Assigned in [TestInitialize], guaranteed non-null for the duration of each test body.
    private IBrowserContext? _context1;
    private IPage? _page1;
    private IBrowserContext? _context2;
    private IPage? _page2;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _factory = new PcsProWebApplicationFactory();

        // Trigger WAF EnsureServer() to start the Kestrel host.
        using var startupClient = _factory.CreateClient();
        _serverAddress = await _factory.ServerAddressTask.WaitAsync(TimeSpan.FromSeconds(30));

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        _context1 = await _browser.NewContextAsync();
        _page1 = await _context1.NewPageAsync();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        // Safety-net resets — guard against any test leaving shared singleton state behind.
        // These are no-ops when the test's own finally block already cleaned up correctly.
        _factory.RealServices.GetRequiredService<IOperationCoordinatorService>().MarkComplete();
        _factory.RealServices.GetRequiredService<IManualModeService>().Disable();
        _factory.RealServices.GetRequiredService<IOptions<MockPcsProOptions>>()
            .Value.ForcedErrorMode = MockForcedErrorMode.None;

        // Null-check each resource — a test may leave _page2/_context2 as null.
        if (_page2 is not null)
        {
            await _page2.CloseAsync();
            _page2 = null;
        }

        if (_context2 is not null)
        {
            await _context2.CloseAsync();
            _context2 = null;
        }

        if (_page1 is not null)
        {
            await _page1.CloseAsync();
            _page1 = null;
        }

        if (_context1 is not null)
        {
            await _context1.CloseAsync();
            _context1 = null;
        }
    }

    /// <summary>
    /// TC-1 (H-SC-8): Mock error injection drives state to Error; ErrorDisplay renders with a
    /// non-empty reason string and the retry button is visible and enabled.
    /// </summary>
    [TestMethod]
    public async Task ErrorDisplay_MockForcedError_RendersWithReasonAndRetryButton()
    {
        var mockOptions = _factory.RealServices.GetRequiredService<IOptions<MockPcsProOptions>>();
        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();

        try
        {
            // Force the LaunchingToLoginScreen error path deterministically.
            mockOptions.Value.ForcedErrorMode = MockForcedErrorMode.LaunchingToLoginScreen;

            await _page1!.GotoAsync(_serverAddress);
            await mockService.LaunchAndLoginAsync(CancellationToken.None);

            // ErrorDisplay renders only when state == Error.
            await _page1.Locator(".error-display")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Reason string must not be empty (H-SC-6: descriptive reason displayed).
            var reason = await _page1.Locator(".error-display__reason").InnerTextAsync();
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(reason),
                "ErrorDisplay must render a non-empty reason string.");

            // Retry button must be visible and enabled (manual mode is off, no op in-progress).
            var retryBtn = _page1.Locator(".error-display__retry-btn");
            await retryBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            Assert.IsFalse(
                await retryBtn.IsDisabledAsync(),
                "Retry button must be enabled when manual mode is off and no op is in-progress.");
        }
        finally
        {
            mockOptions.Value.ForcedErrorMode = MockForcedErrorMode.None;
            if (mockService.CurrentState != PcsProState.NotRunning)
                await mockService.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// TC-2 (H-SC-7): Clicking the retry button in Error state transitions the state machine
    /// out of Error and initiates the re-launch sequence.
    /// </summary>
    [TestMethod]
    public async Task RetryButton_Click_TransitionsOutOfErrorState()
    {
        var mockOptions = _factory.RealServices.GetRequiredService<IOptions<MockPcsProOptions>>();
        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();

        try
        {
            // Set up error state — LaunchingToLoginScreen is the simplest deterministic path.
            mockOptions.Value.ForcedErrorMode = MockForcedErrorMode.LaunchingToLoginScreen;
            await _page1!.GotoAsync(_serverAddress);
            await mockService.LaunchAndLoginAsync(CancellationToken.None);

            // Wait for retry button to confirm we are in Error state.
            var retryBtn = _page1.Locator(".error-display__retry-btn");
            await retryBtn.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Clear error mode before retry so the re-launch sequence succeeds.
            mockOptions.Value.ForcedErrorMode = MockForcedErrorMode.None;

            // Click retry — triggers RetryAsync() → NotRunning → LaunchAndLoginAsync().
            await retryBtn.ClickAsync();

            // ErrorDisplay must disappear: state has left Error.
            await _page1.Locator(".error-display")
                .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

            // The re-launch sequence must have progressed to a running state (H-SC-7).
            // With zero-delay mock, auto-selection may advance state to MatchLoaded before
            // this check runs, so assert any non-Error running state (yellow or green class).
            await _page1.Locator(".pcs-status-indicator:is(.status-yellow, .status-green)")
                .WaitForAsync(new() { Timeout = 10_000 });
        }
        finally
        {
            mockOptions.Value.ForcedErrorMode = MockForcedErrorMode.None;
            await mockService.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// TC-3 (H-SC-5): While an operation is in progress, all connected browser contexts see
    /// the action buttons disabled. Buttons re-enable after the operation completes.
    /// </summary>
    [TestMethod]
    public async Task OperationLocking_WhileInProgress_BothBrowsersSeeBtnDisabled()
    {
        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();
        var coordinator = _factory.RealServices
            .GetRequiredService<IOperationCoordinatorService>();
        var scoreboardService = _factory.RealServices.GetRequiredService<IScoreboardService>();

        try
        {
            // Drive page1 to MatchLoaded so the action buttons are rendered.
            await DriveToMatchLoadedAsync(_page1!);

            // Open page2 — service is already in MatchLoaded, buttons render on navigation.
            _context2 = await _browser.NewContextAsync();
            _page2 = await _context2.NewPageAsync();
            await _page2.GotoAsync(_serverAddress);
            await _page2.Locator(".change-match-button")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Baseline: both buttons are enabled in both browser contexts.
            Assert.IsFalse(await _page1!.Locator(".refresh-scoreboard-button").IsDisabledAsync());
            Assert.IsFalse(await _page1.Locator(".change-match-button").IsDisabledAsync());
            Assert.IsFalse(await _page2.Locator(".refresh-scoreboard-button").IsDisabledAsync());
            Assert.IsFalse(await _page2.Locator(".change-match-button").IsDisabledAsync());

            // Simulate an operation beginning — BeginOperation() fires OperationInProgressChanged(true)
            // to all subscribers, which triggers the broadcaster to push the flag to all Blazor circuits.
            // Assert the CAS succeeds to fail fast if a prior test leaked a held lock.
            Assert.IsTrue(
                coordinator.BeginOperation(),
                "BeginOperation CAS must succeed — coordinator must be idle at test start.");

            // Both browser contexts must render the buttons as disabled.
            await _page1.Locator(".refresh-scoreboard-button[disabled]")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page1.Locator(".change-match-button[disabled]")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page2.Locator(".refresh-scoreboard-button[disabled]")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page2.Locator(".change-match-button[disabled]")
                .WaitForAsync(new() { Timeout = 5_000 });

            // Release the lock — MarkComplete() fires OperationInProgressChanged(false).
            coordinator.MarkComplete();

            // Buttons must re-enable in both browser contexts.
            await _page1.Locator(".refresh-scoreboard-button:not([disabled])")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page1.Locator(".change-match-button:not([disabled])")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page2.Locator(".refresh-scoreboard-button:not([disabled])")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page2.Locator(".change-match-button:not([disabled])")
                .WaitForAsync(new() { Timeout = 5_000 });
        }
        finally
        {
            await mockService.StopAsync(CancellationToken.None);
            scoreboardService.ClearCache();
        }
    }

    /// <summary>
    /// TC-4 (H-SC-3): When manual mode is enabled, the banner is visible in all connected
    /// browser contexts and the action buttons are disabled.
    /// </summary>
    [TestMethod]
    public async Task ManualMode_Enable_BothBrowsersSeeBannerAndDisabledButtons()
    {
        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();
        var manualMode = _factory.RealServices.GetRequiredService<IManualModeService>();
        var scoreboardService = _factory.RealServices.GetRequiredService<IScoreboardService>();

        try
        {
            // Drive both browsers to MatchLoaded.
            await DriveToMatchLoadedAsync(_page1!);

            _context2 = await _browser.NewContextAsync();
            _page2 = await _context2.NewPageAsync();
            await _page2.GotoAsync(_serverAddress);
            await _page2.Locator(".change-match-button")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Enable manual mode — ManualModeChanged(true) propagates to all Blazor circuits.
            manualMode.Enable();

            // Both browsers must show the manual mode banner (H-SC-3).
            await _page1!.Locator(".manual-mode-banner")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            await _page2.Locator(".manual-mode-banner")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

            // Action buttons must be disabled in both browser contexts (H-SC-4).
            await _page1.Locator(".refresh-scoreboard-button[disabled]")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page1.Locator(".change-match-button[disabled]")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page2.Locator(".refresh-scoreboard-button[disabled]")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page2.Locator(".change-match-button[disabled]")
                .WaitForAsync(new() { Timeout = 5_000 });
        }
        finally
        {
            manualMode.Disable();
            await mockService.StopAsync(CancellationToken.None);
            scoreboardService.ClearCache();
        }
    }

    /// <summary>
    /// TC-5 (H-SC-4): When manual mode is disabled, the banner disappears, buttons re-enable,
    /// and the state machine remains in the same state (no reset occurs).
    /// </summary>
    [TestMethod]
    public async Task ManualMode_Disable_BannerHidesAndButtonsReenabledStatePreserved()
    {
        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();
        var manualMode = _factory.RealServices.GetRequiredService<IManualModeService>();
        var scoreboardService = _factory.RealServices.GetRequiredService<IScoreboardService>();

        try
        {
            // Drive both browsers to MatchLoaded.
            await DriveToMatchLoadedAsync(_page1!);

            _context2 = await _browser.NewContextAsync();
            _page2 = await _context2.NewPageAsync();
            await _page2.GotoAsync(_serverAddress);
            await _page2.Locator(".change-match-button")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Enable and verify banner visible (prerequisite for the disable step).
            manualMode.Enable();
            await _page1!.Locator(".manual-mode-banner")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
            await _page2.Locator(".manual-mode-banner")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });

            // Disable manual mode — ManualModeChanged(false) propagates to all Blazor circuits.
            manualMode.Disable();

            // Banner must disappear in both browser contexts.
            await _page1.Locator(".manual-mode-banner")
                .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
            await _page2.Locator(".manual-mode-banner")
                .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });

            // Buttons must re-enable in both browser contexts.
            await _page1.Locator(".refresh-scoreboard-button:not([disabled])")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page1.Locator(".change-match-button:not([disabled])")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page2.Locator(".refresh-scoreboard-button:not([disabled])")
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page2.Locator(".change-match-button:not([disabled])")
                .WaitForAsync(new() { Timeout = 5_000 });

            // State machine must still be at MatchLoaded — status indicator shows "Match loaded".
            await _page1.Locator(".pcs-status-indicator", new() { HasText = "Match loaded" })
                .WaitForAsync(new() { Timeout = 5_000 });
            await _page2.Locator(".pcs-status-indicator", new() { HasText = "Match loaded" })
                .WaitForAsync(new() { Timeout = 5_000 });
        }
        finally
        {
            manualMode.Disable();
            await mockService.StopAsync(CancellationToken.None);
            scoreboardService.ClearCache();
        }
    }

    /// <summary>
    /// Navigates <paramref name="page"/> to the server, drives the singleton
    /// <see cref="MockPcsProAutomationService"/> to <see cref="PcsProState.MatchLoaded"/>,
    /// and waits for the Blazor circuit to reflect that state.
    /// </summary>
    private async Task DriveToMatchLoadedAsync(IPage page)
    {
        await page.GotoAsync(_serverAddress);

        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();

        await mockService.LaunchAndLoginAsync(CancellationToken.None);

        var matches = await mockService.GetTodaysMatchesAsync(CancellationToken.None);
        await mockService.LoadMatchAsync(matches[0], CancellationToken.None);

        await page.Locator(".change-match-button")
            .WaitForAsync(new() { Timeout = 10_000 });
    }
}
