namespace PcsRemote.Core;

/// <summary>
/// Manages scoreboard image capture, SHA-256 delta detection, and event-based
/// broadcast to subscribed Blazor components via the circuit-subscription pattern.
/// </summary>
public interface IScoreboardService
{
    /// <summary>
    /// Raised when a new scoreboard image is available for display.
    /// Fires on the normal poll path when the captured image hash differs from the stored
    /// hash, and unconditionally on the forced-refresh path.
    /// The event argument is always a non-null, non-empty JPEG byte array.
    /// </summary>
    event EventHandler<byte[]> ScoreboardUpdated;

    /// <summary>
    /// Raised after <see cref="ScoreboardUpdated"/> on the forced-refresh path only.
    /// Acts as an acknowledgement signal; carries no image data.
    /// </summary>
    event EventHandler RefreshCompleted;

    /// <summary>
    /// The most recently broadcast scoreboard image.
    /// <c>null</c> until the first successful capture.
    /// Late-joining components read this property on initialisation to display the
    /// current image without waiting for the next poll.
    /// </summary>
    byte[]? CurrentImage { get; }

    /// <summary>
    /// Captures a scoreboard image via <see cref="IPcsProAutomationService"/>, computes
    /// its SHA-256 hash, and fires <see cref="ScoreboardUpdated"/> only when the hash
    /// differs from the previously stored hash (delta detection).
    /// If the capture returns null or empty bytes, no event is fired and no state is updated.
    /// Propagates exceptions to the caller.
    /// </summary>
    Task CaptureAndBroadcastAsync(CancellationToken ct = default);

    /// <summary>
    /// Forces a scoreboard capture and unconditional broadcast, bypassing delta detection.
    /// Clears the stored hash before capture so that even a byte-identical image
    /// produces a <see cref="ScoreboardUpdated"/> event, followed by <see cref="RefreshCompleted"/>.
    /// Propagates exceptions to the caller.
    /// </summary>
    Task ForceRefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Resets <see cref="CurrentImage"/> to <c>null</c> and clears the stored hash.
    /// Does not fire any events.
    /// Called by the change-match flow before transitioning away from <c>MatchLoaded</c>.
    /// </summary>
    void ClearCache();
}
