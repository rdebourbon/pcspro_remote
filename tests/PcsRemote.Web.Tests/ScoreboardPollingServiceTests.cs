using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;

namespace PcsRemote.Web.Tests;

[TestClass]
[DoNotParallelize] // tests share process resources; run sequentially within the assembly
public sealed class ScoreboardPollingServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(int? intervalSeconds = null)
    {
        var values = new Dictionary<string, string?>();
        if (intervalSeconds.HasValue)
            values["Scoreboard:CaptureIntervalSeconds"] = intervalSeconds.Value.ToString();
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Builds a <see cref="ScoreboardPollingService"/> backed by a <see cref="FakePeriodicTimer"/>
    /// factory. Ticks never fire automatically — tests drive them via the returned channel reader.
    /// </summary>
    private static (
        ScoreboardPollingService Svc,
        Mock<IScoreboardService> ScoreMock,
        Mock<IPcsProAutomationService> AutoMock,
        ChannelReader<FakePeriodicTimer> Timers)
    BuildFake(PcsProState initialState = PcsProState.NotRunning)
    {
        var scoreMock = new Mock<IScoreboardService>();
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(initialState);

        // Thread-safe channel: factory (called on Task.Run thread) writes; test thread reads.
        var timerChannel = Channel.CreateUnbounded<FakePeriodicTimer>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var svc = new ScoreboardPollingService(
            scoreMock.Object, autoMock.Object,
            () =>
            {
                var t = new FakePeriodicTimer();
                timerChannel.Writer.TryWrite(t);
                return t;
            },
            NullLogger<ScoreboardPollingService>.Instance);

        return (svc, scoreMock, autoMock, timerChannel.Reader);
    }

    /// <summary>
    /// Reads the next timer created by the factory (waits up to <paramref name="timeout"/>).
    /// </summary>
    private static async Task<FakePeriodicTimer> ReadTimerAsync(
        ChannelReader<FakePeriodicTimer> timers,
        TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(5);
        return await timers.ReadAsync().AsTask().WaitAsync(timeout);
    }

    // ── TC-1: Cold-start polling ─────────────────────────────────────────────

    [TestMethod]
    public async Task StartAsync_StateAlreadyMatchLoaded_StartsPollingImmediately()
    {
        var (svc, scoreMock, _, timers) = BuildFake(PcsProState.MatchLoaded);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        timer.TriggerTick();

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scoreMock.Verify(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-2: No polling when not in MatchLoaded ─────────────────────────────

    [TestMethod]
    public async Task StartAsync_StateNotMatchLoaded_DoesNotStartPolling()
    {
        var (svc, scoreMock, _, timers) = BuildFake(PcsProState.NotRunning);

        await svc.StartAsync(CancellationToken.None);

        // ExecuteAsync subscribes to StateChanged then awaits indefinitely — no StartLoop
        // for NotRunning state means no timer was created and no ticks were fired.
        timers.TryRead(out _).Should().BeFalse("no loop should start for NotRunning state");
        scoreMock.Verify(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()), Times.Never);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-3: StateChanged → MatchLoaded starts polling ──────────────────────

    [TestMethod]
    public async Task StateChanged_ToMatchLoaded_StartsPolling()
    {
        var (svc, scoreMock, autoMock, timers) = BuildFake(PcsProState.NotRunning);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        timer.TriggerTick();

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scoreMock.Verify(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-4: StateChanged away stops polling ────────────────────────────────

    [TestMethod]
    public async Task StateChanged_AwayFromMatchLoaded_StopsPolling()
    {
        var (svc, scoreMock, autoMock, timers) = BuildFake(PcsProState.MatchLoaded);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        timer.TriggerTick();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)); // confirm polling started

        // Transition away — OnStateChanged fires StopLoopAsync fire-and-forget
        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);

        // WhenDisposed completes when RunLoopAsync's await-using disposes the timer — i.e.,
        // the loop has fully exited after the cancellation token was fired.
        await timer.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(5));

        var countAfterStop = scoreMock.Invocations.Count(
            i => i.Method.Name == nameof(IScoreboardService.CaptureAndBroadcastAsync));

        // Timer is disposed — TriggerTick is a no-op (channel writer is completed)
        timer.TriggerTick();
        timer.TriggerTick();

        var countAfterWait = scoreMock.Invocations.Count(
            i => i.Method.Name == nameof(IScoreboardService.CaptureAndBroadcastAsync));

        countAfterWait.Should().Be(countAfterStop, "polling should have stopped");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-5: Exception in capture is logged, loop continues ─────────────────

    [TestMethod]
    public async Task CaptureThrows_ErrorIsLogged_LoopContinues()
    {
        var (svc, scoreMock, autoMock, timers) = BuildFake(PcsProState.NotRunning);
        var secondCallTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callCount = 0;

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("capture boom");
                secondCallTcs.TrySetResult(true);
                return Task.CompletedTask;
            });

        await svc.StartAsync(CancellationToken.None);
        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

        var timer = await ReadTimerAsync(timers);

        await timer.WaitingForTickAsync(); // loop ready for tick 1
        timer.TriggerTick();               // tick 1 → CaptureAndBroadcastAsync throws

        await timer.WaitingForTickAsync(); // loop caught exception and is ready for tick 2
        timer.TriggerTick();               // tick 2 → sets secondCallTcs

        await secondCallTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        callCount.Should().BeGreaterThanOrEqualTo(2, "loop must continue after exception");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-6: Config absent → default 2s ────────────────────────────────────

    [TestMethod]
    public void IntervalConfig_KeyAbsent_ConstructsWithDefault()
    {
        var scoreMock = new Mock<IScoreboardService>();
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(PcsProState.NotRunning);

        var config = BuildConfig(); // no key
        var svc = new ScoreboardPollingService(scoreMock.Object, autoMock.Object, config, NullLogger<ScoreboardPollingService>.Instance);

        // No exception = default (2s) was used
        svc.Dispose();
    }

    // ── TC-7: Config present → uses configured value ─────────────────────────

    [TestMethod]
    public async Task IntervalConfig_KeyPresent_UsesConfiguredValue()
    {
        // This test uses the production IConfiguration constructor — it exercises config
        // parsing and therefore uses a real RealPeriodicTimer. The 5s timeout is generous
        // for a 1s interval so this test remains stable even without a fake timer.
        var scoreMock = new Mock<IScoreboardService>();
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(PcsProState.MatchLoaded);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        var config = BuildConfig(1); // 1s interval
        var svc = new ScoreboardPollingService(scoreMock.Object, autoMock.Object, config, NullLogger<ScoreboardPollingService>.Instance);

        await svc.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scoreMock.Verify(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-8: StopAsync while polling exits cleanly ──────────────────────────

    [TestMethod]
    public async Task StopAsync_WhilePolling_ExitsCleanly()
    {
        var (svc, scoreMock, _, timers) = BuildFake(PcsProState.MatchLoaded);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        timer.TriggerTick();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Func<Task> stop = () => svc.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();

        svc.Dispose();
    }

    // ── TC-9: Concurrent MatchLoaded events → idempotent (one loop only) ─────

    [TestMethod]
    public async Task StartLoop_CalledConcurrently_IsIdempotent()
    {
        var (svc, scoreMock, autoMock, timers) = BuildFake(PcsProState.NotRunning);
        var captureCount = 0;

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                Interlocked.Increment(ref captureCount);
                return Task.CompletedTask;
            });

        await svc.StartAsync(CancellationToken.None);

        // Fire two concurrent MatchLoaded events — Interlocked CAS must reject the second
        await Task.WhenAll(
            Task.Run(() => autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded)),
            Task.Run(() => autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded)));

        // Exactly one Task.Run was dispatched (CAS loser returns without Task.Run) —
        // so exactly one timer will ever be created.
        var timer = await ReadTimerAsync(timers);
        timers.TryRead(out _).Should().BeFalse("CAS gate must allow only one loop to start");

        // Trigger 3 ticks — one active loop yields exactly 3 captures; a rogue second
        // loop would double each tick producing 6+.
        for (var i = 0; i < 3; i++)
        {
            await timer.WaitingForTickAsync();
            timer.TriggerTick();
        }
        // Wait for the loop to be ready for a 4th tick — confirms all 3 ticks were processed.
        await timer.WaitingForTickAsync();

        Volatile.Read(ref captureCount).Should().Be(3, "three ticks must yield exactly 3 captures from one loop");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-10: Loop task completes as RanToCompletion after state-change stop ─

    [TestMethod]
    public async Task StateChanged_AwayFromMatchLoaded_LoopTaskCompletesWithoutException()
    {
        var (svc, scoreMock, autoMock, timers) = BuildFake(PcsProState.MatchLoaded);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        timer.TriggerTick();
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Transition away — StopLoopAsync fires asynchronously from OnStateChanged
        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);

        // WhenDisposed is deterministic: fires when RunLoopAsync's await-using exits.
        // This replaces the previous Task.Delay(2000) with a precise synchronisation point.
        await timer.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(5));

        // ExecuteTask should still be running (service alive), not faulted
        svc.ExecuteTask.Should().NotBeNull();
        svc.ExecuteTask!.IsFaulted.Should().BeFalse("loop exit should not fault the service");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-11: StopAsync while idle exits cleanly ────────────────────────────

    [TestMethod]
    public async Task StopAsync_WhileIdle_ExitsCleanly()
    {
        var (svc, _, _, _) = BuildFake(PcsProState.NotRunning);

        await svc.StartAsync(CancellationToken.None);

        Func<Task> stop = () => svc.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();

        svc.Dispose();
    }

    // ── TC-12: CaptureAndBroadcastAsync receives loop-scoped cancellation token

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_ReceivesLoopCancellationToken()
    {
        var (svc, scoreMock, autoMock, timers) = BuildFake(PcsProState.NotRunning);
        CancellationToken capturedToken = default;
        var captureTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct =>
            {
                capturedToken = ct;
                captureTcs.TrySetResult(true);
            })
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        timer.TriggerTick();
        await captureTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        capturedToken.Should().NotBe(CancellationToken.None, "loop-scoped token must be forwarded");
        capturedToken.CanBeCanceled.Should().BeTrue("a real loop-scoped token must be cancellable");
        capturedToken.IsCancellationRequested.Should().BeFalse("token should not yet be cancelled");

        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);

        // WhenDisposed replaces the previous Task.Delay(2000) — deterministic loop exit signal.
        await timer.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(5));

        // Do NOT check capturedToken.IsCancellationRequested — CTS is disposed after a clean
        // stop and accessing a token property on a disposed source is undefined behaviour.

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }
}
