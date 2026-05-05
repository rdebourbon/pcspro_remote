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

        // Click "Load Matches" to trigger fetch (no longer auto-fetched on mount)
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();

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
    // AC-8: MatchSelection + 2 matches → 2 interactive MatchCard components
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionWithTwoMatches_RendersInteractiveCards()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock_drive_to_match_selection(mock, cut);

        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));
        cut.FindAll(".match-card--disabled").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-9: MatchSelection + 0 matches → match-empty-state, no cards
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionWithNoMatches_ShowsEmptyState()
    {
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo>());

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock_drive_to_match_selection(mock, cut);

        cut.WaitForAssertion(() => cut.Find(".match-empty-state"));
        cut.FindAll(".match-card").Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-10: Auto-select (a) — single match + StateChanged(MatchSelection)
    //        → FetchMatchesAsync auto-selects, LoadMatchAsync called with that match
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_MatchSelectionWithSingleMatch_AutoSelectsMatch()
    {
        var singleMatch = TestMatch(1);
        var mock = BuildMock(PcsProState.NotRunning);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { singleMatch });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();
        mock_drive_to_match_selection(mock, cut);

        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(
                It.Is<MatchInfo>(m => m == singleMatch),
                It.IsAny<CancellationToken>()),
                Times.Once));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-11: Auto-select (b) — mount in MatchSelection + single match
    //        → LoadMatchAsync called via FetchMatchesAsync completion path
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OnInit_MountedInMatchSelectionWithSingleMatch_AutoSelectsMatch()
    {
        var singleMatch = TestMatch(1);
        var mock = BuildMock(PcsProState.MatchSelection);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { singleMatch });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        // Click "Load Matches" to trigger fetch (no longer auto-fetched on mount)
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();

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
        mock_drive_to_match_selection(mock, cut);

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
        mock_drive_to_match_selection(mock, cut);

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

        // First entry → click button → fetch → 2 matches → cards shown
        mock_drive_to_match_selection(mock, cut);
        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));

        // Re-entry → click button → second fetch (TCS, pending)
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelection);
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();

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
        var mock = BuildMock(PcsProState.MatchSelection);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        // Click "Load Matches" to trigger fetch → auto-select (single match)
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();

        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));

        mock.Setup(s => s.LoadedMatch).Returns(match);
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
        mock_drive_to_match_selection(mock, cut);

        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));
        cut.FindAll(".match-card")[0].Click();
        mock.Setup(s => s.LoadedMatch).Returns(match1);
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
        // Drive to MatchSelection and click "Load Matches" → fetch → auto-select (path-a)
        mock_drive_to_match_selection(mock, cut);
        cut.WaitForAssertion(() =>
            mock.Verify(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()), Times.Once));

        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchSelectionReady);
        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));

        mock.Setup(s => s.LoadedMatch).Returns(match);
        mock.Raise(s => s.StateChanged += null, mock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".match-loaded__home").TextContent.Should().Be("Auto Home");
            cut.Find(".match-loaded__away").TextContent.Should().Be("Auto Away");
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-5 (S-007): Auto-select path (b) — mount in MatchSelection, fetch
    //               completes with single match → auto-select fires
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void AutoSelectPathB_ThenMatchLoaded_RendersTeamNames()
    {
        var match = new MatchInfo("1", "FetchPath Home", "FetchPath Away", "League", new DateOnly(2026, 6, 20));
        var mock = BuildMock(PcsProState.MatchSelection);
        mock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        using var ctx = BuildCtx(mock);

        var cut = ctx.Render<IndexPage>();

        // Click "Load Matches" to trigger fetch → auto-select (single match)
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();

        cut.WaitForAssertion(() =>
            mock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));

        mock.Setup(s => s.LoadedMatch).Returns(match);
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
        var autoMock = BuildMock(PcsProState.MatchSelection);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns((byte[]?)null);

        using var ctx = BuildCtx(autoMock, scoreMock);
        var cut = ctx.Render<IndexPage>();

        // Click "Load Matches" → auto-select fires (single match); drive to MatchLoaded
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();
        cut.WaitForAssertion(() =>
            autoMock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));
        autoMock.Setup(s => s.LoadedMatch).Returns(match);
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
        var autoMock = BuildMock(PcsProState.MatchSelection);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns((byte[]?)null);

        using var ctx = BuildCtx(autoMock, scoreMock);
        var cut = ctx.Render<IndexPage>();

        // Click "Load Matches" → auto-select fires (single match); drive to MatchLoaded
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();
        cut.WaitForAssertion(() =>
            autoMock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));
        autoMock.Setup(s => s.LoadedMatch).Returns(match);
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
        var autoMock = BuildMock(PcsProState.MatchSelection);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match });

        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns((byte[]?)null);

        using var ctx = BuildCtx(autoMock, scoreMock);
        var cut = ctx.Render<IndexPage>();

        // Click "Load Matches" → auto-select fires (single match); drive to MatchLoaded
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();
        cut.WaitForAssertion(() =>
            autoMock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Once));
        autoMock.Setup(s => s.LoadedMatch).Returns(match);
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

        mock_drive_to_match_selection(autoMock, cut);

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

        mock_drive_to_match_selection(autoMock, cut);
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

        mock_drive_to_match_selection(autoMock, cut);
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
        Mock<IOperationCoordinatorService>? coordinatorMock = null,
        NotificationService? notificationService = null,
        Mock<IDateSelectionService>? dateSelectionMock = null)
    {
        var mmMock = manualModeMock ?? new Mock<IManualModeService>();
        if (manualModeMock is null)
            mmMock.Setup(s => s.IsManualModeActive).Returns(false);

        var coordMock = coordinatorMock ?? new Mock<IOperationCoordinatorService>();
        if (coordinatorMock is null)
            coordMock.Setup(s => s.IsOperationInProgress).Returns(false);

        var dsMock = dateSelectionMock ?? new Mock<IDateSelectionService>();
        if (dateSelectionMock is null)
            dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(autoMock.Object);
        ctx.Services.AddSingleton((scoreMock ?? new Mock<IScoreboardService>()).Object);
        ctx.Services.AddSingleton(mmMock.Object);
        ctx.Services.AddSingleton(coordMock.Object);
        ctx.Services.AddSingleton(dsMock.Object);
        ctx.Services.AddSingleton(notificationService ?? new NotificationService());
        ctx.Services.AddSingleton<ILogger<RefreshScoreboardButton>>(
            NullLogger<RefreshScoreboardButton>.Instance);
        ctx.Services.AddSingleton(new Mock<IConfirmDialogService>().Object);
        ctx.Services.AddSingleton<ILogger<ChangeMatchButton>>(
            NullLogger<ChangeMatchButton>.Instance);
        ctx.Services.AddSingleton(new Mock<IYouTubeLiveStreamService>().Object);
        ctx.Services.AddSingleton<ILogger<IndexPage>>(NullLogger<IndexPage>.Instance);
        return ctx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-21: Match cards rendered with match-card--disabled when coordinator in-progress
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OperationInProgress_MatchCardsRendered_WithDisabledInteractivity()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var autoMock = BuildMock(PcsProState.NotRunning);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(s => s.IsOperationInProgress).Returns(true);

        using var ctx = BuildCtx(autoMock, coordinatorMock: coordinatorMock);
        var cut = ctx.Render<IndexPage>();

        mock_drive_to_match_selection(autoMock, cut);

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".match-card").Should().HaveCount(2);
            cut.FindAll(".match-card--disabled").Should().HaveCount(2);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-22: Tooltip "Automation in progress…" on disabled match cards when in-progress
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OperationInProgress_MatchCards_HaveTooltipAttribute()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var autoMock = BuildMock(PcsProState.NotRunning);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(s => s.IsOperationInProgress).Returns(true);

        using var ctx = BuildCtx(autoMock, coordinatorMock: coordinatorMock);
        var cut = ctx.Render<IndexPage>();

        mock_drive_to_match_selection(autoMock, cut);

        cut.WaitForAssertion(() =>
            cut.Find(".match-card--disabled").GetAttribute("title")
                .Should().Be("Automation in progress\u2026"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-23: SelectMatchAsync returns without calling LoadMatchAsync when in-progress
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void OperationInProgress_SelectMatch_RejectedSilently()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var autoMock = BuildMock(PcsProState.NotRunning);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(s => s.IsOperationInProgress).Returns(false);

        using var ctx = BuildCtx(autoMock, coordinatorMock: coordinatorMock);
        var cut = ctx.Render<IndexPage>();

        mock_drive_to_match_selection(autoMock, cut);
        cut.WaitForAssertion(() => cut.FindAll(".match-card").Should().HaveCount(2));

        // Coordinator fires in-progress — component field updates but re-render may not have hidden the cards yet.
        // The guard in SelectMatchAsync is the defense-in-depth for this TOCTOU window.
        coordinatorMock.Raise(c => c.OperationInProgressChanged += null, coordinatorMock.Object, true);

        // Cards become non-interactive once the component's _operationInProgress field is updated.
        cut.WaitForAssertion(() => cut.FindAll(".match-card--disabled").Should().HaveCount(2));

        // bUnit cannot dispatch click events on non-interactive elements, so the guard in
        // SelectMatchAsync is not directly exercisable via UI simulation here. The key invariant
        // verified by this test is that the coordinator event causes the cards to become disabled —
        // LoadMatchAsync is never called because no interactive card click can occur.
        autoMock.Verify(
            s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-24: Match cards re-enable when coordinator fires in-progress = false
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CoordinatorFiresComplete_MatchCardsReEnable()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var autoMock = BuildMock(PcsProState.NotRunning);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(s => s.IsOperationInProgress).Returns(true);

        using var ctx = BuildCtx(autoMock, coordinatorMock: coordinatorMock);
        var cut = ctx.Render<IndexPage>();

        mock_drive_to_match_selection(autoMock, cut);
        cut.WaitForAssertion(() =>
            cut.FindAll(".match-card--disabled").Should().HaveCount(2));

        coordinatorMock.Raise(c => c.OperationInProgressChanged += null, coordinatorMock.Object, false);

        cut.WaitForAssertion(() =>
            cut.FindAll(".match-card--disabled").Should().BeEmpty());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TC-25: Late-join — index rendered when coordinator already in-progress → cards disabled
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void LateJoin_CoordinatorAlreadyInProgress_CardsRenderedDisabled()
    {
        var match1 = TestMatch(1);
        var match2 = TestMatch(2);
        var autoMock = BuildMock(PcsProState.MatchSelection);
        autoMock.Setup(s => s.GetTodaysMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { match1, match2 });

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(s => s.IsOperationInProgress).Returns(true);

        using var ctx = BuildCtx(autoMock, coordinatorMock: coordinatorMock);
        var cut = ctx.Render<IndexPage>();

        // Click "Load Matches" to trigger fetch (button disabled state handled separately)
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".match-card").Should().HaveCount(2);
            cut.FindAll(".match-card--disabled").Should().HaveCount(2,
                "late-joining client should see cards as disabled immediately");
        });
    }

    private static void mock_drive_to_match_selection(Mock<IPcsProAutomationService> autoMock, IRenderedComponent<IndexPage> cut)
    {
        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);
        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.Find(".load-matches-button").Click();
    }

    private static Mock<IPcsProAutomationService> BuildMock(
        PcsProState initialState, MatchInfo? loadedMatch = null)
    {
        var mock = new Mock<IPcsProAutomationService>();
        mock.Setup(s => s.CurrentState).Returns(initialState);
        mock.Setup(s => s.LoadedMatch).Returns(loadedMatch);
        // Default LoadMatchAsync completes immediately; tests requiring pending can override.
        mock.Setup(s => s.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static MatchInfo TestMatch(int id) =>
        new(id.ToString(), $"Home {id}", $"Away {id}", "League", new DateOnly(2026, 6, 20));

    // ══════════════════════════════════════════════════════════════════════════
    // S-005: Auto-stop on PCS Pro Error (AC-7 through AC-10)
    // ══════════════════════════════════════════════════════════════════════════

    private static (BunitContext Ctx, Mock<IPcsProAutomationService> AutoMock, Mock<IYouTubeLiveStreamService> StreamMock) BuildCtxWithStream(
        PcsProState initialState, LiveStreamStatus streamStatus, ILogger<IndexPage>? logger = null)
    {
        var autoMock = BuildMock(initialState);
        var streamMock = new Mock<IYouTubeLiveStreamService>();
        streamMock.Setup(s => s.CurrentStatus).Returns(streamStatus);

        var mmMock = new Mock<IManualModeService>();
        mmMock.Setup(s => s.IsManualModeActive).Returns(false);
        var coordMock = new Mock<IOperationCoordinatorService>();
        coordMock.Setup(s => s.IsOperationInProgress).Returns(false);
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(autoMock.Object);
        ctx.Services.AddSingleton(streamMock.Object);
        ctx.Services.AddSingleton(new Mock<IScoreboardService>().Object);
        ctx.Services.AddSingleton(mmMock.Object);
        ctx.Services.AddSingleton(coordMock.Object);
        ctx.Services.AddSingleton(dsMock.Object);
        ctx.Services.AddSingleton(new NotificationService());
        ctx.Services.AddSingleton<ILogger<RefreshScoreboardButton>>(NullLogger<RefreshScoreboardButton>.Instance);
        ctx.Services.AddSingleton(new Mock<IConfirmDialogService>().Object);
        ctx.Services.AddSingleton<ILogger<ChangeMatchButton>>(NullLogger<ChangeMatchButton>.Instance);
        ctx.Services.AddSingleton(logger ?? (ILogger<IndexPage>)NullLogger<IndexPage>.Instance);
        return (ctx, autoMock, streamMock);
    }

    // ── AC-7: PCS Pro Error + Live stream → auto-stop ─────────────────────

    [TestMethod]
    public void OnStateChanged_ErrorWithLiveStream_StopsStream()
    {
        var (ctx, autoMock, streamMock) = BuildCtxWithStream(PcsProState.MatchLoaded, LiveStreamStatus.Live);
        using (ctx)
        {
            var cut = ctx.Render<IndexPage>();

            autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.Error);

            cut.WaitForAssertion(() =>
                streamMock.Verify(s => s.StopStreamAsync(It.IsAny<CancellationToken>()), Times.Once));
        }
    }

    // ── AC-8: PCS Pro Error + Starting stream → auto-stop ─────────────────

    [TestMethod]
    public void OnStateChanged_ErrorWithStartingStream_StopsStream()
    {
        var (ctx, autoMock, streamMock) = BuildCtxWithStream(PcsProState.MatchLoaded, LiveStreamStatus.Starting);
        using (ctx)
        {
            var cut = ctx.Render<IndexPage>();

            autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.Error);

            cut.WaitForAssertion(() =>
                streamMock.Verify(s => s.StopStreamAsync(It.IsAny<CancellationToken>()), Times.Once));
        }
    }

    // ── AC-9: PCS Pro Error + non-active stream → no auto-stop ────────────

    [TestMethod]
    [DataRow(LiveStreamStatus.Idle)]
    [DataRow(LiveStreamStatus.Stopping)]
    [DataRow(LiveStreamStatus.Error)]
    public void OnStateChanged_ErrorWithNonActiveStream_NoStopStream(LiveStreamStatus streamStatus)
    {
        var (ctx, autoMock, streamMock) = BuildCtxWithStream(PcsProState.MatchLoaded, streamStatus);
        using (ctx)
        {
            var cut = ctx.Render<IndexPage>();

            autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.Error);

            cut.WaitForAssertion(() =>
                streamMock.Verify(s => s.StopStreamAsync(It.IsAny<CancellationToken>()), Times.Never));
        }
    }

    // ── AC-10: Auto-stop failure → logged as Warning, not thrown ──────────

    [TestMethod]
    public void OnStateChanged_ErrorAutoStopFails_LogsWarningNoThrow()
    {
        var loggerMock = new Mock<ILogger<IndexPage>>();
        var autoMock = BuildMock(PcsProState.MatchLoaded);
        var streamMock = new Mock<IYouTubeLiveStreamService>();
        streamMock.Setup(s => s.CurrentStatus).Returns(LiveStreamStatus.Live);
        streamMock.Setup(s => s.StopStreamAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("YouTube API failure"));

        var mmMock = new Mock<IManualModeService>();
        mmMock.Setup(s => s.IsManualModeActive).Returns(false);
        var coordMock = new Mock<IOperationCoordinatorService>();
        coordMock.Setup(s => s.IsOperationInProgress).Returns(false);

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(autoMock.Object);
        ctx.Services.AddSingleton(streamMock.Object);
        ctx.Services.AddSingleton(new Mock<IScoreboardService>().Object);
        ctx.Services.AddSingleton(mmMock.Object);
        ctx.Services.AddSingleton(coordMock.Object);
        ctx.Services.AddSingleton(new Mock<IDateSelectionService>().Object);
        ctx.Services.AddSingleton(new NotificationService());
        ctx.Services.AddSingleton<ILogger<RefreshScoreboardButton>>(NullLogger<RefreshScoreboardButton>.Instance);
        ctx.Services.AddSingleton(new Mock<IConfirmDialogService>().Object);
        ctx.Services.AddSingleton<ILogger<ChangeMatchButton>>(NullLogger<ChangeMatchButton>.Instance);
        ctx.Services.AddSingleton<ILogger<IndexPage>>(loggerMock.Object);

        using (ctx)
        {
            var cut = ctx.Render<IndexPage>();

            autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.Error);

            cut.WaitForAssertion(() =>
                loggerMock.Verify(
                    l => l.Log(
                        LogLevel.Warning,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("auto-stop")), // ToString on Moq's It.IsAnyType FormattedLogValues never returns null
                        It.IsAny<Exception>(),
                        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                    Times.Once));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // S-002: UI match hydration & ChangeMatch resilience
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public void OnInit_MountedInMatchLoadedState_HydratesLoadedMatch()
    {
        var match = TestMatch(42);
        var autoMock = BuildMock(PcsProState.MatchLoaded, match);

        using var ctx = BuildCtx(autoMock);
        var cut = ctx.Render<IndexPage>();

        // Team names rendered from hydrated LoadedMatch
        cut.WaitForAssertion(() =>
        {
            cut.Find(".match-loaded__home").TextContent.Should().Be(match.HomeTeam);
            cut.Find(".match-loaded__away").TextContent.Should().Be(match.AwayTeam);
        });
    }

    [TestMethod]
    public void OnInit_MountedInMatchLoadedState_NullLoadedMatch_ShowsFallback()
    {
        var autoMock = BuildMock(PcsProState.MatchLoaded);

        using var ctx = BuildCtx(autoMock);
        var cut = ctx.Render<IndexPage>();

        // Fallback label shown when LoadedMatch is null
        cut.WaitForAssertion(() =>
        {
            cut.Find(".match-loaded__fallback").TextContent.Should().Be("Match loaded");
            cut.FindAll(".change-match-button").Should().HaveCount(1,
                "ChangeMatch button must render even without match metadata");
        });
    }

    [TestMethod]
    public void StateChanged_MatchLoaded_HydratesLoadedMatchFromService()
    {
        var match = TestMatch(99);
        var autoMock = BuildMock(PcsProState.NotRunning);

        using var ctx = BuildCtx(autoMock);
        var cut = ctx.Render<IndexPage>();

        // Now service returns a loaded match
        autoMock.Setup(s => s.LoadedMatch).Returns(match);
        autoMock.Setup(s => s.CurrentState).Returns(PcsProState.MatchLoaded);
        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".match-loaded__home").TextContent.Should().Be(match.HomeTeam);
            cut.Find(".match-loaded__away").TextContent.Should().Be(match.AwayTeam);
        });
    }

    [TestMethod]
    public void StateChanged_MatchSelection_AfterMatchLoaded_ClearsLoadedMatch()
    {
        var match = TestMatch(7);
        var autoMock = BuildMock(PcsProState.MatchLoaded, match);

        using var ctx = BuildCtx(autoMock);
        var cut = ctx.Render<IndexPage>();

        // Verify match is shown first
        cut.WaitForAssertion(() => cut.Find(".match-loaded__home"));

        // Transition to MatchSelection
        autoMock.Setup(s => s.CurrentState).Returns(PcsProState.MatchSelection);
        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);

        // Team names and fallback should no longer be rendered
        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".match-loaded__home").Should().BeEmpty();
            cut.FindAll(".match-loaded__fallback").Should().BeEmpty();
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // S-005: Date Picker UI and Auto-Reset
    // ══════════════════════════════════════════════════════════════════════════

    // ── TC-1: Date picker hidden when toggle disabled ──────────────────────

    [TestMethod]
    public void DatePicker_ToggleDisabled_NotVisible()
    {
        var autoMock = BuildMock(PcsProState.MatchSelection);
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        using var ctx = BuildCtx(autoMock, dateSelectionMock: dsMock);
        var cut = ctx.Render<IndexPage>();

        cut.WaitForAssertion(() => cut.Find(".load-matches-button"));
        cut.FindAll(".date-selection-picker").Should().BeEmpty();
        cut.FindAll(".load-matches-date-button").Should().BeEmpty();
    }

    // ── TC-2: Date picker visible when toggle enabled + MatchSelection ─────

    [TestMethod]
    public void DatePicker_ToggleEnabled_MatchSelection_Visible()
    {
        var autoMock = BuildMock(PcsProState.MatchSelection);
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        using var ctx = BuildCtx(autoMock, dateSelectionMock: dsMock);
        var cut = ctx.Render<IndexPage>();

        // Simulate toggle enabled via event (real flow: user enables from debug panel)
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(true);
        dsMock.Raise(s => s.DateSelectionEnabledChanged += null, dsMock.Object, true);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".date-selection-picker").Should().NotBeNull();
            cut.Find(".load-matches-date-button").Should().NotBeNull();
        });
    }

    // ── TC-2b: Date picker visible when toggle enabled + MatchSelectionReady

    [TestMethod]
    public void DatePicker_ToggleEnabled_MatchSelectionReady_ViaStateChange_Visible()
    {
        var autoMock = BuildMock(PcsProState.NotRunning);
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        using var ctx = BuildCtx(autoMock, dateSelectionMock: dsMock);
        var cut = ctx.Render<IndexPage>();

        // Drive to MatchSelectionReady (auto-reset fires, but toggle is already disabled)
        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchSelectionReady);

        // Now enable the toggle while in MatchSelectionReady
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(true);
        dsMock.Raise(s => s.DateSelectionEnabledChanged += null, dsMock.Object, true);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".date-selection-picker").Should().NotBeNull();
            cut.Find(".load-matches-date-button").Should().NotBeNull();
        });
    }

    // ── TC-3: Date picker defaults to today ────────────────────────────────

    [TestMethod]
    public void DatePicker_DefaultsToToday()
    {
        var autoMock = BuildMock(PcsProState.MatchSelection);
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        using var ctx = BuildCtx(autoMock, dateSelectionMock: dsMock);
        var cut = ctx.Render<IndexPage>();

        // Enable toggle via event
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(true);
        dsMock.Raise(s => s.DateSelectionEnabledChanged += null, dsMock.Object, true);

        cut.WaitForAssertion(() => cut.Find(".date-selection-picker"));
        var picker = cut.Find(".date-selection-picker");
        picker.GetAttribute("value").Should().Be(DateTime.Today.ToString("yyyy-MM-dd"));
    }

    // ── TC-4a: Auto-reset on MatchSelection entry ──────────────────────────

    [TestMethod]
    public void AutoReset_MatchSelectionEntry_DisablesToggle()
    {
        var autoMock = BuildMock(PcsProState.MatchLoaded);
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        using var ctx = BuildCtx(autoMock, dateSelectionMock: dsMock);
        var cut = ctx.Render<IndexPage>();

        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);

        cut.WaitForAssertion(() =>
            dsMock.Verify(s => s.Disable(), Times.Once));
    }

    // ── TC-4b: Auto-reset on MatchSelectionReady entry ─────────────────────

    [TestMethod]
    public void AutoReset_MatchSelectionReadyEntry_DisablesToggle()
    {
        var autoMock = BuildMock(PcsProState.MatchLoaded);
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        using var ctx = BuildCtx(autoMock, dateSelectionMock: dsMock);
        var cut = ctx.Render<IndexPage>();

        autoMock.Raise(s => s.StateChanged += null, autoMock.Object, PcsProState.MatchSelectionReady);

        cut.WaitForAssertion(() =>
            dsMock.Verify(s => s.Disable(), Times.Once));
    }

    // ── TC-5: Date picker visible regardless of debug panel state (C-2b) ───

    [TestMethod]
    public void DatePicker_VisibleRegardlessOfDebugPanelState()
    {
        // The date picker checks only _dateSelectionEnabled and state — no debug panel coupling.
        var autoMock = BuildMock(PcsProState.MatchSelection);
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        using var ctx = BuildCtx(autoMock, dateSelectionMock: dsMock);
        var cut = ctx.Render<IndexPage>();

        // Enable toggle via event (simulates debug panel toggle)
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(true);
        dsMock.Raise(s => s.DateSelectionEnabledChanged += null, dsMock.Object, true);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".date-selection-picker").Should().NotBeNull();
            cut.Find(".load-matches-date-button").Should().NotBeNull();
        });
        // No DebugSection reference exists in Index.razor's date picker visibility logic
    }

    // ── TC-6: Single-match auto-select for date-based retrieval (SC-8) ─────

    [TestMethod]
    public void FetchMatchesForDate_SingleMatch_AutoSelects()
    {
        var singleMatch = TestMatch(1);
        var autoMock = BuildMock(PcsProState.MatchSelection);
        autoMock.Setup(s => s.GetMatchesForDateAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { singleMatch });
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        using var ctx = BuildCtx(autoMock, dateSelectionMock: dsMock);
        var cut = ctx.Render<IndexPage>();

        // Enable toggle
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(true);
        dsMock.Raise(s => s.DateSelectionEnabledChanged += null, dsMock.Object, true);

        cut.WaitForAssertion(() => cut.Find(".load-matches-date-button"));
        cut.Find(".load-matches-date-button").Click();

        cut.WaitForAssertion(() =>
            autoMock.Verify(
                s => s.LoadMatchAsync(It.Is<MatchInfo>(m => m == singleMatch), It.IsAny<CancellationToken>()),
                Times.Once));
    }

    // ── TC-7: Selected date forwarded to GetMatchesForDateAsync (SC-3) ─────

    [TestMethod]
    public void FetchMatchesForDate_ForwardsSelectedDate()
    {
        var autoMock = BuildMock(PcsProState.MatchSelection);
        autoMock.Setup(s => s.GetMatchesForDateAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MatchInfo> { TestMatch(1), TestMatch(2) });
        var dsMock = new Mock<IDateSelectionService>();
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(false);

        using var ctx = BuildCtx(autoMock, dateSelectionMock: dsMock);
        var cut = ctx.Render<IndexPage>();

        // Enable toggle
        dsMock.Setup(s => s.IsDateSelectionEnabled).Returns(true);
        dsMock.Raise(s => s.DateSelectionEnabledChanged += null, dsMock.Object, true);

        cut.WaitForAssertion(() => cut.Find(".date-selection-picker"));

        // Change the date picker to a specific date
        var testDate = new DateOnly(2025, 3, 15);
        cut.Find(".date-selection-picker").Change(testDate.ToString("yyyy-MM-dd"));
        cut.Find(".load-matches-date-button").Click();

        cut.WaitForAssertion(() =>
            autoMock.Verify(
                s => s.GetMatchesForDateAsync(testDate, It.IsAny<CancellationToken>()),
                Times.Once));
    }
}

