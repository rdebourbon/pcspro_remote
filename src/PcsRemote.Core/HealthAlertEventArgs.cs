namespace PcsRemote.Core;

/// <summary>
/// Classifies the kind of health alert raised by the periodic health-check poll.
/// </summary>
public enum HealthAlertKind
{
    /// <summary>PCS Pro main window is no longer discoverable.</summary>
    WindowLost,

    /// <summary>Match-loaded detection signal is no longer present (window still exists).</summary>
    MatchLost,

    /// <summary>Status bar sync status text changed (informational, no state transition).</summary>
    SyncStatusChanged
}

/// <summary>
/// Event data raised by the health-check poll when a monitored PCS Pro signal changes.
/// </summary>
/// <param name="Kind">The category of health alert.</param>
/// <param name="Message">Human-readable description of the detected change.</param>
/// <param name="CurrentResult">The health-check snapshot that triggered the alert.</param>
public sealed record HealthAlertEventArgs(
    HealthAlertKind Kind,
    string Message,
    HealthCheckResult CurrentResult);
