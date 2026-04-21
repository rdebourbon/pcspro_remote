# SPEC-S-002: Operation Description on Coordinator Interface

| Field | Value |
|---|---|
| **Document** | SPEC-S-002-OperationDescriptionContract.md |
| **Status** | APPROVED |
| **Version** | 0.1 |
| **Date** | 2026-04-21 |
| **Governing IS** | IS-011-Operational-UX.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-011-Operational-UX.md v0.4 (APPROVED) |
| **Step** | S-002 |
| **Branch** | `feature/IS-011-S-002-operation-description` |

---

## 1. Objective

Extend `IOperationCoordinatorService` in `PcsRemote.Core` so that callers can provide a human-readable description when beginning an operation (e.g., "Launching PCS Pro…", "Loading match…"). The interface gains a readable property for the current description and the `BeginOperation` method gains a description parameter. This provides the Core contract required by S-005 (implementation and UI) to display a status banner showing what operation is executing.

No implementation is delivered in this step — only the interface changes.

---

## 2. Scope

### In Scope

- Description parameter on `BeginOperation`
- `CurrentOperationDescription` read-only property on `IOperationCoordinatorService`
- Unit tests verifying the interface members exist via reflection

### Out of Scope

- `OperationCoordinatorService` implementation changes (S-005)
- Status banner UI (S-005)
- Broadcaster/hub changes to push description (S-005)
- Clearing mechanism (primary and safety-net paths) (S-005)

---

## 3. Requirements

### R-1: BeginOperation Description Parameter

Add a string parameter to `BeginOperation` that accepts a human-readable description of the operation being started. The parameter must be optional with a null default value so that existing callers compile without changes. The description should be nullable — passing null or omitting it indicates no description is available.

### R-2: CurrentOperationDescription Property

Add a read-only nullable string property named `CurrentOperationDescription` to the interface. Returns the description provided by the most recent successful `BeginOperation` call, or null when no operation is in progress. The semantics of when this property is cleared (on `MarkComplete`, on state transitions, etc.) are implementation concerns for S-005 — the interface contract only specifies that the property exists and is readable.

### R-3: Event Unchanged

The existing `OperationInProgressChanged` event type (`EventHandler<bool>`) remains unchanged. Subscribers that need the description can read `CurrentOperationDescription` from the service when they receive the event notification. This avoids a breaking change to the event signature and follows the existing pattern where subscribers read service properties in response to events.

---

## 4. Design Notes

### Core Zero-Dependency Rule

`PcsRemote.Core.csproj` must have zero project references after this step. No new NuGet packages required.

### Backward Compatibility

The optional default parameter on `BeginOperation` ensures source-level backward compatibility — existing callers that pass no description continue to compile. Binary compatibility is not a concern for this in-process project.

### Compiler Impact

Changing the `BeginOperation` method signature with a default parameter does not break implementors at the source level — existing implementations will compile as long as the parameter has a default value. However, if the implementation's method signature does not match (i.e., it lacks the new parameter), it will need updating. The downstream `OperationCoordinatorService` implementation will be updated in S-005. The Core and Core.Tests projects will compile cleanly — Core.Tests has no `IOperationCoordinatorService` implementors.

---

## 5. Test Cases

All tests live in `PcsRemote.Core.Tests`.

### TC-1: `IOperationCoordinatorService_HasCurrentOperationDescriptionProperty`
**Assert:** The interface has a read-only nullable string property named `CurrentOperationDescription`.

### TC-2: `IOperationCoordinatorService_BeginOperation_AcceptsOptionalStringParameter`
**Assert:** The `BeginOperation` method has one parameter of type `string?` with a default value.

---

## 6. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `IOperationCoordinatorService.BeginOperation` accepts an optional nullable string description parameter |
| AC-2 | `IOperationCoordinatorService` has a read-only `CurrentOperationDescription` property of type `string?` |
| AC-3 | The `OperationInProgressChanged` event signature remains `EventHandler<bool>` (unchanged) |
| AC-4 | `PcsRemote.Core.csproj` has zero project references (unchanged) |
| AC-5 | TC-1 and TC-2 pass |
| AC-6 | `PcsRemote.Core` and `PcsRemote.Core.Tests` build with 0 errors, 0 warnings |
| AC-7 | All pre-existing tests in `PcsRemote.Core.Tests` continue to pass |

---

## 7. Commit Strategy

Delivered on `feature/IS-011-S-002-operation-description`; squash-merged to `master` after adversarial code review approval.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Claude Opus 4.7, GPT 5.4 | Opus: APPROVE. GPT: REQUEST CHANGES — 3 findings (F1 event contract, F2 cross-assembly build, F3 timing risk) all rejected with rationale: F1/F3 — property-read pattern matches existing proven codebase convention, synchronous event guarantees ordering; F2 — same Core-only scope precedent as S-001. APPROVED with 1/2 + justified rejections. |

