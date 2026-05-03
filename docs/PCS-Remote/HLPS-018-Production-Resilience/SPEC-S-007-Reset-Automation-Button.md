# SPEC-S-007 — Reset Automation Button in Debug Panel

| Field        | Value |
|--------------|-------|
| **Status**   | APPROVED |
| **Step**     | S-007 |
| **Author**   | Agent |
| **Created**  | 2026-05-03 |
| **Governs**  | IS-018 S-007; HLPS-018 SC7 |
| **Depends**  | S-005 (Serilog sink — DONE) |

---

## Context

When automation enters an unrecoverable state (e.g., PCS Pro hangs, state machine is stuck in Error, Retry/Dismiss don't help), the operator currently has no escape hatch from the web UI. The only option is to physically access the server and restart the TrayHost process. SC7 requires a "Reset Automation" button in the debug panel that terminates PCS Pro and relaunches the full automation sequence.

The debug panel already has an expand/unlock mechanism (PIN-protected). The Reset button is placed inside the expanded+unlocked view alongside the log display, giving the operator access to both diagnostics and the reset action in one place.

---

## Requirements

### R1 — Button placement and visibility

The Reset Automation button must appear inside the debug panel's expanded and unlocked content area.

**Acceptance Criteria:**

- **AC-1:** The button renders only when the debug panel is expanded AND unlocked (`_expanded && _unlocked`).
- **AC-2:** The button is styled distinctly (e.g., danger/warning style) to indicate it is a destructive operation.
- **AC-3:** The button text is "Reset Automation".

### R2 — Confirmation dialog

The button must require explicit confirmation before executing the reset sequence.

**Acceptance Criteria:**

- **AC-4:** Clicking the button triggers the existing `IConfirmDialogService` with a clear warning message describing what the reset will do.
- **AC-5:** Any non-`true` confirmation result (`false` or `null`/dismissed) aborts the reset with no side effects.

### R3 — Reset sequence

The reset performs a full stop-and-relaunch cycle. The sequence is:

1. Stop active YouTube stream (best-effort — swallow any failure).
2. Stop automation (`StopAsync`) — this terminates PCS Pro and returns state to `NotRunning`.
3. Relaunch automation (`LaunchAndLoginAsync`) — starts PCS Pro and logs in.

**Acceptance Criteria:**

- **AC-6:** If a YouTube stream is active (`CurrentStatus` is not `Idle` and not `Error`), `StopStreamAsync` is called. Failure is logged but does not abort the reset.
- **AC-7:** `StopAsync` is called to terminate PCS Pro. This uses the existing force-kill fallback (10-second graceful timeout).
- **AC-8:** After `StopAsync` completes, `LaunchAndLoginAsync` is called to restart the full automation sequence.
- **AC-9:** The reset sequence does NOT use `BeginOperation`/`MarkComplete` — it is a privileged operation that bypasses the operation coordinator lock. Any in-flight operation will naturally fail when the process is killed.

### R4 — Status feedback and error handling

The operator must see what is happening during the reset.

**Acceptance Criteria:**

- **AC-10:** A status message is displayed during the reset sequence (e.g., "Stopping stream...", "Stopping PCS Pro...", "Relaunching...").
- **AC-11:** For aborting failures (StopAsync, LaunchAndLoginAsync throwing), the exception message, current step, and current `PcsProState` are displayed to the operator. For non-aborting failures (StopStreamAsync throwing), the exception is logged but the reset continues — an optional transient warning may be shown.
- **AC-12:** If StopAsync or LaunchAndLoginAsync throws, the exception message, the failed step, and the current `PcsProState` are shown. The button becomes re-clickable so the operator can retry or take other action.
- **AC-13:** During the reset sequence, the Reset button is disabled to prevent double-clicks.
- **AC-14:** After a successful reset sequence completes (all steps succeed), the Reset button is re-enabled and the status message is cleared or shows a brief success confirmation.

### R5 — Component dependencies

**Acceptance Criteria:**

- **AC-15:** `DebugSection.razor` injects `IPcsProAutomationService`, `IYouTubeLiveStreamService`, and `IConfirmDialogService`.
- **AC-16:** No new services or interfaces are created — the reset uses existing service methods.

---

## Test Cases

### TC-1 — Button visible when expanded and unlocked

**Setup:** Render `DebugSection` with no PIN configured (auto-unlocked).  
**Action:** Click Debug toggle to expand.  
**Assert:** A button with text "Reset Automation" is present inside the debug content area.

### TC-2 — Button hidden when collapsed

**Setup:** Render `DebugSection`, do not expand.  
**Assert:** No Reset Automation button is rendered.

### TC-3 — Confirmation dialog shown on click

**Setup:** Render expanded `DebugSection`. Mock `IConfirmDialogService` to return `false` (cancel).  
**Action:** Click Reset Automation.  
**Assert:** `IConfirmDialogService.ConfirmAsync` was called. Neither `StopStreamAsync`, `StopAsync`, nor `LaunchAndLoginAsync` were called.

### TC-4 — Full reset sequence executes on confirm

**Setup:** Render expanded `DebugSection`. Mock confirm to return `true`. Mock stream service `CurrentStatus` = `Live`. Mock all service methods to succeed.  
**Action:** Click Reset Automation, confirm.  
**Assert:** `StopStreamAsync` called, then `StopAsync` called, then `LaunchAndLoginAsync` called, in that order. After completion, the Reset button is re-enabled (not disabled) and the status message area is either empty or shows a success confirmation.

### TC-5 — Stream stop failure does not abort reset

**Setup:** Mock `StopStreamAsync` to throw. Mock confirm = `true`. Stream status = `Live`.  
**Action:** Click Reset, confirm.  
**Assert:** `StopStreamAsync` was called and threw. `StopAsync` was still called. `LaunchAndLoginAsync` was still called. The exception was logged (verifiable at the component level via ILogger or the automation log service; Serilog routing to the debug panel is covered by S-005).

### TC-6 — StopAsync failure shows error and allows retry

**Setup:** Mock `StopAsync` to throw. Mock confirm = `true`. Stream status = `Idle`.  
**Action:** Click Reset, confirm.  
**Assert:** Error message is displayed including the current `PcsProState`. `LaunchAndLoginAsync` was NOT called. The Reset button is re-enabled for retry.

### TC-7 — Button disabled during reset

**Setup:** Mock `StopAsync` to delay (use `TaskCompletionSource`). Mock confirm = `true`.  
**Action:** Click Reset, confirm.  
**Assert:** While StopAsync is pending, the Reset button is disabled.

### TC-8 — Skips stream stop when Idle

**Setup:** Mock stream service `CurrentStatus` = `Idle`. Mock confirm = `true`.  
**Action:** Click Reset, confirm.  
**Assert:** `StopStreamAsync` was NOT called. `StopAsync` was called. `LaunchAndLoginAsync` was called.

### TC-9 — Skips stream stop when Error

**Setup:** Mock stream service `CurrentStatus` = `Error`. Mock confirm = `true`.  
**Action:** Click Reset, confirm.  
**Assert:** `StopStreamAsync` was NOT called. `StopAsync` was called. `LaunchAndLoginAsync` was called.

### TC-10 — LaunchAndLoginAsync failure shows error and allows retry

**Setup:** Mock `StopAsync` to succeed. Mock `LaunchAndLoginAsync` to throw. Mock confirm = `true`. Stream status = `Idle`.  
**Action:** Click Reset, confirm.  
**Assert:** Error message is displayed including the current `PcsProState`. The Reset button is re-enabled for retry. `StopAsync` was called exactly once (not re-called).

### TC-11 — Button hidden when PIN-locked

**Setup:** Render `DebugSection` with a PIN configured. Click Debug toggle to show PIN prompt. Do NOT enter PIN.  
**Assert:** No Reset Automation button is rendered.

---

## Out of Scope

- Adding a global reset accessible outside the debug panel.
- Timeout logic for the reset sequence (StopAsync already has its own 10-second force-kill timeout). `LaunchAndLoginAsync`'s internal timeouts are trusted; adding an outer timeout would add complexity without clear benefit for an emergency escape-hatch operation.
- Persisting reset history or audit trail beyond Serilog logging.

---

## Review History

| Round | Reviewers | Outcome | Notes |
|-------|-----------|---------|-------|
| R1 | GPT 5.4, Sonnet 4.6 | REVISE | GPT: 4 findings (1H, 2M, 1L). Sonnet: 7 findings (1H, 3M, 3L). Accepted: show current PcsProState on failure (GPT-F1), clarify non-true confirm = cancel (GPT-F2), add PIN-locked visibility test (GPT-F4), add TC-9 Error skip path (Sonnet-F2), add TC-10 LaunchAndLoginAsync failure (Sonnet-F3), clarify AC-11 aborting vs non-aborting failures (Sonnet-F5), clean TC-5 Serilog assertion boundary (Sonnet-F6), add success state AC (Sonnet-F7). Deferred: coordinator bypass test (GPT-F3, AC-9 is clear), async void boundary try-catch (Sonnet-F4, owned by S-006). Downgraded: LaunchAndLoginAsync timeout (Sonnet-F1, accepted risk in Out of Scope). |
| R2 | GPT 5.4, Sonnet 4.6 | — | GPT: 2 findings (1M, 1L). Sonnet: 3 findings (1M, 2L). Accepted: TC-6/TC-10 must assert PcsProState (GPT R2-1 + Sonnet F1), TC-4 must assert success message (Sonnet F2), align AC-11/AC-12 terminology (Sonnet F3). Deferred to Delivery: null confirm test (GPT R2-2). |
