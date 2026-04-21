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
public class ConnectedUserCountTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // AC-1: Zero users
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_ZeroUsers_RendersCorrectLabelAndClass()
    {
        var mock = BuildMock(0);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<ConnectedUserCount>();

        var el = cut.Find(".connected-user-count");
        el.TextContent.Trim().Should().Be("0 users online");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-2: Singular user
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_OneUser_RendersSingularLabel()
    {
        var mock = BuildMock(1);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<ConnectedUserCount>();

        cut.Find(".connected-user-count").TextContent.Trim().Should().Be("1 user online");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-3: Plural users
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_ThreeUsers_RendersPluralLabel()
    {
        var mock = BuildMock(3);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<ConnectedUserCount>();

        cut.Find(".connected-user-count").TextContent.Trim().Should().Be("3 users online");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-4: Real-time update — count increase
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CountChanged_Increase_RendersUpdatedLabel()
    {
        var mock = BuildMock(0);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<ConnectedUserCount>();
        cut.Find(".connected-user-count").TextContent.Trim().Should().Be("0 users online");

        mock.Raise(t => t.ConnectionCountChanged += null, 1);

        cut.WaitForAssertion(() =>
            cut.Find(".connected-user-count").TextContent.Trim().Should().Be("1 user online"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-5a: Real-time update — plural-to-singular decrease
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CountChanged_PluralToSingular_RendersUpdatedLabel()
    {
        var mock = BuildMock(2);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<ConnectedUserCount>();

        mock.Raise(t => t.ConnectionCountChanged += null, 1);

        cut.WaitForAssertion(() =>
            cut.Find(".connected-user-count").TextContent.Trim().Should().Be("1 user online"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-5b: Real-time update — singular-to-zero decrease
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CountChanged_SingularToZero_RendersUpdatedLabel()
    {
        var mock = BuildMock(1);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<ConnectedUserCount>();

        mock.Raise(t => t.ConnectionCountChanged += null, 0);

        cut.WaitForAssertion(() =>
            cut.Find(".connected-user-count").TextContent.Trim().Should().Be("0 users online"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-6: Initialisation from current count
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnInit_ReadsCurrentCountImmediately_NotDefault()
    {
        var mock = BuildMock(2);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<ConnectedUserCount>();

        cut.Find(".connected-user-count").TextContent.Trim().Should().Be("2 users online");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-7: Disposal unsubscribes from ConnectionCountChanged
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromConnectionCountChanged()
    {
        var mock = BuildMock(0);
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<ConnectedUserCount>();

        // Use cut.Instance.Dispose() for synchronous disposal so VerifyRemove
        // can be asserted immediately (same pattern as SPEC-S-004 AC-11).
        cut.Instance.Dispose();

        mock.VerifyRemove(
            t => t.ConnectionCountChanged -= It.IsAny<Action<int>>(),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-8: MainLayout wire-up
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MainLayout_ContainsConnectedUserCount()
    {
        var mock = BuildMock(0);
        var statusMock = new Mock<IPcsProAutomationService>();
        statusMock.Setup(s => s.CurrentState).Returns(PcsRemote.Core.PcsProState.NotRunning);
        var manualModeMock = new Mock<IManualModeService>();
        manualModeMock.Setup(s => s.IsManualModeActive).Returns(false);
        var coordinatorMock = new Mock<IOperationCoordinatorService>();

        using var ctx = new BunitContext();
        ctx.Services.AddRadzenComponents();
        ctx.Services.AddSingleton<IConnectionTracker>(mock.Object);
        ctx.Services.AddSingleton<IPcsProAutomationService>(statusMock.Object);
        ctx.Services.AddSingleton<IManualModeService>(manualModeMock.Object);
        ctx.Services.AddSingleton(coordinatorMock.Object);

        var cut = ctx.Render<MainLayout>(p =>
            p.Add(l => l.Body, builder => { }));

        cut.FindComponent<ConnectedUserCount>().Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static Mock<IConnectionTracker> BuildMock(int initialCount)
    {
        var mock = new Mock<IConnectionTracker>();
        mock.Setup(t => t.ConnectionCount).Returns(initialCount);
        return mock;
    }
}
