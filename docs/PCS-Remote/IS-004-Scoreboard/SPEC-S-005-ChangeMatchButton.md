# SPEC-S-005 — Change Match Button

| Field | Value |
|---|---|
| **Spec** | SPEC-S-005-ChangeMatchButton.md |
| **Step** | IS-004 S-005 |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-004-Scoreboard.md v0.3 (APPROVED) |
| **Dependencies** | S-001 (`IScoreboardService.ClearCache` delivered `2bc5784`), S-003 (ScoreboardPreview `3c1566c`), S-004 (RefreshScoreboardButton `1cb54d7`) |

---

## 1. Purpose

Add a "Change Match" button to the match-loaded section of the UI. When clicked, the button shows a Radzen confirmation dialog. On confirmation, the scoreboard cache is cleared and the automation service transitions the application back to the `MatchSelection` state, ready for a new match to be selected. On cancellation, nothing changes.

---

## 2. Scope

**In scope:**
- New `ChangeMatchAsync` method on `IPcsProAutomationService` in `PcsRemote.Core`
- `MockPcsProAutomationService` implementation of `ChangeMatchAsync`
- New `ChangeMatchButton.razor` Razor component in `PcsRemote.Web/Shared/`
- Embedding the component in the `MatchLoaded` section of `Pages/Index.razor` after `<RefreshScoreboardButton />`
- bUnit tests in `PcsRemote.Web.Tests/Shared/ChangeMatchButtonTests.cs` (TC-1 through TC-9, TC-11)
- bUnit integration test TC-10 in `PcsRemote.Web.Tests/Pages/IndexTests.cs`
- Unit tests for `ChangeMatchAsync` in `PcsRemote.Automation.Mock.Tests` (TC-12, TC-13)

**Out of scope:**
- Playwright E2E tests (S-006)
- Visual styling beyond structural CSS class hooks required by tests
- Changes to `ScoreboardPollingService` — stops automatically on state leave
- Any changes to `ScoreboardPreview` or `RefreshScoreboardButton`
- Accessibility (WCAG, ARIA, keyboard navigation) — deferred to a future cross-cutting concern

---

## 3. Interface Extension — `IPcsProAutomationService`

A new `ChangeMatchAsync` method is added to `IPcsProAutomationService`:

**Purpose:** Initiates the change-match sequence, transitioning the application from `MatchLoaded` back to `MatchSelection` so a new match can be selected.

**Precondition:** Current state must be `MatchLoaded`. Calling from any other state throws `InvalidOperationException`.

**Outcome:** On success, state is `MatchSelection` and `StateChanged` has been raised with `MatchSelection`. On failure, the method throws an exception — there is no silent failure mode. The `Error` state postcondition arises only if the underlying automation layer transitions via the state machine's Error path and throws.

**Signature shape:** Async, accepts an optional `CancellationToken`, returns `Task`.

**Mock behaviour:** Transitions directly from `MatchLoaded` to `MatchSelection` after the existing `ChangeMatchDelay` option. Fires `StateChanged` with `MatchSelection`. Uses the existing `_semaphore` guard (same pattern as other lifecycle operations).

---

## 4. Component — `ChangeMatchButton.razor`

### 4.1 Injected Dependencies

- `IPcsProAutomationService` — for `ChangeMatchAsync` and `StateChanged`
- `IScoreboardService` — for `ClearCache`
- `DialogService` (Radzen) — for the confirmation dialog
- `NotificationService` (Radzen) — for error notifications
- `ILogger<ChangeMatchButton>` — for error logging

### 4.2 Enabled/Disabled State

The button is enabled when `CurrentState == MatchLoaded` AND no operation is in progress. An `_operationInProgress` flag covers the entire click flow — from the moment the button is clicked until `ChangeMatchAsync` completes (success or failure). State is tracked locally using the subscribe-before-snapshot pattern (same as `RefreshScoreboardButton`).

`IsEnabled` = `_currentState == MatchLoaded && !_operationInProgress`

### 4.3 Click Flow

1. Set `_operationInProgress = true`; call `StateHasChanged()` (disables button immediately).
2. Show Radzen confirmation dialog: title `"Change Match"`, message `"Load a different match? PCS Pro will close and reopen to the match selection screen."` — await the result.
3. If the dialog returns `false` or `null` (cancel or dismiss): clear `_operationInProgress`, call `StateHasChanged()`, return. `ClearCache` and `ChangeMatchAsync` are NOT called. Steps 4–5 do not execute.
4. If the dialog returns `true` (confirm), execute the following within a `try/catch/finally` block (steps 4a–4c and step 5 are the only code inside this block — steps 1–3 run before it):
   a. Call `IScoreboardService.ClearCache()` — synchronous, infallible.
   b. `await AutomationService.ChangeMatchAsync()` wrapped in try/catch.
   c. On exception: log at `Error` level with the exception object; show an error notification with message `"Change match failed"`. Exception is NOT re-thrown.
5. `finally` (inside the `try/catch/finally` from step 4 only): clear `_operationInProgress`; call `StateHasChanged()`.

### 4.4 Disposal

Implements `IDisposable`. `Dispose` sets a `_disposed` flag, then unsubscribes from `IPcsProAutomationService.StateChanged`. No other cleanup is required.

### 4.5 `StateChanged` Handler

`async void` event handler. Updates `_currentState`, calls `InvokeAsync(StateHasChanged)` for circuit safety.

### 4.6 CSS Class Hook and Button Label

| Element | CSS class | Visible text |
|---|---|---|
| Button element | `change-match-button` | "Change Match" |

No other structural CSS requirements. Visual styling is out of scope.

---

## 5. `Index.razor` Integration

`<ChangeMatchButton />` is added inside the `MatchLoaded` section of `Index.razor`, after `<RefreshScoreboardButton />`. No parameters; self-contained via `@inject`.

`DialogService` is already registered in `Program.cs` via `AddRadzenComponents()`. The bUnit `BuildCtx` helper in `IndexTests.cs` must also register a `DialogService` instance to support TC-10.

---

## 6. Test Cases

All `ChangeMatchButton` tests use a `Mock<IDialogService>` (Moq) or a real `DialogService` with a setup — the choice is the delivery engineer's. Since `DialogService` is a concrete Radzen class without an interface, tests must inject the concrete instance and control its behaviour via a subclass or through bUnit's JSInterop. If the Radzen `DialogService` requires JSInterop, tests should use bUnit's `ctx.JSInterop.SetupVoid(...)` stubs as needed — this is a delivery concern.

### TC-1: Button enabled in MatchLoaded state

Render with `CurrentState = MatchLoaded`. Assert: `change-match-button` element is present and not disabled.

### TC-2: Button disabled in all non-MatchLoaded states

`[DataRow]` per non-MatchLoaded state: `NotRunning`, `Launching`, `LoginScreen`, `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, `Error`. Assert: button is disabled for each.

### TC-3: StateChanged to MatchLoaded enables button

Render with `CurrentState = NotRunning`. Raise `StateChanged` with `MatchLoaded`. Assert via `WaitForAssertion`: button is enabled.

### TC-4: StateChanged away from MatchLoaded disables button

Render with `CurrentState = MatchLoaded`. Raise `StateChanged` with `MatchSelection`. Assert via `WaitForAssertion`: button is disabled.

### TC-5: Click shows confirmation dialog with correct arguments

Render with `CurrentState = MatchLoaded`. Set up the dialog service to return `false`. Click the button. Assert via `WaitForAssertion`: the confirmation dialog was invoked exactly once with message `"Load a different match? PCS Pro will close and reopen to the match selection screen."` and title `"Change Match"`.

### TC-6: Cancel — no ClearCache, no ChangeMatchAsync (both false and null paths)

Implemented as `[DataRow]` with dialog return values `false` and `null`. For each row: click the button. Assert via `WaitForAssertion`: `ClearCache` NOT called; `ChangeMatchAsync` NOT called.

### TC-7: Confirm — ClearCache called before ChangeMatchAsync

Set up dialog to return `true`. Set up `ChangeMatchAsync` to return `Task.CompletedTask`. Click the button. Assert via `WaitForAssertion`:
- `ClearCache` was called exactly once
- `ChangeMatchAsync` was called exactly once
- `ClearCache` was called before `ChangeMatchAsync` (use a call-order tracking list appended to in each stub callback)

### TC-8: ChangeMatchAsync exception — logs, notifies, button re-enables

Use a verifiable `Mock<ILogger<ChangeMatchButton>>`. Set up dialog to return `true`. Set up `ChangeMatchAsync` to throw. Click the button. Assert via `WaitForAssertion`:
- Error notification emitted with message `"Change match failed"`
- Logger received a `LogError` call with the exact thrown exception instance (`It.Is<Exception>(e => e == thrown)`)
- No exception propagates to the test runner
- Button element is present and not disabled (`_currentState` is still `MatchLoaded`)

### TC-9: Button disabled during in-progress operation

Render with `CurrentState = MatchLoaded`. Set up dialog to return `true`. Use a `TaskCompletionSource` for `ChangeMatchAsync` that does not complete during the test. Click the button. Assert via `WaitForAssertion`: button is disabled (operation in progress). The TCS is never completed; bUnit context disposal handles cleanup.

### TC-10: Index integration — all MatchLoaded elements present

bUnit test in `IndexTests.cs`. Render `Index.razor` driven to `MatchLoaded`. Assert: `change-match-button` present; `refresh-scoreboard-button` present; `scoreboard-placeholder` present; home/away team names present.

### TC-11: Dispose unsubscribes from StateChanged

Render the component. Call `cut.Instance.Dispose()` (established project convention — see `RefreshScoreboardButtonTests.cs` TC-9). Verify via Moq `VerifyRemove`: `StateChanged` handler was removed exactly once.

### TC-12: Mock ChangeMatchAsync transitions MatchLoaded → MatchSelection

Unit test in `PcsRemote.Automation.Mock.Tests`. Construct `MockPcsProAutomationService` in `MatchLoaded` state. Call `ChangeMatchAsync`. Assert: `StateChanged` fires with `MatchSelection`; `CurrentState == MatchSelection`.

### TC-13: Mock ChangeMatchAsync throws from non-MatchLoaded state

Unit test in `PcsRemote.Automation.Mock.Tests`. Construct `MockPcsProAutomationService` in `MatchSelection` state (or any non-`MatchLoaded` state). Call `ChangeMatchAsync`. Assert: `InvalidOperationException` thrown.

---

## 7. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `ChangeMatchAsync` method exists on `IPcsProAutomationService` |
| AC-2 | `MockPcsProAutomationService` implements `ChangeMatchAsync`, transitioning `MatchLoaded → MatchSelection` |
| AC-3 | `ChangeMatchButton` component exists in `PcsRemote.Web/Shared/` |
| AC-4 | Button enabled only when `CurrentState == MatchLoaded` AND no operation in progress |
| AC-5 | Click shows Radzen dialog with exact title `"Change Match"` and exact message text |
| AC-6 | Cancel (`false` or `null`): `ClearCache` and `ChangeMatchAsync` are NOT called |
| AC-7 | Confirm: `ClearCache` is called first, then `ChangeMatchAsync` |
| AC-8 | `ChangeMatchAsync` exception: error notification `"Change match failed"`, logs exception at Error level, does not re-throw |
| AC-9 | `Dispose` unsubscribes from `StateChanged` |
| AC-10 | `<ChangeMatchButton />` embedded in `MatchLoaded` section of `Index.razor` after `<RefreshScoreboardButton />` |
| AC-11 | All 13 TCs pass; all existing tests continue to pass |

---

## 8. Branch and Commit Strategy

- Branch: `feature/S-005-change-match-button`

---

## 9. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | NEEDS REVIEW — 2 HIGH (GPT), 5 MEDIUM (Sonnet) |
| R2 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | **UNANIMOUS APPROVED** — all R1 fixes verified; Sonnet 1 LOW (§4.3 finally scope wording) applied post-review |

### R1 Findings Applied

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| No disabled state during async confirm flow | GPT | HIGH | Accept | Added `_operationInProgress` flag to §4.2–4.3; added TC-9 |
| State=Error after ChangeMatchAsync unhandled | GPT | HIGH | Downgrade + Clarify | §3 clarified: `ChangeMatchAsync` throws on failure; no silent Error path |
| Error notification lacks exception detail | GPT | MEDIUM | Reject | Consistent with established pattern (S-004); generic UI message, detail in log |
| AC-9 Dispose has no backing TC | Sonnet | MEDIUM | Accept | Added TC-11 |
| TC-5 no dialog argument verification | Sonnet | MEDIUM | Accept | TC-5 extended with argument assertions |
| `null` cancel path has no TC | Sonnet | MEDIUM | Accept | TC-6 made `[DataRow]` covering `false` + `null` |
| TC-4 missing `WaitForAssertion` | Sonnet | MEDIUM | Accept | TC-4 updated |
| TC-7 no call order assertion | Sonnet | MEDIUM | Accept | TC-7 extended with call-order verification |
| TC-8 "remains usable" vague | Sonnet | LOW | Accept | Replaced with concrete assertions |
| TC-10 precondition guard not tested | Sonnet | LOW | Accept | Added TC-13 |
| BuildCtx wording in §5 | Sonnet | LOW | Accept | §5 reworded to separate production DI from test context |
| Accessibility requirements | GPT | LOW | Reject | Out of scope for this project phase; added to §2 Out of scope |
| Edge case test coverage | GPT | LOW | Accept (partial) | Note added to §6 TC notes; disposal-during-operation covered by `_disposed` flag |


| Field | Value |
|---|---|
| **Spec** | SPEC-S-005-ChangeMatchButton.md |
| **Step** | IS-004 S-005 |
| **Status** | IN REVIEW |
| **Version** | 0.1 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-004-Scoreboard.md v0.3 (APPROVED) |
| **Dependencies** | S-001 (`IScoreboardService.ClearCache` delivered `2bc5784`), S-003 (ScoreboardPreview `3c1566c`), S-004 (RefreshScoreboardButton `1cb54d7`) |

---

## 1. Purpose

Add a "Change Match" button to the match-loaded section of the UI. When clicked, the button shows a Radzen confirmation dialog. On confirmation, the scoreboard cache is cleared and the automation service transitions the application back to the `MatchSelection` state, ready for a new match to be selected. On cancellation, nothing changes.

---

## 2. Scope

**In scope:**
- New `ChangeMatchAsync` method on `IPcsProAutomationService` in `PcsRemote.Core` — transitions away from `MatchLoaded` back to `MatchSelection`
- `MockPcsProAutomationService` implementation of `ChangeMatchAsync`
- New `ChangeMatchButton.razor` Razor component in `PcsRemote.Web/Shared/`
- Embedding the component in the `MatchLoaded` section of `Pages/Index.razor` after `<RefreshScoreboardButton />`
- bUnit tests in `PcsRemote.Web.Tests/Shared/ChangeMatchButtonTests.cs` (TC-1 through TC-8)
- bUnit integration test TC-9 in `PcsRemote.Web.Tests/Pages/IndexTests.cs`
- Unit tests for `ChangeMatchAsync` in `PcsRemote.Automation.Mock.Tests` (TC-10)

**Out of scope:**
- Playwright E2E tests (S-006)
- Visual styling beyond structural CSS class hooks required by tests
- Changes to `ScoreboardPollingService` — it already stops automatically when state leaves `MatchLoaded`
- Any changes to `ScoreboardPreview` or `RefreshScoreboardButton`

---

## 3. Interface Extension — `IPcsProAutomationService`

A new `ChangeMatchAsync` method is added to `IPcsProAutomationService`:

**Purpose:** Initiates the change-match sequence, transitioning the application from `MatchLoaded` back to `MatchSelection` so a new match can be selected.

**Precondition:** Current state must be `MatchLoaded`. Calling from any other state throws `InvalidOperationException`.

**Postcondition:** State is `MatchSelection` (or `Error` if the transition fails).

**Signature shape:** Async, accepts an optional `CancellationToken`, returns `Task`.

**Mock behaviour:** Transitions directly from `MatchLoaded` to `MatchSelection` after a configurable delay (reuse the existing `ChangeMatchDelay` option already present in `MockPcsProOptions`). Fires `StateChanged` with `MatchSelection`. Uses the existing `_semaphore` guard (same pattern as other lifecycle operations).

---

## 4. Component — `ChangeMatchButton.razor`

### 4.1 Injected Dependencies

- `IPcsProAutomationService` — for `ChangeMatchAsync` and `StateChanged`
- `IScoreboardService` — for `ClearCache`
- `DialogService` (Radzen) — for the confirmation dialog
- `NotificationService` (Radzen) — for error notifications
- `ILogger<ChangeMatchButton>` — for error logging

### 4.2 Enabled/Disabled State

The button is enabled when `CurrentState == MatchLoaded`. Disabled in all other states. This is tracked locally using the subscribe-before-snapshot pattern (same as `RefreshScoreboardButton`).

### 4.3 Click Flow

On click:
1. Show a Radzen confirmation dialog: title "Change Match", message "Load a different match? PCS Pro will close and reopen to the match selection screen."
2. If the operator cancels (dialog returns `false` or `null`): no-op; button remains in its current state.
3. If the operator confirms (dialog returns `true`):
   a. Call `IScoreboardService.ClearCache()` — synchronous, no error handling needed (method is infallible per its contract)
   b. Call `await AutomationService.ChangeMatchAsync()` — wrapped in try/catch
   c. On exception: log at `Error` level with the exception object, show an error notification toast with message "Change match failed". Exception is NOT re-thrown.

### 4.4 Disposal

Implements `IDisposable`. `Dispose` sets a `_disposed` flag, then unsubscribes from `IPcsProAutomationService.StateChanged`. No other cleanup is required.

### 4.5 `StateChanged` Handler

`async void` event handler. Updates `_currentState`, calls `InvokeAsync(StateHasChanged)` for circuit safety.

### 4.6 CSS Class Hook and Button Label

| Element | CSS class | Visible text |
|---|---|---|
| Button element | `change-match-button` | "Change Match" |

No other structural CSS requirements. Visual styling is out of scope.

---

## 5. `Index.razor` Integration

`<ChangeMatchButton />` is added inside the `MatchLoaded` section of `Index.razor`, after `<RefreshScoreboardButton />`. No parameters; self-contained via `@inject`.

`BuildCtx` in `IndexTests.cs` must be updated to register `DialogService` (real Radzen instance, same pattern as `NotificationService` in S-004).

---

## 6. Test Cases

### TC-1: Button enabled in MatchLoaded state

Render with `CurrentState = MatchLoaded`. Assert: `change-match-button` is present and not disabled.

### TC-2: Button disabled in all non-MatchLoaded states

`[DataRow]` per non-MatchLoaded state. For each, render with that initial state. Assert: button is disabled.

### TC-3: StateChanged to MatchLoaded enables button

Render with `CurrentState = NotRunning`. Raise `StateChanged` with `MatchLoaded`. Assert via `WaitForAssertion`: button is enabled.

### TC-4: StateChanged away from MatchLoaded disables button

Render with `CurrentState = MatchLoaded`. Raise `StateChanged` with `MatchSelection`. Assert via `WaitForAssertion`: button is disabled.

### TC-5: Click shows confirmation dialog

Render with `CurrentState = MatchLoaded`. Set up `DialogService.Confirm` to return `false`. Click the button. Assert via `WaitForAssertion`: `Confirm` was called exactly once.

### TC-6: Cancel — no state change, no ClearCache, no ChangeMatchAsync

Render with `CurrentState = MatchLoaded`. Set up `DialogService.Confirm` to return `false`. Click the button. Assert via `WaitForAssertion`: `ClearCache` NOT called; `ChangeMatchAsync` NOT called.

### TC-7: Confirm — calls ClearCache then ChangeMatchAsync

Render with `CurrentState = MatchLoaded`. Set up `DialogService.Confirm` to return `true`. Set up `ChangeMatchAsync` to return `Task.CompletedTask`. Click. Assert via `WaitForAssertion`: `ClearCache` called once; `ChangeMatchAsync` called once.

### TC-8: ChangeMatchAsync exception — logs, notifies, does not re-throw

Render with `CurrentState = MatchLoaded`. Use a verifiable `Mock<ILogger<ChangeMatchButton>>`. Set up `DialogService.Confirm` to return `true`. Set up `ChangeMatchAsync` to throw. Click. Assert via `WaitForAssertion`:
- Error notification with message "Change match failed" emitted
- `LogError` called with the exact thrown exception instance
- Component does not crash / bUnit context remains usable

### TC-9: Index integration — button present in MatchLoaded section

bUnit test in `IndexTests.cs`. Render `Index.razor` driven to `MatchLoaded`. Assert: `change-match-button` present; `refresh-scoreboard-button` and `scoreboard-placeholder` and team names unaffected.

### TC-10: Mock ChangeMatchAsync transitions MatchLoaded → MatchSelection

Unit test in `PcsRemote.Automation.Mock.Tests`. Construct `MockPcsProAutomationService` in `MatchLoaded` state. Call `ChangeMatchAsync`. Assert: `StateChanged` fires with `MatchSelection`; `CurrentState == MatchSelection`.

---

## 7. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `ChangeMatchAsync` method exists on `IPcsProAutomationService` |
| AC-2 | `MockPcsProAutomationService` implements `ChangeMatchAsync`, transitioning `MatchLoaded → MatchSelection` |
| AC-3 | `ChangeMatchButton` component exists in `PcsRemote.Web/Shared/` |
| AC-4 | Button enabled only when `CurrentState == MatchLoaded`; disabled otherwise |
| AC-5 | Click shows Radzen confirmation dialog with correct title and message |
| AC-6 | Cancel: `ClearCache` and `ChangeMatchAsync` are NOT called |
| AC-7 | Confirm: `ClearCache` is called first, then `ChangeMatchAsync` |
| AC-8 | `ChangeMatchAsync` exception triggers error notification, logs with exception object, does not re-throw |
| AC-9 | `Dispose` unsubscribes from `StateChanged` |
| AC-10 | `<ChangeMatchButton />` embedded in `MatchLoaded` section of `Index.razor` after `<RefreshScoreboardButton />` |
| AC-11 | All 10 TCs pass; all existing tests continue to pass |

---

## 8. Branch and Commit Strategy

- Branch: `feature/S-005-change-match-button`

---

## 9. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | Pending |
