namespace PcsRemote.Core;

/// <summary>
/// Service contract for the automation log — a rolling buffer of recent
/// automation actions. Implementations must be thread-safe.
/// </summary>
public interface IAutomationLogService
{
    /// <summary>
    /// Records a new log entry with the given action description and outcome.
    /// The timestamp is generated internally by the implementation.
    /// </summary>
    void AddEntry(string action, AutomationLogOutcome outcome);

    /// <summary>
    /// Returns a point-in-time snapshot of recent entries, ordered oldest to newest.
    /// The returned list is an immutable copy — subsequent additions do not mutate it.
    /// </summary>
    IReadOnlyList<AutomationLogEntry> GetRecentEntries();

    /// <summary>
    /// Raised after a new entry is added. The entry is already present in
    /// <see cref="GetRecentEntries"/> on any thread when this event fires
    /// (publish-after-commit).
    /// </summary>
    event EventHandler<AutomationLogEntry> EntryAdded;
}
