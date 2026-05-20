# SPEC-IS-021-S-004 — Auto-watch Watcher Service: Interface and State

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **Step** | IS-021 S-004 |
| **Branch** | `feature/IS-021-S-004-watcher-service` |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md |

---

## Context

S-001 through S-003 delivered the Play-Cricket API contract, real HTTP client, and mock client. S-004 introduces the central state-management layer for the entire auto-watch feature: `IPlayCricketWatcherService` in `PcsRemote.Core` and its concrete implementation in `PcsRemote.Web`.

The hosted service (S-005 through S-007), UI components (S-009), and push notification module (S-010) all depend exclusively on this interface. No downstream component touches domain state directly. This makes the watcher service independently testable in isolation from polling logic.

The implementation is the single source of truth for:
- Auto-watch enabled/disabled state (never persisted — SC-7)
- Resolved Play-Cricket fixture ID for the loaded match
- Active countdown state (remaining time)
- Auto-load suppression records (per-match deduplification)
- Auto-close dismiss records (per-fixture, prevents countdown restart)

---

## Commit Strategy

Commit at three natural boundaries: (1) interface, snapshot records, and Core contract tests; (2) implementation class and Web unit tests; (3) DI registration wiring. Each commit must leave the solution building with zero warnings and all tests passing.

---

## Requirements

### R-1 — `IPlayCricketWatcherService` interface (Core)

The interface is added to `PcsRemote.Core`. It introduces no new package dependencies (all types are BCL or already in Core).

**Observable state (read-only properties):**

- `IsEnabled` — `bool`. Whether auto-watch is currently enabled. Always `false` on construction (SC-7).
- `CurrentFixtureId` — `int?`. The resolved Play-Cricket fixture ID for the currently loaded match. `null` when no fixture has been resolved or when the system is not in a match-loaded state. A null fixture ID is a normal condition — it means auto-close polling is skipped, not an error.
- `CountdownRemaining` — `TimeSpan?`. The time remaining in the active auto-close countdown. `null` when no countdown is active.

**Lifecycle methods:**

- `Enable()` — Enables auto-watch. On transition from disabled to enabled: clears all dismissed-fixture records (operator is signalling intent to resume); raises `AutoWatchEnabledChanged` with `true`. No-op (no event) if already enabled.
- `Disable()` — Disables auto-watch. On transition from enabled to disabled: cancels any active countdown (which itself raises `CountdownCancelled` if a countdown was active); then raises `AutoWatchEnabledChanged` with `false`. No-op (no event) if already disabled. This is the single code path for all disable operations.
- `SetCurrentFixtureId(int? fixtureId)` — Sets or clears the resolved fixture ID. Called by the hosted service after resolution (S-006). If the new value equals the current `CurrentFixtureId` (including both null), this is a no-op: no state change, no event. Otherwise raises `FixtureIdChanged` with an immutable snapshot of the new value.

**Countdown methods:**

- `StartCountdown(TimeSpan duration)` — Starts the auto-close countdown. If a countdown is already active, this is a no-op (existing countdown continues). Sets `CountdownRemaining` to `duration`. Records whether the T-60 warning is eligible for this countdown: the warning is eligible only when `duration > 60 seconds` (C-3a). Raises `CountdownStarted` with an immutable snapshot containing the initial duration and current `CurrentFixtureId`.
- `bool TickCountdown()` — Advances the active countdown by one second. Returns `false` when no countdown is active (caller exits the tick loop). When a countdown is active: decrements `CountdownRemaining` by one second. If `CountdownRemaining` reaches zero or below: clears `CountdownRemaining` (sets to null), raises `CountdownExpired`, returns `true`. This method does **not** dismiss the current fixture on expiry — dismiss-on-expiry is the caller's responsibility (see S-007). If `CountdownRemaining` crosses the 60-second threshold from above (i.e., was above 60 seconds before this tick, now at or below 60 seconds), and the T-60 warning is eligible for this countdown: raises `AutoCloseT60Warning`. Returns `true` when a countdown was active for this tick (including the final tick that triggers expiry).
- `CancelCountdown()` — Cancels the active countdown. If no countdown is active, this is a no-op (no event). When active: clears `CountdownRemaining`, dismisses the current fixture (records the current `CurrentFixtureId` as dismissed, if non-null), raises `CountdownCancelled`.

**Auto-load suppression methods:**

- `RecordAutoLoadSuppression(string matchId)` — Records that auto-load has fired for the given match ID. Subsequent calls with the same `matchId` return `true` from the lookup without re-triggering load (SC-1 duplicate-trigger suppression).
- `bool IsAutoLoadSuppressed(string matchId)` — Returns `true` if auto-load has already fired for this match ID in the current session/day.
- `ClearAutoLoadSuppressions()` — Removes all suppression records. Called by the midnight reset service (S-008).

**Auto-close dismiss methods:**

- `DismissFixture(int fixtureId)` — Records that auto-close should not fire for this fixture ID. Called by `CancelCountdown()` automatically, and also by the expiry sequence in S-007 immediately on expiry (before the re-check). Prevents countdown restart for the same fixture after cancellation or expiry.
- `bool IsFixtureDismissed(int fixtureId)` — Returns `true` if the given fixture ID has been dismissed. Callers are responsible for checking `CurrentFixtureId != null` before invoking this method.

**Events (use the `EventHandler<TSnapshot>` pattern with immutable snapshot records where a meaningful payload exists; events with no informational payload use plain `EventHandler`, consistent with existing Core interfaces):**

- `event EventHandler<AutoWatchEnabledChangedSnapshot> AutoWatchEnabledChanged` — Fired on `Enable()` and `Disable()` state transitions. Snapshot holds the new `IsEnabled` value. Not fired on no-op calls.
- `event EventHandler<CountdownStartedSnapshot> CountdownStarted` — Fired by `StartCountdown` when a countdown begins. Snapshot includes initial duration and the fixture ID at countdown start.
- `event EventHandler AutoCloseT60Warning` — Fired by `TickCountdown` when remaining time crosses the 60-second threshold from above, but only when the T-60 warning was eligible for this countdown (initial duration > 60 seconds).
- `event EventHandler CountdownExpired` — Fired by `TickCountdown` when the countdown reaches zero.
- `event EventHandler CountdownCancelled` — Fired by `CancelCountdown` when an active countdown is cancelled.
- `event EventHandler<FixtureIdChangedSnapshot> FixtureIdChanged` — Fired by `SetCurrentFixtureId` when the value changes. Snapshot holds the new fixture ID (may be `null`).

### R-2 — Snapshot types (Core)

Three new immutable records are added to `PcsRemote.Core`:

**`CountdownStartedSnapshot`** — immutable record with:
- Initial countdown duration (the `TimeSpan` passed to `StartCountdown`).
- Fixture ID at the moment the countdown started (`int?` — may be null if fixture resolution has not yet completed, though in practice the hosted service starts the countdown only after resolving).

**`AutoWatchEnabledChangedSnapshot`** — immutable record with:
- The new enabled state (`bool`).

**`FixtureIdChangedSnapshot`** — immutable record with:
- The new fixture ID (`int?`).

### R-3 — `PlayCricketWatcherService` implementation (Web)

The concrete implementation is added to `PcsRemote.Web/Services/`. It implements `IPlayCricketWatcherService`.

**Thread safety:** All state mutations are guarded by a single `object _stateLock`. Event handlers are captured under the lock but **invoked outside the lock** to prevent deadlocks. The pattern mirrors `ManualModeService` in `PcsRemote.Core`.

**`IPcsProAutomationService` subscription:**
- The implementation accepts `IPcsProAutomationService` in its constructor.
- On construction, it subscribes to `IPcsProAutomationService.StateChanged`.
- When the new state is anything other than `PcsProState.MatchLoaded`: calls `SetCurrentFixtureId(null)` to clear the resolved fixture ID. This ensures the fixture ID does not persist into a new match-selection session.
- The implementation must implement `IDisposable`. On disposal, it unsubscribes from `IPcsProAutomationService.StateChanged`.

**Enable/Disable semantics:**
- `Enable()`: acquire the lock. If already enabled, release and return. Otherwise set the enabled flag to true, clear the dismissed-fixture set, capture the `AutoWatchEnabledChanged` handler. Release the lock. Invoke the captured handler with a snapshot of `true`.
- `Disable()`: acquire the lock. If already disabled, release and return. Otherwise, invoke a private non-locking cancellation helper (see below). Then set the enabled flag to false. Capture the `AutoWatchEnabledChanged` handler. Release the lock. Fire any pending `CountdownCancelled` handler (if the helper indicated a countdown was active). Then fire the captured `AutoWatchEnabledChanged` handler with a snapshot of `false`.

The private cancellation helper performs the following: if no countdown is currently active (remaining time is null), it takes no action and returns (was-active = false, no captured handler). Otherwise it clears the remaining time and the T-60 eligibility flag, and if a fixture ID is currently set adds it to the dismissed-fixture set. It returns (was-active = true, captured `CountdownCancelled` handler). The public `CancelCountdown()` method acquires the lock, calls this helper, releases the lock, then fires `CountdownCancelled` outside the lock if the helper indicated a countdown was active. This single helper satisfies both the public cancellation path and the internal disable path, ensuring events are always raised after the lock is fully released and that fixture dismissal only occurs when a countdown was actually active.

**Countdown semantics:**
- `StartCountdown(TimeSpan duration)`: acquire the lock. If a countdown is already active (remaining time is not null), release and return (no-op). Otherwise record whether the T-60 warning is eligible for this countdown instance (eligible only when `duration > 60 seconds`), set the remaining time to `duration`, capture the `CountdownStarted` handler. Release the lock. Fire the event with snapshot.
- `TickCountdown()`: acquire the lock. If no countdown is active, release the lock and return false. Decrement remaining time by one second. If the remaining time has crossed the 60-second threshold from above and the T-60 warning is eligible for this countdown, capture the `AutoCloseT60Warning` handler for post-lock firing. If remaining time has reached zero or below: clear the remaining time and the T-60 eligibility flag, capture the `CountdownExpired` handler. Release the lock. Fire `AutoCloseT60Warning` first (if applicable), then `CountdownExpired`. Return true. Otherwise release the lock, fire `AutoCloseT60Warning` if applicable, return true.
- `CancelCountdown()`: acquire the lock. If no countdown is active, release the lock and return (no-op). Otherwise invoke the private cancellation helper (which clears remaining time, T-60 flag, and dismisses the current fixture if non-null), capture the `CountdownCancelled` handler. Release the lock. Fire `CountdownCancelled`.

**Auto-load suppression:** backed by a `HashSet<string>` (match IDs), guarded by `_stateLock`.

**Dismissed fixtures:** backed by a `HashSet<int>` (fixture IDs), guarded by `_stateLock`. Cleared entirely on `Enable()`.

### R-4 — DI registration

`PlayCricketWatcherService` is registered as a singleton in `WebApplicationBuilderExtensions.AddPcsRemoteServices`, unconditionally (both mock and real Play-Cricket branches use the same watcher service).

---

## Test Cases

Tests in `PcsRemote.Web.Tests` (existing project). `PlayCricketWatcherService` is constructed directly in tests using mock `IPcsProAutomationService` (`Moq`).

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-1 | `Enable_WhenDisabled_SetsEnabledAndFiresEvent` | Fresh instance, call `Enable()` | `IsEnabled == true`; `AutoWatchEnabledChanged` fired once with `true` |
| TC-2 | `Enable_WhenAlreadyEnabled_IsNoOp` | Enable twice | `AutoWatchEnabledChanged` fired exactly once total |
| TC-3 | `Enable_ClearsDismissedFixtures` | Dismiss fixture 42; call `Enable()`; check `IsFixtureDismissed(42)` | Returns `false` after Enable |
| TC-4 | `Disable_WhenEnabled_SetsDisabledAndFiresEvent` | Enable then disable | `IsEnabled == false`; `AutoWatchEnabledChanged` fired with `false` |
| TC-5 | `Disable_WhenAlreadyDisabled_IsNoOp` | Disable twice from initial state | No event fired |
| TC-6 | `Disable_WithActiveCountdown_CancelsCountdownThenFiresDisabled` | Enable; `SetCurrentFixtureId(42)`; `StartCountdown(5 min)`; Disable | `CountdownCancelled` fired before `AutoWatchEnabledChanged(false)`; `CountdownRemaining == null`; `IsFixtureDismissed(42) == true` |
| TC-7 | `Disable_WithNoCountdown_NoCancelEvent` | Enable; set fixture ID 42; Disable (no countdown started) | `CountdownCancelled` not fired; `AutoWatchEnabledChanged(false)` fired once; `IsFixtureDismissed(42)` returns `false` |
| TC-8 | `StartCountdown_WhenNoCountdownActive_StartsAndFiresEvent` | Call `StartCountdown(TimeSpan.FromMinutes(5))` (no fixture ID set) | `CountdownRemaining` == 5 min; `CountdownStarted` fired with snapshot `Duration == 5 minutes`, `FixtureId == null` |
| TC-9 | `StartCountdown_WhenCountdownAlreadyActive_IsNoOp` | Start 5-min countdown; then call `StartCountdown(3 min)` | `CountdownRemaining` still 5 min; `CountdownStarted` fired exactly once |
| TC-10 | `TickCountdown_WithActiveCountdown_DecrementsAndReturnsTrue` | Start 10-second countdown; call `TickCountdown()` once | Returns `true`; `CountdownRemaining == 9 seconds` |
| TC-11 | `TickCountdown_WithNoCountdown_ReturnsFalse` | No countdown; call `TickCountdown()` | Returns `false` |
| TC-12 | `TickCountdown_WhenCountdownExpires_FiresExpiredEventAndClearsRemaining` | `SetCurrentFixtureId(42)`; start 1-second countdown; call `TickCountdown()` | Returns `true`; `CountdownExpired` fired; `CountdownRemaining == null`; `IsFixtureDismissed(42) == false` (expiry does not dismiss) |
| TC-13 | `TickCountdown_CrossesT60_FiresWarningWhenEligible` | Start 62-second countdown; tick to 60s remaining | `AutoCloseT60Warning` fired exactly once |
| TC-14 | `TickCountdown_T60SuppressedWhenDurationAtOrBelow60` | Start 60-second countdown; tick to 59s remaining | `AutoCloseT60Warning` not fired |
| TC-15 | `CancelCountdown_WithActiveCountdown_ClearsAndFiresEvent` | Start countdown; call `CancelCountdown()` | `CountdownRemaining == null`; `CountdownCancelled` fired |
| TC-16 | `CancelCountdown_DismissesCurrentFixture` | Set fixture ID 42; start countdown; cancel | `IsFixtureDismissed(42) == true` |
| TC-17 | `CancelCountdown_WithNoCountdown_IsNoOp` | No countdown; call `CancelCountdown()` | `CountdownCancelled` not fired |
| TC-18 | `RecordAutoLoadSuppression_ThenLookup_ReturnsTrue` | Record suppression for "match-1"; check lookup | `IsAutoLoadSuppressed("match-1") == true` |
| TC-19 | `IsAutoLoadSuppressed_UnknownMatchId_ReturnsFalse` | Fresh instance; check "match-1" | Returns `false` |
| TC-20 | `ClearAutoLoadSuppressions_ClearsAllRecords` | Record "match-1"; clear; check "match-1" | Returns `false` after clear |
| TC-21 | `DismissFixture_ThenLookup_ReturnsTrue` | Dismiss fixture 99; lookup | `IsFixtureDismissed(99) == true` |
| TC-22 | `IsFixtureDismissed_UnknownFixture_ReturnsFalse` | Fresh instance; check 99 | Returns `false` |
| TC-23 | `SetCurrentFixtureId_FiresFixtureIdChangedEvent` | Call `SetCurrentFixtureId(42)` | `FixtureIdChanged` fired with `42`; `CurrentFixtureId == 42` |
| TC-24 | `SetCurrentFixtureId_Null_ClearsFixtureId` | Set 42 then set null | `FixtureIdChanged` fired with null; `CurrentFixtureId == null` |
| TC-25 | `StateChanged_AwayFromMatchLoaded_ClearsFixtureId` | Set fixture 42; raise `StateChanged` with `NotRunning` | `CurrentFixtureId == null`; `FixtureIdChanged` fired with null |
| TC-26 | `StateChanged_ToMatchLoaded_DoesNotClearFixtureId` | Set fixture 42; raise `StateChanged` with `MatchLoaded` | `CurrentFixtureId` unchanged (still 42) |
| TC-27 | `ConcurrentEnableDisable_DoesNotLoseEvents` | 10 threads alternating Enable/Disable (fresh instance, all threads start after setup) | Final `IsEnabled` reflects last operation; total `AutoWatchEnabledChanged` events equals number of actual transitions |
| TC-28 | `SetCurrentFixtureId_SameValueAsExisting_IsNoOp` | Call `SetCurrentFixtureId(42)` twice | `FixtureIdChanged` fired exactly once total |
| TC-29 | `CancelCountdown_WithActiveCountdown_WhenNoFixtureId_ClearsAndFiresEventWithoutDismiss` | No fixture ID set; start a countdown; cancel it | `CountdownRemaining == null`; `CountdownCancelled` fired; `IsFixtureDismissed` returns false for any ID |
| TC-30 | `ConcurrentTickAndCancel_DoesNotCorruptState` | Start a 60-second countdown; spin N threads calling `TickCountdown()` in a loop and M threads calling `CancelCountdown()` concurrently; join all | After all threads complete: `CountdownRemaining` is null; `CountdownCancelled` fired at most once; `CountdownExpired` fired at most once; the sum of `CountdownCancelled` + `CountdownExpired` events is exactly one; no exceptions thrown |
| TC-31 | `SetCurrentFixtureId_NullWhenAlreadyNull_IsNoOp` | Fresh instance (CurrentFixtureId already null); call `SetCurrentFixtureId(null)` | `FixtureIdChanged` not fired |
| TC-32 | `Dispose_UnsubscribesFromStateChanged` | Construct with mock automation service; set fixture ID to 42; call `Dispose()`; raise `StateChanged` on mock with non-MatchLoaded state | `CurrentFixtureId` remains 42; `FixtureIdChanged` not fired |
| TC-33 | `StartCountdown_WithFixtureIdSet_SnapshotContainsFixtureId` | `SetCurrentFixtureId(42)`; call `StartCountdown(TimeSpan.FromMinutes(5))` | `CountdownStarted` fired with snapshot `Duration == 5 minutes`, `FixtureId == 42` |
| TC-34 | `SetCurrentFixtureId_DifferentNonNullValue_FiresEventWithNewValue` | `SetCurrentFixtureId(99)`; then `SetCurrentFixtureId(42)` | `FixtureIdChanged` fired twice total; second event snapshot `FixtureId == 42`; `CurrentFixtureId == 42` |

---

## Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `IPlayCricketWatcherService` is declared in `PcsRemote.Core` with all specified properties, methods, and events |
| AC-2 | `CountdownStartedSnapshot`, `AutoWatchEnabledChangedSnapshot`, and `FixtureIdChangedSnapshot` immutable records are declared in `PcsRemote.Core` |
| AC-3 | `PlayCricketWatcherService` exists in `PcsRemote.Web/Services/` and implements `IPlayCricketWatcherService` |
| AC-4 | `PlayCricketWatcherService` subscribes to `IPcsProAutomationService.StateChanged`; fixture ID is cleared on exit from `MatchLoaded` |
| AC-5 | `IsEnabled` is `false` on construction (SC-7) |
| AC-6 | `Enable()` clears dismissed-fixture records (operator resume intent) |
| AC-7 | `Disable()` cancels countdown via single code path before raising `AutoWatchEnabledChanged` |
| AC-8 | T-60 warning is not raised when `StartCountdown` is called with `duration <= 60 seconds` (C-3a) |
| AC-9 | `CancelCountdown()` dismisses current fixture (prevents countdown restart) |
| AC-10 | All state mutations are thread-safe; events are raised outside the lock |
| AC-11 | `PlayCricketWatcherService` is registered as a singleton in `AddPcsRemoteServices` |
| AC-12 | TC-1 through TC-34 are runnable and pass |
| AC-13 | Solution builds with zero warnings |
| AC-14 | All existing tests continue to pass |
| AC-15 | `PlayCricketWatcherService` implements `IDisposable` and unsubscribes from `IPcsProAutomationService.StateChanged` on disposal |

---

## Documentation Updates

None beyond this spec.

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| 1 | Tier 2 | pr-review-agent (GPT-5.4) | F-1 (HIGH) missing commit strategy; F-2 (HIGH) events fired inside lock in Disable(); F-3 (HIGH) SetCurrentFixtureId idempotency gap; F-4 (MEDIUM) primitive event args violate snapshot pattern; F-5 (MEDIUM) missing null-fixture cancel test; F-6 (LOW) private field names in R-3; F-7 (LOW) TickCountdown dismiss ownership implicit; F-8 (LOW) TC-27 concurrent scope too narrow | All 8 accepted | REVISION REQUIRED | All resolved in rev 1 |
| 2 | Tier 2 | pr-review-agent (GPT-5.4) | F-1 (MEDIUM) null-null idempotency TC missing; F-2 (MEDIUM) TickCountdown no-op lock guidance missing; F-3 (LOW) event preamble "all" contradicts plain EventHandler events; F-4 (LOW) IsFixtureDismissed null-input note vacuous; F-5 (LOW) IDisposable conditional → subscription leak | All 5 accepted | REVISION REQUIRED | All resolved in rev 2 |
| 3 | Tier 2 | pr-review-agent (GPT-5.4) | F-1 (MEDIUM) helper dismisses fixture even without active countdown; F-2 (MEDIUM) AC-15 IDisposable has no test | Both accepted | REVISION REQUIRED | All resolved in rev 3 |
| 4 | Tier 2 | pr-review-agent (GPT-5.4) | F-1 (LOW) TC-6 missing fixture-dismissal assertion on Disable+countdown+fixtureId path | Accepted | REVISION REQUIRED | Resolved in rev 4 — TC-6 extended |
| 5 | Tier 2 | pr-review-agent (GPT-5.4) | F-1 (MEDIUM) TC-30 missing CountdownExpired mutual-exclusivity assertion; F-2 (LOW) TC-8 snapshot assertion underspecified | Both accepted | REVISION REQUIRED | Resolved in rev 5 |
| 6 | Tier 2 | pr-review-agent (GPT-5.4) | F-1 (LOW) TC-12 missing no-dismiss assertion on expiry | Accepted | REVISION REQUIRED | Resolved in rev 6 |
| 7 | Tier 2 | pr-review-agent (GPT-5.4) | F-1 (LOW) TC-8 only covers null FixtureId in snapshot; F-2 (LOW) SetCurrentFixtureId non-null→different-non-null path untested | Both accepted | REVISION REQUIRED | Resolved in rev 7 — TC-33 and TC-34 added |
| 8 | Tier 2 | pr-review-agent (GPT-5.4) | None | — | **APPROVED** | — |
