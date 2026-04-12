# SPEC-S-002 — Error Reason Propagation on `IPcsProAutomationService`

| Field | Value |
|---|---|
| **Document** | SPEC-S-002-ErrorReasonPropagation.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.2 |
| **Date** | 2026-04-12 |
| **Step** | S-002 |
| **Governing IS** | IS-005-Hardening.md v0.2 (APPROVED) |
| **Governing HLPS** | HLPS-005-Hardening.md v0.2 (APPROVED) |

---

## 1. Purpose

`IPcsProAutomationService` currently has no mechanism for callers to read the reason why the service entered the `Error` state. `MockPcsProAutomationService` already exposes a concrete `LastErrorReason` property, but it is invisible to callers holding the interface type.

This step promotes that mechanism to the interface contract so that the error display component (S-005) can read it in a type-safe, layer-correct way.

---

## 2. Scope

**In scope:**
- Add a read-only error reason property to `IPcsProAutomationService` in `PcsRemote.Core`.
- Align `MockPcsProAutomationService` so it satisfies the updated interface contract.
- Update mock reason strings to meaningfully describe each of the five H-SC-8 failure modes.
- Add unit tests verifying the Mock surfaces a non-null, non-empty reason string for each of those five error paths.

**Out of scope:**
- FlaUI automation service (`PcsRemote.Automation` currently has no concrete class; if one exists at delivery time, a `null`-returning stub for `LastErrorReason` is sufficient — full FlaUI error reason implementation is a later step).
- DI registration changes.
- Error display component implementation (S-005).

---

## 3. Contract Addition

A nullable `string` property named `LastErrorReason` is added to `IPcsProAutomationService`. It is read-only from the caller's perspective. Its semantics:

- When the service is in the `Error` state, the property holds a non-null, human-readable description of why the error occurred.
- When the service is in any non-`Error` state (including after `StopAsync` clears state and returns to `NotRunning`), the property is `null`.
- The property must be `null` initially (before any operation is attempted).

---

## 4. Mock Alignment

`MockPcsProAutomationService` already has a matching concrete property. The only required change is to ensure the property signature satisfies the new interface definition.

The mock must update its error-path reason strings to clearly identify each H-SC-8 failure mode. Each `TransitionToError(...)` call must provide a reason string that:
- Is non-null and non-empty.
- Names the phase or transition during which the simulated failure occurs in terms a user or developer can recognise.
- Is unique per failure mode — two different failure modes must not produce identical strings.

The five modes and their expected character:

| H-SC-8 Failure Mode | Expected reason character |
|---|---|
| Launching → LoginScreen (40 s timeout path) | Describes a failure reaching the login screen after launch |
| LoginScreen → MatchSelection (20 s timeout path) | Describes a failure completing login / reaching match selection |
| MatchSelectionSearching → MatchSelectionReady (30 s timeout path) | Describes a failure waiting for search results |
| MatchSelection → MatchLoaded (15 s timeout path) | Describes a failure opening the selected match |
| Unexpected dialog path | Describes an unexpected UI state or dialog interrupting automation |

Four existing injection sites cover the four timeout paths; one new injection site for the unexpected-dialog path must be added within `LaunchAndLoginAsync`.

**Deterministic test isolation:** `MockPcsProOptions` must expose a `ForcedErrorMode` option that, when set, bypasses the probability roll and fires exactly that error path. Tests for TC-4 through TC-8 each configure the Mock with a specific `ForcedErrorMode` value to target one failure mode in isolation. When `ForcedErrorMode` is not set, the existing probabilistic behaviour is unchanged.

---

## 5. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `IPcsProAutomationService` declares a `string?` property for the error reason; it compiles as part of `PcsRemote.Core` with zero new project references. |
| AC-2 | Callers holding `IPcsProAutomationService` can read `LastErrorReason` without casting to the concrete type. |
| AC-3 | After `StopAsync` completes, the property returns `null`. |
| AC-4 | For each of the five H-SC-8 failure modes, the Mock's property returns a non-null, non-empty, mode-unique string when the service is in the `Error` state. |
| AC-5 | All pre-existing tests continue to pass. |
| AC-6 | The `PcsRemote.Core.csproj` project file gains no new project references. |
| AC-7 | The full solution (`dotnet build`) completes with zero errors after all changes from this step are applied. |

---

## 6. Branch and Commit Strategy

- Branch: `feature/IS-005-S-002-error-reason-propagation`
- One or two focused commits: interface addition first, then mock alignment and tests.

---

## 7. Test Cases

Tests live in `tests/PcsRemote.Core.Tests/` for the interface contract and in a test file for the Mock (in `tests/PcsRemote.Automation.Mock.Tests/` if the project exists, otherwise alongside existing Core tests).

| TC | Scenario | Expected outcome |
|---|---|---|
| TC-1 | Compile-time contract — assign a fresh Mock to an `IPcsProAutomationService` variable; access `LastErrorReason` | Code compiles; property returns `null` (combined with TC-2) |
| TC-2 | Fresh `MockPcsProAutomationService` instance | `LastErrorReason` is null |
| TC-3 | After `StopAsync` on an errored mock | `LastErrorReason` is null |
| TC-4 | Mock error path — Launching→LoginScreen failure mode (via `ForcedErrorMode`) | `LastErrorReason` is non-null, non-empty, contains text identifying the launch/login-screen phase |
| TC-5 | Mock error path — LoginScreen→MatchSelection failure mode (via `ForcedErrorMode`) | `LastErrorReason` is non-null, non-empty, contains text identifying the login/match-selection phase |
| TC-6 | Mock error path — MatchSelectionSearching→MatchSelectionReady failure mode (via `ForcedErrorMode`) | `LastErrorReason` is non-null, non-empty, contains text identifying the search-results phase |
| TC-7 | Mock error path — MatchSelection→MatchLoaded failure mode (via `ForcedErrorMode`) | `LastErrorReason` is non-null, non-empty, contains text identifying the match-loading phase |
| TC-8 | Mock error path — unexpected-dialog path (via `ForcedErrorMode`) | `LastErrorReason` is non-null, non-empty, contains text indicating an unexpected dialog or UI state |

> **Note on TC-4 through TC-8:** Each test configures the Mock with a specific `ForcedErrorMode` value to target exactly one failure mode in isolation, bypassing the probabilistic injection. When `ForcedErrorMode` is not set, the existing probabilistic behaviour is unchanged.

---

## 8. Review History

| Round | Reviewer | Verdict | Key Findings |
|---|---|---|---|
| R1 | GPT-5.4 | NEEDS REVIEW | F-1 HIGH (TC-4–8 unworkable), F-2 MEDIUM (TC-1 nullable reflection), F-3 MEDIUM (unexpected-dialog underspecified), F-4 LOW (AC-2 wording), F-5 LOW (section label — rejected, artefact) |
| R1 | Sonnet 4.6 | NEEDS REVIEW | F-1 CRITICAL (FlaUI build break), F-2 HIGH (TC-4–8 unworkable), F-3 MEDIUM (§4 contradiction), F-4 LOW (TC-1 reflection), F-5 LOW (AC-1 scope) |
| R2 | GPT-5.4 | APPROVED | All 5 R1 fixes verified, 0 regressions |
| R2 | Sonnet 4.6 | APPROVED | All 5 R1 fixes verified, 0 regressions, cross-cutting regression check passed |
