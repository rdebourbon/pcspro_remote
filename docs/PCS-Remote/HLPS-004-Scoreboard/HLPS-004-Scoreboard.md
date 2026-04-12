# HLPS-004: Scoreboard Management

| Field | Value |
|---|---|
| **Document** | HLPS-004-Scoreboard.md |
| **Status** | APPROVED |
| **Version** | 0.4 |
| **Date** | 2026-04-12 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
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
- **SHA-256 delta detection**: Hash each captured frame; skip broadcast when hash matches previous frame (per PRD §10). Forced refresh (see below) clears the stored hash before capture, guaranteeing a broadcast regardless of image content.
- **Background polling service**: `PeriodicTimer`-based service polling every 2 seconds (configurable via `Scoreboard:CaptureIntervalSeconds`; default 2s provides headroom against C-12). Active only when state = `MatchLoaded`. Stops on transition to any other state. A capture exception does not terminate the polling loop — the service continues polling on subsequent ticks. (per PRD §10)
- **Scoreboard image service event**: When a new image is captured and passes delta detection, the scoreboard service fires a `ScoreboardUpdated` event. Each `ScoreboardPreview` Blazor component subscribes on `OnInitializedAsync` and calls `InvokeAsync(StateHasChanged)` to re-render. Late joiners read the service's cached current image on initialisation. No dedicated SignalR hub is required for image data — Blazor Server's built-in circuit transport is sufficient (per PROJECT-CONTEXT.md AD#6 revised).
- **Live preview Blazor component**: Renders scoreboard image in browser. When state ≠ `MatchLoaded`, shows a "No scoreboard data" placeholder rather than blank or stale content (per S-U-2).
- **Refresh scoreboard button**: Enabled only when state = `MatchLoaded`. Triggers `RefreshScoreboardAsync`, which clears the stored SHA-256 hash before capture and bypasses delta detection. On completion, two service events fire in sequence: (1) `ScoreboardUpdated` fires first, delivering the captured image to all subscribed `ScoreboardPreview` components via the Blazor circuit pattern; (2) `RefreshCompleted` fires second as an acknowledgement signal, which components may use to display a brief confirmation notification. Both events use the service-event/Blazor-circuit pattern — no SignalR hub is involved. Disabled in all other states. (per PRD §11)
- **Change match flow**: Button with confirmation modal ("Load a different match? PCS Pro will close and reopen…"). Enabled only when state = `MatchLoaded`; disabled in all other states. On confirm, clears the stored hash, resets the cached image, stops the polling service, and transitions back to `MatchSelection` state. (per PRD §12)
- **Error propagation**: Exceptions thrown by image capture, delta detection, or `RefreshScoreboardAsync` are caught, logged via Serilog at Error level, and surfaced to the UI as a transient generic notification. Full actionable error display per C-9 is implemented in HLPS-005.
- **Unit tests**: Delta detection logic, forced-refresh hash-clear behaviour, polling service start/stop lifecycle
- **bUnit tests**: Scoreboard preview component (image and placeholder states), refresh button enabled/disabled states, change match button enabled/disabled states, change match modal
- **Playwright E2E**: Scoreboard image appears in browser; refresh button interaction; multi-browser context test verifying simultaneous update to two browser tabs (S-SC-8)

### Out of Scope

- Real PrintWindow Win32 capture (HLPS-006 — mock generates placeholder images for now)
- Dedicated SignalR hub for scoreboard image broadcast (superseded by Blazor Server component-subscription pattern; see PROJECT-CONTEXT.md AD#6 revised)
- System tray / manual mode (HLPS-005)
- Multi-user button locking during automation (HLPS-005)
- Full actionable error display (HLPS-005 — interim error propagation is in scope above)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| S-SC-1 | Scoreboard image appears in browser within 3 seconds of mock generating a new image (worst-case: image changes immediately after a poll fires) | Timing test via mock — measure from mock image availability after a poll-miss |
| S-SC-2 | Delta detection prevents broadcast when image hasn't changed | Unit test — verify no `ScoreboardUpdated` event on identical hash |
| S-SC-3 | Polling service starts when entering `MatchLoaded` and stops on any other state | Unit test for lifecycle management |
| S-SC-4 | Refresh button enabled only in `MatchLoaded` state, disabled otherwise | bUnit test for each relevant state |
| S-SC-5 | Refresh button triggers `RefreshScoreboardAsync`; `ScoreboardUpdated` fires with the captured image (even if byte-identical to previous) and a `RefreshCompleted` service event notifies all subscribed components | Integration test via mock |
| S-SC-6 | Change match confirmation modal appears on button click | bUnit test |
| S-SC-7 | Confirming change match transitions to `MatchSelection` state | Integration test via mock |
| S-SC-8 | Scoreboard updates visible to all connected browsers simultaneously | Playwright multi-browser context test — two browser contexts receive `ScoreboardUpdated` re-render simultaneously |
| S-SC-9 | Polling interval configurable via `Scoreboard:CaptureIntervalSeconds` | Configuration test |
| S-SC-10 | When state ≠ `MatchLoaded`, the scoreboard preview component renders a "No scoreboard data" placeholder | bUnit test for `NotRunning`, `MatchSelection`, and `Error` states |
| S-SC-11 | Forced refresh (`RefreshScoreboardAsync`) fires a `ScoreboardUpdated` event even when the captured image is byte-identical to the previous frame | Unit test — mock returning identical bytes; verify event fires after forced refresh |
| S-SC-12 | Change match button is disabled when state ≠ `MatchLoaded` (e.g., `Launching`, `MatchSelectionSearching`) | bUnit test for at least `Launching` and `MatchSelectionSearching` states |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? | Resolution | Status |
|---|---|---|---|---|---|
| S-U-1 | Optimal JPEG quality for mock placeholder images? PRD says 75 for real capture. | Agent | No | Use 75 for consistency with real implementation | ✅ Resolved |
| S-U-2 | Should scoreboard preview show a placeholder when state ≠ `MatchLoaded`? | Agent | No | Yes — show a "No scoreboard data" placeholder; improves UX (S-SC-10) | ✅ Resolved |
| S-U-3 | C-12 (≤3s latency) and poll interval are boundary-tight at 3s default. | Agent | No | Default poll interval set to 2 seconds to provide headroom; LAN delivery latency assumed < 50ms. S-SC-1 tests worst-case timing. | ✅ Resolved |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | NEEDS REVIEW — 1 CRITICAL (→ accepted as HIGH), 2 HIGH, 4 MEDIUM; fixes applied in v0.3 |
| R2 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | GPT APPROVED; Sonnet NEEDS REVIEW — 2 MEDIUM regressions (ScoreboardRefreshComplete transport + event sequence undefined, polling loop resilience); fixes applied in v0.4 |
| R3 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | **UNANIMOUS APPROVED** — all 3 R2 fixes verified; 0 blocking, 0 non-blocking |

### R2 Findings Applied

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| `ScoreboardRefreshComplete` transport undefined after hub removal | Sonnet | MEDIUM | Accept | Renamed to `RefreshCompleted`; specified as service event; delivery via Blazor circuit — no hub involved |
| `ScoreboardUpdated`/`RefreshCompleted` event sequence undefined | Sonnet | MEDIUM | Accept | Explicit firing order added to refresh scope item: `ScoreboardUpdated` first (image update), `RefreshCompleted` second (acknowledgement); S-SC-5 updated |
| Polling loop resilience after capture error not specified | Sonnet | LOW | Accept | Added "capture exception does not terminate the polling loop" to polling service scope item |

### R1 Findings Applied

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| No error handling for capture/SignalR failures (C-9 gap) | GPT | CRITICAL → HIGH | Accept | Added error propagation in-scope item; clarified out-of-scope boundary |
| State cleanup ambiguity on change match | GPT | HIGH | Accept | Added cleanup note (hash clear, image reset) to change match scope item |
| RefreshScoreboardAsync/delta detection interaction unspecified | Sonnet | HIGH | Accept | Added forced-refresh hash-clear to scope and S-SC-11 |
| Hub topology violates PROJECT-CONTEXT.md AD#6 | Sonnet | HIGH | Accept (different fix) | Updated PROJECT-CONTEXT.md AD#6 to Blazor circuit-based delivery; no dedicated image hub needed |
| No SC for placeholder image (S-U-2) | Both | MEDIUM | Accept | Added S-SC-10 |
| C-12 / poll interval boundary tension | Sonnet | MEDIUM | Accept | Added S-U-3; default reduced to 2s; S-SC-1 updated for worst-case timing |
| No SC for capture interface abstraction | Sonnet | MEDIUM | Defer to Delivery | Code review gate sufficient; not a runtime-testable HLPS success criterion |
| Browser compatibility not stated | GPT | MEDIUM | Reject | Covered by PROJECT-CONTEXT.md C-3 (desktop Chrome/Edge only) |
| S-SC-5 "notification" ambiguity | Sonnet | LOW | Accept | Tightened to "all connected browsers receive `ScoreboardRefreshComplete`" |
| S-SC-8 no multi-browser Playwright scope | Sonnet | LOW | Accept | Added multi-browser context test to Playwright E2E scope and S-SC-8 |
| Change match button disabled states | Sonnet | LOW | Accept | Added S-SC-12 |
| Accessibility for confirmation modal | GPT | LOW | Defer to Delivery | Implementation detail; Radzen modal is accessible by default |
| No reconnect scenario test | GPT | LOW | Reject | Component-subscription + `OnInitializedAsync` current-image read handles reconnect naturally |
