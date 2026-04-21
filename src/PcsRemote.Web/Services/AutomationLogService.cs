using PcsRemote.Core;

namespace PcsRemote.Web.Services;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IAutomationLogService"/>.
/// Maintains a bounded rolling buffer of the most recent entries.
/// </summary>
public sealed class AutomationLogService : IAutomationLogService
{
    private const int MaxCapacity = 200;
    private readonly Queue<AutomationLogEntry> _buffer = new();
    private readonly object _bufferGuard = new();

    /// <inheritdoc/>
    public event EventHandler<AutomationLogEntry>? EntryAdded;

    /// <inheritdoc/>
    public void AddEntry(string action, AutomationLogOutcome outcome)
    {
        var entry = new AutomationLogEntry(DateTimeOffset.UtcNow, action, outcome);

        lock (_bufferGuard)
        {
            if (_buffer.Count >= MaxCapacity)
            {
                _buffer.Dequeue();
            }

            _buffer.Enqueue(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    /// <inheritdoc/>
    public IReadOnlyList<AutomationLogEntry> GetRecentEntries()
    {
        lock (_bufferGuard)
        {
            return _buffer.ToList().AsReadOnly();
        }
    }
}
