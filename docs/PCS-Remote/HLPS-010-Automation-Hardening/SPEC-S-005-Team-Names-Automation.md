# SPEC-S-005: Team Names Automation

| Field | Value |
|---|---|
| **Document** | SPEC-S-005-Team-Names-Automation.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-22 |
| **Step ID** | S-005 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-005-team-names` |

---

## 1. Purpose

Replace the `FlaUiTeamNamesAutomation` stub with a working implementation ported from the diagnostic tool's Step 8 (`Step8_FindTeamNameElements`). The implementation navigates to Scoring → Match Details/Teams..., reads club and team ComboBox values for both teams, and closes the dialog.

Refer to `tools/AutomationDiagnostic/DiagnosticRunner.cs` (`Step8_FindTeamNameElements`, `ReadComboBoxValue`, `CloseMatchDetailsDialog`) for the proven patterns.

---

## 2. Scope

### 2.1 Modified Class: `FlaUiTeamNamesAutomation`

- R1: Constructor accepts `PcsProWindowLocator`, `ILogger<FlaUiTeamNamesAutomation>`. No configuration options needed (team names are read-only).
- R2: Remove the three local `TODO_REPLACE_ON_GARAGE_PC` constants — use `KnownElements` constants instead.
- R3: **`OpenTeamsDialog()`** — Performs the navigation sequence:
  1. Find the main window via `PcsProWindowLocator`.
  2. Click the Scoring menu (`KnownElements.ScoringMenuAutomationId`).
  3. Find and click "Match Details/Teams..." menu item by Name (`KnownElements.MatchDetailsMenuItemName`). Use `WaitForElement` with a short timeout (~2s) to locate the menu item after the popup appears. Use a brief pause (~300ms) after clicking the menu for the popup to render.
  4. Wait for the Match Details/Teams dialog to appear (identified by `Name = KnownElements.MatchDetailsDialogName`) with a 5-second timeout.
  5. Throw if any element is not found or the dialog does not appear.

- R4: **`ReadHomeTeamName()`** — Read the home team name:
  1. Find the Match Details dialog by Name.
  2. Find all `MatchTeamView` descendants by ClassName (`KnownElements.MatchTeamViewClassName`). Expect at least 2 — first is home, second is away.
  3. In the first `MatchTeamView`, find the Team ComboBox (`KnownElements.TeamComboBoxAutomationId`).
  4. Read its value using the multi-strategy pattern from the diagnostic tool: (a) try `ValuePattern`, (b) try `Name` property, (c) try `SelectedItem.Name`.
  5. Return the team name string. Throw if value cannot be read.

- R5: **`ReadAwayTeamName()`** — Same as R4 but using the second `MatchTeamView` element.

- R6: **`TryCloseTeamsDialog()`** — Best-effort dialog close:
  1. Find the Match Details dialog by Name in the main window.
  2. Find the OK button by `KnownElements.MatchDetailsOkButtonAutomationId`.
  3. Invoke it using `UIAutomationHelpers.InvokeButtonSafely`.
  4. Never throw — wrap in try/catch.

- R7: **`IsUnexpectedDialogPresent()`** — Delegates to the shared `UIAutomationHelpers.HasUnexpectedDialog` helper which classifies all child windows against known dialog names and the login password field. Must not throw.

- R8: **`TryCloseUnexpectedDialog()`** — Delegates to the shared `UIAutomationHelpers.TryCloseFirstUnexpectedDialog` helper. Best-effort, no throw.

### 2.2 Out of Scope

- Structured team name return type (club vs team) — deferred to S-010.
- Team name caching or validation — the service layer handles return values.

---

## 3. Test Strategy

- **T1: Build verification** — 0 warnings, 0 errors.
- **T2: Existing tests remain green** — No behavioural changes to service-layer tests (they use `FakeTeamNamesAutomation`).
- **T3: No unit tests for FlaUiTeamNamesAutomation** — FlaUI interactions require a live PCS Pro instance. Manual verification deferred to garage PC walkthrough (see H-SC-3).

---

## 4. Acceptance Criteria

- AC-1: Zero `TODO_REPLACE_ON_GARAGE_PC` constants remain in `FlaUiTeamNamesAutomation.cs`.
- AC-2: All six `ITeamNamesAutomation` methods are implemented (no `NotImplementedException`).
- AC-3: `FlaUiTeamNamesAutomation` constructor accepts `PcsProWindowLocator` and `ILogger`.
- AC-4: `OpenTeamsDialog` navigates Scoring → Match Details/Teams... and waits for dialog.
- AC-5: `ReadHomeTeamName` and `ReadAwayTeamName` use multi-strategy ComboBox value reading (ValuePattern → Name → SelectedItem).
- AC-6: `TryCloseTeamsDialog` closes the dialog via OK button. Must not throw.
- AC-7: `IsUnexpectedDialogPresent` excludes Match Details, match selection, and login dialogs. Must not throw.
- AC-8: Build: 0 warnings, 0 errors. All existing tests pass.

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | The order of MatchTeamView elements (home = first, away = second) may not hold for all match types | This is the consistent pattern observed in the diagnostic tool. If reversed for certain fixtures, a future step can add validation. |
| R-2 | ComboBox value reading strategy may fail if PCS Pro changes its ComboBox implementation | The three-strategy fallback (ValuePattern → Name → SelectedItem) covers all known WPF patterns. |
