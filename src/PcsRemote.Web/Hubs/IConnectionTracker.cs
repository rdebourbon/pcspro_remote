namespace PcsRemote.Web.Hubs;

/// <summary>
/// Tracks the number of currently connected SignalR clients and notifies
/// subscribers whenever the count changes.
/// </summary>
public interface IConnectionTracker
{
    /// <summary>Gets the current number of connected clients.</summary>
    int ConnectionCount { get; }

    /// <summary>
    /// Raised on the calling thread whenever the connection count changes.
    /// The argument is the new count value. Subscribers must be prepared for
    /// concurrent invocations from multiple threads.
    /// </summary>
    event Action<int>? ConnectionCountChanged;

    /// <summary>Increments the connection count by one.</summary>
    void Increment();

    /// <summary>
    /// Decrements the connection count by one. If the count is already zero
    /// this is a no-op and no notification is raised.
    /// </summary>
    void Decrement();
}
