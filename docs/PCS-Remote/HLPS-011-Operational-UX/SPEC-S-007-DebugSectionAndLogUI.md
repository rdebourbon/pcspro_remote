# SPEC-S-007 — Debug Section and Automation Log UI

**IS Step:** S-007
**Version:** 0.1
**Status:** DRAFT

---

## Scope

Add a collapsible debug/advanced section to the main layout, gated by an optional PIN. Inside it, render a live automation log display that subscribes to the automation log service (S-006). PIN and expand/collapse state are per-circuit (lost on page refresh).

---

## Requirements

### R-1 — DebugSectionOptions

A new options class `DebugSectionOptions` in `PcsRemote.Web` with a single `string? Pin` property. Bound to `DebugSection:Pin` in `appsettings.json` via `Configure<DebugSectionOptions>`. Injected as `IOptions<DebugSectionOptions>`.

### R-2 — DebugSection component

A new `DebugSection.razor` component in `PcsRemote.Web/Shared`. Behaviour:
- **Collapsed by default** (OUX-C-1).
- A toggle button labelled "Debug" expands/collapses the section.
- When PIN is configured (non-null, non-empty), first expand prompts for PIN entry.
- When no PIN is configured, section opens freely (OUX-U-6).
- Correct PIN unlocks the section for the circuit lifetime (OUX-U-5).
- Incorrect PIN shows a brief error message; section remains locked.
- Expand/collapse state persists across re-renders within the same circuit — reopening after a re-render does not reset to collapsed.

### R-3 — AutomationLogDisplay component

A new `AutomationLogDisplay.razor` component in `PcsRemote.Web/Shared`. Placed inside `DebugSection`. Behaviour:
- Injects `IAutomationLogService`.
- On initialisation, loads the current buffer via `GetRecentEntries()` (late-joiner support).
- Subscribes to `EntryAdded` for real-time updates (subscribe-before-snapshot pattern).
- Renders entries as a timestamped list, newest at bottom.
- Caps browser-side display at 200 entries (matching server buffer cap). When a new entry arrives and the display is at capacity, the oldest display entry is removed.
- Each entry shows: timestamp (local time, `HH:mm:ss` format), action text, and outcome badge (`Info`/`Success`/`Failure`).

### R-4 — MainLayout integration

`MainLayout.razor` includes `<DebugSection />` in the body area (after `@Body`).

### R-5 — Configuration

`appsettings.json` gains a `DebugSection` object with a `Pin` property defaulting to `null` (no PIN).

---

## Implementation Notes

- **PIN storage:** The PIN is a plain string in config, not a credential (per OUX-C-2). No hashing.
- **Circuit lifetime:** Blazor Server circuits persist for the browser tab lifetime. Component `@code` state survives re-renders (StateHasChanged) but not page navigation/refresh. This gives the correct per-circuit PIN unlock behaviour without additional infrastructure.
- **AutomationLogDisplay pattern:** Same subscribe-before-snapshot + IDisposable pattern as ManualModeBanner, OperationStatusBanner.
- **Display cap enforcement:** Maintain a `List<AutomationLogEntry>` capped at 200. On new entry, if at capacity, `RemoveAt(0)` then `Add`.

---

## Test Cases

| ID | Category | Scenario | Expectation |
|---|---|---|---|
| TC-1 | DebugSection | Default state | Section is collapsed, no content visible |
| TC-2 | DebugSection | No PIN configured — toggle | Section expands on click, no PIN prompt |
| TC-3 | DebugSection | PIN configured — toggle | PIN prompt appears on first expand |
| TC-4 | DebugSection | Correct PIN | Section unlocks and expands |
| TC-5 | DebugSection | Incorrect PIN | Error message shown, section stays locked |
| TC-6 | DebugSection | Unlock persists across re-renders | After unlock, section stays unlocked through StateHasChanged |
| TC-7 | DebugSection | Collapse and re-expand | After unlock, collapse and re-expand does not re-prompt for PIN |
| TC-8 | LogDisplay | Initial load | Entries from GetRecentEntries displayed |
| TC-9 | LogDisplay | Real-time entry | New entry appears when EntryAdded fires |
| TC-10 | LogDisplay | Entry format | Each entry shows timestamp, action, outcome |
| TC-11 | LogDisplay | Display cap | At 200 entries, adding 201st removes oldest |

---

## Acceptance Criteria

1. Debug section collapsed by default.
2. PIN prompt when PIN configured; free access when not.
3. Correct PIN unlocks for circuit lifetime.
4. Incorrect PIN shows error, stays locked.
5. Automation log displays entries with timestamp, action, outcome.
6. Real-time entry updates via EntryAdded subscription.
7. Browser-side display capped at 200 entries.
8. All existing tests pass (469+).
9. Build produces 0 errors, 0 warnings.
