# SPEC-S-002: YouTube Service Integration + OBS Text Corrections

| Field | Value |
|---|---|
| **Document** | SPEC-S-002-YouTube-Integration-OBS-Corrections.md |
| **Status** | APPROVED |
| **Version** | 0.1 |
| **Date** | 2026-04-17 |
| **IS Step** | S-002 |
| **Governing Docs** | HLPS-009 (APPROVED), IS-009 (APPROVED) |
| **Branch** | `feature/IS-009-S-002-youtube-integration` |

---

## 1. Objective

Integrate `StartStreamingAsync` / `StopStreamingAsync` calls into both `YouTubeLiveStreamService` and `MockYouTubeLiveStreamService` at the correct lifecycle points. Correct all OBS references to PCS Pro per HLPS-009 §7.

---

## 2. Requirements

### R-1: Real YouTube Service — Start Sequencing (C-2)

In `YouTubeLiveStreamService.WaitForStreamAndGoLiveAsync`, call `_automationService.StartStreamingAsync(ct)` **before** `PollStreamReadyAsync`. This satisfies the HLPS-009 §5.1 lifecycle: Create → Bind → **Start PCS Pro streaming** → Poll readiness → Go live.

If `StartStreamingAsync` throws, the exception propagates to `StartStreamAsync`'s catch blocks which handle cleanup (the existing `CleanupFailedStartAsync` path satisfies S-SA-9).

### R-2: Real YouTube Service — Stop Sequencing (C-3)

In `YouTubeLiveStreamService.StopStreamAsync`, call `_automationService.StopStreamingAsync(ct)` **before** `TryCompleteBroadcastAsync`. Per HLPS-009 §5.2: Stop PCS Pro streaming → Complete broadcast.

If `StopStreamingAsync` throws, catch the exception, log a warning, and continue with broadcast completion (S-SA-10: best-effort). The stop path must never fail because PCS Pro streaming didn't stop cleanly.

### R-3: Real YouTube Service — Cancel Cleanup (S-SA-11)

In `CleanupCancelledStartAsync`, call `_automationService.StopStreamingAsync(CancellationToken.None)` wrapped in a try/catch — if it throws, log a warning and continue. The cleanup outcome (transition to Idle) must not be affected by a `StopStreamingAsync` failure. Use `CancellationToken.None` since cleanup must complete regardless of caller cancellation.

### R-4: Real YouTube Service — Error Cleanup (S-SA-13)

In `CleanupFailedStartAsync`, call `_automationService.StopStreamingAsync(CancellationToken.None)` wrapped in a try/catch — if it throws, log a warning and continue. The cleanup outcome (transition to Error with the original exception) must not be affected by a `StopStreamingAsync` failure.

### R-5: Real YouTube Service — Stop During Stop (§5.5)

If `StopStreamingAsync` fails during `StopStreamAsync`, the failure must be logged but must not prevent broadcast completion or the transition to Idle.

### R-6: Mock YouTube Service — Start Integration

In `MockYouTubeLiveStreamService.StartStreamAsync`, call `_automationService.StartStreamingAsync` after the status has transitioned to `Starting` and before transitioning to `Live`. Call placement: after the `Starting` delay completes, before the `Live` status set.

### R-7: Mock YouTube Service — Stop Integration

In `MockYouTubeLiveStreamService.StopStreamAsync`, call `_automationService.StopStreamingAsync(CancellationToken.None)` before the `Idle` status set. Place after the stop delay, inside the final gate acquisition.

### R-8: OBS Text Corrections (S-SA-14)

Replace all 5 OBS references in `YouTubeLiveStreamService.cs` with PCS Pro equivalents:
1. Line ~295: XML doc comment "OBS readiness" → "PCS Pro stream readiness"
2. Line ~481: Log message "verify OBS is configured" → "verify PCS Pro stream key is configured"
3. Line ~486: Exception message "Ensure OBS has connected" → "Ensure PCS Pro has connected"
4. Line ~637: Exception message "OBS is not streaming" → "PCS Pro is not streaming"
5. Line ~639: Guidance "Check that OBS is running and streaming" → "Check that PCS Pro is running and streaming"

Additionally, replace 3 OBS references in `YouTubeOptions.cs`:
6. Line ~15: XML doc "bound to the club's OBS stream key" → "bound to the club's PCS Pro stream key"
7. Line ~32: XML doc "waiting for OBS to report stream as active" → "waiting for PCS Pro to report stream as active"
8. Line ~37: XML doc "between OBS health poll requests" → "between PCS Pro stream health poll requests"

Line numbers are approximate — identify by text content.

---

## 3. Test Cases

### YouTubeLiveStreamService Tests (in PcsRemote.YouTube.Tests)

Existing tests use a mock `IPcsProAutomationService`. New tests verify call ordering.

| Test | Description |
|---|---|
| StartStreamAsync_CallsStartStreamingBeforePollReady | Verify `StartStreamingAsync` is called and completes before `PollStreamReadyAsync` begins |
| StopStreamAsync_CallsStopStreamingBeforeCompleteBroadcast | Verify `StopStreamingAsync` is called before `TryCompleteBroadcastAsync` |
| StopStreamAsync_WhenStopStreamingFails_StillCompletesAndTransitionsToIdle | Verify that if `StopStreamingAsync` throws, broadcast still completes and status reaches Idle |
| CleanupCancelledStart_CallsStopStreamingBestEffort | Verify `StopStreamingAsync` is called during cancellation cleanup |
| CleanupFailedStart_CallsStopStreamingBestEffort | Verify `StopStreamingAsync` is called during error cleanup |

### MockYouTubeLiveStreamService Tests (in PcsRemote.YouTube.Mock.Tests)

| Test | Description |
|---|---|
| StartStreamAsync_CallsStartStreamingOnAutomationService | Verify `StartStreamingAsync` is called during start lifecycle |
| StopStreamAsync_CallsStopStreamingOnAutomationService | Verify `StopStreamingAsync` is called during stop lifecycle |

---

## 4. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `StartStreamingAsync` called before `PollStreamReadyAsync` in real YouTube service |
| AC-2 | `StopStreamingAsync` called before `TryCompleteBroadcastAsync` in real YouTube service |
| AC-3 | `StopStreamingAsync` failure during stop does not prevent broadcast completion |
| AC-4 | `StopStreamingAsync` called best-effort in both cancel and error cleanup paths |
| AC-5 | Mock YouTube service calls both `StartStreamingAsync` and `StopStreamingAsync` |
| AC-6 | All 8 OBS references replaced with PCS Pro equivalents (5 in YouTubeLiveStreamService.cs, 3 in YouTubeOptions.cs) |
| AC-7 | All new tests pass |
| AC-8 | All existing tests pass (baseline: 266 pass, 19 pre-existing IndexTests failures) |
| AC-9 | Solution builds with 0 errors, 0 warnings |

---

## 5. Out of Scope

- FlaUI streaming automation (S-003)
- YouTube API credential setup
- Stream key configuration

---

## 6. Review History

| Round | Reviewer(s) | Verdict | Notes |
|---|---|---|---|
| R1 | Opus 4.6 / GPT-5.4 | APPROVED / NEEDS REVIEW | 2 MEDIUM (best-effort clarified, YouTubeOptions OBS refs added), 4 LOW accepted/dismissed |
