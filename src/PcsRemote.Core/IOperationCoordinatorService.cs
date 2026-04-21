namespace PcsRemote.Core;

/// <summary>
/// Singleton coordination service that tracks whether an automation operation is currently
/// in progress across all connected browsers.
/// </summary>
public interface IOperationCoordinatorService
{
    /// <summary>Gets a value indicating whether an operation is currently in progress.</summary>
    bool IsOperationInProgress { get; }

    /// <summary>
    /// Gets the human-readable description of the current operation, or <see langword="null"/>
    /// when no operation is in progress.
    /// </summary>
    string? CurrentOperationDescription { get; }

    /// <summary>
    /// Raised when the in-progress state changes. The argument is the new state:
    /// <see langword="true"/> when an operation begins, <see langword="false"/> when it completes.
    /// </summary>
    event EventHandler<bool> OperationInProgressChanged;

    /// <summary>
    /// Marks the start of an operation. Returns <see langword="true"/> if this call acquired the
    /// lock (CAS 0→1 succeeded) and raised the event; <see langword="false"/> if an operation was
    /// already in progress (no-op — the caller must NOT call <see cref="MarkComplete"/>).
    /// </summary>
    /// <param name="description">
    /// An optional human-readable description of the operation (e.g., "Launching PCS Pro…").
    /// When provided, the value is available via <see cref="CurrentOperationDescription"/>.
    /// </param>
    bool BeginOperation(string? description = null);

    /// <summary>
    /// Marks the completion of the current operation. If no operation is in progress this is a
    /// no-op and no event is raised.
    /// </summary>
    void MarkComplete();
}
