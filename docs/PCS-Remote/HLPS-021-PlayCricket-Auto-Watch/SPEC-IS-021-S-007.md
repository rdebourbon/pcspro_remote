# SPEC-IS-021-S-007 — Auto-close Polling and Countdown

| Field | Value |
|---|---|
| **Document** | SPEC-IS-021-S-007.md |
| **Step** | IS-021 S-007 |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-05-20 |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Branch** | `feature/IS-021-S-007-auto-close-countdown` |

---

## 1. Context and Purpose

S-007 extends the `PlayCricketWatcherHostedService` (already delivering S-005 auto-load and S-006 fixture ID resolution) with the auto-close half: polling for match completion via the Play-Cricket API, driving a visible countdown, and executing the stop-and-return sequence when the countdown expires without cancellation.

The watcher service (`PlayCricketWatcherService`) requires one addition to its interface (`AutoCloseFired` event) to support S-009 and S-010 notification wiring. All other watcher-service state management (`StartCountdown`, `TickCountdown`, `CancelCountdown`, `DismissFixture`, `IsFixtureDismissed`) is already implemented.

OQ-1 (Play-Cricket completion status strings) is resolved in this spec:
- **Completed statuses**: `"Result"`, `"Abandoned"`, `"No Result"` — any of these indicates the match is no longer in play and should trigger auto-close.
- **In-progress status**: `"Playing"` — match is live; no auto-close action.
- Any other status (e.g., `"Fixture"`) is treated as not-yet-started; no action.

---

## 2. Requirements

### R-1 — AutoCloseFired event on IPlayCricketWatcherService

Add an `AutoCloseFired` event to `IPlayCricketWatcherService` in `PcsRemote.Core`. The event carries an immutable snapshot containing the fixture ID for which auto-close completed. A new `AutoCloseFiredSnapshot` record is added alongside the existing snapshot types.

Following the existing pattern (where `Enable()`, `Disable()`, `SetCurrentFixtureId()`, etc. raise events from within watcher service methods), a `RaiseAutoCloseFired(AutoCloseFiredSnapshot)` method is also added to `IPlayCricketWatcherService`. The hosted service calls this method after a successful expiry sequence; the implementation fires the event. The event is not raised directly by the watcher service's own logic.

The `PlayCricketWatcherService` implementation in `PcsRemote.Web.Services` must declare the event and implement `RaiseAutoCloseFired`.

### R-2 — Auto-close pre-conditions (per-tick check)

On each poll tick, after the auto-load half completes, the hosted service performs an auto-close check. The check proceeds only when **all** of the following are true:
- Auto-watch is enabled
- Manual mode is not active
- The automation service state is `MatchLoaded`
- A fixture ID is currently resolved (non-null)
- The resolved fixture has not been dismissed

If any condition fails, the tick returns without querying the API or starting a countdown.

### R-3 — Completion detection

When R-2 pre-conditions are satisfied, the hosted service queries the current fixture via `IPlayCricketApiClient.GetFixturesAsync` for each configured site. A fixture is considered **completed** when its status string is one of the resolved OQ-1 values: `"Result"`, `"Abandoned"`, or `"No Result"` (case-insensitive comparison).

The match is located by fixture ID within the API response. If the fixture ID is not found across any site result, the check is a no-op for this tick (the fixture may not yet have been updated — retry on next tick). API failures (empty list, exception) are already handled by the API client returning an empty list with a warning logged; the hosted service treats an empty result set as a no-op (no countdown started, tick exits cleanly). Fixture IDs are assumed globally unique across configured sites; if the same fixture ID is found on multiple sites, the first occurrence is used.

A second pre-condition re-check covering **all five R-2 conditions** is performed immediately before calling `StartCountdown` to guard against the conditions changing between the API call and the countdown start.

### R-4 — Countdown inner loop

When completion is detected and the final R-2 re-check passes, the hosted service calls `_watcherService.StartCountdown(duration)` where `duration` is derived from `PlayCricketOptions.CountdownDurationSeconds`. The `StartCountdown` call is a no-op if a countdown is already active.

After `StartCountdown`, the hosted service captures the fixture ID at countdown start (from `CurrentFixtureId` at the moment of the call, or from the `CountdownStartedSnapshot` payload of the `CountdownStarted` event). This captured ID is used in the expiry re-check (R-5).

A second-granularity inner loop then runs as a background task (similar in structure to the resolution task): each iteration waits one second, then calls `TickCountdown()`. The loop exit conditions are:

- **`TickCountdown()` returns `false`** — no countdown was active at the time of call (countdown was already cancelled externally before this tick). The loop exits without calling `DismissFixture` or the expiry sequence.
- **`TickCountdown()` returns `true` and `CountdownRemaining` is non-null after the call** — tick processed, countdown still running, continue loop.
- **`TickCountdown()` returns `true` and `CountdownRemaining` is null after the call** — expiry tick: the watcher service has just fired `CountdownExpired` and cleared the remaining time. Proceed to expiry sequence (R-5).
- **Countdown CTS is cancelled (see R-6)** — the one-second wait throws `OperationCanceledException`; caught cleanly, no error logged.

The inner loop must not be started if `StartCountdown` was a no-op (i.e., a countdown loop is already in flight). The spec allows for the case where the implementation detects this by checking whether the `CountdownStarted` event fired, checking `CountdownRemaining` before and after the call, or by guarding `_countdownTask` atomically — the approach is a Delivery decision. The invariant is: at most one countdown inner loop task exists at any time.

### R-5 — Expiry sequence

When the expiry tick is detected (R-4), the hosted service immediately calls `_watcherService.DismissFixture(capturedFixtureId)` before any other action. This unconditional dismiss prevents any retry regardless of what follows.

A final pre-execution re-check then validates the following five pre-expiry conditions before calling `StopStreamingAsync`:
- Auto-watch is still enabled
- Manual mode is not active
- State is still `MatchLoaded`
- `CurrentFixtureId` is non-null
- `CurrentFixtureId` still equals the fixture ID captured at countdown start

If the re-check fails, the service logs a warning and returns. The fixture remains dismissed; the operator must manage match end manually.

If the re-check passes:
1. `StopStreamingAsync` is called (idempotent — no-op if not streaming; this is accepted per SC-3).
2. `ChangeMatchAsync` is called.
3. `_watcherService.RaiseAutoCloseFired(snapshot)` is called, which fires `AutoCloseFired` on all subscribers.

Steps 1–2 are wrapped in a single exception handler. If any non-OCE exception is thrown, the exception is caught and logged; the fixture remains dismissed; and the method returns early (step 3 is not reached). If `OperationCanceledException` is thrown (host shutdown), it is re-thrown. This is an accepted risk per SC-9 — the operator must intervene manually.

Step 3 (`RaiseAutoCloseFired`) runs after the exception handler completes successfully. Subscriber exceptions from step 3 propagate normally (subscribers must not throw).

### R-6 — Cancellation triggers and countdown CTS lifecycle

The countdown inner loop is governed by a `_countdownCts` (separate from the resolution `_resolutionCts`). The countdown CTS is:
- Created when the countdown inner loop task is started.
- Linked to the service's `stoppingToken` so host shutdown cancels it.
- Cancelled (and disposed) by any of the five cancellation triggers below.

For all triggers, the hosted service must ensure the watcher service countdown state is fully cleaned up: `_watcherService.CancelCountdown()` is called (or is already guaranteed to have been called) so that `CountdownRemaining` is cleared and future `StartCountdown` calls are not permanently suppressed. The five cancellation triggers are:

**(a) Manual mode activated** — The hosted service subscribes to `IManualModeService.ManualModeChanged` (constructor; unsubscribed in `Dispose`). On transition to active (`true`): the hosted service calls `_watcherService.CancelCountdown()` (which dismisses the current fixture and fires `CountdownCancelled`), then cancels the countdown CTS. This cancellation must not be deferred to the next poll tick. *(Note: `CancelCountdown()` raises `CountdownCancelled` internally, which trigger (e) also responds to by cancelling the CTS — the explicit CTS cancel here is a belt-and-braces guard to ensure immediacy.)*

**(b) Auto-watch disabled** — `IPlayCricketWatcherService.Disable()` internally calls `CancelCountdown()`, which dismisses the current fixture and raises `CountdownCancelled`. The hosted service need not call `CancelCountdown()` a second time; the `CountdownCancelled` event (trigger (e)) is sufficient to cancel the countdown CTS.

**(c) State exits `MatchLoaded`** — The existing `OnStateChanged` handler already cancels the resolution CTS and calls `SetCurrentFixtureId(null)`. It must also: call `_watcherService.CancelCountdown()` (to clear countdown state and dismiss any active fixture) and cancel the countdown CTS. *(Note: the explicit CTS cancel is a belt-and-braces guard — `CancelCountdown()` raises `CountdownCancelled`, which trigger (e) also handles.)*

**(d) Fixture ID changes** — The hosted service subscribes to `IPlayCricketWatcherService.FixtureIdChanged` (constructor; unsubscribed in `Dispose`). On any change (including transition to null): call `_watcherService.CancelCountdown()` then cancel the countdown CTS. *(Note: the explicit CTS cancel is a belt-and-braces guard — `CancelCountdown()` raises `CountdownCancelled`, which trigger (e) also handles.)*

**(e) User action via UI (operator cancels)** — `CancelCountdown()` is called from the UI (S-009). This raises `CountdownCancelled` and dismisses the current fixture within the watcher service. The hosted service subscribes to `CountdownCancelled` (constructor; unsubscribed in `Dispose`) to cancel the countdown CTS so the inner loop task exits cleanly. No additional `CancelCountdown()` call is needed.

After all five triggers, the countdown CTS is cancelled and `CountdownRemaining` in the watcher service is null. The inner loop task exits via OCE (cleanly, no error logged).

### R-7 — Lifecycle of countdown CTS and task

The pattern mirrors the resolution task lifecycle:
- A `_countdownCts` field and `_countdownTask` field are added to the hosted service.
- When a new countdown starts: if a prior countdown task is in flight (a defensive guard for unexpected concurrent ticks), the service must call `_watcherService.CancelCountdown()` to clear watcher state, cancel the prior CTS, and then create a new CTS before starting a new inner loop.
- `OnStateChanged` (for non-MatchLoaded transitions), `FixtureIdChanged`, and `ManualModeChanged→active` all call `_watcherService.CancelCountdown()`, then cancel and clear both the countdown CTS and task.
- `Dispose` cancels and disposes the countdown CTS (in addition to the resolution CTS).
- OCE exiting the countdown inner loop is caught cleanly without error logging.

### R-8 — No interaction with auto-load path

The auto-close check runs on the **same poll tick** as auto-load, after the auto-load section of `RunTickAsync`. The two paths are independent: a tick where auto-load conditions are not met (e.g., state is `MatchLoaded`) will still proceed to the auto-close check. There is no coupling between the two loops beyond shared pre-conditions.

---

## 3. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | All new production code builds with zero warnings. |
| AC-2 | `IPlayCricketWatcherService` has `AutoCloseFired` event and `RaiseAutoCloseFired(AutoCloseFiredSnapshot)` method; `PlayCricketWatcherService` declares the event and implements the method. |
| AC-3 | A corresponding immutable `AutoCloseFiredSnapshot` record (carrying the fixture ID) is added to `PcsRemote.Core`. |
| AC-4 | On each tick, when all five R-2 pre-conditions are met and the resolved fixture has a completed status (R-3), `StartCountdown` is called once. |
| AC-5 | When `StartCountdown` is called (and starts a new countdown), a one-second-resolution inner loop advances the countdown via `TickCountdown()`. At most one countdown inner loop task exists at any time. |
| AC-6 | On expiry tick detection (`TickCountdown()` returns `true` and `CountdownRemaining` is null), `DismissFixture` is called immediately before the re-check and expiry actions. |
| AC-7 | The expiry re-check (R-5, all five conditions) aborts the sequence when any pre-condition fails or the fixture ID has changed since countdown start. |
| AC-8 | `StopStreamingAsync` and `ChangeMatchAsync` are called in order when the expiry re-check passes. |
| AC-9 | `RaiseAutoCloseFired` is called only after both `StopStreamingAsync` and `ChangeMatchAsync` complete successfully. |
| AC-10 | Non-OCE exceptions in the expiry sequence (R-5 steps 1–2) are caught and logged; `RaiseAutoCloseFired` is not called; the fixture remains dismissed. OCE is re-thrown. `RaiseAutoCloseFired` (step 3) runs outside the handler and its subscriber exceptions propagate normally. |
| AC-11 | Manual-mode activation calls `CancelCountdown()` then cancels the countdown CTS immediately (not deferred to the next tick). |
| AC-12 | State-exit (`OnStateChanged` non-MatchLoaded) calls `CancelCountdown()` then cancels both the resolution CTS and the countdown CTS. |
| AC-13 | `FixtureIdChanged` calls `CancelCountdown()` then cancels the countdown CTS when a countdown is in flight. |
| AC-14 | `CountdownCancelled` event causes the countdown CTS to be cancelled; inner loop exits cleanly. |
| AC-15 | OCE thrown inside the countdown inner loop (from the one-second wait) is caught without being logged as an error. |
| AC-16 | Countdown is not started when the fixture is already dismissed (R-2 pre-condition). |
| AC-17 | `CountdownDurationSeconds` from options governs the `StartCountdown` duration. |
| AC-18 | All existing tests continue to pass. |
| AC-19 | `_countdownCts` is cancelled and disposed in `Dispose`. |
| AC-20 | `ManualModeChanged` subscription is added in constructor and removed in `Dispose`. |
| AC-21 | `FixtureIdChanged` and `CountdownCancelled` subscriptions are added in constructor and removed in `Dispose`. |
| AC-22 | When the API client returns an empty result (API outage path), no countdown is started and the tick exits cleanly with no exception. |

**Total: 22 acceptance criteria.**

---

## 4. Test Cases

| TC | Description |
|---|---|
| TC-1 | When all R-2 pre-conditions met and fixture has status `"Result"`, `StartCountdown` is called with the configured duration. |
| TC-2 | When fixture status is `"Abandoned"`, `StartCountdown` is called. |
| TC-3 | When fixture status is `"No Result"`, `StartCountdown` is called. |
| TC-4 | When fixture status is `"Playing"`, `StartCountdown` is NOT called. |
| TC-5 | When fixture status is `"Fixture"` (not-yet-started), `StartCountdown` is NOT called. |
| TC-6 | When `IsFixtureDismissed` is `true` for the current fixture, the auto-close check does not call the API or start countdown. |
| TC-7 | When `IsEnabled` is `false`, the auto-close check is skipped entirely. |
| TC-8 | When manual mode is active at tick time, the auto-close check is skipped entirely. |
| TC-9 | When `CurrentFixtureId` is null, the auto-close check is skipped. |
| TC-10 | When the fixture is not found in any API result, the tick is a no-op (no countdown started). |
| TC-11 | `TickCountdown()` is called once per second inside the inner loop; the expiry sequence does NOT fire before the configured number of `TickCountdown()` calls. |
| TC-12 | When `TickCountdown()` returns `false` (cancelled path), the inner loop exits without calling `DismissFixture` or the expiry sequence. |
| TC-13 | When the expiry tick is detected (`TickCountdown()` returns `true`, `CountdownRemaining` null), `DismissFixture` is called before the expiry re-check. |
| TC-14 | When the expiry re-check fails (state no longer `MatchLoaded`), `StopStreamingAsync` is NOT called. |
| TC-15 | When the expiry re-check fails (fixture ID changed since countdown start), `StopStreamingAsync` is NOT called. |
| TC-16 | When the expiry re-check passes, `StopStreamingAsync` then `ChangeMatchAsync` are called in order, and `RaiseAutoCloseFired` is called. |
| TC-17 | When `StopStreamingAsync` throws (non-OCE), the exception is caught, `RaiseAutoCloseFired` is NOT called, `ChangeMatchAsync` is NOT called, and the fixture remains dismissed. |
| TC-18 | Manual-mode activation mid-countdown calls `CancelCountdown()` and cancels the inner loop immediately without waiting for the next tick. |
| TC-19 | `FixtureIdChanged` event (fixture ID clears to null) calls `CancelCountdown()` and cancels the countdown inner loop. |
| TC-20 | `CountdownCancelled` event (operator cancel via UI) cancels the inner loop cleanly (no error logged). |
| TC-21 | When fixture status is `"RESULT"` (upper-case variant), `StartCountdown` is called — confirming case-insensitive comparison. |
| TC-22 | When the API client returns an empty list, no countdown is started and the tick exits cleanly with no exception. |

**Total: 22 test cases.**

---

## 5. Branch and Commit Strategy

Branch: `feature/IS-021-S-007-auto-close-countdown`

Commits should be small and focused:
- `IPlayCricketWatcherService` `AutoCloseFired` event + `AutoCloseFiredSnapshot` record
- `PlayCricketWatcherService` event declaration
- `PlayCricketWatcherHostedService` auto-close extension (R-2 through R-8)
- Tests

---

## 6. Open Questions Resolved

| OQ | Resolution |
|---|---|
| OQ-1 | Completed statuses: `"Result"`, `"Abandoned"`, `"No Result"` (case-insensitive). In-progress: `"Playing"`. Not-started: `"Fixture"`. Confirmed via Play-Cricket API v2 documentation. |

---

## 7. Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 2 | Claude Sonnet 4.6, GPT-5.4 | CRITICAL: 1 / HIGH: 2 / MEDIUM: 6 / LOW: 2 | Accept: 9, Reject: 1, Defer: 1 | REVISION | CRITICAL: inner loop exit condition inverted — `TickCountdown()` returns `true` on every active tick, not only expiry; expiry detected by `CountdownRemaining==null` (Claude Issue 1). HIGH: no mechanism for hosted service to fire interface event — `RaiseAutoCloseFired()` method added to interface (Claude Issue 2). HIGH: triggers (a/c/d) didn't call `CancelCountdown()`, leaving watcher state permanently dirty (Claude Issue 3 = GPT F2). MEDIUMs accepted: wrong cross-ref R-8→R-6; FixtureIdChanged/CountdownCancelled subscription lifecycle added; pre-countdown re-check scope enumerated; case-insensitive TC-21 added; concurrent-tick defensive guard updated to call `CancelCountdown()`; API outage TC-22 added; OCE in expiry sequence clarified (re-thrown). Rejected: GPT F5 (multi-site fixture ID precedence — fixture IDs globally unique; not a gap). Deferred: Claude Issue 10 (StartCountdown idempotency TC — covered by AC-5 invariant + TC-11 expiry timing). |
| R2 | Tier 2 | Claude Sonnet 4.6, GPT-5.4 | LOW: 3 (Claude) + 1 CRITICAL downgraded to LOW (GPT) | Accept all | APPROVED (self-cert) | GPT CRITICAL (downgraded): R-5 "all five R-2 conditions" label read as including "not dismissed" — condition is absent from R-5 enumeration; label only was misleading. Fix: changed lead-in to "the following five pre-expiry conditions". Claude LOW-1: same label finding — resolved by same fix. Claude LOW-2: triggers (a/c/d) produce belt-and-braces double CTS cancellation via trigger (e) — benign; annotated as intentional. Claude LOW-3: R-5 exception handler wrapped steps 1–3 but text said steps 1–2; `RaiseAutoCloseFired` moved outside handler with subscriber-exception note; AC-10 updated. No blocking findings; Tier-0 self-cert applied. |
