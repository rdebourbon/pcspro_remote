using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;
using PcsRemote.Web.Shared;
using Radzen;
using IndexPage = PcsRemote.Web.Pages.Index;

namespace PcsRemote.Web.Tests.Pages;

[TestClass]
public class IndexTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // AC-6: Fetch in-flight → match-loading shown, no cards
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnInit_FetchInFlight_ShowsLoadingIndicator()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<MatchInfo>>();
        var mock = BuildMock(PcsProState.MatchSelection);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>())).Returns(tcs.Task);

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        cut.WaitForAssertion(() => cut.Find(".match-loading"));
        cut.FindAll(".match-card").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-7: StateChanged(MatchSelectionSearching) → match-loading, no cards
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionSearching_ShowsLoadingIndicator()
    {
        var mock = BuildMock(PcsProState.NotRunning);

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionSearching);

        cut.WaitForAssertion(() => cut.Find(".match-loading"));
        cut.FindAll(".match-card").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-8: MatchSelectionReady + 2 matches → 2 interactive MatchCard components
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionReadyWithTwoMatches_RendersInteractiveCards()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);

        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));
        cut.FindAll(".match-card--disabled").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-9: MatchSelectionReady + 0 matches → match-empty-state, no cards
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionReadyWithNoMatches_ShowsEmptyState()
    {
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo>());

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);

        cut.WaitForAssertion(() => cut.Find(".match-empty-state"));
        cut.FindAll(".match-card").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-10: Auto-select (a) — single match + StateChanged(MatchSelectionReady)
    //        → LoadMatchAsync called with that match
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionReadyWithSingleMatch_AutoSelectsMatch()
    {
        var singleMatch = TestMatch(1);
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { singleMatch });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);

        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(
                It.Is<MatchInfo>(m => m == singleMatch),
                It.IsAny<CancellationToken>()),
                Times.Once));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-11: Auto-select (b) — mount in MatchSelectionReady + single match
    //        → LoadMatchAsync called via FetchMatchesAsync completion path
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnInit_MountedInMatchSelectionReadyWithSingleMatch_AutoSelectsMatch()
    {
        var singleMatch = TestMatch(1);
        var mock = BuildMock(PcsProState.MatchSelectionReady);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { singleMatch });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        // bUnit's WaitForAssertion polls until the async auto-select path (b) fires
        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(
                It.Is<MatchInfo>(m => m == singleMatch),
                It.IsAny<CancellationToken>()),
                Times.Once));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-12: MatchSelectionReady fires twice — LoadMatchAsync called exactly once
    //        The null guard (_matches = null before LoadMatchAsync) prevents re-trigger
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionReadyTwice_LoadMatchCalledOnce()
    {
        var singleMatch = TestMatch(1);
        var loadTcs = new TaskCompletionSource();  // Keeps LoadMatchAsync pending
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { singleMatch });
        mock.Setup(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()))
            .Returns(loadTcs.Task);

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);

        // Wait for fetch to populate _matches
        cut.WaitForAssertion(() =>
            mock.Verify(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()), Times.Once));

        // First MatchSelectionReady → auto-select fires, _matches set to null
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);

        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));

        // Second MatchSelectionReady before LoadMatchAsync completes → null guard prevents re-trigger
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);

        mock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-13: Clicking a card calls LoadMatchAsync with the correct MatchInfo
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CardClick_CallsLoadMatchAsyncWithCorrectMatch()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);

        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));

        cut.FindAll(".match-card")[0].Click();

        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(
                It.Is<MatchInfo>(m => m == match1),
                It.IsAny<CancellationToken>()),
                Times.Once));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-14: MatchSelection re-entry while new fetch is pending (TCS) →
    //        loading indicator shown, no stale cards
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionReEntry_ClearsStaleCardsAndShowsLoading()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var tcs = new TaskCompletionSource<IReadOnlyList<MatchInfo>>();
        var mock = BuildMock(PcsProState.NotRunning);
        mock.SetupSequence(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 })
            .Returns(tcs.Task);  // Second call never completes

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        // First entry → fetch → 2 matches → cards shown
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);
        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));

        // Re-entry → second fetch (TCS, pending)
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".match-loading");
            cut.FindAll(".match-card").Should().BeEmpty();
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-15: Mount in MatchSelectionSearching → loading shown, no fetch triggered
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnInit_MountedInMatchSelectionSearching_ShowsLoadingWithNoFetch()
    {
        var mock = BuildMock(PcsProState.MatchSelectionSearching);

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        cut.Find(".match-loading");
        cut.FindAll(".match-card").Should().BeEmpty();
        mock.Verify(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-4 invariant: StateChanged(MatchSelectionReady) alone NEVER triggers a fetch
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionReadyAlone_DoesNotTriggerFetch()
    {
        var mock = BuildMock(PcsProState.NotRunning);

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);

        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().BeEmpty());
        mock.Verify(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-16: Dispose unsubscribes from StateChanged
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromStateChanged()
    {
        var mock = BuildMock(PcsProState.NotRunning);

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        // cut.Instance.Dispose() calls IDisposable.Dispose() synchronously so
        // VerifyRemove can be asserted immediately (same pattern as SPEC-S-004 AC-11).
        cut.Instance.Dispose();

        mock.VerifyRemove(
            s => s.StateChanged -= It.IsAny<EventHandler<PcsProState>>(),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-1 (S-007): MatchLoaded state with _loadedMatch set → renders team names;
    //               GetTeamNamesAsync never called (R-2)
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MatchLoaded_WithLoadedMatch_RendersTeamNames()
    {
        var match = new MatchInfo("1", "Home XI", "Away XI", "League", new DateOnly(2026, 6, 20));
        var mock = BuildMock(PcsProState.MatchSelectionReady);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        // Path-b auto-select fires (mount in MatchSelectionReady + 1 match)
        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));

        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".match-loaded__home").TextContent.Should().Be("Home XI");
            cut.Find(".match-loaded__away").TextContent.Should().Be("Away XI");
        });
        mock.Verify(s => s.GetTeamNamesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-2 (S-007): Non-MatchLoaded state → no .match-loaded element in DOM
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void NonMatchLoadedState_DoesNotRenderMatchLoaded()
    {
        var mock = BuildMock(PcsProState.NotRunning);

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        cut.FindAll(".match-loaded").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-3 (S-007): Card click → _loadedMatch stored → MatchLoaded → team names
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CardClick_ThenMatchLoaded_RendersTeamNames()
    {
        var match1 = new MatchInfo("1", "Alpha CC", "Beta CC", "League", new DateOnly(2026, 6, 20));
        var match2 = TestMatch(2);
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);

        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));
        cut.FindAll(".match-card")[0].Click();
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".match-loaded__home").TextContent.Should().Be("Alpha CC");
            cut.Find(".match-loaded__away").TextContent.Should().Be("Beta CC");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-4 (S-007): Auto-select path (a) — state arrives after fetch
    //               → _loadedMatch stored → MatchLoaded → team names
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AutoSelectPathA_ThenMatchLoaded_RendersTeamNames()
    {
        var match = new MatchInfo("1", "Auto Home", "Auto Away", "League", new DateOnly(2026, 6, 20));
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        // Drive fetch first, then arrive at MatchSelectionReady (path-a)
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);
        cut.WaitForAssertion(() =>
            mock.Verify(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()), Times.Once));

        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);
        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));

        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".match-loaded__home").TextContent.Should().Be("Auto Home");
            cut.Find(".match-loaded__away").TextContent.Should().Be("Auto Away");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-5 (S-007): Auto-select path (b) — fetch completes while state is already
    //               MatchSelectionReady → _loadedMatch stored → MatchLoaded → team names
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AutoSelectPathB_ThenMatchLoaded_RendersTeamNames()
    {
        var match = new MatchInfo("1", "FetchPath Home", "FetchPath Away", "League", new DateOnly(2026, 6, 20));
        var mock = BuildMock(PcsProState.MatchSelectionReady);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        using var ctx = BuildCtx(mock);

        // On mount: OnInitializedAsync reads MatchSelectionReady → calls FetchMatchesAsync
        // FetchMatchesAsync completes with 1 match while _currentState == MatchSelectionReady → path-b fires
        var cut = ctx.Render<IndexPage>();
        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));

        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".match-loaded__home").TextContent.Should().Be("FetchPath Home");
            cut.Find(".match-loaded__away").TextContent.Should().Be("FetchPath Away");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-9 (S-003): Index.razor embeds ScoreboardPreview and preserves team names
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MatchLoaded_ScoreboardPreviewEmbedded_TeamNamesPreserved()
    {
        var match = new MatchInfo("1", "Riverside CC", "Westwood CC", "League", new DateOnly(2026, 6, 20));
        var autoMock = BuildMock(PcsProState.MatchSelectionReady);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns((byte[]?)null);

        using var ctx = BuildCtx(autoMock, scoreMock);
        var cut = ctx.Render<IndexPage>();

        // Path-b auto-select fires; drive to MatchLoaded
        cut.WaitForAssertion(() =>
            autoMock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));
        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            // Team names are preserved
            cut.Find(".match-loaded__home").TextContent.Should().Be("Riverside CC");
            cut.Find(".match-loaded__away").TextContent.Should().Be("Westwood CC");
            // ScoreboardPreview is embedded (placeholder because no image cached)
            cut.Find(".scoreboard-placeholder");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-10 (S-004): Index.razor embeds RefreshScoreboardButton in MatchLoaded section
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MatchLoaded_RefreshButtonPresent_ExistingElementsUnaffected()
    {
        var match = new MatchInfo("1", "Riverside CC", "Westwood CC", "League", new DateOnly(2026, 6, 20));
        var autoMock = BuildMock(PcsProState.MatchSelectionReady);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns((byte[]?)null);

        using var ctx = BuildCtx(autoMock, scoreMock);
        var cut = ctx.Render<IndexPage>();

        cut.WaitForAssertion(() =>
            autoMock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));
        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            // S-004 AC-10: refresh button present
            cut.Find(".refresh-scoreboard-button");
            // S-003 AC-10 unchanged: team names and placeholder unaffected
            cut.Find(".match-loaded__home").TextContent.Should().Be("Riverside CC");
            cut.Find(".match-loaded__away").TextContent.Should().Be("Westwood CC");
            cut.Find(".scoreboard-placeholder");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-10 (S-005): Index.razor embeds ChangeMatchButton in MatchLoaded section
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MatchLoaded_ChangeMatchButtonPresent_AllMatchLoadedElementsPresent()
    {
        var match = new MatchInfo("1", "Riverside CC", "Westwood CC", "League", new DateOnly(2026, 6, 20));
        var autoMock = BuildMock(PcsProState.MatchSelectionReady);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns((byte[]?)null);

        using var ctx = BuildCtx(autoMock, scoreMock);
        var cut = ctx.Render<IndexPage>();

        cut.WaitForAssertion(() =>
            autoMock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));
        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            // S-005 AC-10: change match button present
            cut.Find(".change-match-button");
            // S-004 AC-10 unchanged: refresh button present
            cut.Find(".refresh-scoreboard-button");
            // S-003 AC-10 unchanged: team names and placeholder unaffected
            cut.Find(".match-loaded__home").TextContent.Should().Be("Riverside CC");
            cut.Find(".match-loaded__away").TextContent.Should().Be("Westwood CC");
            cut.Find(".scoreboard-placeholder");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-21 (S-003): MatchCards rendered with IsInteractive=false when manual mode active
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ManualModeActive_MatchCardsRendered_WithDisabledInteractivity()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var autoMock = BuildMock(PcsProState.NotRunning);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        var manualModeMock = new Mock<IManualModeService>();
        manualModeMock.Setup(s => s.IsManualModeActive).Returns(true);

        using var ctx = BuildCtx(autoMock, manualModeMock: manualModeMock);
        var cut = ctx.Render<IndexPage>();

        mock_drive_to_ready(autoMock);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".match-card").Should().HaveCount(2);
            // All cards rendered in disabled state (IsInteractive=false → match-card--disabled CSS class)
            cut.FindAll(".match-card--disabled").Should().HaveCount(2);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-22 (S-003): SelectMatchAsync guard blocks LoadMatchAsync when manual mode active.
    //                Simulates the race window: service returns true but the ManualModeChanged
    //                event has not yet propagated to update the component's _manualModeActive
    //                field (so the card remains interactive and can still be clicked).
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ManualModeActive_SelectMatch_RejectedWithNotification()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var autoMock = BuildMock(PcsProState.NotRunning);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        var manualModeMock = new Mock<IManualModeService>();
        manualModeMock.Setup(s => s.IsManualModeActive).Returns(false);

        var notificationSvc = new NotificationService();
        var notifications = new List<NotificationMessage>();
        notificationSvc.Messages.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (NotificationMessage msg in e.NewItems)
                    notifications.Add(msg);
        };

        using var ctx = BuildCtx(autoMock, manualModeMock: manualModeMock, notificationService: notificationSvc);
        var cut = ctx.Render<IndexPage>();

        mock_drive_to_ready(autoMock);
        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));

        // Simulate the race: service reports active but event has not yet propagated
        // (so _manualModeActive is still false and the cards remain interactive).
        manualModeMock.Setup(s => s.IsManualModeActive).Returns(true);

        cut.FindAll(".match-card")[0].Click();

        cut.WaitForAssertion(() =>
        {
            notifications.Should().ContainSingle(n =>
                n.Summary == "Automation is paused — disable manual mode before issuing commands" &&
                n.Severity == NotificationSeverity.Warning);
            autoMock.Verify(
                s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()),
                Times.Never);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-23 (S-003): SelectMatchAsync proceeds normally when manual mode inactive
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ManualModeInactive_SelectMatch_ProceedsNormally()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var autoMock = BuildMock(PcsProState.NotRunning);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        using var ctx = BuildCtx(autoMock);
        var cut = ctx.Render<IndexPage>();

        mock_drive_to_ready(autoMock);
        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));

        cut.FindAll(".match-card")[0].Click();

        cut.WaitForAssertion(() =>
            autoMock.Verify(
                s => s.LoadMatchAsync(It.Is<MatchInfo>(m => m == match1), It.IsAny<CancellationToken>()),
                Times.Once));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static BunitContext BuildCtx(
        Mock<IPcsProAutomationService> autoMock,
        Mock<IScoreboardService>? scoreMock = null,
        Mock<IManualModeService>? manualModeMock = null,
        NotificationService? notificationService = null)
    {
        var mmMock = manualModeMock ?? new Mock<IManualModeService>();
        if (manualModeMock is null)
            mmMock.Setup(s => s.IsManualModeActive).Returns(false);

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(autoMock.Object);
        ctx.Services.AddSingleton((scoreMock ?? new Mock<IScoreboardService>()).Object);
        ctx.Services.AddSingleton(mmMock.Object);
        ctx.Services.AddSingleton(notificationService ?? new NotificationService());
        ctx.Services.AddSingleton<ILogger<RefreshScoreboardButton>>(
            NullLogger<RefreshScoreboardButton>.Instance);
        ctx.Services.AddSingleton(new Mock<IConfirmDialogService>().Object);
        ctx.Services.AddSingleton<ILogger<ChangeMatchButton>>(
            NullLogger<ChangeMatchButton>.Instance);
        return ctx;
    }

    private static void mock_drive_to_ready(Mock<IPcsProAutomationService> autoMock)
    {
        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);
        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchSelectionReady);
    }

    private static Mock<IPcsProAutomationService> BuildMock(PcsProState initialState)
    {
        var mock = new Mock<IPcsProAutomationService>();
        mock.Setup(s => s.CurrentState).Returns(initialState);
        // Default LoadMatchAsync completes immediately; tests requiring pending can override.
        mock.Setup(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static MatchInfo TestMatch(int id) =>
        new(id.ToString(), $"Home {id}", $"Away {id}", "League", new DateOnly(2026, 6, 20));
}

