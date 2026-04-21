# SPEC-S-010: Team Names Structured Return Type

| Field | Value |
|---|---|
| **Document** | SPEC-S-010-Team-Names-Structured-Type.md |
| **Status** | IN REVIEW |
| **Version** | 0.1 |
| **Date** | 2026-04-21 |
| **Step ID** | S-010 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-010-team-names-structured-type` |

---

## 1. Problem

The `ITeamNamesAutomation` interface returns bare `string` values from `ReadHomeTeamName()` and `ReadAwayTeamName()`. The diagnostic tool proved that PCS Pro's Match Details dialog exposes **two** ComboBoxes per team: `cboClub` (e.g., "Northampton Town CC") and `cbxTeam` (e.g., "2nd XI"). Currently only the team ComboBox (`cbxTeam`) is read — the club name is discarded.

This prevents the `BroadcastTitleRenderer` from including club names in YouTube stream titles, and limits the web UI's ability to display full team identity.

---

## 2. Solution

### 2.1 New Structured Type

Introduce a `TeamNameInfo` record in `PcsRemote.Core` containing both `ClubName` and `TeamName` string properties. This is the return type for individual team reads.

### 2.2 MatchTeams Evolution

Update the existing `MatchTeams` record to hold `TeamNameInfo` for each side instead of bare strings. Add convenience properties or methods so that existing consumers that only need the display name (e.g., `MatchTeams.HomeTeam` as a string) continue to work without breaking changes, or update all call sites.

### 2.3 ITeamNamesAutomation Return Type Change

`ReadHomeTeamName()` and `ReadAwayTeamName()` change their return type from `string` to `TeamNameInfo`. This is an internal interface — no external API contract is broken.

### 2.4 FlaUiTeamNamesAutomation — Read Club ComboBox

The `ReadTeamName` private method is extended to also read the `cboClub` ComboBox (using the existing `KnownElements.ClubComboBoxAutomationId` constant) and return a `TeamNameInfo` containing both values.

### 2.5 BroadcastTitleRenderer — New Tokens

Add `HomeClub` and `AwayClub` tokens to the title renderer's token resolution, enabling templates like `"{HomeClub} {HomeTeam} vs {AwayClub} {AwayTeam}"`.

### 2.6 Consumer Updates

All consumers of `MatchTeams.HomeTeam` / `MatchTeams.AwayTeam` as strings must be updated:
- `PcsProAutomationService.GetTeamNamesCoreAsync` — constructs `MatchTeams` from read results
- `MockPcsProAutomationService.GetTeamNamesAsync` — returns mock data
- Web UI components (`Index.razor`, `MatchCard.razor`) — display team names
- `FakeTeamNamesAutomation` and `StubTeamNamesAutomation` test doubles
- `BroadcastTitleRenderer` tests
- Any other test that constructs `MatchTeams` directly

---

## 3. Requirements

| ID | Requirement |
|---|---|
| R-1 | A `TeamNameInfo` record is created in `PcsRemote.Core` with `ClubName` (string) and `TeamName` (string) properties. |
| R-2 | `MatchTeams` is updated to hold `TeamNameInfo` for home and away instead of bare strings. Existing consumers that reference `.HomeTeam` / `.AwayTeam` as strings must compile after the change — either via convenience properties or full call-site updates. |
| R-3 | `ITeamNamesAutomation.ReadHomeTeamName()` and `ReadAwayTeamName()` return `TeamNameInfo` instead of `string`. Method names may be updated to reflect the new return type (e.g., `ReadHomeTeam()` / `ReadAwayTeam()`). |
| R-4 | `FlaUiTeamNamesAutomation` reads both `cboClub` and `cbxTeam` ComboBoxes per team and returns a populated `TeamNameInfo`. |
| R-5 | `BroadcastTitleRenderer` supports `{HomeClub}` and `{AwayClub}` tokens in addition to existing `{HomeTeam}` and `{AwayTeam}`. |
| R-6 | `MockPcsProAutomationService` returns realistic mock data with both club and team names. |
| R-7 | All test doubles (`FakeTeamNamesAutomation`, `StubTeamNamesAutomation`) are updated to return `TeamNameInfo`. |
| R-8 | Build: 0 warnings, 0 errors. All existing tests pass. |

---

## 4. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `TeamNameInfo` record exists in `PcsRemote.Core` with `ClubName` and `TeamName` properties. |
| AC-2 | `MatchTeams` holds `TeamNameInfo Home` and `TeamNameInfo Away` (or equivalent). Code that previously accessed `MatchTeams.HomeTeam` as a string continues to compile (via convenience property or call-site update). |
| AC-3 | `ITeamNamesAutomation` read methods return `TeamNameInfo`. |
| AC-4 | `FlaUiTeamNamesAutomation.ReadTeamName` reads both `KnownElements.ClubComboBoxAutomationId` and `KnownElements.TeamComboBoxAutomationId`, returning a `TeamNameInfo` with both populated. |
| AC-5 | If the club ComboBox read fails or returns empty, `TeamNameInfo.ClubName` defaults to empty string (non-throwing). The team name read continues to throw on failure as it does today. |
| AC-6 | `BroadcastTitleRenderer.ResolveToken` handles `HomeClub` and `AwayClub` tokens, returning the club name from `MatchInfo`. |
| AC-7 | `MatchInfo` record gains club name fields (e.g., `HomeClub`, `AwayClub`) so the title renderer can access them. |
| AC-8 | Unit tests cover: `TeamNameInfo` construction, `MatchTeams` with structured types, `BroadcastTitleRenderer` with new `{HomeClub}` / `{AwayClub}` tokens. |
| AC-9 | All existing tests pass with 0 warnings, 0 errors. |
| AC-10 | Web UI components compile and display team names correctly (no functional change to displayed text — club name display is a future UI enhancement). |

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | Wide refactoring surface — many consumers of `MatchTeams` | Incremental approach: add structured type first, update `MatchTeams` with convenience accessors, then propagate. Compiler will catch missed sites. |
| R-2 | `cboClub` ComboBox may be empty for some match types | AC-5 requires graceful fallback to empty string. |
| R-3 | Breaking change to `MatchInfo` record | All call sites that construct `MatchInfo` must be updated with new parameters. Compiler will enforce. |

---

## 6. Out of Scope

- Web UI rendering changes to display club names separately (future enhancement)
- `MatchRowParser` changes — the parser reads from DataGrid rows which do not contain separate club/team fields
- Default title template change in `appsettings.json` — the existing template continues to work; operators can opt in to club tokens manually
- `MatchInfo.HomeTeam` / `AwayTeam` semantics change — these remain the team display name for backward compatibility

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | | | |
