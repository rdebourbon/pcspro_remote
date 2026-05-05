# SPEC-S-003 — Remove Config-Based Date Override

| Field        | Value                                      |
|--------------|--------------------------------------------|
| **Status**   | APPROVED                                   |
| **Author**   | Copilot                                    |
| **Created**  | 2026-05-06                                 |
| **Governs**  | IS-019 S-003 / HLPS-019 SC-6, C-6         |
| **Depends**  | S-002 (delivered)                          |

---

## Objective

Remove the `TestDateOverride` configuration key, the `FixedDateTimeProvider` class, and the parameterless `OpenMatchDialogAndSearch()` overload that is no longer called. Simplify DI registration to always use `TimeProvider.System`.

---

## Requirements

### R1 — Delete `FixedDateTimeProvider`

Delete `src/PcsRemote.Automation/FixedDateTimeProvider.cs` entirely.

### R2 — Remove `TestDateOverride` config key

Remove the `"TestDateOverride": "2025-06-01"` entry from `src/PcsRemote.Web/appsettings.json`.

### R3 — Simplify DI registration

In `AutomationServiceCollectionExtensions.AddPcsProAutomation`:
- Remove the `TestDateOverride` config read and the `FixedDateTimeProvider` conditional branch.
- Replace with unconditional `services.AddSingleton(TimeProvider.System)`.

### R4 — Remove parameterless `OpenMatchDialogAndSearch()`

Verified: `PcsProAutomationService` calls only the `DateOnly` overload (`OpenMatchDialogAndSearch(searchDate)` at line 1041). The parameterless overload has no remaining callers.

Remove the parameterless `OpenMatchDialogAndSearch()` from:
- `IMatchSelectionAutomation` interface
- `FlaUiMatchSelectionAutomation` implementation
- `FakeMatchSelectionAutomation` test fake
- `SpinnerDropsAfterNCallsFake` and `ReadDataGridThrowsFake` inline test fakes

### R5 — Remove `TimeProvider` from `FlaUiMatchSelectionAutomation`

Remove the `TimeProvider` constructor parameter and `_timeProvider` field from `FlaUiMatchSelectionAutomation`. Update the constructor call site in `AutomationServiceCollectionExtensions` if it passes `TimeProvider` explicitly (verify — it may use DI auto-resolution).

Note: The `services.AddSingleton(TimeProvider.System)` registration from R3 is still required — `PcsProAutomationService` resolves `TimeProvider` via DI for timing operations.

### R6 — No test behaviour changes

All existing tests must continue to pass. No new tests are required — this step is purely removal. The `FakeTimeProvider` used in `PcsProAutomationServiceTests` is unaffected (it provides timing for the service, not date derivation).

---

## Scope Exclusions

- The `TimeProvider` dependency in `PcsProAutomationService` is **retained** — it is used for `GetTimestamp`, `GetElapsedTime`, and `Task.Delay` throughout the service.
- The parameterless `OpenMatchDialogAndSearch()` in the test fakes' callers is not called anywhere in production code; removal is safe.
- Older spec documents referencing the parameterless overload (e.g., SPEC-S-004-MatchSelection.md from HLPS-006) become stale after this step. Updating those documents is deferred to S-006 (Documentation & Cleanup).

---

## Verification

1. Build succeeds with 0 errors, 0 warnings.
2. `grep -r "TestDateOverride" src/ docs/` returns no results in source code (doc references are deferred to S-006).
3. `grep -r "FixedDateTimeProvider" src/` returns no results.
4. No parameterless `OpenMatchDialogAndSearch()` in `IMatchSelectionAutomation` or implementations.
5. All existing Automation and Core tests pass.
