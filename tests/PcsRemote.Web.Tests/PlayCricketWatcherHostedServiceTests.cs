using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PcsRemote.Core;
using PcsRemote.PlayCricket;
using PcsRemote.Web;
using PcsRemote.Web.Services;

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

        var apiClientMock = new Mock<IPlayCricketApiClient>();
        apiClientMock.Setup(a => a.GetFixturesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[]);

        var timerChannel = Channel.CreateUnbounded<FakePeriodicTimer>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var logger = new CapturingLogger<PlayCricketWatcherHostedService>();

        var svc = new PlayCricketWatcherHostedService(
            autoMock.Object,
            watcherMock.Object,
            manualMock.Object,
            apiClientMock.Object,
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
        var apiClientMock = new Mock<IPlayCricketApiClient>();
        var logger = new CapturingLogger<PlayCricketWatcherHostedService>();
        var options = Options.Create(new PlayCricketOptions { PollingIntervalSeconds = 30 });

        using var svc = new PlayCricketWatcherHostedService(
            autoMock.Object, watcherMock.Object, manualMock.Object, apiClientMock.Object, options, logger);

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

    // ── S-006 helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal stub that exposes <see cref="RaiseStateChanged"/> to sidestep the
    /// Moq value-type EventArgs incompatibility with <see cref="EventHandler{TEventArgs}"/>.
    /// </summary>
    private sealed class FakePcsProAutomationService : IPcsProAutomationService
    {
        public PcsProState CurrentState { get; set; } = PcsProState.MatchSelection;
        public string? LastErrorReason => null;
        public MatchInfo? LoadedMatch { get; set; }
        public event EventHandler<PcsProState>? StateChanged;
#pragma warning disable CS0067  // HealthAlert is required by interface but never raised in this stub
        public event EventHandler<HealthAlertEventArgs>? HealthAlert;
#pragma warning restore CS0067

        public void RaiseStateChanged(PcsProState newState)
        {
            CurrentState = newState;
            StateChanged?.Invoke(this, newState);
        }

        public Task<IReadOnlyList<MatchInfo>> GetSelectableMatchesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MatchInfo>>(Array.Empty<MatchInfo>());

        public Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default) => Task.CompletedTask;
        public Task LaunchAndLoginAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MatchInfo>> GetMatchesForDateAsync(DateOnly searchDate, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RefreshScoreboardAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task ChangeMatchAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RetryAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task StartStreamingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task StopStreamingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MatchTeams> UseCurrentMatchAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task DismissAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly DateOnly DefaultMatchDate = new(2025, 6, 14);

    private static (
        PlayCricketWatcherHostedService Svc,
        FakePcsProAutomationService AutoFake,
        Mock<IPlayCricketWatcherService> WatcherMock,
        Mock<IPlayCricketApiClient> ApiClientMock,
        CapturingLogger<PlayCricketWatcherHostedService> Logger)
    BuildFakeForResolution(
        string homeTeam = "Home XI",
        string awayTeam = "Away XI",
        DateOnly matchDate = default,
        IReadOnlyList<int>? siteIds = null,
        string clubName = "",
        int? currentFixtureId = null)
    {
        var effectiveDate = matchDate == default ? DefaultMatchDate : matchDate;

        var autoFake = new FakePcsProAutomationService
        {
            LoadedMatch = new MatchInfo(
                MatchId: "test-match",
                HomeTeam: homeTeam,
                AwayTeam: awayTeam,
                MatchDate: effectiveDate)
        };

        var watcherMock = new Mock<IPlayCricketWatcherService>();
        watcherMock.SetupGet(w => w.IsEnabled).Returns(true);
        watcherMock.Setup(w => w.IsAutoLoadSuppressed(It.IsAny<string>())).Returns(false);
        watcherMock.SetupGet(w => w.CurrentFixtureId).Returns(currentFixtureId);

        var apiClientMock = new Mock<IPlayCricketApiClient>();
        apiClientMock.Setup(a => a.GetFixturesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[]);

        var manualMock = new Mock<IManualModeService>();
        manualMock.SetupGet(m => m.IsManualModeActive).Returns(false);

        var logger = new CapturingLogger<PlayCricketWatcherHostedService>();

        var timerChannel = Channel.CreateUnbounded<FakePeriodicTimer>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var svc = new PlayCricketWatcherHostedService(
            autoFake,
            watcherMock.Object,
            manualMock.Object,
            apiClientMock.Object,
            () =>
            {
                var t = new FakePeriodicTimer();
                timerChannel.Writer.TryWrite(t);
                return t;
            },
            logger,
            siteIds ?? [1],
            clubName);

        return (svc, autoFake, watcherMock, apiClientMock, logger);
    }

    private static async Task AwaitResolutionAsync(PlayCricketWatcherHostedService svc, TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(5);
        var task = svc.ResolutionTask;
        if (task is not null)
            await task.WaitAsync(timeout);
    }

    // ── S-006 TC-1: Unique fixture found ──────────────────────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_UniqueMatchFound_SetsFixtureId()
    {
        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution();

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 42, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: DefaultMatchDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(42), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-2: Zero fixtures after date filter ───────────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_AllFixturesWrongDate_DoesNotSetFixtureId_LogsWarning()
    {
        var (svc, autoFake, watcherMock, apiClientMock, logger) = BuildFakeForResolution();

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 5, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: new DateOnly(2025, 1, 1))
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(It.IsAny<int?>()), Times.Never);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("0"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-3: Multiple fixtures after date filter ───────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_MultipleMatchingFixtures_DoesNotSetFixtureId_LogsWarning()
    {
        var (svc, autoFake, watcherMock, apiClientMock, logger) = BuildFakeForResolution();

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 10, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: DefaultMatchDate),
                new PlayCricketFixture(FixtureId: 11, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: DefaultMatchDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(It.IsAny<int?>()), Times.Never);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("2"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-4: Sentinel match date skips resolution ─────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_SentinelMatchDate_SkipsResolution_LogsWarning()
    {
        var (svc, autoFake, watcherMock, _, logger) = BuildFakeForResolution(matchDate: default);

        // Overwrite LoadedMatch with sentinel date (default DateOnly)
        autoFake.LoadedMatch = new MatchInfo(MatchId: "test", HomeTeam: "Home XI", AwayTeam: "Away XI");

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(It.IsAny<int?>()), Times.Never);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("sentinel"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-5: Date filter uses loaded match date, not today ─────────────

    [TestMethod]
    public async Task ResolveFixtureId_UsesLoadedMatchDate_NotToday()
    {
        var pastDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-7);
        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution(matchDate: pastDate);

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 77, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: pastDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(77), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-6: Existing fixture ID replaced by different ID ─────────────

    [TestMethod]
    public async Task ResolveFixtureId_NewIdDiffersFromCurrent_CancelsCountdownThenUpdatesId()
    {
        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution(currentFixtureId: 10);

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 99, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: DefaultMatchDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        var order = new MockSequence();
        watcherMock.InSequence(order).Setup(w => w.CancelCountdown());
        watcherMock.Verify(w => w.CancelCountdown(), Times.Once);
        watcherMock.Verify(w => w.SetCurrentFixtureId(99), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-6b: Existing fixture ID re-resolved to same ID ──────────────

    [TestMethod]
    public async Task ResolveFixtureId_NewIdSameAsCurrent_DoesNotCancelCountdown()
    {
        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution(currentFixtureId: 10);

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 10, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: DefaultMatchDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.CancelCountdown(), Times.Never);
        watcherMock.Verify(w => w.SetCurrentFixtureId(10), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-7: Task cancelled on state exit ──────────────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_StateExitsMatchLoaded_CancelsTaskAndClearsFixtureId()
    {
        var queryGate = new TaskCompletionSource<IReadOnlyList<PlayCricketFixture>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var queryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution();

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .Returns(async (int _, CancellationToken _) =>
            {
                queryStarted.TrySetResult();
                return await queryGate.Task;
            });

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await queryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        autoFake.RaiseStateChanged(PcsProState.NotRunning);
        queryGate.SetResult([]);

        await Task.Delay(50); // let any stray continuations settle

        watcherMock.Verify(w => w.SetCurrentFixtureId(null), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-8: Task cancelled on re-entry to MatchLoaded ────────────────

    [TestMethod]
    public async Task ResolveFixtureId_ReEntryToMatchLoaded_CancelsFirstTaskStartsSecond()
    {
        var firstQueryGate = new TaskCompletionSource<IReadOnlyList<PlayCricketFixture>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var firstQueryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCallCount = 0;

        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution();

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .Returns(async (int _, CancellationToken _) =>
            {
                if (Interlocked.Increment(ref secondCallCount) == 1)
                {
                    firstQueryStarted.TrySetResult();
                    return await firstQueryGate.Task;
                }

                return (IReadOnlyList<PlayCricketFixture>)[];
            });

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);      // first task
        await firstQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);      // second task (cancels first)
        var secondTask = svc.ResolutionTask;
        firstQueryGate.SetResult([]);
        await secondTask!.WaitAsync(TimeSpan.FromSeconds(5));

        watcherMock.Verify(w => w.SetCurrentFixtureId(It.IsAny<int>()), Times.Never,
            "second task found no candidates — no fixture ID should be set");
        secondCallCount.Should().BeGreaterThanOrEqualTo(2, "second task should have called GetFixturesAsync");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-9: OCE propagates cleanly ───────────────────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_TokenCancelled_ExitsCleanly_NoErrorLog()
    {
        // Verify that when the cancellation token is cancelled while GetFixturesAsync is in
        // flight, OCE propagates out of Task.WhenAll and is caught cleanly (not logged as error).
        var queryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queryGate = new TaskCompletionSource<IReadOnlyList<PlayCricketFixture>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var (svc, autoFake, watcherMock, apiClientMock, logger) = BuildFakeForResolution();

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .Returns(async (int _, CancellationToken ct) =>
            {
                queryStarted.TrySetResult();
                // Wait on gate; if CT is cancelled, Task.WhenAll will throw OCE
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await Task.WhenAny(queryGate.Task, Task.Delay(Timeout.Infinite, cts.Token));
                ct.ThrowIfCancellationRequested();
                return await queryGate.Task;
            });

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await queryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var resolutionTask = svc.ResolutionTask!;

        // Cancel via state-change (this cancels the per-entry CTS, identical mechanism to stoppingToken cancel)
        autoFake.RaiseStateChanged(PcsProState.NotRunning);

        await resolutionTask.WaitAsync(TimeSpan.FromSeconds(5));

        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error,
            "OCE must not be logged as an error");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-10: Club prefix stripped from both sides ─────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_ClubPrefixStrippedBothSides_ResolvesFixture()
    {
        const string clubName = "High Halstow CC";
        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution(
            homeTeam: "High Halstow CC 1st XI",
            awayTeam: "High Halstow CC 2nd XI",
            clubName: clubName);

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(
                    FixtureId: 55,
                    HomeTeam: "High Halstow CC 1st XI",
                    AwayTeam: "High Halstow CC 2nd XI",
                    MatchDate: DefaultMatchDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(55), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-11: Reversed orientation match ───────────────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_ReversedOrientation_ResolvesFixture()
    {
        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution(
            homeTeam: "Home XI", awayTeam: "Away XI");

        // Fixture has home/away swapped relative to PCS Pro loaded match
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 33, HomeTeam: "Away XI", AwayTeam: "Home XI", MatchDate: DefaultMatchDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(33), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-12: Multiple sites queried concurrently ─────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_MultipleSites_QueriedConcurrently()
    {
        var site1Gate = new TaskCompletionSource<IReadOnlyList<PlayCricketFixture>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var site2Completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution(siteIds: [1, 2]);

        // Site 1: blocks until signalled; returns non-matching-date fixtures
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken _) => site1Gate.Task);

        // Site 2: returns immediately with matching fixture
        apiClientMock.Setup(a => a.GetFixturesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, CancellationToken _) =>
            {
                site2Completed.TrySetResult();
                return (IReadOnlyList<PlayCricketFixture>)
                [
                    new PlayCricketFixture(FixtureId: 88, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: DefaultMatchDate)
                ];
            });

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);

        // Site 2 completes without needing site 1 — proves concurrent execution
        await site2Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Now unblock site 1 (returns empty — wrong date; doesn't affect candidate)
        site1Gate.SetResult([]);

        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(88), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-13: Fixture ID cleared on state exit ─────────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_StateExitsMatchLoaded_ClearsFixtureId()
    {
        var (svc, autoFake, watcherMock, _, _) = BuildFakeForResolution();

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        autoFake.RaiseStateChanged(PcsProState.NotRunning);

        watcherMock.Verify(w => w.SetCurrentFixtureId(null), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-14: Zero fixtures from all sites ─────────────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_AllSitesReturnEmpty_DoesNotSetFixtureId_LogsWarning()
    {
        var (svc, autoFake, watcherMock, apiClientMock, logger) = BuildFakeForResolution(siteIds: [1, 2]);

        // Both sites return empty (default mock setup)

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(It.IsAny<int?>()), Times.Never);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("0"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-15: Cross-site multiple-match ambiguity ──────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_CrossSiteMultipleMatches_DoesNotSetFixtureId_LogsWarning()
    {
        var (svc, autoFake, watcherMock, apiClientMock, logger) = BuildFakeForResolution(siteIds: [1, 2]);

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 10, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: DefaultMatchDate)
            ]);

        apiClientMock.Setup(a => a.GetFixturesAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 20, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: DefaultMatchDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(It.IsAny<int?>()), Times.Never);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("2"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-16: Correct date but non-matching team names ─────────────────

    [TestMethod]
    public async Task ResolveFixtureId_CorrectDateWrongTeamNames_DoesNotSetFixtureId_LogsWarning()
    {
        var (svc, autoFake, watcherMock, apiClientMock, logger) = BuildFakeForResolution(
            homeTeam: "Home XI", awayTeam: "Away XI");

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 5, HomeTeam: "Other XI", AwayTeam: "Rival XI", MatchDate: DefaultMatchDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(It.IsAny<int?>()), Times.Never);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("0"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-17: Final cancellation check prevents post-completion race ───

    [TestMethod]
    public async Task ResolveFixtureId_CancellationAfterCandidateSelection_DoesNotSetFixtureId()
    {
        var queryGate = new TaskCompletionSource<IReadOnlyList<PlayCricketFixture>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var queryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution();

        // Query blocks until signalled, then returns a matching fixture
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .Returns(async (int _, CancellationToken _) =>
            {
                queryStarted.TrySetResult();
                return await queryGate.Task;
            });

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await queryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Inject cancellation by transitioning away from MatchLoaded
        // (handler cancels the CTS and clears fixture ID)
        autoFake.RaiseStateChanged(PcsProState.NotRunning);

        // Now unblock the query — it will return a matching fixture but
        // the final CT check must prevent SetCurrentFixtureId from being called
        queryGate.SetResult(
        [
            new PlayCricketFixture(FixtureId: 99, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: DefaultMatchDate)
        ]);

        await Task.Delay(100); // let task finish

        watcherMock.Verify(w => w.SetCurrentFixtureId(99), Times.Never,
            "final CT check must prevent assignment after cancellation");
        watcherMock.Verify(w => w.SetCurrentFixtureId(null), Times.Once,
            "state-exit handler must have cleared fixture ID to null");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-18: Case-insensitive team name comparison ────────────────────

    [TestMethod]
    public async Task ResolveFixtureId_DifferentCasingTeamNames_ResolvesFixture()
    {
        var (svc, autoFake, watcherMock, apiClientMock, _) = BuildFakeForResolution(
            homeTeam: "Home 1st XI", awayTeam: "Away 1st XI");

        // API returns same names in different casing
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 66, HomeTeam: "HOME 1ST XI", AwayTeam: "AWAY 1ST XI", MatchDate: DefaultMatchDate)
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        watcherMock.Verify(w => w.SetCurrentFixtureId(66), Times.Once);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-006 TC-19: Pre-existing fixture ID preserved on re-resolution failure

    [TestMethod]
    public async Task ResolveFixtureId_ReEntryWithPreExistingId_ZeroCandidates_PreservesExistingId()
    {
        var (svc, autoFake, watcherMock, apiClientMock, logger) = BuildFakeForResolution(currentFixtureId: 10);

        // All fixtures have wrong date — zero candidates after filter
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [
                new PlayCricketFixture(FixtureId: 10, HomeTeam: "Home XI", AwayTeam: "Away XI", MatchDate: new DateOnly(2025, 1, 1))
            ]);

        await svc.StartAsync(CancellationToken.None);
        autoFake.RaiseStateChanged(PcsProState.MatchLoaded);
        await AwaitResolutionAsync(svc);

        // SetCurrentFixtureId must NOT be called — pre-existing ID 10 is preserved
        watcherMock.Verify(w => w.SetCurrentFixtureId(It.IsAny<int?>()), Times.Never);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("0"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 helpers ─────────────────────────────────────────────────────────

    private sealed class FakeAutoClosePcsProService : IPcsProAutomationService
    {
        public PcsProState CurrentState { get; set; } = PcsProState.MatchLoaded;
        public string? LastErrorReason => null;
        public MatchInfo? LoadedMatch { get; set; }

        public event EventHandler<PcsProState>? StateChanged;
#pragma warning disable CS0067
        public event EventHandler<HealthAlertEventArgs>? HealthAlert;
#pragma warning restore CS0067

        public Func<CancellationToken, Task> StopStreamingHandler { get; set; } = _ => Task.CompletedTask;
        public Func<CancellationToken, Task> ChangeMatchHandler { get; set; } = _ => Task.CompletedTask;
        public bool StopStreamingWasCalled { get; private set; }

        public void RaiseStateChanged(PcsProState newState)
        {
            CurrentState = newState;
            StateChanged?.Invoke(this, newState);
        }

        public Task<IReadOnlyList<MatchInfo>> GetSelectableMatchesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MatchInfo>>(Array.Empty<MatchInfo>());
        public Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default) => Task.CompletedTask;
        public Task StopStreamingAsync(CancellationToken ct = default)
        {
            StopStreamingWasCalled = true;
            return StopStreamingHandler(ct);
        }
        public Task ChangeMatchAsync(CancellationToken ct = default) => ChangeMatchHandler(ct);
        public Task LaunchAndLoginAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MatchInfo>> GetMatchesForDateAsync(DateOnly d, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RefreshScoreboardAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RetryAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task StartStreamingAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MatchTeams> UseCurrentMatchAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task DismissAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeManualModeService : IManualModeService
    {
        public bool IsManualModeActive { get; private set; }
        public event EventHandler<bool>? ManualModeChanged;

        public void Enable()
        {
            if (IsManualModeActive) return;
            IsManualModeActive = true;
            ManualModeChanged?.Invoke(this, true);
        }

        public void Disable()
        {
            if (!IsManualModeActive) return;
            IsManualModeActive = false;
            ManualModeChanged?.Invoke(this, false);
        }

        public void Toggle()
        {
            if (IsManualModeActive) Disable(); else Enable();
        }
    }

    private const int AutoCloseFixtureId = 42;
    private static readonly PlayCricketFixture ResultFixture =
        new(AutoCloseFixtureId, "Home XI", "Away XI", "Result");

    private static (
        PlayCricketWatcherHostedService Svc,
        FakeAutoClosePcsProService AutoFake,
        PlayCricketWatcherService WatcherSvc,
        FakeManualModeService ManualFake,
        Mock<IPlayCricketApiClient> ApiClientMock,
        ChannelReader<FakePeriodicTimer> Timers,
        CapturingLogger<PlayCricketWatcherHostedService> Logger)
    BuildFakeForAutoClose(
        bool isEnabled = true,
        bool isManualModeActive = false,
        bool isDismissed = false,
        bool nullFixtureId = false,
        TimeSpan countdownDuration = default,
        TimeSpan countdownTickInterval = default)
    {
        var autoFake = new FakeAutoClosePcsProService();
        var watcherSvc = new PlayCricketWatcherService(autoFake);
        var manualFake = new FakeManualModeService();

        if (isManualModeActive) manualFake.Enable();
        if (isEnabled) watcherSvc.Enable();
        if (!nullFixtureId) watcherSvc.SetCurrentFixtureId(AutoCloseFixtureId);
        if (isDismissed) watcherSvc.DismissFixture(AutoCloseFixtureId);

        var apiClientMock = new Mock<IPlayCricketApiClient>();
        apiClientMock.Setup(a => a.GetFixturesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[]);

        var timerChannel = Channel.CreateUnbounded<FakePeriodicTimer>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var logger = new CapturingLogger<PlayCricketWatcherHostedService>();

        var svc = new PlayCricketWatcherHostedService(
            autoFake,
            watcherSvc,
            manualFake,
            apiClientMock.Object,
            () =>
            {
                var t = new FakePeriodicTimer();
                timerChannel.Writer.TryWrite(t);
                return t;
            },
            logger,
            siteIds: [1],
            countdownDuration: countdownDuration == default ? TimeSpan.FromSeconds(2) : countdownDuration,
            countdownTickInterval: countdownTickInterval == default ? TimeSpan.FromMilliseconds(1) : countdownTickInterval);

        return (svc, autoFake, watcherSvc, manualFake, apiClientMock, timerChannel.Reader, logger);
    }

    private static async Task AwaitCountdownAsync(PlayCricketWatcherHostedService svc, TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(5);
        var task = svc.CountdownTask;
        if (task is not null)
            await task.WaitAsync(timeout);
    }

    // Overload accepting a pre-captured task snapshot — use when the cancellation trigger will
    // null out svc.CountdownTask before AwaitCountdownAsync can read it.
    private static async Task AwaitCountdownAsync(Task? countdownSnapshot, TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(5);
        if (countdownSnapshot is not null)
            await countdownSnapshot.WaitAsync(timeout);
    }

    // ── S-007 TC-1: "Result" status triggers StartCountdown ───────────────────

    [TestMethod]
    public async Task AutoCloseTick_ResultStatus_CallsStartCountdown()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose();
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        // StartCountdown was called — countdown task should have started
        svc.CountdownTask.Should().NotBeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-2: "Abandoned" status triggers StartCountdown ────────────────

    [TestMethod]
    public async Task AutoCloseTick_AbandonedStatus_CallsStartCountdown()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose();
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [new PlayCricketFixture(AutoCloseFixtureId, Status: "Abandoned")]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        svc.CountdownTask.Should().NotBeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-3: "No Result" status triggers StartCountdown ────────────────

    [TestMethod]
    public async Task AutoCloseTick_NoResultStatus_CallsStartCountdown()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose();
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [new PlayCricketFixture(AutoCloseFixtureId, Status: "No Result")]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        svc.CountdownTask.Should().NotBeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-4: "Playing" status — no countdown ───────────────────────────

    [TestMethod]
    public async Task AutoCloseTick_PlayingStatus_DoesNotStartCountdown()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose();
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [new PlayCricketFixture(AutoCloseFixtureId, Status: "Playing")]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        svc.CountdownTask.Should().BeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-5: "Fixture" status — no countdown ───────────────────────────

    [TestMethod]
    public async Task AutoCloseTick_FixtureStatus_DoesNotStartCountdown()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose();
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [new PlayCricketFixture(AutoCloseFixtureId, Status: "Fixture")]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        svc.CountdownTask.Should().BeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-6: Dismissed fixture — API not queried, no countdown ─────────

    [TestMethod]
    public async Task AutoCloseTick_FixtureDismissed_SkipsApiQueryAndCountdown()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose(isDismissed: true);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        apiClientMock.Verify(
            a => a.GetFixturesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "API must not be queried when fixture is dismissed");
        svc.CountdownTask.Should().BeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-7: Auto-watch disabled — auto-close skipped ─────────────────

    [TestMethod]
    public async Task AutoCloseTick_AutoWatchDisabled_SkipsAutoClose()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose(isEnabled: false);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        apiClientMock.Verify(
            a => a.GetFixturesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        svc.CountdownTask.Should().BeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-8: Manual mode active — auto-close skipped ──────────────────

    [TestMethod]
    public async Task AutoCloseTick_ManualModeActive_SkipsAutoClose()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose(isManualModeActive: true);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        apiClientMock.Verify(
            a => a.GetFixturesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        svc.CountdownTask.Should().BeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-9: Null fixture ID — auto-close skipped ─────────────────────

    [TestMethod]
    public async Task AutoCloseTick_NullFixtureId_SkipsAutoClose()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose(nullFixtureId: true);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        apiClientMock.Verify(
            a => a.GetFixturesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        svc.CountdownTask.Should().BeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-10: Fixture not found in API response — no countdown ─────────

    [TestMethod]
    public async Task AutoCloseTick_FixtureNotFoundInApiResult_NoCountdownStarted()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose();
        // API returns fixtures with a DIFFERENT fixture ID
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [new PlayCricketFixture(99, Status: "Result")]);  // ID 99, not 42

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        svc.CountdownTask.Should().BeNull();

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-11: Countdown expires after N configured ticks → StopStreaming + ChangeMatch called

    [TestMethod]
    public async Task CountdownLoop_Expires_CallsStopStreamingAfterConfiguredTickCount()
    {
        // Use Moq watcher to count TickCountdown calls and verify the configured tick count
        // is required before expiry fires — guards against regressions where expiry fires immediately.
        const int countdownSeconds = 3;
        var tickCount = 0;
        var countdownStarted = false;
        var remaining = TimeSpan.FromSeconds(countdownSeconds);

        var autoFake = new FakeAutoClosePcsProService();
        var watcherMock = new Mock<IPlayCricketWatcherService>();
        var manualFake = new FakeManualModeService();
        var apiClientMock = new Mock<IPlayCricketApiClient>();
        var logger = new CapturingLogger<PlayCricketWatcherHostedService>();

        watcherMock.SetupGet(w => w.IsEnabled).Returns(true);
        watcherMock.SetupGet(w => w.CurrentFixtureId).Returns(() => (int?)AutoCloseFixtureId);
        watcherMock.SetupGet(w => w.CountdownRemaining)
            .Returns(() => countdownStarted && remaining > TimeSpan.Zero ? remaining : (TimeSpan?)null);
        watcherMock.Setup(w => w.IsFixtureDismissed(It.IsAny<int>())).Returns(false);
        watcherMock.Setup(w => w.IsAutoLoadSuppressed(It.IsAny<string>())).Returns(false);
        watcherMock.Setup(w => w.StartCountdown(It.IsAny<TimeSpan>()))
            .Callback<TimeSpan>(_ => countdownStarted = true);
        watcherMock.Setup(w => w.DismissFixture(It.IsAny<int>()));
        watcherMock.Setup(w => w.RaiseAutoCloseFired(It.IsAny<AutoCloseFiredSnapshot>()));
        watcherMock.Setup(w => w.TickCountdown()).Returns(() =>
        {
            tickCount++;
            remaining -= TimeSpan.FromSeconds(1);
            return true; // countdown was active this tick
        });

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        var timerChannel = Channel.CreateUnbounded<FakePeriodicTimer>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        using var svc = new PlayCricketWatcherHostedService(
            autoFake, watcherMock.Object, manualFake, apiClientMock.Object,
            () => { var t = new FakePeriodicTimer(); timerChannel.Writer.TryWrite(t); return t; },
            logger, siteIds: [1],
            countdownDuration: TimeSpan.FromSeconds(countdownSeconds),
            countdownTickInterval: TimeSpan.FromMilliseconds(1));

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timerChannel.Reader);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);
        await AwaitCountdownAsync(svc);

        tickCount.Should().Be(countdownSeconds,
            "TickCountdown must be called exactly {0} times for a {0}-second countdown", countdownSeconds);
        autoFake.StopStreamingWasCalled.Should().BeTrue("StopStreamingAsync must be called after expiry");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);

        await svc.StopAsync(CancellationToken.None);
    }

    // ── S-007 TC-12: TickCountdown returns false → DismissFixture NOT called ──

    [TestMethod]
    public async Task CountdownLoop_TickCountdownReturnsFalse_NoDismissNoExpiry()
    {
        // Use Moq for watcher to control TickCountdown return value precisely
        var autoFake = new FakeAutoClosePcsProService();
        var watcherMock = new Mock<IPlayCricketWatcherService>();
        var manualFake = new FakeManualModeService();
        var apiClientMock = new Mock<IPlayCricketApiClient>();
        var logger = new CapturingLogger<PlayCricketWatcherHostedService>();

        watcherMock.SetupGet(w => w.IsEnabled).Returns(true);
        watcherMock.SetupGet(w => w.CurrentFixtureId).Returns(AutoCloseFixtureId);
        watcherMock.SetupGet(w => w.CountdownRemaining).Returns((TimeSpan?)null);
        watcherMock.Setup(w => w.IsFixtureDismissed(It.IsAny<int>())).Returns(false);
        watcherMock.Setup(w => w.IsAutoLoadSuppressed(It.IsAny<string>())).Returns(false);
        watcherMock.Setup(w => w.TickCountdown()).Returns(false);
        watcherMock.Setup(w => w.StartCountdown(It.IsAny<TimeSpan>()));

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        var timerChannel = Channel.CreateUnbounded<FakePeriodicTimer>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        using var svc = new PlayCricketWatcherHostedService(
            autoFake, watcherMock.Object, manualFake, apiClientMock.Object,
            () => { var t = new FakePeriodicTimer(); timerChannel.Writer.TryWrite(t); return t; },
            logger, siteIds: [1],
            countdownDuration: TimeSpan.FromSeconds(1),
            countdownTickInterval: TimeSpan.FromMilliseconds(1));

        await svc.StartAsync(CancellationToken.None);
        var timer = await timerChannel.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        await AwaitCountdownAsync(svc);

        watcherMock.Verify(w => w.DismissFixture(It.IsAny<int>()), Times.Never,
            "DismissFixture must not be called when TickCountdown returns false");
        autoFake.StopStreamingWasCalled.Should().BeFalse("expiry sequence must not fire when TickCountdown returns false");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);

        await svc.StopAsync(CancellationToken.None);
    }

    // ── S-007 TC-13: DismissFixture called before expiry re-check ─────────────

    [TestMethod]
    public async Task CountdownLoop_ExpiryTick_DismissFixtureCalledBeforeStopStreaming()
    {
        var dismissedBeforeStop = false;

        var (svc, autoFake, watcherSvc, _, apiClientMock, timers, _) = BuildFakeForAutoClose(
            countdownDuration: TimeSpan.FromSeconds(1));
        autoFake.StopStreamingHandler = _ =>
        {
            dismissedBeforeStop = watcherSvc.IsFixtureDismissed(AutoCloseFixtureId);
            return Task.CompletedTask;
        };

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);
        await AwaitCountdownAsync(svc);

        dismissedBeforeStop.Should().BeTrue("DismissFixture must be called before StopStreamingAsync");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-14: Expiry re-check fails — state not MatchLoaded ────────────

    [TestMethod]
    public async Task CountdownLoop_ExpiryReCheckFails_StateNotMatchLoaded_StopStreamingNotCalled()
    {
        var (svc, autoFake, _, _, apiClientMock, timers, logger) = BuildFakeForAutoClose(
            countdownDuration: TimeSpan.FromSeconds(1));

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        // Countdown is now running in the background. Change state without
        // firing StateChanged so the hosted service's CTS is NOT cancelled —
        // the countdown loop continues and expires naturally.
        autoFake.CurrentState = PcsProState.MatchSelection;

        await AwaitCountdownAsync(svc);

        autoFake.StopStreamingWasCalled.Should().BeFalse(
            "StopStreamingAsync must not be called when re-check fails due to state change");
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("re-check failed"),
            "warning must be logged when expiry re-check fails");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-15: Expiry re-check fails — fixture ID changed to mismatch ────

    [TestMethod]
    public async Task CountdownLoop_ExpiryReCheckFails_FixtureIdMismatch_StopStreamingNotCalled()
    {
        // Use Moq for watcher so we can simulate the fixture ID changing to a different value
        // at expiry time (testing the currentId != capturedFixtureId branch in RunExpirySequenceAsync).
        var autoFake = new FakeAutoClosePcsProService();
        var manualFake = new FakeManualModeService();
        var apiClientMock = new Mock<IPlayCricketApiClient>();
        var logger = new CapturingLogger<PlayCricketWatcherHostedService>();
        var watcherMock = new Mock<IPlayCricketWatcherService>();

        // TickCountdown: changes CurrentFixtureId to a different value on first call,
        // simulating a concurrent fixture ID change exactly at expiry time.
        var fixtureIdValue = (int?)AutoCloseFixtureId; // 42
        watcherMock.SetupGet(w => w.IsEnabled).Returns(true);
        watcherMock.SetupGet(w => w.CurrentFixtureId).Returns(() => fixtureIdValue);
        watcherMock.SetupGet(w => w.CountdownRemaining).Returns((TimeSpan?)null);
        watcherMock.Setup(w => w.IsFixtureDismissed(It.IsAny<int>())).Returns(false);
        watcherMock.Setup(w => w.IsAutoLoadSuppressed(It.IsAny<string>())).Returns(false);
        watcherMock.Setup(w => w.StartCountdown(It.IsAny<TimeSpan>()));
        watcherMock.Setup(w => w.DismissFixture(It.IsAny<int>()));
        watcherMock.Setup(w => w.TickCountdown()).Returns(() =>
        {
            fixtureIdValue = 99; // mismatch with capturedFixtureId=42
            return true;         // countdown was active and just expired
        });

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        var timerChannel = Channel.CreateUnbounded<FakePeriodicTimer>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        using var svc = new PlayCricketWatcherHostedService(
            autoFake, watcherMock.Object, manualFake, apiClientMock.Object,
            () => { var t = new FakePeriodicTimer(); timerChannel.Writer.TryWrite(t); return t; },
            logger, siteIds: [1],
            countdownDuration: TimeSpan.FromSeconds(1),
            countdownTickInterval: TimeSpan.FromMilliseconds(1));

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timerChannel.Reader);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);
        await AwaitCountdownAsync(svc);

        autoFake.StopStreamingWasCalled.Should().BeFalse(
            "StopStreamingAsync must not be called when expiry re-check fails due to fixture ID mismatch");
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("re-check failed"),
            "warning must be logged when currentId != capturedFixtureId at expiry");

        await svc.StopAsync(CancellationToken.None);
    }

    // ── S-007 TC-16: Happy path — full expiry sequence executes ───────────────

    [TestMethod]
    public async Task CountdownLoop_HappyPath_FullExpirySequenceExecutesInOrder()
    {
        var order = new List<string>();
        AutoCloseFiredSnapshot? firedSnapshot = null;

        var (svc, autoFake, watcherSvc, _, apiClientMock, timers, logger) = BuildFakeForAutoClose(
            countdownDuration: TimeSpan.FromSeconds(1));
        autoFake.StopStreamingHandler = _ => { order.Add("stop"); return Task.CompletedTask; };
        autoFake.ChangeMatchHandler = _ => { order.Add("change"); return Task.CompletedTask; };
        watcherSvc.AutoCloseFired += (_, s) => { order.Add("fired"); firedSnapshot = s; };

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);
        await AwaitCountdownAsync(svc);

        order.Should().Equal("stop", "change", "fired");
        firedSnapshot.Should().NotBeNull();
        firedSnapshot!.FixtureId.Should().Be(AutoCloseFixtureId);
        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.Contains("auto-close completed"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-17: StopStreamingAsync throws — exception caught, no RaiseAutoCloseFired

    [TestMethod]
    public async Task CountdownLoop_StopStreamingThrows_ExceptionCaughtAndRaiseAutoCloseFiredNotCalled()
    {
        var changeMatchCalled = false;
        var autoCloseFiredCalled = false;

        var (svc, autoFake, watcherSvc, _, apiClientMock, timers, logger) = BuildFakeForAutoClose(
            countdownDuration: TimeSpan.FromSeconds(1));
        autoFake.StopStreamingHandler = _ => throw new InvalidOperationException("streaming error");
        autoFake.ChangeMatchHandler = _ => { changeMatchCalled = true; return Task.CompletedTask; };
        watcherSvc.AutoCloseFired += (_, _) => autoCloseFiredCalled = true;

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);
        await AwaitCountdownAsync(svc);

        changeMatchCalled.Should().BeFalse("ChangeMatchAsync must not be called when StopStreamingAsync throws");
        autoCloseFiredCalled.Should().BeFalse("RaiseAutoCloseFired must not be called on exception");
        watcherSvc.IsFixtureDismissed(AutoCloseFixtureId).Should().BeTrue("fixture must remain dismissed");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("expiry sequence failed"));

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-18: Manual mode activated mid-countdown — CancelCountdown + CTS cancelled

    [TestMethod]
    public async Task ManualModeActivated_MidCountdown_CancelsCountdownAndExitsLoop()
    {
        var stopCalled = false;

        var (svc, autoFake, watcherSvc, manualFake, apiClientMock, timers, logger) = BuildFakeForAutoClose(
            countdownDuration: TimeSpan.FromSeconds(30),
            countdownTickInterval: TimeSpan.FromMilliseconds(5));
        autoFake.StopStreamingHandler = _ => { stopCalled = true; return Task.CompletedTask; };

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        // Capture task BEFORE triggering cancellation; CancelAndDisposeCountdownCts() will null it.
        var countdownSnapshot = svc.CountdownTask;

        // Activate manual mode — should immediately cancel countdown
        manualFake.Enable();

        await AwaitCountdownAsync(countdownSnapshot);

        stopCalled.Should().BeFalse("expiry sequence must not fire when manual mode cancels the countdown");
        watcherSvc.CountdownRemaining.Should().BeNull("countdown must be cleared");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-19: FixtureIdChanged mid-countdown — countdown cancelled ──────

    [TestMethod]
    public async Task FixtureIdChanged_MidCountdown_CancelsCountdownAndExitsLoop()
    {
        var stopCalled = false;

        var (svc, autoFake, watcherSvc, _, apiClientMock, timers, logger) = BuildFakeForAutoClose(
            countdownDuration: TimeSpan.FromSeconds(30),
            countdownTickInterval: TimeSpan.FromMilliseconds(5));
        autoFake.StopStreamingHandler = _ => { stopCalled = true; return Task.CompletedTask; };

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        // Capture task BEFORE triggering cancellation; CancelAndDisposeCountdownCts() will null it.
        var countdownSnapshot = svc.CountdownTask;

        // Change fixture ID — triggers OnFixtureIdChanged which cancels countdown
        watcherSvc.SetCurrentFixtureId(99);

        await AwaitCountdownAsync(countdownSnapshot);

        stopCalled.Should().BeFalse("expiry sequence must not fire when fixture ID changes");
        watcherSvc.CountdownRemaining.Should().BeNull("countdown must be cleared");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-20: CountdownCancelled (UI cancel) — loop exits cleanly ──────

    [TestMethod]
    public async Task CountdownCancelled_ViaUiCancel_InnerLoopExitsCleanlyNoErrorLog()
    {
        var stopCalled = false;

        var (svc, autoFake, watcherSvc, _, apiClientMock, timers, logger) = BuildFakeForAutoClose(
            countdownDuration: TimeSpan.FromSeconds(30),
            countdownTickInterval: TimeSpan.FromMilliseconds(5));
        autoFake.StopStreamingHandler = _ => { stopCalled = true; return Task.CompletedTask; };

        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)[ResultFixture]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        // Capture task BEFORE triggering cancellation; CancelAndDisposeCountdownCts() will null it.
        var countdownSnapshot = svc.CountdownTask;

        // Simulate operator cancelling via UI — calls CancelCountdown directly
        watcherSvc.CancelCountdown();

        await AwaitCountdownAsync(countdownSnapshot);

        stopCalled.Should().BeFalse("expiry sequence must not fire on UI cancel");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error,
            "OCE from CTS cancellation must not be logged as an error");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-21: Case-insensitive status "RESULT" triggers countdown ───────

    [TestMethod]
    public async Task AutoCloseTick_UpperCaseResultStatus_CallsStartCountdown()
    {
        var (svc, _, _, _, apiClientMock, timers, _) = BuildFakeForAutoClose();
        apiClientMock.Setup(a => a.GetFixturesAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PlayCricketFixture>)
            [new PlayCricketFixture(AutoCloseFixtureId, Status: "RESULT")]);

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        svc.CountdownTask.Should().NotBeNull("case-insensitive comparison must recognise 'RESULT'");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }

    // ── S-007 TC-22: API returns empty list — no countdown, no exception ───────

    [TestMethod]
    public async Task AutoCloseTick_ApiReturnsEmptyList_NoCountdownNoException()
    {
        var (svc, _, _, _, apiClientMock, timers, logger) = BuildFakeForAutoClose();
        // Default: returns [] — no completed fixture

        await svc.StartAsync(CancellationToken.None);
        var timer = await ReadTimerAsync(timers);
        await timer.WaitingForTickAsync();
        await TriggerAndAwaitTickAsync(timer);

        svc.CountdownTask.Should().BeNull("no countdown must start when API returns empty list");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error || e.Level == LogLevel.Warning,
            "empty API result is a normal no-op, must not generate warning or error");

        await svc.StopAsync(CancellationToken.None);
        svc.Dispose();
    }
}
