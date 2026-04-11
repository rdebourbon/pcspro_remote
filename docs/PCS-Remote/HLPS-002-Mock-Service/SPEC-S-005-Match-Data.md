# SPEC-S-005: Match Data

| Field | Value |
|---|---|
| **Document** | SPEC-S-005-Match-Data.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Step** | IS-002 S-005 |
| **Date** | 2026-04-11 |
| **Governing Docs** | HLPS-002-Mock-Service.md (APPROVED v0.3), IS-002-Mock-Service.md (APPROVED v0.2) |
| **Branch** | `feature/hlps002-S005-match-data` |
| **Branch target** | `master` |

---

## 1. Context

S-004 (Error Injection) is complete. `GetTodaysMatchesAsync` currently throws `NotImplementedException`. This step implements it and extends the domain type `MatchInfo` to carry the display-level fields required by the web control panel (HLPS-003): home and away team names, match type, and match date.

The success criterion M-SC-3 mandates that every returned record's match date equals the date at call-time — not a fixture embedded at startup or hard-coded literal.

---

## 2. Scope

### 2.1 `PcsRemote.Core` — Extend `MatchInfo`

The `MatchInfo` record currently carries only `MatchId`. Four display fields are required by callers:

- **Home team name** — a non-empty string identifying the home team
- **Away team name** — a non-empty string identifying the away team
- **Match type** — a short label (e.g. `"20 overs"`, `"Club T20"`)
- **Match date** — a `DateOnly` value representing the fixture date

All new fields must carry explicit defaults so that all existing test usages of `new MatchInfo("m1")` remain valid without modification. Backward-compatibility across the existing 59 tests is a hard constraint.

**Required defaults:** `HomeTeam = "Home XI"`, `AwayTeam = "Away XI"`, `MatchType = "Friendly"`, `MatchDate = default` (`DateOnly.MinValue`). The `MatchDate` default is a documented sentinel meaning "date not set"; callers must not treat it as a valid fixture date. Records produced by `GetTodaysMatchesAsync` always carry an explicit date and are never sentinel-valued.

> **Design note — `MatchInfo` vs `MatchTeams`:** The existing `MatchTeams` record holds team names *read from a loaded match* by `GetTeamNamesAsync` — definitive post-load values. `MatchInfo` team name fields serve a different purpose: pre-selection display in the match list (the user must see team names to pick a match before it is loaded). The two types therefore co-exist intentionally with distinct provenance and lifecycle.

### 2.2 `PcsRemote.Automation.Mock` — Implement `GetTodaysMatchesAsync`

Replace the `NotImplementedException` stub with an implementation that:

- Returns exactly `FakeMatchCount` records (sourced from `MockPcsProOptions`).
- All `MatchId` values within a single response are distinct (e.g. sequential identifiers such as `"match-1"`, `"match-2"`).
- Each record carries:
  - A generated `MatchId` (e.g. sequential identifier — does not need to be globally unique)
  - Plausible home and away team names (hard-coded fixture pool or generated strings — realism preferred but not mandated)
  - A plausible match type label
  - `MatchDate` set to `DateOnly.FromDateTime(DateTime.Today)` (local system date) **at the time the method is called**, not at construction time
- When `FakeMatchCount == 0`, returns an empty list.
- Does **not** acquire the lifecycle semaphore — this is a pure data query, not a lifecycle operation.
- A pre-cancelled `CancellationToken` is silently ignored; the method always returns the full list without throwing `OperationCanceledException`.

---

## 3. Requirements

| Ref | Requirement |
|---|---|
| R-1 | `MatchInfo` gains four new optional fields — `HomeTeam` (default `"Home XI"`), `AwayTeam` (default `"Away XI"`), `MatchType` (default `"Friendly"`), `MatchDate` (default `DateOnly.MinValue` / `default`) — each with an explicit default that keeps all existing `new MatchInfo("m1")` usages valid. |
| R-2 | `GetTodaysMatchesAsync` returns exactly `FakeMatchCount` records. |
| R-3 | Every returned record's `MatchDate` equals `DateOnly.FromDateTime(DateTime.Today)` (local system date) evaluated at the time of the call. |
| R-4 | When `FakeMatchCount == 0`, `GetTodaysMatchesAsync` returns an empty list (not null, not an exception). |
| R-5 | Every returned record's `HomeTeam`, `AwayTeam`, and `MatchType` are non-empty strings. |
| R-6 | `GetTodaysMatchesAsync` does not acquire the lifecycle semaphore. |
| R-7 | Existing tests (59) continue to pass without modification. |
| R-8 | All `MatchId` values within a single response are distinct. |
| R-9 | A pre-cancelled `CancellationToken` is silently ignored; `GetTodaysMatchesAsync` always returns the full list without throwing `OperationCanceledException`. |

---

## 4. Acceptance Criteria

### AC-1 — Correct count
`GetTodaysMatchesAsync` called with `FakeMatchCount = 5` returns a list of exactly 5 records.

### AC-2 — Today's date on all records
Capture `DateOnly.FromDateTime(DateTime.Today)` into a local variable **before** calling `GetTodaysMatchesAsync`. All returned records carry a `MatchDate` equal to that captured value. (The pre-capture eliminates any midnight-boundary race between the call and the assertion.)

### AC-3 — Zero count returns empty list
`GetTodaysMatchesAsync` called with `FakeMatchCount = 0` returns an empty (non-null) list.

### AC-4 — Non-empty team names and match type
Every returned record's `HomeTeam`, `AwayTeam`, and `MatchType` are non-empty strings.

### AC-5 — Backward-compatibility defaults
`new MatchInfo("m1")` constructs successfully. The resulting instance has `MatchId == "m1"`, `HomeTeam == "Home XI"`, `AwayTeam == "Away XI"`, `MatchType == "Friendly"`, and `MatchDate == default(DateOnly)`.

### AC-6 — No semaphore interaction while lifecycle operation is in progress
With a lifecycle method in progress (the semaphore held via a `LaunchAndLoginAsync` configured with a delay of at least 2 seconds), a call to `GetTodaysMatchesAsync` completes and returns results within 200 ms (no exception of any kind). This directly falsifies any implementation that acquires the semaphore.

### AC-7 — Distinct MatchIds within a single response
A response with `FakeMatchCount = 5` contains 5 records with pairwise-distinct `MatchId` values.

### AC-8 — Pre-cancelled CancellationToken returns full list
Calling `GetTodaysMatchesAsync` with a pre-cancelled `CancellationToken` and `FakeMatchCount = 3` returns a list of 3 records without throwing any exception.

---

## 5. Out of Scope

- Real PlayCricket API integration
- Filtering or searching match records
- Persistence or caching of match data between calls
- Any change to the lifecycle semaphore logic
- Any change to existing test assertions

---

## 6. Files Affected

| File | Change |
|---|---|
| `src/PcsRemote.Core/MatchInfo.cs` | Extend with new optional fields (R-1) |
| `src/PcsRemote.Automation.Mock/MockPcsProAutomationService.cs` | Implement `GetTodaysMatchesAsync` (R-2 through R-6) |
| `tests/PcsRemote.Automation.Mock.Tests/MockPcsProAutomationServiceTests.cs` | Add tests for AC-1 through AC-8 |

No other files require modification.

---

## 7. Test Strategy

Tests are added to the existing `MockPcsProAutomationServiceTests` class using the existing `CreateSut` helper.

- **AC-1, AC-3, AC-4, AC-7**: Synchronous assertions on the returned list.
- **AC-2**: Capture `DateOnly.FromDateTime(DateTime.Today)` before calling; assert equality against the captured value.
- **AC-5**: Construct `new MatchInfo("m1")` directly and assert each property against its specified default.
- **AC-6**: Start a `LaunchAndLoginAsync` configured with a delay of at least 2 seconds to ensure the semaphore is held for the full assertion window; while it is in-flight, call `GetTodaysMatchesAsync` and assert it completes within 200 ms (no exception). Cancel the in-flight launch afterward.
- **AC-8**: Construct a pre-cancelled `CancellationToken` (`new CancellationToken(canceled: true)`), call `GetTodaysMatchesAsync` with `FakeMatchCount = 3`, and assert 3 records are returned without exception.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | pr-review-agent (default), Claude Sonnet 4.6 | NEEDS REVIEW — 2 HIGH + 5 MEDIUM + 3 LOW (combined) |
| R2 | 2026-04-11 | pr-review-agent (default), Claude Sonnet 4.6 | NEEDS REVIEW / APPROVED — 1 MEDIUM + 2 LOW (default); 3 LOW (Sonnet) |
| R3 | 2026-04-11 | pr-review-agent (default), Claude Sonnet 4.6 | **APPROVED** (both) — 1 LOW polish applied (AC-8 exception wording aligned with R-9/§7) |

### R2 Findings Applied (v0.2 → v0.3)

| Ref | Reviewer | Severity | Disposition | Resolution |
|---|---|---|---|---|
| Default Issue-1 / Sonnet Issue-3 | Both | MEDIUM / LOW | Accept | Added R-9 and AC-8: pre-cancelled token returns full list without throwing |
| Default Issue-2 / Sonnet Issue-2 | Both | LOW | Accept | AC-6 phrasing simplified: "completes within 200 ms (no exception of any kind)" |
| Sonnet Issue-1 | Sonnet | LOW | Accept | AC-6 now specifies "at least 2 seconds" minimum delay; §7 mirrors this |
| Default Issue-3 | Default | LOW | Accept | Disposition table corrected: Sonnet R1-1 / Default R1-5 now cites AC-4 (not AC-7) |

### R1 Findings Applied (v0.1 → v0.2)

| Ref | Reviewer | Severity | Disposition | Resolution |
|---|---|---|---|---|
| Sonnet R1-1 / Default R1-5 | Both | HIGH | Accept | Extended R-5 to include MatchType; AC-4 now asserts MatchType non-empty |
| Default R1-1 | Default | HIGH | Accept | AC-6 redesigned: tests semaphore non-acquisition by calling while lifecycle op in-flight |
| Sonnet R1-2 | Sonnet | HIGH | Downgrade to MEDIUM | Added design note in §2.1 justifying MatchInfo team names (pre-selection display) vs MatchTeams (post-load definitive). No change to design. |
| Default R1-2 | Default | HIGH | Downgrade to MEDIUM | Clock abstraction (TimeProvider) is delivery-level. Fix: AC-2 now mandates pre-capture of date before call to eliminate midnight boundary race |
| Default R1-3 / Sonnet R1-6 | Both | MEDIUM | Accept | Added R-8 (distinct MatchIds) and AC-7 |
| Default R1-4 / Sonnet R1-3 | Both | MEDIUM | Accept | Pinned all default values in §2.1 (HomeTeam="Home XI", AwayTeam="Away XI", MatchType="Friendly", MatchDate=default) |
| Sonnet R1-4 | Sonnet | MEDIUM | Accept | AC-2 pre-capture note added |
| Sonnet R1-5 | Sonnet | MEDIUM | Accept | AC-6 redesigned (same as Default R1-1) |
| Default R1-6 | Default | LOW | Accept | AC-5 rewritten as runnable assertion with specific default values |
| Default R1-7 | Default | LOW | Accept | Added CancellationToken behaviour to §2.2 (pre-cancelled = silently ignored) |
| Sonnet R1-7 | Sonnet | LOW | Accept | Added "(local system date)" parenthetical to §2.2 and R-3 |
| Sonnet R1-8 | Sonnet | LOW | Accept | Removed async implementation constraint sentence from §2.2 |
