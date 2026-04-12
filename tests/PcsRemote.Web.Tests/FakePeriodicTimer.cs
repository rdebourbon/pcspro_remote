namespace PcsRemote.Web.Tests;

/// <summary>
/// A deterministic <see cref="IPeriodicTimer"/> for unit tests.
/// Ticks only fire when the test explicitly calls <see cref="TriggerTick"/>.
/// Use <see cref="WaitingForTickAsync"/> before each trigger to synchronise with the loop.
/// </summary>
internal sealed class FakePeriodicTimer : IPeriodicTimer
{
    // Counting semaphore: Release() = TriggerTick, WaitAsync() = wait for next tick.
    // Pre-buffering works naturally — Release before WaitAsync queues the tick count.
    private readonly SemaphoreSlim _ticks = new(0);

    // Releases once each time WaitForNextTickAsync is entered so the test knows
    // the loop is paused and ready to receive the next tick.
    private readonly SemaphoreSlim _waitStarted = new(0);

    // Cancelled on DisposeAsync to unblock any in-flight WaitAsync (returns false,
    // mirroring PeriodicTimer's contract: return false rather than throw on disposal).
    private readonly CancellationTokenSource _disposeCts = new();

    // Fires when DisposeAsync is called (RunLoopAsync has fully exited via await using).
    private readonly TaskCompletionSource _disposed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Waits until the polling loop calls <see cref="WaitForNextTickAsync"/> (i.e., it is
    /// paused and ready to receive the next tick). Each call consumes one "ready" signal.
    /// Always await this before calling <see cref="TriggerTick"/> to avoid races.
    /// </summary>
    public Task WaitingForTickAsync() => _waitStarted.WaitAsync();

    /// <summary>
    /// Completes when the timer is disposed, meaning <see cref="RunLoopAsync"/> has fully
    /// exited and the <c>await using</c> block called <see cref="DisposeAsync"/>.
    /// </summary>
    public Task WhenDisposed => _disposed.Task;

    /// <summary>Queues one tick for the loop to consume.</summary>
    public void TriggerTick() => _ticks.Release();

    public async ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken)
    {
        // Signal before awaiting — the tick may already be buffered (Release was called first).
        _waitStarted.Release();

        // Link the loop-stop token with the disposal token so either unblocks the wait.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposeCts.Token);
        try
        {
            await _ticks.WaitAsync(linked.Token);
            return true;
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            // Disposal cancelled the wait — return false to match PeriodicTimer's contract.
            return false;
        }
        // OperationCanceledException from the loop-stop token propagates naturally.
    }

    public ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _disposed.TrySetResult();
        return ValueTask.CompletedTask;
    }
}
