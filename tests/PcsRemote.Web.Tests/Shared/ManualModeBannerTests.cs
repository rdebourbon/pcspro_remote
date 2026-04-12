using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Shared;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public sealed class ManualModeBannerTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static Mock<IManualModeService> BuildMock(bool isActive)
    {
        var mock = new Mock<IManualModeService>();
        mock.Setup(s => s.IsManualModeActive).Returns(isActive);
        return mock;
    }

    private static IRenderedComponent<ManualModeBanner> Render(Mock<IManualModeService> mock)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);
        return ctx.Render<ManualModeBanner>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-11  Render with IsManualModeActive = false — no banner in DOM
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_ManualModeInactive_NoBannerInDom()
    {
        var mock = BuildMock(false);
        var cut = Render(mock);

        cut.FindAll(".manual-mode-banner").Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-12  Render with IsManualModeActive = true — banner present with text
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_ManualModeActive_BannerPresentWithCorrectText()
    {
        var mock = BuildMock(true);
        var cut = Render(mock);

        var banner = cut.Find(".manual-mode-banner");
        banner.TextContent.Should().Contain("automation paused by local operator");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-13  ManualModeChanged fires true after inactive render — banner appears
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ManualModeChanged_TrueAfterInactiveRender_BannerAppearsWithoutRemount()
    {
        var mock = BuildMock(false);
        var cut = Render(mock);

        cut.FindAll(".manual-mode-banner").Should().BeEmpty();

        mock.Raise(s => s.ManualModeChanged += null, this, true);

        cut.WaitForAssertion(() =>
            cut.Find(".manual-mode-banner").TextContent.Should().Contain("automation paused by local operator"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-14  ManualModeChanged fires false after active render — banner disappears
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ManualModeChanged_FalseAfterActiveRender_BannerDisappears()
    {
        var mock = BuildMock(true);
        var cut = Render(mock);

        cut.Find(".manual-mode-banner");

        mock.Raise(s => s.ManualModeChanged += null, this, false);

        cut.WaitForAssertion(() =>
            cut.FindAll(".manual-mode-banner").Should().BeEmpty());
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-15  Late-join scenario — banner visible on initial render without hub
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_LateJoin_ManualModeActive_BannerVisibleOnInitialRenderWithoutHubBroadcast()
    {
        // Arrange: service already active before component mounts.
        // No ManualModeChanged event is raised — the component must snapshot directly.
        var mock = BuildMock(true);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        // Act: mount fresh component
        var cut = ctx.Render<ManualModeBanner>();

        // Assert: visible immediately on first render without any broadcast
        cut.Find(".manual-mode-banner").Should().NotBeNull();
        cut.Find(".manual-mode-banner").TextContent.Should().Contain("automation paused by local operator");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-15a  Post-disposal ManualModeChanged — no StateHasChanged, no exception
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_ThenManualModeChanged_NoExceptionThrown()
    {
        var mock = BuildMock(false);
        var cut = Render(mock);

        // Dispose the component first
        cut.Instance.Dispose();

        // Fire the event after disposal — must not throw
        var act = () => mock.Raise(s => s.ManualModeChanged += null, this, true);
        act.Should().NotThrow();
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-11  Dispose unsubscribes from ManualModeChanged
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromManualModeChanged()
    {
        var mock = BuildMock(false);
        var cut = Render(mock);

        cut.Instance.Dispose();

        mock.VerifyRemove(
            m => m.ManualModeChanged -= It.IsAny<EventHandler<bool>>(),
            Times.Once);
    }
}
