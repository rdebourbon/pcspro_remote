# HLPS-005: System Tray, Manual Mode & Production Hardening

| Field | Value |
|---|---|
| **Document** | HLPS-005-Hardening.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | HLPS-003 (web UI must exist to add hardening to) |

---

## 1. Problem Statement

The application will run on a shared garage PC where someone may occasionally need to interact directly with PCS Pro (configuration, troubleshooting, manual scoring checks). If remote automation runs while a human is using PCS Pro, FlaUI and human clicks will conflict, causing unpredictable failures.

Additionally, when multiple operators connect from different browsers, they could issue conflicting commands simultaneously. And when automation fails, operators need clear, actionable error messages with retry capability — not silent failures or cryptic exceptions.

This HLPS delivers three capabilities:
1. **System tray with manual mode** — local operator can pause remote automation at the garage PC.
2. **Multi-user safety** — buttons disabled during automation, preventing conflicting commands.
3. **Error hardening** — error display component, retry flow, and actionable error messages for every failure mode.

---

## 2. Scope

### In Scope

- **System tray icon** (`PcsRemote.TrayHost`): WinForms `NotifyIcon` with context menu
  - "Switch to Manual Mode" / "Resume Automation" toggle
  - Tray icon visual state changes to indicate current mode
  - "Open Browser" shortcut (launches default browser to app URL)
  - "Exit" option
- **Manual mode service**: Shared state accessible by web layer. When active:
  - All remote automation commands rejected with descriptive reason
  - Web UI shows "Manual mode — automation paused by local operator" banner
  - All action buttons disabled in all connected browsers
  - Current PCS Pro state preserved (no restart on resume)
  - `StateChanged` events still fire (state observation continues)
- **Multi-user button locking**: During any automation operation (launch, login, match search, match load, refresh):
  - All action buttons disabled for ALL connected browsers
  - "Automation in progress…" tooltip shown (per PRD §12)
  - State machine naturally prevents conflicting actions
- **Error display component**: Blazor component showing error state with:
  - Red indicator (🔴) and descriptive error message (per PRD §12)
  - Actionable guidance (e.g., "Check PCS Pro credentials", "Verify PCS Pro is reachable")
  - [⟳ Retry] button that transitions through Error → NotRunning → restart flow
- **Retry flow**: Error state → Retry → NotRunning → full automation restart (per PRD §6)
- **Unit tests**: Manual mode service, button state logic with manual mode, error state transitions
- **bUnit tests**: Error display component, manual mode banner, disabled button states
- **Playwright E2E**: Error display renders correctly, retry button works

### Out of Scope

- Real FlaUI automation (HLPS-006)
- Task Scheduler deployment (HLPS-007)
- Crash recovery (HLPS-007)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| H-SC-1 | System tray icon visible when application runs; context menu accessible | Manual test on Windows |
| H-SC-2 | Manual mode toggle pauses all remote automation commands | Test: toggle manual mode, attempt command from browser → rejected |
| H-SC-3 | Web UI shows "Manual mode" banner in all connected browsers when manual mode active | Multi-browser test |
| H-SC-4 | Resuming automation picks up from preserved state without restart | Test: pause in MatchLoaded, resume → still MatchLoaded |
| H-SC-5 | All buttons disabled for all browsers during automation operations | Multi-browser test during mock state transitions |
| H-SC-6 | Error state displays actionable message with retry button | bUnit test + visual inspection |
| H-SC-7 | Retry from error state restarts full automation flow | Integration test via mock with error injection |
| H-SC-8 | No silent failures — every error path surfaces to UI (per C-9) | Error injection test for each timeout/failure mode |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| H-U-1 | Should manual mode persist across application restarts, or always default to "automation active"? | Agent | No — default to "automation active" on start; safer for unattended garage PC |
| H-U-2 | TrayHost as separate executable or integrated into PcsRemote.Web? A separate exe would allow the tray to survive web crashes, but adds deployment complexity. | Agent | No — start integrated (same process); separate exe is a future enhancement if needed |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | Not yet reviewed |
