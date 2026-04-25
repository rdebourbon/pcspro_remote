# SPEC-S-006: Manual-Mode Wiring in Backend Services

| Field | Value |
|---|---|
| **Document** | SPEC-S-006-ManualMode-Service-Wiring.md |
| **Status** | DRAFT |
| **Version** | 0.2 |
| **Date** | 2026-04-24 |
| **Step ID** | S-006 |
| **Governing HLPS** | HLPS-016-Operator-Mode-And-Resilience.md (APPROVED v0.4) |
| **Governing IS** | IS-016-Operator-Mode-And-Resilience.md (APPROVED v0.5) |
| **Branch** | `feature/016-S-006-manual-mode-wiring` |

---

## 1. Purpose

This step wires the existing `IManualModeService` into the two backend services that currently do not respect manual mode:

1. **`ScoreboardPollingService`** — the periodic capture loop should skip ticks when manual mode is active and fire one immediate capture when mode is deactivated.
2. **`PcsProAutomationService`** — all automation-driving public methods should short-circuit when manual mode is active, while lifecycle methods bypass the gate.

Today, manual mode only disables Blazor UI buttons. After this step, the backend services also respect the gate, ensuring no FlaUI automation runs while the operator is using PCS Pro manually.

---

## 2. Scope

### 2.1 `ScoreboardPollingService` — Manual-Mode Gate

**Requirements:**

- R1: Accept `IManualModeService` via constructor injection (both the production constructor and the test-only constructor).
- R2: On each timer tick (inside the `RunLoopAsync` loop), check `IsManualModeActive` before calling `CaptureAndBroadcast`. If active, log at `Verbose` level ("Scoreboard capture skipped — manual mode active") and skip the capture. Do not stop the loop — the timer continues ticking.
- R3: Subscribe to `ManualModeChanged`. When manual mode transitions from active → inactive (`isActive == false`), trigger one immediate capture. The resume capture must be serialised with normal polling work (not called directly from the event handler) and must re-check manual mode at execution time.
- R4: The immediate-resume capture must be guarded: if the polling loop is not running (state machine not in `MatchLoaded`), the resume capture is a no-op.
- R5: If the immediate-resume capture fails (exception), log a warning and continue — do not crash the loop or change state.
- R5b: Manual-mode event subscriptions must be scoped to service lifetime and cleaned up on stop/dispose.

### 2.2 `PcsProAutomationService` — Manual-Mode Gate

**Requirements:**

- R6: `IManualModeService` must be available to the automation service. Injection mechanism is a delivery-phase decision.
- R7: At the entry of every automation-driving public method, check `IsManualModeActive`. If active, log at `Information` level (include the operation name and current state) and return early with a contract-safe benign result. No state transition, no exception.
- R8: All automation-driving public methods are gated, including streaming operations and the health-check poll tick. The only exceptions are lifecycle/recovery methods listed in R9.
- R9: Lifecycle and recovery methods **bypass** the gate — they execute normally regardless of manual-mode state. These include launch, stop, error recovery, and error dismissal operations. The operator must be able to recover from errors or restart PCS Pro while manual mode is active.
- R10: The manual-mode gate must run before any operation serialisation primitive, state mutation, or FlaUI interaction. If manual mode is active, the method returns immediately without acquiring locks or touching state.
- R11: Gated methods return contract-safe benign results when manual mode is active (empty collections, empty arrays, completed tasks, etc.). Exact return values are determined during delivery based on existing null-safety contracts.
- R12: If an automation operation is already executing when manual mode is toggled on, that operation runs to completion. Manual mode only prevents **new** operations from starting.
- R13: Health-check ticks are skipped while manual mode is active and resume on the next scheduled tick after deactivation. No state transition or alert is raised while skipped.

### 2.3 Out of Scope

- Blazor component changes (S-007).
- Hotkey registration (S-008).
- Changes to `IManualModeService` interface (completed in S-001).

---

## 3. Test Strategy

### 3.1 `ScoreboardPollingService` Tests

- **T1 — Skip when manual mode active:** Configure manual mode as active. Start the loop and advance the timer. Assert `CaptureAndBroadcast` is not called.
- **T2 — Resume when manual mode deactivated:** Start with manual mode active, skip ticks, then deactivate. Assert one immediate capture is fired.
- **T3 — Resume guard — loop not running:** Deactivate manual mode when the polling loop is not active (state is not `MatchLoaded`). Assert no capture is attempted.
- **T4 — Resume capture failure is swallowed:** Configure the immediate-resume capture to throw. Assert the exception is logged as a warning and the loop continues on the next tick.
- **T5 — Normal operation unaffected:** Manual mode is inactive throughout. Assert captures fire on every timer tick as before.

### 3.2 `PcsProAutomationService` Tests

- **T6 — Gated methods return early:** For each gated method (R8), set manual mode active and call the method. Assert it returns a benign result without changing state.
- **T7 — Lifecycle methods bypass gate:** For each bypass method (R9), set manual mode active and call the method. Assert it executes normally (does not return early).
- **T8 — Gate check before lock:** Set manual mode active and call a gated method. Assert the operation lock was never acquired.
- **T9 — Normal operation unaffected:** Manual mode is inactive. Assert gated methods execute normally.
- **T10 — Health-check skipped in manual mode:** Set manual mode active. Assert health-check tick is skipped — no state transition or alert raised.
- **T11 — Health-check resumes after deactivation:** Deactivate manual mode. Assert health-check runs on the next scheduled tick.
- **T12 — In-flight operation completes:** Start an automation operation, toggle manual mode on during execution. Assert the in-flight operation completes successfully. Assert subsequent calls are rejected.
- **T13 — Streaming methods gated:** Set manual mode active. Assert streaming start/stop methods return early.

---

## 4. Acceptance Criteria

| ID | Criterion | Verification |
|----|-----------|-------------|
| AC-1 | Scoreboard timer tick is skipped when manual mode active (logged at `Verbose`) | T1 |
| AC-2 | Resuming from manual mode fires one immediate capture, serialised with normal polling | T2 |
| AC-3 | Resume capture is guarded — no-op when loop not running | T3 |
| AC-4 | Resume capture failure is logged and swallowed | T4 |
| AC-5 | All automation-driving public methods (including streaming) return early when manual mode active | T6, T13 |
| AC-6 | Lifecycle/recovery methods execute normally in manual mode | T7 |
| AC-7 | Gate check occurs before operation serialisation primitives | T8 |
| AC-8 | Health-check tick is skipped in manual mode and resumes after deactivation | T10, T11 |
| AC-9 | In-flight operations complete when manual mode is toggled on during execution | T12 |
| AC-10 | Manual-mode subscriptions cleaned up on service stop/dispose | Code review |
| AC-11 | No `Thread.Sleep` in tests | Code review |
| AC-12 | No test regressions | Full test run |

---

## 5. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Immediate-resume capture races with a manual-mode re-enable (operator toggles rapidly) | Low | Low | The capture checks `IsManualModeActive` at execution time. If mode was re-enabled during the capture, the next tick will skip. No harmful side effect. |
| Adding constructor parameter to `PcsProAutomationService` impacts many existing test setups | Medium | Low | Mechanical — add `IManualModeService` mock to test factories. Delivery phase handles. |
| Health-check poll is an internal method — gate placement needs care | Low | Low | The health check already runs inside the operation lock with `Wait(0)`. The manual-mode check precedes the lock try-acquire. |

---

## 6. Documentation Updates

- No external documentation changes for this step. Documentation is updated in S-009.

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | DRAFT — not yet submitted for review |
