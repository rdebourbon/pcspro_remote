# SPEC-S-003: Core State Machine Lifecycle

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-Core-Lifecycle.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-10 |
| **Step** | S-003 — Core State Machine Lifecycle |
| **IS** | IS-002-Mock-Service.md (APPROVED v0.2) |
| **HLPS** | HLPS-002-Mock-Service.md (APPROVED v0.3) |
| **Branch** | `feature/hlps002-S003-core-lifecycle` |

---

## 1. Overview

This step implements all active lifecycle behaviour in `MockPcsProAutomationService`. After this step the service transitions state correctly, fires `StateChanged` events, enforces single-caller semantics, propagates cancellation, and includes initial `ILogger` instrumentation.

Two additional interface methods (`GetTeamNamesAsync` and `RefreshScoreboardAsync`) are also implemented here as simple zero-latency stubs — they produce no state transitions and require no options — closing the M-SC-1 gap before S-005/S-006 address the remaining data and image methods.

`GetTodaysMatchesAsync` and `CaptureScoreboardImageAsync` remain `throw new NotImplementedException()` (addressed in S-005 and S-006 respectively).

---

## 2. Scope

### In Scope

- Full implementation of `LaunchAndLoginAsync` (NotRunning → Launching → LoginScreen → MatchSelection)
- Full implementation of `LoadMatchAsync` (MatchSelection → MatchSelectionSearching → MatchSelectionReady → MatchLoaded, with ChangeMatch detour if already in MatchLoaded)
- Full implementation of `StopAsync` (force-reset to NotRunning from any state)
- `GetTeamNamesAsync` → returns a fixed fake `MatchTeams` record
- `RefreshScoreboardAsync` → returns a completed task (no-op)
- Single-caller semaphore guard on all three lifecycle methods
- `CancellationToken` propagation through all configurable delays
- `StateChanged` event firing after each state transition
- Basic `ILogger` instrumentation throughout lifecycle methods
- Unit tests covering all HLPS-002 success criteria addressable in this step (M-SC-1, M-SC-2, M-SC-8, M-SC-9, M-SC-10, M-SC-11, M-SC-12)

### Out of Scope

- Error injection (S-004)
- `GetTodaysMatchesAsync` (S-005)
- `CaptureScoreboardImageAsync` (S-006)
- Serilog finalisation and DI registration (S-007)
- Any change to `MockPcsProOptions` — the shape established in S-002 is complete for this step

> **IS-002 naming note:** IS-002 §S-003 names `SelectMatchAsync` and `RetryAsync`. `SelectMatchAsync` corresponds directly to `LoadMatchAsync` in the finalised `IPcsProAutomationService` interface (same contract, the name was settled during HLPS-001 delivery). `RetryAsync` is not an interface method — error recovery flow (Error → NotRunning) is addressed in S-004 via the `RetryAsync` internal path; no separate public method is required.

---

## 3. Requirements

### R-1: Transition sequencer design

The mock manages its own state via the `_currentState` field introduced in S-002. It does NOT use `PcsProStateMachine` internally — the Stateless machine is reserved for the real FlaUI implementation. Every state change in this step follows the same pattern:
1. Apply the relevant configurable delay (passed the caller's `CancellationToken`)
2. Assign the new state to `_currentState`
3. Call `OnStateChanged` with the new state

This pattern ensures: (a) cancellation before a delay prevents the following transition from occurring; (b) state is never in a half-transitioned condition; (c) `StateChanged` is always fired after `_currentState` is updated with the new state value as the event argument. `OnStateChanged` is called synchronously and inline — no `await` or deferred dispatch occurs between the state assignment and the event call.

### R-2: Single-caller enforcement

A `SemaphoreSlim(1, 1)` private field must be added to `MockPcsProAutomationService`. All three lifecycle methods (`LaunchAndLoginAsync`, `LoadMatchAsync`, `StopAsync`) must attempt to enter the semaphore without blocking. If the semaphore is unavailable (another lifecycle call is already in progress), the method must throw `InvalidOperationException` immediately. The semaphore must be released in a `finally` block to guarantee release on both normal completion and exception (including `OperationCanceledException`).

### R-3: LaunchAndLoginAsync

Drives the service from `NotRunning` to `MatchSelection` in three transitions.

**Precondition:** If `CurrentState != NotRunning`, throw `InvalidOperationException` before applying any delay. This guard runs after acquiring the semaphore.

Each delay is read from `_options` and applied before its transition:
- `LaunchDelay` before NotRunning → Launching
- `LoginDetectedDelay` before Launching → LoginScreen
- `CredentialsEnteredDelay` before LoginScreen → MatchSelection

Each transition calls `OnStateChanged` immediately after `_currentState` is updated. Cancellation at any delay propagates `OperationCanceledException` to the caller; state remains at whatever value it held before that delay.

### R-4: LoadMatchAsync

Drives the service through match selection to `MatchLoaded`. The method handles two valid entry states:

**Precondition:** If `CurrentState` is neither `MatchSelection` nor `MatchLoaded`, throw `InvalidOperationException` before applying any delay. This guard runs after acquiring the semaphore.

**From MatchLoaded (ChangeMatch path):** Apply `ChangeMatchDelay` before transitioning from MatchLoaded → MatchSelection. Then continue with the selection sequence below.

**From MatchSelection (standard path):** Apply `SearchTriggeredDelay` before MatchSelection → MatchSelectionSearching; apply `SpinnerGoneDelay` before MatchSelectionSearching → MatchSelectionReady; apply `MatchOpenedDelay` before MatchSelectionReady → MatchLoaded.

Each transition fires `OnStateChanged`. Cancellation propagates `OperationCanceledException`; state remains at the value held before the cancelled delay. The `MatchInfo` parameter is accepted (it will be used by S-005 and S-007 for logging) but no logic depends on its value in this step.

### R-5: StopAsync

Resets the service to `NotRunning` regardless of current state. If `CurrentState` is already `NotRunning`, return immediately without applying `StopDelay` and without firing `StateChanged` (idempotent no-op). Otherwise, apply `StopDelay` (from options, pass `ct`), then directly set `_currentState = NotRunning` and call `OnStateChanged(NotRunning)`. This is an intentional bypass of Stateless trigger validation — the `Stop` trigger is only valid from `MatchLoaded` in the state machine, but the mock must succeed from any state per M-SC-12. The semaphore guard applies here too (see R-2).

### R-6: GetTeamNamesAsync

Returns a fixed `MatchTeams` record with two placeholder team names (e.g., "Home XI" and "Away XI"). No delay, no state transition, no semaphore. This provides a working implementation for M-SC-1 until S-005 can provide match-specific team names if needed.

### R-7: RefreshScoreboardAsync

Returns a completed `Task`. No delay, no state transition, no semaphore.

### R-8: Logging

Add `ILogger` calls throughout the three lifecycle methods:
- `LogInformation` at method entry for `LaunchAndLoginAsync`, `LoadMatchAsync`, and `StopAsync`
- `LogDebug` after each intermediate state transition (Launching, LoginScreen, MatchSelectionSearching, MatchSelectionReady)
- `LogInformation` on method completion (MatchSelection reached, MatchLoaded reached, NotRunning reached)

Log calls must include the current state in structured log properties where appropriate. These calls represent the initial instrumentation layer; S-007 will review and finalise log levels.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` from solution root exits 0 with zero errors and zero warnings |
| AC-2 | `dotnet test` from solution root exits 0; all pre-existing tests pass; new tests in Mock.Tests also pass |
| AC-3 | `LaunchAndLoginAsync` with zero delays fires `StateChanged` events in order: Launching, LoginScreen, MatchSelection; `CurrentState` is MatchSelection on completion |
| AC-4 | `LoadMatchAsync` from MatchSelection with zero delays fires `StateChanged` events in order: MatchSelectionSearching, MatchSelectionReady, MatchLoaded; `CurrentState` is MatchLoaded on completion |
| AC-5 | `LoadMatchAsync` from MatchLoaded with zero delays fires `StateChanged` events in order: MatchSelection, MatchSelectionSearching, MatchSelectionReady, MatchLoaded |
| AC-6 | `StopAsync` from MatchLoaded with zero delay fires `StateChanged(NotRunning)` and returns `CurrentState == NotRunning` |
| AC-7 | `StopAsync` from NotRunning (no prior lifecycle) returns `CurrentState == NotRunning` immediately without firing `StateChanged` |
| AC-8 | Cancellation: `LaunchAndLoginAsync` with non-zero `LaunchDelay`, cancelled before the delay elapses → throws `OperationCanceledException`; `CurrentState` remains `NotRunning` |
| AC-9 | Concurrency: `LaunchAndLoginAsync` started with a non-zero delay; before it completes a second call to `LaunchAndLoginAsync` → second call throws `InvalidOperationException`; first call completes normally |
| AC-10 | Timing: `LaunchAndLoginAsync` followed by `LoadMatchAsync` with all delays `TimeSpan.Zero`; elapsed time from first call to final `StateChanged(MatchLoaded)` event is less than 500ms (M-SC-10) |
| AC-11 | `GetTeamNamesAsync` completes successfully and returns a `MatchTeams` instance with non-empty `HomeTeam` and `AwayTeam` strings |
| AC-12 | `RefreshScoreboardAsync` completes successfully without throwing |
| AC-13 | Code review: no numeric literals in `MockPcsProAutomationService`; all delay values read from `_options` |
| AC-14 | Cancellation of `LoadMatchAsync`: call from MatchSelection with non-zero `SearchTriggeredDelay`, cancel before delay elapses → throws `OperationCanceledException`; `CurrentState` remains MatchSelection |
| AC-15 | Cancellation of `StopAsync`: call from MatchLoaded with non-zero `StopDelay`, cancel before delay elapses → throws `OperationCanceledException`; `CurrentState` remains MatchLoaded |
| AC-16 | Entry-state guard for `LaunchAndLoginAsync`: call from MatchSelection (any non-NotRunning state) → throws `InvalidOperationException` without modifying state |
| AC-17 | Entry-state guard for `LoadMatchAsync`: call from Launching (any state other than MatchSelection or MatchLoaded) → throws `InvalidOperationException` without modifying state |
| AC-18 | Cross-method concurrency: `LaunchAndLoginAsync` started with a non-zero delay; before it completes, call `LoadMatchAsync` → `LoadMatchAsync` throws `InvalidOperationException` |

---

## 5. Verification Table

| AC | Verification Method |
|---|---|
| AC-1 | `dotnet build` — exit 0, zero warnings |
| AC-2 | `dotnet test` — exit 0 |
| AC-3 | Unit test: subscribe to StateChanged, call LaunchAndLoginAsync (zero delays), assert event sequence and final CurrentState |
| AC-4 | Unit test: drive to MatchSelection first; subscribe to StateChanged; call LoadMatchAsync; assert event sequence and final CurrentState |
| AC-5 | Unit test: drive to MatchLoaded first; subscribe to StateChanged; call LoadMatchAsync; assert event sequence: MatchSelection, MatchSelectionSearching, MatchSelectionReady, MatchLoaded |
| AC-6 | Unit test: drive to MatchLoaded; call StopAsync; assert StateChanged fired with NotRunning; assert CurrentState == NotRunning |
| AC-7 | Unit test: fresh service (NotRunning); subscribe to StateChanged; call StopAsync; assert CurrentState == NotRunning; assert StateChanged event count == 0 |
| AC-8 | Unit test: CancellationTokenSource; LaunchAndLoginAsync with non-zero LaunchDelay and token; cancel immediately; assert OperationCanceledException; assert CurrentState == NotRunning |
| AC-9 | Unit test: start LaunchAndLoginAsync with non-zero delay; before it completes call LaunchAndLoginAsync again; assert second throws InvalidOperationException; first completes normally |
| AC-10 | Unit test: all delays zero; Stopwatch wrapping LaunchAndLoginAsync + LoadMatchAsync; assert elapsed < 500ms |
| AC-11 | Unit test: await GetTeamNamesAsync; assert HomeTeam and AwayTeam non-null, non-empty |
| AC-12 | Unit test: await RefreshScoreboardAsync; assert no exception |
| AC-13 | Code review: no bare numeric literals in MockPcsProAutomationService.cs |
| AC-14 | Unit test: drive to MatchSelection; CancellationTokenSource; LoadMatchAsync with non-zero SearchTriggeredDelay and token; cancel; assert OperationCanceledException; assert CurrentState == MatchSelection |
| AC-15 | Unit test: drive to MatchLoaded; CancellationTokenSource; StopAsync with non-zero StopDelay and token; cancel; assert OperationCanceledException; assert CurrentState == MatchLoaded |
| AC-16 | Unit test: drive to MatchSelection; call LaunchAndLoginAsync; assert InvalidOperationException; assert CurrentState unchanged |
| AC-17 | Unit test: fresh service (NotRunning); call LoadMatchAsync with a MatchInfo; assert InvalidOperationException; assert CurrentState == NotRunning |
| AC-18 | Unit test: start LaunchAndLoginAsync with non-zero delay; before it completes call LoadMatchAsync; assert LoadMatchAsync throws InvalidOperationException |

---

## 6. Risk Notes

### Concurrency test reliability (AC-9, AC-18)
Tests that rely on a first call being mid-delay when the second is issued are sensitive to timing. Using a short delay (e.g., 100ms) and issuing the second call immediately (no sleep in the test) is generally reliable. For maximum CI robustness, the test may use a `TaskCompletionSource` or `SemaphoreSlim` signal triggered from within a test-injected delay to guarantee the second call is issued while the semaphore is definitely held. The spec does not prescribe the exact synchronisation mechanism — that is a delivery decision.

### State consistency after OperationCanceledException (AC-8, AC-14, AC-15)
Cancellation tests must use a delay long enough to be reliably cancelled (e.g., 200ms delay, cancel after 10ms) to avoid false passes on fast machines where the delay completes before the cancel token fires.

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | Sonnet 4.6, GPT-4.1 | NEEDS REVIEW — 1 HIGH + 4 MED + 3 LOW (Sonnet); 1 HIGH + 2 MED + 2 LOW (GPT) |
| R2 | 2026-04-10 | Sonnet 4.6 | **APPROVED** — all R1 findings verified resolved; 0 regressions |

### R1 Findings Applied (v0.1 → v0.2)

| Ref | Finding | Disposition | Resolution |
|---|---|---|---|
| Sonnet HIGH-1 | AC-9 "after cancellation" clause factually wrong | Accept | Removed clause; AC-9 now reads "first call completes normally" |
| Sonnet MED-2 | SelectMatchAsync/RetryAsync traceability gap vs IS-002 | Accept | IS-002 naming note added to Out of Scope section |
| Sonnet MED-3 | Cancellation untested for LoadMatchAsync and StopAsync | Accept | AC-14 and AC-15 added; corresponding verification rows added |
| Sonnet MED-4 | Entry-state guards unspecified | Accept | Preconditions added to R-3 and R-4; AC-16 and AC-17 added |
| Sonnet MED-5 | AC-7 ambiguous about StateChanged firing when already NotRunning | Accept | R-5 updated: idempotent no-op when already NotRunning (no delay, no event); AC-7 updated to assert event count == 0 |
| Sonnet LOW-6 | Cross-method concurrency untested | Accept | AC-18 added |
| Sonnet LOW-7 | AC-5 format inconsistency | Accept | AC-5 rewritten to match AC-3/AC-4 format |
| Sonnet LOW-8 | Section 6 concurrency note endorses fragile design | Accept | Risk note updated; TaskCompletionSource approach recommended |
| GPT HIGH-1 | "immediately" thread context ambiguity | Downgrade LOW | OnStateChanged is synchronous; clarification added to R-1 (inline, no await between assignment and call) |
| GPT MED-2 | Event arg must match new state | Downgrade LOW | R-1 already states "with the new state value as the event argument" — clarification added inline |
| GPT MED-3 | Exception message content | Defer to Delivery | Exact message text is implementation detail |
| GPT LOW-4 | Logger property names | Defer to Delivery | Property names are implementation detail |
| GPT LOW-5 | Negative cases for invalid states | Covered | Addressed by entry-state guard additions (Sonnet MED-4) |
