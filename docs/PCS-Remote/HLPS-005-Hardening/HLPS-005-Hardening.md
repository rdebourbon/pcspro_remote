# HLPS-005: System Tray, Manual Mode & Production Hardening

| Field | Value |
|---|---|
| **Document** | HLPS-005-Hardening.md |
| **Status** | DRAFT |
| **Version** | 0.2 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | HLPS-003 (web UI baseline), HLPS-004 (scoreboard controls — button-locking and error display harden these components) |

---

## 1. Problem Statement

The application will run on a shared garage PC where someone may occasionally need to interact directly with PCS Pro (configuration, troubleshooting, manual scoring checks). If remote automation runs while a human is using PCS Pro, FlaUI and human clicks will conflict, causing unpredictable failures.

Additionally, when multiple operators connect from different browsers, they could issue conflicting commands simultaneously. And when automation fails, operators need clear, actionable error messages with retry capability — not silent failures or cryptic exceptions.

This HLPS delivers four capabilities:
1. **System tray with manual mode** — local operator can pause remote automation at the garage PC.
2. **Multi-user safety** — buttons disabled during automation, preventing conflicting commands.
3. **Error hardening** — error display component, retry flow, and actionable error messages for every failure mode.
4. **Graceful shutdown** — "Exit" tray option stops the automation service cleanly before process termination (deferred from HLPS-003).

---

## 2. Scope

### In Scope

- **System tray icon** (`PcsRemote.TrayHost`): WinForms `NotifyIcon` with context menu
  - "Switch to Manual Mode" / "Resume Automation" toggle
  - Tray icon visual state changes to indicate current mode (distinct icons for Automation Active / Manual Mode)
  - "Open Browser" shortcut (launches default browser to configured app URL)
  - "Exit" option — calls `StopAsync` on `IPcsProAutomationService` before process termination; graceful shutdown only (crash recovery deferred to HLPS-007)
- **TrayHost integration**: `PcsRemote.TrayHost` hosts the ASP.NET Core web host in-process. A dedicated STA thread (`Thread.SetApartmentState(ApartmentState.STA)`) runs the WinForms `Application.Run()` message loop inside an `IHostedService`, ensuring WinForms and Kestrel coexist without STA/MTA conflicts.
- **`IManualModeService`** (interface in `PcsRemote.Core`): both `PcsRemote.TrayHost` (writes) and `PcsRemote.Web` (reads) depend on Core — no illegal layer dependency. Implemented as a DI-registered singleton.
- **Manual mode service**: When active:
  - All remote automation commands rejected with a descriptive reason string
  - Web UI shows "Manual mode — automation paused by local operator" banner, pushed to all connected browsers via a new `ManualModeChanged` event on `PcsProHub` (thread-safe dispatch from the STA thread via `InvokeAsync`)
  - All action buttons and match cards disabled in all connected browsers
  - Current PCS Pro state is preserved in the state machine; **no new state transitions occur** — the state machine is passive and only transitions when the automation service fires triggers; with automation paused, no triggers fire
  - On resume: the preserved last-known state is used as-is. State re-synchronisation with the actual PCS Pro UI is deferred to HLPS-006 (FlaUI observation); under mock-only scope the mock preserves state identically. Engineers should note that if a local operator changes PCS Pro while paused, the displayed state will be stale until HLPS-006 provides re-sync.
  - `StateChanged` events are **not fired** during manual mode pause; late-joining browsers receive the cached last-known state on connect via the existing hub push
- **Multi-user button locking**: During any automation operation (launch, login, match search, match load, refresh, change match):
  - **Server-side guard**: only one automation command may be in-flight at a time; any concurrent command issued before the UI disable propagates is rejected by the state machine (`InvalidOperationException` on invalid trigger) — this is the authoritative concurrency contract
  - **UI guard**: all action buttons **and match selection cards** (`IsInteractive`) disabled for ALL connected browsers; the UI guard is UX-only and does not replace the server-side guard
  - "Automation in progress…" tooltip shown (per PRD §12)
- **Error reason propagation**: `IPcsProAutomationService` (in `PcsRemote.Core`) must expose a mechanism for the error reason string to be consumed by the UI layer — either a `string? LastErrorReason` property or an `ErrorOccurred` event with a reason payload. The exact contract is determined during IS derivation; the principle is that the interface owns the error reason, not a concrete class.
- **Error display component**: Blazor component showing error state with:
  - Red indicator (🔴) and descriptive error message sourced from `IPcsProAutomationService` (per PRD §12)
  - Actionable guidance text is rendered from the error reason string; examples of future FlaUI guidance: "Check PCS Pro credentials", "Verify PCS Pro is reachable". Under mock scope the component renders whatever reason string the mock injects — no FlaUI-specific routing required at this stage.
  - [⟳ Retry] button that transitions through Error → NotRunning → restart flow
- **Retry flow**: Error state → `Fire(Retry)` → `NotRunning` → the retry button's click handler then directly calls `LaunchAndLoginAsync()` (or equivalent re-launch entry point) to restart the full automation sequence. `AutoLaunchService` fires once on startup only and does not re-trigger on `NotRunning` re-entry; the retry button is responsible for re-initiating the launch chain.
- **Unit tests**: Manual mode service, button state logic with manual mode, error state transitions, retry flow re-launch chain
- **bUnit tests**: Error display component, manual mode banner, disabled button/card states
- **Playwright E2E**: Error display renders correctly, retry button works. Manual mode banner and disabled-state tests are covered by bUnit (component level) and manual multi-browser test; direct Playwright E2E of the tray toggle is not practical as tray interaction requires native WinForms input outside the browser context.

### Out of Scope

- Real FlaUI automation (HLPS-006)
- Task Scheduler deployment (HLPS-007)
- Crash recovery (HLPS-007)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| H-SC-1 | System tray icon visible when application runs; context menu accessible; tray icon changes visually when manual mode is toggled (distinct icon per mode) | Manual test on Windows |
| H-SC-2 | Manual mode toggle pauses all remote automation commands | Test: toggle manual mode, attempt command from browser → rejected with descriptive reason |
| H-SC-3 | Web UI shows "Manual mode" banner in all connected browsers when manual mode active | bUnit component test + manual multi-browser test |
| H-SC-4 | Resuming automation picks up from preserved state without restart | Test: pause in MatchLoaded, resume → state machine still shows MatchLoaded |
| H-SC-5 | All action buttons and match cards disabled for all browsers during automation operations | bUnit + multi-browser test during mock state transitions |
| H-SC-6 | Error state displays actionable message with retry button | bUnit test + visual inspection |
| H-SC-7 | Retry from error state restarts full automation flow | Integration test via mock: inject error → retry → state transitions NotRunning → Launching → … |
| H-SC-8 | Every error path that results in a `PcsProState.Error` state surfaces a descriptive reason string to the UI. Failure modes covered: all four PRD §6 timeout paths (Launching→LoginScreen 40s, LoginScreen→MatchSelection 20s, MatchSelectionSearching→MatchSelectionReady 30s, MatchSelection→MatchLoaded 15s) + unexpected-dialog path. | Error injection test per failure mode via mock; reason string visible in component |
| H-SC-9 | "Open Browser" tray menu item launches default browser to the configured application URL | Manual test on Windows |
| H-SC-10 | "Exit" tray menu item calls `StopAsync` on the automation service and terminates the process cleanly | Manual test: confirm no orphaned PCS Pro state; process exits within 5s |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| H-U-1 | Manual mode default on restart | Agent | **Resolved** — default to "automation active" on start; safer for unattended garage PC; no persistence required |
| H-U-2 | TrayHost integration model | Agent | **Resolved** — `PcsRemote.TrayHost` (`WinExe`) is the process host; it creates the `WebApplication` builder and runs the Kestrel web host in-process, with a dedicated STA background thread (`Thread.SetApartmentState(ApartmentState.STA)`) running the WinForms `Application.Run()` message loop inside an `IHostedService`. `PcsRemote.Web` remains the web layer project; `TrayHost` is the entry point exe. Separate-exe deployment is a HLPS-007 future enhancement. |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | Claude Opus 4.6, GPT-5.4, Claude Sonnet 4.6 | NEEDS REVIEW — 2 CRITICAL, 7 HIGH, 4 MEDIUM |
| R2 | 2026-04-10 | Agent (self-review, all R1 findings applied) | Pending user sign-off |
