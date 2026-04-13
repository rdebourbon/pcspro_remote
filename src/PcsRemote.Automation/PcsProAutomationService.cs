using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IPcsProAutomationService"/>.
/// Implements process launch, main window detection, crash watching, and stop (S-002).
/// Login automation is deferred to S-003; match-selection, scoreboard, and change-match
/// are deferred to S-004 through S-007.
/// </summary>
internal sealed class PcsProAutomationService : IPcsProAutomationService, IAsyncDisposable
{
    private readonly PcsProOptions _options;
    private readonly ScoreboardOptions _scoreboardOptions;
    private readonly ILogger<PcsProAutomationService> _logger;
    private readonly IProcessManager _processManager;
    private readonly TimeProvider _timeProvider;
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

    private IProcessHandle? _process;
    private CancellationTokenSource? _crashWatcherCts;
    private Task? _crashWatcherTask;
    private string? _lastErrorReason;

    public PcsProAutomationService(
        IOptions<PcsProOptions> options,
        IOptions<ScoreboardOptions> scoreboardOptions,
        ILogger<PcsProAutomationService> logger,
        IProcessManager processManager,
        TimeProvider timeProvider)
    {
        _options = options.Value;
        _scoreboardOptions = scoreboardOptions.Value;
        _logger = logger;
        _processManager = processManager;
        _timeProvider = timeProvider;

        _stateMachine = new PcsProStateMachine();
        _stateMachine.OnTransitioned(newState =>
        {
            _logger.LogInformation("State transition → {State}", newState);
            _pendingTransitions.Enqueue(newState);
        });
    }

    /// <inheritdoc/>
    public PcsProState CurrentState => _stateMachine.CurrentState;

    /// <inheritdoc/>
    public string? LastErrorReason => _lastErrorReason;

    /// <inheritdoc/>
    public event EventHandler<PcsProState>? StateChanged;

    /// <inheritdoc/>
    public async Task LaunchAndLoginAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "LaunchAndLoginAsync starting; {ExecutablePath}", _options.ExecutablePath);

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

        _logger.LogInformation(
            "LaunchAndLoginAsync complete — service in {State}", CurrentState);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StopAsync called; current state {State}", CurrentState);

        if (CurrentState == PcsProState.NotRunning)
            return;

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

        _logger.LogInformation("StopAsync complete; current state {State}", CurrentState);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
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
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            WorkingDirectory = _options.WorkingDirectory,
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
    private async Task FireUnderLockAsync(PcsProTrigger trigger)
    {
        await _operationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
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
            _lastErrorReason = errorReason;
            _stateMachine.Fire(trigger);
        }
        finally
        {
            _operationLock.Release();
        }
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
    }

    // ---- S-003 through S-007 (not yet implemented) -----------------------

    /// <inheritdoc/>
    public Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(GetTodaysMatchesAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(LoadMatchAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(GetTeamNamesAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task RefreshScoreboardAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(RefreshScoreboardAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(CaptureScoreboardImageAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task ChangeMatchAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(ChangeMatchAsync)} is not yet implemented.");

    /// <inheritdoc/>
    public Task RetryAsync(CancellationToken ct = default) =>
        throw new NotImplementedException($"{nameof(RetryAsync)} is not yet implemented.");
}
