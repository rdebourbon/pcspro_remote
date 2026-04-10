using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock.Tests;

[TestClass]
public sealed class MockPcsProAutomationServiceTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static MockPcsProAutomationService CreateSut(
        Action<MockPcsProOptions>? configure = null)
    {
        var opts = new MockPcsProOptions();
        configure?.Invoke(opts);
        return new MockPcsProAutomationService(
            Options.Create(opts),
            NullLogger<MockPcsProAutomationService>.Instance);
    }

    private static List<PcsProState> CaptureEvents(MockPcsProAutomationService sut)
    {
        var events = new List<PcsProState>();
        sut.StateChanged += (_, s) => events.Add(s);
        return events;
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-3: LaunchAndLoginAsync happy path
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_ZeroDelays_FiresEventsInOrderAndReachesMatchSelection()
    {
        var sut = CreateSut();
        var events = CaptureEvents(sut);

        await sut.LaunchAndLoginAsync();

        events.Should().Equal(
            PcsProState.Launching,
            PcsProState.LoginScreen,
            PcsProState.MatchSelection);
        sut.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-4: LoadMatchAsync standard path
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_FromMatchSelection_FiresEventsInOrderAndReachesMatchLoaded()
    {
        var sut = CreateSut();
        await sut.LaunchAndLoginAsync();
        var events = CaptureEvents(sut);

        await sut.LoadMatchAsync(new MatchInfo("m1"));

        events.Should().Equal(
            PcsProState.MatchSelectionSearching,
            PcsProState.MatchSelectionReady,
            PcsProState.MatchLoaded);
        sut.CurrentState.Should().Be(PcsProState.MatchLoaded);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-5: LoadMatchAsync ChangeMatch path
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_FromMatchLoaded_FiresChangeMatchEventsThenLoads()
    {
        var sut = CreateSut();
        await sut.LaunchAndLoginAsync();
        await sut.LoadMatchAsync(new MatchInfo("m1"));
        var events = CaptureEvents(sut);

        await sut.LoadMatchAsync(new MatchInfo("m2"));

        events.Should().Equal(
            PcsProState.MatchSelection,
            PcsProState.MatchSelectionSearching,
            PcsProState.MatchSelectionReady,
            PcsProState.MatchLoaded);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-6: StopAsync from MatchLoaded
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StopAsync_FromMatchLoaded_FiresNotRunningAndResetsState()
    {
        var sut = CreateSut();
        await sut.LaunchAndLoginAsync();
        await sut.LoadMatchAsync(new MatchInfo("m1"));
        var events = CaptureEvents(sut);

        await sut.StopAsync();

        events.Should().ContainSingle().Which.Should().Be(PcsProState.NotRunning);
        sut.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-7: StopAsync from NotRunning — idempotent, no event
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StopAsync_FromNotRunning_ReturnsImmediatelyWithoutFiringEvent()
    {
        var sut = CreateSut();
        var events = CaptureEvents(sut);

        await sut.StopAsync();

        events.Should().BeEmpty();
        sut.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-8: Cancellation of LaunchAndLoginAsync
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_CancelledBeforeLaunchDelay_ThrowsOceAndStateUnchanged()
    {
        var sut = CreateSut(o => o.LaunchDelay = TimeSpan.FromMilliseconds(500));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        Func<Task> act = () => sut.LaunchAndLoginAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sut.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-9: Same-method concurrency throws InvalidOperationException
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_ConcurrentCall_SecondThrowsInvalidOperationException()
    {
        var sut = CreateSut(o => o.LaunchDelay = TimeSpan.FromMilliseconds(300));
        using var cts = new CancellationTokenSource();

        var first = Task.Run(() => sut.LaunchAndLoginAsync(cts.Token));

        // Give the first call time to acquire the semaphore mid-delay
        await Task.Delay(50);

        var act = () => sut.LaunchAndLoginAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();

        cts.Cancel();
        await first.IgnoringCancellationException();
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-10: Timing — full happy-path under 500ms at zero delays
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task FullLifecycle_ZeroDelays_CompletesUnder500ms()
    {
        var sut = CreateSut();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await sut.LaunchAndLoginAsync();
        await sut.LoadMatchAsync(new MatchInfo("m1"));

        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-11: GetTeamNamesAsync
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTeamNamesAsync_ReturnsNonEmptyTeamNames()
    {
        var sut = CreateSut();

        var teams = await sut.GetTeamNamesAsync();

        teams.HomeTeam.Should().NotBeNullOrEmpty();
        teams.AwayTeam.Should().NotBeNullOrEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-12: RefreshScoreboardAsync
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RefreshScoreboardAsync_CompletesWithoutException()
    {
        var sut = CreateSut();
        await sut.RefreshScoreboardAsync();
        // no assert needed — reaching here means no exception
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-14: Cancellation of LoadMatchAsync
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_CancelledBeforeFirstDelay_ThrowsOceAndStateUnchanged()
    {
        var sut = CreateSut(o => o.SearchTriggeredDelay = TimeSpan.FromMilliseconds(500));
        await sut.LaunchAndLoginAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        Func<Task> act = () => sut.LoadMatchAsync(new MatchInfo("m1"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sut.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-15: Cancellation of StopAsync
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StopAsync_CancelledBeforeStopDelay_ThrowsOceAndStateUnchanged()
    {
        var sut = CreateSut(o => o.StopDelay = TimeSpan.FromMilliseconds(500));
        await sut.LaunchAndLoginAsync();
        await sut.LoadMatchAsync(new MatchInfo("m1"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        Func<Task> act = () => sut.StopAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sut.CurrentState.Should().Be(PcsProState.MatchLoaded);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-16: LaunchAndLoginAsync from invalid entry state
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_FromNonNotRunningState_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        await sut.LaunchAndLoginAsync(); // now in MatchSelection

        Func<Task> act = () => sut.LaunchAndLoginAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        sut.CurrentState.Should().Be(PcsProState.MatchSelection);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-17: LoadMatchAsync from invalid entry state
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_FromNotRunning_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(); // NotRunning

        Func<Task> act = () => sut.LoadMatchAsync(new MatchInfo("m1"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        sut.CurrentState.Should().Be(PcsProState.NotRunning);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-18: Cross-method concurrency
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_WhileLaunchAndLoginInProgress_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(o => o.LaunchDelay = TimeSpan.FromMilliseconds(300));
        using var cts = new CancellationTokenSource();

        var first = Task.Run(() => sut.LaunchAndLoginAsync(cts.Token));

        await Task.Delay(50);

        var act = () => sut.LoadMatchAsync(new MatchInfo("m1"));
        await act.Should().ThrowAsync<InvalidOperationException>();

        cts.Cancel();
        await first.IgnoringCancellationException();
    }
}

// ──────────────────────────────────────────────────────────────────────────
// Task extension to swallow expected cancellation in concurrency tests
// ──────────────────────────────────────────────────────────────────────────

internal static class TaskExtensions
{
    internal static async Task IgnoringCancellationException(this Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
    }
}
