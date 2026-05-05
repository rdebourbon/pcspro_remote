namespace PcsRemote.Core;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IDateSelectionService"/>.
/// State is backed by an <see langword="int"/> field toggled atomically with
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>, preventing
/// lost updates under concurrent access.
/// </summary>
public sealed class DateSelectionService : IDateSelectionService
{
    // 0 = disabled, 1 = enabled.
    private int _enabled;

    /// <inheritdoc/>
    public bool IsDateSelectionEnabled => Volatile.Read(ref _enabled) == 1;

    /// <inheritdoc/>
    public event EventHandler<bool>? DateSelectionEnabledChanged;

    /// <inheritdoc/>
    public void Enable()
    {
        if (Interlocked.CompareExchange(ref _enabled, 1, 0) == 0)
            DateSelectionEnabledChanged?.Invoke(this, true);
    }

    /// <inheritdoc/>
    public void Disable()
    {
        if (Interlocked.CompareExchange(ref _enabled, 0, 1) == 1)
            DateSelectionEnabledChanged?.Invoke(this, false);
    }
}
