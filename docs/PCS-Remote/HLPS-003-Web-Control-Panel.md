# HLPS-003: Real-Time Web Control Panel

| Field | Value |
|---|---|
| **Document** | HLPS-003-Web-Control-Panel.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | HLPS-002 (mock service required to test all UI interactions) |

---

## 1. Problem Statement

The existing automation system provides no remote visibility or control — operators must be physically present at the garage PC. There is no status feedback, no error reporting, and no way for multiple people to see what's happening.

This HLPS delivers the core browser-based control panel: a Blazor Server application with real-time state display, match selection, and multi-user awareness. When complete, an operator on the local network can see PCS Pro's state, select today's match, and monitor progress — all from a browser.

This is the first HLPS that produces visible, interactive output. It connects the domain model (HLPS-001) and mock service (HLPS-002) to a real user interface.

---

## 2. Scope

### In Scope

- **Blazor Server host setup**: ASP.NET Core web application in `PcsRemote.Web` with Radzen Blazor components
- **Radzen theming and layout**: Application shell, navigation, consistent styling (no Bootstrap)
- **SignalR hub**: `PcsProHub` for explicit state broadcasting and connection tracking
- **PCS Pro status indicator**: Coloured indicator reflecting current state (per PRD §12: ⚪🟡🟢🔴 mapping)
- **Connected user count**: Real-time count of connected browsers displayed in header (per PRD §12)
- **Match selection flow**:
  - Display today's matches as selectable cards (per PRD §8)
  - Auto-select when only one match is found
  - Multi-match card presentation with team names, match type, time
  - Selection triggers `LoadMatchAsync` → state transitions visible in real-time
- **Team name display**: After match load, home and away team names shown in UI (per PRD §9)
- **Button state management**: Buttons enabled/disabled based on current PCS Pro state (per PRD §12)
- **Blazor component tests**: bUnit tests for key components (status indicator, match card, button states)
- **Playwright E2E test infrastructure**: Project setup, first smoke test (app loads, status indicator visible)
- **Auto-launch flow**: On application start, automatically begin the LaunchAndLogin → MatchSelection flow

### Out of Scope

- Scoreboard image capture/preview (HLPS-004)
- Refresh scoreboard / change match buttons (HLPS-004)
- System tray / manual mode (HLPS-005)
- Multi-user button locking during automation (HLPS-005)
- Error display component and retry flow (HLPS-005)
- Real FlaUI automation (HLPS-006)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| W-SC-1 | Web application launches and is accessible from a browser on the local network | Manual test + Playwright smoke test |
| W-SC-2 | PCS Pro state indicator updates in real-time as mock transitions through states | Visual inspection + bUnit test |
| W-SC-3 | Connected user count updates when browsers connect/disconnect | Multi-browser manual test |
| W-SC-4 | Today's matches displayed as selectable cards with correct data from mock | bUnit test + visual inspection |
| W-SC-5 | Single match auto-selected without user interaction | Integration test via mock (1 match configured) |
| W-SC-6 | Match selection triggers state transitions visible in browser | End-to-end test via mock |
| W-SC-7 | Team names (home/away) displayed after match load | bUnit test + visual inspection |
| W-SC-8 | Buttons correctly enabled/disabled per state (per PRD §12 button states table) | bUnit test for each state |
| W-SC-9 | All state changes pushed to all connected browsers simultaneously | Multi-browser test |
| W-SC-10 | Playwright E2E project runs at least one smoke test successfully | `dotnet test` on E2E project |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| W-U-1 | Radzen Blazor theme choice: Material, Standard, Dark, or custom? Affects overall look and feel. | User | No — can start with default theme and customise later |
| W-U-2 | Should auto-launch (LaunchAndLogin) happen on app start or require manual trigger from browser? | Agent | No — default to auto-launch per PRD flow; can be toggled via config |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | Not yet reviewed |
