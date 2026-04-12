# SPEC-S-001: IScoreboardService — Interface, Singleton, Delta Detection, and Event Model

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-ScoreboardService.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-004-Scoreboard.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-004-Scoreboard.md v0.4 (APPROVED) |
| **Step** | S-001 |
| **Branch** | `feature/S-001-scoreboard-service` |

---

## 1. Objective

Introduce the `IScoreboardService` contract in `PcsRemote.Core` and a concrete singleton implementation in `PcsRemote.Web`. This service is the shared foundation for all subsequent IS-004 steps: it owns the image cache, SHA-256 delta detection, and the two service events (`ScoreboardUpdated`, `RefreshCompleted`) that drive the Blazor component subscription pattern (PROJECT-CONTEXT.md AD#6 v1.1).

---

## 2. Scope

### In Scope

- `IScoreboardService` interface in `PcsRemote.Core`
- `ScoreboardService` singleton implementation in `PcsRemote.Web`
- DI registration in `Program.cs`
- Unit tests in `PcsRemote.Web.Tests`

### Out of Scope

- Polling service (S-002)
- Blazor components (S-003 onwards)
- Real PrintWindow capture (HLPS-006)

---

## 3. Interface Contract (`IScoreboardService` in `PcsRemote.Core`)

The interface is defined in `PcsRemote.Core` so it can be referenced by any project without introducing a dependency on `PcsRemote.Web`.

### Events

**`ScoreboardUpdated`** — `EventHandler<byte[]>`
Fires when a new scoreboard image is available for broadcast. The event argument is the JPEG image bytes. Fires on:
- Normal poll path: when the captured image hash differs from the stored hash.
- Forced refresh path: unconditionally, regardless of image content.

**`RefreshCompleted`** — `EventHandler`
Fires after `ScoreboardUpdated` on the forced-refresh path only. Acts as an acknowledgement signal. The event argument carries no data beyond the notification itself (standard `EventArgs`).

### Properties

**`CurrentImage`** — `byte[]?` (read-only)
The most recently broadcast image bytes. `null` until the first successful capture. Used by late-joining `ScoreboardPreview` components to display the current image immediately on initialisation without waiting for the next poll or event.

### Methods

**`CaptureAndBroadcastAsync(CancellationToken ct = default)`** — called by the polling service on each timer tick.
- Passes `ct` through to `IPcsProAutomationService.CaptureScoreboardImageAsync(ct)`.
- Computes the SHA-256 hash of the result.
- Compares against the stored hash.
- If hashes differ (or no hash is stored yet): updates `CurrentImage`, stores the new hash, fires `ScoreboardUpdated`.
- If hashes match: does nothing (delta suppression — S-SC-2).
- If capture returns null or empty bytes: does not fire any event and does not update `CurrentImage` or the stored hash (see TC-10).
- Propagates exceptions to the caller — no internal swallowing. The polling service (S-002) is responsible for catching and logging capture exceptions.
- Returns `Task`.

**`ForceRefreshAsync(CancellationToken ct = default)`** — called by the refresh button component.
- Clears the stored hash (sets to `null`/empty) before capture.
- Passes `ct` through to `CaptureScoreboardImageAsync(ct)`.
- Regardless of image content (hash bypass guaranteed by clearing stored hash first): updates `CurrentImage`, stores the new hash, fires `ScoreboardUpdated`.
- Fires `RefreshCompleted` after `ScoreboardUpdated`.
- Propagates exceptions to the caller — no internal swallowing. The button component (S-004) is responsible for catching and surfacing the "Refresh failed" notification.
- Returns `Task`.

**`ClearCache`** — called by the change match flow.
- Synchronously resets `CurrentImage` to `null` and the stored hash to `null`/empty.
- Does NOT fire any events.
- Returns `void`.

---

## 4. Implementation Notes

### Thread Safety
The service is a singleton consumed by multiple circuits simultaneously. All writes to `CurrentImage` and the stored hash must be thread-safe. A `lock` object is acceptable; an `Interlocked` or `ReaderWriterLockSlim` is also acceptable. The choice is left to Delivery.

Concurrent calls to `ForceRefreshAsync` are not required to be serialised at the service level. The calling UI component (S-004) is responsible for preventing concurrent invocations via its in-progress disabled state. Implementations may optionally add a guard if defensive serialisation is preferred.

### Hash Algorithm
SHA-256 via `System.Security.Cryptography.SHA256`. The hash is stored as a `byte[]` and compared with `SequenceEqual`. The hash bytes do not need to be stored as a hex string.

### Event Payload Nullability
The `byte[]` argument of `ScoreboardUpdated` must never be null. The service must not fire `ScoreboardUpdated` if `CaptureScoreboardImageAsync` returns null or empty bytes (see TC-10).

### Event Invocation
Events are standard C# `event` fields (not async). Subscribers may be async-void (Blazor pattern). The service must not `await` event handler delegates — it fires and forgets, consistent with the existing `PcsProStateBroadcaster` pattern.

### `IPcsProAutomationService` Injection
The concrete implementation receives `IPcsProAutomationService` via constructor injection. It does not hold a reference to the state machine directly — state management is handled by the polling service and component layer.

### Serilog Logging
- Log `Debug` when `CaptureAndBroadcastAsync` suppresses a frame (identical hash). Include the hash as a structured property.
- Log `Debug` when `ScoreboardUpdated` fires. Include image size in bytes.
- Log `Debug` when `ForceRefreshAsync` completes. Include image size in bytes.
- No `Information` logs — this is a high-frequency operation (every 2s).

---

## 5. Test Cases

All tests live in `PcsRemote.Web.Tests`. The test class uses `Moq` for `IPcsProAutomationService` and `FluentAssertions` for assertions, consistent with existing test patterns.

### TC-1: `CaptureAndBroadcastAsync_FirstCapture_FiresScoreboardUpdated`
**Arrange:** Service with no prior state (no cached hash). Mock returns image bytes `A`.
**Act:** Call `CaptureAndBroadcastAsync`.
**Assert:** `ScoreboardUpdated` fires exactly once with argument = `A`. `CurrentImage` equals `A`.

### TC-2: `CaptureAndBroadcastAsync_IdenticalHash_DoesNotFireScoreboardUpdated`
**Arrange:** Service has stored hash of image `A`. Mock returns `A` again.
**Act:** Call `CaptureAndBroadcastAsync`.
**Assert:** `ScoreboardUpdated` does not fire. `CurrentImage` remains `A` (unchanged).
*Satisfies S-SC-2.*

### TC-3: `CaptureAndBroadcastAsync_DifferentHash_FiresScoreboardUpdated`
**Arrange:** Service has stored hash of image `A`. Mock returns image `B` (different bytes).
**Act:** Call `CaptureAndBroadcastAsync`.
**Assert:** `ScoreboardUpdated` fires exactly once with argument = `B`. `CurrentImage` equals `B`.

### TC-4: `ForceRefreshAsync_IdenticalBytes_StillFiresScoreboardUpdated`
**Arrange:** Service has stored hash of image `A`. Mock returns `A` again.
**Act:** Call `ForceRefreshAsync`.
**Assert:** `ScoreboardUpdated` fires exactly once. `RefreshCompleted` fires exactly once. Both events fire in order: `ScoreboardUpdated` before `RefreshCompleted`. `CurrentImage` equals `A`.
*Satisfies S-SC-11 and S-SC-5.*

### TC-5: `ForceRefreshAsync_DifferentBytes_FiresBothEvents`
**Arrange:** Service has stored hash of image `A`. Mock returns image `B`.
**Act:** Call `ForceRefreshAsync`.
**Assert:** `ScoreboardUpdated` fires once with `B`. `RefreshCompleted` fires once. Order: `ScoreboardUpdated` before `RefreshCompleted`. `CurrentImage` equals `B`.

### TC-6: `ForceRefreshAsync_NoStoredHash_FiresBothEvents`
**Arrange:** Fresh service, no prior state. Mock returns image `A`.
**Act:** Call `ForceRefreshAsync`.
**Assert:** `ScoreboardUpdated` fires once. `RefreshCompleted` fires once.

### TC-7: `ClearCache_ResetsCurrentImageAndHash`
**Arrange:** Service has processed image `A` (CurrentImage = `A`, hash stored).
**Act:** Call `ClearCache`.
**Assert:** `CurrentImage` is `null`. Subsequent call to `CaptureAndBroadcastAsync` (mock returns `A`) fires `ScoreboardUpdated` (hash was cleared, so `A` is treated as new).

### TC-8: `ClearCache_DoesNotFireAnyEvents`
**Arrange:** Subscribe to both `ScoreboardUpdated` and `RefreshCompleted`.
**Act:** Set up service with cached image, then call `ClearCache`.
**Assert:** Neither event fires.

### TC-9: `CurrentImage_NullBeforeFirstCapture`
**Arrange:** Fresh service, no captures performed.
**Assert:** `CurrentImage` is `null`.

### TC-10: `CaptureAndBroadcastAsync_NullBytesFromCapture_DoesNotFireEventAndDoesNotUpdateCache`
**Arrange:** Mock `CaptureScoreboardImageAsync` returns `null`.
**Act:** Call `CaptureAndBroadcastAsync`.
**Assert:** `ScoreboardUpdated` does not fire. `CurrentImage` remains unchanged. No exception is thrown.

### TC-11: `CaptureAndBroadcastAsync_EmptyBytesFromCapture_DoesNotFireEventAndDoesNotUpdateCache`
**Arrange:** Mock `CaptureScoreboardImageAsync` returns an empty byte array.
**Act:** Call `CaptureAndBroadcastAsync`.
**Assert:** `ScoreboardUpdated` does not fire. `CurrentImage` remains unchanged. No exception is thrown.

---

## 6. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `IScoreboardService` is defined in `PcsRemote.Core`; `ScoreboardService` implementation in `PcsRemote.Web` |
| AC-2 | `ScoreboardService` is registered as a singleton in `Program.cs` |
| AC-3 | TC-1 through TC-11 pass |
| AC-4 | `PcsRemote.Core` project has zero new external dependencies (no NuGet additions) |
| AC-5 | All tests passing on `master` at the time of branch creation continue to pass |
| AC-6 | Build produces 0 errors, 0 warnings |
| AC-7 | `ClearCache` is synchronous (returns `void`); `CaptureAndBroadcastAsync` and `ForceRefreshAsync` are async (return `Task`) and accept `CancellationToken ct = default` |

---

## 7. Commit Strategy

Delivered on a feature branch (`feature/S-001-scoreboard-service`); squash-merged to `master` after adversarial code review approval.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | GPT APPROVED; Sonnet NEEDS REVIEW — 1 HIGH (CancellationToken missing), 2 MEDIUM (exception propagation, concurrent ForceRefreshAsync), 4 LOW; fixes applied in v0.2 |

### R1 Findings Applied

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| CancellationToken absent from async method contracts | Sonnet | HIGH | Accept | Added `ct = default` to both methods in §3; token-flow note added; AC-7 updated |
| Exception propagation undefined | Sonnet | MEDIUM | Accept | Added exception contract to §3 method descriptions: both methods propagate to caller |
| ForceRefreshAsync concurrent-call unspecified | Sonnet | MEDIUM | Accept | Added concurrency note to §4 Thread Safety: S-004 button-disabled is the guard |
| PROJECT-CONTEXT.md version mismatch (v1.0 vs v1.1 cited) | Sonnet | LOW | Accept | Bumped PROJECT-CONTEXT.md header to v1.1 |
| §7 Commit Strategy is delivery detail | Sonnet | LOW | Accept | Reduced to one-line note |
| AC-5 hardcodes test count | Sonnet | LOW | Accept | Changed to "all tests passing on master at branch creation" |
| No TC for null/empty bytes from capture | Sonnet | LOW | Accept | Added TC-10 with null-return contract: no event, no update, no throw |
| Event payload nullability unclear | GPT | LOW | Accept | Added §4 Event Payload Nullability note: payload always non-null |

| R2 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | GPT APPROVED; Sonnet NEEDS REVIEW — 1 LOW (TC-10 covers null only, not empty bytes); fix applied in v0.3 |
| R3 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | **UNANIMOUS APPROVED** — R2 fix verified; 0 blocking, 0 non-blocking |

### R2 Findings Applied

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| TC-10 covers null only; spec says "null or empty bytes" | Sonnet | LOW | Accept | Split into TC-10 (null) and TC-11 (empty bytes) as separate explicit test cases; AC-3 updated to TC-1 through TC-11 |
