using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Shared;
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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

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

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);

        var cut = ctx.Render<IndexPage>();

        // cut.Instance.Dispose() calls IDisposable.Dispose() synchronously so
        // VerifyRemove can be asserted immediately (same pattern as SPEC-S-004 AC-11).
        cut.Instance.Dispose();

        mock.VerifyRemove(
            s => s.StateChanged -= It.IsAny<EventHandler<PcsProState>>(),
            Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

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

