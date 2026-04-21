using PcsRemote.Core;

namespace PcsRemote.Web.Services;

/// <summary>
/// Thread-safe singleton implementation of <see cref="IOperationCoordinatorService"/>.
/// State is backed by an <see langword="int"/> field toggled atomically with
/// <see cref="Interlocked.CompareExchange(ref int, int, int)"/>, preventing
/// lost updates under concurrent access. Description mutations are protected
/// by <c>_descriptionGuard</c> to prevent TOCTOU races with
/// <see cref="ClearStaleDescription"/>.
/// </summary>
public sealed class OperationCoordinatorService : IOperationCoordinatorService
{
    private int _inProgress;
    private readonly object _descriptionGuard = new();
    private string? _description;

    /// <inheritdoc/>
    public bool IsOperationInProgress => Volatile.Read(ref _inProgress) == 1;

    /// <inheritdoc/>
    public string? CurrentOperationDescription => Volatile.Read(ref _description);

    /// <inheritdoc/>
    public event EventHandler<bool>? OperationInProgressChanged;

    /// <inheritdoc/>
    public event EventHandler<string?>? OperationDescriptionChanged;

    /// <inheritdoc/>
    public bool BeginOperation(string? description = null)
    {
        if (Interlocked.CompareExchange(ref _inProgress, 1, 0) == 0)
        {
            lock (_descriptionGuard)
            {
                _description = description;
            }

            OperationInProgressChanged?.Invoke(this, true);
            OperationDescriptionChanged?.Invoke(this, description);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void MarkComplete()
    {
        if (Interlocked.CompareExchange(ref _inProgress, 0, 1) == 1)
        {
            lock (_descriptionGuard)
            {
                _description = null;
            }

            OperationDescriptionChanged?.Invoke(this, null);
            OperationInProgressChanged?.Invoke(this, false);
        }
    }

    /// <inheritdoc/>
    public void ClearStaleDescription()
    {
        lock (_descriptionGuard)
        {
            if (Volatile.Read(ref _inProgress) != 0)
                return;

            if (_description is null)
                return;

            _description = null;
        }

        // Post-exchange re-check: suppress event if a concurrent
        // BeginOperation acquired the coordinator after our lock release.
        if (Volatile.Read(ref _inProgress) != 0)
            return;

        OperationDescriptionChanged?.Invoke(this, null);
    }
}
