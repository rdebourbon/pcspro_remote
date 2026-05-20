namespace PcsRemote.Core;

/// <summary>
/// Immutable snapshot carried by <see cref="IPlayCricketWatcherService.FixtureIdChanged"/>.
/// </summary>
/// <param name="FixtureId">The new resolved Play-Cricket fixture ID, or <see langword="null"/> when cleared.</param>
public record FixtureIdChangedSnapshot(int? FixtureId);
