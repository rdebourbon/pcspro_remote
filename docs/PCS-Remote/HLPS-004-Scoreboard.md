# HLPS-004: Scoreboard Management

| Field | Value |
|---|---|
| **Document** | HLPS-004-Scoreboard.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | HLPS-003 (web UI infrastructure and SignalR hub must exist) |

---

## 1. Problem Statement

Once a match is loaded, operators need two things: a live preview of the scoreboard as it appears on the LED display, and the ability to force a scoreboard refresh when data is lost in transit over the COM5 serial connection.

Currently there is no way to see the scoreboard remotely — operators must walk to the physical LED board or peer at the garage PC screen. There is no remote refresh capability either; lost COM messages require physical intervention.

This HLPS delivers the scoreboard image capture pipeline (using mock-generated images initially, real PrintWindow in HLPS-006), SHA-256 delta detection to avoid unnecessary broadcasts, live browser preview, the refresh scoreboard button, and the change match flow.

---

## 2. Scope

### In Scope

- **Scoreboard image capture interface**: Abstract the capture mechanism so mock (generates placeholder images) and real (PrintWindow) implementations are interchangeable
- **SHA-256 delta detection**: Hash each captured frame; skip SignalR broadcast when hash matches previous frame (per PRD §10)
- **Background polling service**: `PeriodicTimer`-based service polling every 3 seconds (configurable). Active only when state = `MatchLoaded`. Stops on transition to any other state. (per PRD §10)
- **SignalR image broadcast**: `ScoreboardUpdate` event sending base64-encoded JPEG to all connected browsers (per PRD §10)
- **Live preview Blazor component**: Renders scoreboard image in browser, updates automatically on `ScoreboardUpdate` event (per PRD §10)
- **Refresh scoreboard button**: Enabled only when state = `MatchLoaded`. Triggers `RefreshScoreboardAsync`. Pushes `ScoreboardRefreshComplete` notification via SignalR (per PRD §11)
- **Change match flow**: Button with confirmation modal ("Load a different match? PCS Pro will close and reopen…"). On confirm, transitions back to `MatchSelection` state (per PRD §12)
- **Unit tests**: Delta detection logic, polling service start/stop behaviour, button state management
- **bUnit tests**: Scoreboard preview component, refresh button state, change match modal
- **Playwright E2E**: Scoreboard image appears in browser, refresh button interaction

### Out of Scope

- Real PrintWindow Win32 capture (HLPS-006 — mock generates placeholder images for now)
- System tray / manual mode (HLPS-005)
- Multi-user button locking during automation (HLPS-005)
- Error display (HLPS-005)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| S-SC-1 | Scoreboard image appears in browser within 3 seconds of mock generating a new image | Timing test via mock |
| S-SC-2 | Delta detection prevents broadcast when image hasn't changed | Unit test — verify no broadcast on identical hash |
| S-SC-3 | Polling service starts when entering `MatchLoaded` and stops on any other state | Unit test for lifecycle management |
| S-SC-4 | Refresh button enabled only in `MatchLoaded` state, disabled otherwise | bUnit test for each state |
| S-SC-5 | Refresh button triggers `RefreshScoreboardAsync` and shows confirmation notification | Integration test via mock |
| S-SC-6 | Change match confirmation modal appears on button click | bUnit test |
| S-SC-7 | Confirming change match transitions to `MatchSelection` state | Integration test via mock |
| S-SC-8 | Scoreboard updates visible to all connected browsers simultaneously | Multi-browser test |
| S-SC-9 | Polling interval configurable via `Scoreboard:CaptureIntervalSeconds` | Configuration test |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| S-U-1 | Optimal JPEG quality for mock placeholder images? PRD says 75 for real capture. Mock can use any quality — intent is to exercise the pipeline. | Agent | No — use 75 for consistency |
| S-U-2 | Should scoreboard preview show a "No scoreboard data" placeholder when state ≠ MatchLoaded? | Agent | No — yes, show a placeholder; improves UX |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | Not yet reviewed |
