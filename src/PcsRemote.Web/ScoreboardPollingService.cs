using PcsRemote.Core;

namespace PcsRemote.Web;

/// <summary>
/// Drives periodic scoreboard capture by calling
/// <see cref="IScoreboardService.CaptureAndBroadcastAsync"/> on a <see cref="PeriodicTimer"/>.
/// Lifecycle is tied to <see cref="PcsProState.MatchLoaded"/>:
/// polling starts when the state machine enters that state and stops when it leaves.
/// </summary>
public sealed class ScoreboardPollingService : BackgroundService
{
    private readonly IScoreboardService _scoreboardService;
    private readonly IPcsProAutomationService _automationService;
    private readonly Func<IPeriodicTimer> _timerFactory;
    private readonly ILogger<ScoreboardPollingService> _logger;

    private readonly object _loopLock = new();
    private int _loopActive; // 0 = idle, 1 = active (Interlocked CAS gate)
    private CancellationTokenSource? _loopCts;
    private Task _loopTask = Task.CompletedTask;

    public ScoreboardPollingService(
        IScoreboardService scoreboardService,
        IPcsProAutomationService automationService,
        IConfiguration configuration,
        ILogger<ScoreboardPollingService> logger)
    {
        _scoreboardService = scoreboardService;
        _automationService = automationService;
        _logger = logger;

        var seconds = configuration.GetValue("Scoreboard:CaptureIntervalSeconds", defaultValue: 2);
        if (seconds <= 0)
        {
            _logger.LogWarning(
                "ScoreboardPollingService: Scoreboard:CaptureIntervalSeconds={Seconds} is invalid — falling back to 2s default",
                seconds);
            seconds = 2;
        }

        _timerFactory = () => new RealPeriodicTimer(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// Test-only constructor: accepts an <see cref="IPeriodicTimer"/> factory, bypassing
    /// config parsing and real clock. The factory is called once per loop start.
    /// </summary>
    internal ScoreboardPollingService(
        IScoreboardService scoreboardService,
        IPcsProAutomationService automationService,
        Func<IPeriodicTimer> timerFactory,
        ILogger<ScoreboardPollingService> logger)
    {
        _scoreboardService = scoreboardService;
        _automationService = automationService;
        _logger = logger;
        _timerFactory = timerFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe BEFORE reading current state (closes cold-start race window).
        _automationService.StateChanged += OnStateChanged;

        if (_automationService.CurrentState == PcsProState.MatchLoaded)
            StartLoop();

        await Task.Delay(Timeout.Infinite, stoppingToken); // OCE propagates per AC-6
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Unsubscribe FIRST to prevent a racing StateChanged from starting a new loop.
        _automationService.StateChanged -= OnStateChanged;

        await StopLoopAsync();

        await base.StopAsync(cancellationToken);
    }

    private void OnStateChanged(object? sender, PcsProState newState)
    {
        if (newState == PcsProState.MatchLoaded)
        {
            _logger.LogInformation("ScoreboardPollingService: MatchLoaded — starting capture loop");
            StartLoop();
        }
        else
        {
            _logger.LogInformation("ScoreboardPollingService: {State} — stopping capture loop", newState);
            var stopTask = StopLoopAsync();
            _ = stopTask.ContinueWith(
                t => _logger.LogError(t.Exception, "ScoreboardPollingService: StopLoopAsync faulted unexpectedly"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private void StartLoop()
    {
        // Atomic idempotency guard — only one loop may be active at a time.
        if (Interlocked.CompareExchange(ref _loopActive, 1, 0) != 0)
            return;

        // CTS creation and both field writes are atomic with respect to StopLoopAsync's locked reads.
        lock (_loopLock)
        {
            var cts = new CancellationTokenSource();
            _loopCts = cts;
            // Task.Run ensures the loop runs on a clean thread-pool thread with no
            // captured SynchronizationContext, regardless of the calling context.
            _loopTask = Task.Run(() => RunLoopAsync(cts.Token));
        }
    }

    private async Task StopLoopAsync()
    {
        CancellationTokenSource? cts;
        Task loopTask;

        lock (_loopLock)
        {
            cts = _loopCts;
            loopTask = _loopTask;
            _loopCts = null;
            _loopTask = Task.CompletedTask; // clear stale reference before releasing lock
        }

        if (cts is null)
        {
            // Loop hadn't started yet — reset active flag so future starts are not blocked.
            Interlocked.Exchange(ref _loopActive, 0);
            return;
        }

        var cleanStop = false;
        try
        {
            await cts.CancelAsync();

            try
            {
                await loopTask.WaitAsync(TimeSpan.FromSeconds(5));
                cleanStop = true;
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("ScoreboardPollingService: loop did not exit within 5s during teardown");
                // Do NOT dispose — loop task still holds loopToken. The CTS is intentionally
                // leaked until the loop task eventually exits and the GC collects the source.
            }
            catch (OperationCanceledException)
            {
                cleanStop = true; // loop exited via cancellation — safe to dispose
            }
        }
        finally
        {
            // Always reset active flag regardless of any exception path.
            Interlocked.Exchange(ref _loopActive, 0);
            if (cleanStop)
                cts.Dispose();
        }
    }

    private async Task RunLoopAsync(CancellationToken loopToken)
    {
        await using var timer = _timerFactory();

        try
        {
            while (await timer.WaitForNextTickAsync(loopToken).ConfigureAwait(false))
            {
                try
                {
                    await _scoreboardService.CaptureAndBroadcastAsync(loopToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (loopToken.IsCancellationRequested)
                {
                    // Loop is being stopped — exit cleanly.
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "ScoreboardPollingService: capture failed on tick — {ExType}: {ExMessage} — loop will continue",
                        ex.GetType().Name, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // WaitForNextTickAsync threw OCE — exit cleanly.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ScoreboardPollingService: RunLoopAsync terminated unexpectedly");
        }
    }
}
