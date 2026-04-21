namespace PcsRemote.Core;

/// <summary>
/// Immutable log entry representing a single automation action and its outcome.
/// </summary>
public record AutomationLogEntry(
    DateTimeOffset Timestamp,
    string Action,
    AutomationLogOutcome Outcome);
