# SPEC-S-004: State Machine and Unit Tests

| Field | Value |
|---|---|
| **Document** | SPEC-S-004-State-Machine-and-Unit-Tests.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-10 |
| **Step** | S-004 — State Machine and Unit Tests |
| **IS** | IS-001-Foundation.md (APPROVED v0.2) |
| **HLPS** | HLPS-001-Foundation.md (APPROVED v0.4) |
| **Branch** | `feature/S-004-state-machine` |

---

## 1. Overview

This step implements `PcsProStateMachine` in `PcsRemote.Core` — the domain core that every automation sequence in every subsequent HLPS flows through. It wraps the Stateless library to model the full PCS Pro application lifecycle: 8 states, 11 triggers, all defined transitions, and compile-time timeout constants. A comprehensive unit test suite in `PcsRemote.Core.Tests` covers all valid transitions, invalid transition rejection, timeout constants, and timeout-triggered Error transitions.

This delivers HLPS-001 success criteria F-SC-2, F-SC-3, F-SC-4, and F-SC-5.

---

## 2. Scope

### In Scope

- Implement `PcsProStateMachine` class in `PcsRemote.Core`
- Wrap `StateMachine<PcsProState, PcsProTrigger>` from the Stateless NuGet package
- Expose the public surface defined in HLPS-001 §2:
  - `PcsProState CurrentState` — read-only property reflecting inner machine state
  - `void Fire(PcsProTrigger trigger)` — delegates to inner Stateless machine; throws `InvalidOperationException` on invalid transitions (Stateless default behaviour)
  - Transition notification mechanism — an `Action<PcsProState>` delegate or equivalent exposed so consuming implementations can raise `StateChanged` events
- Define compile-time timeout constants for the 4 timed states:
  - Launching: 40 seconds
  - LoginScreen: 20 seconds
  - MatchSelectionSearching: 30 seconds
  - MatchSelectionReady: 15 seconds
- Wire all transitions per PRD §6 (full table in §3 below)
- Write unit tests in `PcsRemote.Core.Tests` covering all required scenarios (§4)

### Out of Scope

- Timer management — firing `PcsProTrigger.Timeout` after a delay is the responsibility of the automation layer (HLPS-002+); the state machine is purely passive
- `IPcsProAutomationService` implementation — HLPS-002/HLPS-006
- DI registration — HLPS-002
- Any FlaUI or Windows UI interaction

---

## 3. State Machine Definition

### 3.1 Full Transition Table (from PRD §6)

The initial state on construction is `NotRunning`. Thread safety is not required; usage is assumed to be single-threaded.

| From State | Trigger | To State |
|---|---|---|
| `NotRunning` | `Launch` | `Launching` |
| `Launching` | `LoginDetected` | `LoginScreen` |
| `LoginScreen` | `CredentialsEntered` | `MatchSelection` |
| `MatchSelection` | `SearchTriggered` | `MatchSelectionSearching` |
| `MatchSelectionSearching` | `SpinnerGone` | `MatchSelectionReady` |
| `MatchSelectionReady` | `MatchOpened` | `MatchLoaded` |
| `MatchLoaded` | `ChangeMatch` | `MatchSelection` |
| `MatchLoaded` | `Stop` | `NotRunning` |
| All states except `NotRunning` and `Error` | `UnexpectedDialog` | `Error` |
| All states except `NotRunning` and `Error` | `Timeout` | `Error` |
| `Error` | `Retry` | `NotRunning` |

`NotRunning` and `Error` intentionally exclude the `UnexpectedDialog` and `Timeout` triggers: PCS Pro is not running in `NotRunning` so no dialog or timeout can occur; `Error` is a terminal-pending state exited only by `Retry`.

### 3.2 Timeout Constants

| Constant Name | State | Value |
|---|---|---|
| `LaunchingTimeoutSeconds` | `Launching` | 40 |
| `LoginScreenTimeoutSeconds` | `LoginScreen` | 20 |
| `MatchSelectionSearchingTimeoutSeconds` | `MatchSelectionSearching` | 30 |
| `MatchSelectionReadyTimeoutSeconds` | `MatchSelectionReady` | 15 |

These must be defined as `public const int` or `public static readonly int` members of `PcsProStateMachine` so they are directly assertable in unit tests without reflection.

### 3.3 Transition Notification

The state machine must expose a mechanism for consumers to register a callback that is invoked after each successful transition. The callback receives the new `PcsProState`. This is how the `StateChanged` event in consuming implementations will be driven. The mechanism must be registered via Stateless's `OnTransitioned` hook. The callback must not be invoked when `Fire()` throws `InvalidOperationException` — it fires only on completed transitions.

---

## 4. Unit Tests

All tests go in `PcsRemote.Core.Tests`. Each test creates a fresh `PcsProStateMachine` instance. Tests must be independent and not share state.

### 4.1 Valid Transition Tests

For each of the 9 deterministic-source rows in §3.1 (rows with a single named From State — i.e., all rows except the `UnexpectedDialog`/`Timeout` wildcard rows, which are covered by §4.2):
- Verify that a machine in the `From State` transitions to `To State` when `Trigger` is fired

### 4.2 Wildcard Transition Tests

For `UnexpectedDialog` and `Timeout` triggers, verify that firing each from all 6 applicable states produces `Error`. Applicable states (per §3.1): `Launching`, `LoginScreen`, `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, `MatchLoaded`. `NotRunning` and `Error` are explicitly excluded (see §3.1 rationale).

### 4.3 Invalid Transition Tests

Verify that firing a trigger not listed in §3.1 for the current state throws `InvalidOperationException`. Representative cases sufficient — at minimum:
- Fire `LoginDetected` from `NotRunning`
- Fire `Launch` from `MatchLoaded`
- Fire `MatchOpened` from `Launching`

### 4.4 Timeout Constant Tests

Assert the exact values of all 4 timeout constants:
- `LaunchingTimeoutSeconds` == 40
- `LoginScreenTimeoutSeconds` == 20
- `MatchSelectionSearchingTimeoutSeconds` == 30
- `MatchSelectionReadyTimeoutSeconds` == 15

### 4.5 Timeout Trigger to Error Tests

Verify that `Fire(PcsProTrigger.Timeout)` from each of the 4 timed states transitions to `Error`. The purpose of this distinct section is F-SC-5 traceability — confirming that the states with defined timeout constants (§3.2) are a subset of the states that transition via `Timeout`, and that all four do so correctly. These 4 tests are a subset of §4.2 and may be implemented as parameterised data rows shared with §4.2's Timeout cases.

- From `Launching`
- From `LoginScreen`
- From `MatchSelectionSearching`
- From `MatchSelectionReady`

### 4.6 Transition Notification Tests

Verify that the transition notification mechanism fires after a representative successful transition and that the new state is correctly reported to the registered callback. Also verify that the callback is NOT invoked when `Fire()` throws `InvalidOperationException` (i.e., on an invalid transition attempt).

---

## 5. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` exits 0 with no errors or warnings |
| AC-2 | `dotnet test` exits 0 — all tests pass |
| AC-3 | `PcsProStateMachine` class exists in `PcsRemote.Core` with the 3-member public surface: `CurrentState`, `Fire`, and a transition notification mechanism |
| AC-4 | All transitions from §3.1 are wired: 9 deterministic-source transitions (including `Error` → `Retry` → `NotRunning`) + `UnexpectedDialog`/`Timeout` from 6 applicable states; firing each produces the correct destination state |
| AC-5 | Firing an invalid trigger from the current state throws `InvalidOperationException` |
| AC-6 | Four compile-time timeout constants defined as `public const int` or `public static readonly int` with values matching §3.2 |
| AC-7 | `Fire(PcsProTrigger.Timeout)` from each of the 4 timed states transitions to `Error` |
| AC-8 | Transition notification callback fires after a successful transition with the correct new state; callback is NOT invoked when `Fire()` throws `InvalidOperationException` |
| AC-9 | `PcsRemote.Core` has no new project references (zero P2P deps; only Stateless retained) |
| AC-10 | A freshly constructed `PcsProStateMachine` reports `CurrentState == NotRunning` |

---

## 6. Verification Table

| AC | Verification Method |
|---|---|
| AC-1 | `dotnet build` — exit 0, zero warnings |
| AC-2 | `dotnet test` — all test classes in `PcsRemote.Core.Tests` pass |
| AC-3 | Code review of `PcsProStateMachine.cs` — public surface matches HLPS-001 §2 |
| AC-4 | Unit tests §4.1 + §4.2 pass; code review confirms Stateless `.Configure()` calls cover all rows including 6-state wildcards |
| AC-5 | Unit tests §4.3 pass |
| AC-6 | Unit tests §4.4 assert constant values; code review confirms `const`/`static readonly` declaration |
| AC-7 | Unit tests §4.5 pass |
| AC-8 | Unit tests §4.6 pass (positive and negative notification tests) |
| AC-9 | Inspect `PcsRemote.Core.csproj` — no new `<ProjectReference>` elements |
| AC-10 | Unit test verifying `new PcsProStateMachine().CurrentState == NotRunning` passes |

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | Claude Opus 4.6, Claude Sonnet 4.6, GPT-4.1 | REVISE / REVISE / APPROVE — 2 MEDIUM accepted; 5 LOW accepted (4) / rejected (1) |
| R2 | 2026-04-10 | Claude Opus 4.6, Claude Sonnet 4.6 | APPROVE / REVISE — 2 LOW + 1 MEDIUM, same root cause: AC-4 wording + §4.5 "exactly" → fixed |

### R1 Findings Applied (v0.1 → v0.2)

| Ref | Finding | Disposition | Resolution |
|---|---|---|---|
| All — F1 | "Any state" scope inconsistency between §3.1 and §4.2 | Accept (MEDIUM) | §3.1 wildcard rows now specify "All states except `NotRunning` and `Error`" with rationale note; §4.1 explicitly scoped to 9 deterministic rows; §4.2 renamed "Wildcard Transition Tests" with explicit 6-state enumeration and exclusion rationale; AC-4 updated to reference explicit state sets |
| Sonnet + GPT — F2 | Initial state not specified | Accept (LOW) | Added "Initial state on construction: `NotRunning`" to §3.1 preamble; AC-10 added |
| All — F3 | §4.5 overlaps §4.2 | Accept (LOW) | §4.5 now carries explicit F-SC-5 traceability note and states tests may share implementation with §4.2 parameterised rows |
| All — F4 | §4.6 "each" ambiguous; negative callback test missing | Accept (LOW) | §4.6 changed to "representative"; negative test (callback NOT fired on invalid transition) added |
| Sonnet — F5 | Callback must not fire on `InvalidOperationException` | Accept (LOW) | Added to §3.3; reflected in §4.6 and AC-8 |
| GPT — thread safety | Thread safety not stated | Accept (LOW) | Added single-threaded assumption to §3.1 preamble |
| GPT — notification registration type | Mechanism type is a delivery detail | Reject | Already settled in §3.3; spec correctly defers to delivery |

### R2 Findings Applied (v0.2 → v0.3)

| Ref | Finding | Disposition | Resolution |
|---|---|---|---|
| Both — AC-4 double-count | `Error → Retry → NotRunning` listed as separate bullet outside "9 deterministic" despite being in §4.1 scope | Accept | Parenthetical added: "9 deterministic-source transitions (including `Error` → `Retry` → `NotRunning`)" |
| Opus — §4.5 "exactly" | "exactly the states that can transition via Timeout" false — 4 timed < 6 Timeout-capable | Accept | Changed to "a subset of the states that transition via `Timeout`, and that all four do so correctly" |
