# SPEC-IS-020-S-001 — P2 Fix: Tray Pre-Check and Corrected Balloon Message

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **Step** | IS-020 S-001 |
| **Branch** | `feature/IS-020-S-001-tray-precheck` |
| **Governing IS** | IS-020-YouTube-Token-Maintenance.md |
| **Governing HLPS** | HLPS-020-YouTube-Token-Maintenance.md |

---

## Context

The existing `OnYouTubeSetupClicked` handler in `TrayApplicationContext` calls `RunOAuthSetupAsync` unconditionally. When `RunOAuthSetupAsync` returns `false` (which currently happens for both the credentials-missing path AND the non-Idle state guard), the handler shows the credentials-missing balloon message. This means an operator clicking "YouTube Setup..." during a live stream sees a misleading configuration error, not an accurate "stream is active" message.

Additionally, `RunOAuthSetupAsync`'s internal state guard currently rejects any status other than `Idle`. It should also permit `Error` state — a stream in `Error` has already failed, and re-authorisation may resolve the underlying cause.

---

## Requirements

### R-1 — Tray pre-check for active-stream states

Before calling `RunOAuthSetupAsync`, `OnYouTubeSetupClicked` must read the current live stream status. If the status is `Live`, `Starting`, or `Stopping`, the handler must:
1. Show an advisory balloon with the exact text mandated by SC4: *"YouTube Setup cannot run while streaming is active. Stop the stream first, then retry."*
2. Return without calling `RunOAuthSetupAsync`. The setup-in-progress guard (`_setupInProgress`) is not entered and the menu item remains in its current enabled state.

For `Idle` and `Error` states, the handler proceeds with the existing flow unchanged.

### R-2 — Credentials-missing balloon retained

The existing balloon message shown when `RunOAuthSetupAsync` returns `false` is retained exactly as-is. After R-1, this path is only reached when credentials are not configured — the active-stream false-return is pre-empted by the new pre-check. No change to the message text or the logic that shows it.

### R-3 — Extend `RunOAuthSetupAsync` state guard to allow `Error`

The internal state guard in `YouTubeLiveStreamService.RunOAuthSetupAsync` that currently rejects any non-`Idle` status must be updated to only reject active-stream states: `Live`, `Starting`, and `Stopping`. The guard must allow both `Idle` and `Error` to proceed to the OAuth consent flow. The log message for the rejected path should reflect the updated set of rejected states.

This is an implementation change only — the `IYouTubeLiveStreamService` interface signature is unchanged (SC5, C5).

---

## Test Cases

All new tests follow the existing project conventions: MSTest 3.x + FluentAssertions, method naming `MethodName_Scenario_ExpectedResult`.

### Tray tests (STA-dependent — stub + `[Ignore]`)

New test stubs to be added to `TrayApplicationContextTests`. All must be decorated `[Ignore]` with the standard headless-CI rationale, following the existing stub pattern. Each stub body should `true.Should().BeTrue("stub — see SPEC-IS-020-S-001 {TC-ID}")`.

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-1 | `OnYouTubeSetupClicked_WhenLive_ShowsAccurateBalloon` | Status is `Live` at click time | Accurate balloon shown; `RunOAuthSetupAsync` not called |
| TC-2 | `OnYouTubeSetupClicked_WhenStarting_ShowsAccurateBalloon` | Status is `Starting` at click time | Accurate balloon shown; `RunOAuthSetupAsync` not called |
| TC-3 | `OnYouTubeSetupClicked_WhenStopping_ShowsAccurateBalloon` | Status is `Stopping` at click time | Accurate balloon shown; `RunOAuthSetupAsync` not called |
| TC-4 | `OnYouTubeSetupClicked_WhenError_CallsRunOAuthSetupAsync` | Status is `Error` at click time | `RunOAuthSetupAsync` is called; setup flow proceeds normally |

### Service tests (headless — runnable)

New tests to be added to `YouTubeLiveStreamServiceTests`:

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-5 | `RunOAuthSetupAsync_WhenStatusIsLive_ReturnsFalse` | Service is in `Live` state | Returns `false` immediately; `GoogleWebAuthorizationBroker` not invoked |
| TC-6 | `RunOAuthSetupAsync_WhenStatusIsStarting_ReturnsFalse` | Service is in `Starting` state | Returns `false` immediately |
| TC-7 | `RunOAuthSetupAsync_WhenStatusIsStopping_ReturnsFalse` | Service is in `Stopping` state | Returns `false` immediately |

For TC-5 through TC-7: the service must be placed in the target state before the call. The mechanism may use whatever test-infrastructure approach the delivery phase determines (e.g., a test subclass, reflection-based state injection, or an integration path through the service's error-setting logic). The spec does not prescribe the mechanism — only the observable outcome.

**TC-8 — `RunOAuthSetupAsync_WhenStatusIsError_ProceedsToOAuthFlow`**: The service must be placed in `Error` state. A concrete, runnable test (not an `[Ignore]` stub) must verify that `RunOAuthSetupAsync` does NOT return immediately from the state guard and instead proceeds to the OAuth consent-flow logic. The delivery phase is responsible for providing the test infrastructure mechanism to place the service in `Error` state (e.g., via a failed initialisation path, a StartStreamAsync failure path, or an internal state-setting seam added for testability). The `[Ignore]` stub pattern is not acceptable for this test case.

---

## Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | Clicking "YouTube Setup..." while the stream is `Live`, `Starting`, or `Stopping` shows a balloon with the exact text: *"YouTube Setup cannot run while streaming is active. Stop the stream first, then retry."* `RunOAuthSetupAsync` is not called |
| AC-2 | Clicking "YouTube Setup..." while the stream is `Idle` or `Error` invokes `RunOAuthSetupAsync` and processes its result through the existing flow, including showing the credentials-missing balloon if the service returns `false` |
| AC-3 | The credentials-missing balloon message (shown when `RunOAuthSetupAsync` returns `false` on the credentials path) is unchanged |
| AC-4 | `RunOAuthSetupAsync` in `YouTubeLiveStreamService` allows `Error` state and proceeds with the consent flow; `Live`, `Starting`, and `Stopping` states still return `false` |
| AC-5 | `IYouTubeLiveStreamService` interface is unchanged |
| AC-6 | Solution builds with zero new warnings |
| AC-7a | Tray test stubs (TC-1 through TC-4) must exist in `TrayApplicationContextTests` decorated `[Ignore]` — headless CI cannot execute STA Win32 tests |
| AC-7b | Runnable service tests (TC-5 through TC-8) must execute and pass; `[Ignore]` is not acceptable for any of these test cases |

---

## Documentation Updates

None required for this step. No new public API surface, configuration, or architecture changes.

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 2 | Claude Opus 4.5, GPT-5.4 | HIGH: 2 / MEDIUM: 2 / LOW: 0 | Accept: 4 | REVISION | Opus APPROVED clean. GPT: F-1 HIGH (AC-2 misstated Idle/Error outcome); F-2 HIGH (Error-state service test had opt-out escape hatch); F-3 MEDIUM (balloon text not pinned to SC4 verbatim); F-4 MEDIUM (AC-7 conflated tray stubs with runnable service tests). All 4 accepted. |
| R2 | Tier 1 | GPT-5.4 | 0 | — | APPROVED | All 4 R1 fixes verified adequate. No regressions. |
