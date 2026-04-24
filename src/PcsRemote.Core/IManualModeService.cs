namespace PcsRemote.Core;

/// <summary>
/// Defines the contract for pausing and resuming remote automation via manual mode.
/// The interface resides in Core so that <c>PcsRemote.TrayHost</c> (writer) and
/// <c>PcsRemote.Web</c> (reader) can share the contract without creating an illegal
/// cross-layer dependency.
/// </summary>
public interface IManualModeService
{
    /// <summary>
    /// Gets a value indicating whether manual mode is currently active.
    /// When <see langword="true"/>, all remote automation commands must be rejected.
    /// </summary>
    bool IsManualModeActive { get; }

    /// <summary>
    /// Activates manual mode. If manual mode is already active this is a no-op
    /// and <see cref="ManualModeChanged"/> is not raised.
    /// Raises <see cref="ManualModeChanged"/> only when the flag transitions from
    /// inactive to active.
    /// </summary>
    void Enable();

    /// <summary>
    /// Deactivates manual mode. If manual mode is already inactive this is a no-op
    /// and <see cref="ManualModeChanged"/> is not raised.
    /// Raises <see cref="ManualModeChanged"/> only when the flag transitions from
    /// active to inactive.
    /// </summary>
    void Disable();

    /// <summary>
    /// Raised after <see cref="Enable"/>, <see cref="Disable"/>, or <see cref="Toggle"/>
    /// transitions the active flag. The event argument is the new value of
    /// <see cref="IsManualModeActive"/>. Not raised on no-op calls.
    /// </summary>
    event EventHandler<bool> ManualModeChanged;

    /// <summary>
    /// Atomically toggles manual mode: if inactive, activates it; if active, deactivates it.
    /// The toggle retries internally when a concurrent caller wins the same transition,
    /// ensuring every call completes exactly one state flip and raises
    /// <see cref="ManualModeChanged"/> exactly once with the new value.
    /// </summary>
    void Toggle();
}
