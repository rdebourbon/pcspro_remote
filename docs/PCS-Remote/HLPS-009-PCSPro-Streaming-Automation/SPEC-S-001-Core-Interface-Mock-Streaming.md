# SPEC-S-001: Core Interface + Mock Streaming Automation

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-Core-Interface-Mock-Streaming.md |
| **Status** | APPROVED |
| **Version** | 0.1 |
| **Date** | 2026-04-17 |
| **IS Step** | S-001 |
| **Governing Docs** | HLPS-009 (APPROVED), IS-009 (APPROVED) |
| **Branch** | `feature/IS-009-S-001-core-mock-streaming` |

---

## 1. Objective

Add `StartStreamingAsync` and `StopStreamingAsync` to `IPcsProAutomationService`, implement them in the mock with configurable delays, streaming state tracking, and idempotency, and add `NotImplementedException` stubs to the real FlaUI automation service.

---

## 2. Requirements

### R-1: Interface Extension

Add two new methods to `IPcsProAutomationService`:
- `Task StartStreamingAsync(CancellationToken ct = default)` — initiates PCS Pro's built-in RTMP streaming
- `Task StopStreamingAsync(CancellationToken ct = default)` — stops PCS Pro's RTMP streaming

Both methods return `Task`, use `CancellationToken ct = default` consistent with all existing interface methods, and include XML doc comments matching the existing documentation style.

### R-2: Mock Implementation — State Tracking

`MockPcsProAutomationService` must track a boolean streaming state via a public `IsStreaming` property. `StartStreamingAsync` sets it to true; `StopStreamingAsync` sets it to false. This property is public on the concrete mock class for test assertions but is **not** added to `IPcsProAutomationService` (PCS Remote does not need to query PCS Pro's streaming state; the YouTube API stream readiness poll serves as the authoritative check). If cancellation occurs during the configured delay, `OperationCanceledException` must propagate and the streaming state must remain unchanged.

### R-3: Mock Implementation — Configurable Delays

`MockPcsProOptions` gains two new delay properties for streaming operations. The mock methods apply these delays before changing state, consistent with the existing delay pattern used by all other mock operations.

### R-4: Mock Implementation — State Precondition (C-7)

Both `StartStreamingAsync` and `StopStreamingAsync` must throw `InvalidOperationException` if `CurrentState != MatchLoaded`. The exception message must include the current state, consistent with the pattern used by `ChangeMatchAsync` and other methods.

### R-5: Mock Implementation — Idempotency (C-6)

- Calling `StartStreamingAsync` when already streaming is a logged no-op — returns immediately without incurring the configured delay
- Calling `StopStreamingAsync` when not streaming is a logged no-op — returns immediately without incurring the configured delay
- Log messages use structured templates (no string interpolation)

### R-6: Real Automation Service — Stubs

`PcsProAutomationService` must compile with both new interface methods. Both stub implementations throw `NotImplementedException` with a message indicating they will be implemented in S-003. This maintains build integrity while the FlaUI implementation is gated.

### R-7: Semaphore Consistency

The mock's `StartStreamingAsync` and `StopStreamingAsync` must acquire the existing `_semaphore` at entry and release in `finally`, consistent with all other mock lifecycle operations. This prevents concurrent lifecycle operations.

---

## 3. Test Cases

All tests follow the `MethodName_Scenario_ExpectedResult` naming convention and use FluentAssertions.

### StartStreamingAsync Tests

| Test | Description |
|---|---|
| StartStreamingAsync_WhenMatchLoaded_SetsStreamingState | From MatchLoaded, calling start should succeed and the mock should track streaming as active |
| StartStreamingAsync_WhenNotMatchLoaded_ThrowsInvalidOperationException | From any state other than MatchLoaded (e.g., NotRunning, MatchSelection), calling start should throw with descriptive message |
| StartStreamingAsync_WhenAlreadyStreaming_IsNoOp | Call start twice from MatchLoaded — second call should return without error |
| StartStreamingAsync_WithConfiguredDelay_RespectsDelay | Configure a non-zero delay and verify the method takes at least that long |

### StopStreamingAsync Tests

| Test | Description |
|---|---|
| StopStreamingAsync_WhenStreaming_ClearsStreamingState | After starting, calling stop should succeed and reset streaming state |
| StopStreamingAsync_WhenNotMatchLoaded_ThrowsInvalidOperationException | From any state other than MatchLoaded, calling stop should throw |
| StopStreamingAsync_WhenNotStreaming_IsNoOp | From MatchLoaded without prior start, stop should return without error |
| StopStreamingAsync_WithConfiguredDelay_RespectsDelay | Configure a non-zero delay and verify the method takes at least that long |

### Cancellation Tests

| Test | Description |
|---|---|
| StartStreamingAsync_CancelledDuringDelay_ThrowsAndStateUnchanged | Configure a delay, cancel the token before completion — `OperationCanceledException` must be thrown and `IsStreaming` must remain false |
| StopStreamingAsync_CancelledDuringDelay_ThrowsAndStateUnchanged | Start streaming, configure a delay for stop, cancel — `OperationCanceledException` must be thrown and `IsStreaming` must remain true |

### Integration Test

| Test | Description |
|---|---|
| StartThenStop_RoundTrip_StreamingStateTrackedCorrectly | Full lifecycle: launch → load match → start streaming → verify streaming → stop streaming → verify not streaming |

---

## 4. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `IPcsProAutomationService` has `StartStreamingAsync` and `StopStreamingAsync` |
| AC-2 | `MockPcsProAutomationService` implements both methods with delay, state tracking, idempotency, and precondition guard |
| AC-3 | `PcsProAutomationService` compiles with `NotImplementedException` stubs |
| AC-4 | All 11 new tests pass |
| AC-5 | All existing tests pass (255 pass, 19 pre-existing IndexTests failures baseline) |
| AC-6 | Solution builds with 0 errors, 0 warnings |

---

## 5. Out of Scope

- YouTube service integration (S-002)
- FlaUI streaming automation (S-003)
- E2E tests (existing tests are sufficient for this step)
- Exposing `IsStreaming` on the interface (internal mock state only)

---

## 6. Review History

| Round | Reviewer(s) | Verdict | Notes |
|---|---|---|---|
| R1 | Opus 4.6 / GPT-5.4 | APPROVED / NEEDS REVIEW | 1 HIGH (cancellation), 2 MEDIUM, 6 LOW — 4 fixed, 3 accepted, 2 dismissed |
