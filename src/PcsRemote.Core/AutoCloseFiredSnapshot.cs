namespace PcsRemote.Core;

/// <summary>
/// Immutable snapshot carried by <see cref="IPlayCricketWatcherService.AutoCloseFired"/>.
/// Captures the Play-Cricket fixture ID for which auto-close completed.
/// </summary>
/// <param name="FixtureId">The Play-Cricket fixture ID for which auto-close completed.</param>
public record AutoCloseFiredSnapshot(int FixtureId);
