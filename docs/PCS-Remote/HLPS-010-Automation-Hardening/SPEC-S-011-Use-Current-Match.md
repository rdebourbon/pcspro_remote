# SPEC-S-011: Use Current Match (GAP-010)

| Field | Value |
|---|---|
| **Document** | SPEC-S-011-Use-Current-Match.md |
| **Status** | IN REVIEW |
| **Version** | 0.1 |
| **Date** | 2026-04-21 |
| **Step ID** | S-011 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-011-use-current-match` |

---

## 1. Problem

The current automation lifecycle always starts from `NotRunning` and requires the full launch → login → match selection → load match sequence. If PCS Pro is already running with a match loaded (e.g., operator started it manually, or a previous session crashed and the service restarted), there is no way to reconnect. The operator must close PCS Pro and re-run the full flow, losing time and interrupting any active match.

---

## 2. Solution

### 2.1 New State Machine Trigger

Add an `AttachToMatch` trigger to `PcsProTrigger`. Configure the state machine to permit this trigger from `NotRunning` → `MatchLoaded`. This bypasses the full launch/login/selection sequence when PCS Pro is already running with a match open.

The trigger is only permitted from `NotRunning` because:
- From `MatchLoaded`: the service is already attached — use `ChangeMatchAsync` or `GetTeamNamesAsync` instead.
- From intermediate states (`Launching`, `LoginScreen`, `MatchSelection*`): an operation is already in progress — attaching mid-flow would corrupt state.
- From `Error`: the operator must first `RetryAsync` (→ `NotRunning`), then can attach.

### 2.2 New Service Method

Add `UseCurrentMatchAsync` to `IPcsProAutomationService`. This method:

1. **Verifies** the service is in `NotRunning` state (throws `InvalidOperationException` otherwise).
2. **Detects** that PCS Pro is running by locating the main window.
3. **Detects** that a match is loaded by checking for the match-loaded detection signal (the existing `IsMatchLoaded()` pattern using the `ScoreSummaryPane` element).
4. **Fires** the `AttachToMatch` trigger → state becomes `MatchLoaded`.
5. **Reads team names** by delegating to the existing team name reading flow, returning `MatchTeams`.

If detection fails (PCS Pro not running, no match loaded), the method throws `InvalidOperationException` with a descriptive message. The state machine does not transition — it remains in `NotRunning`.

If team name reading fails after the state transition, the existing error handling in the team name flow transitions to `Error` state. The method returns a sentinel `MatchTeams` with empty `TeamNameInfo` values (consistent with `GetTeamNamesAsync` error paths).

### 2.3 LoadedMatch Property

After a successful attach, `LoadedMatch` is `null` because no `MatchInfo` is available (the match was not selected from the grid — there is no `MatchId`, `MatchType`, or `MatchDate`). This is a known limitation documented in the HLPS: "Match identity verification is deferred — the operator's assertion is trusted."

The Web UI must handle `LoadedMatch == null` when in `MatchLoaded` state gracefully (e.g., displaying team names from `MatchTeams` instead of full match info).

### 2.4 Concurrency

The method uses the same `_operationLock` semaphore as other automation operations to prevent concurrent FlaUI access. The interlocked pattern follows existing service methods.

### 2.5 Existing Reusable Components

The following existing components are reused without modification:
- `PcsProWindowLocator.FindMainWindow()` — process/window detection
- `FlaUiMatchSelectionAutomation.IsMatchLoaded()` — match-loaded detection signal
- `ITeamNamesAutomation` — team name reading (OpenTeamsDialog, ReadHome/AwayTeamName, TryCloseTeamsDialog)
- `UIAutomationHelpers` — safe element search and dialog handling

### 2.6 Consumer Updates

- `IPcsProAutomationService`: new method added.
- `PcsProAutomationService`: new method implemented.
- `MockPcsProAutomationService`: mock implementation — transitions to `MatchLoaded` and returns mock team names.
- Web UI: No changes required in this step. The UI already handles `MatchLoaded` state. `LoadedMatch == null` handling is a UI concern deferred to a future step if needed.

---

## 3. Requirements

| ID | Requirement |
|---|---|
| R-1 | `PcsProTrigger` enum gains an `AttachToMatch` member. |
| R-2 | `PcsProStateMachine` permits `AttachToMatch` from `NotRunning` → `MatchLoaded`. |
| R-3 | `IPcsProAutomationService` gains a `UseCurrentMatchAsync` method returning `Task<MatchTeams>`. |
| R-4 | `PcsProAutomationService.UseCurrentMatchAsync` verifies `NotRunning` state, detects PCS Pro running with match loaded, fires `AttachToMatch`, and reads team names. |
| R-5 | If PCS Pro is not running or no match is loaded, `UseCurrentMatchAsync` throws `InvalidOperationException` without transitioning state. |
| R-6 | `MockPcsProAutomationService` implements `UseCurrentMatchAsync` with a mock transition and mock team name return. |
| R-7 | State machine tests cover the new trigger: valid transition, invalid triggers from other states. |
| R-8 | Service-level tests cover: happy path, PCS Pro not running, no match loaded, wrong state. |
| R-9 | Build: 0 warnings, 0 errors. All existing tests pass. |

---

## 4. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `PcsProTrigger.AttachToMatch` exists. |
| AC-2 | Firing `AttachToMatch` from `NotRunning` transitions to `MatchLoaded`. Firing from any other state throws `InvalidOperationException`. |
| AC-3 | `IPcsProAutomationService.UseCurrentMatchAsync(CancellationToken)` exists and returns `Task<MatchTeams>`. |
| AC-4 | Happy path: when PCS Pro is running with a match loaded, `UseCurrentMatchAsync` transitions state to `MatchLoaded` and returns `MatchTeams` with populated team names. |
| AC-5 | When PCS Pro is not running (main window not found), `UseCurrentMatchAsync` throws `InvalidOperationException`. State remains `NotRunning`. |
| AC-6 | When PCS Pro is running but no match is loaded (detection signal absent), `UseCurrentMatchAsync` throws `InvalidOperationException`. State remains `NotRunning`. |
| AC-7 | When called from a state other than `NotRunning`, `UseCurrentMatchAsync` throws `InvalidOperationException`. |
| AC-8 | `LoadedMatch` is `null` after a successful attach (no `MatchInfo` available). |
| AC-9 | `MockPcsProAutomationService.UseCurrentMatchAsync` transitions to `MatchLoaded` and returns mock team data. |
| AC-10 | All existing tests pass with 0 warnings, 0 errors. |

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| RISK-1 | `LoadedMatch == null` after attach may cause NullReferenceException in UI code that assumes it's set when in `MatchLoaded` | AC-8 documents this. Web UI defensiveness is a future UI step. The service contract already declares `LoadedMatch` as nullable. |
| RISK-2 | `IsMatchLoaded()` depends on `IMatchSelectionAutomation` which is a different interface than `ITeamNamesAutomation` — both need to be available in `UseCurrentMatchAsync` | Both interfaces are already injected into `PcsProAutomationService`. No new dependencies. |
| RISK-3 | Race condition: PCS Pro match unloaded between detection and team name read | Existing team name error handling (try-catch → Error state) covers this. No additional mitigation needed. |

---

## 6. Out of Scope

- Web UI changes for `LoadedMatch == null` display (future UI enhancement).
- Match identity verification (operator trust per IS-010).
- Attach from states other than `NotRunning` (e.g., `Error` — use `RetryAsync` first).
- `MatchInfo` population from window title parsing (requires format knowledge, deferred).

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | — | Pending |
