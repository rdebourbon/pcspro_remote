# SPEC-IS-020-S-002 — Interface Extension and Mock Update

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **Step** | IS-020 S-002 |
| **Branch** | `feature/IS-020-S-002-token-expiry-event` |
| **Governing IS** | IS-020-YouTube-Token-Maintenance.md |
| **Governing HLPS** | HLPS-020-YouTube-Token-Maintenance.md |

---

## Context

S-003 and S-004 require a mechanism for the YouTube service to notify consumers (the tray host, background scheduler) that a token is approaching expiry. That notification must flow across the DI boundary — which means it must be declared on `IYouTubeLiveStreamService` in `PcsRemote.Core` before any consuming code can wire up a handler.

`MockYouTubeLiveStreamService` implements the interface directly and must be updated simultaneously to avoid a build break. The mock never fires the event (mock tokens do not expire), but it must carry the event member to preserve substitutability at the DI boundary.

The real `YouTubeLiveStreamService` is **not changed in this step** — event firing logic is added in S-003 once the interface contract is stable.

---

## Requirements

### R-1 — Add token-expiry-approaching event to the interface

`IYouTubeLiveStreamService` gains a new event that signals token expiry is approaching. The event uses `EventHandler` with no additional payload — consumers need only to know that the advisory condition has been detected; no time-remaining data is communicated at this stage. The event must follow the same declaration style as the existing `StatusChanged` and `AuthStatusChanged` events on the interface.

### R-2 — Implement the event in the mock

`MockYouTubeLiveStreamService` adds the new event member with an `event EventHandler?` backing field declaration, consistent with the existing event declarations in that class. The mock never invokes the event during any of its operations (`InitializeAsync`, `StartStreamAsync`, `StopStreamAsync`, `ResetAsync`, `RunOAuthSetupAsync`). The mock's `Availability` property continues to report Ready, and the `AuthStatusChanged` event continues to fire on init as before.

### R-3 — Real service compiles

`YouTubeLiveStreamService` must continue to build without warnings after the interface change. Because it already satisfies the interface contract at source level, this will require only adding the new event member with a matching backing field declaration. The event is not fired in this step — firing logic is S-003.

---

## Test Cases

All tests follow project conventions: MSTest 3.x + FluentAssertions, method naming `MethodName_Scenario_ExpectedResult`.

### Mock tests (`MockYouTubeLiveStreamServiceTests`)

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-1 | `TokenExpiryApproaching_DuringFullLifecycle_NeverFires` | Mock runs through init → start → stop lifecycle | `TokenExpiryApproaching` event is never raised; `Availability` remains `Ready` throughout |
| TC-2 | `TokenExpiryApproaching_AfterRunOAuthSetupAsync_NeverFires` | `RunOAuthSetupAsync` completes successfully | `TokenExpiryApproaching` event is never raised |

Both tests subscribe to the event before the lifecycle begins and assert the subscription was never invoked.

### Real service tests

No new tests for `YouTubeLiveStreamService` in this step — the real service is only updated to compile (R-3). Compile success plus existing passing tests constitute verification.

---

## Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `IYouTubeLiveStreamService` declares the token-expiry-approaching event |
| AC-2 | `MockYouTubeLiveStreamService` implements the new event member and never fires it during normal operations |
| AC-3 | `YouTubeLiveStreamService` compiles with zero warnings after the interface change |
| AC-4 | TC-1 and TC-2 are runnable (not `[Ignore]`) and pass |
| AC-5 | All existing tests continue to pass (no regressions) |
| AC-6 | Solution builds with zero warnings |
| AC-7 | `MockYouTubeLiveStreamService.Availability` remains `Ready` after the interface change |

---

## Documentation Updates

None required. No public API changes visible to external consumers; the event is an internal advisory mechanism surfaced through the existing service interface.

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 2 | GPT-5.4 | HIGH: 1 / MEDIUM: 1 / LOW: 0 | Accept: 2 | REVISION | F-1 HIGH: AC-3/AC-6 said "zero new warnings" rather than "zero warnings" — weakened the governing IS requirement. F-2 MEDIUM: Mock Availability=Ready not verified in ACs or TCs. Both accepted: AC-3/AC-6 updated to "zero warnings"; AC-7 added; TC-1 extended to assert Availability=Ready throughout lifecycle. |
