namespace PcsRemote.Core;

/// <summary>
/// Defines the contract for enabling or disabling date selection override in the debug panel.
/// When enabled, operators can select a specific date for match retrieval instead of today's date.
/// The service tracks only the toggle state — the selected date is managed by the UI and passed
/// as an explicit parameter to the automation layer.
/// </summary>
public interface IDateSelectionService
{
    /// <summary>
    /// Gets a value indicating whether date selection override is currently enabled.
    /// When <see langword="true"/>, the UI shows a date picker for selecting a match date.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    bool IsDateSelectionEnabled { get; }

    /// <summary>
    /// Enables date selection override. If already enabled this is a no-op
    /// and <see cref="DateSelectionEnabledChanged"/> is not raised.
    /// Raises <see cref="DateSelectionEnabledChanged"/> only when the flag transitions from
    /// disabled to enabled.
    /// </summary>
    void Enable();

    /// <summary>
    /// Disables date selection override. If already disabled this is a no-op
    /// and <see cref="DateSelectionEnabledChanged"/> is not raised.
    /// Raises <see cref="DateSelectionEnabledChanged"/> only when the flag transitions from
    /// enabled to disabled.
    /// </summary>
    void Disable();

    /// <summary>
    /// Raised after <see cref="Enable"/> or <see cref="Disable"/> transitions the flag.
    /// The event argument is the new value of <see cref="IsDateSelectionEnabled"/>.
    /// Not raised on no-op calls.
    /// </summary>
    event EventHandler<bool> DateSelectionEnabledChanged;
}
