# HLPS-009: PCS Pro Streaming Automation

| Field | Value |
|---|---|
| **Document** | HLPS-009-PCSPro-Streaming-Automation.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-17 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Dependencies** | HLPS-006 (FlaUI integration patterns), HLPS-008 (YouTube broadcast lifecycle) |

---

## 1. Problem Statement

HLPS-008 delivered YouTube broadcast lifecycle management (create broadcast, bind to stream key, transition to live/complete) but assumed OBS was the sole RTMP video source. In reality, **PCS Pro is the sole RTMP source** — it has built-in streaming capability configured with the club's YouTube stream key.

PCS Pro exposes a **"Start Live Stream" button** inside its **"Video Display" tool window** (automation ID: `twdVideoCapture`). The tool window and its "Live Streaming Controls" panel must first be made visible via: `View → Video → Video Display` and `View → Video → Live Streaming Controls` (check the menu option if not already checked). The "Start Live Stream" button does not have an automation ID; it is identified by its text content.

Currently, this button must be clicked manually by the operator at the correct point in the broadcast lifecycle. This creates an operational gap: PCS Remote automates the YouTube broadcast but the operator must still manually interact with PCS Pro to start/stop the video stream. This defeats the goal of single-button streaming.

This HLPS adds FlaUI automation hooks to click PCS Pro's streaming menu items at the correct points in the YouTube broadcast lifecycle, and integrates them into the existing `IYouTubeLiveStreamService` start/stop flows. The mock automation service is extended to support these new operations for development and testing.

### Architectural Correction

HLPS-008 §5.7 step 5 describes polling `liveStream.status.streamStatus` for `"active"` as an "OBS health check." This is architecturally correct but the naming is wrong — the RTMP source is PCS Pro, not OBS. This HLPS corrects this assumption without altering the polling mechanism (the YouTube API is source-agnostic — it reports stream health regardless of what software pushes the RTMP data).

---

## 2. Scope

### In Scope

- **New methods on `IPcsProAutomationService`**: `StartStreamingAsync(CancellationToken)` and `StopStreamingAsync(CancellationToken)` — UI automation commands to click PCS Pro's streaming buttons
- **FlaUI implementation** (`PcsRemote.Automation`): new `IStreamingAutomation` sub-automation interface and `FlaUiStreamingAutomation` class implementing button click automation in the "Video Display" tool window (consistent with existing sub-automation pattern: `ILoginAutomation`, `IMatchSelectionAutomation`, etc.). Must first ensure the tool window and live streaming controls are visible via `View → Video → Video Display` and `View → Video → Live Streaming Controls`.
- **Mock implementation** (`PcsRemote.Automation.Mock`): `StartStreamingAsync` and `StopStreamingAsync` with configurable delays (consistent with existing mock patterns)
- **Integration into `YouTubeLiveStreamService.StartStreamAsync`**: after creating and binding the broadcast (step 6 in HLPS-008 §5.7), call `IPcsProAutomationService.StartStreamingAsync()` before polling stream readiness
- **Integration into `YouTubeLiveStreamService.StopStreamAsync`**: call `IPcsProAutomationService.StopStreamingAsync()` before transitioning the YouTube broadcast to "complete"
- **Integration into `MockYouTubeLiveStreamService`**: same sequencing as real implementation — call `StartStreamingAsync`/`StopStreamingAsync` on the injected `IPcsProAutomationService` at the correct lifecycle points
- **Unit tests**: verify new automation methods, integration with YouTube service start/stop flows
- **HLPS-008 addendum**: document the OBS → PCS Pro correction for traceability

### Out of Scope

- Changing the YouTube broadcast API lifecycle (create, bind, transition, complete) — that is already implemented and tested in HLPS-008
- OBS integration or control (OBS is not used)
- PCS Pro streaming configuration (stream key, RTMP endpoint) — this is pre-configured in PCS Pro
- Modifying the `StreamingControls.razor` UI component — the start/stop buttons already call `StartStreamAsync`/`StopStreamAsync`; the PCS Pro automation is transparent to the UI
- State machine changes — `PcsProState` enum is not modified; streaming menu clicks do not change PCS Pro's lifecycle state (the match remains loaded throughout)

---

## 3. Constraints

| ID | Constraint | Rationale |
|---|---|---|
| C-1 | `PcsRemote.Core` must remain dependency-free | Architecture rule: Core has zero external dependencies |
| C-2 | PCS Pro "Start Streaming" must be clicked **after** the YouTube broadcast is created and bound to the stream, but **before** polling for stream readiness | User-confirmed sequencing — YouTube needs to receive RTMP data to report the stream as "active" |
| C-3 | PCS Pro "Stop Streaming" must be clicked **before** the YouTube broadcast is transitioned to "complete" | User-confirmed sequencing — stop the video source first, then close the broadcast |
| C-4 | The new automation methods must follow the existing sub-automation pattern in `PcsRemote.Automation` | Architectural consistency: `IStreamingAutomation` + `FlaUiStreamingAutomation`, injected into `PcsProAutomationService` |
| C-5 | Button automation must be resilient to the "Video Display" tool window not being visible, the button not being found, **or the button being disabled (greyed out)** | Ensure tool window and live streaming controls are visible before attempting to click; fail with `InvalidOperationException` and descriptive message if the button is not found or not enabled |
| C-6 | `StartStreamingAsync`/`StopStreamingAsync` must be idempotent-safe in **both mock and FlaUI implementations** | Cleanup paths (§5.3, §5.4, §5.5) call `StopStreamingAsync` best-effort without knowing whether PCS Pro streaming was actually initiated. Idempotency means: if PCS Pro is already streaming, `StartStreamingAsync` is a logged no-op; if not streaming, `StopStreamingAsync` is a logged no-op. **Note:** This is distinct from C-5 — C-5 handles the control being absent or disabled (automation failure → throw); C-6 handles the control being present and enabled but the streaming state already matching the requested action (logical no-op → log and return) |
| C-7 | `StartStreamingAsync` and `StopStreamingAsync` require `PcsProState.MatchLoaded`; calling in any other state throws `InvalidOperationException` | Consistent with all existing `IPcsProAutomationService` methods having state preconditions |
| C-8 | PCS Pro's configured RTMP streaming endpoint must match the YouTube `liveStream` resource bound by the service (`YouTube:LiveStreamId`) | Configuration parity is a deployment prerequisite — if they differ, the stream readiness poll will never succeed even though all automation steps complete correctly |

---

## 3a. Assumptions

| ID | Assumption | Risk if wrong |
|---|---|---|
| A-1 | PCS Pro's pre-configured RTMP stream key targets the same YouTube `liveStream` resource used by `YouTube:LiveStreamId`. If they differ, PCS Pro will push video to the wrong ingest point and the stream readiness poll will never succeed. | Stream readiness poll times out → `YouTubeLiveStreamService` transitions to `Error`. Remediation: verify keys match during deployment setup (C-8). |
| A-2 | PCS Pro's UI labels ("Start Live Stream", "Video Display", "Live Streaming Controls") are stable across the version deployed on the garage PC and do not vary by Windows display language. | Button text mismatch → `InvalidOperationException` from FlaUI automation. Remediation: text constants extracted to named code constants for easy update (deferred to IS). |

---

## 4. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| S-SA-1 | `IPcsProAutomationService` exposes `StartStreamingAsync` and `StopStreamingAsync` methods | Compile-time verification; interface change |
| S-SA-2 | `FlaUiStreamingAutomation` ensures the Video Display tool window and Live Streaming Controls are visible, then clicks PCS Pro's "Start Live Stream" button when `StartStreamingAsync` is called | FlaUI automation test (manual verification on garage PC). **Gated on resolution of SA-U-2 and SA-U-3** |
| S-SA-3 | `FlaUiStreamingAutomation` clicks PCS Pro's stop streaming button when `StopStreamingAsync` is called | FlaUI automation test (manual verification on garage PC). **Gated on resolution of SA-U-2 and SA-U-3** |
| S-SA-4 | `MockPcsProAutomationService.StartStreamingAsync` simulates a configurable delay and tracks streaming state | Unit test |
| S-SA-5 | `MockPcsProAutomationService.StopStreamingAsync` simulates a configurable delay and resets streaming state | Unit test |
| S-SA-6 | `YouTubeLiveStreamService.StartStreamAsync` calls `StartStreamingAsync` after broadcast bind and before stream readiness polling | Unit test with mocked automation service — verify call order |
| S-SA-7 | `YouTubeLiveStreamService.StopStreamAsync` calls `StopStreamingAsync` before transitioning broadcast to complete | Unit test with mocked automation service — verify call order |
| S-SA-8 | `MockYouTubeLiveStreamService` calls `StartStreamingAsync`/`StopStreamingAsync` at the same lifecycle points as the real implementation | Unit test — verify `MockYouTubeLiveStreamService` calls `StartStreamingAsync` after setting status to `Starting` and before transitioning to `Live`, and calls `StopStreamingAsync` before transitioning to `Idle` |
| S-SA-9 | If `StartStreamingAsync` fails (e.g., button not found), `YouTubeLiveStreamService` transitions to `Error` state with descriptive message | Unit test |
| S-SA-10 | If `StopStreamingAsync` fails, `YouTubeLiveStreamService` still transitions to `Idle` (best-effort stop — broadcast is still completed on YouTube) | Unit test |
| S-SA-11 | Cancel during Starting (after PCS Pro streaming started but before going live) calls `StopStreamingAsync` as part of cleanup | Unit test |
| S-SA-12 | Existing E2E and unit tests continue to pass (no regressions) | CI verification |
| S-SA-13 | If an exception occurs after `StartStreamingAsync` succeeds (step 5+), cleanup calls `StopStreamingAsync` best-effort before transitioning to `Error` | Unit test — verify `StopStreamingAsync` called during error cleanup |
| S-SA-14 | All OBS references in `YouTubeLiveStreamService` error messages and log templates are corrected to reference PCS Pro (per §7 items 1-5) | Unit test assertion on error message content |
| S-SA-15 | Calling `StartStreamingAsync` or `StopStreamingAsync` when `CurrentState != MatchLoaded` throws `InvalidOperationException` | Unit test |

---

## 5. Revised Start/Stop Lifecycle

### 5.1 Start Stream (revised from HLPS-008 §5.7)

```
0. Validate: LoadedMatch not null; YouTube token available
1. Set CurrentStatus = Starting; fire StatusChanged
2. Render broadcast title from template + LoadedMatch
3. Insert liveBroadcast on YouTube (status=created)
4. Bind broadcast to liveStream (YouTube:LiveStreamId)
5. ★ NEW: Call IPcsProAutomationService.StartStreamingAsync(ct)
   → PCS Pro begins pushing RTMP data to the YouTube endpoint
6. Poll liveStream.status.streamStatus at 3s intervals:
   a. "active" → proceed to step 7
   b. Timeout → throw YouTubeStreamException
   c. ct cancelled → cleanup (see §5.3)
7. liveBroadcasts.transition(broadcastStatus: "live")
8. Set CurrentStatus = Live; fire StatusChanged
```

### 5.2 Stop Stream (revised from HLPS-008)

```
1. Set CurrentStatus = Stopping; fire StatusChanged
2. ★ NEW: Call IPcsProAutomationService.StopStreamingAsync(ct)
   → PCS Pro stops pushing RTMP data
3. liveBroadcasts.transition(broadcastStatus: "complete")
4. Set CurrentStatus = Idle; fire StatusChanged
```

### 5.3 Cancel During Starting (with PCS Pro cleanup)

If cancellation occurs after step 5 (PCS Pro streaming started):
```
1. Call IPcsProAutomationService.StopStreamingAsync() (best-effort)
2. Delete partially-created broadcast (best-effort)
3. Set CurrentStatus = Idle (never Error — S-YT-11)
```

If cancellation occurs before step 5 (PCS Pro not yet streaming):
```
1. Delete partially-created broadcast (best-effort)
2. Set CurrentStatus = Idle
```

### 5.4 Error During Starting (with PCS Pro cleanup)

If an exception occurs after step 5 (PCS Pro streaming started):
```
1. Call IPcsProAutomationService.StopStreamingAsync(ct) (best-effort)
2. Delete partially-created broadcast (best-effort)
3. Set CurrentStatus = Error with message
```

### 5.5 Error During Stopping

If `StopStreamingAsync` fails in §5.2 step 2:
```
1. Log warning (best-effort — broadcast still needs completing)
2. Continue from step 3 (transition broadcast to "complete")
3. Set CurrentStatus = Idle; fire StatusChanged
```

The YouTube broadcast is completed regardless of whether PCS Pro streaming was successfully stopped. This prevents a stuck `Stopping` state.

---

## 6. Unknowns Register

| ID | Description | Owner | Blocking? | Resolution | Status |
|---|---|---|---|---|---|
| SA-U-1 | ~~What is the exact menu path for streaming controls?~~ Resolved: button "Start Live Stream" in "Video Display" tool window (`twdVideoCapture`). Tool window opened via `View → Video → Video Display`; streaming controls panel via `View → Video → Live Streaming Controls`. Button has no automation ID — identified by text content. | User | No | Resolved | ✅ Resolved |
| SA-U-2 | ~~Does the "Start Live Stream" button show a confirmation dialog before starting?~~ Resolved: YES — two dialogs appear. (1) A mandatory "Video Consent" dialog requiring a "Video Consented" button click. (2) An optional "Add Live Stream to Match Centre?" dialog (click "No"). Both handled by `HandleConsentDialogs()` in `FlaUiStreamingAutomation`. Discovered via diagnostic tool Step 11. | User | No | Resolved — implemented in IS-010 S-009 | ✅ Resolved |
| SA-U-3 | ~~Is "Stop" a toggle of the same button or a separate button?~~ Resolved: TOGGLE — the same physical button's child TextBlock label changes from "Start Live Stream" to "Stop Live" when streaming is active. FlaUI locates both via `FindButtonByChildText` with different search text. Discovered via diagnostic tool Step 11. | User | No | Resolved — implemented in IS-010 S-009 | ✅ Resolved |
| SA-U-4 | Does PCS Pro provide visual feedback (status bar, icon change) when streaming is active? | User | No | Useful for verification but not blocking — the YouTube API stream status poll serves as the authoritative readiness check. | Deferred |
| SA-U-5 | Can "Start Live Stream" be clicked when no match is loaded in PCS Pro? | User | No | PCS Remote already enforces MatchLoaded state before streaming controls are visible, so this is an edge case. Will add a guard in the FlaUI implementation regardless. | Deferred |
| SA-U-6 | Does FlaUI menu automation work reliably when PCS Pro is minimised or not the foreground window? | Developer | No | Existing sub-automations already handle window activation (FlaUI pattern). This is a delivery-time concern, not an HLPS-level blocker. | Deferred |
| SA-U-7 | Are PCS Pro UI labels ("Start Live Stream", "Video Display") stable across deployed versions and language settings? | Developer | No | Button is identified by text content (no automation ID). If labels change across PCS Pro versions, the automation will need updating. Accepted as operational risk — PCS Pro version is controlled on the garage PC. | Deferred |

---

## 7. Impact on Existing Documents

### HLPS-008 Addendum

The following corrections apply to HLPS-008:

1. **§1 Problem Statement**: Replace "coordinate the go-live transition with OBS" with "coordinate the go-live transition with PCS Pro's built-in streaming"
2. **§2 In Scope**: Replace "OBS is already configured on the garage PC with the club's YouTube stream key and must be running and actively streaming" with "PCS Pro has built-in RTMP streaming configured with the club's YouTube stream key"
3. **§5.5**: Replace references to "OBS" with "PCS Pro" in the context of RTMP streaming
4. **§5.7 step 5**: The "OBS health check" is really a "PCS Pro RTMP health check" — polling the YouTube API for stream readiness after PCS Pro starts streaming
5. **S-YT-5**: Replace "OBS not streaming" error message with "PCS Pro is not streaming"

These corrections are documented here for traceability. The original HLPS-008 text is not modified (it is APPROVED and delivered).

---

## 8. Review History

| Round | Reviewer(s) | Verdict | Notes |
|---|---|---|---|
| R1 | Opus 4.6, GPT-5.4 | NEEDS REVIEW | Opus: 1 HIGH, 3 MEDIUM, 3 LOW. GPT: 3 HIGH, 2 MEDIUM. Combined: 11 unique findings. 10 accepted (fixes applied), 1 deferred to IS (cleanup method naming is IS detail). Key fixes: broadened C-6 idempotency to FlaUI, added C-7 MatchLoaded precondition, added C-8 stream-key alignment, gated S-SA-2/3 on unknowns, added S-SA-13/14/15, extended C-5 for disabled buttons, added SA-U-6/7. |
| R2 | Opus 4.6, GPT-5.4 | Opus: APPROVED, GPT: NEEDS REVIEW → all fixed | All 12 R1 fixes verified ✅. 6 new minor findings — all accepted and applied: added §5.5 error-during-stopping, clarified C-5/C-6 distinction, added ct to pseudocode, fixed scope wording (menu items→buttons), fixed A-2 recompilation wording, bumped version to 0.2. **APPROVED — user approved.** |
