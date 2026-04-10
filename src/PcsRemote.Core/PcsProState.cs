namespace PcsRemote.Core;

/// <summary>
/// Models the lifecycle state of the PlayCricket Scorer Pro application.
/// </summary>
public enum PcsProState
{
    NotRunning,
    Launching,
    LoginScreen,
    MatchSelection,
    MatchSelectionSearching,
    MatchSelectionReady,
    MatchLoaded,
    Error,
}
