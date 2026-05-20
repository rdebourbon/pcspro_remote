namespace PcsRemote.TrayHost;

/// <summary>Production wrapper around <see cref="PeriodicTimer"/>.</summary>
internal sealed class RealPeriodicTimer : IPeriodicTimer
{
    private readonly PeriodicTimer _inner;

    internal RealPeriodicTimer(TimeSpan interval) => _inner = new PeriodicTimer(interval);

    public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
        => _inner.WaitForNextTickAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return ValueTask.CompletedTask;
    }
}
