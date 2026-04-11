namespace PcsRemote.Web.Hubs;

/// <inheritdoc cref="IConnectionTracker"/>
public sealed class ConnectionTracker : IConnectionTracker
{
    private int _count;

    /// <inheritdoc/>
    public int ConnectionCount => Volatile.Read(ref _count);

    /// <inheritdoc/>
    public event Action<int>? ConnectionCountChanged;

    /// <inheritdoc/>
    public void Increment()
    {
        var newCount = Interlocked.Increment(ref _count);
        ConnectionCountChanged?.Invoke(newCount);
    }

    /// <inheritdoc/>
    public void Decrement()
    {
        int current;
        int newCount;

        do
        {
            current = Volatile.Read(ref _count);
            if (current <= 0) return; // no-op: do not fire notification
            newCount = current - 1;
        }
        while (Interlocked.CompareExchange(ref _count, newCount, current) != current);

        ConnectionCountChanged?.Invoke(newCount);
    }
}
