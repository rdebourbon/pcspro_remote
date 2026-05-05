# SPEC-S-001 — Date Selection Service (Core)

| Field           | Value |
|-----------------|-------|
| **Status**      | APPROVED |
| **Step ID**     | S-001 |
| **Governs**     | IS-019 S-001 |
| **Branch**      | `feature/S-001-date-selection-service` |
| **SC Coverage** | SC-1 (primary), SC-4, SC-5 (enables), C-3 |

---

## Problem

HLPS-019 requires a toggle service to track whether the date selection override is enabled. This service is the foundational dependency for the debug panel toggle (S-004), the date picker UI (S-005), and the auto-reset mechanism (S-005). Without it, no downstream step can proceed.

---

## Requirements

### R1 — Interface contract

Define a public interface in `PcsRemote.Core` that exposes:

- A read-only boolean property indicating whether date selection is currently enabled. Must default to `false` (SC-1, C-3).
- An `Enable()` method that transitions the flag from disabled to enabled. Idempotent: calling when already enabled is a no-op and must not raise the change notification.
- A `Disable()` method that transitions the flag from enabled to disabled. Idempotent: calling when already disabled is a no-op and must not raise the change notification.
- A change notification event that fires only on actual state transitions, passing the new boolean value as the event argument.

The interface must follow the same structural pattern as the existing `IManualModeService` interface in `PcsRemote.Core`. A `Toggle()` method is not required — the date selection service only needs explicit enable/disable control.

### R2 — Thread-safe implementation

Provide a concrete implementation in `PcsRemote.Core` following the same thread-safety pattern as `ManualModeService`:

- State backed by an integer field toggled atomically via `Interlocked.CompareExchange`.
- The change notification event fires exactly once per actual state transition, even under concurrent access from multiple threads.
- No locking; the CAS pattern is sufficient for a boolean toggle.

### R3 — No external dependencies

The implementation must reside in `PcsRemote.Core` with zero dependencies on `PcsRemote.Web`, `PcsRemote.Automation`, or any other PCS Remote project. This is an architectural invariant (copilot-instructions §Architecture Rules).

### R4 — DI registration deferred

DI registration of the service as a singleton is deferred to S-004, which owns the wiring in the web layer. This step creates only the interface and implementation in Core.

---

## Test Strategy

All tests use MSTest 3.x with FluentAssertions. Tests follow the `MethodName_Scenario_ExpectedResult` naming convention. Test class should follow the existing pattern in `ManualModeServiceTests.cs` (factory method, section-commented test cases).

### TC-1 — Default state

Verify that a freshly constructed instance reports the toggle as disabled.

### TC-2 — Enable transitions state

Verify that calling Enable on a disabled instance transitions the state to enabled and fires the change notification exactly once with `true`.

### TC-3 — Enable idempotency

Verify that calling Enable twice fires the event only once total and the state remains enabled.

### TC-4 — Disable transitions state

Verify that calling Disable on an enabled instance transitions the state to disabled and fires the change notification exactly once with `false`.

### TC-5 — Disable idempotency on default state

Verify that calling Disable on a freshly constructed (already disabled) instance does not fire the event and the state remains disabled.

### TC-6 — Concurrent Enable calls

Verify that N threads calling Enable concurrently result in exactly one event firing and the final state being enabled. Follow the `Barrier`-based pattern from `ManualModeServiceTests.TC-6`.

### TC-7 — Concurrent Disable calls

Verify that N threads calling Disable on an enabled instance concurrently result in exactly one event firing and the final state being disabled.

### TC-8 — Mixed concurrent Enable and Disable calls

Verify that N threads each calling Enable and Disable concurrently do not corrupt state and the alternation invariant holds (event count for each direction balanced within ±1).

---

## Acceptance Criteria

| AC   | Description |
|------|-------------|
| AC-1 | Interface and implementation exist in `PcsRemote.Core` with no additional project dependencies. |
| AC-2 | Default state is disabled (`false`). |
| AC-3 | Enable/Disable are idempotent — no spurious events on redundant calls. |
| AC-4 | Change notification fires exactly once per actual state transition with the new value. |
| AC-5 | Thread-safe under concurrent access (CAS-based, no locks). |
| AC-6 | All tests (TC-1 through TC-8) pass. |
| AC-7 | All existing project tests continue to pass. |
