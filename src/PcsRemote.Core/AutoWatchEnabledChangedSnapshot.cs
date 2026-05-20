namespace PcsRemote.Core;

/// <summary>
/// Immutable snapshot carried by <see cref="IPlayCricketWatcherService.AutoWatchEnabledChanged"/>.
/// </summary>
/// <param name="IsEnabled">The new auto-watch enabled state.</param>
public record AutoWatchEnabledChangedSnapshot(bool IsEnabled);
