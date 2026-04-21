namespace PcsRemote.Core;

/// <summary>
/// Snapshot of PCS Pro health signals read by the periodic health-check poll.
/// All fields are read-only; the poll compares successive snapshots to detect changes.
/// </summary>
/// <param name="IsMainWindowPresent">
/// <see langword="true"/> if the PCS Pro main window is discoverable in the automation tree.
/// </param>
/// <param name="IsMatchLoaded">
/// <see langword="true"/> if the match-loaded detection signal (twdScoreSummary) is present.
/// </param>
/// <param name="SyncStatus">
/// Text content of the status bar element, or <see langword="null"/> if not readable.
/// </param>
/// <param name="WindowTitle">
/// The main window title, or <see langword="null"/> if the window is not present.
/// </param>
/// <param name="ProbeSucceeded">
/// <see langword="true"/> if the probe completed without FlaUI exceptions during element probing.
/// When <see langword="false"/>, the other fields contain default values and must not be acted upon.
/// </param>
public sealed record HealthCheckResult(
    bool IsMainWindowPresent,
    bool IsMatchLoaded,
    string? SyncStatus,
    string? WindowTitle,
    bool ProbeSucceeded);
