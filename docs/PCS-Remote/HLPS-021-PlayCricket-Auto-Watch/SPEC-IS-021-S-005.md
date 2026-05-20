# SPEC-IS-021-S-005 — Auto-load Polling Loop

| Field | Value |
|---|---|
| **Status** | APPROVED — Rev 3 |
| **Step** | IS-021 S-005 |
| **Branch** | `feature/IS-021-S-005-autoload-polling` |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md |

---

## Context

S-001 through S-004 delivered the Play-Cricket contracts, real and mock API clients, and the watcher service. S-005 introduces the first half of `PlayCricketWatcherHostedService`: the auto-load polling loop that periodically refreshes the PCS Pro match list and automatically loads a match when exactly one scorer-started fixture is detected.

This step also adds a new method to `IPcsProAutomationService` — the FlaUI-triggered refresh that SC-1 requires — and its corresponding mock and real implementations. Neither the existing `GetMatchesForDateAsync` nor `GetTodaysMatchesAsync` satisfies SC-1: both open a new date-scoped search and drive `SearchTriggered`/`SpinnerGone` state transitions, which are inappropriate for a background polling operation. The new method performs a lighter-weight "Clear Filters" refresh on the already-open dialog without driving state machine transitions, as confirmed by OQ-4 resolution.

The hosted service is stateless: all domain state flows through `IPlayCricketWatcherService`. The polling loop simply reads conditions, calls the automation service, and records suppression on success.

---

## Commit Strategy

Commit at three natural boundaries: (1) new interface method, real automation service stub, and mock implementation; (2) `PlayCricketWatcherHostedService` with its polling loop and tests; (3) DI registration. Each commit must leave the solution building with zero warnings and all tests passing.

---

## Requirements

### R-1 — New `GetSelectableMatchesAsync` method on `IPcsProAutomationService` (Core)

A new method is added to `IPcsProAutomationService` that:

- Triggers an active UI refresh of the PCS Pro match selection dialog. The preferred FlaUI mechanism is "Clear Filters" (per OQ-4 resolution) as it is the least disruptive to ongoing state: it clears any active filter, causing the dialog to re-evaluate and display all currently converted fixtures without opening a new date-scoped search or firing `SearchTriggered`/`SpinnerGone` state machine transitions.
- After the refresh, reads and returns the fixtures currently listed in the dialog as a `IReadOnlyList<MatchInfo>`.
- Accepts a `CancellationToken`.
- The caller (hosted service) ensures this method is only invoked when the system is in a match-selection state. The implementation may validate this precondition and throw `InvalidOperationException` if not met, but the hosted service pre-condition guard is the primary enforcement path.
- Propagates `OperationCanceledException` to the caller (does not swallow it). Returns an empty list if the dialog read fails for any reason other than cancellation; does not drive the state machine to Error on recoverable read failures.

**Real automation service (`PcsRemote.Automation`):**

A new method is added to `IMatchSelectionAutomation` that performs the Clear Filters FlaUI interaction and then reads the dialog entries in a single step. `PcsProAutomationService` implements `GetSelectableMatchesAsync` by calling this method and parsing the returned row texts via the existing `MatchRowParser`. The method guards against concurrent match-selection operations using the existing in-progress gate; if the gate is already held (i.e., a user-initiated operation is in progress), the method throws `InvalidOperationException` immediately (consistent with the existing pattern used by `GetMatchesForDateAsync`). Exceptions from the FlaUI interaction — including the in-progress gate throw — are caught and logged; an empty list is returned to the caller rather than propagating, except for `OperationCanceledException` which propagates unconditionally.

**Mock automation service (`PcsRemote.Automation.Mock`):**

`MockPcsProAutomationService` implements `GetSelectableMatchesAsync` by returning the same configurable fixture list produced by `GetTodaysMatchesAsync` (using the existing `FakeMatchCount` option). The mock returns an empty list when the current state is not one of the match-selection states, mirroring the real precondition semantics without throwing.

---

### R-2 — `PlayCricketWatcherHostedService` (Web)

A new `BackgroundService` is added to `PcsRemote.Web`. It is constructed with:

- `IPcsProAutomationService` — for state checks and match list refresh
- `IPlayCricketWatcherService` — for reading auto-watch state and recording suppressions
- `IManualModeService` — for suppression of automated actions per SC-11
- `IOptions<PlayCricketOptions>` — for the configured polling interval
- A logger

The production constructor derives the polling interval from `PlayCricketOptions.PollingIntervalSeconds`, clamping it to a minimum of 60 seconds per C-3. An internal test constructor accepts a `Func<IPeriodicTimer>` timer factory, bypassing the real clock (following the same pattern as `ScoreboardPollingService`).

**Polling loop:**

On each timer tick, the service executes a single auto-load attempt. The loop is inherently non-reentrant: the next `WaitForNextTickAsync` call is not made until the current tick body has fully completed. Exceptions from the tick body that are not `OperationCanceledException` (from the stopping token) are caught and logged; the loop continues. `OperationCanceledException` caused by the stopping token is not caught by the tick body handler — it propagates out to exit the loop cleanly.

**Tick body logic:**

1. **Pre-condition check**: If `IPlayCricketWatcherService.IsEnabled` is `false`, or `IManualModeService.IsManualModeActive` is `true`, or `IPcsProAutomationService.CurrentState` is not one of `MatchSelection`, `MatchSelectionSearching`, or `MatchSelectionReady`, skip the tick silently.

2. **Retrieve selectable matches**: Call `IPcsProAutomationService.GetSelectableMatchesAsync(ct)`. `OperationCanceledException` propagates unconditionally (per R-1 contract and per the stopping-token handling rule above). All other exceptions are caught; a warning is logged and the tick is skipped. These are transient FlaUI automation failures (e.g., dialog not found, element not available, or concurrent user-initiated operation); a single-tick failure is non-fatal and the loop retries on the next tick.

3. **Zero matches**: Silent no-op. No log entry at any level.

4. **Multiple matches**: Emit a warning-level log entry noting the ambiguity per SC-10. No automated action.

5. **Exactly one match — suppression check**: If `IPlayCricketWatcherService.IsAutoLoadSuppressed(match.MatchId)` is `true`, skip silently (SC-1 duplicate-trigger suppression).

6. **Exactly one match — pre-load re-check**: Re-evaluate all three pre-conditions from step 1: `IsEnabled`, `!IsManualModeActive`, and `CurrentState` in match-selection states. If any condition has changed since the match was retrieved, abort the auto-load and log at debug level. Do not record suppression.

7. **Load and suppress**: Call `IPcsProAutomationService.LoadMatchAsync(match, ct)`. Success is defined as "returns without throwing." On success, call `IPlayCricketWatcherService.RecordAutoLoadSuppression(match.MatchId)`. `OperationCanceledException` from `LoadMatchAsync` propagates unconditionally. All other exceptions from `LoadMatchAsync` are caught; a warning is logged and suppression is NOT recorded (the load did not succeed, so the match may be retried on the next tick). `RecordAutoLoadSuppression` exceptions, if any, fall through to the outer tick body handler — they are logged as an unexpected error and the loop continues; the match is not considered suppressed for this attempt.

**Startup and shutdown:**

The `StopAsync` override cancels the polling token and awaits the loop task within a bounded timeout, following the same teardown pattern as `ScoreboardPollingService`. No `StateChanged` subscription is established in S-005 (that wiring belongs to S-006 which introduces subscription-driven behaviour).

---

### R-3 — DI registration

`PlayCricketWatcherHostedService` is registered as a hosted service in `WebApplicationBuilderExtensions.AddPcsRemoteServices`.

---

## Test Cases

Tests in `PcsRemote.Web.Tests` (existing project). `PlayCricketWatcherHostedService` is constructed via the test constructor with `FakePeriodicTimer` and `Mock<>` collaborators (`Mock<IPcsProAutomationService>`, `Mock<IPlayCricketWatcherService>`, `Mock<IManualModeService>`).

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-1 | `ExecuteAsync_WhenEnabled_SingleUnsuppressedMatch_TriggersLoadAndRecordsSuppression` | `IsEnabled=true`, manual mode off, state=`MatchSelection`, `GetSelectableMatchesAsync` returns one unsuppressed match, re-check passes | `LoadMatchAsync` called with the match; `RecordAutoLoadSuppression(match.MatchId)` called |
| TC-2 | `ExecuteAsync_WhenAutoWatchDisabled_DoesNotCallGetSelectableMatches` | `IsEnabled=false` | `GetSelectableMatchesAsync` not called; `LoadMatchAsync` not called |
| TC-3 | `ExecuteAsync_WhenManualModeActive_DoesNotTriggerAutoLoad` | `IsEnabled=true`, manual mode on | `GetSelectableMatchesAsync` not called; `LoadMatchAsync` not called |
| TC-4 | `ExecuteAsync_WhenStateIsMatchLoaded_DoesNotTriggerAutoLoad` | `IsEnabled=true`, manual mode off, state=`MatchLoaded` | `GetSelectableMatchesAsync` not called; `LoadMatchAsync` not called |
| TC-5 | `ExecuteAsync_WhenZeroMatchesReturned_IsNoOp` | Pre-conditions met; `GetSelectableMatchesAsync` returns empty list | `LoadMatchAsync` not called; no log entry at any level emitted for this tick |
| TC-6 | `ExecuteAsync_WhenMultipleMatchesReturned_LogsAmbiguityAndTakesNoAction` | Pre-conditions met; `GetSelectableMatchesAsync` returns 2 matches | `LoadMatchAsync` not called; a warning-level log entry mentioning ambiguity is emitted |
| TC-7 | `ExecuteAsync_WhenMatchAlreadySuppressed_SkipsAutoLoad` | Pre-conditions met; 1 match returned; `IsAutoLoadSuppressed` returns `true` | `LoadMatchAsync` not called; `RecordAutoLoadSuppression` not called |
| TC-8 | `ExecuteAsync_WhenPreLoadReCheckFails_StateChanged_AbortsLoad` | Pre-conditions pass; 1 unsuppressed match returned; `CurrentState` changes to `MatchLoaded` before re-check | `LoadMatchAsync` not called; `RecordAutoLoadSuppression` not called; a debug-level log entry is emitted |
| TC-9 | `ExecuteAsync_WhenGetSelectableMatchesThrows_LogsWarningAndContinues` | Pre-conditions met; `GetSelectableMatchesAsync` throws `Exception` | Warning logged; `LoadMatchAsync` not called; service continues to process subsequent ticks without throwing |
| TC-10 | `ExecuteAsync_WhenLoadMatchAsyncThrows_LogsWarningAndDoesNotRecordSuppression` | Pre-conditions met; 1 unsuppressed match; re-check passes; `LoadMatchAsync` throws | Warning logged; `RecordAutoLoadSuppression` not called |
| TC-11 | `Constructor_WhenPollingIntervalBelowMinimum_LogsWarning` | Public (DI) constructor used; `PollingIntervalSeconds=30` in options | A warning-level log entry noting the configured value and the clamped minimum is emitted at construction time |
| TC-12 | `StopAsync_StopsPollingLoop` | Service is running; `StopAsync` called | Loop exits without throwing; task completes within a bounded timeout |
| TC-13 | `ExecuteAsync_MatchSelectionSearchingState_IsValidForAutoLoad` | State=`MatchSelectionSearching` | `GetSelectableMatchesAsync` is called (state is one of the three accepted match-selection states) |
| TC-14 | `ExecuteAsync_MatchSelectionReadyState_IsValidForAutoLoad` | State=`MatchSelectionReady` | `GetSelectableMatchesAsync` is called (all three match-selection states are valid pre-conditions) |
| TC-15 | `ExecuteAsync_WhenPreLoadReCheckFails_AutoWatchDisabled_AbortsLoad` | Pre-conditions pass; 1 unsuppressed match returned; `IsEnabled` becomes `false` before re-check | `LoadMatchAsync` not called; `RecordAutoLoadSuppression` not called; a debug-level log entry is emitted |
| TC-16 | `ExecuteAsync_WhenPreLoadReCheckFails_ManualModeActivated_AbortsLoad` | Pre-conditions pass; 1 unsuppressed match returned; `IsManualModeActive` becomes `true` before re-check | `LoadMatchAsync` not called; `RecordAutoLoadSuppression` not called; a debug-level log entry is emitted |
| TC-17 | `ExecuteAsync_WhenRecordAutoLoadSuppressionThrows_LogsErrorAndContinues` | Pre-conditions met; 1 unsuppressed match; re-check passes; `LoadMatchAsync` succeeds; `RecordAutoLoadSuppression` throws | `RecordAutoLoadSuppression` called once; error-level log entry emitted; loop continues to process the next tick without throwing; match is not considered suppressed (subsequent tick would attempt load again) |
| TC-18 | `ExecuteAsync_WhenGetSelectableMatchesThrowsOperationCanceledException_Propagates` | Pre-conditions met; `GetSelectableMatchesAsync` throws `OperationCanceledException` | Exception propagates out of `ExecuteAsync`; no warning or error log emitted for the cancellation |

---

## Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `IPcsProAutomationService` declares the new selectable-match refresh method in `PcsRemote.Core` |
| AC-2 | `IMatchSelectionAutomation` declares a corresponding Clear Filters + read method in `PcsRemote.Automation` |
| AC-3 | `PcsProAutomationService` implements the new interface method (real FlaUI path — delivers in this step) |
| AC-4 | `MockPcsProAutomationService` implements the new interface method, returning the configured fixture list when in a match-selection state |
| AC-5 | `PlayCricketWatcherHostedService` exists in `PcsRemote.Web` as a `BackgroundService` |
| AC-6 | Polling interval is clamped to a minimum of 60 seconds regardless of the configured value (C-3); a warning is logged when clamping is applied |
| AC-7 | Auto-load fires only when auto-watch is enabled, manual mode is off, and state is one of the three match-selection states (SC-11) |
| AC-8 | Exactly one unsuppressed match triggers `LoadMatchAsync`; suppression is recorded on success (no exception thrown); not recorded when `LoadMatchAsync` throws |
| AC-9 | Zero matches produce no action and no log entry at any level |
| AC-10 | Multiple matches produce a warning-level log entry and no automated action (SC-10) |
| AC-11 | Pre-load re-check aborts the load if any of the three pre-conditions (`IsEnabled`, `!IsManualModeActive`, match-selection state) changes between match retrieval and load call |
| AC-12 | Non-OCE exceptions from `GetSelectableMatchesAsync` are caught and logged; the polling loop continues (transient FlaUI failure resilience); `OperationCanceledException` propagates unconditionally |
| AC-13 | `PlayCricketWatcherHostedService` is registered as a hosted service in `AddPcsRemoteServices` |
| AC-14 | TC-1 through TC-18 are runnable and pass |
| AC-15 | Solution builds with zero warnings |
| AC-16 | All existing tests continue to pass |

---

## Documentation Updates

None beyond this spec.

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| 1 | Tier 2 | pr-review-agent (GPT-5.4) | F-1 (CRITICAL) OCE not excluded from any catch site; F-2 (HIGH) TC-11 incompatible with test constructor; F-3 (HIGH) MatchSelectionReady untested in TC-13; F-4 (HIGH) TC-5 zero-matches log check too narrow; F-5 (HIGH) dead StateChanged subscription with no AC/TC coverage; F-6 (MEDIUM) TC-8 only one re-check axis; F-7 (MEDIUM) R-1 OCE contract vs Step 2 handler incoherent; F-8 (MEDIUM) in-progress gate contention undefined; F-9 (MEDIUM) SC-9 rationale misattributes failure mode; F-10 (MEDIUM) LoadMatchAsync success undefined; F-11 (LOW) TC-6 phrasing; F-12 (LOW) RecordAutoLoadSuppression exception unaddressed | All 12 accepted | REVISION REQUIRED | All resolved in Rev 1 |
| 2 | Tier 2 | pr-review-agent (GPT-5.4) | NEW-1 (LOW) RecordAutoLoadSuppression exception path has no TC; NEW-2 (LOW) re-check debug log specified but not asserted in TC-8/TC-15/TC-16 | All 2 accepted | REVISION REQUIRED | All resolved in Rev 2: TC-17 added; TC-8/TC-15/TC-16 expected results updated |
| 3 | Tier 2 | pr-review-agent (Claude Sonnet 4.6) | F-1 (LOW) TC-17 missing positive assertion `RecordAutoLoadSuppression` called; F-2 (LOW) no TC for OCE propagation from tick body (AC-12 gap) | Both accepted | APPROVED | TC-17 expected result updated; TC-18 added; AC-14 updated to TC-1–TC-18 |
