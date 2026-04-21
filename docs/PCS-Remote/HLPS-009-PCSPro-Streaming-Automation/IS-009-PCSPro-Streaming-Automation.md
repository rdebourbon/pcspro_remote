# IS-009: PCS Pro Streaming Automation — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-009-PCSPro-Streaming-Automation.md |
| **Status** | COMPLETE |
| **Version** | 0.2 |
| **Date** | 2026-04-21 |
| **Governing HLPS** | HLPS-009-PCSPro-Streaming-Automation.md (APPROVED) |

---

## 1. Overview

This IS breaks HLPS-009 into an ordered sequence of atomic, independently valuable steps. Each step produces a buildable, testable increment.

All steps delivered. S-003 was originally gated on SA-U-2/SA-U-3; both were resolved via diagnostic tool Step 11 during IS-010 delivery. The FlaUI streaming automation was implemented as IS-010 S-009 (commit `d73a001`), which superseded this step.

---

## 2. Implementation Steps

### S-001: Core Interface + Mock Automation — ✅ DELIVERED (`2a1e81e`)

**What changes:** Add `StartStreamingAsync` and `StopStreamingAsync` to `IPcsProAutomationService`. Implement both methods in `MockPcsProAutomationService` with configurable delays, streaming state tracking, and idempotency. Add stub implementations in `PcsProAutomationService` (throw `NotImplementedException`) to maintain build integrity until S-003 delivers the real FlaUI automation.

**Why:** Satisfies S-SA-1 (interface), S-SA-4/S-SA-5 (mock implementation), S-SA-15 (state precondition), and establishes the contract that all downstream consumers depend on. C-7 (MatchLoaded precondition) and C-6 (idempotency) are enforced here.

**Dependencies:** None — this is the foundation step.

**Verification intent:**
- Interface compiles with new methods
- Real automation service compiles with stub implementations
- Mock tracks streaming state (start → streaming → stop → not streaming)
- Configurable delays work as expected
- Idempotent-safe: calling `StartStreamingAsync` when already streaming is a logged no-op; calling `StopStreamingAsync` when not streaming is a logged no-op
- `InvalidOperationException` thrown when `CurrentState != MatchLoaded`
- All existing tests continue to pass

---

### S-002: YouTube Service Integration + OBS Text Corrections — ✅ DELIVERED (`62f6cbd`)

**What changes:** Integrate `StartStreamingAsync` / `StopStreamingAsync` calls into both `YouTubeLiveStreamService` and `MockYouTubeLiveStreamService` at the correct lifecycle points per HLPS-009 §5. Also correct all OBS references to PCS Pro in error messages and log templates per HLPS-009 §7.

**Why:** Satisfies S-SA-6/S-SA-7 (real YouTube service call ordering), S-SA-8 (mock YouTube service call ordering), S-SA-9 (start failure → Error), S-SA-10 (stop failure → best-effort continue), S-SA-11 (cancel cleanup), S-SA-13 (error cleanup), S-SA-14 (OBS→PCS Pro text), and §5.5 (error during stopping).

**Dependencies:** S-001 (interface and mock must exist).

**Verification intent:**
- `YouTubeLiveStreamService.WaitForStreamAndGoLiveAsync` calls `StartStreamingAsync` after broadcast bind and before stream readiness polling (satisfying C-2 sequencing)
- `YouTubeLiveStreamService.StopStreamAsync` calls `StopStreamingAsync` before `TryCompleteBroadcastAsync`
- `CleanupCancelledStartAsync` calls `StopStreamingAsync` best-effort when PCS Pro streaming was started
- `CleanupFailedStartAsync` calls `StopStreamingAsync` best-effort when PCS Pro streaming was started
- If `StopStreamingAsync` fails during stop, broadcast still completes and state transitions to Idle
- `MockYouTubeLiveStreamService` calls `StartStreamingAsync` after `Starting` and before `Live`, and `StopStreamingAsync` before `Idle`
- All 5 OBS references replaced with PCS Pro equivalents
- All existing tests continue to pass; new tests verify call ordering and error paths

---

### S-003: FlaUI Streaming Automation — ✅ DELIVERED (IS-010 S-009, `d73a001`)

**Superseded by IS-010 S-009.** SA-U-2 and SA-U-3 were resolved via diagnostic tool Step 11 during IS-010 delivery:
- **SA-U-2:** YES — two consent dialogs appear (Video Consent + optional Match Centre). Handled by `HandleConsentDialogs()`.
- **SA-U-3:** TOGGLE — same button, child TextBlock changes from "Start Live Stream" to "Stop Live".

**What was delivered:** `IStreamingAutomation` interface and `FlaUiStreamingAutomation` class in `PcsRemote.Automation`, wired into `PcsProAutomationService`. Includes `ClickStartLiveStream()`, `HandleConsentDialogs()`, `ClickStopLiveStream()`, `IsStreamingActive()`, and unexpected dialog handling. All element identifiers extracted to `KnownElements` constants. Verified on garage PC.

**Satisfies:** S-SA-2, S-SA-3. All 15 HLPS-009 success criteria now covered.

---

## 3. Dependency Graph

```
S-001 (Core + Mock)
  └─→ S-002 (YouTube Integration + OBS Corrections)

S-001 (Core + Mock)
  └─→ S-003 (FlaUI) [SA-U-2, SA-U-3 resolved; delivered via IS-010 S-009]
```

All steps delivered. S-003 was delivered under IS-010 S-009 after unknowns were resolved via diagnostic testing.

---

## 4. HLPS Success Criteria Coverage

| Criterion | Step |
|---|---|
| S-SA-1 (interface) | S-001 |
| S-SA-2 (FlaUI start) | S-003 |
| S-SA-3 (FlaUI stop) | S-003 |
| S-SA-4 (mock start) | S-001 |
| S-SA-5 (mock stop) | S-001 |
| S-SA-6 (YouTube start ordering) | S-002 |
| S-SA-7 (YouTube stop ordering) | S-002 |
| S-SA-8 (mock YouTube ordering) | S-002 |
| S-SA-9 (start failure → Error) | S-002 |
| S-SA-10 (stop failure → Idle) | S-002 |
| S-SA-11 (cancel cleanup) | S-002 |
| S-SA-12 (no regressions) | All steps |
| S-SA-13 (error cleanup) | S-002 |
| S-SA-14 (OBS→PCS Pro text) | S-002 |
| S-SA-15 (state precondition) | S-001 |

All 15 criteria are covered. S-003 is the only gated step.

---

## 5. Delivery Notes

- **C-8 (stream-key alignment)** is a deployment prerequisite, not an implementation step. Stream key parity between PCS Pro and YouTube must be verified during garage PC setup. If the stream readiness poll times out during operation, C-8 mismatch should be the first diagnostic check.
- **S-003 requires manual verification** on the garage PC — it is the only step without automated test coverage. This is accepted because FlaUI automation against PCS Pro cannot be tested in CI.
- **IS-009 modifies IS-008 deliverables**: `YouTubeLiveStreamService`, `MockYouTubeLiveStreamService`, and their test files are all IS-008 outputs that S-002 will modify.
- **`IPcsProAutomationService` is already injected** into both `YouTubeLiveStreamService` and `MockYouTubeLiveStreamService` constructors — no new DI wiring is required for S-002.

---

## 6. Review History

| Round | Reviewer(s) | Verdict | Notes |
|---|---|---|---|
| R1 | Opus 4.6, GPT-5.4 | NEEDS REVIEW | 8 findings, 5 accepted, 3 rejected. Fixes: added real PcsProAutomationService stubs to S-001, clarified C-2 sequencing in S-002, added A-2 label constants to S-003, added §5 Delivery Notes. |
| R2 | Opus 4.6, GPT-5.4 | APPROVED (unanimous) | All 5 R1 fixes verified ✅. All 3 rejections justified ✅. No new issues. **APPROVED — pending user approval.** |
