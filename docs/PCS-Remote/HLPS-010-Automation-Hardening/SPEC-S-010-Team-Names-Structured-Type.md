# SPEC-S-010: Team Names Structured Return Type

| Field | Value |
|---|---|
| **Document** | SPEC-S-010-Team-Names-Structured-Type.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-21 |
| **Step ID** | S-010 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-010-team-names-structured-type` |

---

## 1. Problem

The `ITeamNamesAutomation` interface (internal to `PcsRemote.Automation`) returns bare `string` values from `ReadHomeTeamName()` and `ReadAwayTeamName()`. The diagnostic tool proved that PCS Pro's Match Details dialog exposes **two** ComboBoxes per team: `cboClub` (e.g., "Northampton Town CC") and `cbxTeam` (e.g., "2nd XI"). Currently only the team ComboBox (`cbxTeam`) is read — the club name is discarded.

This prevents downstream features (e.g., YouTube stream title formatting) from accessing club identity, which requires both club name and team name to be available in the domain model.

---

## 2. Solution

### 2.1 New Structured Type

Introduce a `TeamNameInfo` record in `PcsRemote.Core` containing both `ClubName` and `TeamName` properties. Both properties are non-nullable `string`. `ClubName` defaults to `string.Empty` when the club ComboBox is blank or unreadable. `TeamName` follows the existing throwing behaviour — it is never empty on success.

### 2.2 MatchTeams Evolution

Update the existing `MatchTeams` record (in `PcsRemote.Core`) to hold `TeamNameInfo` for each side instead of bare strings. All consumers are updated via full call-site migration — no backward-compatibility convenience properties are added. The consumer count is bounded and fully internal.

### 2.3 ITeamNamesAutomation Return Type Change

`ReadHomeTeamName()` and `ReadAwayTeamName()` change their return type from `string` to `TeamNameInfo`. Method names are retained (no rename). This interface is `internal` to `PcsRemote.Automation` — no external API contract is broken.

### 2.4 FlaUiTeamNamesAutomation — Read Club ComboBox

The `ReadTeamName` private method is extended to also read the `cboClub` ComboBox (using the existing `KnownElements.ClubComboBoxAutomationId` constant) and return a `TeamNameInfo` containing both values.

**Failure behaviour:**
- If the club ComboBox control is present but its value is blank or unselected, `ClubName` defaults to `string.Empty` (non-throwing).
- If the club ComboBox control cannot be found or read due to an automation error, `ClubName` defaults to `string.Empty` and a warning is logged. This is distinct from a blank control value — the warning enables detection of UI automation regressions.
- The team name ComboBox (`cbxTeam`) read continues to throw on failure as it does today.

### 2.5 Consumer Updates

Consumers affected by the `MatchTeams` and `ITeamNamesAutomation` type changes:

**`MatchTeams` consumers (in `PcsRemote.Core` and dependents):**
- `PcsProAutomationService.GetTeamNamesCoreAsync` — constructs `MatchTeams` from `TeamNameInfo` values. Error paths construct `MatchTeams` with empty `TeamNameInfo` (both fields `string.Empty`).
- `MockPcsProAutomationService.GetTeamNamesAsync` — returns mock data with club and team names.
- Web UI (`Index.razor`) — accesses `MatchTeams.Home.TeamName` / `MatchTeams.Away.TeamName` (or equivalent after type change). No new club-name display is added.

**`ITeamNamesAutomation` consumers (internal to Automation and test projects):**
- `FakeTeamNamesAutomation` (test double) — updated to return `TeamNameInfo`.
- All test code that constructs `MatchTeams` directly — updated to use `TeamNameInfo`.

**Not affected by this step:**
- `MatchInfo` record — unchanged. `MatchInfo.HomeTeam` / `AwayTeam` remain bare strings populated by `MatchRowParser` from DataGrid rows. No club fields are added.
- `MatchCard.razor` — uses `MatchInfo`, not `MatchTeams`. Unaffected.
- `BroadcastTitleRenderer` — uses `MatchInfo`, not `MatchTeams`. Title rendering integration with club names requires a data-flow bridge between `MatchInfo` (pre-load) and `MatchTeams` (post-load) — deferred to a future step.
- `MatchRowParser` — no changes.

---

## 3. Requirements

| ID | Requirement |
|---|---|
| R-1 | A `TeamNameInfo` record is created in `PcsRemote.Core` with non-nullable `string ClubName` and `string TeamName` properties. |
| R-2 | `MatchTeams` is updated to hold `TeamNameInfo` for home and away instead of bare strings. All consumers are updated via full call-site migration. |
| R-3 | `ITeamNamesAutomation.ReadHomeTeamName()` and `ReadAwayTeamName()` return `TeamNameInfo` instead of `string`. Method names are retained. |
| R-4 | `FlaUiTeamNamesAutomation` reads both `cboClub` and `cbxTeam` ComboBoxes per team and returns a populated `TeamNameInfo`. Club read failures default to empty with a logged warning. |
| R-5 | `PcsProAutomationService.GetTeamNamesCoreAsync` constructs `MatchTeams` from `TeamNameInfo` values. All error paths construct `MatchTeams` with empty `TeamNameInfo` (both fields `string.Empty`). |
| R-6 | `MockPcsProAutomationService` returns realistic mock data with both club and team names. |
| R-7 | `FakeTeamNamesAutomation` test double is updated to return `TeamNameInfo`. |
| R-8 | Build: 0 warnings, 0 errors. All existing tests pass. |

---

## 4. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `TeamNameInfo` record exists in `PcsRemote.Core` with non-nullable `ClubName` and `TeamName` string properties. |
| AC-2 | `MatchTeams` holds `TeamNameInfo` for home and away. All existing consumers are migrated to the new type. No backward-compatibility convenience properties exist. |
| AC-3 | `ITeamNamesAutomation` read methods return `TeamNameInfo`. Method names are unchanged. |
| AC-4 | `FlaUiTeamNamesAutomation.ReadTeamName` reads both `KnownElements.ClubComboBoxAutomationId` and `KnownElements.TeamComboBoxAutomationId`, returning a `TeamNameInfo` with both fields set (note: `ClubName` may be `string.Empty` per AC-5). |
| AC-5 | If the club ComboBox is present but blank, `TeamNameInfo.ClubName` is `string.Empty` (non-throwing). If the club ComboBox cannot be found or read, `TeamNameInfo.ClubName` is `string.Empty` and a warning is logged. The team name read continues to throw on failure. |
| AC-6 | `PcsProAutomationService.GetTeamNamesCoreAsync` constructs `MatchTeams` from `TeamNameInfo`. All error paths construct `MatchTeams` with `new TeamNameInfo(string.Empty, string.Empty)` for both home and away. |
| AC-7 | Unit tests cover: `TeamNameInfo` construction, `MatchTeams` with structured types, service-layer happy and error paths. |
| AC-8 | All existing tests pass with 0 warnings, 0 errors. |
| AC-9 | Web UI components compile and display team names correctly (no functional change to displayed text — club name display is a future UI enhancement). |

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| RISK-1 | Wide refactoring surface — many consumers of `MatchTeams` | Full call-site migration with compiler enforcement. Consumer count is bounded and fully internal. |
| RISK-2 | `cboClub` ComboBox may be empty for some match types | AC-5 requires graceful fallback to empty string with distinction between blank control and read failure. |
| RISK-3 | Club ComboBox read failure silently masking UI automation regression | AC-5 requires logged warning on read failure (distinct from blank value), enabling detection. |

---

## 6. Out of Scope

- `MatchInfo` record changes — `MatchInfo.HomeTeam` / `AwayTeam` remain bare strings populated by `MatchRowParser`. No club fields are added to `MatchInfo`.
- `BroadcastTitleRenderer` changes — the renderer takes `MatchInfo`, not `MatchTeams`. Adding club-name tokens to the title renderer requires a data-flow bridge between the two types, which is deferred to a future step.
- `MatchRowParser` changes — the parser reads from DataGrid rows which do not contain separate club/team fields.
- Default title template change in `appsettings.json`.
- Web UI rendering changes to display club names separately (future enhancement).
- `MatchCard.razor` changes — this component uses `MatchInfo`, not `MatchTeams`, and is unaffected.

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Opus / GPT-5.4 | NEEDS REVIEW — 1 CRITICAL (data flow gap), 4 HIGH, 6 MEDIUM, 2 LOW, 1 INFO. 13 accepted, 2 rejected as moot → v0.2 |
