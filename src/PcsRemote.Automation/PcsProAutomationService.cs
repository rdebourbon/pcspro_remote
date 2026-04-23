using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IPcsProAutomationService"/>.
/// Implements process launch, main window detection, crash watching, stop (S-002),
/// login automation (S-003), match selection (S-004), team name extraction (S-005),
/// scoreboard refresh, capture, and change-match (S-006), streaming (S-009),
/// use-current-match attach (S-011), and health-check poll (S-012).
/// </summary>
internal sealed class PcsProAutomationService : IPcsProAutomationService, IAsyncDisposable
{
    private readonly PcsProOptions _options;
    private readonly ILogger<PcsProAutomationService> _logger;
    private readonly IProcessManager _processManager;
    private readonly TimeProvider _timeProvider;
    private readonly ILoginAutomation _loginAutomation;
    private readonly IMatchSelectionAutomation _matchSelectionAutomation;
    private readonly ITeamNamesAutomation _teamNamesAutomation;
    private readonly IScoreboardAutomation _scoreboardAutomation;
    private readonly IChangeMatchAutomation _changeMatchAutomation;
    private readonly IStreamingAutomation _streamingAutomation;
    private readonly IHealthCheckAutomation _healthCheckAutomation;
    private readonly IAutomationLogService _logService;
    private readonly PcsProStateMachine _stateMachine;

    /// <summary>
    /// Guards <c>_stateMachine.Fire()</c> and <c>_lastErrorReason</c> writes.
    /// Never held across blocking I/O (process start, window polling, graceful-close wait).
    /// Crash watcher acquires via <c>WaitAsync(crashCt)</c> to enable deadlock-free
    /// cancellation by <see cref="StopAsync"/>.
    /// </summary>
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    /// <summary>
    /// State transitions enqueued inside <see cref="_operationLock"/> by <c>OnTransitioned</c>,
    /// then drained and raised outside the lock by <see cref="FlushStateChangedEvents"/>.
    /// Prevents re-entrant deadlock if a <see cref="StateChanged"/> subscriber calls back
    /// into a lifecycle method.
    /// </summary>
    private readonly ConcurrentQueue<PcsProState> _pendingTransitions = new();
    private readonly ConcurrentQueue<HealthAlertEventArgs> _pendingHealthAlerts = new();

    private IProcessHandle? _process;
    private CancellationTokenSource? _crashWatcherCts;
    private Task? _crashWatcherTask;
    private string? _lastErrorReason;
    private MatchInfo? _loadedMatch;
    private MatchInfo? _pendingLoadedMatch;

    private CancellationTokenSource? _healthPollCts;
    private Task? _healthPollTask;
    private HealthCheckResult? _lastHealthCheck;

    /// <summary>
    /// 0 = idle, 1 = a match-selection lifecycle operation is in progress.
    /// Used by GetTodaysMatchesAsync and LoadMatchAsync to prevent concurrent calls.
    /// Interlocked because these methods release <see cref="_operationLock"/> between
    /// poll iterations and a lock-held check alone cannot cover the full operation window.
    /// </summary>
    private int _isMatchSelectionOperationInProgress;

    /// <summary>
    /// 0 = idle, 1 = a team-names operation is in progress.
    /// Used by GetTeamNamesAsync to prevent concurrent calls.
    /// </summary>
    private int _isTeamNamesOperationInProgress;

    /// <summary>
    /// Serialises match-loaded FlaUI operations so only one runs at a time.
    /// Callers that can wait use <c>WaitAsync(MatchLoadedGateTimeout, ct)</c>;
    /// non-critical callers (health probe) use <c>Wait(0)</c> to skip if busy.
    /// </summary>
    private readonly SemaphoreSlim _matchLoadedGate = new(1, 1);

    private static readonly TimeSpan MatchLoadedGateTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Tracks whether a switch-user attempt has been made during the current login phase.
    /// Reset at the start of each <see cref="PerformLoginAsync"/> call (bounded to 1 retry per C-8).
    /// </summary>
    private bool _switchUserAttempted;

    public PcsProAutomationService(
        IOptions<PcsProOptions> options,
        ILogger<PcsProAutomationService> logger,
        IProcessManager processManager,
        TimeProvider timeProvider,
        AutomationDependencies automationDependencies,
        IAutomationLogService logService)
    {
        _options = options.Value;
        _logger = logger;
        _processManager = processManager;
        _timeProvider = timeProvider;
        _logService = logService;
        _loginAutomation = automationDependencies.LoginAutomation;
        _matchSelectionAutomation = automationDependencies.MatchSelectionAutomation;
        _teamNamesAutomation = automationDependencies.TeamNamesAutomation;
        _scoreboardAutomation = automationDependencies.ScoreboardAutomation;
        _changeMatchAutomation = automationDependencies.ChangeMatchAutomation;
        _streamingAutomation = automationDependencies.StreamingAutomation;
        _healthCheckAutomation = automationDependencies.HealthCheckAutomation;

        _stateMachine = new PcsProStateMachine();
        _stateMachine.OnTransitioned(newState =>
        {
            if (newState == PcsProState.MatchLoaded)
            {
                _loadedMatch = _pendingLoadedMatch;
                _pendingLoadedMatch = null;
                StartHealthPoll();
            }
            else
            {
                _loadedMatch = null;
                _pendingLoadedMatch = null;
                CancelHealthPoll();
            }

            _logger.LogInformation("State transition → {State}", newState);
            _pendingTransitions.Enqueue(newState);
        });
    }

    /// <inheritdoc/>
    public PcsProState CurrentState => _stateMachine.CurrentState;

    /// <inheritdoc/>
    public string? LastErrorReason => _lastErrorReason;

    /// <inheritdoc/>
    public MatchInfo? LoadedMatch => _loadedMatch;

    /// <inheritdoc/>
    public event EventHandler<PcsProState>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<HealthAlertEventArgs>? HealthAlert;

    /// <inheritdoc/>
    public async Task LaunchAndLoginAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "LaunchAndLoginAsync starting; {ExecutablePath}", _options.ExecutablePath);
        _logService.AddEntry("Launching PCS Pro\u2026", AutomationLogOutcome.Info);

        // Non-blocking probe: if the lock is already held, another operation is in progress.
        if (!_operationLock.Wait(0))
            throw new InvalidOperationException("A lifecycle operation is already in progress.");
        _operationLock.Release();

        KillPreExistingProcesses();

        // Fire Launch (NotRunning → Launching) before starting the process (AC-5).
        await FireUnderLockAsync(PcsProTrigger.Launch).ConfigureAwait(false);

        // Start cricket.exe (AC-4). On failure: set LastErrorReason, fire Timeout → Error (AC-6c).
        var process = await TryStartProcessAsync().ConfigureAwait(false);
        if (process is null)
            return; // Error state already set.

        // Poll for main window until detected, process exits, timeout, or caller cancels (AC-6).
        if (!await PollForMainWindowAsync(process, ct).ConfigureAwait(false))
            return; // Error state already set.

        // Fire LoginDetected (Launching → LoginScreen) (AC-19).
        await FireUnderLockAsync(PcsProTrigger.LoginDetected).ConfigureAwait(false);

        // Start crash watcher (AC-8, AC-9). Pass the local process reference to avoid
        // a null-ref race if StopAsync nulls _process while the watcher is running.
        StartCrashWatcher(process);

        // S-003: Login phase — enter credentials and wait for match selection dialog.
        _logService.AddEntry("Entering credentials\u2026", AutomationLogOutcome.Info);
        if (!await PerformLoginAsync(ct).ConfigureAwait(false))
            return;

        // Fire CredentialsEntered (LoginScreen → MatchSelection) (S-003 AC-1).
        // guardTerminal: crash watcher or StopAsync may have transitioned to Error/NotRunning
        // between PerformLoginAsync returning and this lock acquisition.
        await FireUnderLockAsync(PcsProTrigger.CredentialsEntered, guardTerminal: true).ConfigureAwait(false);

        _logService.AddEntry("Launch and login complete", AutomationLogOutcome.Success);
        _logger.LogInformation(
            "LaunchAndLoginAsync complete — service in {State}", CurrentState);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StopAsync called; current state {State}", CurrentState);
        _logService.AddEntry("Stopping PCS Pro\u2026", AutomationLogOutcome.Info);

        if (CurrentState == PcsProState.NotRunning)
            return;

        // Stop health poll BEFORE acquiring _operationLock (avoids deadlock with CancelHealthPoll).
        await StopHealthPollAsync().ConfigureAwait(false);

        // Cancel and await crash watcher BEFORE killing the process (§3.3, AC-11).
        if (_crashWatcherCts is not null)
        {
            _crashWatcherCts.Cancel();
            if (_crashWatcherTask is not null)
                await _crashWatcherTask.ConfigureAwait(false);
            _crashWatcherCts.Dispose();
            _crashWatcherCts = null;
            _crashWatcherTask = null;
        }

        // Graceful close then force-kill (§3.9, AC-12, AC-13).
        if (_process is not null)
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();

                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(PcsProStateMachine.GracefulCloseTimeoutSeconds));
                using var gracefulCts = CancellationTokenSource
                    .CreateLinkedTokenSource(ct, timeoutCts.Token);
                try
                {
                    await _process.WaitForExitAsync(gracefulCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug(
                        "Graceful close wait ended — proceeding to force-kill if needed");
                }
            }

            if (!_process.HasExited)
            {
                _process.Kill();
                _logger.LogWarning("Force-killed cricket.exe {ProcessId}", _process.Id);
            }
        }

        // Fire state-appropriate trigger sequence (§3.7).
        await _operationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            FireStopSequence();
            _lastErrorReason = null;
        }
        finally
        {
            _operationLock.Release();
        }
        FlushStateChangedEvents();

        _process?.Dispose();
        _process = null;

        _logService.AddEntry("PCS Pro stopped", AutomationLogOutcome.Success);
        _logger.LogInformation("StopAsync complete; current state {State}", CurrentState);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await StopHealthPollAsync().ConfigureAwait(false);

        if (_crashWatcherCts is not null)
        {
            _crashWatcherCts.Cancel();
            if (_crashWatcherTask is not null)
                await _crashWatcherTask.ConfigureAwait(false);
            _crashWatcherCts.Dispose();
            _crashWatcherCts = null;
        }

        _process?.Dispose();
        _process = null;
        _operationLock.Dispose();
    }

    // ---- Private helpers ---------------------------------------------------

    private void KillPreExistingProcesses()
    {
        foreach (var existing in _processManager.GetByName("cricket"))
        {
            try
            {
                existing.Kill();
                _logger.LogWarning("Killed pre-existing cricket.exe {ProcessId}", existing.Id);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                _logger.LogWarning(ex,
                    "Could not kill pre-existing cricket.exe {ProcessId}", existing.Id);
            }
            finally
            {
                existing.Dispose();
            }
        }
    }

    /// <summary>
    /// Starts cricket.exe. On failure, fires <see cref="PcsProTrigger.Timeout"/> and returns
    /// <see langword="null"/>. The <see cref="_process"/> field is NOT set here — the returned
    /// handle is stored as a local by <see cref="LaunchAndLoginAsync"/> to prevent a null-ref
    /// race with a concurrent <see cref="StopAsync"/> that nulls <see cref="_process"/>.
    /// </summary>
    private async Task<IProcessHandle?> TryStartProcessAsync()
    {
        var exePath = _options.ExecutablePath.Trim('"');
        var workDir = _options.WorkingDirectory.Trim('"');
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workDir,
            UseShellExecute = false,
        };

        try
        {
            var process = _processManager.Start(startInfo);
            _process = process;
            _logger.LogDebug("Started cricket.exe {ProcessId}", process.Id);
            return process;
        }
        catch (Exception ex)
        {
            var reason = $"Failed to start PCS Pro: {ex.Message}";
            _logger.LogError(ex, "Process start failed for {ExecutablePath}", _options.ExecutablePath);
            await FireErrorUnderLockAsync(PcsProTrigger.Timeout, reason).ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Polls <paramref name="process"/> for its main window. Uses the local handle rather than
    /// <see cref="_process"/> to prevent a null-ref race with <see cref="StopAsync"/>.
    /// Returns <see langword="true"/> when the window appears; <see langword="false"/> if the
    /// process exits, times out, or the caller cancels (error state is already set on false).
    /// </summary>
    private async Task<bool> PollForMainWindowAsync(IProcessHandle process, CancellationToken ct)
    {
        var startTimestamp = _timeProvider.GetTimestamp();
        while (true)
        {
            if (process.HasExited)
            {
                _logger.LogError(
                    "cricket.exe {ProcessId} exited before main window appeared", process.Id);
                await FireErrorUnderLockAsync(
                    PcsProTrigger.Timeout,
                    "PCS Pro exited before main window appeared").ConfigureAwait(false);
                return false;
            }

            if (process.TryGetMainWindow() is not null)
            {
                _logger.LogDebug("Main window detected for process {ProcessId}", process.Id);
                return true;
            }

            var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
            if (elapsed.TotalSeconds >= PcsProStateMachine.LaunchingTimeoutSeconds)
            {
                _logger.LogError(
                    "Timed out after {Seconds}s waiting for main window; {ExecutablePath}",
                    PcsProStateMachine.LaunchingTimeoutSeconds, _options.ExecutablePath);
                await FireErrorUnderLockAsync(
                    PcsProTrigger.Timeout,
                    "Timed out waiting for PCS Pro main window").ConfigureAwait(false);
                return false;
            }

            try
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Caller cancelled — transition to Error for consistency, then re-throw.
                await FireErrorUnderLockAsync(
                    PcsProTrigger.Timeout,
                    "Launch cancelled by caller").ConfigureAwait(false);
                throw;
            }
        }
    }

    private void StartCrashWatcher(IProcessHandle process)
    {
        _crashWatcherCts = new CancellationTokenSource();
        _crashWatcherTask = WatchForCrashAsync(process, _crashWatcherCts.Token);
    }

    /// <summary>
    /// Acquires <see cref="_operationLock"/>, fires <paramref name="trigger"/>,
    /// releases the lock, then raises any pending <see cref="StateChanged"/> events.
    /// </summary>
    /// <param name="guardTerminal">
    /// When <see langword="true"/>, silently returns if the state machine is already in a
    /// terminal state (<see cref="PcsProState.Error"/> or <see cref="PcsProState.NotRunning"/>).
    /// Use this for triggers that race with the crash watcher or <see cref="StopAsync"/>
    /// (e.g. <see cref="PcsProTrigger.CredentialsEntered"/> after login completes).
    /// </param>
    private async Task FireUnderLockAsync(PcsProTrigger trigger, bool guardTerminal = false)
    {
        await _operationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (guardTerminal &&
                _stateMachine.CurrentState is PcsProState.Error or PcsProState.NotRunning)
            {
                return;
            }

            _stateMachine.Fire(trigger);
        }
        finally
        {
            _operationLock.Release();
        }
        FlushStateChangedEvents();
    }

    /// <summary>
    /// Sets <see cref="_lastErrorReason"/>, fires <paramref name="trigger"/> under lock,
    /// then raises pending <see cref="StateChanged"/> events.
    /// </summary>
    private async Task FireErrorUnderLockAsync(PcsProTrigger trigger, string errorReason)
    {
        await _operationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            // Double-transition guard: crash watcher or a concurrent caller may already have
            // moved the state machine to Error or NotRunning. Stateless (no valid trigger from
            // those states) — silently return rather than throw InvalidOperationException.
            if (_stateMachine.CurrentState is PcsProState.Error or PcsProState.NotRunning)
                return;

            _lastErrorReason = errorReason;
            _stateMachine.Fire(trigger);
        }
        finally
        {
            _operationLock.Release();
        }
        _logService.AddEntry(errorReason, AutomationLogOutcome.Failure);
        FlushStateChangedEvents();
    }

    private void FireStopSequence()
    {
        switch (_stateMachine.CurrentState)
        {
            case PcsProState.NotRunning:
                break;

            case PcsProState.MatchLoaded:
                _stateMachine.Fire(PcsProTrigger.Stop);
                break;

            case PcsProState.Error:
                _stateMachine.Fire(PcsProTrigger.Retry);
                break;

            default:
                // Launching, LoginScreen, MatchSelection, MatchSelectionSearching, MatchSelectionReady
                _stateMachine.Fire(PcsProTrigger.Timeout);
                _stateMachine.Fire(PcsProTrigger.Retry);
                break;
        }
    }

    /// <summary>
    /// Drains <see cref="_pendingTransitions"/> and raises <see cref="StateChanged"/> for each.
    /// Must be called outside <see cref="_operationLock"/> to prevent re-entrant deadlock if a
    /// subscriber calls back into a lifecycle method synchronously.
    /// </summary>
    private void FlushStateChangedEvents()
    {
        while (_pendingTransitions.TryDequeue(out var state))
            StateChanged?.Invoke(this, state);
    }

    /// <summary>
    /// Drains <see cref="_pendingHealthAlerts"/> and raises <see cref="HealthAlert"/> for each.
    /// Must be called outside <see cref="_operationLock"/> to prevent re-entrant deadlock.
    /// </summary>
    private void FlushHealthAlertEvents()
    {
        while (_pendingHealthAlerts.TryDequeue(out var alert))
            HealthAlert?.Invoke(this, alert);
    }

    // ---- S-012: Health-check poll -----------------------------------------

    /// <summary>
    /// Starts the periodic health poll. Called from <c>OnTransitioned</c> when entering
    /// <see cref="PcsProState.MatchLoaded"/>. Fire-and-forget — the loop runs on a pooled thread.
    /// </summary>
    private void StartHealthPoll()
    {
        _healthPollCts?.Dispose();
        _lastHealthCheck = null;
        var cts = new CancellationTokenSource();
        _healthPollCts = cts;
        _healthPollTask = Task.Run(() => HealthPollLoopAsync(cts.Token));
        _logger.LogDebug("Health poll started");
    }

    /// <summary>
    /// Cancels the health poll without awaiting completion. Safe to call from the
    /// synchronous <c>OnTransitioned</c> callback that runs inside <see cref="_operationLock"/>.
    /// </summary>
    private void CancelHealthPoll()
    {
        _healthPollCts?.Cancel();
        _logger.LogDebug("Health poll cancel requested");
    }

    /// <summary>
    /// Cancels and awaits completion of the health poll, then disposes the CTS.
    /// Called from <see cref="StopAsync"/> and <see cref="DisposeAsync"/> BEFORE
    /// acquiring <see cref="_operationLock"/> to prevent deadlock.
    /// </summary>
    private async Task StopHealthPollAsync()
    {
        if (_healthPollCts is not null)
        {
            _healthPollCts.Cancel();
            if (_healthPollTask is not null)
                await _healthPollTask.ConfigureAwait(false);
            _healthPollCts.Dispose();
            _healthPollCts = null;
            _healthPollTask = null;
        }
    }

    /// <summary>
    /// Periodic poll loop that reads health signals and fires state transitions
    /// when PCS Pro signals are lost.
    /// </summary>
    private async Task HealthPollLoopAsync(CancellationToken ct)
    {
        var interval = ResolveHealthCheckInterval();
        _logger.LogInformation("Health poll loop running with interval {IntervalSeconds}s",
            (int)interval.TotalSeconds);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(interval, _timeProvider, ct).ConfigureAwait(false);

                var result = TryReadHealthProbe();
                if (result is null)
                    continue;

                var action = EvaluateHealthProbeResult(result);
                if (action == HealthProbeAction.Timeout)
                {
                    string reason = result.IsMainWindowPresent
                        ? "Match no longer loaded"
                        : "PCS Pro main window lost";
                    _logger.LogWarning("Health poll: {Reason}", reason);
                    await FireHealthTimeoutAsync(reason, result, ct).ConfigureAwait(false);
                    return;
                }

                FlushHealthAlertEvents();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — CancelHealthPoll or StopHealthPollAsync signalled.
        }

        _logger.LogDebug("Health poll loop exited");
    }

    /// <summary>
    /// Resolves and clamps the configured health-check interval to [5, 60] seconds.
    /// </summary>
    private TimeSpan ResolveHealthCheckInterval()
    {
        int intervalSeconds = _options.HealthCheckIntervalSeconds;
        if (intervalSeconds < 5 || intervalSeconds > 60)
        {
            _logger.LogWarning(
                "HealthCheckIntervalSeconds {ConfiguredValue} out of range [5,60]; clamping",
                intervalSeconds);
            intervalSeconds = Math.Clamp(intervalSeconds, 5, 60);
        }

        return TimeSpan.FromSeconds(intervalSeconds);
    }

    /// <summary>
    /// Attempts to read health signals under the CAS guard. Returns <see langword="null"/>
    /// if the guard could not be acquired or the probe failed.
    /// </summary>
    private HealthCheckResult? TryReadHealthProbe()
    {
        if (!_matchLoadedGate.Wait(0))
        {
            _logger.LogDebug("Health poll skipped — FlaUI operation in progress");
            return null;
        }

        HealthCheckResult result;
        try
        {
            result = _healthCheckAutomation.ReadHealthSignals();
        }
        finally
        {
            _matchLoadedGate.Release();
        }

        if (!result.ProbeSucceeded)
        {
            _logger.LogDebug("Health probe failed — skipping this cycle");
            return null;
        }

        return result;
    }

    private enum HealthProbeAction { Continue, Timeout }

    /// <summary>
    /// Compares a successful probe result against the previous baseline and enqueues
    /// informational alerts. Returns whether a state transition is needed.
    /// </summary>
    private HealthProbeAction EvaluateHealthProbeResult(HealthCheckResult result)
    {
        var previous = _lastHealthCheck;
        _lastHealthCheck = result;

        if (previous is not null && result.SyncStatus != previous.SyncStatus)
        {
            _pendingHealthAlerts.Enqueue(new HealthAlertEventArgs(
                HealthAlertKind.SyncStatusChanged,
                $"Sync status changed: '{previous.SyncStatus}' → '{result.SyncStatus}'",
                result));
        }

        if (!result.IsMainWindowPresent || !result.IsMatchLoaded)
            return HealthProbeAction.Timeout;

        return HealthProbeAction.Continue;
    }

    /// <summary>
    /// Acquires <see cref="_operationLock"/>, fires <see cref="PcsProTrigger.Timeout"/> if the
    /// state machine is not already in a terminal state, enqueues a <see cref="HealthAlert"/>
    /// event, and flushes both queues outside the lock.
    /// </summary>
    private async Task FireHealthTimeoutAsync(string reason, HealthCheckResult result, CancellationToken ct)
    {
        try
        {
            await _operationLock.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var state = _stateMachine.CurrentState;
            if (state is PcsProState.NotRunning or PcsProState.Error)
                return;

            _lastErrorReason = reason;

            var alertKind = result.IsMainWindowPresent
                ? HealthAlertKind.MatchLost
                : HealthAlertKind.WindowLost;

            _pendingHealthAlerts.Enqueue(new HealthAlertEventArgs(alertKind, reason, result));
            _stateMachine.Fire(PcsProTrigger.Timeout);
        }
        finally
        {
            _operationLock.Release();
        }

        FlushStateChangedEvents();
        FlushHealthAlertEvents();
        _logService.AddEntry(reason, AutomationLogOutcome.Failure);
    }

    private async Task WatchForCrashAsync(IProcessHandle process, CancellationToken crashCt)
    {
        try
        {
            await process.WaitForExitAsync(crashCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by StopAsync — intentional shutdown, not a crash.
            return;
        }

        // Process exited unexpectedly. Acquire the lock to fire the transition.
        // WaitAsync(crashCt) prevents deadlock: if StopAsync cancels our token while we
        // are waiting for the lock, OperationCanceledException exits this watcher cleanly (§3.2).
        try
        {
            await _operationLock.WaitAsync(crashCt).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var state = _stateMachine.CurrentState;
            if (state is PcsProState.NotRunning or PcsProState.Error)
                return;

            _logger.LogError(
                "cricket.exe exited unexpectedly from {State}; {Reason}",
                state, "PCS Pro exited unexpectedly");
            _lastErrorReason = "PCS Pro exited unexpectedly";
            _stateMachine.Fire(PcsProTrigger.Timeout);
        }
        finally
        {
            _operationLock.Release();
        }
        FlushStateChangedEvents();
        _logService.AddEntry("PCS Pro exited unexpectedly", AutomationLogOutcome.Failure);
    }

    /// <summary>
    /// Polls for login dialog visibility, submits credentials, and waits for match
    /// selection to appear. Returns <see langword="false"/> if the login phase ends in an
    /// error state (unexpected dialog, timeout, cancellation, or interaction exception).
    /// </summary>
    private async Task<bool> PerformLoginAsync(CancellationToken ct)
    {
        const int PollIntervalMs = 200;
        var startTime = _timeProvider.GetTimestamp();
        bool submitted = false;
        _switchUserAttempted = false;

        while (true)
        {
            if (IsInTerminalState())
                return false;

            if (await HandleUnexpectedDialogAsync().ConfigureAwait(false))
                return false;

            var submitResult = await TrySubmitCredentialsAsync(submitted, ct).ConfigureAwait(false);
            if (submitResult == LoginSubmitResult.Failed)
                return false;
            if (submitResult == LoginSubmitResult.Submitted)
            {
                submitted = true;
                continue; // skip delay — check match selection immediately
            }

            if (submitted && !_loginAutomation.IsLoginDialogVisible())
            {
                _logger.LogDebug("PerformLoginAsync: login dialog closed — login accepted");
                return true;
            }

            if (await CheckLoginTimeoutAsync(startTime, submitted).ConfigureAwait(false))
                return false;

            await DelayOrCancelAsync(PollIntervalMs, ct).ConfigureAwait(false);
        }
    }

    private enum LoginSubmitResult { NotReady, Submitted, Failed }

    /// <summary>
    /// Returns <see langword="true"/> if the state machine has already transitioned to a
    /// terminal state (crash watcher fired ahead of the login phase).
    /// </summary>
    private bool IsInTerminalState()
    {
        if (_stateMachine.CurrentState is not (PcsProState.Error or PcsProState.NotRunning))
            return false;

        _logger.LogDebug(
            "PerformLoginAsync: detected {State} — crash watcher fired first",
            _stateMachine.CurrentState);
        return true;
    }

    /// <summary>
    /// Checks for an unexpected dialog. If found, attempts close and fires the
    /// <see cref="PcsProTrigger.UnexpectedDialog"/> trigger.
    /// Returns <see langword="true"/> when the login phase should abort.
    /// </summary>
    private async Task<bool> HandleUnexpectedDialogAsync()
    {
        if (!_loginAutomation.IsUnexpectedDialogPresent())
            return false;

        var dialogName = _loginAutomation.GetUnexpectedDialogName();
        _logger.LogWarning(
            "PerformLoginAsync: unexpected dialog detected during login phase - {DialogName}",
            dialogName ?? "(unnamed)");
        _loginAutomation.TryCloseUnexpectedDialog();
        await FireErrorUnderLockAsync(
            PcsProTrigger.UnexpectedDialog,
            $"An unexpected dialog appeared during login: {dialogName ?? "(unnamed)"}").ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Attempts to enter the password and click submit when the login dialog is visible
    /// and credentials have not yet been submitted. When <c>ExpectedUsername</c> is configured,
    /// checks the pre-populated username first and performs a switch-user flow on mismatch
    /// (bounded to 1 attempt per login phase via <see cref="_switchUserAttempted"/>).
    /// After a successful switch-user, returns <see cref="LoginSubmitResult.NotReady"/> to
    /// defer submission to the next poll iteration — this allows the username to be
    /// re-verified before entering the password.
    /// Returns <see cref="LoginSubmitResult.Submitted"/> on success,
    /// <see cref="LoginSubmitResult.Failed"/> on interaction exception (error already fired),
    /// or <see cref="LoginSubmitResult.NotReady"/> when the dialog is not yet visible
    /// or a switch-user was just performed.
    /// </summary>
    private async Task<LoginSubmitResult> TrySubmitCredentialsAsync(
        bool alreadySubmitted, CancellationToken ct)
    {
        if (alreadySubmitted || !_loginAutomation.IsLoginDialogVisible())
            return LoginSubmitResult.NotReady;

        try
        {
            var mismatchResult = await TryHandleUsernameMismatchAsync(ct).ConfigureAwait(false);
            if (mismatchResult == UsernameMismatchResult.Failed)
                return LoginSubmitResult.Failed;
            if (mismatchResult == UsernameMismatchResult.SwitchPerformed)
                return LoginSubmitResult.NotReady;

            _loginAutomation.EnterPassword(_options.Password);
            _loginAutomation.ClickSubmit();
            _logger.LogDebug("PerformLoginAsync: credentials submitted");
            return LoginSubmitResult.Submitted;
        }
        // FlaUI can throw COMException, ElementNotAvailableException, and other diverse types.
        // Spec §3.8 explicitly mandates catching all exceptions from credential interaction.
        catch (Exception ex)
        {
            _logger.LogError(ex, "PerformLoginAsync: interaction error entering credentials");
            await FireErrorUnderLockAsync(
                PcsProTrigger.Timeout,
                "Login interaction failed").ConfigureAwait(false);
            return LoginSubmitResult.Failed;
        }
    }

    private enum UsernameMismatchResult { NoAction, SwitchPerformed, Failed }

    /// <summary>
    /// Checks the pre-populated username against the configured expected username.
    /// If a mismatch is detected and switch-user has not yet been attempted, performs
    /// the switch-user flow. Returns <see cref="UsernameMismatchResult.Failed"/> when
    /// the mismatch cannot be resolved (retry exhausted or interaction failure).
    /// </summary>
    private async Task<UsernameMismatchResult> TryHandleUsernameMismatchAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.ExpectedUsername))
            return UsernameMismatchResult.NoAction;

        var currentUsername = _loginAutomation.ReadUsername();
        if (string.IsNullOrEmpty(currentUsername))
        {
            _logger.LogDebug("PerformLoginAsync: username field is blank or not found — skipping check");
            return UsernameMismatchResult.NoAction;
        }

        if (string.Equals(currentUsername, _options.ExpectedUsername, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("PerformLoginAsync: username matches expected value");
            return UsernameMismatchResult.NoAction;
        }

        // Mismatch detected
        if (_switchUserAttempted)
        {
            _logger.LogError("PerformLoginAsync: username mismatch persists after switch-user attempt");
            await FireErrorUnderLockAsync(
                PcsProTrigger.Timeout,
                "Username mismatch persists after switch-user — retry exhausted").ConfigureAwait(false);
            return UsernameMismatchResult.Failed;
        }

        _logger.LogWarning("PerformLoginAsync: username mismatch detected — initiating switch-user");
        _switchUserAttempted = true;

        _loginAutomation.ClickSwitchUser();

        // Wait for the login dialog to reappear after switch-user (may briefly disappear)
        const int SwitchUserWaitMs = 200;
        const int MaxSwitchUserWaitMs = 5000;
        var switchStart = _timeProvider.GetTimestamp();

        while (_timeProvider.GetElapsedTime(switchStart).TotalMilliseconds < MaxSwitchUserWaitMs)
        {
            if (_loginAutomation.IsLoginDialogVisible())
            {
                _logger.LogDebug("PerformLoginAsync: login dialog reappeared after switch-user");
                break;
            }

            await Task.Delay(SwitchUserWaitMs, ct).ConfigureAwait(false);
        }

        if (!_loginAutomation.IsLoginDialogVisible())
        {
            _logger.LogError(
                "PerformLoginAsync: login dialog did not reappear after switch-user within {TimeoutMs}ms",
                MaxSwitchUserWaitMs);
            await FireErrorUnderLockAsync(
                PcsProTrigger.Timeout,
                "Switch-user completed but login dialog did not reappear").ConfigureAwait(false);
            return UsernameMismatchResult.Failed;
        }

        _loginAutomation.EnterUsername(_options.ExpectedUsername);
        return UsernameMismatchResult.SwitchPerformed;
    }

    /// <summary>
    /// Checks whether the login phase has exceeded its timeout.
    /// Fires <see cref="PcsProTrigger.Timeout"/> if elapsed.
    /// Returns <see langword="true"/> when the login phase should abort.
    /// </summary>
    private async Task<bool> CheckLoginTimeoutAsync(long startTimestamp, bool submitted)
    {
        if (_timeProvider.GetElapsedTime(startTimestamp).TotalSeconds
            < _options.LoginScreenTimeoutSeconds)
        {
            return false;
        }

        var reason = submitted
            ? $"Login dialog did not close within {_options.LoginScreenTimeoutSeconds} seconds after submitting credentials"
            : $"Login screen did not appear within {_options.LoginScreenTimeoutSeconds} seconds";

        _logger.LogWarning("PerformLoginAsync: timeout — {Reason}", reason);
        await FireErrorUnderLockAsync(PcsProTrigger.Timeout, reason).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Awaits the poll delay. On <see cref="OperationCanceledException"/> fires
    /// <see cref="PcsProTrigger.Timeout"/> and re-throws so that
    /// <see cref="LaunchAndLoginAsync"/> propagates the cancellation to the caller.
    /// </summary>
    private async Task DelayOrCancelAsync(int delayMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PerformLoginAsync: cancelled during poll delay");
            await FireErrorUnderLockAsync(
                PcsProTrigger.Timeout,
                "Login phase cancelled by caller").ConfigureAwait(false);
            throw;
        }
    }

    // ---- S-004: Match selection automation ----------------------------------

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("GetTodaysMatchesAsync starting; current state {State}", CurrentState);

        if (Interlocked.CompareExchange(ref _isMatchSelectionOperationInProgress, 1, 0) != 0)
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            return await GetTodaysMatchesCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _isMatchSelectionOperationInProgress, 0);
        }
    }

    private async Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesCoreAsync(CancellationToken ct)
    {
        var startTimestamp = _timeProvider.GetTimestamp();

        try
        {
            _matchSelectionAutomation.OpenMatchDialogAndSearch();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTodaysMatchesAsync: interaction error opening match dialog");
            await FireErrorUnderLockAsync(
                PcsProTrigger.Timeout,
                "Match selection interaction failed").ConfigureAwait(false);
            return [];
        }

        // SearchTriggered: MatchSelection → MatchSelectionSearching
        // guardTerminal: crash watcher may have fired between OpenMatchDialogAndSearch and here.
        await FireUnderLockAsync(PcsProTrigger.SearchTriggered, guardTerminal: true).ConfigureAwait(false);
        if (_stateMachine.CurrentState is PcsProState.Error or PcsProState.NotRunning)
            return [];

        // 200ms guard: PCS Pro may not have started the spinner immediately.
        try
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await FireErrorUnderLockAsync(
                PcsProTrigger.Timeout,
                "Match search cancelled by caller").ConfigureAwait(false);
            throw;
        }

        // Poll until spinner clears, timeout, cancellation, or terminal state.
        return await PollForSpinnerAndParseAsync(startTimestamp, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<MatchInfo>> PollForSpinnerAndParseAsync(
        long startTimestamp,
        CancellationToken ct)
    {
        const int PollIntervalMs = 200;

        while (true)
        {
            if (_stateMachine.CurrentState is PcsProState.Error or PcsProState.NotRunning)
                return [];

            if (_matchSelectionAutomation.IsUnexpectedDialogPresent())
            {
                _logger.LogWarning("GetTodaysMatchesAsync: unexpected dialog during spinner wait");
                _matchSelectionAutomation.TryCloseUnexpectedDialog();
                await FireErrorUnderLockAsync(
                    PcsProTrigger.UnexpectedDialog,
                    "An unexpected dialog appeared during match search").ConfigureAwait(false);
                return [];
            }

            if (!_matchSelectionAutomation.IsSpinnerVisible())
            {
                _logger.LogDebug("GetTodaysMatchesAsync: spinner cleared — reading DataGrid");
                return await ParseAndFilterMatchesAsync(startTimestamp, ct).ConfigureAwait(false);
            }

            if (_timeProvider.GetElapsedTime(startTimestamp).TotalSeconds
                >= PcsProStateMachine.MatchSelectionSearchingTimeoutSeconds)
            {
                var reason =
                    $"Match search did not complete within " +
                    $"{PcsProStateMachine.MatchSelectionSearchingTimeoutSeconds} seconds";
                _logger.LogWarning("GetTodaysMatchesAsync: timeout — {Reason}", reason);
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, reason).ConfigureAwait(false);
                return [];
            }

            try
            {
                await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await FireErrorUnderLockAsync(
                    PcsProTrigger.Timeout,
                    "Match search cancelled by caller").ConfigureAwait(false);
                throw;
            }
        }
    }

    private async Task<IReadOnlyList<MatchInfo>> ParseAndFilterMatchesAsync(
        long startTimestamp,
        CancellationToken ct)
    {
        // SpinnerGone: MatchSelectionSearching → MatchSelectionReady
        // guardTerminal: crash watcher may have fired while spinner was polling.
        await FireUnderLockAsync(PcsProTrigger.SpinnerGone, guardTerminal: true).ConfigureAwait(false);
        if (_stateMachine.CurrentState is PcsProState.Error or PcsProState.NotRunning)
            return [];

        IReadOnlyList<string> rowTexts;
        try
        {
            rowTexts = _matchSelectionAutomation.ReadDataGridRowTexts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTodaysMatchesAsync: interaction error reading DataGrid");
            await FireErrorUnderLockAsync(
                PcsProTrigger.Timeout,
                "Match selection interaction failed").ConfigureAwait(false);
            return [];
        }

        var parsed = new List<MatchInfo>();
        foreach (var rowText in rowTexts)
        {
            if (MatchRowParser.TryParse(rowText, out var match))
                parsed.Add(match! /* non-null when TryParse returns true — out parameter contract */);
            else
                _logger.LogWarning("GetTodaysMatchesAsync: could not parse row text {RowText}", rowText);
        }

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var filtered = MatchRowParser.FilterToday(parsed, today);

        if (filtered.Count == 0)
        {
            const string NoMatchesReason = "No matches found for today";
            _logger.LogWarning("GetTodaysMatchesAsync: {Reason}", NoMatchesReason);
            await FireErrorUnderLockAsync(PcsProTrigger.Timeout, NoMatchesReason).ConfigureAwait(false);
            return [];
        }

        _logger.LogInformation(
            "GetTodaysMatchesAsync complete — {MatchCount} match(es) found", filtered.Count);
        return filtered;
    }

    /// <inheritdoc/>
    public async Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "LoadMatchAsync starting; MatchId={MatchId}, current state {State}",
            match.MatchId, CurrentState);
        _logService.AddEntry("Loading match\u2026", AutomationLogOutcome.Info);

        if (Interlocked.CompareExchange(ref _isMatchSelectionOperationInProgress, 1, 0) != 0)
            throw new InvalidOperationException("A lifecycle operation is already in progress.");

        try
        {
            await LoadMatchCoreAsync(match, ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _isMatchSelectionOperationInProgress, 0);
        }
    }

    private async Task LoadMatchCoreAsync(MatchInfo match, CancellationToken ct)
    {
        var startTimestamp = _timeProvider.GetTimestamp();

        try
        {
            _matchSelectionAutomation.SelectAndOpenMatch(match);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LoadMatchAsync: interaction error selecting match");
            await FireErrorUnderLockAsync(
                PcsProTrigger.Timeout,
                "Match selection interaction failed").ConfigureAwait(false);
            return;
        }

        _pendingLoadedMatch = FormatMatchForTitle(match);
        await PollForMatchLoadedAsync(startTimestamp, ct).ConfigureAwait(false);
    }

    private async Task PollForMatchLoadedAsync(long startTimestamp, CancellationToken ct)
    {
        const int PollIntervalMs = 200;

        while (true)
        {
            if (_stateMachine.CurrentState is PcsProState.Error or PcsProState.NotRunning)
                return;

            if (_matchSelectionAutomation.IsUnexpectedDialogPresent())
            {
                _logger.LogWarning("LoadMatchAsync: unexpected dialog during open wait");
                _matchSelectionAutomation.TryCloseUnexpectedDialog();
                await FireErrorUnderLockAsync(
                    PcsProTrigger.UnexpectedDialog,
                    "An unexpected dialog appeared while opening match").ConfigureAwait(false);
                return;
            }

            if (_matchSelectionAutomation.IsMatchLoaded())
            {
                _logger.LogDebug("LoadMatchAsync: match loaded");

                // MatchOpened: MatchSelectionReady → MatchLoaded
                // guardTerminal: crash watcher may have fired between load detection and here.
                await FireUnderLockAsync(PcsProTrigger.MatchOpened, guardTerminal: true)
                    .ConfigureAwait(false);

                _logService.AddEntry("Match loaded", AutomationLogOutcome.Success);
                _logger.LogInformation("LoadMatchAsync complete — service in {State}", CurrentState);
                return;
            }

            if (_timeProvider.GetElapsedTime(startTimestamp).TotalSeconds
                >= PcsProStateMachine.MatchSelectionReadyTimeoutSeconds)
            {
                var reason =
                    $"Match did not open within " +
                    $"{PcsProStateMachine.MatchSelectionReadyTimeoutSeconds} seconds";
                _logger.LogWarning("LoadMatchAsync: timeout — {Reason}", reason);
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, reason).ConfigureAwait(false);
                return;
            }

            try
            {
                await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await FireErrorUnderLockAsync(
                    PcsProTrigger.Timeout,
                    "Match open cancelled by caller").ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("GetTeamNamesAsync starting; current state {State}", CurrentState);

        if (Interlocked.CompareExchange(ref _isTeamNamesOperationInProgress, 1, 0) != 0)
            throw new InvalidOperationException(
                "A GetTeamNamesAsync operation is already in progress.");

        try
        {
            return await GetTeamNamesCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _isTeamNamesOperationInProgress, 0);
        }
    }

    private async Task<MatchTeams> GetTeamNamesCoreAsync(CancellationToken ct)
    {
        if (_stateMachine.CurrentState != PcsProState.MatchLoaded)
            throw new InvalidOperationException(
                $"GetTeamNamesAsync requires state {PcsProState.MatchLoaded} " +
                $"but current state is {_stateMachine.CurrentState}.");

        try
        {
            ct.ThrowIfCancellationRequested();

            // §4.2 — Open the teams dialog.
            try
            {
                _teamNamesAutomation.OpenTeamsDialog();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "GetTeamNamesAsync: failed to open teams dialog");
                _teamNamesAutomation.TryCloseTeamsDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "Teams dialog failed to open").ConfigureAwait(false);
                return new MatchTeams(
                    new TeamNameInfo(string.Empty, string.Empty),
                    new TeamNameInfo(string.Empty, string.Empty));
            }

            ct.ThrowIfCancellationRequested();

            // §4.3 — Check for an unexpected dialog before reading.
            if (_teamNamesAutomation.IsUnexpectedDialogPresent())
            {
                _logger.LogWarning("GetTeamNamesAsync: unexpected dialog detected after opening teams dialog");
                _teamNamesAutomation.TryCloseUnexpectedDialog();
                _teamNamesAutomation.TryCloseTeamsDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.UnexpectedDialog, "Unexpected dialog blocked team name extraction").ConfigureAwait(false);
                return new MatchTeams(
                    new TeamNameInfo(string.Empty, string.Empty),
                    new TeamNameInfo(string.Empty, string.Empty));
            }

            // §4.4 — Read home and away team names.
            TeamNameInfo homeTeam;
            TeamNameInfo awayTeam;
            try
            {
                homeTeam = _teamNamesAutomation.ReadHomeTeamName();
                awayTeam = _teamNamesAutomation.ReadAwayTeamName();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "GetTeamNamesAsync: failed to read team names");
                _teamNamesAutomation.TryCloseTeamsDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "Team names could not be read").ConfigureAwait(false);
                return new MatchTeams(
                    new TeamNameInfo(string.Empty, string.Empty),
                    new TeamNameInfo(string.Empty, string.Empty));
            }

            // §4.5 — Close the dialog and return on success.
            _teamNamesAutomation.TryCloseTeamsDialog();
            _logger.LogInformation(
                "GetTeamNamesAsync succeeded — Home={HomeClub}/{HomeTeam} Away={AwayClub}/{AwayTeam}",
                homeTeam.ClubName, homeTeam.TeamName, awayTeam.ClubName, awayTeam.TeamName);

            // S-004 — Enrich LoadedMatch with club names, ordered to match S-003's team reorder.
            var current = _loadedMatch;
            if (current is not null)
            {
                var (firstClub, secondClub) = TeamNameFormatter.OrderClubNamesForTitle(
                    homeTeam.ClubName, awayTeam.ClubName, _options.ClubName);
                _loadedMatch = current with { HomeClub = firstClub, AwayClub = secondClub };
            }

            return new MatchTeams(homeTeam, awayTeam);
        }
        catch (OperationCanceledException)
        {
            // §4.6 — Cancellation: best-effort cleanup, log at Warning, re-throw.
            _logger.LogWarning("GetTeamNamesAsync cancelled");
            _teamNamesAutomation.TryCloseTeamsDialog();
            await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "GetTeamNamesAsync was cancelled").ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task RefreshScoreboardAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("RefreshScoreboardAsync starting; current state {State}", CurrentState);

        if (!await _matchLoadedGate.WaitAsync(MatchLoadedGateTimeout, ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                "Timed out waiting for a match-loaded operation to complete.");

        try
        {
            await RefreshScoreboardCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _matchLoadedGate.Release();
        }
    }

    private async Task RefreshScoreboardCoreAsync(CancellationToken ct)
    {
        if (_stateMachine.CurrentState != PcsProState.MatchLoaded)
            throw new InvalidOperationException(
                $"RefreshScoreboardAsync requires state {PcsProState.MatchLoaded} " +
                $"but current state is {_stateMachine.CurrentState}.");

        try
        {
            // §4.1.1 — Entry unexpected-dialog check.
            if (_scoreboardAutomation.IsUnexpectedDialogPresent())
            {
                _logger.LogWarning("RefreshScoreboardAsync: unexpected dialog detected at entry");
                _scoreboardAutomation.TryCloseUnexpectedDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.UnexpectedDialog, "Unexpected dialog blocked scoreboard refresh").ConfigureAwait(false);
                return;
            }

            ct.ThrowIfCancellationRequested();

            // §4.1.2 — Click settings cog.
            try
            {
                _scoreboardAutomation.ClickSettingsCog();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "RefreshScoreboardAsync: failed to open settings menu");
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "Failed to open settings menu").ConfigureAwait(false);
                return;
            }

            // §4.1.3 — Click refresh menu item.
            try
            {
                _scoreboardAutomation.ClickRefreshAllScoreboards();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "RefreshScoreboardAsync: failed to click Refresh All Scoreboards");
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "Failed to click Refresh All Scoreboards").ConfigureAwait(false);
                return;
            }

            _logger.LogInformation("RefreshScoreboardAsync complete");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("RefreshScoreboardAsync cancelled");
            await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "RefreshScoreboardAsync was cancelled").ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("CaptureScoreboardImageAsync starting; current state {State}", CurrentState);

        bool acquired;
        try
        {
            acquired = await _matchLoadedGate.WaitAsync(MatchLoadedGateTimeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("CaptureScoreboardImageAsync skipped — cancelled while waiting for the gate");
            return Array.Empty<byte>();
        }

        if (!acquired)
        {
            _logger.LogDebug("CaptureScoreboardImageAsync skipped — another operation holds the gate");
            return Array.Empty<byte>();
        }

        try
        {
            return await CaptureScoreboardImageCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _matchLoadedGate.Release();
        }
    }

    private async Task<byte[]> CaptureScoreboardImageCoreAsync(CancellationToken ct)
    {
        if (_stateMachine.CurrentState != PcsProState.MatchLoaded)
            throw new InvalidOperationException(
                $"CaptureScoreboardImageAsync requires state {PcsProState.MatchLoaded} " +
                $"but current state is {_stateMachine.CurrentState}.");

        try
        {
            // §4.2.1 — Entry unexpected-dialog check.
            if (_scoreboardAutomation.IsUnexpectedDialogPresent())
            {
                _logger.LogWarning("CaptureScoreboardImageAsync: unexpected dialog detected at entry");
                _scoreboardAutomation.TryCloseUnexpectedDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.UnexpectedDialog, "Unexpected dialog blocked scoreboard capture").ConfigureAwait(false);
                return Array.Empty<byte>();
            }

            ct.ThrowIfCancellationRequested();

            // §4.2.2 — Capture image.
            byte[] bytes;
            try
            {
                bytes = _scoreboardAutomation.CaptureScoreboardImage();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "CaptureScoreboardImageAsync: failed to capture scoreboard image");
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "Failed to capture scoreboard image").ConfigureAwait(false);
                return Array.Empty<byte>();
            }

            _logger.LogInformation("CaptureScoreboardImageAsync complete — {ByteCount} bytes", bytes.Length);
            return bytes;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("CaptureScoreboardImageAsync cancelled");
            await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "CaptureScoreboardImageAsync was cancelled").ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ChangeMatchAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("ChangeMatchAsync starting; current state {State}", CurrentState);
        _logService.AddEntry("Changing match\u2026", AutomationLogOutcome.Info);

        if (!await _matchLoadedGate.WaitAsync(MatchLoadedGateTimeout, ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                "Timed out waiting for a match-loaded operation to complete.");

        try
        {
            await ChangeMatchCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _matchLoadedGate.Release();
        }
    }

    private async Task ChangeMatchCoreAsync(CancellationToken ct)
    {
        if (_stateMachine.CurrentState != PcsProState.MatchLoaded)
            throw new InvalidOperationException(
                $"ChangeMatchAsync requires state {PcsProState.MatchLoaded} " +
                $"but current state is {_stateMachine.CurrentState}.");

        try
        {
            // §4.3.1 — Entry unexpected-dialog check.
            if (_changeMatchAutomation.IsUnexpectedDialogPresent())
            {
                _logger.LogWarning("ChangeMatchAsync: unexpected dialog detected at entry");
                _changeMatchAutomation.TryCloseUnexpectedDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.UnexpectedDialog, "Unexpected dialog blocked match change").ConfigureAwait(false);
                return;
            }

            ct.ThrowIfCancellationRequested();

            // §4.3.2 — Execute change-match sequence.
            try
            {
                _changeMatchAutomation.ExecuteChangeMatchSequence();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ChangeMatchAsync: change match sequence failed");
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "Change match sequence failed").ConfigureAwait(false);
                return;
            }

            // §4.3.3 — Fire state transition.
            await FireUnderLockAsync(PcsProTrigger.ChangeMatch, guardTerminal: true).ConfigureAwait(false);
            _logService.AddEntry("Returned to match selection", AutomationLogOutcome.Success);
            _logger.LogInformation("ChangeMatchAsync complete — state {State}", CurrentState);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ChangeMatchAsync cancelled");
            await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "ChangeMatchAsync was cancelled").ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DismissAsync(CancellationToken ct = default)
    {
        // Step 1: Log entry.
        _logger.LogInformation("DismissAsync called; current state {State}", CurrentState);
        _logService.AddEntry("Dismissing error\u2026", AutomationLogOutcome.Info);

        // Step 2: State guard — only valid from Error.
        if (CurrentState != PcsProState.Error)
            throw new InvalidOperationException(
                $"DismissAsync called from {CurrentState} — only valid from Error state.");

        // Step 3: Stop health poll BEFORE acquiring _operationLock (same pattern as StopAsync).
        await StopHealthPollAsync().ConfigureAwait(false);

        // Step 4: Fire Dismiss trigger under lock (Error → NotRunning), clear last error reason.
        await _operationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _stateMachine.Fire(PcsProTrigger.Dismiss);
            _lastErrorReason = null;
        }
        finally
        {
            _operationLock.Release();
        }

        // Step 5: Flush state events — subscribers observe NotRunning.
        FlushStateChangedEvents();

        // Step 6: Cancel and await crash watcher.
        if (_crashWatcherCts is not null)
        {
            _logger.LogDebug("DismissAsync cancelling crash watcher");
            _crashWatcherCts.Cancel();
            if (_crashWatcherTask is not null)
                await _crashWatcherTask.ConfigureAwait(false);
            _crashWatcherCts.Dispose();
            _crashWatcherCts = null;
            _crashWatcherTask = null;
        }

        // No process termination — NotRunning means "not managing lifecycle".
        // No re-launch — unlike RetryAsync, dismiss returns to quiescent state only.
        // Dispose the process handle (process has already exited/crashed in Error state).
        _process?.Dispose();
        _process = null;

        _logService.AddEntry("Error dismissed", AutomationLogOutcome.Success);
        _logger.LogInformation("DismissAsync complete; current state {State}", CurrentState);
    }

    /// <inheritdoc/>
    public async Task RetryAsync(CancellationToken ct = default)
    {
        // Step 1: Log entry.
        _logger.LogInformation("RetryAsync called; current state {State}", CurrentState);
        _logService.AddEntry("Retrying automation\u2026", AutomationLogOutcome.Info);

        // Step 2: State guard — only valid from Error.
        if (CurrentState != PcsProState.Error)
            throw new InvalidOperationException(
                $"RetryAsync called from {CurrentState} — only valid from Error state.");

        // Step 3: Fire Retry trigger under lock (Error → NotRunning), clear last error reason.
        await _operationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _stateMachine.Fire(PcsProTrigger.Retry);
            _lastErrorReason = null;
        }
        finally
        {
            _operationLock.Release();
        }

        // Step 4: Flush state events — subscribers observe NotRunning before process teardown.
        FlushStateChangedEvents();

        // Step 5: Cancel and await crash watcher.
        if (_crashWatcherCts is not null)
        {
            _logger.LogDebug("RetryAsync cancelling crash watcher");
            _crashWatcherCts.Cancel();
            if (_crashWatcherTask is not null)
                await _crashWatcherTask.ConfigureAwait(false);
            _crashWatcherCts.Dispose();
            _crashWatcherCts = null;
            _crashWatcherTask = null;
        }

        // Step 6: Kill lingering process — standalone timeout CTS only (not linked with ct).
        //         Caller cancellation does not shorten the 5-second graceful close window.
        if (_process is not null && !_process.HasExited)
        {
            _process.CloseMainWindow();

            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(PcsProStateMachine.GracefulCloseTimeoutSeconds));
            try
            {
                await _process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Graceful close wait ended — proceeding to force-kill if needed");
            }

            if (!_process.HasExited)
            {
                _process.Kill();
                _logger.LogWarning("Force-killed cricket.exe {ProcessId}", _process.Id);
            }
        }

        // Step 7: Dispose process.
        _process?.Dispose();
        _process = null;

        // Step 7.5: Guard cancellation — earliest safe point; state=NotRunning, _process=null.
        ct.ThrowIfCancellationRequested();

        // Step 8: Relaunch.
        await LaunchAndLoginAsync(ct).ConfigureAwait(false);

        // Step 9: Log completion.
        _logger.LogInformation("RetryAsync complete; current state {State}", CurrentState);
    }

    /// <inheritdoc/>
    public async Task StartStreamingAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StartStreamingAsync starting; current state {State}", CurrentState);
        _logService.AddEntry("Starting streaming\u2026", AutomationLogOutcome.Info);

        if (!await _matchLoadedGate.WaitAsync(MatchLoadedGateTimeout, ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                "Timed out waiting for a match-loaded operation to complete.");

        try
        {
            await StartStreamingCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _matchLoadedGate.Release();
        }
    }

    private async Task StartStreamingCoreAsync(CancellationToken ct)
    {
        if (_stateMachine.CurrentState != PcsProState.MatchLoaded)
            throw new InvalidOperationException(
                $"StartStreamingAsync requires state {PcsProState.MatchLoaded} " +
                $"but current state is {_stateMachine.CurrentState}.");

        if (_streamingAutomation.IsStreamingActive())
        {
            _logger.LogDebug("StartStreamingAsync: streaming already active — no-op");
            return;
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            _streamingAutomation.ClickStartLiveStream();
            ct.ThrowIfCancellationRequested();
            _streamingAutomation.HandleConsentDialogs();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartStreamingAsync failed during FlaUI automation");
            await FireErrorUnderLockAsync(
                PcsProTrigger.UnexpectedDialog,
                $"Streaming start failed: {ex.Message}").ConfigureAwait(false);
            return;
        }

        _logService.AddEntry("Streaming started", AutomationLogOutcome.Success);
        _logger.LogInformation("StartStreamingAsync completed successfully");
    }

    /// <inheritdoc/>
    public async Task StopStreamingAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StopStreamingAsync starting; current state {State}", CurrentState);
        _logService.AddEntry("Stopping streaming\u2026", AutomationLogOutcome.Info);

        if (!await _matchLoadedGate.WaitAsync(MatchLoadedGateTimeout, ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                "Timed out waiting for a match-loaded operation to complete.");

        try
        {
            await StopStreamingCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _matchLoadedGate.Release();
        }
    }

    private async Task StopStreamingCoreAsync(CancellationToken ct)
    {
        if (_stateMachine.CurrentState != PcsProState.MatchLoaded)
            throw new InvalidOperationException(
                $"StopStreamingAsync requires state {PcsProState.MatchLoaded} " +
                $"but current state is {_stateMachine.CurrentState}.");

        if (!_streamingAutomation.IsStreamingActive())
        {
            _logger.LogDebug("StopStreamingAsync: streaming not active — no-op");
            return;
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            _streamingAutomation.ClickStopLiveStream();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StopStreamingAsync failed during FlaUI automation");
            await FireErrorUnderLockAsync(
                PcsProTrigger.UnexpectedDialog,
                $"Streaming stop failed: {ex.Message}").ConfigureAwait(false);
            return;
        }

        _logService.AddEntry("Streaming stopped", AutomationLogOutcome.Success);
        _logger.LogInformation("StopStreamingAsync completed successfully");
    }

    /// <inheritdoc/>
    public async Task<MatchTeams> UseCurrentMatchAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("UseCurrentMatchAsync starting; current state {State}", CurrentState);
        _logService.AddEntry("Attaching to current match\u2026", AutomationLogOutcome.Info);

        if (!await _matchLoadedGate.WaitAsync(MatchLoadedGateTimeout, ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                "Timed out waiting for a match-loaded operation to complete.");

        try
        {
            return await UseCurrentMatchCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _matchLoadedGate.Release();
        }
    }

    private async Task<MatchTeams> UseCurrentMatchCoreAsync(CancellationToken ct)
    {
        var state = _stateMachine.CurrentState;
        if (state != PcsProState.NotRunning && state != PcsProState.MatchSelection)
            throw new InvalidOperationException(
                $"UseCurrentMatchAsync requires state {PcsProState.NotRunning} or " +
                $"{PcsProState.MatchSelection} but current state is {state}.");

        // Step 2: Verify PCS Pro is running by locating the main window.
        if (!_matchSelectionAutomation.IsMainWindowPresent())
            throw new InvalidOperationException(
                "PCS Pro is not running — cannot attach to current match.");

        // Step 3: Verify a match is loaded.
        if (!_matchSelectionAutomation.IsMatchLoaded())
            throw new InvalidOperationException(
                "No match is currently loaded in PCS Pro — cannot attach.");

        ct.ThrowIfCancellationRequested();

        // Step 4: Fire AttachToMatch (NotRunning → MatchLoaded).
        // _pendingLoadedMatch is intentionally NOT set — LoadedMatch remains null (AC-8).
        await FireUnderLockAsync(PcsProTrigger.AttachToMatch).ConfigureAwait(false);

        _logger.LogInformation(
            "UseCurrentMatchAsync attached — state {State}, reading team names", CurrentState);

        // Step 5: Read team names using existing flow.
        return await ReadTeamNamesAfterAttachAsync(ct).ConfigureAwait(false);
    }

    private async Task<MatchTeams> ReadTeamNamesAfterAttachAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                _teamNamesAutomation.OpenTeamsDialog();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "UseCurrentMatchAsync: failed to open teams dialog");
                _teamNamesAutomation.TryCloseTeamsDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "Teams dialog failed to open after attach").ConfigureAwait(false);
                return new MatchTeams(
                    new TeamNameInfo(string.Empty, string.Empty),
                    new TeamNameInfo(string.Empty, string.Empty));
            }

            ct.ThrowIfCancellationRequested();

            if (_teamNamesAutomation.IsUnexpectedDialogPresent())
            {
                _logger.LogWarning("UseCurrentMatchAsync: unexpected dialog detected after opening teams dialog");
                _teamNamesAutomation.TryCloseUnexpectedDialog();
                _teamNamesAutomation.TryCloseTeamsDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.UnexpectedDialog, "Unexpected dialog blocked team name extraction after attach").ConfigureAwait(false);
                return new MatchTeams(
                    new TeamNameInfo(string.Empty, string.Empty),
                    new TeamNameInfo(string.Empty, string.Empty));
            }

            TeamNameInfo homeTeam;
            TeamNameInfo awayTeam;
            try
            {
                homeTeam = _teamNamesAutomation.ReadHomeTeamName();
                awayTeam = _teamNamesAutomation.ReadAwayTeamName();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "UseCurrentMatchAsync: failed to read team names after attach");
                _teamNamesAutomation.TryCloseTeamsDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "Team names could not be read after attach").ConfigureAwait(false);
                return new MatchTeams(
                    new TeamNameInfo(string.Empty, string.Empty),
                    new TeamNameInfo(string.Empty, string.Empty));
            }

            _teamNamesAutomation.TryCloseTeamsDialog();
            _logService.AddEntry("Attached to current match", AutomationLogOutcome.Success);
            _logger.LogInformation(
                "UseCurrentMatchAsync succeeded — Home={HomeClub}/{HomeTeam} Away={AwayClub}/{AwayTeam}",
                homeTeam.ClubName, homeTeam.TeamName, awayTeam.ClubName, awayTeam.TeamName);
            return new MatchTeams(homeTeam, awayTeam);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("UseCurrentMatchAsync cancelled during team name read");
            _teamNamesAutomation.TryCloseTeamsDialog();
            await FireErrorUnderLockAsync(PcsProTrigger.Timeout, "UseCurrentMatchAsync was cancelled").ConfigureAwait(false);
            throw;
        }
    }

    private MatchInfo FormatMatchForTitle(MatchInfo match)
    {
        var clubName = _options.ClubName;
        if (string.IsNullOrEmpty(clubName))
        {
            return match;
        }

        var (first, second) = TeamNameFormatter.FormatForTitle(
            match.HomeTeam, match.AwayTeam, clubName);

        return match with { HomeTeam = first, AwayTeam = second };
    }
}
