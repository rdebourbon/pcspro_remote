using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;

namespace PcsRemote.E2E.Tests;

/// <summary>
/// Playwright E2E tests for YouTube live stream controls (IS-008 S-007).
/// Validates cross-circuit status synchronisation, conditional visibility,
/// and themed appearance.
/// </summary>
[TestClass]
[DoNotParallelize]
public class YouTubeStreamE2ETests
{
    private const int DefaultTimeoutMs = 10_000;
    private const int ShortTimeoutMs = 5_000;

    // All four static fields are guaranteed non-null after [ClassInitialize] completes,
    // which MSTest guarantees runs before any [TestMethod] in this class.
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

        // Reset singleton services to clean state for next test.
        var ytService = _factory.RealServices.GetRequiredService<IYouTubeLiveStreamService>();
        if (ytService.CurrentStatus is LiveStreamStatus.Live or LiveStreamStatus.Starting or LiveStreamStatus.Stopping)
        {
            await ytService.StopStreamAsync(CancellationToken.None);
        }
        else if (ytService.CurrentStatus is LiveStreamStatus.Error)
        {
            await ytService.ResetAsync(CancellationToken.None);
        }

        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();
        await mockService.StopAsync(CancellationToken.None);

        var scoreboardService = _factory.RealServices.GetRequiredService<IScoreboardService>();
        scoreboardService.ClearCache();
    }

    /// <summary>
    /// S-YT-7: When one browser context clicks Start Stream, both contexts see the
    /// status update to Live. Validates cross-circuit SignalR synchronisation.
    /// </summary>
    [TestMethod]
    public async Task StartStream_BothContextsSeeStatusLive()
    {
        // Step 1: Drive page1 to MatchLoaded and verify streaming controls visible.
        await DriveToMatchLoadedAsync(_page1!);
        await _page1!.Locator(".streaming-controls")
            .WaitForAsync(new() { Timeout = DefaultTimeoutMs });

        // Step 2: Open page2 — singleton service is already in MatchLoaded.
        _context2 = await _browser.NewContextAsync();
        _page2 = await _context2.NewPageAsync();
        await _page2.GotoAsync(_serverAddress);
        await _page2.Locator(".streaming-controls")
            .WaitForAsync(new() { Timeout = DefaultTimeoutMs });

        // Step 3: Both contexts should show Idle status.
        await _page1.Locator(".streaming-controls__badge", new() { HasText = "Idle" })
            .WaitForAsync(new() { Timeout = ShortTimeoutMs });
        await _page2.Locator(".streaming-controls__badge", new() { HasText = "Idle" })
            .WaitForAsync(new() { Timeout = ShortTimeoutMs });

        // Step 4: Click Start Stream on page1.
        await _page1.Locator(".streaming-controls__btn--start").ClickAsync();

        // Step 5: Both contexts should transition to Live.
        // Mock has 100ms start delay so this should be fast.
        await _page1.Locator(".streaming-controls__badge", new() { HasText = "Live" })
            .WaitForAsync(new() { Timeout = DefaultTimeoutMs });
        await _page2.Locator(".streaming-controls__badge", new() { HasText = "Live" })
            .WaitForAsync(new() { Timeout = DefaultTimeoutMs });

        // Step 6: Stop the stream to clean up.
        await _page1.Locator(".streaming-controls__btn--stop").ClickAsync();
        await _page1.Locator(".streaming-controls__badge", new() { HasText = "Idle" })
            .WaitForAsync(new() { Timeout = DefaultTimeoutMs });
    }

    /// <summary>
    /// Streaming controls are visible when match is loaded and absent when no match
    /// is loaded (state ≠ MatchLoaded).
    /// </summary>
    [TestMethod]
    public async Task StreamingControls_VisibleOnlyWhenMatchLoaded()
    {
        // Step 1: Navigate — initial state is NotRunning, streaming controls absent.
        await _page1!.GotoAsync(_serverAddress);
        await _page1.Locator(".pcs-status-indicator", new() { HasText = "PCS Pro not running" })
            .WaitForAsync(new() { Timeout = DefaultTimeoutMs });

        var streamControls = _page1.Locator(".streaming-controls");
        Assert.AreEqual(0, await streamControls.CountAsync(),
            "Streaming controls should not be visible when PCS Pro is not running");

        // Step 2: Drive to MatchLoaded — streaming controls should appear.
        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();
        await mockService.LaunchAndLoginAsync(CancellationToken.None);
        var matches = await mockService.GetTodaysMatchesAsync(CancellationToken.None);
        Assert.AreNotEqual(0, matches.Count, "Mock should return at least one match");
        await mockService.LoadMatchAsync(matches[0], CancellationToken.None);

        await streamControls.WaitForAsync(new() { Timeout = DefaultTimeoutMs });
        Assert.AreEqual(1, await streamControls.CountAsync(),
            "Streaming controls should be visible when match is loaded");
    }

    /// <summary>
    /// S-YT-16: Themed appearance smoke test — streaming controls badge uses the
    /// maroon/gold colour palette defined in the club theme.
    /// </summary>
    [TestMethod]
    public async Task StreamingControls_UseClubThemeColours()
    {
        await DriveToMatchLoadedAsync(_page1!);
        await _page1!.Locator(".streaming-controls")
            .WaitForAsync(new() { Timeout = DefaultTimeoutMs });

        // Verify the streaming controls header uses club theme colours.
        // The badge background for "idle" state should use the maroon palette.
        var badge = _page1.Locator(".streaming-controls__badge");
        await badge.WaitForAsync(new() { Timeout = ShortTimeoutMs });

        var bgColor = await badge.EvaluateAsync<string>(
            "el => getComputedStyle(el).backgroundColor");

        // Maroon palette: #800020 = rgb(128, 0, 32) or similar dark red tones.
        // Parse rgb(r, g, b) and validate within the maroon colour range.
        Assert.IsFalse(
            string.IsNullOrEmpty(bgColor) ||
            bgColor == "rgba(0, 0, 0, 0)" ||
            bgColor == "rgb(255, 255, 255)",
            $"Expected themed background colour, got: {bgColor}");

        var rgbValues = bgColor
            .Replace("rgb(", "").Replace("rgba(", "").Replace(")", "")
            .Split(',');
        int r = int.Parse(rgbValues[0].Trim());
        int g = int.Parse(rgbValues[1].Trim());
        int b = int.Parse(rgbValues[2].Trim());

        Assert.IsTrue(r >= 80 && g <= 50 && b <= 60,
            $"Expected maroon palette (high red, low green, low blue), got rgb({r}, {g}, {b})");
    }

    /// <summary>
    /// Navigates to the server, drives the singleton mock to MatchLoaded,
    /// and waits for the Blazor circuit to reflect that state.
    /// </summary>
    private async Task DriveToMatchLoadedAsync(IPage page)
    {
        await page.GotoAsync(_serverAddress);

        var mockService = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();

        await mockService.LaunchAndLoginAsync(CancellationToken.None);

        var matches = await mockService.GetTodaysMatchesAsync(CancellationToken.None);
        Assert.AreNotEqual(0, matches.Count, "Mock should return at least one match");
        await mockService.LoadMatchAsync(matches[0], CancellationToken.None);

        await page.Locator(".change-match-button")
            .WaitForAsync(new() { Timeout = DefaultTimeoutMs });
    }
}
