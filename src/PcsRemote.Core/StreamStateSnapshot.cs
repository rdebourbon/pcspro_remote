namespace PcsRemote.Core;

/// <summary>
/// Immutable snapshot carried by <see cref="IYouTubeLiveStreamService.StatusChanged"/> events.
/// Captures the current status, the active broadcast (if any), and an optional error message.
/// </summary>
/// <param name="Status">The current <see cref="LiveStreamStatus"/>.</param>
/// <param name="CurrentBroadcast">The active broadcast, or <see langword="null"/> when idle or in error.</param>
/// <param name="ErrorMessage">A human-readable error description, or <see langword="null"/> when no error.</param>
public record StreamStateSnapshot(
    LiveStreamStatus Status,
    LiveBroadcastInfo? CurrentBroadcast,
    string? ErrorMessage);
