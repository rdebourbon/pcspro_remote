# SPEC-S-004 — Debug Panel Toggle UI

| Field        | Value                                      |
|--------------|--------------------------------------------|
| **Status**   | APPROVED                                   |
| **Author**   | Copilot                                    |
| **Created**  | 2026-05-06                                 |
| **Governs**  | IS-019 S-004 / HLPS-019 SC-2, C-2a, C-3   |
| **Depends**  | S-001 (delivered)                          |

---

## Objective

Add an "Allow Date Selection" toggle button to the debug section component. Wire it to `IDateSelectionService.Enable()`/`Disable()`. Register the service in DI.

---

## Requirements

### R1 — DI Registration

Add `services.AddSingleton<IDateSelectionService, DateSelectionService>()` to `WebApplicationBuilderExtensions.AddPcsRemoteServices`, adjacent to the existing `IManualModeService` registration.

### R2 — Inject Service into DebugSection

Add `@inject IDateSelectionService DateSelectionService` to `DebugSection.razor`.

### R3 — Toggle Button

Add a button inside the `debug-section-content` div (after the Reset Automation section) with:
- Label: "Allow Date Selection: Off" / "Allow Date Selection: On" reflecting current state
- CSS class: `debug-section-date-toggle` (plus `active` modifier when enabled)
- Click handler calls `DateSelectionService.Enable()` or `DateSelectionService.Disable()` based on current state

### R4 — Button Only Visible When Unlocked

The toggle button is rendered inside the existing `@if (_expanded && _unlocked)` block, inheriting the debug section's PIN-gate (C-2). No additional gating logic is needed.

### R5 — Subscribe to Service Changes

Subscribe to `DateSelectionService.DateSelectionEnabledChanged` in `OnInitialized` to call `InvokeAsync(StateHasChanged)` when the toggle state changes externally (e.g., auto-reset from S-005). The handler must guard against `ObjectDisposedException` using `try { await InvokeAsync(StateHasChanged); } catch (ObjectDisposedException) { }` — consistent with the established pattern in `Index.razor` for singleton service event handlers (lines 297-298, 339-343). Unsubscribe in `Dispose`. Implement `IDisposable` on the component.

### R6 — bUnit Component Tests

Add bUnit tests in `PcsRemote.Web.Tests` to verify:
- **TC-1**: Toggle button renders inside the debug section when expanded and unlocked.
- **TC-2**: Clicking the toggle calls `Enable()`/`Disable()` on the service and updates the button label.
- **TC-3**: Button label reflects current service state ("On" vs "Off").
- **TC-4**: Toggle button is not rendered when the debug panel is locked (PIN-gated, C-2a).

Note: C-2b ("remains effective even if panel is later collapsed/locked") is verified in S-005 where the date picker visibility is tested independently of debug section state.

---

## Scope Exclusions

- The date picker UI and auto-reset logic belong to S-005 — this step only adds the toggle.
- CSS styling follows existing `debug-section-reset` patterns — no new stylesheet files.

---

## Verification

1. Build succeeds with 0 errors, 0 warnings.
2. `IDateSelectionService` resolves from DI at runtime (confirmed by component rendering without exception; build alone does not verify DI registration).
3. Toggle button renders inside the debug section when expanded and unlocked.
4. All existing tests pass; new bUnit tests (TC-1 through TC-4) pass.
