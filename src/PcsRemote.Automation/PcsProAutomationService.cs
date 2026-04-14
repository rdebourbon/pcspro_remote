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
/// login automation (S-003), match selection (S-004), and team name extraction (S-005).
/// Scoreboard and change-match are deferred to S-006 through S-007.
/// </summary>
internal sealed class PcsProAutomationService : IPcsProAutomationService, IAsyncDisposable
{
    private readonly PcsProOptions _options;
    private readonly ScoreboardOptions _scoreboardOptions;
    private readonly ILogger<PcsProAutomationService> _logger;
    private readonly IProcessManager _processManager;
    private readonly TimeProvider _timeProvider;
    private readonly ILoginAutomation _loginAutomation;
    private readonly IMatchSelectionAutomation _matchSelectionAutomation;
    private readonly ITeamNamesAutomation _teamNamesAutomation;
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

    public PcsProAutomationService(
        IOptions<PcsProOptions> options,
        IOptions<ScoreboardOptions> scoreboardOptions,
        ILogger<PcsProAutomationService> logger,
        IProcessManager processManager,
        TimeProvider timeProvider,
        ILoginAutomation loginAutomation,
        IMatchSelectionAutomation matchSelectionAutomation,
        ITeamNamesAutomation teamNamesAutomation)
    {
        _options = options.Value;
        _scoreboardOptions = scoreboardOptions.Value;
        _logger = logger;
        _processManager = processManager;
        _timeProvider = timeProvider;
        _loginAutomation = loginAutomation;
        _matchSelectionAutomation = matchSelectionAutomation;
        _teamNamesAutomation = teamNamesAutomation;

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

        // S-003: Login phase — enter credentials and wait for match selection dialog.
        if (!await PerformLoginAsync(ct).ConfigureAwait(false))
            return;

        // Fire CredentialsEntered (LoginScreen → MatchSelection) (S-003 AC-1).
        // guardTerminal: crash watcher or StopAsync may have transitioned to Error/NotRunning
        // between PerformLoginAsync returning and this lock acquisition.
        await FireUnderLockAsync(PcsProTrigger.CredentialsEntered, guardTerminal: true).ConfigureAwait(false);

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

    // ---- S-003: Login automation ----------------------------------------

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

        while (true)
        {
            if (IsInTerminalState())
                return false;

            if (await HandleUnexpectedDialogAsync().ConfigureAwait(false))
                return false;

            var submitResult = await TrySubmitCredentialsAsync(submitted).ConfigureAwait(false);
            if (submitResult == LoginSubmitResult.Failed)
                return false;
            if (submitResult == LoginSubmitResult.Submitted)
            {
                submitted = true;
                continue; // skip delay — check match selection immediately
            }

            if (submitted && _loginAutomation.IsMatchSelectionVisible())
            {
                _logger.LogDebug("PerformLoginAsync: match selection dialog detected — login accepted");
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

        _logger.LogWarning("PerformLoginAsync: unexpected dialog detected during login phase");
        _loginAutomation.TryCloseUnexpectedDialog();
        await FireErrorUnderLockAsync(
            PcsProTrigger.UnexpectedDialog,
            "An unexpected dialog appeared during login").ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Attempts to enter the password and click submit when the login dialog is visible
    /// and credentials have not yet been submitted.
    /// Returns <see cref="LoginSubmitResult.Submitted"/> on success,
    /// <see cref="LoginSubmitResult.Failed"/> on interaction exception (error already fired),
    /// or <see cref="LoginSubmitResult.NotReady"/> when the dialog is not yet visible.
    /// </summary>
    private async Task<LoginSubmitResult> TrySubmitCredentialsAsync(bool alreadySubmitted)
    {
        if (alreadySubmitted || !_loginAutomation.IsLoginDialogVisible())
            return LoginSubmitResult.NotReady;

        try
        {
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

    /// <summary>
    /// Checks whether the login phase has exceeded its timeout.
    /// Fires <see cref="PcsProTrigger.Timeout"/> if elapsed.
    /// Returns <see langword="true"/> when the login phase should abort.
    /// </summary>
    private async Task<bool> CheckLoginTimeoutAsync(long startTimestamp, bool submitted)
    {
        if (_timeProvider.GetElapsedTime(startTimestamp).TotalSeconds
            < PcsProStateMachine.LoginScreenTimeoutSeconds)
        {
            return false;
        }

        var reason = submitted
            ? "Match selection dialog did not appear within the timeout after submitting credentials"
            : $"Login screen did not appear within {PcsProStateMachine.LoginScreenTimeoutSeconds} seconds";

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
                return new MatchTeams(string.Empty, string.Empty);
            }

            ct.ThrowIfCancellationRequested();

            // §4.3 — Check for an unexpected dialog before reading.
            if (_teamNamesAutomation.IsUnexpectedDialogPresent())
            {
                _logger.LogWarning("GetTeamNamesAsync: unexpected dialog detected after opening teams dialog");
                _teamNamesAutomation.TryCloseUnexpectedDialog();
                _teamNamesAutomation.TryCloseTeamsDialog();
                await FireErrorUnderLockAsync(PcsProTrigger.UnexpectedDialog, "Unexpected dialog blocked team name extraction").ConfigureAwait(false);
                return new MatchTeams(string.Empty, string.Empty);
            }

            // §4.4 — Read home and away team names.
            string homeTeam;
            string awayTeam;
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
                return new MatchTeams(string.Empty, string.Empty);
            }

            // §4.5 — Close the dialog and return on success.
            _teamNamesAutomation.TryCloseTeamsDialog();
            _logger.LogInformation(
                "GetTeamNamesAsync succeeded — HomeTeam={HomeTeam} AwayTeam={AwayTeam}",
                homeTeam, awayTeam);
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
