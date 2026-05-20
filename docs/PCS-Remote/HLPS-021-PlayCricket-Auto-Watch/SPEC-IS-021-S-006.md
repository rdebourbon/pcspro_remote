# SPEC-IS-021-S-006 — Post-load Fixture ID Resolution

| Field | Value |
|---|---|
| **Status** | APPROVED — Rev 4 |
| **Step** | IS-021 S-006 |
| **Branch** | `feature/IS-021-S-006-fixture-resolution` |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md |

---

## Context

S-001 through S-005 delivered the Play-Cricket contracts, API clients, watcher service, and the auto-load polling loop. S-006 implements the other side of the bridge between the PCS Pro domain and the Play-Cricket API: after a match is loaded (auto or manual), the hosted service attempts to resolve the corresponding Play-Cricket fixture ID so that S-007 (auto-close polling) can track it.

Resolution is triggered by the state machine entering the loaded state. It runs as a cancellable background task: all configured site IDs are queried in parallel, results are date-filtered to the loaded match date, and team names are normalised and compared. A unique fixture match sets the resolved ID in the watcher service. Any other outcome leaves the ID unresolved and auto-close polling is skipped per SC-6.

This step also resolves OQ-3 (team name normalisation algorithm) — see R-3 below.

---

## Commit Strategy

Commit at three natural boundaries: (1) any new interface additions or options extension needed for the resolver (e.g., adding a club-name config property if not already accessible from the hosted service); (2) the resolution logic itself within the hosted service, with full state-change subscription and in-flight task management; (3) associated tests. Each commit must leave the solution building with zero warnings and all existing tests passing.

---

## Requirements

### R-1 — State-change subscription and in-flight task lifecycle

`PlayCricketWatcherHostedService` is extended to:

- Accept `IPlayCricketApiClient` as a constructor dependency. This gives the service access to the Play-Cricket API without coupling resolution logic to the polling tick.
- Subscribe to `IPcsProAutomationService.StateChanged` on construction (and unsubscribe in a `Dispose` override). The `BackgroundService` base class supports `Dispose`; the override must call `base.Dispose`.
- Maintain a cancellation-token source and a reference to the in-flight resolution task.

**On any state change:**

1. Cancel and discard any in-flight resolution task immediately.
2. If the new state is NOT `MatchLoaded`: call the watcher service to clear the resolved fixture ID (set to null). This ensures the ID is always cleared when the system is no longer in a loaded state.
3. If the new state IS `MatchLoaded`: start a new background resolution task (fire-and-forget, but stored so it can be cancelled on subsequent state changes). The task is linked to both the stoppingToken (received by `ExecuteAsync`) and the per-entry cancellation source.

The state-change handler must return quickly — it must not await the resolution task or block.

---

### R-2 — Resolution task

When triggered by entry to `MatchLoaded`, the resolution task proceeds as follows:

**Guard — sentinel date:**
If the loaded match's date is the sentinel value (the `DateOnly` unset sentinel used throughout the project), resolution is skipped entirely: a warning is logged and the fixture ID remains null. This prevents resolution when the loaded match's date is unknown — the primary real-world trigger for this guard is `UseCurrentMatchAsync` (the manual attach flow), which always populates `LoadedMatch` with sentinel identity fields including a default `MatchDate`.

**Parallel API queries:**
All configured site IDs from `PlayCricketOptions` are queried concurrently. Each query calls the API client to retrieve all fixtures for that site. `OperationCanceledException` from any query propagates unconditionally — the task exits cleanly.

Because `IPlayCricketApiClient.GetFixturesAsync` never throws on API errors (it returns an empty list and logs internally at warning level), the hosted service cannot distinguish a per-site failure from a legitimately empty site at this level. This is a deliberate simplification: when one or more sites return empty due to an unreachable API, the effect is indistinguishable from those sites having no matching fixtures, and resolution proceeds against the available (potentially incomplete) data. This deviates from the governing IS's "indeterminate cycle" wording, which was written assuming failure could be detected; that wording is accepted as-is because the API client already handles per-site error logging and there is no clean mechanism to signal failure vs. empty without a breaking change to `IPlayCricketApiClient`.

**Retry policy:** because resolution is state-change–driven (not poll-driven), there is no automatic background retry. Re-resolution occurs on the next entry to `MatchLoaded` (e.g., if the operator manually returns to match selection and reloads). This is acceptable per SC-6 and the accepted risk at HLPS-021.

**Date filtering:**
After collecting all results across all sites, discard any fixture whose date does not match the loaded match's date. Do not use today's date — use the loaded match's date directly. This ensures resolution works correctly for manually loaded non-today matches.

**Team name matching:**
For each remaining fixture, check whether it matches the loaded match by team name using the normalisation algorithm defined in R-3. The normalisation function is applied to both sides: each Play-Cricket fixture team name and each PCS Pro loaded match team name are individually normalised before comparison. A fixture is a candidate if, after normalisation, the fixture's home and away team names match the loaded match's home and away team names in either orientation:
- orientation A: fixture home matches loaded home AND fixture away matches loaded away
- orientation B: fixture home matches loaded away AND fixture away matches loaded home

Collect all candidates. If exactly one candidate is found across all sites, proceed to the assignment step. Otherwise, log a warning with the match count and return without modifying the fixture ID. Any pre-existing fixture ID is left unchanged — it is not cleared. Auto-close polling skips for the current entry per SC-6 regardless.

**Final cancellation check:**
Immediately before calling the watcher service to assign the fixture ID, check the cancellation token one final time. If it is cancelled at this point, skip the assignment entirely and exit. This closes the race window between the task's last internal check and the state-change handler's `SetCurrentFixtureId(null)` clear: without this check, the task could set the fixture ID after the handler has already cleared it, leaving a non-null fixture ID persisting in a non-MatchLoaded state.

**Assignment:**
If an existing fixture ID is already set in the watcher service AND the new candidate's fixture ID differs from it, cancel the active countdown before setting the new ID. When they are the same value, `SetCurrentFixtureId` is already a no-op per its interface contract, so no action is needed and `CancelCountdown` must NOT be called — calling it would dismiss the current fixture and silently block auto-close for that fixture permanently.

Then call the watcher service to set the resolved fixture ID.

---

### R-3 — OQ-3 resolution: team name normalisation algorithm

This step resolves OQ-3. The normalisation function takes a single team name and a club-name prefix and returns a canonical form for comparison:

1. If a club-name prefix is configured (non-empty) and the team name starts with it at a word boundary (matching the existing `TeamNameFormatter.MatchesClubPrefix` logic — case-insensitive, complete word boundary), strip the prefix from the team name.
2. Trim all leading and trailing whitespace and separator characters (spaces, hyphens, tabs) from the result.
3. Collapse any internal runs of whitespace to a single space.
4. The result is compared case-insensitively.

Roman-numeral/ordinal normalisation (e.g., "1st" ≡ "I") is NOT required. Both PCS Pro and Play-Cricket are expected to use consistent ordinal-format suffixes for the same club. The algorithm is intentionally minimal to reduce false-positive risk.

The club-name prefix is sourced from the PCS Pro configuration (the same club name used by `TeamNameFormatter` in the automation layer). The delivery phase determines whether this is injected via `IOptions<PcsProOptions>` or added as a new property on `PlayCricketOptions`; either is acceptable provided it is the same logical value.

---

## Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | On entry to `MatchLoaded` state, the hosted service starts a background resolution task. |
| AC-2 | On any state change away from `MatchLoaded`, any in-flight resolution task is cancelled and the watcher service's fixture ID is set to null. |
| AC-3 | On re-entry to `MatchLoaded` (system went MatchLoaded → other → MatchLoaded), the first resolution task is cancelled and a new one starts. |
| AC-4 | When the loaded match's date is the sentinel (unset) value, resolution is skipped with a warning and the fixture ID remains null. |
| AC-5 | All configured site IDs are queried concurrently (not sequentially). |
| AC-6 | Date filtering uses the loaded match's date, not today's date. |
| AC-7 | A unique team-name match across all sites sets the fixture ID via the watcher service. |
| AC-8 | Zero or multiple team-name matches across all sites: a warning is logged and the fixture ID is left unchanged (any pre-existing ID is preserved; if there was none, it remains null). |
| AC-9 | Team name normalisation strips the configured club-name prefix before comparing. |
| AC-10 | Team name matching accepts either home/away orientation. |
| AC-11 | When a new unique match is found but a fixture ID is already set AND the new ID differs from the current one, the countdown is cancelled before the ID is updated. When the new ID equals the current one, no action is taken (assignment is a no-op; `CancelCountdown` is not called). |
| AC-12 | `OperationCanceledException` propagates cleanly from the resolution task (no swallowing, no crash). The final cancellation check before assignment prevents the fixture ID being set after the state-change handler has cleared it. |
| AC-13 | All 20 test cases (TC-1 through TC-19, plus TC-6b) verified passing. |

---

## Test Cases

| ID | Scenario | Pre-conditions | Action | Expected |
|---|---|---|---|---|
| TC-1 | Unique fixture found | One site, one fixture matching date and team names (orientation A) | StateChanged → MatchLoaded | Fixture ID set to matching fixture's ID |
| TC-2 | Zero fixtures after filtering | One site, all fixtures on wrong date | StateChanged → MatchLoaded | Fixture ID remains null; warning logged |
| TC-3 | Multiple fixtures after filtering | One site, two fixtures on correct date both matching team names | StateChanged → MatchLoaded | Fixture ID remains null; warning logged |
| TC-4 | Sentinel match date | Loaded match has default (sentinel) date | StateChanged → MatchLoaded | Resolution skipped; warning logged; fixture ID null |
| TC-5 | Date filter uses loaded date, not today | One site, fixture on loaded match's date (not today) | StateChanged → MatchLoaded | Fixture ID resolved using loaded match's date |
| TC-6 | Existing fixture ID replaced by different ID | Fixture ID already set to fixture 10; countdown active; new unique match found with fixture ID 99 | StateChanged → MatchLoaded again | Countdown cancelled (and fixture 10 dismissed); fixture ID updated to 99 |
| TC-6b | Existing fixture ID re-resolved to same ID | Fixture ID already set to fixture 10; countdown active; re-resolution finds same fixture ID 10 | StateChanged → MatchLoaded again | Countdown NOT cancelled; fixture 10 NOT dismissed; fixture ID remains 10 (no-op) |
| TC-7 | Task cancelled on state exit | Resolution task in flight | StateChanged → NotRunning (during task) | Task cancelled; fixture ID set to null |
| TC-8 | Task cancelled on re-entry | Resolution task in flight; second state-change → MatchLoaded | StateChanged → MatchLoaded (second time) | First task cancelled; second task started |
| TC-9 | OCE propagates cleanly | Resolution task running; cancellation requested via stoppingToken | Cancel stoppingToken | Task exits without crash or swallowed OCE |
| TC-10 | Club prefix stripped from both sides | Play-Cricket fixture has "High Halstow CC 1st XI" (home); PCS Pro match has "High Halstow CC 1st XI" (home, raw) — club name "High Halstow CC" configured | StateChanged → MatchLoaded | Both sides normalised to "1st XI"; fixture ID resolved correctly |
| TC-11 | Reversed orientation match | Play-Cricket fixture home team matches PCS Pro away team (and vice versa) | StateChanged → MatchLoaded | Fixture ID resolved correctly |
| TC-12 | Multiple sites queried concurrently | Two sites; site 1 uses a controllable delay gate (does not return until signalled) and returns fixtures that do not match the loaded match's date; site 2 returns a unique fixture immediately on the correct date with matching team names | StateChanged → MatchLoaded | Before signalling site 1, site 2's query has already completed — confirming both queries were in-flight simultaneously (not sequential). Fixture ID resolved from site 2. |
| TC-13 | Fixture ID cleared on state exit | Fixture ID set from previous resolution | StateChanged → NotRunning | Watcher service fixture ID is null |
| TC-14 | Zero fixtures returned by all sites | Two sites, both return empty lists | StateChanged → MatchLoaded | Fixture ID null; warning logged |
| TC-15 | Cross-site multiple-match ambiguity | Two sites; site 1 returns fixture ID 10 matching correct date and loaded match's team names; site 2 returns fixture ID 20 matching the same date and team names | StateChanged → MatchLoaded | Fixture ID null; warning logged with candidate count = 2 |
| TC-16 | Correct date but non-matching team names | One site, one fixture on the correct date but team names do not match the loaded match after normalisation | StateChanged → MatchLoaded | Fixture ID null; warning logged |
| TC-17 | Final cancellation check prevents post-completion race | One site returns a unique matching fixture (all queries complete, unique candidate identified); cancellation is injected after candidate selection but before assignment executes (state-change fires, fixture ID cleared to null) | Cancellation injected in the post-completion window | `SetCurrentFixtureId` is never called with the candidate ID; fixture ID remains null |
| TC-18 | Case-insensitive team name comparison | One site, fixture with team names in different casing from the loaded match (e.g., API returns lower-case "1st xi"; PCS Pro has "1st XI") | StateChanged → MatchLoaded | Fixture ID resolved correctly (case difference does not prevent match) |
| TC-19 | Pre-existing fixture ID preserved on re-resolution failure | Fixture ID already set to fixture 10; resolution task triggered again; all queries return but zero candidates match date+team names | StateChanged → MatchLoaded again | Fixture ID remains 10 (preserved); warning logged; `SetCurrentFixtureId` not called |

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| — | — | — | — | — | DRAFT | Awaiting review |
| R1 | 2 | pr-review-agent | 2×HIGH, 3×MEDIUM, 1×LOW | All 6 accepted | REVISION REQUIRED | F-1 (API failure vs IS): documented deliberate deviation from IS wording; retry policy clarified as state-change–triggered. F-2 (race condition): final CT check before assignment added. F-3 (normalisation scope): both sides normalised; TC-10 updated with raw PCS Pro name. F-4 (CancelCountdown dismiss): assignment guard restricted to new-ID-differs-from-current; TC-6b added. F-5 (TC-12 concurrency): TC-12 updated with controllable-delay gate pattern. F-6 (sentinel rationale): rationale corrected to reference UseCurrentMatchAsync attach flow. |
| R2 | 2 | pr-review-agent | 0×HIGH, 2×MEDIUM, 1×LOW | All 3 accepted | REVISION REQUIRED | Finding-1: TC-12 pre-conditions updated to specify site 1 returns non-matching-date fixtures (removes cross-site ambiguity). Finding-2: TC-15 added — two sites each returning a distinct matching fixture → null + warning (tests cross-site aggregation failure branch). Finding-3: TC-16 added — correct date but non-matching team names → null + warning (tests name-comparison rejection path). AC-13 count updated to 17. All R1 fixes verified resolved. |
| R3 | 2 | pr-review-agent | 0×HIGH, 1×MEDIUM, 1×LOW | Both accepted | REVISION REQUIRED | F-1 (MEDIUM): TC-17 added — cancellation injected after candidate selection but before assignment; verifies final CT check prevents post-completion fixture ID set. F-2 (LOW): TC-18 added — case-insensitive comparison test. AC-13 count updated to 19. All R2 fixes verified resolved. |
| R4 | 2 | pr-review-agent | 0×HIGH, 1×MEDIUM, 0×LOW | Accepted | APPROVED — Rev 4 | F-1 (MEDIUM): R-2 and AC-8 contradicted each other on zero/multiple-candidate path with pre-existing fixture ID. Resolved: R-2 now explicitly states pre-existing ID is preserved unchanged; AC-8 updated to match. TC-19 added — pre-existing ID 10, re-resolution finds zero candidates, ID remains 10. AC-13 count updated to 20. All R3 fixes verified resolved. |
