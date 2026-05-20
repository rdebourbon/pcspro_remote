namespace PcsRemote.Core;

/// <summary>
/// Immutable snapshot carried by <see cref="IPlayCricketWatcherService.CountdownStarted"/>.
/// Captures the initial duration and the resolved fixture ID at the moment the countdown began.
/// </summary>
/// <param name="InitialDuration">The full countdown duration passed to <c>StartCountdown</c>.</param>
/// <param name="FixtureId">
/// The Play-Cricket fixture ID that was resolved at countdown start, or <see langword="null"/>
/// if fixture resolution had not yet completed.
/// </param>
public record CountdownStartedSnapshot(TimeSpan InitialDuration, int? FixtureId);
