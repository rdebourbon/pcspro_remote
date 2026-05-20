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
///
/// After fixture ID resolution, polls the Play-Cricket API each tick for match completion
/// and drives a countdown-then-close sequence when completion is detected (IS-021 S-007).
/// </summary>
public sealed class PlayCricketWatcherHostedService : BackgroundService
{
    private static readonly PcsProState[] MatchSelectionStates =
    [
        PcsProState.MatchSelection,
        PcsProState.MatchSelectionSearching,
        PcsProState.MatchSelectionReady
    ];

    private static readonly HashSet<string> CompletedStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Result", "Abandoned", "No Result" };

    private const int MinimumPollingIntervalSeconds = 60;

    private readonly IPcsProAutomationService _automationService;
    private readonly IPlayCricketWatcherService _watcherService;
    private readonly IManualModeService _manualModeService;
    private readonly IPlayCricketApiClient _apiClient;
    private readonly IReadOnlyList<int> _siteIds;
    private readonly string _clubName;
    private readonly Func<IPeriodicTimer> _timerFactory;
    private readonly ILogger<PlayCricketWatcherHostedService> _logger;
    private readonly TimeSpan _countdownDuration;
    private readonly TimeSpan _countdownTickInterval;
    private CancellationToken _stoppingToken;
    private CancellationTokenSource? _resolutionCts;
    private Task? _resolutionTask;
    private CancellationTokenSource? _countdownCts;
    private Task? _countdownTask;

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
        _countdownDuration = TimeSpan.FromSeconds(options.Value.CountdownDurationSeconds);
        _countdownTickInterval = TimeSpan.FromSeconds(1);
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
        _watcherService.FixtureIdChanged += OnFixtureIdChanged;
        _watcherService.CountdownCancelled += OnCountdownCancelled;
        _manualModeService.ManualModeChanged += OnManualModeChanged;
    }

    /// <summary>
    /// Test-only constructor: accepts a <see cref="IPeriodicTimer"/> factory and optional
    /// resolution and countdown parameters, bypassing config parsing and real clock.
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
        string clubName = "",
        TimeSpan countdownDuration = default,
        TimeSpan countdownTickInterval = default)
    {
        _automationService = automationService;
        _watcherService = watcherService;
        _manualModeService = manualModeService;
        _apiClient = apiClient;
        _siteIds = siteIds ?? [];
        _clubName = clubName;
        _countdownDuration = countdownDuration == default ? TimeSpan.FromSeconds(300) : countdownDuration;
        _countdownTickInterval = countdownTickInterval == default ? TimeSpan.FromSeconds(1) : countdownTickInterval;
        _timerFactory = timerFactory;
        _logger = logger;
        _automationService.StateChanged += OnStateChanged;
        _watcherService.FixtureIdChanged += OnFixtureIdChanged;
        _watcherService.CountdownCancelled += OnCountdownCancelled;
        _manualModeService.ManualModeChanged += OnManualModeChanged;
    }

    /// <summary>For testing only: the current in-flight fixture ID resolution task, if any.</summary>
    internal Task? ResolutionTask => _resolutionTask;

    /// <summary>For testing only: the current in-flight countdown inner loop task, if any.</summary>
    internal Task? CountdownTask => _countdownTask;

    public override void Dispose()
    {
        _automationService.StateChanged -= OnStateChanged;
        _watcherService.FixtureIdChanged -= OnFixtureIdChanged;
        _watcherService.CountdownCancelled -= OnCountdownCancelled;
        _manualModeService.ManualModeChanged -= OnManualModeChanged;
        _resolutionCts?.Cancel();
        _resolutionCts?.Dispose();
        _resolutionCts = null;
        CancelAndDisposeCountdownCts();
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
            // CancelCountdown fires CountdownCancelled synchronously if a countdown was active;
            // CancelAndDisposeCountdownCts handles CTS cleanup atomically to prevent concurrent
            // double-dispose across the four handlers that share _countdownCts.
            _watcherService.CancelCountdown();
            CancelAndDisposeCountdownCts();

            _watcherService.SetCurrentFixtureId(null);
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
        _resolutionCts = cts;
        _resolutionTask = ResolveFixtureIdAsync(cts.Token);
    }

    private void OnFixtureIdChanged(object? sender, FixtureIdChangedSnapshot snapshot)
    {
        // CancelCountdown fires CountdownCancelled synchronously if active (trigger (e) handles CTS).
        // CancelAndDisposeCountdownCts atomically swaps to null, preventing ObjectDisposedException
        // if OnStateChanged or OnManualModeChanged fire concurrently on a different thread.
        _watcherService.CancelCountdown();
        CancelAndDisposeCountdownCts();
    }

    private void OnCountdownCancelled(object? sender, EventArgs e)
    {
        // Trigger (e): countdown was cancelled (internally by Disable(), or via CancelCountdown()
        // called from another trigger). Cancel and clean up the countdown CTS atomically.
        CancelAndDisposeCountdownCts();
    }

    private void OnManualModeChanged(object? sender, bool isActive)
    {
        if (!isActive)
            return;

        // CancelCountdown fires CountdownCancelled synchronously if active (trigger (e) handles CTS).
        // CancelAndDisposeCountdownCts atomically swaps to null, preventing ObjectDisposedException
        // if OnStateChanged or OnFixtureIdChanged fire concurrently on a different thread.
        _watcherService.CancelCountdown();
        CancelAndDisposeCountdownCts();
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
        await RunAutoLoadTickAsync(ct).ConfigureAwait(false);
        await RunAutoCloseTickAsync(ct).ConfigureAwait(false);
    }

    private async Task RunAutoLoadTickAsync(CancellationToken ct)
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

    private async Task RunAutoCloseTickAsync(CancellationToken ct)
    {
        // R-2: pre-condition check
        if (!IsReadyForAutoClose())
            return;

        var fixtureId = _watcherService.CurrentFixtureId;
        if (!fixtureId.HasValue)
            return;

        if (_siteIds.Count == 0)
        {
            _logger.LogDebug(
                "PlayCricketWatcherHostedService: auto-close skipped — no Play-Cricket site IDs configured");
            return;
        }

        // R-3: query all sites in parallel for the fixture
        IReadOnlyList<PlayCricketFixture>[] results;
        try
        {
            var tasks = _siteIds
                .Select(siteId => _apiClient.GetFixturesAsync(siteId, ct))
                .ToArray();
            results = await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "PlayCricketWatcherHostedService: auto-close API query failed — skipping tick");
            return;
        }

        // R-3: locate the fixture and check for a completed status
        PlayCricketFixture? completedFixture = null;
        foreach (var siteFixtures in results)
        {
            foreach (var fixture in siteFixtures)
            {
                if (fixture.FixtureId == fixtureId.Value && IsCompletedStatus(fixture.Status))
                {
                    completedFixture = fixture;
                    break;
                }
            }
            if (completedFixture != null)
                break;
        }

        if (completedFixture == null)
            return;

        // R-3: second pre-condition re-check (all five R-2 conditions) before StartCountdown
        if (!IsReadyForAutoClose())
            return;
        if (_watcherService.CurrentFixtureId != fixtureId)
            return;

        // R-7: defensive guard — cancel any unexpectedly in-flight countdown task
        if (_countdownTask != null && !_countdownTask.IsCompleted)
        {
            _logger.LogWarning(
                "PlayCricketWatcherHostedService: unexpected concurrent countdown task for fixture {FixtureId} — cancelling prior",
                fixtureId.Value);
            _watcherService.CancelCountdown();
            CancelAndDisposeCountdownCts();
        }

        // R-4: start countdown (no-op if already active); only start inner loop for new countdowns
        var countdownAlreadyActive = _watcherService.CountdownRemaining.HasValue;
        _watcherService.StartCountdown(_countdownDuration);

        if (!countdownAlreadyActive)
        {
            var capturedId = fixtureId.Value;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _countdownCts = cts;
            _countdownTask = RunCountdownAsync(capturedId, cts.Token);
        }
    }

    private async Task RunCountdownAsync(int capturedFixtureId, CancellationToken ct)
    {
        try
        {
            await RunCountdownCoreAsync(capturedFixtureId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled cleanly — no action needed.
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PlayCricketWatcherHostedService: countdown task failed unexpectedly for fixture {FixtureId} — {ExType}: {ExMessage}",
                capturedFixtureId, ex.GetType().Name, ex.Message);
        }
    }

    private async Task RunCountdownCoreAsync(int capturedFixtureId, CancellationToken ct)
    {
        while (true)
        {
            await Task.Delay(_countdownTickInterval, ct).ConfigureAwait(false);

            var ticked = _watcherService.TickCountdown();
            if (!ticked)
                return; // Countdown was already cancelled before this tick

            if (_watcherService.CountdownRemaining == null)
            {
                // Expiry tick detected — proceed to expiry sequence (R-5)
                await RunExpirySequenceAsync(capturedFixtureId, ct).ConfigureAwait(false);
                // Expiry complete — relinquish CTS/task references explicitly.
                // OnStateChanged will also dispose the CTS when the state transition arrives,
                // but disposing here makes the expiry path self-contained (Issue 3 fix).
                CancelAndDisposeCountdownCts();
                return;
            }
            // Countdown still active — continue loop
        }
    }

    private async Task RunExpirySequenceAsync(int capturedFixtureId, CancellationToken ct)
    {
        // R-5: unconditional dismiss before re-check — prevents retry regardless of what follows
        _watcherService.DismissFixture(capturedFixtureId);

        // R-5: pre-expiry re-check (five conditions — deliberately excludes "not dismissed"
        // since DismissFixture was just called above)
        if (!_watcherService.IsEnabled
            || _manualModeService.IsManualModeActive
            || _automationService.CurrentState != PcsProState.MatchLoaded
            || _watcherService.CurrentFixtureId is not { } currentId
            || currentId != capturedFixtureId)
        {
            _logger.LogWarning(
                "PlayCricketWatcherHostedService: auto-close expiry re-check failed for fixture {FixtureId} — expiry sequence aborted; operator must manage match end manually",
                capturedFixtureId);
            return;
        }

        // R-5 steps 1–2 wrapped in exception handler
        AutoCloseFiredSnapshot? snapshot = null;
        try
        {
            await _automationService.StopStreamingAsync(ct).ConfigureAwait(false);
            await _automationService.ChangeMatchAsync(ct).ConfigureAwait(false);
            snapshot = new AutoCloseFiredSnapshot(capturedFixtureId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PlayCricketWatcherHostedService: auto-close expiry sequence failed for fixture {FixtureId} — {ExType}: {ExMessage} — operator must intervene manually (SC-9)",
                capturedFixtureId, ex.GetType().Name, ex.Message);
            return;
        }

        // R-5 step 3 — outside exception handler; subscriber exceptions propagate normally
        _watcherService.RaiseAutoCloseFired(snapshot);
        _logger.LogInformation(
            "PlayCricketWatcherHostedService: auto-close completed for fixture {FixtureId}",
            capturedFixtureId);
    }

    private bool IsReadyForAutoLoad() =>
        _watcherService.IsEnabled
        && !_manualModeService.IsManualModeActive
        && Array.IndexOf(MatchSelectionStates, _automationService.CurrentState) >= 0;

    private bool IsReadyForAutoClose()
    {
        if (!_watcherService.IsEnabled) return false;
        if (_manualModeService.IsManualModeActive) return false;
        if (_automationService.CurrentState != PcsProState.MatchLoaded) return false;
        var id = _watcherService.CurrentFixtureId;
        if (!id.HasValue) return false;
        if (_watcherService.IsFixtureDismissed(id.Value)) return false;
        return true;
    }

    /// <summary>
    /// Atomically swaps <see cref="_countdownCts"/> to <see langword="null"/> and disposes the
    /// captured reference. Also nulls <see cref="_countdownTask"/>. Using
    /// <see cref="Interlocked.Exchange{T}"/> ensures exactly one caller disposes the CTS even
    /// when multiple event handlers (OnStateChanged, OnManualModeChanged, OnFixtureIdChanged,
    /// OnCountdownCancelled) fire concurrently, preventing <see cref="ObjectDisposedException"/>
    /// on the belt-and-braces cancel calls.
    /// </summary>
    private void CancelAndDisposeCountdownCts()
    {
        Interlocked.Exchange(ref _countdownTask, null);
        var cts = Interlocked.Exchange(ref _countdownCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private static bool IsCompletedStatus(string status) => CompletedStatuses.Contains(status);
}
