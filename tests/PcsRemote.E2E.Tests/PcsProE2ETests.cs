using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;

namespace PcsRemote.E2E.Tests;

[TestClass]
[DoNotParallelize]
public class PcsProE2ETests
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

        // Trigger WAF EnsureServer() to start the Kestrel host.
        using var startupClient = _factory.CreateClient();
        _serverAddress = await _factory.ServerAddressTask.WaitAsync(TimeSpan.FromSeconds(30));

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        // Null-check each field — if ClassInitialize throws, later fields may be null.
        // Dispose browser resources before factory to avoid Playwright connection-reset noise.
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
        // Null-check each field — AC-1 leaves _page2 and _context2 as null.
        // _page2 is also set to null in AC-2 after CloseAsync() to prevent a second close here.
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
    /// AC-1 (W-SC-10): Root URL returns HTTP 200 and the status indicator is present in the DOM
    /// with the expected initial text.
    /// </summary>
    [TestMethod]
    public async Task Smoke_RootUrl_Returns200AndStatusIndicatorInDom()
    {
        var response = await _page1!.GotoAsync(_serverAddress);

        Assert.IsNotNull(response);
        Assert.AreEqual(200, response.Status);

        // Blazor renders components asynchronously after the initial HTTP response.
        // WaitForAsync with HasText blocks until the circuit connects and the component renders.
        await _page1.Locator(".pcs-status-indicator", new() { HasText = "PCS Pro not running" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// AC-2 (W-SC-3): Connected user count updates as browser contexts connect and disconnect.
    /// </summary>
    [TestMethod]
    public async Task ConnectedUserCount_TwoContexts_UpdatesCorrectly()
    {
        // Step 1: Context 1 navigates; wait until the circuit opens and count reaches 1.
        await _page1!.GotoAsync(_serverAddress);
        await _page1.Locator(".connected-user-count", new() { HasText = "1 user online" })
            .WaitForAsync(new() { Timeout = 10_000 });

        // Steps 2–3: Context 2 navigates; wait until both contexts show 2 users.
        _context2 = await _browser.NewContextAsync();
        _page2 = await _context2.NewPageAsync();
        await _page2.GotoAsync(_serverAddress);

        await _page2.Locator(".connected-user-count", new() { HasText = "2 users online" })
            .WaitForAsync(new() { Timeout = 10_000 });
        await _page1.Locator(".connected-user-count", new() { HasText = "2 users online" })
            .WaitForAsync(new() { Timeout = 10_000 });

        // Step 4: Close Context 2's page. Set _page2 = null immediately after CloseAsync()
        // returns — TestCleanup calls CloseAsync() on non-null pages, so leaving _page2 non-null
        // after closing it here would cause a second CloseAsync() call on an already-closed page.
        await _page2.CloseAsync();
        _page2 = null;

        // Step 5: Context 1 eventually shows count = 1 after the circuit retention period elapses.
        await _page1.Locator(".connected-user-count", new() { HasText = "1 user online" })
            .WaitForAsync(new() { Timeout = 10_000 });
    }

    /// <summary>
    /// AC-3 (W-SC-9): A state change triggered in the test host is broadcast to all connected contexts.
    /// </summary>
    [TestMethod]
    public async Task StateChange_BroadcastToBothContexts()
    {
        // Steps 1–2: Both contexts navigate and confirm the initial NotRunning state.
        await _page1!.GotoAsync(_serverAddress);
        await _page1.Locator(".pcs-status-indicator", new() { HasText = "PCS Pro not running" })
            .WaitForAsync(new() { Timeout = 10_000 });

        _context2 = await _browser.NewContextAsync();
        _page2 = await _context2.NewPageAsync();
        await _page2.GotoAsync(_serverAddress);
        await _page2.Locator(".pcs-status-indicator", new() { HasText = "PCS Pro not running" })
            .WaitForAsync(new() { Timeout = 10_000 });

        // Step 3: Trigger state transition via the singleton mock service.
        // MockPcsProAutomationService is AddSingleton in the Kestrel host — RealServices
        // reaches the same instance that Blazor components subscribe to.
        var service = (MockPcsProAutomationService)_factory.RealServices
            .GetRequiredService<IPcsProAutomationService>();
        await service.LaunchAndLoginAsync(CancellationToken.None);

        try
        {
            // Step 4: Both contexts eventually show "Loading matches…" (PcsProState.MatchSelection label).
            // InvokeAsync(StateHasChanged) queues the DOM update asynchronously on the circuit dispatcher;
            // it does NOT complete synchronously even though the mock transitions are zero-delay.
            await _page1.Locator(".pcs-status-indicator", new() { HasText = "Loading matches\u2026" })
                .WaitForAsync(new() { Timeout = 10_000 });
            await _page2.Locator(".pcs-status-indicator", new() { HasText = "Loading matches\u2026" })
                .WaitForAsync(new() { Timeout = 10_000 });
        }
        finally
        {
            // Step 5: Reset singleton state to NotRunning unconditionally. Without this, a test
            // failure mid-assertion leaves the service in MatchSelection state, causing AC-1 to
            // produce a false failure if it runs after AC-3 in the same session.
            await service.StopAsync(CancellationToken.None);
        }
    }
}
