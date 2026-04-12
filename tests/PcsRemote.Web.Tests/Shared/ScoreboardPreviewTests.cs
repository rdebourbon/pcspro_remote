using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Shared;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public class ScoreboardPreviewTests
{
    private static readonly byte[] SampleImage = [0xFF, 0xD8, 0xFF, 0x01, 0x02]; // minimal JPEG-like bytes
    private static readonly byte[] UpdatedImage = [0xFF, 0xD8, 0xFF, 0x03, 0x04];

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (
        IRenderedComponent<ScoreboardPreview> Cut,
        Mock<IScoreboardService> ScoreMock,
        Mock<IPcsProAutomationService> AutoMock,
        BunitContext Ctx)
    Build(PcsProState initialState, byte[]? currentImage)
    {
        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns(currentImage);

        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(initialState);

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(scoreMock.Object);
        ctx.Services.AddSingleton(autoMock.Object);

        var cut = ctx.Render<ScoreboardPreview>();
        return (cut, scoreMock, autoMock, ctx);
    }

    // ── TC-1: Placeholder for all non-MatchLoaded states (DataTestMethod) ──────

    [TestMethod]
    [DataRow(PcsProState.NotRunning)]
    [DataRow(PcsProState.Launching)]
    [DataRow(PcsProState.LoginScreen)]
    [DataRow(PcsProState.MatchSelection)]
    [DataRow(PcsProState.MatchSelectionSearching)]
    [DataRow(PcsProState.MatchSelectionReady)]
    [DataRow(PcsProState.Error)]
    public void NonMatchLoadedState_RendersPlaceholder_NoImage(PcsProState state)
    {
        var (cut, _, _, ctx) = Build(state, SampleImage);
        using (ctx)
        {
            cut.Find(".scoreboard-placeholder").TextContent.Should().Be("No scoreboard data");
            cut.FindAll(".scoreboard-image").Should().BeEmpty();
        }
    }

    // ── TC-2: MatchLoaded + no cached image → placeholder ────────────────────

    [TestMethod]
    public void MatchLoaded_NoImage_RendersPlaceholder()
    {
        var (cut, _, _, ctx) = Build(PcsProState.MatchLoaded, null);
        using (ctx)
        {
            cut.Find(".scoreboard-placeholder").TextContent.Should().Be("No scoreboard data");
            cut.FindAll(".scoreboard-image").Should().BeEmpty();
        }
    }

    // ── TC-3: MatchLoaded + cached image → JPEG data URI ─────────────────────

    [TestMethod]
    public void MatchLoaded_WithImage_RendersJpegDataUri()
    {
        var (cut, _, _, ctx) = Build(PcsProState.MatchLoaded, SampleImage);
        using (ctx)
        {
            cut.FindAll(".scoreboard-placeholder").Should().BeEmpty();
            var img = cut.Find(".scoreboard-image");
            var src = img.GetAttribute("src")!;
            src.Should().StartWith("data:image/jpeg;base64,",
                "the component must emit a JPEG data URI");
            var b64 = src["data:image/jpeg;base64,".Length..];
            Convert.FromBase64String(b64).Should().Equal(SampleImage);
        }
    }

    // ── TC-4: ScoreboardUpdated event refreshes the image ────────────────────

    [TestMethod]
    public void ScoreboardUpdated_EventFired_ImageUpdates()
    {
        var (cut, scoreMock, _, ctx) = Build(PcsProState.MatchLoaded, SampleImage);
        using (ctx)
        {
            // Confirm initial image is present
            cut.Find(".scoreboard-image");

            scoreMock.Raise(s => s.ScoreboardUpdated += null, scoreMock.Object, UpdatedImage);

            cut.WaitForAssertion(() =>
            {
                var src = cut.Find(".scoreboard-image").GetAttribute("src")!;
                var b64 = src["data:image/jpeg;base64,".Length..];
                Convert.FromBase64String(b64).Should().Equal(UpdatedImage);
            });
        }
    }

    // ── TC-5: StateChanged away from MatchLoaded → placeholder ───────────────

    [TestMethod]
    public void StateChanged_AwayFromMatchLoaded_ShowsPlaceholder()
    {
        var (cut, _, autoMock, ctx) = Build(PcsProState.MatchLoaded, SampleImage);
        using (ctx)
        {
            cut.Find(".scoreboard-image"); // sanity: image present

            autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);

            cut.WaitForAssertion(() =>
            {
                cut.Find(".scoreboard-placeholder");
                cut.FindAll(".scoreboard-image").Should().BeEmpty();
            });
        }
    }

    // ── TC-6: StateChanged into MatchLoaded with cached image → image ─────────

    [TestMethod]
    public void StateChanged_IntoMatchLoaded_WithCachedImage_ShowsImage()
    {
        var (cut, _, autoMock, ctx) = Build(PcsProState.NotRunning, SampleImage);
        using (ctx)
        {
            cut.Find(".scoreboard-placeholder"); // sanity: placeholder while NotRunning

            autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

            cut.WaitForAssertion(() =>
            {
                cut.Find(".scoreboard-image");
                cut.FindAll(".scoreboard-placeholder").Should().BeEmpty();
            });
        }
    }

    // ── TC-7: Late-joiner reads cached image on init ──────────────────────────

    [TestMethod]
    public void OnInit_MatchLoadedWithCachedImage_RendersImmediately()
    {
        var (cut, _, _, ctx) = Build(PcsProState.MatchLoaded, SampleImage);
        using (ctx)
        {
            // No event needed — image must be present from OnInitializedAsync
            cut.Find(".scoreboard-image");
            cut.FindAll(".scoreboard-placeholder").Should().BeEmpty();
        }
    }

    // ── TC-8: Disposal unsubscribes from both events ──────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromBothEvents()
    {
        var (cut, scoreMock, autoMock, ctx) = Build(PcsProState.NotRunning, null);
        using (ctx)
        {
            // cut.Instance.Dispose() calls IDisposable.Dispose() synchronously so
            // VerifyRemove can be asserted immediately — established project pattern
            // (bUnit's cut.Dispose() defers teardown and races the VerifyRemove assertion).
            cut.Instance.Dispose();

            scoreMock.VerifyRemove(
                s => s.ScoreboardUpdated -= It.IsAny<EventHandler<byte[]>>(),
                Times.Once);
            autoMock.VerifyRemove(
                a => a.StateChanged -= It.IsAny<EventHandler<PcsProState>>(),
                Times.Once);
        }
    }

    // ── TC-10: StateChanged into MatchLoaded with null cached image → placeholder

    [TestMethod]
    public void StateChanged_IntoMatchLoaded_NullImage_ShowsPlaceholder()
    {
        var (cut, _, autoMock, ctx) = Build(PcsProState.NotRunning, null);
        using (ctx)
        {
            autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

            cut.WaitForAssertion(() =>
            {
                cut.Find(".scoreboard-placeholder");
                cut.FindAll(".scoreboard-image").Should().BeEmpty();
            });
        }
    }
}
