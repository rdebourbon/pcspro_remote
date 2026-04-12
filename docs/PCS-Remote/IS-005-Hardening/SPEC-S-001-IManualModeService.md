# SPEC-S-001: `IManualModeService` Interface in `PcsRemote.Core`

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-IManualModeService.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-005-Hardening.md v0.2 (APPROVED) |
| **Governing HLPS** | HLPS-005-Hardening.md v0.2 (APPROVED) |
| **Step** | S-001 |
| **Branch** | `feature/IS-005-S-001-imanual-mode-service` |

---

## 1. Objective

Introduce the `IManualModeService` contract in `PcsRemote.Core`. This interface is the shared boundary between `PcsRemote.TrayHost` (which writes the active flag) and `PcsRemote.Web` (which reads it and subscribes to changes). Placing it in Core satisfies the zero-dependency architecture rule: both layers depend on Core and neither depends on the other.

No implementation is delivered in this step.

---

## 2. Scope

### In Scope

- `IManualModeService` interface in `PcsRemote.Core`
- Unit test verifying the interface exists in the correct namespace

### Out of Scope

- `ManualModeService` implementation (S-003)
- DI registration (S-003)
- Hub extension (S-003)
- TrayHost wiring (S-006, S-007)

---

## 3. Interface Contract (`IManualModeService` in `PcsRemote.Core`)

### Property

**`IsManualModeActive`** — read-only boolean  
Returns `true` when manual mode is currently active; `false` otherwise.

### Methods

**`Enable()`** — synchronous, no return value  
Activates manual mode. If manual mode is already active, this is a no-op and `ManualModeChanged` must **not** be raised. Raises `ManualModeChanged` only when the flag transitions from inactive to active.

**`Disable()`** — synchronous, no return value  
Deactivates manual mode. If manual mode is already inactive, this is a no-op and `ManualModeChanged` must **not** be raised. Raises `ManualModeChanged` only when the flag transitions from active to inactive.

### Event

**`ManualModeChanged`** — `EventHandler<bool>`  
Raised by `Enable()` and `Disable()` after the flag transition. The event argument is the new value of `IsManualModeActive` (`true` = just enabled, `false` = just disabled).

The interface does not prescribe how `ManualModeChanged` is raised; that is the implementation's responsibility (S-003).

---

## 4. Design Notes

### Core Zero-Dependency Rule

`PcsRemote.Core.csproj` must have zero project references after this step, as before. No NuGet packages are required.

### Idempotency

`Enable` and `Disable` are idempotent: calling `Enable` when already active, or `Disable` when already inactive, is a no-op — the flag is unchanged and `ManualModeChanged` is not raised. This prevents spurious broadcast events when callers (tray host, future components) invoke toggle operations without first checking `IsManualModeActive`.

### Thread Safety

Thread-safety of the implementation is an S-003 concern and is not a contractual requirement of this interface. This note is **advisory only** and carries no AC for this step: consumers of `IsManualModeActive` (Web components) should treat the value as a point-in-time snapshot; the hub broadcast mechanism defined in S-003 is the authoritative change notification path.

---

## 5. Test Cases

All tests live in `PcsRemote.Core.Tests`.

### TC-1: `IManualModeService_ExistsInCoreNamespace`
**Assert:** The type `PcsRemote.Core.IManualModeService` can be reflected from the `PcsRemote.Core` assembly.

### TC-2: `IManualModeService_HasIsManualModeActiveProperty`
**Assert:** The interface has a read-only boolean property named `IsManualModeActive`.

### TC-3: `IManualModeService_HasEnableMethod`
**Assert:** The interface has a method named `Enable` with no parameters and a `void` return type.

### TC-4: `IManualModeService_HasDisableMethod`
**Assert:** The interface has a method named `Disable` with no parameters and a `void` return type.

### TC-5: `IManualModeService_HasManualModeChangedEvent`
**Assert:** The interface has an event named `ManualModeChanged` of type `EventHandler<bool>`.

> **Note:** TC-1 through TC-5 verify structural membership via reflection. The behavioural contracts — idempotency, event suppression on no-op calls, and event argument correctness — cannot be verified against the interface type alone. These behaviours are normative requirements on the concrete implementation and will be verified by the S-003 unit tests against `ManualModeService`.

---

## 6. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `IManualModeService` is defined in `PcsRemote.Core` with the exact members specified in §3 |
| AC-2 | `PcsRemote.Core.csproj` has zero project references (unchanged) |
| AC-3 | TC-1 through TC-5 pass |
| AC-4 | Build produces 0 errors, 0 warnings |
| AC-5 | All tests passing on `master` at the time of branch creation continue to pass |

---

## 7. Commit Strategy

Delivered on `feature/IS-005-S-001-imanual-mode-service`; squash-merged to `master` after adversarial code review approval.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | GPT-5.4, Claude Sonnet 4.6 | NEEDS REVIEW — 1 HIGH + 2 MEDIUM; 1 MEDIUM rejected (established convention); v0.2 fixes applied |
| R2 | 2026-04-12 | GPT-5.4, Claude Sonnet 4.6 | **APPROVED** — unanimous; 4/4 findings verified; 0 regressions |

### R1 Findings Applied

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| §3 and §4 contradict on event suppression during no-ops | GPT+Sonnet | HIGH | Accept | §3 method descriptions updated: event must NOT fire on no-op; §4 Idempotency rewritten to remove "optionally" and state rule clearly |
| Idempotency/event-suppression not verifiable in TC-1–5 | GPT+Sonnet | MEDIUM | Accept | Note added after TC-5 deferring behavioural contracts to S-003 unit tests |
| AC-4 "0 errors/warnings" broader than verification intent | GPT | MEDIUM | Reject | Established project convention — identical language approved in prior specs |
| Thread safety advisory has no corresponding AC | Sonnet | LOW | Accept | §4 Thread Safety note marked explicitly as advisory; AC-free |
