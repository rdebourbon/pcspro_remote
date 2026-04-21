using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;

namespace PcsRemote.E2E.Tests;

/// <summary>
/// Playwright E2E tests for HLPS-011 operational UX features:
/// dismiss multi-browser, operation status banner, automation log,
/// and debug section PIN gate.
/// </summary>
[TestClass]
[DoNotParallelize]
public class OperationalUxE2ETests
{
    private static PcsProWebApplicationFactory _factory = null!;
    private static string _serverAddress = null!;
    private static IPlaywright _playwright = null!;
    private static IBrowser _browser = null!;

    private IBrowserContext? _context1;
    private IPage? _page1;
    private IBrowserContext? _context2;
    private IPage? _page2;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _factory = new PcsProWebApplicationFactory();

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
        _factory.RealServices.GetRequiredService<IOperationCoordinatorService>().MarkComplete();
        _factory.RealServices.GetRequiredService<IManualModeService>().Disable();

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

    // ──────────────────────────────────────────────────────────────────────
    // TC-1 — Dismiss multi-browser
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One browser dismisses an error; all connected browsers see error clear
    /// and state return to NotRunning.
    /// </summary>
    [TestMethod]
    public async Task Dismiss_FromError_BothBrowsersSeeErrorClearAndNotRunning()
    {
        var mockOptions = _factory.RealServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<MockPcsProOptions>>();
        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();

        try
        {
            // Drive into Error state.
            mockOptions.Value.ForcedErrorMode = MockForcedErrorMode.LaunchingToLoginScreen;
            await _page1!.GotoAsync(_serverAddress);
            await mockService.LaunchAndLoginAsync(CancellationToken.None);

            // Wait for error display in page1.
            await _page1.Locator(".error-display")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Open page2 — should also show error.
            _context2 = await _browser.NewContextAsync();
            _page2 = await _context2.NewPageAsync();
            await _page2.GotoAsync(_serverAddress);
            await _page2.Locator(".error-display")
                .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Click dismiss on page1.
            mockOptions.Value.ForcedErrorMode = MockForcedErrorMode.None;
            await _page1.Locator(".error-display__dismiss-btn").ClickAsync();

            // Both browsers must see error display disappear.
            await _page1.Locator(".error-display")
                .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
            await _page2.Locator(".error-display")
                .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

            // Both browsers must show NotRunning state.
            await _page1.Locator(".pcs-status-indicator", new() { HasText = "PCS Pro not running" })
                .WaitForAsync(new() { Timeout = 10_000 });
            await _page2.Locator(".pcs-status-indicator", new() { HasText = "PCS Pro not running" })
                .WaitForAsync(new() { Timeout = 10_000 });
        }
        finally
        {
            mockOptions.Value.ForcedErrorMode = MockForcedErrorMode.None;
            if (mockService.CurrentState != PcsProState.NotRunning)
                await mockService.StopAsync(CancellationToken.None);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2 — Operation status banner multi-browser
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When an operation is in progress with a description, all browsers see
    /// the operation status banner. A late-joining browser receives the banner
    /// via the hub snapshot. Banner clears when operation completes.
    /// </summary>
    [TestMethod]
    public async Task OperationStatusBanner_ShowsInBothBrowsers_LateJoinerAndClear()
    {
        var coordinator = _factory.RealServices
            .GetRequiredService<IOperationCoordinatorService>();

        await _page1!.GotoAsync(_serverAddress);
        await _page1.Locator(".pcs-status-indicator")
            .WaitForAsync(new() { Timeout = 10_000 });

        // Begin operation BEFORE page2 connects (late-joiner scenario).
        coordinator.BeginOperation("Changing match\u2026");

        // Page1 (early joiner) must see the banner.
        await _page1.Locator(".operation-status-banner", new() { HasText = "Changing match" })
            .WaitForAsync(new() { Timeout = 5_000 });

        // Page2 joins late — must also see the banner via hub snapshot.
        _context2 = await _browser.NewContextAsync();
        _page2 = await _context2.NewPageAsync();
        await _page2.GotoAsync(_serverAddress);
        await _page2.Locator(".operation-status-banner", new() { HasText = "Changing match" })
            .WaitForAsync(new() { Timeout = 5_000 });

        // Complete the operation.
        coordinator.MarkComplete();

        // Banner must disappear in both browsers.
        await _page1.Locator(".operation-status-banner")
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
        await _page2.Locator(".operation-status-banner")
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 5_000 });
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3 — Automation log multi-browser
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Log entries emitted during automation are visible in the debug section
    /// log panel. A late-joining browser receives the buffer history.
    /// </summary>
    [TestMethod]
    public async Task AutomationLog_EntriesVisibleInBothBrowsers_LateJoinerGetsHistory()
    {
        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();

        try
        {
            // Page1 navigates and opens debug section.
            await _page1!.GotoAsync(_serverAddress);
            await _page1.Locator(".pcs-status-indicator")
                .WaitForAsync(new() { Timeout = 10_000 });
            await OpenDebugSectionAsync(_page1);

            // Trigger automation — generates log entries.
            await mockService.LaunchAndLoginAsync(CancellationToken.None);

            // Page1 must show log entries.
            await _page1.Locator(".automation-log-entry")
                .First.WaitForAsync(new() { Timeout = 5_000 });

            // Page2 joins late — should receive buffer snapshot.
            _context2 = await _browser.NewContextAsync();
            _page2 = await _context2.NewPageAsync();
            await _page2.GotoAsync(_serverAddress);
            await _page2.Locator(".pcs-status-indicator")
                .WaitForAsync(new() { Timeout = 10_000 });
            await OpenDebugSectionAsync(_page2);

            // Page2 must also show log entries (from snapshot).
            await _page2.Locator(".automation-log-entry")
                .First.WaitForAsync(new() { Timeout = 5_000 });

            // Both pages must have the same number of entries.
            var count1 = await _page1.Locator(".automation-log-entry").CountAsync();
            var count2 = await _page2.Locator(".automation-log-entry").CountAsync();
            count1.Should().BeGreaterThan(0, "Page1 must have log entries");
            count2.Should().BeGreaterThanOrEqualTo(count1,
                "Late-joining page must receive at least the same log entries");
        }
        finally
        {
            await mockService.StopAsync(CancellationToken.None);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4 — Debug section toggle and PIN gate
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Debug section toggle opens and closes. When no PIN is configured
    /// (test default), clicking Debug expands the section immediately.
    /// </summary>
    [TestMethod]
    public async Task DebugSection_Toggle_ExpandsAndCollapsesContent()
    {
        await _page1!.GotoAsync(_serverAddress);
        await _page1.Locator(".pcs-status-indicator")
            .WaitForAsync(new() { Timeout = 10_000 });

        // Content should not be visible initially.
        await _page1.Locator(".debug-section-content")
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });

        // Click toggle — content should appear.
        await _page1.Locator(".debug-section-toggle").ClickAsync();
        await _page1.Locator(".debug-section-content")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3_000 });

        // Click toggle again — content should hide.
        await _page1.Locator(".debug-section-toggle").ClickAsync();
        await _page1.Locator(".debug-section-content")
            .WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3_000 });
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clicks the Debug toggle button and waits for the debug section content
    /// to become visible. Assumes no PIN is configured (test default).
    /// </summary>
    private static async Task OpenDebugSectionAsync(IPage page)
    {
        await page.Locator(".debug-section-toggle").ClickAsync();
        await page.Locator(".debug-section-content")
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5_000 });
    }
}
