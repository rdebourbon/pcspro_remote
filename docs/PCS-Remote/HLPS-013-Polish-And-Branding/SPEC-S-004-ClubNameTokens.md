# SPEC-S-004 — Club Name Tokens in BroadcastTitleRenderer

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **IS Step** | S-004 |
| **HLPS**    | HLPS-013 v0.3 (APPROVED) |
| **Author**  | Copilot |
| **Created** | 2026-04-22 |

---

## 1. Objective

Add `{HomeClub}` and `{AwayClub}` tokens to `BroadcastTitleRenderer` so operators can include club names in YouTube broadcast title templates. This resolves DEF-001 (renderer lacks club name data) and DEF-002 (`MatchInfo` has no club name fields) by enriching `MatchInfo` after `GetTeamNamesAsync` reads club names from the Match Details dialog.

---

## 2. Branch

`feature/S-004-club-name-tokens`

---

## 3. Requirements

| ID | Requirement |
|----|-------------|
| R-1 | Add `HomeClub` and `AwayClub` positional parameters to `MatchInfo` with default `""`. Existing callers that supply only the original five parameters are unaffected. |
| R-2 | `BroadcastTitleRenderer.ResolveToken` resolves `{HomeClub}` → `match.HomeClub` and `{AwayClub}` → `match.AwayClub`. Empty values produce empty-string substitution (no throw). |
| R-3 | After a successful `GetTeamNamesAsync` call, the automation service reorders club names using `TeamNameFormatter.OrderClubNamesForTitle` (same swap logic as S-003's team reorder), then enriches `_loadedMatch` with the ordered club names. If `_loadedMatch` is null (e.g., UseCurrentMatchAsync attach path), enrichment is skipped. |
| R-4 | Backward compatibility (C-7): templates that do not use `{HomeClub}` or `{AwayClub}` produce identical output to today. |
| R-5 | Mock parity: `MockPcsProAutomationService.GetTeamNamesAsync` enriches `_loadedMatch` with the same club name values it returns. |
| R-6 | `TeamNameFormatter.OrderClubNamesForTitle` is a pure static method that reorders two club names to match `FormatForTitle`'s swap logic, using `MatchesClubPrefix` (changed from `private` to `internal`) for consistent prefix matching. If only the away club matches the configured club name, clubs are swapped; otherwise original order is preserved. |

---

## 4. Dependency Direction

`BroadcastTitleRenderer` and `MatchInfo` are in `PcsRemote.Core` (zero project dependencies). The enrichment logic lives in `PcsRemote.Automation` and `PcsRemote.Automation.Mock` — both already depend on Core. No new project references are introduced.

---

## 5. Design

### 5.1 MatchInfo Record Change

```csharp
public record MatchInfo(
    string MatchId,
    string HomeTeam = "Home XI",
    string AwayTeam = "Away XI",
    string MatchType = "Friendly",
    DateOnly MatchDate = default,
    string HomeClub = "",
    string AwayClub = "");
```

New parameters have defaults, so all existing construction sites (positional, named, `with` expressions) compile without changes.

### 5.2 BroadcastTitleRenderer Token Resolution

Add two cases to the `tokenName switch` in `ResolveToken`:

```csharp
"HomeClub" => match.HomeClub ?? "",
"AwayClub" => match.AwayClub ?? "",
```

The `?? ""` follows the existing defensive pattern for `HomeTeam`/`AwayTeam` even though the type is non-nullable.

### 5.3 Club Name Ordering

S-003's `FormatMatchForTitle` may reorder teams so the club-matching team appears first. Club names must follow the same order. Add a public static method to `TeamNameFormatter`:

```csharp
public static (string First, string Second) OrderClubNamesForTitle(
    string? homeClub, string? awayClub, string? clubName)
{
    if (string.IsNullOrEmpty(clubName))
        return (homeClub ?? "", awayClub ?? "");

    bool homeMatches = MatchesClubPrefix(homeClub, clubName);
    bool awayMatches = MatchesClubPrefix(awayClub, clubName);

    if (!homeMatches && awayMatches)
        return (awayClub ?? "", homeClub ?? "");

    return (homeClub ?? "", awayClub ?? "");
}
```

This reuses `MatchesClubPrefix` (changed from `private` to `internal`) to ensure the same matching predicate as `FormatForTitle`. This prevents divergence when the configured club name is a short form that prefix-matches but doesn't exactly equal the dialog club name.

### 5.4 Automation Service Enrichment

In `PcsProAutomationService.GetTeamNamesCoreAsync`, after successfully reading team names and before returning:

```csharp
var current = _loadedMatch;
if (current is not null)
{
    var (firstClub, secondClub) = TeamNameFormatter.OrderClubNamesForTitle(
        homeTeam.ClubName, awayTeam.ClubName, _options.ClubName);
    _loadedMatch = current with { HomeClub = firstClub, AwayClub = secondClub };
}
```

**Thread safety:** The local `current` snapshot prevents a NullReferenceException if a concurrent state transition clears `_loadedMatch` between the null check and the `with` expression. The enrichment may be "lost" if a concurrent state transition nulls the field, but this is benign — the match is being abandoned in that case.

### 5.5 Mock Service Enrichment

In `MockPcsProAutomationService.GetTeamNamesAsync`, after constructing the `MatchTeams` return value:

```csharp
if (_loadedMatch is not null)
{
    _loadedMatch = _loadedMatch with
    {
        HomeClub = "Home CC",
        AwayClub = "Away CC"
    };
}
```

This uses the same hardcoded club names the mock already returns. The mock does not need `OrderClubNamesForTitle` because its `GetTeamNamesAsync` does not depend on configured club name matching.

---

## 6. Data Flow (S-003 → S-004 Interaction)

The transformation order across `LoadMatchAsync` and `GetTeamNamesAsync`:

1. `LoadMatchCoreAsync` gets raw `MatchInfo` from match selection grid.
2. S-003's `FormatMatchForTitle` strips configured club prefix and reorders teams (club team first). Result stored in `_pendingLoadedMatch`.
3. State transition to `MatchLoaded` promotes `_pendingLoadedMatch` → `_loadedMatch`. At this point, `HomeClub`/`AwayClub` are default `""`.
4. Caller invokes `GetTeamNamesAsync` → reads `MatchTeams` from Match Details dialog (dialog home/away order).
5. S-004's enrichment applies `OrderClubNamesForTitle` to reorder club names to match S-003's team reorder, then writes to `_loadedMatch`.

After step 5, `{HomeTeam}` and `{HomeClub}` refer to the same logical team (the one displayed first), and `{AwayTeam}` and `{AwayClub}` refer to the other.

---

## 6. Test Cases

### 6.1 Core — BroadcastTitleRenderer

| TC | Input | Expected Output | Requirement |
|----|-------|-----------------|-------------|
| TC-1 | Template `{HomeClub}`, match with HomeClub = "High Halstow CC" | "High Halstow CC" | R-2 |
| TC-2 | Template `{AwayClub}`, match with AwayClub = "Cobham CC" | "Cobham CC" | R-2 |
| TC-3 | Template `{HomeTeam} vs {AwayTeam}`, match with default empty club names | Same output as today | R-4 |
| TC-4 | Template `{HomeClub} {HomeTeam} vs {AwayClub} {AwayTeam}`, match with all fields | All four tokens resolved | R-2 |
| TC-5 | Template `{HomeClub}`, match with HomeClub = "" | "" (empty string) | R-2 |
| TC-6 | Template `{HomeClub}`, match with HomeClub = default (empty) | "" (empty string) | R-2 |

### 6.2 Core — OrderClubNamesForTitle

| TC | Inputs (homeClub, awayClub, clubName) | Expected (First, Second) | Requirement |
|----|---------------------------------------|--------------------------|-------------|
| TC-7 | "HHCC", "Cobham CC", "HHCC" | ("HHCC", "Cobham CC") — home matches, no swap | R-6 |
| TC-8 | "Cobham CC", "HHCC", "HHCC" | ("HHCC", "Cobham CC") — away matches, swapped | R-6 |
| TC-9 | "Club A", "Club B", "HHCC" | ("Club A", "Club B") — neither matches, no swap | R-6 |
| TC-10 | "HHCC", "HHCC", "HHCC" | ("HHCC", "HHCC") — both match, no swap | R-6 |
| TC-11 | "HHCC", "Cobham CC", null | ("HHCC", "Cobham CC") — null clubName, no swap | R-6 |
| TC-12 | "HHCC", "Cobham CC", "" | ("HHCC", "Cobham CC") — empty clubName, no swap | R-6 |
| TC-13 | null, "Cobham CC", "HHCC" | ("", "Cobham CC") — null homeClub | R-6 |

### 6.3 Integration — Automation Service

| TC | Scenario | Assertion | Requirement |
|----|----------|-----------|-------------|
| TC-14 | LoadMatchAsync then GetTeamNamesAsync succeeds | `LoadedMatch.HomeClub` and `LoadedMatch.AwayClub` match ordered values from MatchTeams | R-3 |
| TC-15 | LoadMatchAsync without GetTeamNamesAsync | `LoadedMatch.HomeClub` and `LoadedMatch.AwayClub` are "" | R-1 |
| TC-16 | UseCurrentMatchAsync then GetTeamNamesAsync | No throw; `LoadedMatch` remains null; teams returned correctly | R-3 |
| TC-17 | GetTeamNamesAsync fails (dialog error) | `LoadedMatch.HomeClub`/`AwayClub` remain default "" | R-3 |

### 6.4 Integration — Mock Service

| TC | Scenario | Assertion | Requirement |
|----|----------|-----------|-------------|
| TC-18 | LoadMatchAsync then GetTeamNamesAsync | `LoadedMatch.HomeClub` = "Home CC", `LoadedMatch.AwayClub` = "Away CC" | R-5 |

---

## 7. Files Changed

| File | Action | Project |
|------|--------|---------|
| `src/PcsRemote.Core/MatchInfo.cs` | Modify — add HomeClub, AwayClub parameters | PcsRemote.Core |
| `src/PcsRemote.Core/BroadcastTitleRenderer.cs` | Modify — add HomeClub, AwayClub token cases | PcsRemote.Core |
| `src/PcsRemote.Core/TeamNameFormatter.cs` | Modify — add OrderClubNamesForTitle method | PcsRemote.Core |
| `src/PcsRemote.Automation/PcsProAutomationService.cs` | Modify — enrich _loadedMatch in GetTeamNamesCoreAsync | PcsRemote.Automation |
| `src/PcsRemote.Automation.Mock/MockPcsProAutomationService.cs` | Modify — enrich _loadedMatch in GetTeamNamesAsync | PcsRemote.Automation.Mock |
| `tests/PcsRemote.Core.Tests/BroadcastTitleRendererTests.cs` | Modify — add TC-1 through TC-6 | PcsRemote.Core.Tests |
| `tests/PcsRemote.Core.Tests/TeamNameFormatterTests.cs` | Modify — add TC-7 through TC-13 | PcsRemote.Core.Tests |
| `tests/PcsRemote.Automation.Tests/PcsProAutomationServiceTests.cs` | Modify — add TC-14 through TC-17 | PcsRemote.Automation.Tests |
| `tests/PcsRemote.YouTube.Mock.Tests/MockYouTubeLiveStreamServiceTests.cs` | Modify — add TC-18 | PcsRemote.YouTube.Mock.Tests |

---

## 8. DEF-001 / DEF-002 Resolution

After S-004 delivery:
- **DEF-001** (renderer lacks club data): Resolved — `{HomeClub}` and `{AwayClub}` tokens are fully functional.
- **DEF-002** (`MatchInfo` lacks club fields): Resolved — `HomeClub` and `AwayClub` are populated via `GetTeamNamesAsync` enrichment.

Both items should be updated to status "Resolved (S-004)" in `DEFERRED-ITEMS.md`.

---

## 9. Review History

### R1 — Adversarial Review

| # | Reviewer | Severity | Finding | Decision | Fix |
|---|----------|----------|---------|----------|-----|
| 1 | GPT 5.4 | MAJOR | Club names assigned in dialog order, but S-003 may have reordered display teams — `{HomeClub}` could refer to the wrong team | Accept | Added `OrderClubNamesForTitle` method (R-6, §5.3) that applies same swap logic as `FormatForTitle` |
| 2 | Both | MEDIUM | Thread safety: check-then-use on `_loadedMatch` is a read-modify-write race | Accept | Changed to local variable capture pattern in §5.4; documented benign race on state transition |
| 3 | Both | MEDIUM | UseCurrentMatchAsync path leaves LoadedMatch null — untested | Accept | Added TC-16 |
| 4 | Both | MEDIUM | GetTeamNamesAsync failure path untested — no partial enrichment verified | Accept | Added TC-17 |
| 5 | Opus | MEDIUM | S-003/S-004 transformation order not documented | Accept | Added §6 Data Flow section |
| 6 | Opus | MAJOR | Mock GetTeamNamesAsync doesn't mutate _loadedMatch | Set aside — spec §5.5 already describes this change |
| 7 | Opus | MEDIUM | Test migration impact not quantified | Set aside — defaults preserve source compatibility |

### R2 — Verification Review

| # | Reviewer | Verdict | New Finding | Fix |
|---|----------|---------|-------------|-----|
| 1 | Opus 4.5 | REQUEST CHANGES | `OrderClubNamesForTitle` uses `String.Equals` but `FormatForTitle` uses `MatchesClubPrefix` — predicate mismatch on short club names | Accept — changed to reuse `MatchesClubPrefix` (made `internal`) |
| 2 | GPT 5.4 | APPROVE | No new findings | — |
