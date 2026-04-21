# SPEC-S-011: Use Current Match (GAP-010)

| Field | Value |
|---|---|
| **Document** | SPEC-S-011-Use-Current-Match.md |
| **Status** | IN REVIEW |
| **Version** | 0.2 |
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

> **Traceability note:** HLPS-010 §2.8 uses the name `AttachToLoadedMatch` and suggests the transition from `LoginScreen` or a new `Attaching` state. This spec shortens the trigger to `AttachToMatch` (unambiguous — there is no other attach trigger) and uses `NotRunning` as the source state. `NotRunning` is correct because the automation service has not yet engaged with PCS Pro — there is no login screen visible in the attach scenario. Similarly, the method is named `UseCurrentMatchAsync` rather than HLPS's `AttachToCurrentMatchAsync` because `Use` better describes the operator intent (leveraging what's already running, not binding/adopting).

The trigger is only permitted from `NotRunning` because:
- From `MatchLoaded`: the service is already attached — use `ChangeMatchAsync` or `GetTeamNamesAsync` instead.
- From intermediate states (`Launching`, `LoginScreen`, `MatchSelection*`): an operation is already in progress — attaching mid-flow would corrupt state.
- From `Error`: the operator must first `RetryAsync` (→ `NotRunning`), then can attach.

### 2.2 New Service Method

Add `UseCurrentMatchAsync` to `IPcsProAutomationService`. This method:

1. **Verifies** the service is in `NotRunning` state (throws `InvalidOperationException` otherwise).
2. **Detects** that PCS Pro is running by locating the main window.
3. **Detects** that a match is loaded by checking for the match-loaded detection signal (the existing `IsMatchLoaded()` pattern using the `ScoreSummaryPane` element). The `ScoreSummaryPane` (`twdScoreSummary`) is only present when a match is fully loaded and rendered; it is the most reliable single signal. HLPS-010 §2.8 additionally suggests window-title and status-bar checks — these are supplementary confirmation signals that can be added as future hardening but are not required for the initial attach implementation because `ScoreSummaryPane` presence already implies a fully loaded match.
4. **Fires** the `AttachToMatch` trigger → state becomes `MatchLoaded`.
5. **Reads team names** by delegating to the existing team name reading flow, returning `MatchTeams`.

If detection fails (PCS Pro not running, no match loaded), the method throws `InvalidOperationException` with a descriptive message. The state machine does not transition — it remains in `NotRunning`.

If team name reading fails after the state transition, the existing error handling in the team name flow transitions to `Error` state. The method returns a sentinel `MatchTeams` with empty `TeamNameInfo` values: `new MatchTeams(new TeamNameInfo(string.Empty, string.Empty), new TeamNameInfo(string.Empty, string.Empty))`. This is consistent with `GetTeamNamesCoreAsync` error paths which construct empty `TeamNameInfo` on failure.

### 2.3 LoadedMatch Property

After a successful attach, `LoadedMatch` is `null` because no `MatchInfo` is available (the match was not selected from the grid — there is no `MatchId`, `MatchType`, or `MatchDate`). This is a known limitation documented in the HLPS: "Match identity verification is deferred — the operator's assertion is trusted."

The existing `OnTransitioned` callback in `PcsProAutomationService` sets `_loadedMatch = _pendingLoadedMatch` on entry to `MatchLoaded`. Since `UseCurrentMatchAsync` does not set `_pendingLoadedMatch` (there is no `MatchInfo` to set), `_loadedMatch` remains `null` by design.

The Web UI must handle `LoadedMatch == null` when in `MatchLoaded` state gracefully (e.g., displaying team names from `MatchTeams` instead of full match info).

### 2.4 Process Lifecycle

The attach flow does **not** take ownership of the PCS Pro process. Unlike `LaunchAndLoginAsync` — which starts PCS Pro and retains the `Process` handle for `StopAsync` and crash monitoring — `UseCurrentMatchAsync` connects to an externally-managed instance. Specifically:

- **`_process` remains `null`** — the service did not start PCS Pro and does not adopt its process handle.
- **`StopAsync` behaviour:** When `_process` is `null`, `StopAsync` transitions state to `NotRunning` but does not attempt to close or kill PCS Pro. This is correct: the operator manages the process lifecycle of an externally-started instance.
- **Crash detection:** The `Process.Exited` crash watcher is not started because there is no `_process` to monitor. If PCS Pro exits after attach, the service remains in `MatchLoaded` until the next automation operation fails (at which point existing error handling transitions to `Error` state). Proactive crash detection after attach is deferred to a future hardening step.

### 2.5 Concurrency

The method uses the same `_operationLock` semaphore as other automation operations to prevent concurrent FlaUI access. The interlocked pattern follows existing service methods.

### 2.6 Existing Reusable Components

The following existing components are reused without modification:
- `PcsProWindowLocator.FindMainWindow()` — process/window detection
- `FlaUiMatchSelectionAutomation.IsMatchLoaded()` — match-loaded detection signal
- `ITeamNamesAutomation` — team name reading (OpenTeamsDialog, ReadHome/AwayTeamName, TryCloseTeamsDialog)
- `UIAutomationHelpers` — safe element search and dialog handling

### 2.7 Consumer Updates

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
| R-7 | State machine tests in `PcsRemote.Core.Tests` cover the new trigger: valid transition, invalid triggers from other states. |
| R-8 | Service-level tests in `PcsRemote.Automation.Tests` cover: happy path, PCS Pro not running, no match loaded, wrong state, concurrent operation rejection. |
| R-9 | Build: 0 warnings, 0 errors. All existing tests pass. |

---

## 4. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `PcsProTrigger.AttachToMatch` exists. |
| AC-2 | Firing `AttachToMatch` from `NotRunning` transitions to `MatchLoaded`. Firing from any other state throws `InvalidOperationException`. |
| AC-3 | `IPcsProAutomationService.UseCurrentMatchAsync(CancellationToken)` exists and returns `Task<MatchTeams>`. |
| AC-4 | Happy path: when PCS Pro is running with a match loaded, `UseCurrentMatchAsync` transitions state to `MatchLoaded` and returns `MatchTeams` where both `Home` and `Away` have non-empty `TeamName` strings. |
| AC-5 | When PCS Pro is not running (main window not found), `UseCurrentMatchAsync` throws `InvalidOperationException`. State remains `NotRunning`. |
| AC-6 | When PCS Pro is running but no match is loaded (detection signal absent), `UseCurrentMatchAsync` throws `InvalidOperationException`. State remains `NotRunning`. |
| AC-7 | When called from a state other than `NotRunning`, `UseCurrentMatchAsync` throws `InvalidOperationException`. |
| AC-8 | `LoadedMatch` is `null` after a successful attach (no `MatchInfo` available). |
| AC-9 | `MockPcsProAutomationService.UseCurrentMatchAsync` transitions to `MatchLoaded` and returns mock team data. |
| AC-10 | All existing tests pass with 0 warnings, 0 errors. |
| AC-11 | When another automation operation is in progress (semaphore held), `UseCurrentMatchAsync` throws `InvalidOperationException`. |

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| RISK-1 | `LoadedMatch == null` after attach may cause `InvalidOperationException` in downstream service-layer consumers (e.g., `YouTubeLiveStreamService` throws when `LoadedMatch` is null) and `NullReferenceException` in UI code | AC-8 documents this. YouTube streaming after attach is explicitly out of scope (§6). Web UI defensiveness is a future UI step. The service contract already declares `LoadedMatch` as nullable (`MatchInfo?`). |
| RISK-2 | `IsMatchLoaded()` depends on `IMatchSelectionAutomation` which is a different interface than `ITeamNamesAutomation` — both need to be available in `UseCurrentMatchAsync` | Both interfaces are already injected into `PcsProAutomationService`. No new dependencies. |
| RISK-3 | Race condition: PCS Pro match unloaded between detection and team name read | Existing team name error handling (try-catch → Error state) covers this. No additional mitigation needed. |
| RISK-4 | YouTube streaming (`StartStreamingAsync`) is non-functional after attach because it requires non-null `LoadedMatch` to construct the broadcast title | Explicitly deferred. YouTube streaming after attach requires either a minimal sentinel `MatchInfo` or YouTube service changes to accept `MatchTeams` directly. Tracked in Out of Scope §6. |
| RISK-5 | No crash detection after attach — if PCS Pro exits unexpectedly, the service remains in `MatchLoaded` until the next operation fails | Acceptable for initial implementation. The next automation operation (e.g., `GetTeamNamesAsync`, `RefreshScoreboardAsync`) will fail and transition to `Error` state. Proactive crash monitoring after attach is deferred (§6). |

---

## 6. Out of Scope

- Web UI changes for `LoadedMatch == null` display (future UI enhancement).
- Match identity verification (operator trust per IS-010).
- Attach from states other than `NotRunning` (e.g., `Error` — use `RetryAsync` first).
- `MatchInfo` population from window title parsing (requires format knowledge, deferred).
- YouTube streaming after attach — requires `LoadedMatch` for broadcast title construction; deferred until a sentinel `MatchInfo` or YouTube service refactoring is implemented (see RISK-4).
- Process adoption and crash monitoring after attach — the service does not take ownership of externally-started PCS Pro processes (see §2.4, RISK-5).
- Additional match-loaded detection signals (window title check, status bar "Up to Date") beyond `ScoreSummaryPane` — deferred as supplementary hardening (see §2.2 step 3).

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Opus, GPT-5.4 | NEEDS REVIEW — 1 CRITICAL (process lifecycle), 2 HIGH (YouTube/LoadedMatch, detection signals), 4 MEDIUM, 4 LOW, 1 INFO. All 11 findings accepted and fixed in v0.2. |
| R2 | 2026-04-21 | — | Pending |
