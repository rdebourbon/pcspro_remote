# IS-004: Scoreboard Management — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-004-Scoreboard.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-12 |
| **Governing HLPS** | HLPS-004-Scoreboard.md v0.4 (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Prerequisites** | IS-003 delivered (Blazor Server pipeline, SignalR hub, circuit handler, Playwright E2E infrastructure) |

---

## Overview

This sequence implements the HLPS-004 scope in six atomic steps: the scoreboard service interface and singleton (delta detection, event model, cache), the background polling hosted service (lifecycle tied to `MatchLoaded`), the live preview component (image rendering and placeholder), the refresh scoreboard button (forced capture bypass), the change match flow (modal confirmation and state cleanup), and finally Playwright E2E tests for multi-browser simultaneous update.

Each step is independently verifiable and builds on the previous. Steps are identified with stable IDs (S-001 through S-006). IDs are never renumbered; deferred steps leave gaps.

---

## Steps

### S-001 — Scoreboard service: interface, singleton, delta detection, and event model

**What changes:** A `IScoreboardService` interface is introduced in `PcsRemote.Core` and a concrete singleton implementation is added in `PcsRemote.Web`. The service owns:
- A cache of the current scoreboard image (byte array, nullable — null until first capture).
- A SHA-256 hash of the most recently broadcast image, used for delta detection.
- A `ScoreboardUpdated` event fired when a new image passes delta detection or a forced refresh completes. Carries the captured image bytes.
- A `RefreshCompleted` event fired after `ScoreboardUpdated` on a forced refresh path only, serving as an acknowledgement signal.
- A `CaptureAndBroadcastAsync` method (called by the polling service) that captures via `IPcsProAutomationService`, computes the hash, compares against the cached hash, and fires `ScoreboardUpdated` only on change.
- A `ForceRefreshAsync` method that clears the stored hash before capture, fires `ScoreboardUpdated` unconditionally, then fires `RefreshCompleted`.
- A `ClearCache` method that resets the stored hash and cached image to null (called by the change match flow).

The implementation is registered as a singleton in `Program.cs`. The mock automation service already provides `CaptureScoreboardImageAsync`, so no changes to `PcsRemote.Automation.Mock` are required in this step.

**Why:** All HLPS-004 scope items depend on this service as their shared foundation. Delta detection (S-SC-2), forced-refresh hash bypass (S-SC-5, S-SC-11), and the late-joiner image cache are all implemented here. Separating the service from the polling mechanism and the UI allows each to be tested independently and keeps `PcsRemote.Core` free of infrastructure concerns.

**Dependencies:** HLPS-002 delivery (mock automation service with `CaptureScoreboardImageAsync` exists). No IS-004 step dependencies — this is the first step.

**Verification intent:** Unit tests covering: `CaptureAndBroadcastAsync` fires `ScoreboardUpdated` on first capture; does not fire on identical hash (S-SC-2); fires on changed hash. `ForceRefreshAsync` clears hash, fires `ScoreboardUpdated` even when image bytes are identical to the cached value (S-SC-11), then fires `RefreshCompleted`. `ClearCache` resets both hash and cached image to null. Build and all existing tests pass.

---

### S-002 — Scoreboard polling hosted service (PeriodicTimer, lifecycle tied to MatchLoaded)

**What changes:** A background hosted service is added to `PcsRemote.Web`. It holds a `PeriodicTimer` configured from `Scoreboard:CaptureIntervalSeconds` (default: 2). The service subscribes to the `IPcsProAutomationService` state-change event on startup, then immediately reads the current state — if already `MatchLoaded` at startup, the polling loop starts without waiting for a transition event. The polling-loop start is guarded to be idempotent — a second concurrent start attempt (e.g., from a state-change event racing the startup read) is a no-op. When the state transitions to `MatchLoaded`, the service starts its polling loop, calling `IScoreboardService.CaptureAndBroadcastAsync` on each tick. When the state transitions away from `MatchLoaded` (to any other state), the timer is cancelled and the loop exits cleanly. A capture exception is caught and logged at Error level via Serilog; it does not terminate the loop — the service continues polling on subsequent ticks. The service is registered in `Program.cs` via `AddHostedService`.

**Why:** Satisfies S-SC-1 (image in browser within 3s), S-SC-3 (polling lifecycle tied to `MatchLoaded`), and S-SC-9 (configurable interval). Separating the polling timer from the service keeps the service logic simple and testable. The hosted service boundary also ensures proper startup/shutdown integration with the ASP.NET Core host lifecycle.

**Dependencies:** S-001 (`IScoreboardService.CaptureAndBroadcastAsync` must exist). `IPcsProAutomationService` state-change event already exists from IS-003.

**Verification intent:** Unit tests covering: polling starts when state transitions to `MatchLoaded`; polling starts immediately if state is already `MatchLoaded` at service startup (cold-start — S-SC-3); polling stops when state transitions away; a capture exception does not stop the loop (subsequent ticks continue). Configuration test for `Scoreboard:CaptureIntervalSeconds` binding (S-SC-9). Build and all existing tests pass.

---

### S-003 — ScoreboardPreview Blazor component (live image rendering and placeholder)

**What changes:** A `ScoreboardPreview` Razor component is added to `PcsRemote.Web`. On `OnInitializedAsync`, the component:
- Reads the current cached image from `IScoreboardService` (late-joiner support — displays the current image immediately without waiting for the next poll).
- Subscribes to `IScoreboardService.ScoreboardUpdated` and calls `InvokeAsync(StateHasChanged)` on each event to update the rendered image.
- Subscribes to `IPcsProAutomationService.StateChanged` (or a state provider) to know when state leaves `MatchLoaded`.

When state = `MatchLoaded` and a cached image exists, the component renders the image as a Base64-encoded `<img>` element. When state ≠ `MatchLoaded`, or no image is cached yet, the component renders a "No scoreboard data" placeholder. The component unsubscribes from all events on `Dispose`. The component is embedded in the main scoreboard page/section visible to operators.

**Why:** Satisfies S-SC-8 (all connected browsers receive updates via Blazor circuit subscription), S-SC-10 (placeholder when state ≠ `MatchLoaded`), and the late-joiner requirement from HLPS-004 §2. This is the primary operator-facing deliverable of IS-004.

**Dependencies:** S-001 (`IScoreboardService` and `ScoreboardUpdated` event exist); S-002 (polling service populates the cache so the component has something to display during integration tests).

**Verification intent:** bUnit tests covering: component renders placeholder when state ≠ `MatchLoaded` (for `NotRunning`, `MatchSelection`, `Error` states — S-SC-10); component renders image when state = `MatchLoaded` and image is cached; component updates when `ScoreboardUpdated` fires; component reads cached image on init (late-joiner). Build and all existing tests pass.

---

### S-004 — Refresh scoreboard button (forced capture, ScoreboardUpdated + RefreshCompleted flow)

**What changes:** A "Refresh Scoreboard" button is added to the scoreboard section. The button is enabled only when state = `MatchLoaded` and disabled in all other states. On click, the button calls `IScoreboardService.ForceRefreshAsync`. While the refresh is in progress, the button is disabled to prevent concurrent requests. A brief transient notification (e.g., a Radzen notification toast) is displayed when `RefreshCompleted` fires, informing the operator that the refresh succeeded. The `ScoreboardPreview` component (from S-003) automatically reflects the new image via the `ScoreboardUpdated` event — no additional wiring is needed in this step.

Exceptions thrown by `ForceRefreshAsync` are caught by the service (S-001) and logged; the button catches any propagated exception, logs it at Error level, and surfaces a generic "Refresh failed" transient notification to the UI (interim error propagation per HLPS-004 §2).

**Why:** Satisfies S-SC-4 (button enabled/disabled states), S-SC-5 (forced refresh fires both events in correct order), and the PRD §11 remote refresh capability. The two-event design ensures both the preview update and the operator acknowledgement notification are delivered without polling.

**Dependencies:** S-001 (`ForceRefreshAsync` and `RefreshCompleted` event exist); S-003 (image preview component must be in place so the forced-refresh result is visible in the UI during integration testing).

**Verification intent:** bUnit tests: button enabled only in `MatchLoaded`; button disabled in `Launching`, `MatchSelectionSearching`, and all other non-`MatchLoaded` states (S-SC-4, S-SC-12 partial); button click calls `ForceRefreshAsync`; `RefreshCompleted` causes notification to appear; a simulated `ForceRefreshAsync` exception causes the generic "Refresh failed" notification to appear. Integration test (S-SC-5): mock returns identical bytes on forced refresh; verify `ScoreboardUpdated` fires (S-SC-11), then `RefreshCompleted` fires, and the image in the preview component is updated. Build and all existing tests pass.

---

### S-005 — Change match flow (confirmation modal, state cleanup, MatchSelection transition)

**What changes:** A "Change Match" button is added to the scoreboard section. The button is enabled only when state = `MatchLoaded`; disabled in all other states (including `Launching` and `MatchSelectionSearching` — S-SC-12). On click, a Radzen confirmation modal appears with the message "Load a different match? PCS Pro will close and reopen…" (per PRD §12). The modal has "Confirm" and "Cancel" actions. On Cancel, the modal closes with no state change. On Confirm:
1. `IScoreboardService.ClearCache` is called — clears the stored hash and cached image.
2. The polling service stops (via the state-change event fired in the next sub-step).
3. The state machine is fired with the `ChangeMatch` trigger, transitioning to `MatchSelection`.

Step 2 and 3 are ordered by the state machine transition — when state leaves `MatchLoaded`, the polling service's existing subscription (S-002) automatically stops the timer. No additional wiring is needed.

**Why:** Satisfies S-SC-6 (modal appears on click), S-SC-7 (transition to `MatchSelection` on confirm), S-SC-12 (button disabled outside `MatchLoaded`), and the PRD §12 change match capability. The explicit `ClearCache` call before firing the trigger ensures stale image data cannot reappear if the operator cycles back to `MatchLoaded`.

**Dependencies:** S-002 (polling service auto-stops on state transition); S-003 (preview component must clear/show placeholder when state transitions away from `MatchLoaded`); `ChangeMatch` trigger delivered in IS-003 (state machine step).

**Verification intent:** bUnit tests: modal appears on button click (S-SC-6); modal closed on Cancel with no state change; Confirm calls `ClearCache` and fires `ChangeMatch` trigger. Integration test: confirming change match transitions to `MatchSelection` state and preview component renders placeholder (S-SC-7). Button disabled in `Launching` and `MatchSelectionSearching` (S-SC-12). Build and all existing tests pass.

---

### S-006 — Playwright E2E: multi-browser scoreboard update and refresh flow

**What changes:** New Playwright E2E tests are added to `PcsRemote.E2E.Tests`. The tests cover:
- Scoreboard image appears in the browser when state = `MatchLoaded` and the mock has generated an image (S-SC-1 timing intent verified by waiting for image element, not stopwatch).
- Refresh button is visible and clickable; clicking it produces the "Refresh Scoreboard" notification toast.
- Multi-browser context test: two independent `IBrowserContext` instances connect to the same server; the mock service generates a new image; both contexts receive the updated image rendered in their `ScoreboardPreview` components simultaneously (S-SC-8).
- Change match button click shows the confirmation modal; cancelling leaves the scoreboard visible.

The existing `PcsProWebApplicationFactory` two-host pattern (from IS-003 S-009) is reused. No new infrastructure is needed.

**Why:** Satisfies S-SC-8 (multi-browser simultaneous update — the only success criterion that cannot be verified by unit or bUnit tests) and provides end-to-end regression coverage for the complete scoreboard flow. This step is deliberately last, as all scoreboard components must exist before meaningful E2E tests can be written.

**Dependencies:** S-003, S-004, S-005 (all scoreboard UI components must be in place); existing E2E infrastructure from IS-003 S-009.

**Verification intent:** All new Playwright tests pass in headless mode. Multi-browser test verifies both contexts render the updated image before a 3-second timeout (aligning with S-SC-1 ≤3s SLA). Build and all 148+ existing tests pass.

---

## Success Criteria Coverage Matrix

| HLPS S-SC | Step(s) |
|---|---|
| S-SC-1 (image within 3s) | S-002, S-006 |
| S-SC-2 (delta detection suppresses duplicate) | S-001 |
| S-SC-3 (polling lifecycle tied to MatchLoaded) | S-002 |
| S-SC-4 (refresh button enabled/disabled) | S-004 |
| S-SC-5 (forced refresh fires both events) | S-001, S-004 | Note: S-SC-11 is a strict subset of S-SC-5; a single `ForceRefreshAsync` test asserting ScoreboardUpdated fires on identical bytes then RefreshCompleted follows satisfies both |
| S-SC-6 (change match modal appears) | S-005 |
| S-SC-7 (confirm → MatchSelection) | S-005 |
| S-SC-8 (multi-browser simultaneous update) | S-003, S-006 |
| S-SC-9 (configurable interval) | S-002 |
| S-SC-10 (placeholder when not MatchLoaded) | S-003 |
| S-SC-11 (forced refresh on identical bytes — subset of S-SC-5) | S-001, S-004 |
| S-SC-12 (change match button disabled outside MatchLoaded) | S-005 |

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | GPT APPROVED; Sonnet NEEDS REVIEW — 1 HIGH (S-006 5s window contradicts S-SC-1 3s SLA), 1 MEDIUM (S-002 cold-start gap), 2 LOW; fixes applied in v0.2 |
| R2 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | **UNANIMOUS APPROVED** — all 5 R1 fixes verified; Sonnet 1 LOW non-blocking (S-002 polling-start idempotency race on subscribe-then-read); fix applied in v0.3 |

### R1 Findings Applied

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| S-006 ≤5s assertion window contradicts S-SC-1 3s SLA | Sonnet | HIGH | Accept | S-006 verification intent changed to ≤3s to align with S-SC-1 |
| S-002 cold-start gap: no polling if state already MatchLoaded at startup | Sonnet | MEDIUM | Accept | S-002 What changes and verification intent updated with startup read-current-state check |
| S-005 ChangeMatch trigger not cited to IS-003 | Sonnet | LOW | Accept | Added "delivered in IS-003 (state machine step)" to S-005 dependency |
| S-SC-5/S-SC-11 near-duplication not acknowledged | Sonnet + GPT | LOW | Accept | Coverage matrix note added: S-SC-11 is a subset of S-SC-5 |
| S-004 error notification not in verification intent | GPT | LOW | Accept | Added explicit "error notification test" clause to S-004 verification intent |
| Out-of-scope mock note per step | GPT | LOW | Reject | Already documented in Accepted Risks table; per-step notes would dilute abstraction |
