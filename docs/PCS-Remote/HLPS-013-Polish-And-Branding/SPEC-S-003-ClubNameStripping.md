# SPEC-S-003 — Club Name Stripping & Title Ordering

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **IS Step** | S-003 |
| **HLPS**    | HLPS-013 v0.3 (APPROVED) |
| **Author**  | Copilot |
| **Created** | 2026-04-21 |

---

## 1. Objective

Add a configurable club name setting and implement a pure formatting function that strips the configured club name prefix from team display names and reorders them so the club team appears first in the broadcast title. This delivers HLPS-013 scope items 5–6 (GAP-009).

---

## 2. Branch

`feature/S-003-club-name-stripping`

---

## 3. Requirements

### R-1: Club Name Configuration Property

Add a `ClubName` string property to `PcsProOptions`, bound to the `PcsPro:ClubName` configuration key. When empty or null, no stripping or reordering is applied (C-7 null safety).

### R-2: Pure Formatting Function

Create a static class `TeamNameFormatter` in `PcsRemote.Core` with a pure, stateless static method (safe to call from any thread) that accepts two team display names (in dialog order) and a configured club name, and returns an ordered pair representing the title-ready team names. The dependency direction is `Automation → Core` (allowed by architecture rules).

**Behaviour (SC-4 matrix):**

| Case | Input (Team1, Team2) | Club Name | Output (First, Second) | Rule |
|------|----------------------|-----------|------------------------|------|
| 1 | "HHCC - 1st XI", "Club B - 2nd XI" | "HHCC" | "1st XI", "Club B - 2nd XI" | One match — strip + place first |
| 2 | "Club B - 2nd XI", "HHCC - 1st XI" | "HHCC" | "1st XI", "Club B - 2nd XI" | Reorder — club match goes first |
| 3 | "Club C - 1st XI", "Club D - 1st XI" | "HHCC" | "Club C - 1st XI", "Club D - 1st XI" | No match — unchanged, original order |
| 4 | "HHCC - 1st XI", "HHCC - 2nd XI" | "HHCC" | "1st XI", "2nd XI" | Both match — strip both, original input order preserved |

**Prefix matching:** Case-insensitive, anchored to start-of-string. The club name must appear as a complete prefix — partial word matches are not valid (e.g., club name "High" must not match "Highlands CC").

**Separator trimming:** After stripping the prefix, greedily trim all leading whitespace and dash/hyphen characters from the remaining string until a non-separator character is reached. If stripping + trimming would produce an empty string, return the original team name unchanged.

**Guard conditions (no-op):**
- Club name is null or empty → return both names unchanged in original order.
- Either team name is null or empty → treat that team as non-matching; return names with only the non-null team processed.

### R-3: Integration Point

Apply the formatting function to the `MatchInfo` before it is stored as the loaded match. The automation service should call the formatter after match selection succeeds but before assigning the pending loaded match. This ensures that `LoadedMatch` (and consequently the broadcast title) uses the formatted team names.

The integration must not affect match selection — the raw `MatchInfo` is used for match selection; only the stored copy is transformed.

**Semantic note:** After formatting, `MatchInfo.HomeTeam` and `AwayTeam` become title-ordered display names rather than strict home/away identifiers. This is by design — the user requires club-first ordering regardless of dialog position. S-004's `{HomeClub}`/`{AwayClub}` tokens will use `MatchTeams` data (which carries separate `ClubName`/`TeamName` per team), not `MatchInfo.HomeTeam`/`AwayTeam`, so no data corruption occurs.

**Mock parity:** The mock automation service should apply the same formatting function to maintain interchangeability with the real service at the DI boundary.

### R-4: Idempotency (SC-5)

Applying the formatter to already-formatted team names must produce the same result. A team name that does not start with the club name prefix is returned unchanged.

---

## 4. Test Strategy

Tests are written in `PcsRemote.Core.Tests` for the pure formatting function, and in `PcsRemote.Automation.Tests` for the integration point. All tests follow the `MethodName_Scenario_ExpectedResult` naming convention.

### Core Formatting Tests (TeamNameFormatterTests)

| ID | Scenario | Verification |
|----|----------|--------------|
| TC-1 | One team matches, already first | Stripped + first; other unchanged |
| TC-2 | One team matches, second position | Reordered to first + stripped; other unchanged |
| TC-3 | Neither team matches | Both unchanged, original order |
| TC-4 | Both teams match | Both stripped, original order preserved |
| TC-5 | Club name null | Both unchanged, original order |
| TC-6 | Club name empty | Both unchanged, original order |
| TC-7 | Team name starts with club name + separator " - " | Prefix + separator stripped, trimmed |
| TC-8 | Team name equals club name exactly (no suffix) | Returns club name unchanged (stripping produces empty → fallback) |
| TC-9 | Prefix match is case-insensitive | Stripping still applies |
| TC-10 | Partial word match (club "High", team "Highlands CC") | No match — not stripped |
| TC-11 | Already-stripped name re-processed | Idempotent — same result |
| TC-12 | Team1 is null, Team2 matches | Team2 stripped; null team treated as non-matching |
| TC-13 | Team1 is empty string, Team2 matches | Team2 stripped; empty team treated as non-matching |
| TC-14 | Team1 matches, Team2 is null | Team1 stripped; null team treated as non-matching |
| TC-15 | Team1 matches, Team2 is empty string | Team1 stripped; empty team treated as non-matching |

### Integration Test (Automation)

| ID | Scenario | Verification |
|----|----------|--------------|
| TC-16 | LoadMatchAsync with club name configured | `LoadedMatch.HomeTeam` / `AwayTeam` reflect formatted names |
| TC-17 | LoadMatchAsync with club name empty | `LoadedMatch` team names unchanged |

---

## 5. Acceptance Criteria

1. `TeamNameFormatter` is a static class in `PcsRemote.Core` with no dependencies beyond the .NET BCL.
2. All 4 cases from the SC-4 matrix produce correct output (TC-1 through TC-4).
3. Guard conditions (null/empty club name, null/empty team names in both positions) do not throw (TC-5, TC-6, TC-12 through TC-15).
4. Prefix matching is anchored and case-insensitive (TC-9, TC-10).
5. Separator trimming handles ` - ` and whitespace (TC-7).
6. Stripping is idempotent (TC-11).
7. Integration in automation service transforms the pending loaded match without affecting match selection (TC-16, TC-17).
8. All existing tests continue to pass (regression).
9. Solution builds with 0 warnings.

---

## 6. HLPS Traceability

| Requirement | HLPS Reference |
|-------------|----------------|
| Club name config | C-2, Scope item 5 |
| Symmetric stripping | A-6, SC-4 |
| Idempotency | SC-5 |
| Null safety | C-7 |
| Unit tests | SC-10 |

---

## 7. Review History

### R1

| Finding | Source | Severity | Disposition | Action |
|---------|--------|----------|-------------|--------|
| Dependency direction unclear | Opus F-1 | MAJOR | Accept | Added explicit Automation → Core note in R-2 |
| Case 4 ordering ambiguous | Opus F-2 | MAJOR | Accept | Clarified "original input order preserved" in matrix |
| Separator trimming rules incomplete | Opus F-3 | Medium | Accept | Specified greedy trim of whitespace + dashes |
| Case-sensitivity for separators | Opus F-4 | Medium | Reject | Separators (`-`, ` `) have no case — non-issue |
| Word boundary edge case | Opus F-5 | Minor | Defer to delivery | Implementation detail |
| Thread safety not stated | Opus F-6 | Minor | Accept | Added "pure and stateless" note in R-2 |
| HomeTeam/AwayTeam semantic rewrite | GPT F-1 | MAJOR | Downgrade to Medium, Accept | Added semantic note: S-004 uses MatchTeams, not MatchInfo; reordering is by user design |
| Mock parity omitted | GPT F-2 | Medium | Accept | Added mock parity requirement in R-3 |
| Guard condition TC gap | GPT F-3 | Medium | Accept | Added TC-12 through TC-15 for null/empty team names in both positions; updated AC-3 |

### R2

| Finding | Source | Severity | Disposition | Action |
|---------|--------|----------|-------------|--------|
| Team2 null/empty guard TCs missing | GPT R2 | Medium | Accept | Added TC-14, TC-15 for Team2 null/empty; renumbered integration TCs to TC-16, TC-17 |
