namespace PcsRemote.Core;

/// <summary>
/// Defines the contract between the web layer and the automation layer for controlling PlayCricket Scorer Pro.
/// </summary>
public interface IPcsProAutomationService
{
    /// <summary>
    /// Gets the current lifecycle state of PCS Pro as managed by the state machine.
    /// </summary>
    PcsProState CurrentState { get; }

    /// <summary>
    /// Gets a human-readable description of the most recent error, or <see langword="null"/>
    /// when the service is not in the <see cref="PcsProState.Error"/> state.
    /// The value is cleared (set to <see langword="null"/>) when the service transitions
    /// out of the <see cref="PcsProState.Error"/> state (e.g., after <see cref="StopAsync"/>).
    /// </summary>
    string? LastErrorReason { get; }

    /// <summary>
    /// Gets the currently loaded match when state is <see cref="PcsProState.MatchLoaded"/>,
    /// or <see langword="null"/> otherwise. Also <see langword="null"/> after
    /// <see cref="UseCurrentMatchAsync"/> (attach flow provides no <see cref="MatchInfo"/>).
    /// Cleared on any state transition away from <see cref="PcsProState.MatchLoaded"/>.
    /// </summary>
    MatchInfo? LoadedMatch { get; }

    /// <summary>
    /// Raised whenever the lifecycle state changes; the event argument contains the new state.
    /// </summary>
    event EventHandler<PcsProState> StateChanged;

    /// <summary>
    /// Raised by the health-check poll when a monitored PCS Pro signal changes.
    /// State transitions (window-lost, match-lost) also flow through <see cref="StateChanged"/>;
    /// this event provides additional diagnostic context.
    /// </summary>
    event EventHandler<HealthAlertEventArgs> HealthAlert;

    /// <summary>
    /// Launches PCS Pro and drives through login to the match selection screen.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task LaunchAndLoginAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads and returns the list of today's available match fixtures from PCS Pro.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default);

    /// <summary>
    /// Selects and loads the specified match, advancing the lifecycle state to MatchLoaded.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default);

    /// <summary>
    /// Reads the home and away team names from the currently loaded match.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default);

    /// <summary>
    /// Triggers a scoreboard data refresh within the loaded match.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task RefreshScoreboardAsync(CancellationToken ct = default);

    /// <summary>
    /// Captures and returns a JPEG screenshot of the scoreboard.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops and closes PCS Pro, returning the lifecycle state to NotRunning.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Closes the current match and returns PCS Pro to the match selection screen,
    /// transitioning the lifecycle state from <see cref="PcsProState.MatchLoaded"/> to
    /// <see cref="PcsProState.MatchSelection"/>.
    /// Throws <see cref="InvalidOperationException"/> if the current state is not
    /// <see cref="PcsProState.MatchLoaded"/>.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task ChangeMatchAsync(CancellationToken ct = default);

    /// <summary>
    /// Recovers from an error state by transitioning from <see cref="PcsProState.Error"/>
    /// to <see cref="PcsProState.NotRunning"/> and immediately restarting the full launch
    /// sequence via <see cref="LaunchAndLoginAsync"/>.
    /// Throws <see cref="InvalidOperationException"/> if the current state is not
    /// <see cref="PcsProState.Error"/>.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task RetryAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts PCS Pro's built-in RTMP live streaming.
    /// Throws <see cref="InvalidOperationException"/> if the current state is not
    /// <see cref="PcsProState.MatchLoaded"/>.
    /// Idempotent: calling when already streaming is a no-op.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task StartStreamingAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops PCS Pro's built-in RTMP live streaming.
    /// Throws <see cref="InvalidOperationException"/> if the current state is not
    /// <see cref="PcsProState.MatchLoaded"/>.
    /// Idempotent: calling when not streaming is a no-op.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task StopStreamingAsync(CancellationToken ct = default);

    /// <summary>
    /// Attaches to an already-running PCS Pro instance that has a match loaded,
    /// transitioning from <see cref="PcsProState.NotRunning"/> to <see cref="PcsProState.MatchLoaded"/>.
    /// Verifies PCS Pro is running and a match is loaded, fires the <see cref="PcsProTrigger.AttachToMatch"/>
    /// trigger, then reads and returns team names.
    /// Throws <see cref="InvalidOperationException"/> if PCS Pro is not running, no match is loaded,
    /// or the current state is not <see cref="PcsProState.NotRunning"/>.
    /// <see cref="LoadedMatch"/> remains <see langword="null"/> after a successful attach.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task<MatchTeams> UseCurrentMatchAsync(CancellationToken ct = default);

    /// <summary>
    /// Dismisses the current error state, transitioning from <see cref="PcsProState.Error"/>
    /// to <see cref="PcsProState.NotRunning"/> without restarting any automation sequence.
    /// Unlike <see cref="RetryAsync"/>, dismiss returns the system to a quiescent state only.
    /// Throws <see cref="InvalidOperationException"/> if the current state is not
    /// <see cref="PcsProState.Error"/>.
    /// Accepts an optional <paramref name="ct"/> to cancel the operation.
    /// </summary>
    Task DismissAsync(CancellationToken ct = default);
}
