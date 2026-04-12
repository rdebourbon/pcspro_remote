using PcsRemote.Core;

namespace PcsRemote.Web.Services;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IOperationCoordinatorService"/>.
/// State is backed by an <see langword="int"/> field toggled atomically with
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>, preventing
/// lost updates under concurrent access.
/// </summary>
public sealed class OperationCoordinatorService : IOperationCoordinatorService
{
    // 0 = idle, 1 = in progress.
    private int _inProgress;

    /// <inheritdoc/>
    public bool IsOperationInProgress => Volatile.Read(ref _inProgress) == 1;

    /// <inheritdoc/>
    public event EventHandler<bool>? OperationInProgressChanged;

    /// <inheritdoc/>
    public bool BeginOperation()
    {
        // Transition 0 → 1. If the field was already 1 the exchange returns 1 (original value)
        // meaning the call was a no-op: do not raise the event and return false so the caller
        // knows it did not acquire the lock and must not call MarkComplete().
        if (Interlocked.CompareExchange(ref _inProgress, 1, 0) == 0)
        {
            OperationInProgressChanged?.Invoke(this, true);
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public void MarkComplete()
    {
        // Transition 1 → 0. If the field was already 0 the exchange returns 0 meaning no-op.
        if (Interlocked.CompareExchange(ref _inProgress, 0, 1) == 1)
            OperationInProgressChanged?.Invoke(this, false);
    }
}
