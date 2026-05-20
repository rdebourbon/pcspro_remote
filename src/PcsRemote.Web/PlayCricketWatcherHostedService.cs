using Microsoft.Extensions.Options;
using PcsRemote.Core;
using PcsRemote.PlayCricket;

namespace PcsRemote.Web;

/// <summary>
/// Polls the PCS Pro match selection dialog at a configured interval and automatically
/// loads a match when exactly one scorer-started fixture is detected and auto-watch
/// conditions are satisfied (HLPS-021 SC-1, SC-10, SC-11; IS-021 S-005).
///
/// After a match is loaded, resolves the corresponding Play-Cricket fixture ID by querying
/// all configured Play-Cricket sites in parallel and matching by date and team name
/// (IS-021 S-006).
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
    private readonly IPlayCricketApiClient _apiClient;
    private readonly IReadOnlyList<int> _siteIds;
    private readonly string _clubName;
    private readonly Func<IPeriodicTimer> _timerFactory;
    private readonly ILogger<PlayCricketWatcherHostedService> _logger;
    private CancellationToken _stoppingToken;
    private CancellationTokenSource? _resolutionCts;
    private Task? _resolutionTask;

    public PlayCricketWatcherHostedService(
        IPcsProAutomationService automationService,
        IPlayCricketWatcherService watcherService,
        IManualModeService manualModeService,
        IPlayCricketApiClient apiClient,
        IOptions<PlayCricketOptions> options,
        ILogger<PlayCricketWatcherHostedService> logger)
    {
        _automationService = automationService;
        _watcherService = watcherService;
        _manualModeService = manualModeService;
        _apiClient = apiClient;
        _siteIds = options.Value.SiteIds.AsReadOnly();
        _clubName = options.Value.ClubName;
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
        _automationService.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// Test-only constructor: accepts a <see cref="IPeriodicTimer"/> factory and optional
    /// resolution parameters, bypassing config parsing and real clock.
    /// The factory is called once per <see cref="ExecuteAsync"/> invocation.
    /// </summary>
    internal PlayCricketWatcherHostedService(
        IPcsProAutomationService automationService,
        IPlayCricketWatcherService watcherService,
        IManualModeService manualModeService,
        IPlayCricketApiClient apiClient,
        Func<IPeriodicTimer> timerFactory,
        ILogger<PlayCricketWatcherHostedService> logger,
        IReadOnlyList<int>? siteIds = null,
        string clubName = "")
    {
        _automationService = automationService;
        _watcherService = watcherService;
        _manualModeService = manualModeService;
        _apiClient = apiClient;
        _siteIds = siteIds ?? [];
        _clubName = clubName;
        _timerFactory = timerFactory;
        _logger = logger;
        _automationService.StateChanged += OnStateChanged;
    }

    /// <summary>For testing only: the current in-flight fixture ID resolution task, if any.</summary>
    internal Task? ResolutionTask => _resolutionTask;

    public override void Dispose()
    {
        _automationService.StateChanged -= OnStateChanged;
        _resolutionCts?.Cancel();
        _resolutionCts?.Dispose();
        _resolutionCts = null;
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
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

    private void OnStateChanged(object? sender, PcsProState newState)
    {
        _resolutionCts?.Cancel();
        _resolutionCts?.Dispose();
        _resolutionCts = null;
        _resolutionTask = null;

        if (newState != PcsProState.MatchLoaded)
        {
            _watcherService.SetCurrentFixtureId(null);
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        _resolutionCts = cts;
        _resolutionTask = ResolveFixtureIdAsync(cts.Token);
    }

    private async Task ResolveFixtureIdAsync(CancellationToken ct)
    {
        try
        {
            await ResolveFixtureIdCoreAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled cleanly — no action needed.
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PlayCricketWatcherHostedService: Fixture ID resolution failed unexpectedly — {ExType}: {ExMessage}",
                ex.GetType().Name, ex.Message);
        }
    }

    private async Task ResolveFixtureIdCoreAsync(CancellationToken ct)
    {
        var loadedMatch = _automationService.LoadedMatch;

        if (loadedMatch == null || loadedMatch.MatchDate == default)
        {
            _logger.LogWarning(
                "PlayCricketWatcherHostedService: Fixture ID resolution skipped — loaded match date is sentinel or match is unavailable");
            return;
        }

        if (_siteIds.Count == 0)
        {
            _logger.LogWarning(
                "PlayCricketWatcherHostedService: Fixture ID resolution skipped — no Play-Cricket site IDs configured");
            return;
        }

        var tasks = _siteIds
            .Select(siteId => _apiClient.GetFixturesAsync(siteId, ct))
            .ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var matchDate = loadedMatch.MatchDate;
        var candidates = new List<PlayCricketFixture>();

        foreach (var siteFixtures in results)
        {
            foreach (var fixture in siteFixtures)
            {
                if (fixture.MatchDate != matchDate)
                    continue;

                if (IsTeamNameMatch(fixture, loadedMatch))
                    candidates.Add(fixture);
            }
        }

        if (candidates.Count != 1)
        {
            _logger.LogWarning(
                "PlayCricketWatcherHostedService: Fixture ID resolution found {CandidateCount} candidate(s) — expected exactly 1; skipping assignment",
                candidates.Count);
            return;
        }

        // Final cancellation check — prevents post-completion race where this task would set
        // the fixture ID after a subsequent state-change handler has already cleared it.
        ct.ThrowIfCancellationRequested();

        var candidate = candidates[0];
        var currentId = _watcherService.CurrentFixtureId;

        if (currentId.HasValue && currentId.Value != candidate.FixtureId)
        {
            _watcherService.CancelCountdown();
        }

        _watcherService.SetCurrentFixtureId(candidate.FixtureId);
        _logger.LogInformation(
            "PlayCricketWatcherHostedService: Fixture ID resolved to {FixtureId}",
            candidate.FixtureId);
    }

    private bool IsTeamNameMatch(PlayCricketFixture fixture, MatchInfo loadedMatch)
    {
        var fixtureHome = TeamNameFormatter.NormaliseForMatching(fixture.HomeTeam, _clubName);
        var fixtureAway = TeamNameFormatter.NormaliseForMatching(fixture.AwayTeam, _clubName);
        var loadedHome = TeamNameFormatter.NormaliseForMatching(loadedMatch.HomeTeam, _clubName);
        var loadedAway = TeamNameFormatter.NormaliseForMatching(loadedMatch.AwayTeam, _clubName);

        // Orientation A: fixture home = loaded home, fixture away = loaded away
        bool orientationA = string.Equals(fixtureHome, loadedHome, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(fixtureAway, loadedAway, StringComparison.OrdinalIgnoreCase);

        // Orientation B: fixture home = loaded away, fixture away = loaded home
        bool orientationB = string.Equals(fixtureHome, loadedAway, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(fixtureAway, loadedHome, StringComparison.OrdinalIgnoreCase);

        return orientationA || orientationB;
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
