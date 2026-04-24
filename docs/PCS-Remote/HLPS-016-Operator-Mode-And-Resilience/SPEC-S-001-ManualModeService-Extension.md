# SPEC-S-001: `IManualModeService` Contract Extension and Concrete Relocation

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-ManualModeService-Extension.md |
| **Status** | APPROVED |
| **Version** | 0.4 |
| **Date** | 2026-04-24 |
| **Step ID** | S-001 |
| **Governing HLPS** | HLPS-016-Operator-Mode-And-Resilience.md (APPROVED v0.4) |
| **Governing IS** | IS-016-Operator-Mode-And-Resilience.md (APPROVED v0.5) |
| **Branch** | `feature/016-S-001-manual-mode-toggle` |

---

## 1. Purpose

This step delivers two changes to the manual-mode infrastructure:

1. **Interface extension:** Add a `Toggle()` method to `IManualModeService` so the global hotkey handler (S-008) can switch mode with a single call without read-then-branch logic. The method must be thread-safe, matching the existing `Enable()`/`Disable()` atomicity pattern.

2. **Concrete relocation:** Move `ManualModeService` from `PcsRemote.Web.Services` to `PcsRemote.Core`. The class has no Web-layer dependencies. Moving it aligns with the architecture rule that Core has zero dependencies on Web/Automation/TrayHost and co-locates the concrete with its interface.

---

## 2. Scope

### 2.1 Interface Extension

**Requirements:**

- R1: Add `void Toggle()` to `IManualModeService`. The method atomically flips the active flag and raises `ManualModeChanged` exactly once per successful transition. If a concurrent toggle on another thread wins first, the losing thread retries (same pattern as existing `Enable()`/`Disable()`).
- R2: XML documentation on `Toggle()` must describe its atomicity semantics and event-firing behaviour, consistent with existing interface member documentation style.
- R3: `Toggle()` must raise `ManualModeChanged` with the **new** value of `IsManualModeActive` (consistent with `Enable()`/`Disable()` event semantics).
- R4: Update the XML documentation on `ManualModeChanged` to mention that `Toggle()` also triggers the event (currently only references `Enable()` and `Disable()`).

### 2.2 Concrete Relocation

**Requirements:**

- R5: Move the `ManualModeService` source file from `src/PcsRemote.Web/Services/` to `src/PcsRemote.Core/`. Change the namespace to `PcsRemote.Core`.
- R6: Update the DI registration to reference the Core-located type. The registration lifetime (singleton) must not change.
- R7: Remove the old source file from `src/PcsRemote.Web/Services/`.
- R8: Fix any namespace import statements broken by the move. The DI registration, `ManualModeServiceTests`, and the E2E test factory all reference the concrete type directly and must be updated.

### 2.3 Out of Scope

- Wiring `IManualModeService` into `ScoreboardPollingService` or `PcsProAutomationService` (S-006).
- Creating the hotkey handler (S-008).
- Changing any Blazor component (S-007).

---

## 3. Test Strategy

### 3.1 New Tests (in `PcsRemote.Core.Tests` — co-located with the relocated concrete)

- **T1 — `Toggle()` inactive → active:** Call `Toggle()` when `IsManualModeActive` is `false`. Assert state becomes `true` and `ManualModeChanged` was raised with `true`.
- **T2 — `Toggle()` active → inactive:** Call `Toggle()` when `IsManualModeActive` is `true`. Assert state becomes `false` and `ManualModeChanged` was raised with `false`.
- **T3 — `Toggle()` round-trip:** Call `Toggle()` twice. Assert state returns to original and two events were raised with alternating values.
- **T4 — Thread-safety smoke:** Spawn N threads each calling `Toggle()` once, concurrently. Assert: (a) final state is deterministic (even N → original state, odd N → toggled state), (b) exactly N events were fired (each toggle retries until it wins, so every call completes), and (c) no exceptions are thrown.

### 3.2 Interface Shape Test (in `PcsRemote.Core.Tests`)

- **T5 — `Toggle()` method exists on interface:** Reflection-based test (matching existing TC-3/TC-4 pattern) verifying `Toggle()` exists, returns void, and takes no parameters.

### 3.3 Existing Test Relocation

- **T6 — Move `ManualModeServiceTests` to `PcsRemote.Core.Tests`:** Since the concrete type is now in Core, its behavioural tests belong in Core.Tests. Move the test file, update its namespace and import. All existing tests (TC-1 through TC-7b) must pass unchanged after relocation.

---

## 4. Acceptance Criteria

| ID | Criterion | Verification |
|----|-----------|-------------|
| AC-1 | `IManualModeService.Toggle()` exists with XML documentation | Code review + T5 |
| AC-2 | `Toggle()` is thread-safe (atomic state flip, single event per transition) | Code review + T4 |
| AC-3 | `ManualModeService` resides in `PcsRemote.Core` namespace | Build + T6 |
| AC-4 | Old file `src/PcsRemote.Web/Services/ManualModeService.cs` is deleted | File system check |
| AC-5 | DI registration updated — application builds without error | Build |
| AC-6 | `ManualModeChanged` XML doc updated to reference `Toggle()` | Code review |
| AC-7 | All existing tests pass after relocation (no regressions) | T6 — test run |
| AC-8 | New `Toggle()` tests pass | T1–T5 |

---

## 5. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Namespace change breaks an import somewhere | Low | Low | Mechanical — caught by compiler. All consumers inject the interface, not the concrete. |
| Toggle has subtle ordering with concurrent `Enable()`/`Disable()` calls | Low | Low | The existing `Enable()`/`Disable()` pattern is already atomic. Toggle uses the same primitive. The state machine alternation invariant holds regardless of which method triggers the transition. |

---

## 6. Documentation Updates

- No external documentation changes. Two interface XML doc updates are required: `Toggle()` documentation per R2, and `ManualModeChanged` documentation per R4.

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-25 | Sonnet 4.6, GPT 5.4 | REVISE — 2 MEDIUM (R7 incorrect concrete-reference scope; method name not specified), 2 LOW (T4 event count, test placement after relocation). All accepted. Resolved in v0.3. |
| R2 | 2026-04-25 | Sonnet 4.6, GPT 5.4 | REVISE — 1 LOW (§6 not updated for R4), 1 MEDIUM (E2E factory also references concrete). Both accepted. Resolved in v0.4. |
| R3 | 2026-04-25 | Sonnet 4.6, GPT 5.4 | APPROVE — unanimous approval achieved. |
