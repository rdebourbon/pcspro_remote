# SPEC-S-004 — Refresh Scoreboard Button

| Field | Value |
|---|---|
| **Spec** | SPEC-S-004-RefreshScoreboardButton.md |
| **Step** | IS-004 S-004 |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-004-Scoreboard.md v0.3 (APPROVED) |
| **Dependencies** | S-001 (`ForceRefreshAsync` and `RefreshCompleted` delivered `2bc5784`), S-003 (ScoreboardPreview component delivered `3c1566c`) |

---

## 1. Purpose

Add a "Refresh Scoreboard" button to the match-loaded section of the UI. The button triggers a forced scoreboard capture that bypasses delta detection, and provides visual feedback to the operator via a notification toast — both on success and on failure.

---

## 2. Scope

**In scope:**
- New `RefreshScoreboardButton.razor` Razor component in `PcsRemote.Web/Shared/`
- Embedding the component in the `MatchLoaded` section of `Pages/Index.razor`
- bUnit tests in `PcsRemote.Web.Tests/Shared/RefreshScoreboardButtonTests.cs` (TC-1 through TC-11) and `PcsRemote.Web.Tests/Pages/IndexTests.cs` (TC-10 — addition to existing file)

**Out of scope:**
- Change match button (S-005)
- Playwright E2E tests (S-006)
- Visual styling beyond structural CSS class hooks required by tests
- Changes to `IScoreboardService`, `IPcsProAutomationService`, or any Core type

---

## 3. Behavioural Requirements

### 3.1 Button Enabled/Disabled States

The button is enabled when: `CurrentState == MatchLoaded` AND no refresh is currently in progress.

The button is disabled in all other conditions:
- Any state other than `MatchLoaded` (including `Launching`, `MatchSelectionSearching`, and all others — S-SC-4, S-SC-12)
- While a `ForceRefreshAsync` call is in flight

The enabled/disabled state is driven by tracking `CurrentState` locally (subscribe-before-snapshot pattern, as established in `ScoreboardPreview` and `Index.razor`), and an in-progress flag that is set on click and cleared when the async operation completes.

### 3.2 Refresh Trigger

When the button is clicked, the component:
1. Sets the in-progress flag to `true` and re-renders (disables button immediately)
2. Calls `IScoreboardService.ForceRefreshAsync`
3. Clears the in-progress flag and re-renders in a `finally` block — the button re-enables whether the call succeeded or failed

`InvokeAsync` must wrap any rendering state mutations triggered from non-Blazor-circuit threads (i.e. event handler callbacks from `RefreshCompleted`).

### 3.3 Success Notification

The component subscribes to `IScoreboardService.RefreshCompleted`. When the event fires, the component shows a success notification toast via the injected `NotificationService` with the message "Scoreboard refreshed". The notification dispatch must be circuit-safe (`InvokeAsync`). Default Radzen notification duration and stacking behaviour are acceptable — no custom toast configuration is required.

### 3.4 Error Notification

If `ForceRefreshAsync` throws an exception, the component catches it and shows an error notification toast via `NotificationService` with the message "Refresh failed". The exception is not re-thrown (the error is surfaced to the user via notification; no unhandled exception propagates to the Blazor circuit). The exception — including the exception object itself — must be logged at `Error` level via Serilog before showing the notification. Default Radzen error notification duration and stacking behaviour are acceptable.

### 3.5 Disposal

The component implements `IDisposable`. `Dispose` unsubscribes from both `IPcsProAutomationService.StateChanged` and `IScoreboardService.RefreshCompleted`. No other cleanup is required.

### 3.6 Index.razor Integration

`<RefreshScoreboardButton />` is added inside the existing `MatchLoaded` section of `Index.razor`, after `<ScoreboardPreview />`. `Index.razor` does not pass state or service references as parameters — the component is self-contained via `@inject`.

---

## 4. Component API

The component has **no parameters**. All dependencies are injected via `@inject`:
- `IScoreboardService` — for `ForceRefreshAsync` and `RefreshCompleted`
- `IPcsProAutomationService` — for `CurrentState` and `StateChanged`
- `NotificationService` (Radzen) — for success and error toasts
- `ILogger<RefreshScoreboardButton>` — for error logging

---

## 5. CSS Class Hooks and Button Label

| Element | CSS class | Visible text |
|---|---|---|
| The button element | `refresh-scoreboard-button` | "Refresh Scoreboard" |

No other structural CSS requirements. Visual styling is out of scope.

---

## 6. Test Cases

All component tests are bUnit tests in `PcsRemote.Web.Tests/Shared/RefreshScoreboardButtonTests.cs`.

`IScoreboardService` and `IPcsProAutomationService` are Moq mocks. `NotificationService` is the real Radzen implementation — tests subscribe to `NotificationService.Messages.CollectionChanged` (`ObservableCollection<NotificationMessage>`) to capture emitted notifications for assertion. For all TCs except TC-6, `ILogger<RefreshScoreboardButton>` may be a `NullLogger` or equivalent. In TC-6, the logger must be a verifiable test double (e.g. `Mock<ILogger<RefreshScoreboardButton>>`) so that the `LogError` call can be asserted — the verification must check that the exact thrown exception instance is passed.

### TC-1: Button enabled in MatchLoaded state

Render the component with `CurrentState = MatchLoaded`. Assert: the `refresh-scoreboard-button` element is present and not disabled.

### TC-2: Button disabled in all non-MatchLoaded states

Implemented as a `[DataRow]` per non-MatchLoaded state: `NotRunning`, `Launching`, `LoginScreen`, `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, `Error`. For each row, render with that initial state. Assert: the button is disabled. Note: if new states are added to `PcsProState` in future steps, this DataRow list must be extended accordingly.

### TC-3: Button disabled while refresh is in progress

Render with `CurrentState = MatchLoaded`. Set up `ForceRefreshAsync` to return a `Task` that does not complete during the test (use `TaskCompletionSource`). Click the button. Assert: the button is disabled before the task completes.

### TC-4: Button click calls ForceRefreshAsync

Render with `CurrentState = MatchLoaded`. Set up `ForceRefreshAsync` to return `Task.CompletedTask`. Click the button. Assert via `WaitForAssertion`: `ForceRefreshAsync` was called exactly once.

### TC-5: RefreshCompleted event shows success notification

Render with `CurrentState = MatchLoaded`. Raise `RefreshCompleted` on the `IScoreboardService` mock. Assert via `WaitForAssertion`: a notification with the message "Scoreboard refreshed" was emitted via `NotificationService`.

### TC-6: ForceRefreshAsync exception shows error notification, re-enables button, and logs error

Render with `CurrentState = MatchLoaded`. Use a verifiable logger mock. Set up `ForceRefreshAsync` to throw an exception. Click the button. Assert via `WaitForAssertion`:
- a notification with the message "Refresh failed" was emitted via `NotificationService`
- the `refresh-scoreboard-button` is not disabled (button re-enabled after exception)
- the logger mock received a `LogError` call containing the thrown exception

### TC-7: Button re-enables after refresh completes (success path)

Render with `CurrentState = MatchLoaded`. Set up `ForceRefreshAsync` to return `Task.CompletedTask`. Click the button. Assert via `WaitForAssertion`: the button is enabled again after the call completes.

### TC-8: StateChanged away from MatchLoaded disables button

Render with `CurrentState = MatchLoaded`. Confirm button is enabled. Raise `StateChanged` with `MatchSelection`. Assert via `WaitForAssertion`: the button is disabled.

### TC-9: Disposal unsubscribes from both events

Render the component, then call `cut.Instance.Dispose()`. Verify via `mock.VerifyRemove` that `StateChanged` and `RefreshCompleted` handlers were each removed exactly once. (`cut.Instance.Dispose()` is the established project pattern — bUnit's `cut.Dispose()` defers teardown and would race the `VerifyRemove` assertion.)

### TC-10: Index.razor integration — button present in MatchLoaded section after ScoreboardPreview

bUnit test in `Pages/IndexTests.cs`. Render `Index.razor` with state driven to `MatchLoaded`. Assert: `refresh-scoreboard-button` element is present; existing home/away team name spans and `scoreboard-placeholder` are also present (confirms prior AC-10 from S-003 is unaffected). DOM ordering is verified by inspection of `Index.razor` markup: `<ScoreboardPreview />` must appear before `<RefreshScoreboardButton />` in the template source, which is enforced by the AC-10 placement requirement.

### TC-11: Multi-component integration — button click updates ScoreboardPreview (S-SC-5)

bUnit test in `PcsRemote.Web.Tests/Shared/RefreshScoreboardButtonTests.cs`. Register the same `IScoreboardService` mock, `IPcsProAutomationService` mock (with `CurrentState = MatchLoaded`), and `NotificationService` in the same bUnit context. Render both `RefreshScoreboardButton` and `ScoreboardPreview` as siblings. Set up `ForceRefreshAsync` to complete; when called, the mock raises `ScoreboardUpdated` with a non-empty byte array, then raises `RefreshCompleted`. Click the refresh button. Assert via `WaitForAssertion`:
- the `scoreboard-image` element is present in the rendered `ScoreboardPreview` output with a `data:image/jpeg;base64,` src (verifying the S-SC-11 path: `ScoreboardUpdated` fired before `RefreshCompleted`)
- a "Scoreboard refreshed" notification was emitted (verifying the full forced-refresh event sequence)

---

## 7. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `RefreshScoreboardButton` component exists in `PcsRemote.Web/Shared/` |
| AC-2 | Button is enabled when `CurrentState == MatchLoaded` and no refresh is in progress |
| AC-3 | Button is disabled in all non-`MatchLoaded` states |
| AC-4 | Button is disabled while `ForceRefreshAsync` is in flight |
| AC-5 | Clicking the button calls `ForceRefreshAsync` exactly once |
| AC-6 | `RefreshCompleted` event triggers a success notification with message "Scoreboard refreshed" |
| AC-7 | `ForceRefreshAsync` exception triggers an error notification with message "Refresh failed", logs the exception at Error level, and re-enables the button |
| AC-8 | Button re-enables after `ForceRefreshAsync` completes (success or failure) |
| AC-9 | `Dispose` unsubscribes from both `StateChanged` and `RefreshCompleted` |
| AC-10 | `<RefreshScoreboardButton />` is embedded in the `MatchLoaded` section of `Index.razor` after `<ScoreboardPreview />` |
| AC-11 | All 11 TCs pass; all existing tests continue to pass |

---

## 8. Branch and Commit Strategy

- Branch: `feature/S-004-refresh-scoreboard-button`
- Commit component first, then Index integration, then tests
- Squash-merge to master after adversarial review approval

---

## 9. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R2 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | **UNANIMOUS APPROVED** — all R1 fixes verified; Sonnet 1 non-blocking observation (§3.4 "before" ordering not asserted in TC-6 — natural catch-block structure is sufficient) |

### R1 Findings Applied

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| S-SC-5 integration TC entirely absent | Sonnet | HIGH | Accept | Added TC-11 (multi-component integration test) |
| AC-7 logging unverifiable with NullLogger | Sonnet | MEDIUM | Accept | TC-6 updated: verifiable logger mock required; log assertion added |
| AC-8 error-path button re-enable has no TC | Sonnet | MEDIUM | Accept | TC-6 extended: button re-enable assertion added to error-path test |
| Section §2 numbering collision | Sonnet | LOW | Accept | Sections renumbered: Scope=§2, Behavioural Req=§3, API=§4, CSS=§5, TCs=§6, ACs=§7, Branch=§8, Review=§9 |
| TC-10 DOM ordering not verified | Sonnet | LOW | Accept | TC-10 clarified: ordering enforced by markup inspection per AC-10 |
| TC-2 DataRow not future-proofed | Sonnet | LOW | Accept | Note added to TC-2 |
| Button text not specified | GPT | LOW | Accept | §5 table extended with "Visible text" column: "Refresh Scoreboard" |
| Toast duration/behavior not specified | GPT | LOW | Accept | Notes added to §3.3 and §3.4 confirming default Radzen behavior acceptable |
| Double-click negative TC | GPT | LOW | Reject | TC-3 (button disabled in-flight) + TC-4 ("exactly once") together cover this; a separate TC-4b would be redundant |
| Logging context specificity | GPT | LOW | Accept (minimal) | §3.4 clarified: "exception object itself must be logged" |
