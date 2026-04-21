# SPEC-S-004: Match Selection and Row Parsing

| Field | Value |
|---|---|
| **Document** | SPEC-S-004-Match-Selection-Row-Parsing.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-22 |
| **Step ID** | S-004 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-004-match-selection` |

---

## 1. Purpose

Replace the `FlaUiMatchSelectionAutomation` stub and `MatchRowParser.TryParse` stub with working implementations ported from the diagnostic tool's Steps 4–6. Add a `SiteName` configuration property to scope match searches to the correct club.

Refer to `tools/AutomationDiagnostic/DiagnosticRunner.cs` (`Step4_WaitForMatchSelection`, `Step5_SearchForMatches`, `Step6_SelectFirstMatch`) for the proven patterns.

---

## 2. Scope

### 2.1 KnownElements Additions

Add missing match-selection-phase constants that the diagnostic tool uses by name/class but are not yet in `KnownElements.cs`:

- R1: `ClearFiltersLinkName` — the "Clear Filters" hyperlink-style link found by Name in the match selection dialog.
- R2: `DatePickerClassName` — the WPF DatePicker class name (`"DatePicker"`), used to locate the Date From and Date To controls.
- R3: `DatePickerTextBoxAutomationId` — the `PART_TextBox` inner element of each DatePicker, used for keyboard text entry.
- R3a: **Correct the stale spinner comment** (lines 47–51) that incorrectly states the `LoaderSpinner` is always present and uses `IsOffscreen` toggling. The diagnostic tool's spinner discovery confirmed the spinner is **dynamically added/removed** from the tree — not toggled via `IsOffscreen`. Update the comment to match the diagnostic finding.

### 2.2 PcsProOptions Addition

- R4: Add an optional `SiteName` string property to `PcsProOptions` with a default of empty string. When `SiteName` is empty or null, the site filter step in `OpenMatchDialogAndSearch` is skipped (no site filtering). When provided, it is the site/club name used to filter the match selection ComboBox (e.g., `"Ashtead CC"`). This aligns with IS-010's "sensible default" requirement.

### 2.3 Modified Class: `FlaUiMatchSelectionAutomation`

- R5: Constructor accepts `PcsProWindowLocator`, `ILogger<FlaUiMatchSelectionAutomation>`, and `IOptions<PcsProOptions>` (all injected via DI). This follows the pattern established in S-003.
- R6: Remove the two local `TODO_REPLACE_ON_GARAGE_PC` constants — use `KnownElements` constants instead.
- R7: **`OpenMatchDialogAndSearch()`** — Performs the full filter setup sequence:
  1. Find the main window via `PcsProWindowLocator`.
  2. Open the File menu (`KnownElements.FileMenuAutomationId`), then click Open Match (`KnownElements.OpenMatchMenuItemAutomationId`). Poll until the Open Match dialog appears (identified by `Name = KnownElements.MatchSelectionDialogName`).
  3. Click "Clear Filters" link (by Name).
  4. If `SiteName` is configured (non-empty): find the site ComboBox (the first ComboBox in the dialog with no AutomationId — per diagnostic tool discovery, there is no unique AutomationId for this control), expand it, find and click the item matching `SiteName` from options. Wait for the intermediate spinner to clear before proceeding.
  5. Set Date From to today's date (`DateTime.Today`, local time — PCS Pro is a local desktop application) using keyboard entry into the DatePicker's `PART_TextBox` (Ctrl+A, type date string in `dd/MM/yyyy` format, Tab to commit). Wait for the intermediate spinner to clear.
  6. Set Date To to today's date using the same keyboard entry pattern. Tab to commit.
  7. Return after setting Date To — the service layer polls `IsSpinnerVisible()` for the final search completion.
  
  Each intermediate spinner wait uses a polling loop: check `IsSpinnerVisible()` at ~300ms intervals with a 30-second timeout (matching the diagnostic tool's `WaitForSpinnerIdle` parameters). Include brief pauses (~200ms) between keyboard operations per diagnostic tool timing pattern.
  
  Throw on any element-not-found failure (menu, dialog, site item). The service catches and transitions to error state.

- R8: **`IsSpinnerVisible()`** — Find the Open Match dialog, then search for a descendant with `ClassName = KnownElements.LoaderSpinnerClassName`. The spinner is dynamically added/removed from the tree (not toggled via IsOffscreen) per diagnostic tool discovery. Return `true` if found. Must not throw — wrap all FlaUI calls in try/catch, return `false` on any exception.

- R9: **`IsUnexpectedDialogPresent()`** — Same classification pattern as `FlaUiLoginAutomation`: find all child Windows by `ControlType.Window`, exclude match selection dialog (by Name), exclude login dialog (by password field descendant). Return `true` if any remaining child Windows exist. Must not throw.

- R10: **`TryCloseUnexpectedDialog()`** — Same button search pattern as `FlaUiLoginAutomation`: Cancel → Close → OK → any button. Best-effort, no throw.

- R11: **`ReadDataGridRowTexts()`** — Find the DataGrid by `KnownElements.MatchDataGridAutomationId` within the Open Match dialog. Find all `ControlType.DataItem` descendant rows. For each row, read all `ControlType.Text` **descendant** elements (the visible cell values — these are nested inside DataGridCell containers, not direct children), join their Names with a pipe (`|`) delimiter. Return the list of pipe-delimited row strings. Throw if the DataGrid cannot be found.

- R12: **`SelectAndOpenMatch(MatchInfo match)`** — Re-read the DataGrid rows. Find the row whose Team 1, Team 2, and Match Type cell values match `match.HomeTeam`, `match.AwayTeam`, and `match.MatchType` respectively. Select the row via `SelectionItemPattern` (with Click fallback per diagnostic tool's `SelectDataGridRow` pattern). Then find and invoke the "Open Read Only" button (`KnownElements.OpenReadOnlyButtonAutomationId`) using `UIAutomationHelpers.InvokeButtonSafely`. Throw if the row cannot be matched or the button cannot be found.

- R12a: Update the `IMatchSelectionAutomation.SelectAndOpenMatch` XML doc to clarify that the row is located by team name and match type matching (not by `MatchId`), since `MatchId` is a synthesized composite key not present in the grid.

- R13: **`IsMatchLoaded()`** — Find the main window and search for the score summary pane (`KnownElements.ScoreSummaryPaneAutomationId`). Its presence indicates a match is loaded. Must not throw — return `false` on any exception.

### 2.4 Modified Class: `MatchRowParser`

- R14: Remove the `RowFormatPattern` TODO constant and its suppression line.
- R15: **`TryParse(string rowText, out MatchInfo? result)`** — Parse the pipe-delimited string produced by `ReadDataGridRowTexts`. Split by `|`, expect at least 5 segments (Date, Team 1, Team 2, Competition, Match Type). Parse the date segment (index 0) as `DateOnly` using `dd/MM/yyyy` format with `CultureInfo("en-GB")`. Construct a `MatchInfo` with:
  - `MatchId` synthesized as `"{MatchDate:yyyy-MM-dd}_{HomeTeam}_{AwayTeam}_{MatchType}"` (deterministic composite key including match type to avoid ambiguity when the same teams have multiple fixture types on the same day).
  - `HomeTeam` from segment at `KnownElements.GridColumnTeam1`.
  - `AwayTeam` from segment at `KnownElements.GridColumnTeam2`.
  - `MatchType` from segment index 4.
  - `MatchDate` from segment index 0.
  
  Return `false` on any failure (empty input, too few segments, date parse failure). Never throw.

- R15a: Update the `MatchInfo.MatchId` XML doc parameter description to clarify that the value is a synthesized composite key (not a PlayCricket fixture identifier) when populated by `MatchRowParser`.

### 2.5 DI Registration Update

- R16: Register `FlaUiMatchSelectionAutomation` now depends on `PcsProWindowLocator` (already registered in S-003) and `IOptions<PcsProOptions>` (already registered). No new DI registration is needed beyond the existing `services.AddSingleton<IMatchSelectionAutomation, FlaUiMatchSelectionAutomation>()`.

### 2.6 Out of Scope

- Change match flow (`FlaUiChangeMatchAutomation`) — deferred to S-007.
- Structured team name return type — deferred to S-010.
- Server connection verification ("Connected to Server" check) — a defensive enhancement that can be added later. The service's timeout handling covers the failure case. See R-5 in Risks.
- Date format configuration — PCS Pro is a UK application using `dd/MM/yyyy` format. Hardcoding the format in the parser is acceptable.
- Date source alignment between UI filter (`DateTime.Today`, local) and service-layer `FilterToday` (`TimeProvider.GetUtcNow()`, UTC) — this is a pre-existing concern in the service layer, not introduced by S-004.

---

## 3. Test Strategy

- **T1: Build verification** — 0 warnings, 0 errors.
- **T2: Existing tests remain green** — No behavioural changes to service-layer tests (they use `FakeMatchSelectionAutomation`).
- **T3: MatchRowParser.TryParse unit tests** — Test cases covering:
  - Valid row with all fields → returns `true`, correct MatchInfo fields.
  - Empty/whitespace input → returns `false`.
  - Too few pipe-delimited segments → returns `false`.
  - Invalid date segment → returns `false`.
  - Synthesized MatchId format is deterministic and correct.
- **T4: No unit tests for FlaUiMatchSelectionAutomation** — FlaUI interactions require a live PCS Pro instance. Verified via garage PC end-to-end walkthrough (H-SC-3).

---

## 4. Acceptance Criteria

- AC-1: `KnownElements.cs` includes `ClearFiltersLinkName`, `DatePickerClassName`, and `DatePickerTextBoxAutomationId`. The stale `LoaderSpinner` IsOffscreen comment is corrected to reflect dynamic add/remove behaviour.
- AC-2: `PcsProOptions.SiteName` property exists with empty-string default.
- AC-3: `FlaUiMatchSelectionAutomation` constructor accepts `PcsProWindowLocator`, `ILogger`, and `IOptions<PcsProOptions>`.
- AC-4: Zero `TODO_REPLACE_ON_GARAGE_PC` constants remain in `FlaUiMatchSelectionAutomation.cs` and `MatchRowParser.cs`.
- AC-5: All seven `IMatchSelectionAutomation` methods are implemented (no `NotImplementedException`).
- AC-6: `OpenMatchDialogAndSearch` navigates File → Open Match, optionally sets site filter, sets date filters with intermediate spinner waits between each filter change, using `DateTime.Today` for the date value.
- AC-7: `IsSpinnerVisible` detects the `LoaderSpinner` by ClassName within the match selection dialog. Must not throw — returns `false` on any exception.
- AC-8: `ReadDataGridRowTexts` returns pipe-delimited cell values (from Text descendants) for each DataItem row in the grid.
- AC-9: `MatchRowParser.TryParse` correctly extracts MatchDate (parsed as `dd/MM/yyyy` with en-GB culture), HomeTeam, AwayTeam, MatchType, and synthesized MatchId (including MatchType in composite key) from pipe-delimited input.
- AC-10: `SelectAndOpenMatch` finds the matching row by team names and match type, selects it, and invokes "Open Read Only". `IMatchSelectionAutomation.SelectAndOpenMatch` XML doc is updated to reflect team-name matching (not MatchId).
- AC-11: `IsMatchLoaded` detects the score summary pane as a match-loaded signal. Must not throw — returns `false` on any exception.
- AC-12: MatchRowParser has unit tests covering valid parse, empty input, insufficient segments, and invalid date.
- AC-13: `IsUnexpectedDialogPresent` and `TryCloseUnexpectedDialog` follow non-throwing probe semantics with the same classification pattern as `FlaUiLoginAutomation`.
- AC-14: Build: 0 warnings, 0 errors. All existing tests pass.

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | Site ComboBox has no AutomationId — identification by position (first ComboBox with empty AutomationId) is fragile | This is the proven pattern from the diagnostic tool. If PCS Pro adds another unidentified ComboBox, the implementation would need a discovery pass. |
| R-2 | Date format in the DataGrid may not match the expected parse format | The parser uses flexible date parsing. The diagnostic tool logs show the actual format from the garage PC. |
| R-3 | `OpenMatchDialogAndSearch` blocks for the duration of filter setup including intermediate spinner waits | Expected behaviour — all FlaUI calls are serialized via the service's operation lock. Total time is bounded by the spinner timeout (30s per spinner wait). |
| R-4 | Pipe delimiter in cell values could cause parse failures | Cricket team names and match types do not contain pipe characters. This is a safe delimiter choice. |
| R-5 | Server connection check is not verified before match search. If PCS Pro loses its server connection, the match selection search may return stale or empty results without an obvious error. | Excluded from S-004 scope. The service-layer timeout and spinner polling provide indirect failure detection. A dedicated server-connection-status probe can be added in a future step if needed. |
