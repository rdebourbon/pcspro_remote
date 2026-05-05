# SPEC-S-005 — Date Picker UI and Auto-Reset

| Field        | Value                                              |
|--------------|----------------------------------------------------|
| **Status**   | APPROVED                                           |
| **Author**   | Copilot                                            |
| **Created**  | 2026-05-06                                         |
| **Governs**  | IS-019 S-005 / HLPS-019 SC-2–SC-5, SC-8, C-1, C-2b, C-4 |
| **Depends**  | S-001 (delivered), S-002 (delivered), S-004 (delivered) |

---

## Objective

Add a date picker and "Load Matches for Date" button to the main page, visible when the date selection toggle is enabled and the state renders the match-selection prompt. Implement auto-reset (disable the toggle on any entry to MatchSelection or MatchSelectionReady). Include single-match auto-select for date-based retrieval.

---

## Requirements

### R1 — Inject IDateSelectionService into Index.razor

Add `@inject IDateSelectionService DateSelectionService` to Index.razor.

### R2 — Subscribe to DateSelectionEnabledChanged

Subscribe in `OnInitializedAsync`, unsubscribe in `Dispose`. The handler calls `InvokeAsync(StateHasChanged)` with `ObjectDisposedException` guard, following the established pattern (see `OnManualModeChanged`, `OnOperationInProgressChanged`).

Additionally, snapshot `DateSelectionService.IsDateSelectionEnabled` in `OnInitializedAsync` into a local `_dateSelectionEnabled` field (same pattern as `_manualModeActive`). If the snapshot is `true` while the current state is `MatchSelection` or `MatchSelectionReady`, call `DateSelectionService.Disable()` to clear stale toggle state from a prior circuit.

### R3 — Date Picker UI

When the date selection toggle is enabled (`_dateSelectionEnabled`) AND the current state renders the match-selection prompt (i.e., `_currentState == PcsProState.MatchSelection || _currentState == PcsProState.MatchSelectionReady`, aligning with the existing template branch at line 18 of `Index.razor`) AND `_matches` is null (no results shown yet):

- Show a date input (`<input type="date">`) defaulting to today's date, with CSS class `date-selection-picker`.
- Show a "Load Matches for Date" button with CSS class `load-matches-date-button`, disabled when `_operationInProgress` or `_manualModeActive`.
- These controls appear alongside (not replacing) the existing "Load Today's Matches" and "Use Current Match" buttons (C-1).

### R4 — Date Picker Defaults to Today

The `_selectedDate` field initialises to `DateOnly.FromDateTime(DateTime.Today)` and is bound to the date input. This satisfies HLPS-019 U-1 (resolved: defaults to today).

### R5 — Load Matches for Date Button Handler

Clicking the button calls a new `FetchMatchesForDateAsync()` method that mirrors the existing `FetchMatchesAsync` pattern:
- Sets `_fetchInProgress = true` and `_operationInProgress = true`, clears `_matches`, calls `StateHasChanged()`.
- Calls `AutomationService.GetMatchesForDateAsync(_selectedDate)`.
- On success, stores the result in `_matches`.
- Applies auto-select when exactly one match is returned (same logic as existing `FetchMatchesAsync`, satisfying SC-8).
- On failure, logs the error.
- In `finally`: sets `_fetchInProgress = false`, restores `_operationInProgress` from `CoordinatorService.IsOperationInProgress`, calls `StateHasChanged()`.

### R6 — Auto-Reset on MatchSelection / MatchSelectionReady Entry

In the existing `OnStateChanged` handler, when `newState == PcsProState.MatchSelection` OR `newState == PcsProState.MatchSelectionReady`:
- Call `DateSelectionService.Disable()` to reset the toggle (SC-4, SC-5, C-4).
- Reset `_selectedDate` to `DateOnly.FromDateTime(DateTime.Today)` so the picker defaults fresh.
- Update the local `_dateSelectionEnabled` field to `false`.
- This auto-reset fires on ANY entry to MatchSelection or MatchSelectionReady, including re-entry after ChangeMatch, error recovery, restart, or direct mount into MatchSelectionReady.

**Architecture note:** The auto-reset lives in `Index.razor` (the UI layer) because `IDateSelectionService` in `PcsRemote.Core` has zero dependencies on the state machine or automation layer — it tracks only toggle state. Moving the reset into the service would violate the Core isolation rule. The `OnInitializedAsync` stale-state guard (R2) handles the edge case of a circuit reconnecting while the toggle is already enabled.

### R7 — Date Picker Visibility Independent of Debug Panel State (C-2b)

The date picker visibility depends ONLY on `_dateSelectionEnabled` and the state machine state. It does NOT check whether the debug panel is expanded or locked. This means: once the toggle is enabled via the debug panel, the date picker remains visible even if the debug panel is subsequently collapsed or locked. The auto-reset (R6) is the mechanism that disables the feature.

### R8 — Update Existing Test Helpers

Update `BuildCtx` and `BuildCtxWithStream` in `IndexTests.cs` to register a default `Mock<IDateSelectionService>` with `IsDateSelectionEnabled` returning `false`. This ensures all existing tests continue to pass without modification. The mock must be accessible in the returned tuple for new tests to configure.

### R9 — bUnit Tests

Add tests in `PcsRemote.Web.Tests/Pages/IndexTests.cs`:
- **TC-1**: Date picker and button are hidden when `IsDateSelectionEnabled` is false.
- **TC-2**: Date picker and button are visible when `IsDateSelectionEnabled` is true and state is MatchSelection.
- **TC-2b**: Date picker and button are visible when `IsDateSelectionEnabled` is true and state is MatchSelectionReady.
- **TC-3**: Date picker defaults to today's date.
- **TC-4a**: Auto-reset — raising `StateChanged` with `MatchSelection` calls `Disable()` on the service.
- **TC-4b**: Auto-reset — raising `StateChanged` with `MatchSelectionReady` calls `Disable()` on the service.
- **TC-5**: Date picker remains visible when `IsDateSelectionEnabled` is true regardless of debug panel expand/lock state (C-2b).
- **TC-6**: Single-match auto-select — when `GetMatchesForDateAsync` returns exactly 1 match, `LoadMatchAsync` is called automatically (SC-8).
- **TC-7**: Selected date is forwarded — clicking "Load Matches for Date" calls `GetMatchesForDateAsync` with the value from the date picker, not today's date (SC-3).

---

## Scope Exclusions

- CSS styling follows existing patterns — no new stylesheet files.
- The date picker is a native HTML `<input type="date">` — no third-party date picker library.
- Keyboard/accessibility behaviour inherits from the browser's native date input.

---

## Review History

### Round 1 — Opus 4.6 + GPT 5.4 — REVISE

| # | Reviewer | Severity | Finding | Disposition |
|---|----------|----------|---------|-------------|
| G1 | GPT 5.4 | MAJOR | Auto-reset in UI layer risks stale singleton state if circuit disconnects before MatchSelection fires | REJECT — Core cannot depend on state machine (architecture rule). Added stale-state guard in R2 `OnInitializedAsync` to handle reconnecting circuits. |
| G2 | GPT 5.4 | MEDIUM | Visibility condition should include MatchSelectionReady, not just MatchSelection | ACCEPT — R3 updated to match existing template branch (`MatchSelection \|\| MatchSelectionReady`). |
| G3 | GPT 5.4 | MEDIUM | Test list doesn't explicitly cover C-2b and SC-8 | ACCEPT — Added TC-5 (C-2b) and TC-6 (SC-8) to R9. |
| O1 | Opus 4.6 | MAJOR | Missing IDateSelectionService DI in BuildCtx/BuildCtxWithStream — all existing tests will break | ACCEPT — Added R8 requiring test helper updates. |
| O2 | Opus 4.6 | MEDIUM | Same as G2 — MatchSelectionReady visibility | ACCEPT — same fix as G2. |
| O3 | Opus 4.6 | MEDIUM | Auto-reset should also fire on MatchSelectionReady entry | ACCEPT — R6 now covers both MatchSelection and MatchSelectionReady. |
| O4 | Opus 4.6 | MINOR | Missing `_operationInProgress = true` in FetchMatchesForDateAsync | ACCEPT — R5 now mirrors the full FetchMatchesAsync pattern. |

### Round 2 — Opus 4.6 + GPT 5.4

- **Opus 4.6:** APPROVE — all Round 1 findings adequately addressed, no new issues.
- **GPT 5.4:** REVISE — R9 test cases didn't fully cover the MatchSelectionReady state or date forwarding.

| # | Reviewer | Severity | Finding | Disposition |
|---|----------|----------|---------|-------------|
| G4 | GPT 5.4 | MEDIUM | TC-2 only tests MatchSelection visibility, not MatchSelectionReady; TC-4 only tests MatchSelection auto-reset; no test verifies selected date is forwarded to GetMatchesForDateAsync (SC-3) | ACCEPT — Added TC-2b, TC-4b, and TC-7. |

---

## Verification

1. Build succeeds with 0 errors, 0 warnings.
2. Date picker and button visible only when toggle on + MatchSelection/MatchSelectionReady state + no matches loaded.
3. Auto-reset fires on MatchSelection and MatchSelectionReady entry.
4. All existing tests pass (including those using updated BuildCtx/BuildCtxWithStream); new bUnit tests (TC-1 through TC-7) pass.
5. Existing "Load Today's Matches" and "Use Current Match" buttons remain unchanged (C-1).
6. Date picker remains visible when debug panel collapsed/locked (C-2b).
7. Single-match auto-select works for date-based retrieval (SC-8).
