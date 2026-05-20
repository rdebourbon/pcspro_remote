using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PcsRemote.Core;
using PcsRemote.PlayCricket;
using PcsRemote.Web;

namespace PcsRemote.Web.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PlayCricketWatcherHostedServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly MatchInfo FakeMatch = new(
        MatchId: "2025-06-14_Home XI_Away XI_T20",
        HomeTeam: "Home XI",
        AwayTeam: "Away XI",
        MatchType: "T20",
        MatchDate: new DateOnly(2025, 6, 14));

    private static (
        PlayCricketWatcherHostedService Svc,
        Mock<IPcsProAutomationService> AutoMock,
        Mock<IPlayCricketWatcherService> WatcherMock,
        Mock<IManualModeService> ManualMock,
        ChannelReader<FakePeriodicTimer> Timers,
        CapturingLogger<PlayCricketWatcherHostedService> Logger)
    BuildFake(
        PcsProState state = PcsProState.MatchSelection,
        bool isEnabled = true,
        bool isManualModeActive = false)
    {
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.SetupGet(a => a.CurrentState).Returns(state);
        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[]);
        autoMock.Setup(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var watcherMock = new Mock<IPlayCricketWatcherService>();
        watcherMock.SetupGet(w => w.IsEnabled).Returns(isEnabled);
        watcherMock.Setup(w => w.IsAutoLoadSuppressed(It.IsAny<string>())).Returns(false);

        var manualMock = new Mock<IManualModeService>();
        manualMock.SetupGet(m => m.IsManualModeActive).Returns(isManualModeActive);

        var timerChannel = Channel.CreateUnbounded<FakePeriodicTimer>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var logger = new CapturingLogger<PlayCricketWatcherHostedService>();

        var svc = new PlayCricketWatcherHostedService(
            autoMock.Object,
            watcherMock.Object,
            manualMock.Object,
            () =>
            {
                var t = new FakePeriodicTimer();
                timerChannel.Writer.TryWrite(t);
                return t;
            },
            logger);

        return (svc, autoMock, watcherMock, manualMock, timerChannel.Reader, logger);
    }

    private static async Task<FakePeriodicTimer> ReadTimerAsync(
        ChannelReader<FakePeriodicTimer> timers,
        TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(5);
        return await timers.ReadAsync().AsTask().WaitAsync(timeout);
    }

    /// <summary>
    /// Triggers one tick and waits for the loop to re-enter WaitForNextTickAsync,
    /// confirming the tick body has fully completed.
    /// </summary>
    private static async Task TriggerAndAwaitTickAsync(FakePeriodicTimer timer, TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(5);
        timer.TriggerTick();
        await timer.WaitingForTickAsync().WaitAsync(timeout);
    }

    // ── TC-1: Single unsuppressed match triggers load and records suppression ─

    [TestMethod]
    public async Task ExecuteAsync_WhenEnabled_SingleUnsuppressedMatch_TriggersLoadAndRecordsSuppression()
    {
        var (svc, autoMock, watcherMock, _, timers, _) = BuildFake();

        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[FakeMatch]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.LoadMatchAsync(FakeMatch, It.IsAny<CancellationToken>()), Times.Once);
        watcherMock.Verify(w => w.RecordAutoLoadSuppression(FakeMatch.MatchId), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-2: Auto-watch disabled — GetSelectableMatchesAsync not called ──────

    [TestMethod]
    public async Task ExecuteAsync_WhenAutoWatchDisabled_DoesNotCallGetSelectableMatches()
    {
        var (svc, autoMock, _, _, timers, _) = BuildFake(isEnabled: false);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()), Times.Never);
        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-3: Manual mode active — auto-load suppressed ──────────────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenManualModeActive_DoesNotTriggerAutoLoad()
    {
        var (svc, autoMock, _, _, timers, _) = BuildFake(isManualModeActive: true);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()), Times.Never);
        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-4: State is MatchLoaded — pre-condition fails ─────────────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenStateIsMatchLoaded_DoesNotTriggerAutoLoad()
    {
        var (svc, autoMock, _, _, timers, _) = BuildFake(state: PcsProState.MatchLoaded);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()), Times.Never);
        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-5: Zero matches — silent no-op, no log at any level ───────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenZeroMatchesReturned_IsNoOp()
    {
        var (svc, autoMock, _, _, timers, logger) = BuildFake();
        // GetSelectableMatchesAsync already returns [] by default in BuildFake

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        logger.Entries.Should().BeEmpty("zero-match tick must emit no log at any level");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-6: Multiple matches — warning logged, no automated action ──────────

    [TestMethod]
    public async Task ExecuteAsync_WhenMultipleMatchesReturned_LogsAmbiguityAndTakesNoAction()
    {
        var (svc, autoMock, _, _, timers, logger) = BuildFake();

        var secondMatch = FakeMatch with { MatchId = "other-match", HomeTeam = "Other XI" };
        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[FakeMatch, secondMatch]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("ambiguous"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-7: Match already suppressed — auto-load skipped ───────────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenMatchAlreadySuppressed_SkipsAutoLoad()
    {
        var (svc, autoMock, watcherMock, _, timers, _) = BuildFake();

        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[FakeMatch]);
        watcherMock.Setup(w => w.IsAutoLoadSuppressed(FakeMatch.MatchId)).Returns(true);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.RecordAutoLoadSuppression(It.IsAny<string>()), Times.Never);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-8: Pre-load re-check fails — state changed to MatchLoaded ──────────

    [TestMethod]
    public async Task ExecuteAsync_WhenPreLoadReCheckFails_StateChanged_AbortsLoad()
    {
        var (svc, autoMock, watcherMock, _, timers, logger) = BuildFake();

        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[FakeMatch]);

        // First CurrentState read (pre-condition): MatchSelection; second (re-check): MatchLoaded
        var stateCallCount = 0;
        autoMock.SetupGet(a => a.CurrentState)
            .Returns(() => ++stateCallCount <= 1 ? PcsProState.MatchSelection : PcsProState.MatchLoaded);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.RecordAutoLoadSuppression(It.IsAny<string>()), Times.Never);
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("pre-load re-check failed"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-9: GetSelectableMatchesAsync throws — warning logged, loop continues

    [TestMethod]
    public async Task ExecuteAsync_WhenGetSelectableMatchesThrows_LogsWarningAndContinues()
    {
        var (svc, autoMock, _, _, timers, logger) = BuildFake();

        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("FlaUI element not found"));

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);  // tick 1 — throws

        // Change to non-throwing for tick 2 to prove loop is still alive
        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[]);
        await TriggerAndAwaitTickAsync(timer);  // tick 2 — succeeds

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("GetSelectableMatchesAsync failed"));
        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-10: LoadMatchAsync throws — warning logged, suppression not recorded

    [TestMethod]
    public async Task ExecuteAsync_WhenLoadMatchAsyncThrows_LogsWarningAndDoesNotRecordSuppression()
    {
        var (svc, autoMock, watcherMock, _, timers, logger) = BuildFake();

        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[FakeMatch]);
        autoMock.Setup(a => a.LoadMatchAsync(FakeMatch, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Load failed"));

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("LoadMatchAsync failed"));
        watcherMock.Verify(w => w.RecordAutoLoadSuppression(It.IsAny<string>()), Times.Never);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-11: Production constructor clamps below-minimum interval ───────────

    [TestMethod]
    public void Constructor_WhenPollingIntervalBelowMinimum_LogsWarning()
    {
        var autoMock = new Mock<IPcsProAutomationService>();
        var watcherMock = new Mock<IPlayCricketWatcherService>();
        var manualMock = new Mock<IManualModeService>();
        var logger = new CapturingLogger<PlayCricketWatcherHostedService>();
        var options = Options.Create(new PlayCricketOptions { PollingIntervalSeconds = 30 });

        using var svc = new PlayCricketWatcherHostedService(
            autoMock.Object, watcherMock.Object, manualMock.Object, options, logger);

        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("30")
            && e.Message.Contains("60"));
    }

    // ── TC-12: StopAsync stops polling loop ───────────────────────────────────

    [TestMethod]
    public async Task StopAsync_StopsPollingLoop()
    {
        var (svc, _, _, _, timers, _) = BuildFake();

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();

        await svc.StopAsync(CancellationToken.None);

        await timer.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(5));

        svc.Dispose();
    }

    // ── TC-13: MatchSelectionSearching state is valid for auto-load ───────────

    [TestMethod]
    public async Task ExecuteAsync_MatchSelectionSearchingState_IsValidForAutoLoad()
    {
        var (svc, autoMock, _, _, timers, _) = BuildFake(state: PcsProState.MatchSelectionSearching);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-14: MatchSelectionReady state is valid for auto-load ──────────────

    [TestMethod]
    public async Task ExecuteAsync_MatchSelectionReadyState_IsValidForAutoLoad()
    {
        var (svc, autoMock, _, _, timers, _) = BuildFake(state: PcsProState.MatchSelectionReady);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-15: Pre-load re-check fails — IsEnabled became false ──────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenPreLoadReCheckFails_AutoWatchDisabled_AbortsLoad()
    {
        var (svc, autoMock, watcherMock, _, timers, logger) = BuildFake();

        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[FakeMatch]);

        // First IsEnabled read (pre-condition): true; second (re-check): false
        var enabledCallCount = 0;
        watcherMock.SetupGet(w => w.IsEnabled)
            .Returns(() => ++enabledCallCount <= 1);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.RecordAutoLoadSuppression(It.IsAny<string>()), Times.Never);
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("pre-load re-check failed"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-16: Pre-load re-check fails — manual mode became active ────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenPreLoadReCheckFails_ManualModeActivated_AbortsLoad()
    {
        var (svc, autoMock, watcherMock, manualMock, timers, logger) = BuildFake();

        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[FakeMatch]);

        // First IsManualModeActive read (pre-condition): false; second (re-check): true
        var callCount = 0;
        manualMock.SetupGet(m => m.IsManualModeActive)
            .Returns(() => ++callCount >= 2);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        autoMock.Verify(a => a.LoadMatchAsync(It.IsAny<MatchInfo>(), It.IsAny<CancellationToken>()), Times.Never);
        watcherMock.Verify(w => w.RecordAutoLoadSuppression(It.IsAny<string>()), Times.Never);
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("pre-load re-check failed"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-17: RecordAutoLoadSuppression throws — error logged, loop continues

    [TestMethod]
    public async Task ExecuteAsync_WhenRecordAutoLoadSuppressionThrows_LogsErrorAndContinues()
    {
        var (svc, autoMock, watcherMock, _, timers, logger) = BuildFake();

        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[FakeMatch]);
        watcherMock.Setup(w => w.RecordAutoLoadSuppression(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Suppression store failure"));

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);  // tick 1 — throws after RecordAutoLoadSuppression

        // Verify RecordAutoLoadSuppression was called (load succeeded)
        watcherMock.Verify(w => w.RecordAutoLoadSuppression(FakeMatch.MatchId), Times.Once);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);

        // Prove loop continues by triggering tick 2
        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MatchInfo>)[]);
        await TriggerAndAwaitTickAsync(timer);  // tick 2 — no exception

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── TC-18: GetSelectableMatchesAsync throws OCE — propagates, no warning ──

    [TestMethod]
    public async Task ExecuteAsync_WhenGetSelectableMatchesThrowsOperationCanceledException_Propagates()
    {
        var (svc, autoMock, _, _, timers, logger) = BuildFake();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        autoMock.Setup(a => a.GetSelectableMatchesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        timer.TriggerTick();

        // OCE propagates through the tick body — ExecuteAsync exits, disposing the timer
        await timer.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(5));

        logger.Entries.Should().NotContain(e =>
            e.Level == LogLevel.Warning || e.Level == LogLevel.Error,
            "OCE must not be swallowed and logged as a warning or error");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }
}
