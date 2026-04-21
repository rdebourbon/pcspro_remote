using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Shared;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public sealed class OperationStatusBannerTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static Mock<IOperationCoordinatorService> BuildMock(
        bool isInProgress,
        string? description = null)
    {
        var mock = new Mock<IOperationCoordinatorService>();
        mock.Setup(s => s.IsOperationInProgress).Returns(isInProgress);
        mock.Setup(s => s.CurrentOperationDescription).Returns(description);
        return mock;
    }

    private static IRenderedComponent<OperationStatusBanner> Render(
        Mock<IOperationCoordinatorService> mock)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);
        return ctx.Render<OperationStatusBanner>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // S005-TC-12  In progress with description — banner visible
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OperationInProgress_WithDescription_BannerShowsDescription()
    {
        var mock = BuildMock(true, "Launching\u2026");
        var cut = Render(mock);

        var banner = cut.Find(".operation-status-banner");
        banner.TextContent.Should().Contain("Launching\u2026");
    }

    // ──────────────────────────────────────────────────────────────────────
    // S005-TC-13  No operation — banner empty
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void NoOperation_BannerRendersEmpty()
    {
        var mock = BuildMock(false);
        var cut = Render(mock);

        cut.FindAll(".operation-status-banner").Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // S005-TC-14  Operation complete — banner disappears
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OperationComplete_BannerDisappears()
    {
        var mock = BuildMock(true, "Working\u2026");
        var cut = Render(mock);

        cut.Find(".operation-status-banner").Should().NotBeNull();

        // Simulate operation completing.
        mock.Setup(s => s.IsOperationInProgress).Returns(false);
        mock.Setup(s => s.CurrentOperationDescription).Returns((string?)null);
        mock.Raise(s => s.OperationInProgressChanged += null, mock.Object, false);

        cut.WaitForAssertion(() =>
            cut.FindAll(".operation-status-banner").Should().BeEmpty());
    }

    // ──────────────────────────────────────────────────────────────────────
    // S005-TC-15  Dispose unsubscribes from both events
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesEvents()
    {
        var mock = BuildMock(false);
        var cut = Render(mock);

        cut.Instance.Dispose();

        mock.VerifyRemove(
            m => m.OperationInProgressChanged -= It.IsAny<EventHandler<bool>>(),
            Times.Once);
        mock.VerifyRemove(
            m => m.OperationDescriptionChanged -= It.IsAny<EventHandler<string?>>(),
            Times.Once);
    }

    // ──────────────────────────────────────────────────────────────────────
    // S005-TC-16  Late mount — sees active operation immediately
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void LateMount_SeesActiveOperation()
    {
        // Service already has an active operation before component mounts.
        var mock = BuildMock(true, "Refreshing scoreboard\u2026");

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<OperationStatusBanner>();

        cut.Find(".operation-status-banner").TextContent
            .Should().Contain("Refreshing scoreboard\u2026");
    }

    // ──────────────────────────────────────────────────────────────────────
    // S005-TC-17  Null description while in progress — banner empty
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void NullDescription_InProgress_BannerRendersEmpty()
    {
        var mock = BuildMock(true, null);
        var cut = Render(mock);

        cut.FindAll(".operation-status-banner").Should().BeEmpty();
    }
}
