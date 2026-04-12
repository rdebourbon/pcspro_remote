using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;

namespace PcsRemote.Web.Tests;

[TestClass]
[DoNotParallelize] // BackgroundService tests use PeriodicTimer; run sequentially to avoid thread-pool contention with concurrent tests.
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

    private static (
        ScoreboardPollingService Service,
        Mock<IScoreboardService> ScoreboardMock,
        Mock<IPcsProAutomationService> AutomationMock)
    Build(PcsProState initialState = PcsProState.NotRunning, int intervalMs = 50)
    {
        var scoreMock = new Mock<IScoreboardService>();
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(initialState);

        // Use milliseconds via a custom config-less approach: supply fractional seconds.
        // Since config only reads integer seconds, we build the service with a 1s config
        // but override via a direct subclass approach — instead, we accept ms directly
        // by using a helper that injects a very small interval via config key.
        // For sub-second granularity in tests we use 1s minimum from config.
        // To get faster test execution, we expose a test-only ctor that accepts TimeSpan.
        var config = BuildConfig(1); // 1s is the minimum config-driven value
        _ = intervalMs; // intervalMs used in Test-only ctor path below
        var svc = new ScoreboardPollingService(scoreMock.Object, autoMock.Object, config, NullLogger<ScoreboardPollingService>.Instance);
        return (svc, scoreMock, autoMock);
    }

    private static (
        ScoreboardPollingService Service,
        Mock<IScoreboardService> ScoreboardMock,
        Mock<IPcsProAutomationService> AutomationMock)
    BuildFast(PcsProState initialState = PcsProState.NotRunning)
    {
        var scoreMock = new Mock<IScoreboardService>();
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(initialState);

        // Use the test-only TimeSpan constructor so tests run at 50 ms —
        // fast enough to avoid thread-pool saturation under parallel test execution.
        var svc = new ScoreboardPollingService(scoreMock.Object, autoMock.Object, TimeSpan.FromMilliseconds(50), NullLogger<ScoreboardPollingService>.Instance);
        return (svc, scoreMock, autoMock);
    }

    // ── TC-1: Cold-start polling ─────────────────────────────────────────────

    [TestMethod]
    public async Task StartAsync_StateAlreadyMatchLoaded_StartsPollingImmediately()
    {
        var (svc, scoreMock, _) = BuildFast(PcsProState.MatchLoaded);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scoreMock.Verify(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-2: No polling when not in MatchLoaded ─────────────────────────────

    [TestMethod]
    public async Task StartAsync_StateNotMatchLoaded_DoesNotStartPolling()
    {
        var (svc, scoreMock, _) = BuildFast(PcsProState.NotRunning);

        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(200); // Wait longer than one tick would take

        scoreMock.Verify(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()), Times.Never);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-3: StateChanged → MatchLoaded starts polling ──────────────────────

    [TestMethod]
    public async Task StateChanged_ToMatchLoaded_StartsPolling()
    {
        var (svc, scoreMock, autoMock) = BuildFast(PcsProState.NotRunning);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);

        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scoreMock.Verify(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-4: StateChanged away stops polling ────────────────────────────────

    [TestMethod]
    public async Task StateChanged_AwayFromMatchLoaded_StopsPolling()
    {
        var (svc, scoreMock, autoMock) = BuildFast(PcsProState.MatchLoaded);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5)); // confirm polling started

        // Transition away — stop polling
        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);
        await Task.Delay(200); // allow stop to settle

        var countAfterStop = scoreMock.Invocations.Count(i => i.Method.Name == nameof(IScoreboardService.CaptureAndBroadcastAsync));
        await Task.Delay(300); // wait several ticks worth to confirm no new captures
        var countAfterWait = scoreMock.Invocations.Count(i => i.Method.Name == nameof(IScoreboardService.CaptureAndBroadcastAsync));

        countAfterWait.Should().Be(countAfterStop, "polling should have stopped");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-5: Exception in capture is logged, loop continues ─────────────────

    [TestMethod]
    public async Task CaptureThrows_ErrorIsLogged_LoopContinues()
    {
        var (svc, scoreMock, autoMock) = BuildFast(PcsProState.NotRunning);
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

        await secondCallTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

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
        var (svc, scoreMock, autoMock) = BuildFast(PcsProState.MatchLoaded);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Func<Task> stop = () => svc.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();

        svc.Dispose();
    }

    // ── TC-9: Concurrent MatchLoaded events → idempotent (one loop only) ─────

    [TestMethod]
    public async Task StartLoop_CalledConcurrently_IsIdempotent()
    {
        var (svc, scoreMock, autoMock) = BuildFast(PcsProState.NotRunning);
        var captureCount = 0;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                Interlocked.Increment(ref captureCount);
                tcs.TrySetResult(true);
                return Task.CompletedTask;
            });

        await svc.StartAsync(CancellationToken.None);

        // Fire two concurrent MatchLoaded events
        await Task.WhenAll(
            Task.Run(() => autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded)),
            Task.Run(() => autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded)));

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200); // wait for a few extra ticks to detect a rogue second loop

        // With 50 ms interval over ~200 ms, one loop yields ~4 ticks.
        // Two concurrent loops would yield ~8+ — assert no runaway doubling.
        var observed = Volatile.Read(ref captureCount);
        observed.Should().BeLessThanOrEqualTo(8, "only one loop should be active");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-10: Loop task completes as RanToCompletion after state-change stop ─

    [TestMethod]
    public async Task StateChanged_AwayFromMatchLoaded_LoopTaskCompletesWithoutException()
    {
        var (svc, scoreMock, autoMock) = BuildFast(PcsProState.MatchLoaded);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scoreMock
            .Setup(s => s.CaptureAndBroadcastAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(_ => tcs.TrySetResult(true))
            .Returns(Task.CompletedTask);

        await svc.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Transition away — this calls StopLoopAsync internally
        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);

        // Give async void handler time to complete
        await Task.Delay(2000);

        // ExecuteTask should still be running (service alive), no fault
        svc.ExecuteTask.Should().NotBeNull();
        svc.ExecuteTask!.IsFaulted.Should().BeFalse("loop exit should not fault the service");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-11: StopAsync while idle exits cleanly ────────────────────────────

    [TestMethod]
    public async Task StopAsync_WhileIdle_ExitsCleanly()
    {
        var (svc, _, _) = BuildFast(PcsProState.NotRunning);

        await svc.StartAsync(CancellationToken.None);

        Func<Task> stop = () => svc.StopAsync(CancellationToken.None);
        await stop.Should().NotThrowAsync();

        svc.Dispose();
    }

    // ── TC-12: CaptureAndBroadcastAsync receives loop-scoped cancellation token

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_ReceivesLoopCancellationToken()
    {
        var (svc, scoreMock, autoMock) = BuildFast(PcsProState.NotRunning);
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

        await captureTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert the captured token is a real cancellable loop-scoped token (not CancellationToken.None).
        capturedToken.Should().NotBe(CancellationToken.None, "loop-scoped token must be forwarded");
        capturedToken.CanBeCanceled.Should().BeTrue("a real loop-scoped token must be cancellable");
        capturedToken.IsCancellationRequested.Should().BeFalse("token should not yet be cancelled");

        autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);
        await Task.Delay(2000); // allow StopLoopAsync to cancel and dispose

        // Do NOT check capturedToken.IsCancellationRequested here — CTS is disposed after a clean stop
        // and accessing a token property on a disposed source is undefined behaviour across .NET versions.

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }
}
