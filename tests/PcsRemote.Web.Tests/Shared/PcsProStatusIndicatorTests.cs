using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Hubs;
using PcsRemote.Web.Shared;
using Radzen;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public class PcsProStatusIndicatorTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // AC-1 through AC-8: State-to-colour mapping — colour class and label text
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [DataRow(PcsProState.NotRunning,              "status-grey",   "PCS Pro not running")]
    [DataRow(PcsProState.Launching,               "status-yellow", "PCS Pro starting\u2026")]
    [DataRow(PcsProState.LoginScreen,             "status-yellow", "Logging in\u2026")]
    [DataRow(PcsProState.MatchSelection,          "status-yellow", "Loading matches\u2026")]
    [DataRow(PcsProState.MatchSelectionSearching, "status-yellow", "Searching\u2026")]
    [DataRow(PcsProState.MatchSelectionReady,     "status-yellow", "Select a match")]
    [DataRow(PcsProState.MatchLoaded,             "status-green",  "Match loaded")]
    [DataRow(PcsProState.Error,                   "status-red",    "Error")]
    public void StateMapping_RendersCorrectColourClassAndLabel(
        PcsProState state, string expectedClass, string expectedLabel)
    {
        var mock = BuildMock(state);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<PcsProStatusIndicator>();

        var indicator = cut.Find(".pcs-status-indicator");
        indicator.ClassList.Should().Contain(expectedClass);
        indicator.TextContent.Trim().Should().Be(expectedLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-9: Real-time state update — cross colour-group boundary
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_CrossesColourGroupBoundary_IndicatorUpdates()
    {
        var mock = BuildMock(PcsProState.NotRunning);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<PcsProStatusIndicator>();

        // Initial: grey (Find throws ElementNotFoundException if absent — no NotBeNull needed)
        cut.Find(".status-grey");

        // Raise state change: grey → green (crosses colour-group boundary)
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            var indicator = cut.Find(".pcs-status-indicator");
            indicator.ClassList.Should().Contain("status-green");
            indicator.TextContent.Trim().Should().Be("Match loaded");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-10: Initialisation reads CurrentState (not a stale default)
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnInit_ReadsCurrentStateImmediately_NotDefault()
    {
        var mock = BuildMock(PcsProState.MatchLoaded);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<PcsProStatusIndicator>();

        var indicator = cut.Find(".pcs-status-indicator");
        indicator.ClassList.Should().Contain("status-green");
        indicator.TextContent.Trim().Should().Be("Match loaded");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-11: Disposal unsubscribes from StateChanged
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromStateChanged()
    {
        var mock = BuildMock(PcsProState.NotRunning);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<PcsProStatusIndicator>();

        // Use cut.Instance.Dispose() to call the component's IDisposable.Dispose()
        // synchronously so the VerifyRemove assertion below sees the removal immediately.
        // bUnit's cut.Dispose() defers the Blazor lifecycle teardown, causing the check
        // to race; cut.Instance.Dispose() is the correct pattern for this synchronous
        // bUnit test context.
        cut.Instance.Dispose();

        // Verify the handler was removed from the event
        mock.VerifyRemove(
            m => m.StateChanged -= It.IsAny<EventHandler<PcsProState>>(),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-12: MainLayout wire-up — indicator present in layout output
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MainLayout_ContainsPcsProStatusIndicator()
    {
        var mock = BuildMock(PcsProState.NotRunning);
        var trackerMock = new Mock<IConnectionTracker>();
        trackerMock.Setup(t => t.ConnectionCount).Returns(0);
        var manualModeMock = new Mock<IManualModeService>();
        manualModeMock.Setup(s => s.IsManualModeActive).Returns(false);
        var coordinatorMock = new Mock<IOperationCoordinatorService>();

        using var ctx = new BunitContext();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton(mock.Object);
        ctx.Services.AddSingleton<IConnectionTracker>(trackerMock.Object);
        ctx.Services.AddSingleton(manualModeMock.Object);
        ctx.Services.AddSingleton(coordinatorMock.Object);

        var cut = ctx.Render<MainLayout>(p =>
            p.Add(l => l.Body, builder => { }));

        cut.FindComponent<PcsProStatusIndicator>().Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-13: Unknown-state fallback renders red + "Error"
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void UnknownState_RendersRedIndicatorWithErrorLabel()
    {
        // Cast an out-of-range int to PcsProState to simulate a future enum addition
        var unknownState = (PcsProState)99;
        var mock = BuildMock(unknownState);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<PcsProStatusIndicator>();

        var indicator = cut.Find(".pcs-status-indicator");
        indicator.ClassList.Should().Contain("status-red");
        // The label text "Error" is verified here. The Debug.Assert(false, …) call in
        // HandleUnknownState is not programmatically asserted by this test — verifying
        // the rendered output is the testable AC-13 obligation; the debug assertion is a
        // development-time aid (writes to trace output in test runners, does not throw).
        indicator.TextContent.Trim().Should().Be("Error");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Mock<IPcsProAutomationService> BuildMock(PcsProState initialState)
    {
        var mock = new Mock<IPcsProAutomationService>();
        mock.Setup(s => s.CurrentState).Returns(initialState);
        return mock;
    }
}
