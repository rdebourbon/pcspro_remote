namespace PcsRemote.Core;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IManualModeService"/>.
/// State is backed by an <see langword="int"/> field toggled atomically with
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>, preventing
/// lost updates under concurrent access.
/// </summary>
public sealed class ManualModeService : IManualModeService
{
    // 0 = inactive, 1 = active.
    private int _active;

    /// <inheritdoc/>
    public bool IsManualModeActive => Volatile.Read(ref _active) == 1;

    /// <inheritdoc/>
    public event EventHandler<bool>? ManualModeChanged;

    /// <inheritdoc/>
    public void Enable()
    {
        // Transition 0 → 1. If the field was already 1 the exchange returns 1 (original value)
        // meaning the call was a no-op and we must not raise the event.
        if (Interlocked.CompareExchange(ref _active, 1, 0) == 0)
            ManualModeChanged?.Invoke(this, true);
    }

    /// <inheritdoc/>
    public void Disable()
    {
        // Transition 1 → 0. If the field was already 0 the exchange returns 0 meaning no-op.
        if (Interlocked.CompareExchange(ref _active, 0, 1) == 1)
            ManualModeChanged?.Invoke(this, false);
    }

    /// <inheritdoc/>
    public void Toggle()
    {
        // Retry loop: read current value, attempt to flip it. If another thread
        // wins the same transition, the CAS fails and we retry with the new value.
        while (true)
        {
            var current = Volatile.Read(ref _active);
            var desired = current == 0 ? 1 : 0;

            if (Interlocked.CompareExchange(ref _active, desired, current) == current)
            {
                ManualModeChanged?.Invoke(this, desired == 1);
                return;
            }
        }
    }
}
