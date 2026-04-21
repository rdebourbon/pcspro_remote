# SPEC-S-001: Dismiss Trigger and Dismiss Contract

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-DismissTriggerAndContract.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-21 |
| **Governing IS** | IS-011-Operational-UX.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-011-Operational-UX.md v0.4 (APPROVED) |
| **Step** | S-001 |
| **Branch** | `feature/IS-011-S-001-dismiss-trigger` |

---

## 1. Objective

Extend `PcsRemote.Core` with a named Dismiss trigger on the state machine and a corresponding `DismissAsync` method on `IPcsProAutomationService`. This provides the Core contracts required by S-004 (implementation and UI) to allow operators to dismiss an error without restarting automation.

Dismiss is semantically distinct from Retry: Retry clears the error *and* restarts the automation sequence; Dismiss clears the error only, returning the system to a quiescent NotRunning state.

No implementation is delivered in this step — only the trigger, transition definition, and interface method declaration.

---

## 2. Scope

### In Scope

- New `Dismiss` member on the `PcsProTrigger` enum
- New `Error → NotRunning` transition on the `Dismiss` trigger in `PcsProStateMachine`
- New `DismissAsync` method declaration on `IPcsProAutomationService`
- Unit tests verifying the trigger, transition, invalid-state rejection, and interface member existence

### Out of Scope

- Mock/Real automation service implementations of `DismissAsync` (S-004)
- Dismiss button in the UI (S-004)
- Coordinator/manual-mode exemption for Dismiss (S-004)

---

## 3. Requirements

### R-1: PcsProTrigger Extension

Add a `Dismiss` member to the `PcsProTrigger` enum. The member name must be `Dismiss` to clearly distinguish it from the existing `Retry` trigger.

### R-2: State Machine Transition

Configure the `Error` state in `PcsProStateMachine` to permit the `Dismiss` trigger, transitioning to `NotRunning`. The existing `Retry` transition from `Error` to `NotRunning` must remain unchanged. The `Dismiss` trigger must be invalid (throw `InvalidOperationException`) from all states other than `Error`.

### R-3: Automation Service Interface Extension

Add a `DismissAsync` method to `IPcsProAutomationService`. The method should follow the same pattern as existing async methods on the interface: returning `Task`, accepting an optional `CancellationToken`, and including an XML doc comment describing the method's purpose and preconditions. The precondition is that the current state must be `Error`; calling from any other state should throw `InvalidOperationException`.

---

## 4. Design Notes

### Core Zero-Dependency Rule

`PcsRemote.Core.csproj` must have zero project references after this step, as before. No new NuGet packages are required.

### Coexistence with Retry

Both `Retry` and `Dismiss` transition from `Error` to `NotRunning`. They are distinct triggers with different semantic intent. The state machine permits both from the `Error` state. The distinction in behaviour (Retry restarts automation, Dismiss does not) is an implementation concern for S-004 — not a state machine concern.

### Compiler Impact

Adding `DismissAsync` to the interface will cause compile errors in any class that implements `IPcsProAutomationService` without the new method. This is intentional — the compiler will flag all implementors that need updating. `PcsRemote.Core.Tests` does not contain any `IPcsProAutomationService` implementors, so the Core and Core.Tests projects will compile cleanly. Downstream projects outside Core/Core.Tests (Automation, Automation.Mock, Web) may not compile until S-004 delivers the implementations, which is acceptable for a Core-contract step.

---

## 5. Test Cases

All tests live in `PcsRemote.Core.Tests.PcsProStateMachineTests` (state machine tests) and a new test class for the interface contract verification.

### TC-1: `Fire_Dismiss_FromError_TransitionsToNotRunning`
**Arrange:** Build a state machine at the `Error` state using the existing `BuildMachineAt` helper.
**Act:** Fire the `Dismiss` trigger.
**Assert:** `CurrentState` is `NotRunning`.

### TC-2: `Fire_Dismiss_FromNonErrorState_ThrowsInvalidOperationException`
**Arrange:** Build a state machine at each `PcsProState` value except `Error` using data-driven rows. The row set must cover all non-Error states to guarantee completeness — if a new state is added to the enum in the future, TC-2 must be extended to include it.
**Act:** Fire the `Dismiss` trigger.
**Assert:** Throws `InvalidOperationException`.

### TC-3: `Fire_Retry_FromError_StillTransitionsToNotRunning`
**Purpose:** Regression guard — confirm the existing Retry transition is unaffected by the addition of Dismiss.
**Arrange:** Build a state machine at the `Error` state.
**Act:** Fire the `Retry` trigger.
**Assert:** `CurrentState` is `NotRunning`.

### TC-4: `IPcsProAutomationService_HasDismissAsyncMethod`
**Assert:** The interface type has a method named `DismissAsync` that returns `Task` and accepts a `CancellationToken` parameter. The parameter should follow the optional default-value pattern used by other async methods on the interface.

> **Note:** TC-1 through TC-3 extend the existing `PcsProStateMachineTests` class. TC-4 may be added to the existing `IManualModeServiceTests` pattern or a new interface contract test class — the delivery phase determines the optimal location.

---

## 6. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `PcsProTrigger` enum contains a `Dismiss` member |
| AC-2 | `PcsProStateMachine` permits `Dismiss` from `Error`, transitioning to `NotRunning` |
| AC-3 | `Dismiss` from any non-Error state throws `InvalidOperationException` |
| AC-4 | Existing `Retry` transition from `Error` to `NotRunning` is unaffected |
| AC-5 | `IPcsProAutomationService` declares `DismissAsync` with the standard async method pattern |
| AC-6 | `PcsRemote.Core.csproj` has zero project references (unchanged) |
| AC-7 | TC-1 through TC-4 pass |
| AC-8 | `PcsRemote.Core` and `PcsRemote.Core.Tests` build with 0 errors, 0 warnings |
| AC-9 | All pre-existing tests in `PcsRemote.Core.Tests` continue to pass |

---

## 7. Commit Strategy

Delivered on `feature/IS-011-S-001-dismiss-trigger`; squash-merged to `master` after adversarial code review approval.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Claude Opus 4.7, GPT 5.4 | REQUEST CHANGES — F-1 (AC-8/§4 contradiction), F-2 (TC-2 completeness), F-001 (TC-4 contract shape). All accepted; v0.2 fixes applied. |
| R2 | 2026-04-21 | Claude Opus 4.7, GPT 5.4 | **APPROVED** — unanimous; all R1 findings verified; 0 regressions. |

