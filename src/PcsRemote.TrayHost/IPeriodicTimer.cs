namespace PcsRemote.TrayHost;

/// <summary>
/// Abstraction over <see cref="PeriodicTimer"/> that eliminates real clock and thread-pool
/// dependencies from unit tests.
/// </summary>
internal interface IPeriodicTimer : IAsyncDisposable
{
    /// <summary>
    /// Waits for the next tick. Returns <c>false</c> when the timer has been disposed.
    /// Throws <see cref="OperationCanceledException"/> when
    /// <paramref name="cancellationToken"/> is cancelled before the next tick.
    /// </summary>
    ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken);
}
