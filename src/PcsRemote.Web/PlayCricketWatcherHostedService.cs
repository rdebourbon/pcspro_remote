using Microsoft.Extensions.Options;
using PcsRemote.Core;
using PcsRemote.PlayCricket;

namespace PcsRemote.Web;

/// <summary>
/// Polls the PCS Pro match selection dialog at a configured interval and automatically
/// loads a match when exactly one scorer-started fixture is detected and auto-watch
/// conditions are satisfied (HLPS-021 SC-1, SC-10, SC-11; IS-021 S-005).
/// </summary>
public sealed class PlayCricketWatcherHostedService : BackgroundService
{
    private static readonly PcsProState[] MatchSelectionStates =
    [
        PcsProState.MatchSelection,
        PcsProState.MatchSelectionSearching,
        PcsProState.MatchSelectionReady
    ];

    private const int MinimumPollingIntervalSeconds = 60;

    private readonly IPcsProAutomationService _automationService;
    private readonly IPlayCricketWatcherService _watcherService;
    private readonly IManualModeService _manualModeService;
    private readonly Func<IPeriodicTimer> _timerFactory;
    private readonly ILogger<PlayCricketWatcherHostedService> _logger;

    public PlayCricketWatcherHostedService(
        IPcsProAutomationService automationService,
        IPlayCricketWatcherService watcherService,
        IManualModeService manualModeService,
        IOptions<PlayCricketOptions> options,
        ILogger<PlayCricketWatcherHostedService> logger)
    {
        _automationService = automationService;
        _watcherService = watcherService;
        _manualModeService = manualModeService;
        _logger = logger;

        var seconds = options.Value.PollingIntervalSeconds;
        if (seconds < MinimumPollingIntervalSeconds)
        {
            _logger.LogWarning(
                "PlayCricketWatcherHostedService: PollingIntervalSeconds={Configured} is below minimum {Minimum}s — clamping",
                seconds, MinimumPollingIntervalSeconds);
            seconds = MinimumPollingIntervalSeconds;
        }

        _timerFactory = () => new RealPeriodicTimer(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// Test-only constructor: accepts a <see cref="IPeriodicTimer"/> factory, bypassing
    /// config parsing and real clock. The factory is called once per <see cref="ExecuteAsync"/> invocation.
    /// </summary>
    internal PlayCricketWatcherHostedService(
        IPcsProAutomationService automationService,
        IPlayCricketWatcherService watcherService,
        IManualModeService manualModeService,
        Func<IPeriodicTimer> timerFactory,
        ILogger<PlayCricketWatcherHostedService> logger)
    {
        _automationService = automationService;
        _watcherService = watcherService;
        _manualModeService = manualModeService;
        _timerFactory = timerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var timer = _timerFactory();

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await RunTickAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "PlayCricketWatcherHostedService: unexpected error on tick — {ExType}: {ExMessage} — loop will continue",
                        ex.GetType().Name, ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // WaitForNextTickAsync or tick body threw OCE — exit cleanly.
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        // Step 1: Pre-condition check (SC-11)
        if (!IsReadyForAutoLoad())
            return;

        // Step 2: Retrieve selectable matches
        IReadOnlyList<MatchInfo> matches;
        try
        {
            matches = await _automationService.GetSelectableMatchesAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "PlayCricketWatcherHostedService: GetSelectableMatchesAsync failed — skipping tick");
            return;
        }

        // Step 3: Zero matches — silent no-op (no log at any level)
        if (matches.Count == 0)
            return;

        // Step 4: Multiple matches — ambiguity warning (SC-10)
        if (matches.Count > 1)
        {
            _logger.LogWarning(
                "PlayCricketWatcherHostedService: {MatchCount} matches found — ambiguous, auto-load skipped (SC-10)",
                matches.Count);
            return;
        }

        // Step 5: Exactly one match — suppression check (SC-1)
        var match = matches[0];
        if (_watcherService.IsAutoLoadSuppressed(match.MatchId))
            return;

        // Step 6: Pre-load re-check
        if (!IsReadyForAutoLoad())
        {
            _logger.LogDebug(
                "PlayCricketWatcherHostedService: pre-load re-check failed — aborting auto-load");
            return;
        }

        // Step 7: Load and record suppression
        try
        {
            await _automationService.LoadMatchAsync(match, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "PlayCricketWatcherHostedService: LoadMatchAsync failed for {MatchId} — suppression not recorded",
                match.MatchId);
            return;
        }

        _watcherService.RecordAutoLoadSuppression(match.MatchId);
    }

    private bool IsReadyForAutoLoad() =>
        _watcherService.IsEnabled
        && !_manualModeService.IsManualModeActive
        && Array.IndexOf(MatchSelectionStates, _automationService.CurrentState) >= 0;
}
