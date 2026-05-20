using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;

namespace PcsRemote.Web.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PlayCricketMidnightResetHostedServiceTests
{
    // 23:00:00 → delay to next midnight is 1 hour (3600 s)
    private static readonly DateTimeOffset NightTime =
        new(2025, 6, 14, 23, 0, 0, TimeSpan.Zero);

    // Exactly midnight → delay should be 24 hours (86400 s)
    private static readonly DateTimeOffset ExactMidnight =
        new(2025, 6, 15, 0, 0, 0, TimeSpan.Zero);

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (
        PlayCricketMidnightResetHostedService Svc,
        Mock<IPcsProAutomationService> AutoMock,
        Mock<IManualModeService> ManualMock,
        Mock<IPlayCricketWatcherService> WatcherMock,
        ChannelReader<TaskCompletionSource> Delays,
        CapturingLogger<PlayCricketMidnightResetHostedService> Logger)
    BuildFake(
        PcsProState state = PcsProState.MatchLoaded,
        bool isManualModeActive = false,
        DateTimeOffset? now = null,
        bool autoWatchEnabled = true)
    {
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.SetupGet(a => a.CurrentState).Returns(state);
        autoMock.Setup(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var manualMock = new Mock<IManualModeService>();
        manualMock.SetupGet(m => m.IsManualModeActive).Returns(isManualModeActive);

        var watcherMock = new Mock<IPlayCricketWatcherService>();
        watcherMock.SetupGet(w => w.IsEnabled).Returns(autoWatchEnabled);

        var logger = new CapturingLogger<PlayCricketMidnightResetHostedService>();

        var nowValue = now ?? NightTime;
        var delayChannel = Channel.CreateUnbounded<TaskCompletionSource>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var svc = new PlayCricketMidnightResetHostedService(
            autoMock.Object,
            manualMock.Object,
            watcherMock.Object,
            logger,
            getNow: () => nowValue,
            delayFactory: (_, ct) =>
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                delayChannel.Writer.TryWrite(tcs);
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        return (svc, autoMock, manualMock, watcherMock, delayChannel.Reader, logger);
    }

    private static async Task<TaskCompletionSource> ReadDelayAsync(ChannelReader<TaskCompletionSource> reader)
        => await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    private static async Task TriggerWakeAsync(ChannelReader<TaskCompletionSource> reader)
    {
        var tcs = await ReadDelayAsync(reader);
        tcs.TrySetResult();
        // Allow the wake sequence to complete before the next re-arm delay is created.
        await ReadDelayAsync(reader).WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { });
    }

    private static async Task TriggerWakeAndAwaitReArmAsync(ChannelReader<TaskCompletionSource> reader)
    {
        var tcs = await ReadDelayAsync(reader);
        tcs.TrySetResult();
        // Wait for the service to re-arm (enter the next delay) — confirms wake sequence completed.
        await ReadDelayAsync(reader);
    }

    // ── TC-1: MatchLoaded + manual off → ChangeMatchAsync called ─────────────

    [TestMethod]
    public async Task MidnightWake_MatchLoaded_ManualModeOff_CallsChangeMatch()
    {
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.MatchLoaded, isManualModeActive: false);

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Once);
        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-2: MatchSelection → ChangeMatchAsync NOT called ───────────────────

    [TestMethod]
    public async Task MidnightWake_MatchSelection_DoesNotCallChangeMatch()
    {
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.MatchSelection);

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-3: NotRunning → ChangeMatchAsync NOT called ───────────────────────

    [TestMethod]
    public async Task MidnightWake_NotRunning_DoesNotCallChangeMatch()
    {
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.NotRunning);

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-4: Error state → ChangeMatchAsync NOT called ──────────────────────

    [TestMethod]
    public async Task MidnightWake_Error_DoesNotCallChangeMatch()
    {
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.Error);

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-5: MatchLoaded + manual mode active → ChangeMatchAsync NOT called ──

    [TestMethod]
    public async Task MidnightWake_MatchLoaded_ManualModeActive_DoesNotCallChangeMatch()
    {
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.MatchLoaded, isManualModeActive: true);

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-6: Non-loaded state → suppression always cleared ──────────────────

    [TestMethod]
    public async Task MidnightWake_AlwaysClearsSuppression_WhenNotLoaded()
    {
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.MatchSelection);

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.AtLeastOnce);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-7: Loaded state → suppression cleared after ChangeMatchAsync ───────

    [TestMethod]
    public async Task MidnightWake_AlwaysClearsSuppression_WhenLoaded()
    {
        var changeMatchOrder = new List<string>();
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.MatchLoaded);

        autoMock.Setup(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()))
            .Callback(() => changeMatchOrder.Add("ChangeMatch"))
            .Returns(Task.CompletedTask);
        watcherMock.Setup(w => w.ClearAutoLoadSuppressions())
            .Callback(() => changeMatchOrder.Add("Clear"));

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        changeMatchOrder.Should().Equal(
            new[] { "ChangeMatch", "Clear" },
            "suppression must be cleared after ChangeMatchAsync");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-8: ChangeMatchAsync throws → suppression still cleared ────────────

    [TestMethod]
    public async Task MidnightWake_ChangeMatchThrows_SuppressionStillCleared()
    {
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.MatchLoaded);

        autoMock.Setup(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("automation failure"));

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.AtLeastOnce,
            "suppression must be cleared even when ChangeMatchAsync throws");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-9: ChangeMatchAsync throws → error logged ──────────────────────────

    [TestMethod]
    public async Task MidnightWake_ChangeMatchThrows_LogsError()
    {
        var (svc, autoMock, _, _, delays, logger) = BuildFake(
            state: PcsProState.MatchLoaded);

        autoMock.Setup(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("automation failure"));

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error,
            "error from ChangeMatchAsync must be logged");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-10: Stop during delay → no wake actions ───────────────────────────

    [TestMethod]
    public async Task ServiceStop_DuringDelay_ExitsWithoutWakeActions()
    {
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake();

        await svc.StartAsync(CancellationToken.None);
        // Confirm service is in delay before stopping
        _ = await ReadDelayAsync(delays);

        await svc.StopAsync(CancellationToken.None);

        autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.Never);

        svc.Dispose();
    }

    // ── TC-11: After wake, service re-arms for next midnight ──────────────────

    [TestMethod]
    public async Task MidnightWake_Fires_ReArmsForNextMidnight()
    {
        var (svc, _, _, _, delays, _) = BuildFake();

        await svc.StartAsync(CancellationToken.None);

        var firstDelay = await ReadDelayAsync(delays);
        firstDelay.TrySetResult();

        // Verify re-arm: service must enter a second delay after the wake sequence completes.
        // ReadDelayAsync throws TimeoutException if no re-arm occurs within 5 seconds.
        var secondDelay = await ReadDelayAsync(delays);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-12: Delay duration is 1 hour when current time is 23:00 ───────────

    [TestMethod]
    public async Task MidnightWake_DelayDuration_IsCorrectForCurrentTime()
    {
        var capturedDuration = TimeSpan.Zero;
        var (svc, _, _, _, _, _) = BuildFake(now: NightTime); // 23:00 → 1h to midnight

        // Rebuild to intercept the duration before it's discarded
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.SetupGet(a => a.CurrentState).Returns(PcsProState.MatchSelection);
        var manualMock = new Mock<IManualModeService>();
        manualMock.SetupGet(m => m.IsManualModeActive).Returns(false);
        var watcherMock = new Mock<IPlayCricketWatcherService>();
        var logger = new CapturingLogger<PlayCricketMidnightResetHostedService>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        svc.Dispose();
        using var svcp = new PlayCricketMidnightResetHostedService(
            autoMock.Object, manualMock.Object, watcherMock.Object, logger,
            getNow: () => NightTime,
            delayFactory: (duration, ct) =>
            {
                capturedDuration = duration;
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        await svcp.StartAsync(CancellationToken.None);
        await Task.Delay(50); // Allow service to call delayFactory

        capturedDuration.TotalSeconds.Should().BeInRange(3595, 3605,
            "delay from 23:00 to midnight should be ~3600 seconds");

        await svcp.StopAsync(CancellationToken.None);
    }

    // ── TC-13: Auto-watch disabled → midnight still fires and clears suppression (SC-8)

    [TestMethod]
    public async Task MidnightWake_AutoWatchDisabled_StillFiresAndClearsSuppression()
    {
        var (svc, _, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.MatchSelection, autoWatchEnabled: false);

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.AtLeastOnce,
            "suppression must be cleared regardless of auto-watch enabled state (SC-8)");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-14: Exactly midnight → delay is 24 hours ───────────────────────────

    [TestMethod]
    public async Task MidnightWake_ExactlyAtMidnight_DelaysFullDay()
    {
        var capturedDuration = TimeSpan.Zero;
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.SetupGet(a => a.CurrentState).Returns(PcsProState.MatchSelection);
        var manualMock = new Mock<IManualModeService>();
        manualMock.SetupGet(m => m.IsManualModeActive).Returns(false);
        var watcherMock = new Mock<IPlayCricketWatcherService>();
        var logger = new CapturingLogger<PlayCricketMidnightResetHostedService>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var svc = new PlayCricketMidnightResetHostedService(
            autoMock.Object, manualMock.Object, watcherMock.Object, logger,
            getNow: () => ExactMidnight,
            delayFactory: (duration, ct) =>
            {
                capturedDuration = duration;
                ct.Register(() => tcs.TrySetCanceled(ct));
                return tcs.Task;
            });

        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(50);

        capturedDuration.TotalSeconds.Should().BeInRange(86395, 86405,
            "at exactly midnight the delay should be ~24 hours (86400 seconds)");

        await svc.StopAsync(CancellationToken.None);
    }

    // ── TC-15: ClearAutoLoadSuppressions throws → error logged + re-arm continues

    [TestMethod]
    public async Task MidnightWake_SuppressionClearThrows_LogsErrorAndReArms()
    {
        var (svc, _, _, watcherMock, delays, logger) = BuildFake(
            state: PcsProState.MatchSelection);

        watcherMock.Setup(w => w.ClearAutoLoadSuppressions())
            .Throws(new InvalidOperationException("suppression store failure"));

        await svc.StartAsync(CancellationToken.None);

        var firstDelay = await ReadDelayAsync(delays);
        firstDelay.TrySetResult();

        // Re-arm must occur despite the exception
        var secondDelay = await ReadDelayAsync(delays);

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error,
            "exception from ClearAutoLoadSuppressions must be logged at Error level");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-16: ChangeMatchAsync throws OCE not from stopping token → suppression still cleared (R-3)

    [TestMethod]
    public async Task MidnightWake_ChangeMatchThrowsNonStoppingOce_SuppressionStillCleared()
    {
        var (svc, autoMock, _, watcherMock, delays, _) = BuildFake(
            state: PcsProState.MatchLoaded);

        // ChangeMatchAsync throws OCE from a different (internal) cancellation source, not from the stopping token
        var internalCts = new CancellationTokenSource();
        internalCts.Cancel();
        autoMock.Setup(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(internalCts.Token));

        await svc.StartAsync(CancellationToken.None);
        await TriggerWakeAndAwaitReArmAsync(delays);

        watcherMock.Verify(w => w.ClearAutoLoadSuppressions(), Times.AtLeastOnce,
            "suppression must be cleared even when ChangeMatchAsync throws OCE from a non-stopping source (R-3)");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }
}