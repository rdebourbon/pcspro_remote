# SPEC-S-004 — Multi-Browser Operation Locking

| Field | Value |
|---|---|
| **Document** | SPEC-S-004-OperationCoordinator.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Step ID** | IS-005 S-004 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-005-Hardening.md v0.2 (APPROVED) |
| **Governing HLPS** | HLPS-005-Hardening.md v0.2 (APPROVED) |
| **Dependencies** | SPEC-S-003 (APPROVED) — `PcsProHub` extension pattern; service event → broadcaster → hub pattern established |

---

## 1. Context and Problem

Currently each action button (`ChangeMatchButton`, `RefreshScoreboardButton`) tracks its own operation-in-progress state using a **local component field**. When Browser A starts a long-running automation operation, Browser B's buttons remain enabled and can issue conflicting commands. While the state machine guard ultimately rejects concurrent triggers with `InvalidOperationException`, the user in Browser B sees no indication that an operation is in progress. This violates H-SC-5.

This step replaces the per-component local flag with a **cross-browser hub-driven flag** sourced from a singleton coordination service — the same event-driven architecture already established by `ManualModeService` / `ManualModeBroadcaster` in S-003.

---

## 2. Requirements

### R-1 — Operation Coordinator Service

A new singleton service tracks whether any automation operation is currently in-flight. It exposes:

- A read-only boolean indicating the current in-progress state.
- An operation to begin tracking an in-flight operation. If an operation is already tracked this call is a **no-op** (the flag is not re-set, no event fires).
- An operation to mark the current operation complete. If no operation is in progress this is a **no-op** (the flag is not re-cleared, no event fires).
- A change notification event carrying the new boolean value, raised on every flag transition (begin → in-progress; complete → not in progress). The event follows the same `EventHandler<bool>` signature established by `IManualModeService.ManualModeChanged`.

The begin/complete operations must be **thread-safe**. Concurrent calls from multiple browser circuits must not corrupt state. The implementation must use atomic compare-and-swap (CAS) consistent with `ManualModeService`.

The service is defined by an **interface** in `PcsRemote.Web.Services` (not Core — TrayHost has no operational dependency on in-progress tracking). The interface and implementation reside in the same project. The interface is registered as a singleton in the DI container.

### R-2 — Operation In Progress Broadcaster

A new `IHostedService` in `PcsRemote.Web.Hubs` subscribes to the coordinator service's change event and broadcasts the new boolean value to all SignalR clients via `IHubContext<PcsProHub>`. This follows the `ManualModeBroadcaster` pattern exactly. The broadcaster must:

- Log errors from the hub broadcast at Error level using an injected `ILogger` (consistent with `ManualModeBroadcaster` post-review fix).
- Unsubscribe from the event in `StopAsync`.

A new hub constant `ReceiveOperationInProgressUpdate` is added to `PcsProHubConstants`.

### R-3 — Late-Joiner Support

`PcsProHub.OnConnectedAsync` is extended to send the current in-progress state to the newly connected client. The send order is: `ReceiveStateUpdate` (existing) → `ReceiveManualModeUpdate` (existing) → `ReceiveOperationInProgressUpdate` (new) → `base.OnConnectedAsync()`. This ordering ensures all three state values are delivered before the base hub connection logic runs.

### R-4 — ChangeMatchButton Refactored

`ChangeMatchButton.razor` is updated:

- Injects `IOperationCoordinatorService`.
- **Guard at top of click handler:** `OnChangeMatchClickedAsync` checks `_operationInProgress` at the very start and returns immediately if true (defense-in-depth, consistent with the existing manual mode guard and the `SelectMatchAsync` guard in R-6). This closes the brief TOCTOU window described in AR-3.
- **BeginOperation() timing:** `BeginOperation()` is called **after** the confirmation dialog resolves with "yes" and **before** the first async automation call. The `try/finally` wrapping `MarkComplete()` covers only the automation call, not the dialog. If the user cancels, `BeginOperation()` is never called. This ensures all browsers are locked only once the user has committed to an operation.
- Removes the direct assignment `_operationInProgress = true/false` inside the click handler; these are replaced by `BeginOperation()` / `MarkComplete()` on the coordinator.
- Adds an `OnOperationInProgressChanged` event handler (same pattern as `OnManualModeChanged`) that updates a local `_operationInProgress` field and schedules a re-render. The handler must follow the **full** `_disposed` + `ObjectDisposedException` pattern as established in `ChangeMatchButton.OnManualModeChanged` (NOT `Index.OnManualModeChanged`, which currently lacks the guard):
  - `_disposed` boolean field added (already exists in this component).
  - Handler checks `if (!_disposed)` before invoking `InvokeAsync(StateHasChanged)`.
  - `InvokeAsync` call is wrapped in `try { } catch (ObjectDisposedException) { }`.
- `OnInitialized` subscribes to the coordinator event and reads the current state (subscribe-before-snapshot pattern).
- `Dispose()` unsubscribes from the coordinator event (`ManualModeService.ManualModeChanged -= OnManualModeChanged` demonstrates the pattern).
- `IsEnabled` continues to use `_operationInProgress` — but the field is now driven by the service event, not the click handler.
- When the button is disabled due to `_operationInProgress`, it renders with a `title="Automation in progress…"` tooltip attribute.

### R-5 — RefreshScoreboardButton Refactored

Same changes as R-4 applied symmetrically to `RefreshScoreboardButton.razor`:

- The existing `_refreshInProgress` field is renamed to `_operationInProgress` for naming consistency.
- `OnRefreshClickedAsync` checks `_operationInProgress` at the very start and returns immediately if true (same guard as R-4).
- `BeginOperation()` is called before the first async automation call; `MarkComplete()` is in the `finally` block.
- `OnOperationInProgressChanged` handler with full `_disposed` + `ObjectDisposedException` pattern (the `_disposed` field already exists in this component).
- `OnInitialized` subscribes with subscribe-before-snapshot.
- `Dispose()` unsubscribes from the coordinator event.
- `title="Automation in progress…"` tooltip attribute when button is disabled due to `_operationInProgress`.

### R-6 — Index.razor Match Card Locking

`Index.razor` is updated to:

- Inject `IOperationCoordinatorService`.
- **Subscribe-before-snapshot:** at the start of `OnInitializedAsync`, before the first `await`, subscribe to the coordinator event and read the current state into `_operationInProgress`. This prevents the lost-update race window.
- Maintain a `_operationInProgress` boolean field driven by the event handler.
- Pass `IsInteractive="@(!_manualModeActive && !_operationInProgress)"` to each `MatchCard`.
- `Dispose()` unsubscribes from the coordinator event.
- Add guard in `SelectMatchAsync`: if `_operationInProgress` is true, return immediately. This guard is **intentionally silent** (no notification) because the match card itself will be rendered as non-interactive (`IsInteractive=false`); the guard is pure defense-in-depth for the narrow TOCTOU window between event firing and re-render.

**`_disposed` pattern for Index.razor:** Index.razor currently lacks the `_disposed` + `ObjectDisposedException` guard in its existing `OnManualModeChanged` handler. This is a pre-existing gap; fixing it is out of scope for S-004. For the **new** `OnOperationInProgressChanged` handler, the pattern is also omitted for consistency with the existing `OnManualModeChanged` handler in the same component — Index.razor's async handlers use only `InvokeAsync` without an explicit guard, which is acceptable because Blazor Server's `InvokeAsync` handles disposed component state gracefully. If the pre-existing gap in `OnManualModeChanged` is addressed in a future step, the same fix applies to `OnOperationInProgressChanged`.

When `_operationInProgress` is true, the `MatchCard` (non-interactive branch) renders with a `title="Automation in progress…"` tooltip attribute on the outer `div`.

### R-7 — Program.cs Registration

`OperationCoordinatorService` is registered as a singleton in `Program.cs`. `OperationInProgressBroadcaster` is registered as an `IHostedService`.

---

## 3. Component Interaction Design

```
[ChangeMatchButton click]
        │
        ▼
[guard: if _operationInProgress → return]
        │
        ▼
[confirm dialog shown]
        │ user cancels → return (BeginOperation never called)
        │ user confirms ↓
        ▼
coordinator.BeginOperation()
        │ fires OperationInProgressChanged(true)
        ├─────────────────────────────────────────────────────────────────►
        │                                                                   │
        │  [OperationInProgressBroadcaster]                                 │
        │   OnOperationInProgressChanged → hub.Clients.All.SendAsync       │
        │       → all SignalR-connected clients receive broadcast           │
        │                                                                   │
        │  [Blazor Server circuits — same process]                          │
        │   Each component's OnOperationInProgressChanged handler fires     │
        │   → _operationInProgress = true                                   │
        │   → InvokeAsync(StateHasChanged) — button disables in all UIs     ◄
        │
[automation call executes]
        │
        ▼
coordinator.MarkComplete()  ← always in finally block
        │ fires OperationInProgressChanged(false)
        └─── same broadcast chain → all UIs re-enable
```

---

## 4. Accepted Risks

| ID | Risk | Acceptance Rationale |
|---|---|---|
| AR-1 | Coordinator stays `true` indefinitely if component crashes before `finally`. | `try/finally` in button click handlers guarantees cleanup in all code paths including exceptions. Unhandled process crash leaves state inconsistent but process restart resets singleton. |
| AR-2 | Hub broadcast exceptions swallowed by `try/catch`. | Logged at Error level (R-2). Broadcast failure does not block the operation — it is UX-only. The established project pattern (see `ManualModeBroadcaster`). |
| AR-3 | Brief window between `BeginOperation()` and `OnOperationInProgressChanged` firing where `_operationInProgress` in other components is stale. | Measured in microseconds on the same process. State machine guard is the authoritative concurrency control. UI lag is imperceptible. |

---

## 5. Branch

`feature/IS-005-S-004-operation-coordinator`

---

## 6. Acceptance Criteria

### AC-1 — Coordinator service
- [ ] A singleton `IOperationCoordinatorService` exists in `PcsRemote.Web.Services`.
- [ ] `IsOperationInProgress` is `false` by default.
- [ ] `BeginOperation()` transitions `false → true` and fires the event exactly once.
- [ ] Second concurrent `BeginOperation()` call while in-progress is a no-op.
- [ ] `MarkComplete()` transitions `true → false` and fires the event exactly once.
- [ ] `MarkComplete()` when not in-progress is a no-op.
- [ ] Concurrent `BeginOperation()` calls from multiple threads: exactly one succeeds (event fires once, `IsOperationInProgress` is `true`).

### AC-2 — Broadcaster
- [ ] `OperationInProgressBroadcaster` is an `IHostedService` in `PcsRemote.Web.Hubs`.
- [ ] On `OperationInProgressChanged(true)` → hub broadcasts `ReceiveOperationInProgressUpdate` with `true`.
- [ ] On `OperationInProgressChanged(false)` → hub broadcasts `ReceiveOperationInProgressUpdate` with `false`.
- [ ] `StopAsync` unsubscribes — no further broadcasts after stop.
- [ ] Hub broadcast exceptions are logged at Error level and do not propagate.

### AC-3 — Late-joiner
- [ ] A browser connecting during an in-progress operation receives `ReceiveOperationInProgressUpdate = true` in `OnConnectedAsync`.

### AC-4 — ChangeMatchButton cross-browser disable
- [ ] Clicking Change Match on Browser A disables the Change Match button on Browser B (simulated via coordinator event without needing real hub round-trip in bUnit).
- [ ] Button re-enables after the operation completes.
- [ ] Button re-enables if the operation throws (finally block called).
- [ ] `IsEnabled` respects both `_operationInProgress` and `_manualModeActive`.
- [ ] Tooltip "Automation in progress…" renders when button is disabled due to `_operationInProgress`.

### AC-5 — RefreshScoreboardButton cross-browser disable
- [ ] Same guarantees as AC-4 applied to Refresh button (including tooltip).

### AC-6 — Match card locking
- [ ] `MatchCard IsInteractive` is `false` when `_operationInProgress` is `true`.
- [ ] `SelectMatchAsync` guard returns immediately if `_operationInProgress` is `true`.
- [ ] Match cards re-enable after operation completes.
- [ ] Tooltip "Automation in progress…" renders on match cards when `_operationInProgress` is `true`.

### AC-7 — Server-side rejection
- [ ] A second concurrent automation call while one is in-flight is rejected by `InvalidOperationException` from the state machine (server-side authoritative guard — verifiable via unit test on the state machine or automation service mock).

### AC-8 — No regressions
- [ ] All pre-existing tests in `PcsRemote.Web.Tests`, `PcsRemote.Core.Tests`, and `PcsRemote.Automation.Mock.Tests` continue to pass.
- [ ] No new build warnings.

---

## 7. Test Scope

### New test files (all in `PcsRemote.Web.Tests`)

**`OperationCoordinatorServiceTests.cs`** — 9 unit tests:
- TC-1: Initial state is `IsOperationInProgress = false`
- TC-2: `BeginOperation` sets `true`, fires event exactly once with `true`
- TC-3: `MarkComplete` after begin: sets `false`, fires event exactly once with `false`
- TC-4: Sequential second `BeginOperation` while already in progress is a no-op (event not fired; `IsOperationInProgress` remains `true`)
- TC-5: `MarkComplete` when not in progress is a no-op (event not fired; `IsOperationInProgress` remains `false`)
- TC-6: Concurrent `BeginOperation` from N threads — event fires exactly once with `true`; `IsOperationInProgress` is `true` after all threads complete
- TC-6b: Mixed concurrent `BeginOperation` + `MarkComplete` from N threads — event count invariant: `|trueFirings - falseFirings| ≤ 1`; no exceptions
- TC-7: Subscribe-before-snapshot race scenario: one thread calls `BeginOperation()`; another thread subscribes and immediately reads `IsOperationInProgress` — subscriber observes `true` regardless of event ordering (verifies the subscribe-before-snapshot contract at the service level)
- TC-8: `MarkComplete()` after `BeginOperation()` followed by re-`BeginOperation()` — full two-cycle round-trip fires two pairs of events in correct order

**`OperationInProgressBroadcasterTests.cs`** — 4 unit tests:
- TC-9: `OperationInProgressChanged(true)` → hub broadcasts `ReceiveOperationInProgressUpdate` with `true`
- TC-10: `OperationInProgressChanged(false)` → hub broadcasts `ReceiveOperationInProgressUpdate` with `false`
- TC-11: `StopAsync` unsubscribes — no further broadcasts after stop
- TC-12: Hub broadcast throws → exception is logged at Error level; event handler does not propagate the exception

### Updates to existing test files

**`PcsProHubTests.cs`** — 2 new tests:
- TC-13: `OnConnectedAsync` sends `ReceiveOperationInProgressUpdate = true` to caller when coordinator is in-progress
- TC-14: `OnConnectedAsync` sends `ReceiveOperationInProgressUpdate = false` to caller when coordinator is idle

**`ChangeMatchButtonTests.cs`** — 6 new tests + 1 modified:
- TC-15: `Dispose` unsubscribes from `IOperationCoordinatorService.OperationInProgressChanged` (follows existing TC-11 disposal pattern)
- TC-16: Button disabled when coordinator fires `in-progress = true` from another context
- TC-17: Tooltip `title="Automation in progress…"` attribute present when button disabled due to coordinator
- TC-18: Button re-enables when coordinator fires `in-progress = false`
- TC-19: Button re-enables if automation call throws (finally block guarantees `MarkComplete`)
- TC-20: Dialog cancelled → `BeginOperation()` is never called (verifies BeginOperation placement after confirm)
- Modified TC-9 ("Button disabled during in-progress"): rewrite to verify the coordinator `BeginOperation()`/`MarkComplete()` flow rather than direct local flag assignment; the mechanism changes but the observable behavior (button disabled → re-enables) is the same

**`RefreshScoreboardButtonTests.cs`** — 5 new tests + 1 modified (symmetric to ChangeMatchButton):
- TC-X+1: `Dispose` unsubscribes from coordinator event
- TC-X+2: Button disabled when coordinator fires `in-progress = true`
- TC-X+3: Tooltip attribute present when disabled due to coordinator
- TC-X+4: Button re-enables when coordinator fires `in-progress = false`
- TC-X+5: Button re-enables if refresh throws (finally block)
- Modified TC-3 ("Button disabled while refresh is in progress"): rewrite to verify coordinator flow

**`IndexTests.cs`** — 5 new tests:
- TC-21: Match cards rendered with `match-card--disabled` when coordinator is in-progress
- TC-22: Tooltip `title="Automation in progress…"` attribute present on disabled match card element
- TC-23: `SelectMatchAsync` returns without calling `LoadMatchAsync` when operation in progress
- TC-24: Match cards re-enable when coordinator fires `in-progress = false`
- TC-25: Late-join scenario — index rendered when coordinator is already in-progress → all match cards rendered as `match-card--disabled` on initial render without requiring a subsequent event

---

## 8. Commit Strategy

One or more commits on `feature/IS-005-S-004-operation-coordinator`. Squash-merged to master after review approval with a single summary commit.

---

## 9. Documentation

No user-facing documentation changes required. `PROJECT-CONTEXT.md` does not require updates — no new architectural patterns or constraints.

---

## 10. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | Claude Sonnet 4.6, Claude Opus 4.6, GPT-5.4 | NEEDS REVIEW — findings triaged, v0.2 fixes applied |
| R2 | 2026-04-12 | — | APPROVED — R1 findings fully addressed; R2 adversarial review waived |

### R1 Triage

| Finding | Reviewer(s) | Severity | Disposition | Resolution |
|---|---|---|---|---|
| Tooltip missing from requirements/ACs/tests | All 3 | HIGH | ACCEPT | Added tooltip requirement to R-4, R-5, R-6; AC-4, AC-5, AC-6; TC-17 and TC-22 |
| BeginOperation() timing relative to confirm dialog undefined | Sonnet, Opus | MEDIUM | ACCEPT | R-4 now specifies: BeginOperation() called AFTER confirm, before automation call; §3 diagram updated |
| Index.razor `_disposed` guard references non-existent pattern | All 3 | HIGH | ACCEPT | R-6 now explicitly documents the Index.razor _disposed decision: omit for consistency with existing `OnManualModeChanged`, note pre-existing gap |
| Existing tests not enumerated for rewriting | Sonnet | HIGH | ACCEPT | §7 now lists TC-9 (ChangeMatchButton) and TC-3 (RefreshScoreboardButton) as requiring rewrite |
| No disposal test for coordinator subscription | Sonnet | HIGH | ACCEPT | TC-15 and TC-X+1 added for button components |
| No error-logging test for broadcaster | Sonnet | HIGH | ACCEPT | TC-12 added |
| TC-6 balance invariant wrong (not an exact assertion) | All 3 | MEDIUM | ACCEPT | TC-6 rewritten with exact count assertion |
| TC-6b mixed concurrent test missing | Opus | MEDIUM | ACCEPT | TC-6b added |
| OnInitialized vs OnInitializedAsync inconsistency | Sonnet | MEDIUM | ACCEPT | R-6 now says "before first await in OnInitializedAsync" |
| SelectMatchAsync silent rejection unexplained | Sonnet | MEDIUM | ACCEPT | R-6 now states silence is intentional defense-in-depth |
| No late-join test for Index.razor coordinator | Sonnet | MEDIUM | ACCEPT | TC-25 added |
| TC-7 description ambiguous/redundant | Sonnet | MEDIUM | ACCEPT | TC-7 rewritten as race-condition service scenario |
| Guard at top of click handlers missing | Opus | MEDIUM | ACCEPT | R-4 and R-5 now require top-of-handler guard |
| TC-14 conflates coordinator integration with manual mode | Opus | MEDIUM | ACCEPT | TC-20 (dialog cancel test) added; old TC-14 renamed/reordered |
| TC-4 "concurrent" misnomer | Sonnet | LOW | ACCEPT | Renamed "sequential second BeginOperation" |
| Test files not assigned to project | Sonnet | LOW | ACCEPT | "(all in PcsRemote.Web.Tests)" added to §7 header |
| OnConnectedAsync send order unspecified | Sonnet | LOW | ACCEPT | R-3 now specifies send order explicitly |
| Stale test count "175+" | Sonnet, Opus | LOW | ACCEPT | AC-8 rewritten without hardcoded count |
| BeginOperation() no-op doesn't prevent 2nd concurrent op | GPT | CRITICAL→MEDIUM | DOWNGRADE | IS-005 S-004 explicitly states the state machine is the authoritative guard; UI locking is UX-only. The top-of-handler guard (R-4, R-5) closes the click-path race. Accepted risk AR-3 documented. |
| Auto-select paths not covered by coordinator | GPT | HIGH→LOW | DOWNGRADE | Auto-select paths are triggered by state machine transitions, not user input; no UI-level coordination needed. State machine guard remains authoritative. Deferred to potential future step. |
| AutoLaunchService / Retry button out of scope | GPT | HIGH | REJECT | Retry button coverage is explicitly assigned to S-005 in IS-005. AutoLaunchService is a one-shot startup service, not a user-action path. Out of S-004 scope. |
| S-005 forward dependency advisory | Opus | LOW | ACCEPT as advisory | Note added to §1 |
