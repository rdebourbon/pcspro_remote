# SPEC-S-005 — Error Display Component and Retry Flow

| Field | Value |
|---|---|
| **Document** | SPEC-S-005-ErrorDisplay.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Step ID** | IS-005 S-005 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-005-Hardening.md (APPROVED) |
| **Governing HLPS** | HLPS-005-Hardening.md (APPROVED) |
| **Dependencies** | SPEC-S-001 (APPROVED), SPEC-S-002 (APPROVED), SPEC-S-004 (APPROVED) |

---

## 1. Context and Problem

When the automation service transitions to `PcsProState.Error`, the UI currently shows nothing — no explanation and no recovery path. Users have no way to know why automation failed or how to restart it without refreshing the page. This violates H-SC-6 (error display), H-SC-7 (retry restarts automation), and H-SC-8 (all five error paths surface a reason string).

Additionally, the retry action is an automation operation like any other — it must participate in the hub-driven operation locking established by S-004 so that all connected browsers see a consistent disabled state while the retry is in-flight.

---

## 2. Requirements

### R-1 — `RetryAsync()` on `IPcsProAutomationService`

A new `RetryAsync(CancellationToken ct = default)` method is added to `IPcsProAutomationService`. Its contract:

- Fires the `Retry` trigger on the state machine (transitioning from `Error` to `NotRunning`). If the current state is not `Error`, this throws `InvalidOperationException` (the state machine enforces this).
- Immediately calls `LaunchAndLoginAsync()` to restart the full automation sequence.
- This method is the sole code path for error recovery re-launch. `AutoLaunchService` is a one-shot startup service and does NOT listen for re-entry into `NotRunning`.

The method must be implemented by:
- `MockPcsProAutomationService` in `PcsRemote.Automation.Mock` — resets internal state to `NotRunning`, then calls `LaunchAndLoginAsync()`.
- `NotSupportedPcsProAutomationService` in `PcsRemote.Automation.Mock` — adds a `RetryAsync` stub that throws `NotSupportedException`, consistent with the class's existing pattern.
- The real `FlaUI` automation service (when built in a future IS step) — fires the Retry trigger, then calls `LaunchAndLoginAsync()`.

### R-2 — `ErrorDisplay` Blazor Component

A new `ErrorDisplay.razor` component is created in `PcsRemote.Web/Shared/`. It requires the following injected services:
- `IPcsProAutomationService` — for state, `LastErrorReason`, and `RetryAsync()`
- `IManualModeService` — for manual mode guard
- `IOperationCoordinatorService` — for hub-driven operation locking
- `NotificationService` — for warning/error notifications
- `ILogger<ErrorDisplay>` — for structured error logging

It:

#### R-2a — Rendering

- Renders only when the current `PcsProState` is `Error`. In all other states it renders nothing (empty fragment).
- When rendered, shows:
  - A red error indicator (🔴) or equivalent CSS class (`error-display--icon`).
  - The `LastErrorReason` string from `IPcsProAutomationService`. If `LastErrorReason` is `null` or empty, shows a fallback string `"An unexpected error occurred."`.
  - A Retry button (CSS class `error-display__retry-btn`).

#### R-2b — Retry Button State

The Retry button is disabled when either:
- `_operationInProgress` is `true` (coordinator lock is held by any browser), **or**
- `_manualModeActive` is `true`.

Both conditions are tracked via service events using the **subscribe-before-snapshot** pattern (subscribe to event first, then read current state — the established project pattern).

The button's `title` attribute is set to `"Automation in progress\u2026"` when `_operationInProgress` is true, and to `"Automation is paused"` when `_manualModeActive` is true, and empty otherwise. When both conditions are true simultaneously, `_operationInProgress` takes precedence for the tooltip text.

#### R-2c — Retry Click Handler

The retry click handler (`OnRetryClickedAsync`) follows this sequence:

1. **Top guard**: if `_operationInProgress` is true → return immediately (defense-in-depth).
2. **Manual mode guard**: if `IManualModeService.IsManualModeActive` is true → show a warning notification with the reason string `"Automation is paused — disable manual mode before retrying"` → return.
3. Declare a local `acquired = false` flag **before** the `try` block.
4. Enter the `try` block. The **first statement inside `try`** is `acquired = CoordinatorService.BeginOperation()`. If `acquired` is `false` (another browser owns the lock) → return immediately (the `finally` block will not call `MarkComplete` because `acquired` is still `false`).
5. Call `await AutomationService.RetryAsync()`. Catch unexpected exceptions: log at `Error` level and show an error notification.
6. In `finally`: call `CoordinatorService.MarkComplete()` only if `acquired` is `true`.

This structure mirrors the `ChangeMatchButton` pattern established in S-004.

#### R-2d — Event Subscription and Disposal

The component subscribes to three service events:
- `IPcsProAutomationService.StateChanged`
- `IManualModeService.ManualModeChanged`
- `IOperationCoordinatorService.OperationInProgressChanged`

Each uses the **subscribe-before-snapshot** pattern in `OnInitialized`.

The `OnOperationInProgressChanged` and `OnManualModeChanged` event handlers follow the full **`_disposed` + `ObjectDisposedException`** pattern (same as `ChangeMatchButton`):
- Check `if (!_disposed)` before invoking `InvokeAsync(StateHasChanged)`.
- Wrap `InvokeAsync` in `try { } catch (ObjectDisposedException) { }`.

`OnStateChanged` follows the same pattern as the other handlers in this component.

`Dispose()` sets `_disposed = true` and unsubscribes all three event handlers.

#### R-2e — Logger

The component injects `ILogger<ErrorDisplay>` and logs at `Error` level when `RetryAsync` throws an unexpected exception.

### R-3 — Index.razor Integration

`Index.razor` is updated to render `<ErrorDisplay />` when `_currentState == PcsProState.Error`. The ErrorDisplay component manages its own state subscriptions independently; Index does not pass state props into it.

The `@else if (_currentState == PcsProState.Error)` branch is added after the existing `MatchLoaded` branch in the page template.

> The `@else if` mount condition and ErrorDisplay's own R-2a empty-fragment guard are both intentional — the mount condition avoids needless subscription overhead in non-Error states; the internal guard provides defence-in-depth if the component is ever placed unconditionally in future.

### R-4 — MockPcsProAutomationService: `RetryAsync` implementation

The mock's `RetryAsync`:
- Acquires the internal semaphore (same pattern as other lifecycle operations).
- Validates that `_currentState == PcsProState.Error`; throws `InvalidOperationException` if not.
- Clears `LastErrorReason`.
- Transitions to `NotRunning` and fires the `StateChanged` event.
- Releases the semaphore, then calls `LaunchAndLoginAsync()`.

> **Note**: The mock calls `LaunchAndLoginAsync()` directly (not recursively through `RetryAsync`) after releasing the semaphore to avoid deadlock via the semaphore.

### R-5 — `ForcedErrorMode` on Mock Options

`MockPcsProOptions` already has `ForcedErrorMode`. This spec does not change mock options — the existing five modes (`LaunchingToLoginScreen`, `LoginScreenToMatchSelection`, `MatchSelectionSearchingToReady`, `MatchSelectionToLoaded`, `UnexpectedDialog`) are sufficient to test all five H-SC-8 paths.

---

## 3. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | `ErrorDisplay` renders visibly when state is `Error`; renders nothing in all other states. |
| AC-2 | `LastErrorReason` is displayed; `null`/empty reason falls back to `"An unexpected error occurred."` |
| AC-3 | Retry button is disabled and shows correct tooltip when `_operationInProgress` is `true`. |
| AC-4 | Retry button is disabled and shows correct tooltip when `_manualModeActive` is `true`. |
| AC-5 | Clicking Retry when manual mode is active shows a warning notification and does not call `RetryAsync`. |
| AC-6 | Clicking Retry when not in-progress and not manual-mode calls `RetryAsync` exactly once. |
| AC-7 | `BeginOperation()` returning `false` causes the retry handler to return without calling `RetryAsync` or `MarkComplete`. |
| AC-8 | When `RetryAsync` throws, `MarkComplete()` is still called in the `finally` block. |
| AC-9 | Retry button disabled in a second browser when a retry is in-flight from a first browser (coordinator event propagation). |
| AC-10 | `Dispose()` unsubscribes all three event handlers. |
| AC-11 | `RetryAsync()` on the mock: transitions `Error → NotRunning`, fires `StateChanged(NotRunning)`, then progresses through the normal launch sequence. |
| AC-12 | Calling `RetryAsync()` from a non-`Error` state throws `InvalidOperationException`. |
| AC-13 | `ErrorDisplay` renders empty (no error indicator, no retry button) after state transitions out of `Error` following a successful retry (H-SC-7 flow). |
| AC-14 | `ErrorDisplay` renders the red error indicator element (CSS class `error-display--icon`) when in Error state. |

---

## 4. Test Plan

All tests run in the existing test projects. New tests are added alongside existing patterns; no new test projects are created.

### 4.1 `IPcsProAutomationService` / `MockPcsProAutomationService` Tests (`PcsRemote.Automation.Mock.Tests`)

| TC | Name pattern | Scenario | Assertion |
|----|-------------|---------|-----------|
| TC-1 | `RetryAsync_FromError_TransitionsToNotRunning_ThenLaunches` | Mock in Error state → call `RetryAsync()` | State transitions: Error → NotRunning → Launching → … (launch sequence starts) |
| TC-2 | `RetryAsync_FromNonError_ThrowsInvalidOperationException` | Mock in `MatchLoaded` state → call `RetryAsync()` | `InvalidOperationException` thrown |
| TC-3 | `RetryAsync_ClearsLastErrorReason` | Mock in Error with reason → call `RetryAsync()` | `LastErrorReason` is `null` after transition (before re-launch completes) |

### 4.2 `ErrorDisplay` Component Tests (`PcsRemote.Web.Tests`)

| TC | Name pattern | Scenario | Assertion |
|----|-------------|---------|-----------|
| TC-4 | `ErrorState_RendersComponent_WithReasonString` | State = Error, LastErrorReason = "test reason" | Component DOM contains "test reason", retry button, and `error-display--icon` element |
| TC-5 | `NonErrorState_RendersNothing` | State = NotRunning / MatchLoaded / etc. | Component renders empty fragment (no error-display elements) |
| TC-6 | `ErrorState_NullReason_ShowsFallback` | State = Error, LastErrorReason = null | DOM shows `"An unexpected error occurred."` |
| TC-7 | `OperationInProgress_RetryButtonDisabled_TooltipPresent` | Coordinator fires in-progress = true | Retry button has `disabled` attr; `title` = "Automation in progress…" |
| TC-8 | `ManualModeActive_RetryButtonDisabled` | Manual mode fired = true | Retry button has `disabled` attr |
| TC-9 | `RetryClick_ManualModeActive_RejectsWithNotification` | Click retry when manual mode = true | Warning notification shown; `RetryAsync` never called |
| TC-10 | `RetryClick_Succeeds_CallsRetryAsync` | Normal click | `RetryAsync` called once; `BeginOperation` called once; `MarkComplete` called once |
| TC-11 | `RetryClick_BeginOperationReturnsFalse_DoesNotCallRetryAsync` | BeginOperation returns false | `RetryAsync` never called; `MarkComplete` never called |
| TC-12 | `RetryAsyncThrows_MarkCompleteCalledInFinally` | `RetryAsync` throws | `MarkComplete` called despite exception |
| TC-13 | `CoordinatorFiresInProgress_RetryButtonDisabled` | Coordinator event fires true | Retry button becomes disabled |
| TC-14 | `CoordinatorFiresComplete_RetryButtonReenables` | Coordinator event fires false | Retry button becomes enabled |
| TC-15 | `Dispose_UnsubscribesAllEvents` | Call Dispose() | All three `VerifyRemove` assertions pass |
| TC-16 | `StateChangedToNonError_ComponentBecomesEmpty` | StateChanged fires with NotRunning | Component renders empty fragment (ErrorDisplay hidden) |

### 4.3 Error Injection Tests — H-SC-8 coverage (`PcsRemote.Automation.Mock.Tests`)

| TC | Name pattern | ForcedErrorMode | Assertion |
|----|-------------|-----------------|-----------|
| TC-17 | `ForcedError_LaunchingToLoginScreen_SurfacesReason` | `LaunchingToLoginScreen` | `LastErrorReason` is non-null/non-empty after error |
| TC-18 | `ForcedError_LoginScreenToMatchSelection_SurfacesReason` | `LoginScreenToMatchSelection` | Same |
| TC-19 | `ForcedError_MatchSelectionSearchingToReady_SurfacesReason` | `MatchSelectionSearchingToReady` | Same |
| TC-20 | `ForcedError_MatchSelectionToLoaded_SurfacesReason` | `MatchSelectionToLoaded` | Same |
| TC-21 | `ForcedError_UnexpectedDialog_SurfacesReason` | `UnexpectedDialog` | Same |

> TC-17 through TC-21 verify H-SC-8 completeness — every named error path surfaces a reason string via `LastErrorReason`.

---

## 5. Branch and Commit Strategy

- Branch: `feature/IS-005-S-005-error-display`
- Commits are small and frequent, scoped to logical units:
  - Interface change (`RetryAsync` added to `IPcsProAutomationService`)
  - Mock implementation (`MockPcsProAutomationService.RetryAsync`)
  - Core component (`ErrorDisplay.razor` production code)
  - Index.razor integration
  - Test suite additions

---

## 6. Review History

| Round | Reviewer | Verdict | Key Findings |
|-------|----------|---------|--------------|
| R1 | Opus (claude-opus-4.6) | APPROVED | MEDIUM: contradictory BeginOperation placement; MEDIUM: tooltip priority undefined; LOW: NotSupportedPcsProAutomationService omitted from R-1; LOW: injection list not consolidated |
| R1 | GPT (gpt-5.4) | NEEDS REVIEW | HIGH: contradictory handler sequence (→ MEDIUM post-triage, same as Opus finding); HIGH: manual-mode string variant (→ DISMISSED, 2/3 passed); MEDIUM: red indicator not tested; MEDIUM: multi-browser locking (→ DISMISSED, AC-9+TC-13/14 sufficient); MEDIUM: H-SC-8 weak checks (→ DISMISSED, exact strings are E2E concern) |
| R1 | Sonnet (claude-sonnet-4.6) | APPROVED | MEDIUM: contradictory BeginOperation placement (same as Opus); LOW: tooltip priority undefined (same as Opus); LOW: AC-13 miswording; LOW: dual rendering guard unexplained |
| R1 | Triage | APPROVED | F-3 fixed (R-2c rewritten unambiguously); F-4 fixed (tooltip priority stated); F-5 fixed (AC-14 added, TC-4 updated); F-8 fixed (AC-13 reworded); F-9 fixed (R-3 parenthetical added); F-10 fixed (NotSupportedPcsProAutomationService added to R-1); F-11 fixed (injection summary added to R-2) |
