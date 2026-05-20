namespace PcsRemote.Core;

/// <summary>
/// Defines the contract for the Play-Cricket auto-watch state manager.
/// The service is the single source of truth for auto-watch enabled state, resolved fixture ID,
/// active countdown, auto-load suppression records, and auto-close dismiss records.
/// All implementations must be thread-safe.
/// </summary>
public interface IPlayCricketWatcherService
{
    /// <summary>
    /// Gets a value indicating whether auto-watch is currently enabled.
    /// Always <see langword="false"/> on construction.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets the resolved Play-Cricket fixture ID for the currently loaded match,
    /// or <see langword="null"/> when no fixture has been resolved or the system is
    /// not in a match-loaded state. A null value is normal — it means auto-close
    /// polling is skipped, not an error.
    /// </summary>
    int? CurrentFixtureId { get; }

    /// <summary>
    /// Gets the time remaining in the active auto-close countdown,
    /// or <see langword="null"/> when no countdown is active.
    /// </summary>
    TimeSpan? CountdownRemaining { get; }

    /// <summary>
    /// Enables auto-watch. On transition from disabled to enabled: clears all dismissed-fixture
    /// records and raises <see cref="AutoWatchEnabledChanged"/>. No-op (no event) if already enabled.
    /// </summary>
    void Enable();

    /// <summary>
    /// Disables auto-watch. Cancels any active countdown (which raises
    /// <see cref="CountdownCancelled"/> if a countdown was active), then raises
    /// <see cref="AutoWatchEnabledChanged"/>. No-op (no event) if already disabled.
    /// </summary>
    void Disable();

    /// <summary>
    /// Sets or clears the resolved fixture ID. If the new value equals the current
    /// <see cref="CurrentFixtureId"/> (including both null) this is a no-op: no state change,
    /// no event. Otherwise raises <see cref="FixtureIdChanged"/>.
    /// </summary>
    void SetCurrentFixtureId(int? fixtureId);

    /// <summary>
    /// Starts the auto-close countdown. If a countdown is already active this is a no-op
    /// (existing countdown continues). Records T-60 warning eligibility: the warning fires
    /// only when <paramref name="duration"/> is greater than 60 seconds. Raises
    /// <see cref="CountdownStarted"/> with an immutable snapshot.
    /// </summary>
    void StartCountdown(TimeSpan duration);

    /// <summary>
    /// Advances the active countdown by one second.
    /// Returns <see langword="false"/> when no countdown is active.
    /// Fires <see cref="AutoCloseT60Warning"/> when the remaining time crosses the 60-second
    /// threshold from above and the T-60 warning is eligible for this countdown.
    /// Fires <see cref="CountdownExpired"/> when the countdown reaches zero and clears
    /// <see cref="CountdownRemaining"/>. Does not dismiss the current fixture on expiry —
    /// dismiss-on-expiry is the caller's responsibility (see S-007).
    /// Returns <see langword="true"/> when a countdown was active for this tick.
    /// </summary>
    bool TickCountdown();

    /// <summary>
    /// Cancels the active countdown. If no countdown is active this is a no-op (no event).
    /// When active: clears <see cref="CountdownRemaining"/>, dismisses the current fixture
    /// (if non-null), and raises <see cref="CountdownCancelled"/>.
    /// </summary>
    void CancelCountdown();

    /// <summary>
    /// Records that auto-load has fired for the given match ID. Subsequent calls with the
    /// same <paramref name="matchId"/> return <see langword="true"/> from
    /// <see cref="IsAutoLoadSuppressed"/> without re-triggering load.
    /// </summary>
    void RecordAutoLoadSuppression(string matchId);

    /// <summary>
    /// Returns <see langword="true"/> if auto-load has already fired for this match ID
    /// in the current session.
    /// </summary>
    bool IsAutoLoadSuppressed(string matchId);

    /// <summary>
    /// Removes all auto-load suppression records. Called by the midnight reset service (S-008).
    /// </summary>
    void ClearAutoLoadSuppressions();

    /// <summary>
    /// Records that auto-close should not fire for the given fixture ID.
    /// Called automatically by <see cref="CancelCountdown"/> and explicitly by S-007 on expiry.
    /// Prevents countdown restart for the same fixture after cancellation or expiry.
    /// </summary>
    void DismissFixture(int fixtureId);

    /// <summary>
    /// Returns <see langword="true"/> if the given fixture ID has been dismissed.
    /// Callers are responsible for checking <see cref="CurrentFixtureId"/> is not null
    /// before invoking this method.
    /// </summary>
    bool IsFixtureDismissed(int fixtureId);

    /// <summary>
    /// Raised on <see cref="Enable"/> and <see cref="Disable"/> state transitions.
    /// Not raised on no-op calls.
    /// </summary>
    event EventHandler<AutoWatchEnabledChangedSnapshot> AutoWatchEnabledChanged;

    /// <summary>
    /// Raised by <see cref="StartCountdown"/> when a countdown begins.
    /// </summary>
    event EventHandler<CountdownStartedSnapshot> CountdownStarted;

    /// <summary>
    /// Raised by <see cref="TickCountdown"/> when remaining time crosses the 60-second
    /// threshold from above, but only when the T-60 warning was eligible for this countdown
    /// (initial duration greater than 60 seconds).
    /// </summary>
    event EventHandler AutoCloseT60Warning;

    /// <summary>Raised by <see cref="TickCountdown"/> when the countdown reaches zero.</summary>
    event EventHandler CountdownExpired;

    /// <summary>Raised by <see cref="CancelCountdown"/> when an active countdown is cancelled.</summary>
    event EventHandler CountdownCancelled;

    /// <summary>
    /// Raised by <see cref="SetCurrentFixtureId"/> when the value changes.
    /// Not raised on no-op calls (same-value assignments).
    /// </summary>
    event EventHandler<FixtureIdChangedSnapshot> FixtureIdChanged;

    /// <summary>
    /// Raised when the auto-close expiry sequence completes successfully.
    /// Fired by the hosted service via <see cref="RaiseAutoCloseFired"/> after
    /// <c>StopStreamingAsync</c> and <c>ChangeMatchAsync</c> both succeed.
    /// </summary>
    event EventHandler<AutoCloseFiredSnapshot> AutoCloseFired;

    /// <summary>
    /// Fires <see cref="AutoCloseFired"/> on all subscribers. Called by the hosted service
    /// after a successful auto-close expiry sequence (IS-021 S-007 R-5).
    /// The event can only be raised from within the declaring class, so this method
    /// delegates to the implementation, which invokes the handler directly.
    /// </summary>
    void RaiseAutoCloseFired(AutoCloseFiredSnapshot snapshot);
}
