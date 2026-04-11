using FluentAssertions;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Creates a SUT with an exposed opts reference for mutation-based tests
    /// (AC-4, AC-5, AC-11, AC-14) that need to change ErrorProbability mid-test.
    /// </summary>
    private static (MockPcsProAutomationService Sut, MockPcsProOptions Opts) CreateSutWithOpts(
        Action<MockPcsProOptions>? configure = null)
    {
        var opts = new MockPcsProOptions();
        configure?.Invoke(opts);
        var sut = new MockPcsProAutomationService(
            Options.Create(opts),
            NullLogger<MockPcsProAutomationService>.Instance);
        return (sut, opts);
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

    // ══════════════════════════════════════════════════════════════════════
    // S-004 Error Injection Tests
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-3: LaunchAndLoginAsync with ErrorProbability=1.0 → Error state
    //          with canonical reason string
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_ErrorProbabilityOne_TransitionsToErrorWithCanonicalReason()
    {
        var sut = CreateSut(o => o.ErrorProbability = 1.0);

        await sut.LaunchAndLoginAsync();

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().Be("Error before Launching transition");
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-4: LoadMatchAsync from MatchSelection with EP=1.0 → Error
    //          Opts-mutation: drive to MatchSelection with EP=0, then EP=1
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_FromMatchSelection_ErrorProbabilityOne_TransitionsToError()
    {
        var (sut, opts) = CreateSutWithOpts();
        await sut.LaunchAndLoginAsync(); // EP=0, reaches MatchSelection

        opts.ErrorProbability = 1.0;
        await sut.LoadMatchAsync(new MatchInfo("m1"));

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.LastErrorReason.Should().Be("Error before MatchSelectionSearching transition");
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-5: LoadMatchAsync from MatchLoaded with EP=1.0 → Error (not
    //          MatchSelection) — ChangeMatch delay fires the error
    //          Opts-mutation: drive to MatchLoaded with EP=0, then EP=1
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_FromMatchLoaded_ErrorProbabilityOne_TransitionsToErrorAtChangeMatchDelay()
    {
        var (sut, opts) = CreateSutWithOpts();
        await sut.LaunchAndLoginAsync();
        await sut.LoadMatchAsync(new MatchInfo("m1")); // reaches MatchLoaded

        opts.ErrorProbability = 1.0;
        await sut.LoadMatchAsync(new MatchInfo("m2"));

        sut.CurrentState.Should().Be(PcsProState.Error);
        sut.CurrentState.Should().NotBe(PcsProState.MatchSelection);
        sut.LastErrorReason.Should().Be("Error before ChangeMatch MatchSelection transition");
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-6: LaunchAndLoginAsync with EP=0.0 completes normally
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_ErrorProbabilityZero_CompletesNormallyWithNullReason()
    {
        var sut = CreateSut(o => o.ErrorProbability = 0.0);

        await sut.LaunchAndLoginAsync();

        sut.CurrentState.Should().Be(PcsProState.MatchSelection);
        sut.LastErrorReason.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-7: LoadMatchAsync with EP=0.0 completes normally
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_ErrorProbabilityZero_CompletesNormallyWithNullReason()
    {
        var sut = CreateSut(o => o.ErrorProbability = 0.0);
        await sut.LaunchAndLoginAsync();

        await sut.LoadMatchAsync(new MatchInfo("m1"));

        sut.CurrentState.Should().Be(PcsProState.MatchLoaded);
        sut.LastErrorReason.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-8: Determinism — two instances with same RngSeed and EP=0.5
    //          produce identical outcomes across LaunchAndLoginAsync +
    //          LoadMatchAsync. Seed is discovered at runtime to guarantee
    //          LaunchAndLoginAsync completes (3 launch draws all ≥ 0.5).
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TwoInstancesSameSeed_ProduceIdenticalStateOutcomesAcrossMultipleCalls()
    {
        const double ep = 0.5;

        // Find a seed where all 3 LaunchAndLoginAsync draws are >= EP
        // (i.e., no error fires during launch). With EP=0.5, ~12.5% of seeds qualify.
        int? seed = null;
        for (int s = 0; s < 10_000; s++)
        {
            var probe = new Random(s);
            if (probe.NextDouble() >= ep && probe.NextDouble() >= ep && probe.NextDouble() >= ep)
            {
                seed = s;
                break;
            }
        }
        seed.Should().HaveValue("a suitable seed must be discoverable within 10000 attempts");

        var (sut1, _) = CreateSutWithOpts(o => { o.RngSeed = seed; o.ErrorProbability = ep; });
        var (sut2, _) = CreateSutWithOpts(o => { o.RngSeed = seed; o.ErrorProbability = ep; });

        // Step 1: LaunchAndLoginAsync — both must complete to MatchSelection (seed guarantees this)
        await sut1.LaunchAndLoginAsync();
        await sut2.LaunchAndLoginAsync();

        sut1.CurrentState.Should().Be(PcsProState.MatchSelection,
            "seed was chosen so launch completes on both instances");
        sut2.CurrentState.Should().Be(PcsProState.MatchSelection,
            "seed was chosen so launch completes on both instances");
        sut1.LastErrorReason.Should().Be(sut2.LastErrorReason);

        // Step 2: LoadMatchAsync — RNG sequences continue identically on both instances
        await sut1.LoadMatchAsync(new MatchInfo("m1"));
        await sut2.LoadMatchAsync(new MatchInfo("m1"));

        sut1.CurrentState.Should().Be(sut2.CurrentState,
            "identical seeds produce identical outcomes on LoadMatchAsync");
        sut1.LastErrorReason.Should().Be(sut2.LastErrorReason,
            "identical seeds produce identical LastErrorReason values");
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-9: StateChanged fires with Error; LastErrorReason is non-null
    //          inside the handler (verifies R-3 ordering: LastErrorReason
    //          set BEFORE Transition fires StateChanged)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_ErrorInjected_StateChangedFiresWithLastErrorReasonAlreadySet()
    {
        var sut = CreateSut(o => o.ErrorProbability = 1.0);
        var handlerFired = false;

        sut.StateChanged += (_, state) =>
        {
            if (state == PcsProState.Error)
            {
                sut.LastErrorReason.Should().NotBeNull(
                    "LastErrorReason must be set before StateChanged fires (R-3 ordering)");
                handlerFired = true;
            }
        };

        await sut.LaunchAndLoginAsync();

        handlerFired.Should().BeTrue("the StateChanged handler must have been invoked with Error state");
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-10: StopAsync after error resets to NotRunning and clears reason
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StopAsync_AfterErrorState_ResetsToNotRunningAndClearsLastErrorReason()
    {
        var sut = CreateSut(o => o.ErrorProbability = 1.0);
        await sut.LaunchAndLoginAsync(); // → Error
        sut.CurrentState.Should().Be(PcsProState.Error);

        await sut.StopAsync();

        sut.CurrentState.Should().Be(PcsProState.NotRunning);
        sut.LastErrorReason.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-11: Recovery cycle — after StopAsync following error, the
    //           service can successfully run LaunchAndLoginAsync again.
    //           Opts-mutation: EP=1 to trigger error, then EP=0 to recover.
    //           Separate test method per spec (not bundled with AC-10).
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_AfterErrorAndStop_SuccessfullyRecoversToMatchSelection()
    {
        var (sut, opts) = CreateSutWithOpts(o => o.ErrorProbability = 1.0);
        await sut.LaunchAndLoginAsync(); // → Error
        await sut.StopAsync();           // → NotRunning, reason cleared

        opts.ErrorProbability = 0.0;
        await sut.LaunchAndLoginAsync(); // should complete normally

        sut.CurrentState.Should().Be(PcsProState.MatchSelection);
        sut.LastErrorReason.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-12: LaunchAndLoginAsync from Error state throws
    //           InvalidOperationException (existing guard regression check)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_FromErrorState_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(o => o.ErrorProbability = 1.0);
        await sut.LaunchAndLoginAsync(); // → Error

        Func<Task> act = () => sut.LaunchAndLoginAsync();

        await act.Should().ThrowAsync<InvalidOperationException>(
            "Error state is not NotRunning, so the precondition guard must reject the call");
        sut.CurrentState.Should().Be(PcsProState.Error);
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-13: LoadMatchAsync from Error state throws
    //           InvalidOperationException (existing guard regression check)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_FromErrorState_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(o => o.ErrorProbability = 1.0);
        await sut.LaunchAndLoginAsync(); // → Error

        Func<Task> act = () => sut.LoadMatchAsync(new MatchInfo("m1"));

        await act.Should().ThrowAsync<InvalidOperationException>(
            "Error state is neither MatchSelection nor MatchLoaded, so the guard must reject");
        sut.CurrentState.Should().Be(PcsProState.Error);
    }

    // ──────────────────────────────────────────────────────────────────────
    // EI-AC-14: Reason overwrite — second error from different method
    //           overwrites the first reason set by TransitionToError.
    //           Opts-mutation to reach MatchLoaded then trigger two distinct
    //           error reasons and assert the second overwrites the first.
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TransitionToError_SecondErrorOverwritesFirstLastErrorReason()
    {
        var (sut, opts) = CreateSutWithOpts();
        await sut.LaunchAndLoginAsync();
        await sut.LoadMatchAsync(new MatchInfo("m1")); // reaches MatchLoaded

        // Error 1: LoadMatchAsync from MatchLoaded → ChangeMatchDelay fires
        opts.ErrorProbability = 1.0;
        await sut.LoadMatchAsync(new MatchInfo("m2"));
        sut.LastErrorReason.Should().Be("Error before ChangeMatch MatchSelection transition");

        // Reset to NotRunning (clears LastErrorReason)
        await sut.StopAsync();
        sut.LastErrorReason.Should().BeNull("StopAsync must clear LastErrorReason");

        // Error 2: LaunchAndLoginAsync → LaunchDelay fires (different reason)
        await sut.LaunchAndLoginAsync();
        sut.LastErrorReason.Should().Be("Error before Launching transition",
            "second error reason must overwrite the first");
    }

    // ══════════════════════════════════════════════════════════════════════
    // S-005 Match Data Tests
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────────────
    // AC-1: Correct count
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTodaysMatchesAsync_FakeMatchCountFive_ReturnsExactlyFiveRecords()
    {
        var sut = CreateSut(o => o.FakeMatchCount = 5);

        var matches = await sut.GetTodaysMatchesAsync();

        matches.Should().HaveCount(5);
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-2: Today's date on all records (pre-captured to avoid midnight race)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTodaysMatchesAsync_AllRecordsHaveTodaysDate()
    {
        var sut = CreateSut(o => o.FakeMatchCount = 3);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var matches = await sut.GetTodaysMatchesAsync();

        matches.Should().AllSatisfy(m => m.MatchDate.Should().Be(today));
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-3: Zero count returns empty list
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTodaysMatchesAsync_FakeMatchCountZero_ReturnsEmptyNonNullList()
    {
        var sut = CreateSut(o => o.FakeMatchCount = 0);

        var matches = await sut.GetTodaysMatchesAsync();

        matches.Should().NotBeNull();
        matches.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-4: Non-empty team names and match type on all records
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTodaysMatchesAsync_AllRecordsHaveNonEmptyTeamNamesAndMatchType()
    {
        var sut = CreateSut(o => o.FakeMatchCount = 5);

        var matches = await sut.GetTodaysMatchesAsync();

        matches.Should().AllSatisfy(m =>
        {
            m.HomeTeam.Should().NotBeNullOrEmpty();
            m.AwayTeam.Should().NotBeNullOrEmpty();
            m.MatchType.Should().NotBeNullOrEmpty();
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-5: Backward-compatibility defaults on MatchInfo
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MatchInfo_DefaultConstruction_CarriesExpectedSentinelValues()
    {
        var info = new MatchInfo("m1");

        info.MatchId.Should().Be("m1");
        info.HomeTeam.Should().Be("Home XI");
        info.AwayTeam.Should().Be("Away XI");
        info.MatchType.Should().Be("Friendly");
        info.MatchDate.Should().Be(default(DateOnly));
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-6: No semaphore interaction — call completes while lifecycle op in-flight
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTodaysMatchesAsync_WhileSemaphoreHeld_CompletesWithin200ms()
    {
        var sut = CreateSut(o => o.LaunchDelay = TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();

        var launchTask = Task.Run(() => sut.LaunchAndLoginAsync(cts.Token));
        await Task.Delay(100); // ensure semaphore is acquired before proceeding

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var matches = await sut.GetTodaysMatchesAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            "GetTodaysMatchesAsync must not wait for the semaphore");
        matches.Should().NotBeNull();

        cts.Cancel();
        await launchTask.IgnoringCancellationException();
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-7: Distinct MatchIds within a single response
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTodaysMatchesAsync_FakeMatchCountFive_AllMatchIdsAreDistinct()
    {
        var sut = CreateSut(o => o.FakeMatchCount = 5);

        var matches = await sut.GetTodaysMatchesAsync();

        matches.Select(m => m.MatchId).Should().OnlyHaveUniqueItems();
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-8: Pre-cancelled CancellationToken returns full list without exception
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetTodaysMatchesAsync_PreCancelledToken_ReturnsFullListWithoutException()
    {
        var sut = CreateSut(o => o.FakeMatchCount = 3);
        var cancelledToken = new CancellationToken(canceled: true);

        var matches = await sut.GetTodaysMatchesAsync(cancelledToken);

        matches.Should().HaveCount(3);
    }

    // ══════════════════════════════════════════════════════════════════════
    // S-006 Scoreboard Image Tests
    // ══════════════════════════════════════════════════════════════════════

    // ──────────────────────────────────────────────────────────────────────
    // AC-1: JPEG validity — SOI bytes (FF D8 FF) + EOI bytes (FF D9)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_ReturnsValidJpegBytes()
    {
        var sut = CreateSut(o => { o.RngSeed = 42; o.ErrorProbability = 0.0; });

        var bytes = await sut.CaptureScoreboardImageAsync();

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(4);
        bytes[0].Should().Be(0xFF, "JPEG SOI byte 1");
        bytes[1].Should().Be(0xD8, "JPEG SOI byte 2");
        bytes[2].Should().Be(0xFF, "JPEG marker prefix");
        bytes[^2].Should().Be(0xFF, "JPEG EOI byte 1");
        bytes[^1].Should().Be(0xD9, "JPEG EOI byte 2");
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-2: Probability 1.0 — three consecutive calls all pairwise non-identical
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_Probability1_AlwaysDifferentBytes()
    {
        var sut = CreateSut(o =>
        {
            o.RngSeed = 42;
            o.ErrorProbability = 0.0;
            o.ImageVariationProbability = 1.0;
        });

        var b1 = await sut.CaptureScoreboardImageAsync();
        var b2 = await sut.CaptureScoreboardImageAsync();
        var b3 = await sut.CaptureScoreboardImageAsync();

        b1.SequenceEqual(b2).Should().BeFalse("call 1 and call 2 must differ at P=1.0");
        b1.SequenceEqual(b3).Should().BeFalse("call 1 and call 3 must differ at P=1.0");
        b2.SequenceEqual(b3).Should().BeFalse("call 2 and call 3 must differ at P=1.0");
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-3: Probability 0.0 — three consecutive calls all byte-identical
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_Probability0_AlwaysIdenticalBytes()
    {
        var sut = CreateSut(o =>
        {
            o.RngSeed = 42;
            o.ErrorProbability = 0.0;
            o.ImageVariationProbability = 0.0;
        });

        var b1 = await sut.CaptureScoreboardImageAsync();
        var b2 = await sut.CaptureScoreboardImageAsync();
        var b3 = await sut.CaptureScoreboardImageAsync();

        b1.SequenceEqual(b2).Should().BeTrue("call 1 and call 2 must be identical at P=0.0");
        b1.SequenceEqual(b3).Should().BeTrue("call 1 and call 3 must be identical at P=0.0");
    }

    // ──────────────────────────────────────────────────────────────────────
    // AC-4: Cache-miss path — first call with default IVP (0.2) returns valid JPEG
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CaptureScoreboardImageAsync_FirstCallDefaultOptions_ExercisesCacheMissPath()
    {
        // Default ImageVariationProbability = 0.2; first call always generates (cache is empty)
        var sut = CreateSut(o => { o.RngSeed = 42; o.ErrorProbability = 0.0; });

        var bytes = await sut.CaptureScoreboardImageAsync();

        bytes.Should().NotBeEmpty("cache-miss path must produce a result");
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8);
        bytes[2].Should().Be(0xFF);
        bytes[^2].Should().Be(0xFF);
        bytes[^1].Should().Be(0xD9);
    }

    // ──────────────────────────────────────────────────────────────────────
    // S-007 AC-1: LaunchAndLoginAsync — log levels and counts
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_HappyPath_EmitsCorrectLogLevelsAndCounts()
    {
        var (sut, logger) = CreateSutWithLogger(o => o.ErrorProbability = 0.0);

        await sut.LaunchAndLoginAsync();

        var info  = logger.Captured.Count(e => e.Level == LogLevel.Information);
        var debug = logger.Captured.Count(e => e.Level == LogLevel.Debug);

        info.Should().Be(2,  "entry + MatchSelection completion are Info");
        debug.Should().Be(2, "Launching + LoginScreen transitions are Debug");
    }

    // ──────────────────────────────────────────────────────────────────────
    // S-007 AC-2: LaunchAndLoginAsync — rendered message substrings
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_HappyPath_EmitsExpectedMessageSubstrings()
    {
        var (sut, logger) = CreateSutWithLogger(o => o.ErrorProbability = 0.0);

        await sut.LaunchAndLoginAsync();

        var messages = logger.Captured.Select(e => e.Message).ToList();

        messages.Should().ContainMatch("*LaunchAndLoginAsync starting*");
        messages.Should().ContainMatch("*Launching*");
        messages.Should().ContainMatch("*LoginScreen*");
        messages.Should().ContainMatch("*LaunchAndLoginAsync complete*");
    }

    // ──────────────────────────────────────────────────────────────────────
    // S-007 AC-3: StopAsync — log levels and counts
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task StopAsync_FromMatchLoaded_EmitsOneInfoEntry_OneInfoCompletion()
    {
        var (sut, logger) = CreateSutWithLogger(o => o.ErrorProbability = 0.0);
        await sut.LaunchAndLoginAsync();
        await sut.LoadMatchAsync(new MatchInfo("test-match"));
        logger.Captured.Clear();

        await sut.StopAsync();

        var info = logger.Captured.Count(e => e.Level == LogLevel.Information);
        info.Should().Be(2, "StopAsync entry (starting from state) + completion are both Info");
    }

    // ──────────────────────────────────────────────────────────────────────
    // S-007 AC-4: ErrorProbability=1.0 — warning log emitted with reason string
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LaunchAndLoginAsync_ErrorProbabilityOne_EmitsWarningWithReason()
    {
        var (sut, logger) = CreateSutWithLogger(o => o.ErrorProbability = 1.0);

        await sut.LaunchAndLoginAsync();

        var warning = logger.Captured.Single(e => e.Level == LogLevel.Warning);
        warning.Message.Should().Contain("Error before Launching transition");
    }

    // ──────────────────────────────────────────────────────────────────────
    // S-007 AC-5: LoadMatchAsync happy path — log levels and counts
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_FirstLoad_EmitsCorrectLogLevelsAndCounts()
    {
        var (sut, logger) = CreateSutWithLogger(o => o.ErrorProbability = 0.0);
        await sut.LaunchAndLoginAsync();
        logger.Captured.Clear();

        await sut.LoadMatchAsync(new MatchInfo("test-match"));

        var info  = logger.Captured.Count(e => e.Level == LogLevel.Information);
        var debug = logger.Captured.Count(e => e.Level == LogLevel.Debug);

        info.Should().Be(2,  "LoadMatchAsync entry + completion are Info");
        debug.Should().Be(2, "MatchSelectionSearching + MatchSelectionReady transitions are Debug");
    }

    // ──────────────────────────────────────────────────────────────────────
    // S-007 AC-6: LoadMatchAsync change-match path — extra Debug log
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LoadMatchAsync_ChangeMatchPath_EmitsExtraDebugLog()
    {
        var (sut, logger) = CreateSutWithLogger(o => o.ErrorProbability = 0.0);
        await sut.LaunchAndLoginAsync();
        await sut.LoadMatchAsync(new MatchInfo("first-match"));
        logger.Captured.Clear();

        await sut.LoadMatchAsync(new MatchInfo("second-match"));

        var debug = logger.Captured.Count(e => e.Level == LogLevel.Debug);

        // ChangeMatch path adds one extra Debug: "ChangeMatch — reached MatchSelection"
        debug.Should().Be(3, "ChangeMatch (MatchSelection) + MatchSelectionSearching + MatchSelectionReady");
    }

    // ──────────────────────────────────────────────────────────────────────
    // S-007 logging helper
    // ──────────────────────────────────────────────────────────────────────

    private static (MockPcsProAutomationService Sut, CapturingLogger<MockPcsProAutomationService> Logger)
        CreateSutWithLogger(Action<MockPcsProOptions>? configure = null)
    {
        var opts = new MockPcsProOptions();
        configure?.Invoke(opts);
        var logger = new CapturingLogger<MockPcsProAutomationService>();
        var sut = new MockPcsProAutomationService(Options.Create(opts), logger);
        return (sut, logger);
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

// ──────────────────────────────────────────────────────────────────────────
// Capturing logger for S-007 logging tests
// ──────────────────────────────────────────────────────────────────────────

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public record LogEntry(LogLevel Level, string Message);

    public List<LogEntry> Captured { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Captured.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
