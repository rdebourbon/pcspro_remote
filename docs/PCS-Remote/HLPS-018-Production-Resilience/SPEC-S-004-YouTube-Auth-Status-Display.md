# SPEC-S-004 — YouTube Auth Status Display in Streaming Controls

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **Author**  | Agent |
| **Created** | 2026-05-03 |
| **Governs** | IS-018 S-004 |
| **Depends** | S-003 (DONE) |

---

## Objective

Display YouTube authentication and availability status in the streaming controls section so operators know why streaming is unavailable and what action to take, with live updates when auth is restored.

---

## Requirements

### R1 — Subscribe to AuthStatusChanged and hydrate on init

`StreamingControls.razor` must subscribe to `IYouTubeLiveStreamService.AuthStatusChanged` on initialisation and unsubscribe on dispose. On initialisation, read the current `Availability` value from the service and initialise local state accordingly, so that pre-existing auth failures are reflected immediately when the component mounts. On receiving the event, update local state and re-render.

### R2 — Display availability status messages

When `StreamService.Availability` is not `Ready`, display a status banner above the streaming action buttons:

- `AuthFailed`: "⚠ YouTube authentication failed. Re-authorise via the tray icon → YouTube Setup."
- `ConfigError`: "⚠ YouTube configuration error. Check LiveStreamId and credentials."
- `TransientError`: "⚠ YouTube API temporarily unavailable. Streaming may recover on next attempt."
- `NotConfigured`: "⚠ YouTube not configured. Run setup to authorise."

When `Availability` is `Ready`, no banner is shown.

### R3 — Disable streaming buttons when unavailable

When `Availability` is not `Ready`, the Start Stream button must be disabled regardless of other conditions. Normal enablement rules (e.g. stream status, operation-in-progress, manual mode) continue to apply — auth recovery removes only the availability-based block. The Stop Stream button is unaffected (it should work if a stream was somehow started before token expiry).

### R4 — Live update on re-auth

When `AuthStatusChanged` fires with `Ready` (after tray re-auth), the banner disappears and streaming controls become functional without page refresh.

---

## Acceptance Criteria

| AC | Description |
|----|-------------|
| AC-1 | StreamingControls subscribes to AuthStatusChanged and unsubscribes on dispose |
| AC-2 | Auth-failed banner shows with tray icon instructions when Availability is AuthFailed |
| AC-3 | Config-error banner shows when Availability is ConfigError |
| AC-4 | Transient-error banner shows when Availability is TransientError |
| AC-5 | NotConfigured banner shows when Availability is NotConfigured |
| AC-6 | No banner when Availability is Ready |
| AC-7 | Start Stream button disabled when Availability is not Ready |
| AC-8 | Banner disappears on AuthStatusChanged with Ready (live update) |
| AC-9 | Build succeeds with 0 errors, 0 warnings |
| AC-10 | All existing StreamingControls tests pass |

---

## Test Cases

| TC | Test | Verifies |
|----|------|----------|
| TC-1 | Service Availability=AuthFailed → banner with tray instructions visible, Start disabled | AC-2, AC-7 |
| TC-2 | Service Availability=ConfigError → config error banner visible, Start disabled | AC-3, AC-7 |
| TC-3 | Service Availability=TransientError → transient error banner visible, Start disabled | AC-4, AC-7 |
| TC-4 | Service Availability=NotConfigured → not configured banner visible, Start disabled | AC-5, AC-7 |
| TC-5 | Service Availability=Ready → no banner, Start enabled (when Idle, no op in progress) | AC-6 |
| TC-6 | Given Availability=AuthFailed (banner showing), stream=Idle, no operation in progress. When AuthStatusChanged fires with Ready → banner removed, Start enabled | AC-8 |
| TC-7 | AuthStatusChanged fires AuthFailed → banner appears, Start disabled | AC-1 |
| TC-8 | Dispose unsubscribes from AuthStatusChanged | AC-1 |
| TC-9 | Availability not Ready but stream is Live → Stop button remains enabled | AC-7 |

---

## Review History

| Round | Panel | Outcome | Notes |
|-------|-------|---------|-------|
| R1 | GPT 5.4, Sonnet 4.6 | REVISE | GPT: 3 findings (2M, 1L). Sonnet: 3 findings (2 Minor, 1 Minor). Accepted 4: init-time hydration in R1 (Sonnet-F-002), clarify normal enablement rules in R3 (GPT-F-001), add Stop button unaffected TC-9 (GPT-F-002), TC-6 precondition (Sonnet-F-003). Downgraded 1: live-update into failure states (GPT-F-003, TC-7 already covers). |
| R2 | GPT 5.4, Sonnet 4.6 | APPROVED | GPT: 1 finding (M) — TC-6 re-enable wording, converged with Sonnet. Sonnet: 2 findings (1M, 1L) — TC-6 preconditions, R3 exhaustive list. Fixed: TC-6 preconditions added (stream=Idle, no op), R3 "e.g." added. |
