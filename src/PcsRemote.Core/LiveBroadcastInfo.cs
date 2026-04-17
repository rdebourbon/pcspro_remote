namespace PcsRemote.Core;

/// <summary>
/// Immutable snapshot of a YouTube broadcast. Carries the broadcast identifier,
/// rendered title, and watch URL. Does not carry status — the service's
/// <see cref="IYouTubeLiveStreamService.CurrentStatus"/> is the single source of truth.
/// </summary>
/// <param name="BroadcastId">The YouTube broadcast identifier.</param>
/// <param name="Title">The rendered broadcast title.</param>
/// <param name="WatchUrl">The public watch URL for viewers.</param>
public record LiveBroadcastInfo(
    string BroadcastId,
    string Title,
    string WatchUrl);
