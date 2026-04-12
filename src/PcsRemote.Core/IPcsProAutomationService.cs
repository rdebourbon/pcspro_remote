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
    /// Raised whenever the lifecycle state changes; the event argument contains the new state.
    /// </summary>
    event EventHandler<PcsProState> StateChanged;

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
}
