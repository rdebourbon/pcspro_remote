namespace PcsRemote.Core;

/// <summary>
/// No-op implementation of <see cref="IAutomationLogService"/> for use in
/// unit tests that do not verify log entries.
/// </summary>
public sealed class NullAutomationLogService : IAutomationLogService
{
    /// <inheritdoc/>
    public void AddEntry(string action, AutomationLogOutcome outcome) { }

    /// <inheritdoc/>
    public IReadOnlyList<AutomationLogEntry> GetRecentEntries() => [];

    /// <inheritdoc/>
#pragma warning disable CS0067 // EntryAdded is never raised in the null implementation
    public event EventHandler<AutomationLogEntry>? EntryAdded;
#pragma warning restore CS0067
}
