# HLPS-003: Real-Time Web Control Panel

| Field | Value |
|---|---|
| **Document** | HLPS-003-Web-Control-Panel.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-11 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0; `docs/Requirements/PRD - Match Selection and Scoreboard.md` v1.0 |
| **Dependencies** | HLPS-001 (domain model — `PcsProState`, `MatchInfo`, `MatchTeams`, `IPcsProAutomationService`), HLPS-002 (mock service required to test all UI interactions) |

---

## 1. Problem Statement

The existing automation system provides no remote visibility or control — operators must be physically present at the garage PC. There is no status feedback, no error reporting, and no way for multiple people to see what's happening.

This HLPS delivers the core browser-based control panel: a Blazor Server application with real-time state display, match selection, and multi-user awareness. When complete, an operator on the local network can see PCS Pro's state, select today's match, and monitor progress — all from a browser.

This is the first HLPS that produces visible, interactive output. It connects the domain model (HLPS-001) and mock service (HLPS-002) to a real user interface.

---

## 2. Scope

### In Scope

- **Blazor Server host setup**: ASP.NET Core web application in `PcsRemote.Web` with Radzen Blazor components
- **DI and middleware pipeline**: Complete `Program.cs` with Blazor Server middleware (deferred from HLPS-001), Radzen services, and SignalR hub mapping; `IPcsProAutomationService` registration uses the mechanism delivered in HLPS-002
- **Radzen theming and layout**: Application shell, navigation, consistent styling (no Bootstrap)
- **SignalR hub**: `PcsProHub` for explicit state broadcasting and connection tracking; on client connect, the hub pushes current state immediately so late-joining browsers are not left stale
- **PCS Pro status indicator**: Coloured indicator reflecting current state (per PRD §12: ⚪🟡🟢🔴 mapping, including `Error` → 🔴)
- **Connected user count**: Real-time count of connected browsers displayed in header (per PRD §12); updates are delivered via Blazor Server's SignalR connection, typically sub-second on a local network
- **Match selection flow**:
  - Display today's matches as selectable cards (per PRD §8: home team, away team, match type, start time)
  - While `GetTodaysMatchesAsync` is in flight (`MatchSelectionSearching` state), show a loading indicator and disable card interaction
  - Auto-select when only one match is found
  - Multi-match card presentation; selection triggers `LoadMatchAsync` → state transitions visible in real-time
- **Team name display**: After match load, home and away team names shown in UI (per PRD §9)
- **Match card interactivity by state**: Match cards are only selectable when state is `MatchSelectionReady`; all other states render cards as non-interactive
- **Blazor component tests**: bUnit tests for key components (status indicator, match card, button states)
- **Playwright E2E test infrastructure**: Project setup, first smoke test (app loads, status indicator visible)
- **Auto-launch flow**: On application start, automatically initiate the LaunchAndLogin → MatchSelection flow; if the flow reaches `Error` state, the indicator shows 🔴 — error details and retry are deferred to HLPS-005

> **Design decision (W-U-2 resolved):** Auto-launch is the default behaviour; a configuration flag (`PcsPro:AutoLaunch`) may disable it for environments where manual control is preferred.

### Out of Scope

- Scoreboard image capture/preview (HLPS-004)
- Refresh scoreboard / change match buttons (HLPS-004)
- System tray / manual mode (HLPS-005)
- Multi-user button locking and concurrency guard on match selection (HLPS-005); concurrent selection by two users is undefined behaviour in this iteration
- Error display component, error details, and retry flow (HLPS-005); the `Error` state is visually indicated (🔴) but non-actionable until HLPS-005 — operators must restart the service as a workaround
- `StopAsync` UI (deferred to HLPS-005)
- Authentication and authorisation (HLPS-007)
- Real FlaUI automation (HLPS-006)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| W-SC-1 | Web application launches and is accessible from a browser on the local network | Manual test + Playwright smoke test |
| W-SC-2 | PCS Pro state indicator updates in real-time as mock transitions through states, including `Error` → 🔴 | Visual inspection + bUnit test |
| W-SC-3 | Connected user count updates when browsers connect/disconnect | Playwright multi-context test (two browser contexts) + manual fallback |
| W-SC-4 | Today's matches displayed as selectable cards with correct data from mock (home team, away team, match type, start time) | bUnit test + visual inspection |
| W-SC-5 | Single match auto-selected without user interaction | Integration test via mock (1 match configured) |
| W-SC-6 | Match selection triggers state transitions visible in browser | End-to-end test via mock |
| W-SC-7 | Team names (home/away) displayed after match load | bUnit test + visual inspection |
| W-SC-8 | Match cards are only selectable when state is `MatchSelectionReady`; cards render as non-interactive in all other states | bUnit test for each relevant state |
| W-SC-9 | All state changes pushed to all connected browsers simultaneously | Playwright multi-context test (two browser contexts assert same state) + manual fallback |
| W-SC-10 | Playwright E2E project runs at least one smoke test successfully | `dotnet test` on E2E project |
| W-SC-11 | On application start, auto-launch flow initiates automatically and state transitions are visible to any browser connecting during or after the flow | Integration test via mock + Playwright smoke test |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| W-U-1 | Radzen Blazor theme choice: Material, Standard, Dark, or custom? Affects overall look and feel. | User | No — can start with default theme and customise later |
| W-U-3 | Kestrel binding address: must listen on `0.0.0.0` (all interfaces) not `localhost` for W-SC-1 (local network access) to be satisfied. Confirm default or explicit configuration. | Agent | No — default to `0.0.0.0` in `appsettings.json`; can be overridden per deployment |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | Sonnet 4.6, Opus 4.6, GPT-4.1 | NEEDS REVIEW — 3 HIGH, 7 MEDIUM accepted; v0.2 fixes applied |
| R2 | 2026-04-11 | Sonnet 4.6, Opus 4.6, GPT-4.1 | **APPROVED** — all 14 R1 findings confirmed fixed, 0 regressions, unanimous |
