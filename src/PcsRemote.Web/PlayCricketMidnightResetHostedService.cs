using PcsRemote.Core;

namespace PcsRemote.Web;

/// <summary>
/// Runs a midnight reset at local midnight each day (IS-021 S-008).
/// Clears all auto-load suppression records unconditionally, and returns to match selection
/// when PCS Pro is in the <see cref="PcsProState.MatchLoaded"/> state and manual mode is off.
/// Fires independently of the auto-watch enabled state (SC-8).
/// </summary>
public sealed class PlayCricketMidnightResetHostedService : BackgroundService
{
    private readonly IPcsProAutomationService _automationService;
    private readonly IManualModeService _manualModeService;
    private readonly IPlayCricketWatcherService _watcherService;
    private readonly ILogger<PlayCricketMidnightResetHostedService> _logger;
    private readonly Func<DateTimeOffset> _getNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayFactory;

    public PlayCricketMidnightResetHostedService(
        IPcsProAutomationService automationService,
        IManualModeService manualModeService,
        IPlayCricketWatcherService watcherService,
        ILogger<PlayCricketMidnightResetHostedService> logger)
    {
        _automationService = automationService;
        _manualModeService = manualModeService;
        _watcherService = watcherService;
        _logger = logger;
        _getNow = () => DateTimeOffset.Now;
        _delayFactory = Task.Delay;
    }

    /// <summary>
    /// Test-only constructor: accepts injected clock and delay factory so tests can
    /// control timing without real-clock waits.
    /// </summary>
    internal PlayCricketMidnightResetHostedService(
        IPcsProAutomationService automationService,
        IManualModeService manualModeService,
        IPlayCricketWatcherService watcherService,
        ILogger<PlayCricketMidnightResetHostedService> logger,
        Func<DateTimeOffset> getNow,
        Func<TimeSpan, CancellationToken, Task> delayFactory)
    {
        _automationService = automationService;
        _manualModeService = manualModeService;
        _watcherService = watcherService;
        _logger = logger;
        _getNow = getNow;
        _delayFactory = delayFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PlayCricketMidnightResetHostedService: started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayToNextMidnight();
            _logger.LogDebug(
                "PlayCricketMidnightResetHostedService: next reset in {DelayMinutes:F1} minutes",
                delay.TotalMinutes);

            try
            {
                await _delayFactory(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // R-7: no log entry on normal stop
                return;
            }

            await RunMidnightWakeAsync(stoppingToken);
        }
    }

    private TimeSpan ComputeDelayToNextMidnight()
    {
        var now = _getNow();
        var nextMidnight = now.Date.AddDays(1);
        var delay = nextMidnight - now.DateTime;
        return delay <= TimeSpan.Zero ? TimeSpan.FromHours(24) : delay;
    }

    private async Task RunMidnightWakeAsync(CancellationToken ct)
    {
        _logger.LogInformation("PlayCricketMidnightResetHostedService: midnight wake — running reset sequence");

        try
        {
            if (_automationService.CurrentState == PcsProState.MatchLoaded
                && !_manualModeService.IsManualModeActive)
            {
                _logger.LogInformation(
                    "PlayCricketMidnightResetHostedService: returning to match selection (state={State})",
                    _automationService.CurrentState);
                await _automationService.ChangeMatchAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopping token cancelled during match-return — exit immediately without suppression clear (R-6)
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PlayCricketMidnightResetHostedService: ChangeMatchAsync failed — {ExType}: {ExMessage}",
                ex.GetType().Name, ex.Message);
        }

        try
        {
            _watcherService.ClearAutoLoadSuppressions();
            _logger.LogDebug("PlayCricketMidnightResetHostedService: auto-load suppression records cleared");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PlayCricketMidnightResetHostedService: ClearAutoLoadSuppressions failed — {ExType}: {ExMessage}",
                ex.GetType().Name, ex.Message);
        }
    }
}
