# SPEC-S-004: Error Dismiss Implementation and UI

| Field | Value |
|---|---|
| **Document** | SPEC-S-004-ErrorDismissImplAndUI.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-21 |
| **Governing IS** | IS-011-Operational-UX.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-011-Operational-UX.md v0.4 (APPROVED) |
| **Step** | S-004 |
| **Branch** | `feature/IS-011-S-004-error-dismiss` |

---

## 1. Objective

Implement the `DismissAsync` method on both Mock and Real automation services, and add a Dismiss button to the `ErrorDisplay` component. This completes the error dismiss feature end-to-end: the contract was added in S-001, and this step delivers the implementation and UI.

---

## 2. Scope

### In Scope

- `MockPcsProAutomationService.DismissAsync` implementation
- `PcsProAutomationService.DismissAsync` implementation (Real / FlaUI)
- Dismiss button added to `ErrorDisplay.razor` alongside the existing Retry button
- bUnit tests for Dismiss button rendering and click behaviour
- Compiler error resolution (both services must build after adding the new method)

### Out of Scope

- State machine changes (already delivered in S-001: `Dismiss` trigger, `Error → NotRunning`)
- Interface changes (already delivered in S-001: `DismissAsync` on `IPcsProAutomationService`)
- Automation log entries for dismiss (deferred to S-008 instrumentation)

---

## 3. Requirements

### R-1: MockPcsProAutomationService.DismissAsync

The mock service implements `DismissAsync` following the same structural pattern as `RetryAsync` minus the re-launch:
- Acquires the semaphore (throws if already held)
- Guards on `Error` state (throws `InvalidOperationException` if not in Error)
- Clears `LastErrorReason`
- Transitions to `NotRunning`
- Releases the semaphore
- No process-related side effects (no re-launch, no delay)

### R-2: PcsProAutomationService.DismissAsync

The real service implements `DismissAsync` following the same pattern as `RetryAsync` steps 1–5 (state transition, crash watcher teardown) but without step 6 (re-launch):
- Logs entry at `Information` level
- Guards on `Error` state (throws `InvalidOperationException` if not in Error)
- Acquires `_operationLock`, fires `Dismiss` trigger, clears `_lastErrorReason`, releases lock
- Flushes state events
- Cancels and awaits crash watcher (if active)
- Cancels health poll (if active)
- Does **not** call `LaunchAndLoginAsync` — the system stays in `NotRunning`

### R-3: ErrorDisplay Dismiss Button

A Dismiss button is added to the `ErrorDisplay` component with the following behaviour:
- Renders alongside the Retry button when in `Error` state
- Click calls `AutomationService.DismissAsync()` directly — **no coordinator lock, no manual mode guard**
- The button is always enabled when visible (never disabled by coordinator lock or manual mode)
- Uses a distinct CSS class (`error-display__dismiss-btn`) for styling
- A `_dismissing` flag prevents duplicate in-flight calls on double-click (this is a simple UI guard, not a coordinator lock — it does not contradict the IS-011 exemption)
- On click failure (exception), logs the error and shows a notification

### R-4: No Process Termination on Dismiss

Neither the mock nor real `DismissAsync` kills or terminates the PCS Pro process. The system transitions to `NotRunning` (meaning "not managing lifecycle") — the process may still be running externally. This matches the semantic definition established in HLPS-011 §2.

---

## 4. Design Notes

### Dismiss vs Retry Symmetry

`DismissAsync` mirrors `RetryAsync` structurally for the Error → NotRunning transition but deliberately omits the re-launch. In the real service, it also tears down the crash watcher and health poll (since the service is no longer managing the process lifecycle).

### No Coordinator / Manual Mode Guards

Dismiss is exempt from both guards because it is not an automation operation — it does not start or restart any automation sequence. The operator is explicitly acknowledging and clearing an error. This was established in IS-011 S-004 and HLPS-011 §2.

### Crash Watcher Teardown Ordering

`DismissAsync` follows the same teardown ordering as `RetryAsync`: transition under lock → flush events → cancel crash watcher → cancel health poll. The window between lock release and crash watcher cancellation is safe because the crash watcher's cancellation token is checked on each poll iteration, and the state machine rejects invalid triggers from NotRunning with an exception that the watcher's catch block handles. This is the same ordering RetryAsync has used without issue.

### State Drift on Dismiss Click

If the UI shows the Error state but the server-side state has already moved (e.g., a background health alert triggered a recovery), `DismissAsync` will throw `InvalidOperationException`. This surfaces as an error notification via TC-4's path — acceptable UX since the error display will disappear on the next state update anyway.

---

## 5. Test Cases

### Unit Tests (PcsRemote.Core.Tests — already delivered)

The state machine tests for Dismiss (valid Error → NotRunning, invalid from other states) were delivered in S-001. No new state machine tests are needed.

### bUnit Tests (PcsRemote.Web.Tests)

#### TC-1: `ErrorState_RendersDismissButton`
**Assert:** When in Error state, the component renders a button with CSS class `error-display__dismiss-btn`.

#### TC-2: `NonErrorState_DismissButtonNotRendered`
**Assert:** When in any non-Error state, no element with CSS class `error-display__dismiss-btn` exists.

#### TC-3: `DismissClick_CallsDismissAsync`
**Assert:** Clicking the Dismiss button calls `AutomationService.DismissAsync` exactly once. Neither `BeginOperation` nor `MarkComplete` is called.

#### TC-4: `DismissClick_DismissAsyncThrows_ShowsNotification`
**Assert:** When `DismissAsync` throws, a notification with Error severity is shown and the exception is logged.

#### TC-5: `DismissClick_WhenCoordinatorLocked_StillCallsDismissAsync`
**Assert:** With `operationInProgress` true, the Dismiss button is not disabled. Clicking it calls `DismissAsync` exactly once. Neither `BeginOperation` nor `MarkComplete` is called.

#### TC-6: `DismissClick_WhenManualModeActive_StillCallsDismissAsync`
**Assert:** With `manualModeActive` true, the Dismiss button is not disabled. Clicking it calls `DismissAsync` exactly once. No manual-mode warning notification is shown.

#### TC-7: `DismissClick_DoesNotCallLaunchAndLogin`
**Assert:** After clicking Dismiss, `LaunchAndLoginAsync` is never called on the automation service mock. This verifies the defining distinction between Dismiss and Retry.

#### TC-8: `DismissSuccess_StateChangesToNotRunning_ErrorDisplayDisappears`
**Assert:** After a successful dismiss, when the automation service raises `StateChanged` with `NotRunning`, the error display component renders empty (no `.error-display` element).

#### TC-9: `DismissClick_WhileInFlight_SuppressesSecondCall`
**Assert:** When `DismissAsync` is configured to return a non-completing task, clicking the Dismiss button a second time does not invoke `DismissAsync` again. Total invocation count remains exactly one.

#### TC-10: `DismissClick_AfterFailure_GuardResetsAndAllowsRetry`
**Assert:** When `DismissAsync` throws on the first click, the `_dismissing` guard resets. A subsequent click successfully invokes `DismissAsync` again.

---

## 6. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `MockPcsProAutomationService.DismissAsync` transitions from Error to NotRunning and clears `LastErrorReason` |
| AC-2 | `MockPcsProAutomationService.DismissAsync` throws `InvalidOperationException` when not in Error state |
| AC-3 | `PcsProAutomationService.DismissAsync` transitions from Error to NotRunning, clears error reason, tears down crash watcher and health poll, and does not call `LaunchAndLoginAsync` |
| AC-4 | `PcsProAutomationService.DismissAsync` throws `InvalidOperationException` when not in Error state |
| AC-5 | Neither service kills the PCS Pro process on dismiss |
| AC-6 | Dismiss button renders in ErrorDisplay when in Error state, alongside Retry |
| AC-7 | Dismiss button click does not acquire coordinator lock or check manual mode |
| AC-8 | Dismiss button remains enabled regardless of coordinator lock or manual mode state |
| AC-9 | TC-1 through TC-10 pass |
| AC-12 | Dismiss-click exception is logged and surfaced as an error notification |
| AC-10 | All pre-existing tests pass (91 Core, existing Web) |
| AC-11 | All projects build with 0 errors, 0 warnings |

---

## 7. Commit Strategy

Delivered on `feature/IS-011-S-004-error-dismiss`; squash-merged to `master` after adversarial code review approval.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Opus 4.7, GPT 5.4 | REQUEST CHANGES (test gaps, teardown ordering, double-click) |
| R2 | 2026-04-21 | Opus 4.7, GPT 5.4 | Opus APPROVE; GPT REQUEST CHANGES (dismissing guard tests) |
| R3 | 2026-04-21 | GPT 5.4 | APPROVED |
