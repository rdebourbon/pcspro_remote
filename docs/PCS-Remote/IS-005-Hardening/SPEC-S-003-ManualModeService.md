# SPEC-S-003 — `ManualModeService`, `PcsProHub` Extension, and Manual Mode Banner

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-ManualModeService.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-12 |
| **Step** | S-003 |
| **Governing IS** | IS-005-Hardening.md v0.2 (APPROVED) |
| **Governing HLPS** | HLPS-005-Hardening.md v0.2 (APPROVED) |

---

## 1. Purpose

S-001 delivered the `IManualModeService` Core contract. This step delivers three tightly coupled pieces in `PcsRemote.Web`:

1. **`ManualModeService`** — a thread-safe singleton implementation of `IManualModeService`.
2. **`PcsProHub` extension** — a hub constant and late-joiner send for manual mode state, plus a `ManualModeBroadcaster` hosted service that relays `ManualModeChanged` events to all hub clients.
3. **Manual mode banner** — a Blazor component that shows/hides based on the live manual mode state, subscribing directly to the singleton's event, and disables all automation action controls while active.
4. **Server-side command guard** — each automation-command-issuing component checks manual mode before invoking any automation operation and rejects with a descriptive reason if active.

---

## 2. Scope

**In scope:**
- `ManualModeService` class in `PcsRemote.Web`, DI-registered as singleton for `IManualModeService`.
- A new `PcsProHubConstants` entry for manual mode update messages.
- Update `PcsProHub.OnConnectedAsync` to send current manual mode state to the connecting client.
- `ManualModeBroadcaster` hosted service bridging `ManualModeChanged` to all hub clients.
- `ManualModeBanner` Razor component; inclusion in `MainLayout.razor`.
- Server-side manual mode guard in each automation-command-issuing Blazor component (`ChangeMatchButton`, `RefreshScoreboardButton`, `MatchCard`, and any component calling `LaunchAndLoginAsync`).
- Unit tests for `ManualModeService` (concurrency, idempotency, event firing).
- Unit tests for `ManualModeBroadcaster` (event → hub broadcast wiring).
- bUnit tests for `ManualModeBanner` (renders/hides, late-join, button disabling).
- Tests for the server-side guard in each affected component.

**Out of scope:**
- Hub-driven cross-circuit operation-in-progress locking (S-004).
- `ErrorDisplay` component (S-005).
- TrayHost integration (S-006, S-007).
- Changes to `AutoLaunchService` or the state machine.

---

## 3. Design

### 3.1 `ManualModeService`

Located at `src/PcsRemote.Web/Services/ManualModeService.cs`.

State is backed by an `int` field (`0` = inactive, `1` = active), toggled atomically with `Interlocked.CompareExchange` to prevent lost updates under concurrent access. `Enable()` attempts to transition `0 → 1`; if the field was already `1` (compare-exchange fails), the method is a no-op and `ManualModeChanged` is not raised. `Disable()` attempts `1 → 0`; same idempotency contract. `IsManualModeActive` reads the field with `Thread.VolatileRead` (or equivalent) to ensure visibility across threads.

`ManualModeChanged` is raised from whichever thread calls `Enable()` or `Disable()`. Subscribers are responsible for dispatching to their own synchronisation context (established project pattern: Blazor components use `InvokeAsync(StateHasChanged)`).

The class is sealed and has no external dependencies beyond the `IManualModeService` interface it implements.

### 3.2 DI Registration

`Program.cs` gains one new registration:

```text
AddSingleton<IManualModeService, ManualModeService>
```

No other DI changes for this step. `ManualModeBroadcaster` is registered as a hosted service (consistent with `PcsProStateBroadcaster`).

### 3.3 `PcsProHub` Extension

**Hub constant:** A new entry `ReceiveManualModeUpdate` is added to `PcsProHubConstants`. Its value is the string the client listens on for manual mode change messages.

**Late-joiner send:** `PcsProHub.OnConnectedAsync` is updated to send the current `IManualModeService.IsManualModeActive` value to the connecting client alongside the existing state update. The hub injects `IManualModeService`.

**`ManualModeBroadcaster` hosted service:** Located at `src/PcsRemote.Web/Services/ManualModeBroadcaster.cs`. On `StartAsync`, subscribes to `IManualModeService.ManualModeChanged`. In the event handler, broadcasts the new bool value to all hub clients via `IHubContext<PcsProHub>` using the `ReceiveManualModeUpdate` constant. The event handler must be async-safe: use `async void` and catch `Exception` at the outermost scope (logging but not re-throwing), consistent with `PcsProStateBroadcaster`'s pattern. On `StopAsync`, unsubscribes.

### 3.4 `ManualModeBanner` Component

Located at `src/PcsRemote.Web/Shared/ManualModeBanner.razor`.

**Initial state:** In `OnInitializedAsync`, subscribes to `IManualModeService.ManualModeChanged` first (to eliminate the race window), then snapshots `IsManualModeActive` as the local `_isActive` field. This ensures a late-joining browser immediately reflects active state without waiting for the next broadcast.

**Rendering:** When `_isActive` is `true`, renders a visible alert/banner with the text "Manual mode — automation paused by local operator". When `false`, renders nothing (conditional block, not visibility toggle, so no placeholder DOM is emitted).

**State updates:** The `ManualModeChanged` event handler updates `_isActive` and calls `InvokeAsync(StateHasChanged)` — standard Blazor Server thread-dispatch pattern. A `_disposed` guard prevents `StateHasChanged` after component teardown.

**Disposal:** Implements `IDisposable`. Sets `_disposed = true` and unsubscribes from `ManualModeChanged` in `Dispose()`.

**Placement:** `<ManualModeBanner />` is added to `MainLayout.razor` inside the header or above the `@Body` content area.

**Button disabling:** The banner is a pure visibility/notification component. Button disabling while manual mode is active is the responsibility of each individual button component (see §3.5), not the banner component itself. The banner does not attempt to find or disable sibling components.

### 3.5 Server-Side Command Guard

Each Blazor component that invokes an automation service operation (`ChangeMatchButton`, `RefreshScoreboardButton`, `MatchCard`, and any component invoking `LaunchAndLoginAsync`) must:

1. Inject `IManualModeService`.
2. In its click/activation handler, check `IManualModeService.IsManualModeActive` as the first action before any async work.
3. If active, show a descriptive notification to the user (e.g., "Automation is paused — disable manual mode before issuing commands") and return immediately without calling any automation service method.
4. Include `IManualModeService.IsManualModeActive` in the component's `IsEnabled` computed property (or equivalent) so the button is visually disabled in the browser when manual mode is active. This is the UI layer complement to the server-side guard.

The guard is the authoritative enforcement point. The UI disabling is complementary and informative only.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `ManualModeService` compiles as part of `PcsRemote.Web`; `PcsRemote.Core` gains no new project references. |
| AC-2 | `Enable()` and `Disable()` are idempotent: each raises `ManualModeChanged` **exactly once** per actual state transition, and does not raise it when already in the target state. |
| AC-3 | Concurrent calls to `Enable()` and `Disable()` from multiple threads do not produce duplicate events or lost-update bugs; the final state is consistent. |
| AC-4 | `ManualModeBanner` renders the text "Manual mode — automation paused by local operator" when `IsManualModeActive` is `true`; renders nothing when `false`. |
| AC-5 | `ManualModeBanner` reflects active state immediately on initial render (late-join scenario) without waiting for a hub broadcast. |
| AC-6 | Every automation-command-issuing component — `ChangeMatchButton`, `RefreshScoreboardButton`, `MatchCard`, and any component invoking `LaunchAndLoginAsync` — is visually disabled and its click handler rejects the command with the descriptive reason string `"Automation is paused — disable manual mode before issuing commands"` when `IManualModeService.IsManualModeActive` is `true`. |
| AC-7 | When manual mode is deactivated, the banner disappears and the action buttons become re-enabled in the browser. |
| AC-8 | `ManualModeBroadcaster` broadcasts the new boolean state to all hub clients whenever `ManualModeChanged` fires. |
| AC-9 | `PcsProHub.OnConnectedAsync` sends the current manual mode state to the connecting client alongside the existing state update. |
| AC-10 | All pre-existing tests continue to pass; the full solution builds with zero errors. |
| AC-11 | `ManualModeBanner` implements `IDisposable`, unsubscribes from `IManualModeService.ManualModeChanged` in `Dispose()`, and suppresses `StateHasChanged` calls after disposal via a `_disposed` guard. |

---

## 5. Branch and Commit Strategy

- Branch: `feature/IS-005-S-003-manual-mode-service`
- Strategy: small, focused commits — service implementation first, hub extension next, component and guard last. Commit messages use imperative mood.

---

## 6. Test Cases

### 6.1 `ManualModeService` Unit Tests

| TC | Scenario | Expected outcome |
|---|---|---|
| TC-1 | Fresh instance — `IsManualModeActive` | Returns `false` |
| TC-2 | `Enable()` on inactive service | `IsManualModeActive` becomes `true`; `ManualModeChanged` fires once with `true` |
| TC-3 | `Enable()` called twice on an already-active service | Second call is no-op; `ManualModeChanged` fires exactly once total |
| TC-4 | `Disable()` on active service | `IsManualModeActive` becomes `false`; `ManualModeChanged` fires once with `false` |
| TC-5 | `Disable()` called twice on an already-inactive service | Second call is no-op; `ManualModeChanged` does not fire |
| TC-6 | Concurrent `Enable()` calls from N threads on inactive service | `ManualModeChanged` fires exactly once; final state is `true` |
| TC-7 | Concurrent `Disable()` calls from N threads on active service | `ManualModeChanged` fires exactly once (`true → false`); final state is `false` |
| TC-7b | N threads call `Enable()` and N threads call `Disable()` simultaneously | `ManualModeChanged` fires at most twice total (one per direction that wins); final state equals the last successful atomic transition; no extra events |

### 6.2 `ManualModeBroadcaster` Unit Tests

| TC | Scenario | Expected outcome |
|---|---|---|
| TC-8 | `ManualModeChanged` fires `true` | Hub broadcasts `ReceiveManualModeUpdate` with `true` to all clients |
| TC-9 | `ManualModeChanged` fires `false` | Hub broadcasts `ReceiveManualModeUpdate` with `false` to all clients |
| TC-10 | Service stopped before event fires | No broadcast after `StopAsync` |
| TC-10a | `PcsProHub.OnConnectedAsync` called — manual mode inactive | Sends `ReceiveManualModeUpdate` with `false` to the calling connection only |
| TC-10b | `PcsProHub.OnConnectedAsync` called — manual mode active | Sends `ReceiveManualModeUpdate` with `true` to the calling connection only |

### 6.3 `ManualModeBanner` bUnit Tests

| TC | Scenario | Expected outcome |
|---|---|---|
| TC-11 | Render with `IsManualModeActive = false` | No banner element in DOM |
| TC-12 | Render with `IsManualModeActive = true` | Banner element present; text contains "automation paused by local operator" |
| TC-13 | `ManualModeChanged` fires `true` after initial inactive render | Banner appears in DOM without re-mount |
| TC-14 | `ManualModeChanged` fires `false` after active render | Banner disappears from DOM |
| TC-15 | Render with `IsManualModeActive = true` (late-join scenario) | Banner is visible on initial render without requiring a hub broadcast |
| TC-15a | Component disposed — `ManualModeChanged` fires after disposal | No `StateHasChanged` is invoked; no exception is thrown |

### 6.4 Command Guard Tests (per affected component)

| TC | Component | Scenario | Expected outcome |
|---|---|---|---|
| TC-16 | `ChangeMatchButton` | Manual mode active — button state | `disabled` attribute present |
| TC-17 | `ChangeMatchButton` | Click when manual mode active | Automation service not called; notification shown with reason string `"Automation is paused — disable manual mode before issuing commands"` |
| TC-18 | `ChangeMatchButton` | Manual mode deactivated after being active | Button re-enables |
| TC-19 | `RefreshScoreboardButton` | Manual mode active — button state | `disabled` attribute present |
| TC-20 | `RefreshScoreboardButton` | Click when manual mode active | Automation service not called; notification shown with reason string `"Automation is paused — disable manual mode before issuing commands"` |
| TC-20b | `RefreshScoreboardButton` | Manual mode deactivated after being active | Button re-enables |
| TC-21 | `MatchCard` | Manual mode active — card `IsInteractive` | Card not interactive; automation service not called on click |
| TC-22 | `LaunchButton` (or component gating `LaunchAndLoginAsync`) | Manual mode active — button state | `disabled` attribute present |
| TC-23 | `LaunchButton` | Click when manual mode active | `LaunchAndLoginAsync` not called; notification shown with reason string `"Automation is paused — disable manual mode before issuing commands"` |
| TC-24 | `LaunchButton` | Manual mode deactivated after being active | Button re-enables |

---

## 7. Review History

| Round | Reviewer | Verdict | Key Findings |
|---|---|---|---|
| R1 | GPT-5.4 | NEEDS REVIEW | 2 HIGH: guard surface incomplete, reason string contract vague; 2 MED: mixed concurrency TC missing, banner disposal untested |
| R1 | Claude Sonnet 4.6 | NEEDS REVIEW | 1 HIGH: AC-2 missing positive event contract; 5 MED: mixed concurrency, disposal AC/TC, hub late-join TC, RefreshScoreboardButton re-enable TC, LaunchButton guard TCs missing; 2 LOW: AC-4 text inconsistency, TC-21 "if applicable" |
| R2 | Orchestrator | APPROVED | All 9 R1 findings applied in v0.2: AC-2 now "exactly once", AC-4/TC-12 text aligned, AC-6 exhaustive list + reason string literal, AC-11 added (disposal), TC-7b mixed concurrency, TC-10a/10b hub late-join, TC-15a disposal, TC-20b RefreshScoreboardButton re-enable, TC-21 "if applicable" removed, TC-22/23/24 LaunchButton added |
