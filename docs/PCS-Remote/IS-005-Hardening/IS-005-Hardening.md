# IS-005: System Tray, Manual Mode & Production Hardening — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-005-Hardening.md |
| **Status** | IN REVIEW |
| **Version** | 0.2 |
| **Date** | 2026-04-12 |
| **Governing HLPS** | HLPS-005-Hardening.md v0.2 (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Prerequisites** | IS-004 delivered (scoreboard pipeline, polling service, RefreshScoreboardButton, ChangeMatchButton, Playwright E2E infrastructure) |

---

## Overview

This sequence implements the HLPS-005 scope in eight atomic steps: the `IManualModeService` Core contract (S-001), error reason propagation on `IPcsProAutomationService` (S-002), the `ManualModeService` singleton and hub extension with manual mode banner (S-003), multi-browser button and match card locking during automation operations (S-004), the error display component and retry flow (S-005), TrayHost restructure to host the web application in-process with a dedicated STA thread (S-006), TrayHost context menu wiring (S-007), and finally Playwright E2E tests covering error display, retry, and multi-browser locking (S-008).

Core contract steps (S-001, S-002) are independent and may be done in any order relative to each other. Web feature steps (S-003 through S-005) depend on Core contracts but are independent of TrayHost steps (S-006, S-007). TrayHost steps depend on Web features being registered in the DI container. E2E (S-008) is always last.

Each step is independently verifiable and builds on the previous. Steps are identified with stable IDs (S-001 through S-008). IDs are never renumbered; deferred steps leave gaps.

---

## Steps

### S-001 — `IManualModeService` interface in `PcsRemote.Core`

**What changes:** An `IManualModeService` interface is introduced in `PcsRemote.Core`. The interface expresses: a read-only property indicating whether manual mode is currently active; operations to enable and disable manual mode; and a change notification event that subscribers can use to react to mode transitions. No implementation is added in this step — the interface is the only deliverable.

**Why:** `PcsRemote.Core` has zero dependencies on other layers (architecture rule). Both `PcsRemote.TrayHost` (writer) and `PcsRemote.Web` (reader) must depend on Core to access the shared contract without creating illegal cross-layer dependencies. All downstream steps (S-003, S-006, S-007) require this contract to exist first. Addresses HLPS-005 §2 `IManualModeService` requirement.

**Dependencies:** No IS-005 step dependencies — this is a pure Core addition.

**Verification intent:** The interface exists in `PcsRemote.Core`; `PcsRemote.Core.csproj` has zero project references; all existing tests continue to pass. Unit test verifying the interface is defined in the correct namespace.

---

### S-002 — Error reason propagation on `IPcsProAutomationService` and `MockPcsProAutomationService` alignment

**What changes:** `IPcsProAutomationService` in `PcsRemote.Core` is extended with a mechanism for the error reason string to be surfaced to the UI layer. The concrete `MockPcsProAutomationService` (which already exposes `LastErrorReason` as a concrete property) is updated to implement the new interface member. The mock must set a meaningful reason string on each state transition that ends in the `Error` state, covering all five failure modes enumerated in H-SC-8 (the four PRD §6 timeout paths and the unexpected-dialog path).

**Why:** The error display component (S-005) reads the error reason from the interface, not from a concrete class. Without this interface member, the error display has no type-safe source for the reason string and H-SC-8 cannot be verified. Addresses HLPS-005 §2 Error reason propagation requirement.

**Dependencies:** No IS-005 step dependencies (pure Core interface change + mock update). May proceed in parallel with S-001.

**Verification intent:** `IPcsProAutomationService` exposes the error reason mechanism; Mock returns a non-null, non-empty reason string for each of the five error paths when the state machine enters `Error`; no Core dependency violations; all existing tests pass.

---

### S-003 — `ManualModeService` implementation, `PcsProHub` extension, and manual mode banner

**What changes:** Three tightly coupled deliverables in `PcsRemote.Web`:

1. **`ManualModeService`** — a singleton implementation of `IManualModeService`. It holds thread-safe state for the active flag and fires the change notification event on every toggle. It is registered in the web application's DI container as a singleton implementing the Core interface.

2. **`PcsProHub` extension** — the existing SignalR hub gains a `ManualModeChanged` broadcast method. When `ManualModeService` raises its change event, a subscriber (hosted service or event handler) invokes this broadcast so all connected browser circuits receive the current manual mode state. The dispatch from any non-hub thread must be thread-safe (using `IHubContext`).

3. **Manual mode banner** — a new Blazor component that subscribes to manual mode state via the hub. When manual mode is active it renders a visible "Manual mode — automation paused by local operator" banner and ensures all action buttons on the page are disabled. On manual mode deactivation it hides the banner and re-enables the buttons. The component's `OnInitializedAsync` fetches the current manual mode state directly from the injected `ManualModeService` singleton (not via the hub) before subscribing to change events — this ensures late-joining browsers correctly reflect active state on initial render without waiting for the next broadcast. The component correctly handles Blazor Server's async UI update pattern and disposes its subscriptions.

All automation commands on existing action buttons (`LaunchButton`, `RefreshScoreboardButton`, `ChangeMatchButton`) must check manual mode and reject the command with a descriptive reason string if active. This check must be implemented as a server-side guard in the hub methods (and/or automation service layer); UI-level button disabling via the banner is a complementary UX layer, not the authority boundary.

**Resume contract (H-SC-4):** When manual mode is deactivated, `ManualModeService` fires the change event and the hub broadcasts the cleared flag. No state machine transitions are triggered — the state machine retains its last known state (e.g., `MatchLoaded`). Automation commands become re-enabled as the action buttons react to the cleared broadcast. If manual mode is enabled mid-operation, the current in-flight trigger is allowed to complete (the state machine is passive and continues accepting triggers from the automation service); subsequent command dispatches are rejected until manual mode is deactivated. Responsibility for detecting any state drift that occurred while the local operator controlled PCS Pro directly remains with the existing polling service (IS-004). This drift caveat is explicitly deferred to HLPS-006.

**Why:** Delivers H-SC-2 (commands rejected at server-side guard), H-SC-3 (banner in all browsers, including late-joining), H-SC-4 (preserved state on resume — state machine untouched; resume contract described above). Prerequisite for S-007 (tray writes to the same singleton).

**Dependencies:** S-001 (`IManualModeService` must exist in Core).

**Verification intent:** Unit tests: `ManualModeService` enables/disables correctly; change event fires; concurrent toggle calls are safe. Unit test: `ManualModeService.Enable()` and `Disable()` cause the hub context to broadcast `ManualModeChanged` with the correct boolean state, verified in isolation from Blazor component rendering. Rejection tests: each action button returns a descriptive reason string when manual mode is active. bUnit tests: banner renders when manual mode active; banner hidden when inactive; buttons disabled while manual mode is active; banner and disabled state present on initial render when manual mode was already active before component mount (late-join scenario). All existing tests pass.

---

### S-004 — Multi-browser button and match card locking during automation operations

**What changes:** `PcsProHub` gains an `OperationInProgressChanged` broadcast event carrying a boolean flag. A coordination mechanism (new service or extension of an existing one) tracks whether any automation operation is currently in-flight across all service calls. When an operation starts, the in-progress flag is broadcast to all circuits; when it completes (success or failure), it is cleared.

Existing action button components (`RefreshScoreboardButton`, `ChangeMatchButton`) are updated to subscribe to this hub event and disable themselves based on the broadcast flag — replacing their current local-only `_operationInProgress` flag with the hub-driven cross-browser state. Match selection cards on the `Index` page are also disabled during in-progress state via the same event. The "Automation in progress…" tooltip is shown on all affected controls.

The server-side guard (state machine `InvalidOperationException` on invalid trigger) remains the authoritative concurrency contract; the UI locking is a UX layer on top.

**Why:** Addresses H-SC-5 (all browsers disabled during automation), HLPS-003's deferred concurrency guard for match card selection. The current per-component `_operationInProgress` only disables the originating browser's button — other browsers remain enabled and can issue conflicting commands that reach the state machine guard.

**Dependencies:** S-003 (`PcsProHub` extension pattern established; `ManualModeChanged` broadcast demonstrates the approach).

**Verification intent:** bUnit tests: button disabled in one circuit when operation started from another circuit (simulated via hub broadcast); match card `IsInteractive` false during in-progress; tooltip renders; button re-enables after operation completes or errors. Server-side rejection test: second concurrent call is rejected by `InvalidOperationException`. All existing tests pass.

---

### S-005 — Error display component and retry flow

**What changes:** An `ErrorDisplay` Blazor component is added to `PcsRemote.Web`. It:

- Renders only when the current `PcsProState` is `Error`.
- Shows a red error indicator (🔴) and the error reason string sourced from `IPcsProAutomationService`.
- Shows a [⟳ Retry] button.
- On Retry click: checks `IManualModeService.IsManualModeActive` first — if manual mode is active, the retry is rejected with a descriptive reason string consistent with the H-SC-2 command guard. If manual mode is inactive, fires the `Retry` trigger on the state machine (transitioning to `NotRunning`), then directly calls `LaunchAndLoginAsync()` on `IPcsProAutomationService` to restart the full automation sequence. The retry flow is the only code path responsible for re-initiating the launch after the state reaches `NotRunning` via the retry trigger — `AutoLaunchService` does not re-trigger.
- The Retry button participates in S-004's hub-driven `OperationInProgressChanged` broadcast — it is disabled when the broadcast in-progress flag is set (not a local boolean flag), ensuring all connected browsers see a consistent disabled state during any in-flight automation operation, including retry.

**Why:** Delivers H-SC-6 (error display with retry), H-SC-7 (retry restarts automation), H-SC-8 (all five error paths surface a reason string to the component). The retry chain must explicitly call `LaunchAndLoginAsync()` after `Fire(Retry)` because `AutoLaunchService` is a one-shot startup service and does not listen for `NotRunning` re-entry. The manual mode check ensures H-SC-2 is honoured even from the error recovery path.

**Dependencies:** S-001 (`IManualModeService` — needed for manual mode guard on retry button); S-002 (`IPcsProAutomationService` error reason interface must exist). S-004 (Retry button must participate in hub-driven in-progress locking).

**Verification intent:** Unit tests: retry triggers state transition then calls `LaunchAndLoginAsync()`; `LaunchAndLoginAsync()` is not called when retry is already in-flight; `Retry` trigger on a non-`Error` state throws; retry rejected with descriptive reason string when manual mode is active. bUnit tests: component renders with reason string in `Error` state; does not render in other states; retry button disabled when hub in-progress flag is set (not just local state); retry button disabled in second circuit when retry initiated from first circuit; component re-enables on `NotRunning` re-entry (confirming H-SC-7 flow). Error injection tests covering all five H-SC-8 failure modes: correct reason string surfaced per mode.

---

### S-006 — TrayHost restructure: in-process web hosting and STA message loop

**What changes:** `PcsRemote.TrayHost.Program.cs` is restructured to become the process entry point for the combined application. It builds and runs the ASP.NET Core `WebApplication` (previously started from `PcsRemote.Web.Program.cs`). An `IHostedService` implementation in `TrayHost` is responsible for starting the WinForms `Application.Run()` message loop on a dedicated background thread configured with STA apartment state. This ensures the WinForms message pump and Kestrel's MTA thread pool coexist without STA/MTA conflicts.

All DI registrations previously in `PcsRemote.Web.Program.cs` remain in Web; `TrayHost` adds only its own registrations (including the `IManualModeService` singleton resolved from the shared DI container). `PcsRemote.Web` remains the web layer project and continues to work as a standalone host for testing purposes (Playwright `WebApplicationFactory` tests remain unaffected).

**Why:** H-U-2 resolution (HLPS-005 §4). The TrayHost must be the process host to hold the WinForms `NotifyIcon` message loop. Without this step, S-007 (context menu) cannot wire to the running web application. The STA thread requirement is non-negotiable for WinForms `NotifyIcon` to function correctly.

**Dependencies:** S-001, S-002, S-003, S-004, S-005. All Web feature steps must be complete before TrayHost integration to ensure the hosted application is coherent. The formal dependency list reflects this requirement and supersedes the informal prose previously present in this field.

**Verification intent:** Application starts from `PcsRemote.TrayHost` exe; tray icon appears in Windows notification area; Kestrel is serving the Blazor app at the configured URL; all existing Playwright tests continue to pass (WebApplicationFactory still targets Web project directly). Build succeeds with no new warnings.

---

### S-007 — TrayHost context menu: manual mode toggle, Open Browser, and Exit

**What changes:** The `NotifyIcon` context menu in `PcsRemote.TrayHost` is wired to application services:

- **Manual Mode toggle**: menu item text switches between "Switch to Manual Mode" and "Resume Automation" based on current `IManualModeService.IsManualModeActive` state. Clicking the item calls the appropriate enable/disable method. The tray icon image changes to a distinct visual for each mode (two icon assets required).
- **Open Browser**: launches the system's default browser process pointing to the configured application URL (`https://localhost:{port}` or equivalent).
- **Exit**: calls `StopAsync` on `IPcsProAutomationService` (graceful shutdown of the automation service), then calls `Application.Exit()` to stop the WinForms message loop, which in turn triggers the hosted service `StopAsync` to shut down Kestrel.

All context menu operations originate on the WinForms STA thread; any calls to ASP.NET Core services must be dispatched safely (non-blocking fire-and-forget for `StopAsync`, with a bounded wait before force-exit).

**Why:** Delivers H-SC-1 (tray visible; visual state; context menu), H-SC-2 (tray side of manual mode toggle), H-SC-9 (Open Browser), H-SC-10 (Exit/StopAsync). The tray is the primary local control surface for the garage PC operator.

**Dependencies:** S-006 (TrayHost must be restructured and running the web host before context menu items can resolve DI services); S-003 (`IManualModeService` implementation registered).

**Verification intent:** Manual test on Windows: tray icon visible; icon changes on toggle; "Switch to Manual Mode" click → banner appears in connected browser; "Resume Automation" click → banner disappears; "Open Browser" opens browser to app URL; "Exit" terminates process within 5 seconds (H-SC-10). H-SC-1, H-SC-9, H-SC-10 verified manually per their success criteria.

---

### S-008 — Playwright E2E: error display, retry flow, multi-browser operation locking, and manual mode

**What changes:** Playwright E2E tests are added to `PcsRemote.E2E.Tests` covering:

- **TC-1 (Error display)**: Mock automation service is configured to inject an error for one of the five H-SC-8 failure modes. The test verifies the `ErrorDisplay` component renders with a non-empty reason string and the retry button is visible.
- **TC-2 (Retry restarts automation)**: From the error state established in TC-1, the test clicks the retry button and verifies the state machine transitions through `NotRunning → Launching → …` (the re-launch sequence begins).
- **TC-3 (Multi-browser locking)**: Two browser contexts are open. One triggers a long-running automation operation (e.g., a change-match flow paused via mock delay). The test verifies that the second browser's action buttons and match cards are disabled while the operation is in-flight on the first browser, and re-enable after completion.
- **TC-4 (Manual mode banner and locking — multi-browser)**: Two browser contexts are open. Manual mode is enabled via `IManualModeService` resolved from `RealServices`. The test verifies: the manual mode banner is visible in both browser contexts; all action buttons are disabled in both contexts; an automation command submitted while manual mode is active is rejected with a descriptive reason string.
- **TC-5 (Manual mode resume — state preservation)**: From the state established in TC-4, manual mode is disabled. The test verifies: the banner disappears in both browser contexts; the state machine remains in the same state it held when manual mode was enabled (no state reset occurs); action buttons are re-enabled.

The `PcsProWebApplicationFactory` is extended to register mock error injection, operation-delay capabilities, and programmatic `IManualModeService` control needed by the new tests.

**Why:** Provides automated verification of H-SC-3, H-SC-4, H-SC-5, H-SC-6, H-SC-7, and H-SC-8 at the integration level, complementing unit and bUnit coverage from S-003, S-004, and S-005. TC-4 and TC-5 deliver the multi-browser integration verification for H-SC-3 and H-SC-4 that bUnit alone cannot provide.

**Dependencies:** S-004 (multi-browser locking), S-005 (error display and retry). S-006 is **not** a dependency — all test cases use `PcsProWebApplicationFactory` which targets `PcsRemote.Web` directly and does not require TrayHost to be the process host. This allows S-008 and S-006/S-007 to proceed in parallel on the critical path.

**Verification intent:** All five E2E test cases pass in CI; all 234+ existing tests continue to pass. TC-1 and TC-2 directly verify H-SC-6 and H-SC-7. TC-3 verifies H-SC-5 at the browser level. TC-4 and TC-5 verify H-SC-3 and H-SC-4 at the multi-browser integration level.

---

## Delivery Notes

- **TrayHost testing**: H-SC-1, H-SC-9, and H-SC-10 require manual verification on Windows. They cannot be automated via Playwright as tray interaction requires native OS input outside the browser context. These are explicitly accepted as manual-only verification per HLPS-005 §3.
- **Concurrency guard**: The state machine `InvalidOperationException` is the server-side authoritative guard throughout. The UI locking added in S-004 is complementary, not a replacement. Tests for both layers are required.
- **Critical path parallelism**: S-008 and S-006/S-007 have no shared dependencies and may proceed in parallel once S-004 and S-005 are complete. The TrayHost restructure does not gate E2E verification of web features.

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | Claude Opus 4.6, GPT-5.4, Claude Sonnet 4.6 | NEEDS REVIEW — 9 findings applied (v0.2) |
