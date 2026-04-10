# HLPS-002: Mock Automation Service

| Field | Value |
|---|---|
| **Document** | HLPS-002-Mock-Service.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | HLPS-001 (interface + domain types must exist) |

---

## 1. Problem Statement

PCS Pro (cricket.exe) only runs on the dedicated garage PC. Developers working on the web UI, SignalR integration, state machine behaviour, and end-to-end flows cannot install or run PCS Pro on their development machines.

Without a mock implementation of `IPcsProAutomationService`, all downstream development is blocked until the FlaUI real implementation (HLPS-006) is complete — making iterative, test-driven delivery impossible.

This HLPS delivers a fully functional mock that simulates PCS Pro's entire lifecycle with realistic timing, data, and state transitions, enabling the entire web application to be built, tested, and refined without PCS Pro.

---

## 2. Scope

### In Scope

- **`MockPcsProAutomationService`** implementing `IPcsProAutomationService` in `PcsRemote.Automation.Mock`
- **Realistic state transition simulation**: Short configurable delays (e.g., 2s login, 3s match search) to mimic real PCS Pro timing. These delays represent dwell time before a trigger is fired — they are not transition processing latency. C-11 (<500ms) governs the time from trigger invocation to `StateChanged` event dispatch, which must be <500ms in all implementations including the mock.
- **Fake match data**: `GetTodaysMatchesAsync` returns a configurable list of realistic matches (team names, match types) where each record's match date is stamped with the current date at call-time — not a fixed literal. This ensures the data is always "for today" (per PROJECT-CONTEXT §2 Architecture Decision 2).
- **Placeholder scoreboard images**: `CaptureScoreboardImageAsync` returns a generated JPEG (solid colour with overlay text). Image content varies with a configurable probability per call (default ~20%) to exercise delta detection downstream. Image generation uses `System.Drawing.Common`, which is acceptable given C-1 (Windows-only); this decision must be revisited if CI runners become Linux-hosted.
- **StateChanged event firing**: Mock fires `StateChanged` events on every transition, exactly as the real implementation would.
- **Cancellation support**: Simulated delays pass the caller's `CancellationToken` to `Task.Delay` and propagate `OperationCanceledException` on cancellation, leaving the mock in a defined state.
- **DI registration**: Toggle between mock and real via `PcsPro:UseMock` config flag (per PROJECT-CONTEXT §2 Architecture Decision 2).
- **Single-caller semantics**: The mock is registered as a singleton. Concurrent calls to lifecycle methods (e.g., two simultaneous `LaunchAndLoginAsync` calls) represent an invalid usage of a single PCS Pro instance and must be rejected with an appropriate exception.
- **Configurable behaviour**: Simulated delays (zero-configurable for unit tests), error injection probability, image variation probability, and number of fake matches — all driven from a dedicated options class injected via DI.
- **Unit tests**: Verify state transitions, event firing, delay behaviour, error injection, cancellation, and DI resolution — in `PcsRemote.Automation.Mock.Tests`.
- **Serilog integration**: Mock logs all simulated actions via an injected `ILogger`; log output is verifiable in unit tests via an in-memory sink.

### Out of Scope

- Real FlaUI automation (HLPS-006)
- Web UI or SignalR (HLPS-003+)
- Scoreboard image delta detection logic (HLPS-004 — the mock just produces images)
- PrintWindow Win32 capture (HLPS-006)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| M-SC-1 | Mock implements all `IPcsProAutomationService` methods with simulated behaviour | Code review + unit tests |
| M-SC-2 | State transitions fire `StateChanged` events in correct order | Unit tests verify event sequence |
| M-SC-3 | `GetTodaysMatchesAsync` returns match records where every record's match date equals the current date at call-time | Unit test asserts all returned records carry today's date |
| M-SC-4 | `CaptureScoreboardImageAsync` returns valid JPEG bytes; image content varies according to configurable probability | Unit test verifies JPEG header; with probability=1.0 asserts no two consecutive calls return identical bytes; with probability=0.0 asserts all calls return identical bytes |
| M-SC-5 | DI toggle works: `PcsPro:UseMock=true` → builds `ServiceProvider`, resolves `IPcsProAutomationService`, and asserts instance is `MockPcsProAutomationService`; `UseMock=false` → registration line compiles (no runtime test — real implementation absent) | Integration test with DI container |
| M-SC-6 | Error injection: configurable probability triggers Error state with descriptive reason; RNG is seedable for deterministic tests | Unit test with seeded error config |
| M-SC-7 | All simulated actions are logged via Serilog at appropriate levels | Unit tests inject an in-memory Serilog sink and assert at least one `Information`-or-above event is emitted per state transition |
| M-SC-8 | Simulated delays are configurable and zero-configurable for unit tests (no hardcoded magic numbers) | Code review — all delays sourced from options |
| M-SC-9 | Cancelling an in-progress operation via `CancellationToken` causes the mock to throw `OperationCanceledException`; mock state remains consistent after cancellation | Unit test cancels mid-delay and asserts exception propagates and state is unchanged |
| M-SC-10 | Elapsed time from trigger invocation to `StateChanged` event dispatch is <500ms (C-11 compliance) | Unit test measures elapsed time across a state transition with zero-delay config and asserts <500ms |
| M-SC-11 | A concurrent second call to a lifecycle method while one is already in progress throws `InvalidOperationException` | Unit test issues two overlapping calls and asserts the second throws `InvalidOperationException` |
| M-SC-12 | After `StopAsync` completes, mock state is `NotRunning` | Unit test drives full lifecycle then calls `StopAsync` and asserts `State == NotRunning` |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| M-U-1 | Should the mock support simulating "PCS Pro not running" (NotRunning state) or always auto-launch? | Agent | No — resolved: full lifecycle from NotRunning supported by default |
| M-U-2 | Must simulated delays be zero-configurable for unit tests? | Agent | No — resolved: yes, all delays sourced from options; zero is a valid value |
| M-U-3 | Must error injection RNG be seedable for deterministic unit tests? | Agent | No — resolved: yes, RNG must accept an optional seed; unseeded uses system random |
| M-U-4 | Must mock reset to NotRunning on StopAsync to allow test re-use within a single test run? | Agent | No — resolved: yes, StopAsync returns the mock to NotRunning state |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | Claude Sonnet 4.6, GPT-4.1 | NEEDS REVIEW / APPROVE — 2 HIGH + 6 MEDIUM + 2 LOW (Sonnet); clean (GPT) |
| R2 | 2026-04-10 | Claude Sonnet 4.6 | REVISE — all R1 findings resolved; 2 MEDIUM + 1 LOW regressions (verification gaps) |
| R3 | 2026-04-10 | Claude Sonnet 4.6 | **APPROVED** — all R2 regressions verified resolved; no new findings |

### R2 Findings Applied (v0.2 → v0.3)

| Ref | Finding | Disposition | Resolution |
|---|---|---|---|
| Sonnet MEDIUM-1 | C-11 "must" obligation on mock has no verification criterion | Accept | M-SC-10 added: zero-delay transition timed, asserts <500ms |
| Sonnet MEDIUM-2 | Concurrent-rejection "must" has no verification criterion | Accept | M-SC-11 added: overlapping calls assert `InvalidOperationException` |
| Sonnet LOW-3 | `StopAsync` → NotRunning obligation has no verification criterion | Accept | M-SC-12 added: full lifecycle + StopAsync asserts `NotRunning` |

### R1 Findings Applied (v0.1 → v0.2)

| Ref | Finding | Disposition | Resolution |
|---|---|---|---|
| Sonnet HIGH-1 | Hardcoded match data vs `DateTime.Today` contradiction | Accept | Scope updated: match dates stamped at call-time; M-SC-3 tightened to assert today's date |
| Sonnet HIGH-2 | C-11 <500ms vs simulated delays unresolved | Accept | Scope note added: C-11 governs trigger-to-event latency, not dwell time before trigger |
| Sonnet MED-3 | PRD §5a cited without PRD in governing docs | Accept (downgrade LOW) | Replaced with PROJECT-CONTEXT §2 references |
| Sonnet MED-4 | "Occasionally" unmeasurable in M-SC-4 | Accept | Replaced with "configurable probability (~20% default)"; M-SC-4 now has deterministic test thresholds |
| Sonnet MED-5 | Thread safety of singleton unaddressed | Accept | Scope: single-caller semantics documented; concurrent lifecycle calls rejected with exception |
| Sonnet MED-6 | CancellationToken behaviour unspecified | Accept | Added cancellation scope bullet; M-SC-9 added |
| Sonnet MED-7 | Image library unspecified | Accept | System.Drawing.Common declared in scope; C-1 Windows-only cited |
| Sonnet MED-8 | Implicit assumptions unrecorded | Accept | M-U-2 (delay zeroing), M-U-3 (RNG seeding), M-U-4 (reset on StopAsync) added |
| Sonnet LOW-9 | M-SC-7 not automatable | Accept | M-SC-7 updated to reference in-memory Serilog sink |
| Sonnet LOW-10 | M-SC-5 DI assertion underspecified | Accept | M-SC-5 tightened to specify instance type assertion |
