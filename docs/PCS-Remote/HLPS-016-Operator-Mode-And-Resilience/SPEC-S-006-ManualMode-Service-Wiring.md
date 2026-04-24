# SPEC-S-006: Manual-Mode Wiring in Backend Services

| Field | Value |
|---|---|
| **Document** | SPEC-S-006-ManualMode-Service-Wiring.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-24 |
| **Step ID** | S-006 |
| **Governing HLPS** | HLPS-016-Operator-Mode-And-Resilience.md (DRAFT v0.1) |
| **Governing IS** | IS-016-Operator-Mode-And-Resilience.md (DRAFT v0.1) |
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
- R3: Subscribe to `ManualModeChanged`. When manual mode transitions from active → inactive (`isActive == false`), trigger one immediate capture. The implementation approach for the immediate capture (e.g., signalling the loop via a `TaskCompletionSource`, `ManualResetEventSlim`, or simply calling `CaptureAndBroadcast` directly from the event handler) is a delivery-phase decision.
- R4: The immediate-resume capture must be guarded: if the polling loop is not running (state machine not in `MatchLoaded`), the resume capture is a no-op.
- R5: If the immediate-resume capture fails (exception), log a warning and continue — do not crash the loop or change state.

### 2.2 `PcsProAutomationService` — Manual-Mode Gate

**Requirements:**

- R6: Accept `IManualModeService` via constructor injection (added to the existing constructor parameters and to `AutomationDependencies` if using that record pattern).
- R7: At the entry of every automation-driving public method, check `IsManualModeActive`. If active, log at `Information` level (include the method name and current state) and return early with a default/empty result. No state transition, no exception.
- R8: The methods that are gated (must check manual mode) include at minimum:
  - `RefreshScoreboardAsync`
  - `CaptureScoreboardImageAsync`
  - `ChangeMatchAsync`
  - `GetTeamNamesAsync`
  - `GetTodaysMatchesAsync`
  - `LoadMatchAsync`
  - `UseCurrentMatchAsync`
  - The health-check poll tick (internal method)
- R9: The methods that **bypass** the gate (must NOT check manual mode) are:
  - `LaunchAndLoginAsync`
  - `StopAsync`
  - `RecoverFromErrorAsync` (or equivalent error recovery method)
  - `DismissErrorAsync`
  - These lifecycle methods remain fully functional in manual mode so the operator can recover from errors or restart PCS Pro from the dashboard while manual mode is active.
- R10: The gate check must occur before the `_operationLock` acquisition. If manual mode is active, the method returns immediately without touching the lock, logging, or raising any state change.
- R11: Return values for gated methods when manual mode is active:
  - Methods returning `Task`: return `Task.CompletedTask`.
  - Methods returning `Task<byte[]>`: return empty array.
  - Methods returning `Task<IReadOnlyList<MatchInfo>>`: return empty list.
  - Methods returning `Task<MatchTeams>`: return empty `MatchTeams`.
  - The delivery phase determines exact return values for each method based on existing null-safety contracts.

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

- **T6 — Gated methods return early:** For each gated method (R8), set manual mode active and call the method. Assert it returns the default/empty result without acquiring the operation lock or changing state.
- **T7 — Lifecycle methods bypass gate:** For each bypass method (R9), set manual mode active and call the method. Assert it executes normally (does not return early).
- **T8 — Gate check before lock:** Set manual mode active and call a gated method. Assert the operation lock was never acquired.
- **T9 — Normal operation unaffected:** Manual mode is inactive. Assert gated methods execute normally.

---

## 4. Acceptance Criteria

| ID | Criterion | Verification |
|----|-----------|-------------|
| AC-1 | Scoreboard timer tick is skipped when manual mode active (logged at `Verbose`) | T1 |
| AC-2 | Resuming from manual mode fires one immediate capture | T2 |
| AC-3 | Resume capture is guarded — no-op when loop not running | T3 |
| AC-4 | Resume capture failure is logged and swallowed | T4 |
| AC-5 | All gated automation methods return early when manual mode active | T6 |
| AC-6 | `LaunchAndLoginAsync`, `StopAsync`, `RecoverFromErrorAsync`, `DismissErrorAsync` execute in manual mode | T7 |
| AC-7 | Gate check occurs before operation lock acquisition | T8 |
| AC-8 | No `Thread.Sleep` in tests | Code review |
| AC-9 | No test regressions | Full test run |

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
