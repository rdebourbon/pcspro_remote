# SPEC-S-001 — Populate LoadedMatch in All Match-Loading Flows

| Field           | Value |
|-----------------|-------|
| **Status**      | APPROVED |
| **Step ID**     | S-001 |
| **Governs**     | IS-018 S-001 |
| **Branch**      | `feature/S-001-populate-loaded-match` |
| **SC Coverage** | SC2 (primary), SC3 (enables) |

---

## Problem

The `UseCurrentMatchAsync` attach flow intentionally leaves `LoadedMatch` null (AC-8 design decision from HLPS-011/SPEC-S-011). This causes the ChangeMatch button to vanish on circuit reconnect because `Index.razor` gates rendering on `_loadedMatch` being non-null (P1 in HLPS-018). S-002 cannot hydrate the UI from service state if the service itself has no match metadata after an attach.

---

## Requirements

### R1 — Populate `_pendingLoadedMatch` before firing `AttachToMatch`

`UseCurrentMatchCoreAsync` must read team names and construct a `MatchInfo` from them **before** firing the `AttachToMatch` trigger. This ensures that when the `OnTransitioned` callback executes (setting `_loadedMatch = _pendingLoadedMatch`) and subsequently fires `StateChanged(MatchLoaded)`, all circuits receiving the event see a non-null `LoadedMatch`.

The constructed `MatchInfo` will not have a full `MatchId`, `MatchType`, or `MatchDate` (these are unavailable in the attach flow). A sentinel or synthesized value must be used for `MatchId` so the record is valid, with team names populated from the scoreboard read.

**Ordering constraint:** Team name reading → set `_pendingLoadedMatch` → fire `AttachToMatch`. This is the critical before-trigger invariant that enables SC3 cross-tab consistency.

**Error handling — throw and abort:** If team name reading fails before the trigger has fired (including `OperationCanceledException`), the method must perform best-effort cleanup (`TryCloseTeamsDialog()`) and then throw. No state-machine trigger is fired (since `Timeout` is not valid in the pre-attach state), `LoadedMatch` remains null, and the state machine stays in its pre-attach state (`NotRunning` or `MatchSelection`). The exception propagates to the caller. This is the required behaviour — sentinel-continue is explicitly rejected because it would produce a `MatchLoaded` state with no meaningful team data.

**Sentinel identity fields:** The constructed `MatchInfo` uses the `MatchInfo` record defaults for fields unavailable in the attach flow: `MatchId` = synthesized from team names (e.g., `"current_{Home}_{Away}"`), `MatchType` = `"Friendly"` (record default), `MatchDate` = `DateOnly.MinValue` (record default). `HomeClub` and `AwayClub` must be populated from the `TeamNameInfo.ClubName` values read from the scoreboard.

**Title formatting:** The constructed `MatchInfo` must be passed through `FormatMatchForTitle` (or equivalent title-normalization logic) before being stored as `_pendingLoadedMatch`, matching the existing `LoadMatchCoreAsync` pattern. This ensures consistent team/club ordering when `ClubName` is configured.

### R2 — Normal `LoadMatchAsync` flow unchanged

The existing `LoadMatchCoreAsync` flow already sets `_pendingLoadedMatch` before the match-loaded transition. This behaviour must be preserved — no changes to this path.

### R3 — `StopAsync` and `ChangeMatchAsync` clearing behaviour

Both methods clear `_loadedMatch` via the `OnTransitioned` callback when state transitions away from `MatchLoaded` (line 125: `_loadedMatch = null; _pendingLoadedMatch = null;`). This existing behaviour is correct and must be preserved. No changes needed.

### R4 — Interface documentation update

The `IPcsProAutomationService.LoadedMatch` property xmldoc (lines 21-27) and the `UseCurrentMatchAsync` method xmldoc (line 128-129) both state that `LoadedMatch` is null after `UseCurrentMatchAsync`. Both must be updated to reflect that `LoadedMatch` is now populated with title-formatted team names and sentinel values for identity fields.

### R5 — AC-8 test updates

The following tests assert that `LoadedMatch` is null after `UseCurrentMatchAsync` and must be updated:

- `UseCurrentMatchAsync_HappyPath_LoadedMatchIsNull` — must assert `LoadedMatch` is non-null and contains expected team names.
- `GetTeamNamesAsync_AfterUseCurrentMatch_LoadedMatchRemainsNull` → rename to `GetTeamNamesAsync_AfterUseCurrentMatch_EnrichesLoadedMatchClubNames`. `LoadedMatch` is now non-null after attach with team names populated. After `GetTeamNamesAsync`, assert: `LoadedMatch.HomeClub` and `LoadedMatch.AwayClub` are populated from the `GetTeamNamesAsync` read, team names from the original attach read are preserved. Mirrors the existing `GetTeamNamesAsync_AfterLoadMatch_EnrichesLoadedMatchClubNames` pattern.
- `UseCurrentMatchAsync_TeamNameReadFails_TransitionsToErrorAndReturnsSentinel` — the failure path now occurs before `AttachToMatch` (not after), so the method throws instead of transitioning to `Error`. Must be reworked to assert: exception propagated to caller, state unchanged (remains `NotRunning`/`MatchSelection`), `LoadedMatch` remains null.

### R6 — Spec documentation annotation

SPEC-S-011-Use-Current-Match.md section 2.3, AC-8, and AC-12 must be annotated as superseded by HLPS-018 S-001. AC-12 described a post-attach failure path (team reading fails after `AttachToMatch`, transitions to `Error`) — this path no longer exists since team reading now precedes the trigger.

### R7 — Before-trigger ordering test

A new test must verify the before-trigger ordering invariant: subscribe to `StateChanged`, and inside the handler assert that `LoadedMatch` is non-null at the moment `StateChanged(MatchLoaded)` fires during `UseCurrentMatchAsync`. This is the deterministic guard against the SC3 race condition.

### R8 — Mock service update

`MockPcsProAutomationService.UseCurrentMatchAsync` must also populate `LoadedMatch` to maintain interface behavioural parity.

---

## Acceptance Criteria

| AC | Description |
|----|-------------|
| AC-1 | After `UseCurrentMatchAsync` succeeds, `LoadedMatch` is non-null, contains the team names and club names read from PCS Pro (title-formatted via `FormatMatchForTitle`), and has sentinel/default values for identity fields (`MatchId` synthesized, `MatchType` = record default, `MatchDate` = record default). |
| AC-2 | At the instant `StateChanged(MatchLoaded)` fires during `UseCurrentMatchAsync`, `LoadedMatch` is already non-null (before-trigger ordering). |
| AC-3 | After `LoadMatchAsync` succeeds, `LoadedMatch` behaviour is unchanged from current. |
| AC-4 | After `StopAsync` or `ChangeMatchAsync`, `LoadedMatch` is null (existing clearing behaviour preserved). |
| AC-5 | `IPcsProAutomationService.LoadedMatch` xmldoc no longer states null-after-attach. |
| AC-6 | All existing tests pass (with AC-8 tests updated to assert non-null). |
| AC-7 | A new test verifies the before-trigger ordering invariant. |
| AC-8 | Mock service populates `LoadedMatch` after `UseCurrentMatchAsync`. |
| AC-9 | SPEC-S-011 section 2.3, AC-8, and AC-12 annotated as superseded by HLPS-018 S-001. AC-12's post-attach failure path no longer exists — team reading now precedes the trigger. |
| AC-10 | If team name reading fails before `AttachToMatch` (including `OperationCanceledException`), the method performs best-effort dialog cleanup (`TryCloseTeamsDialog()`), throws, state is unchanged, and `LoadedMatch` remains null. |

---

## Test Strategy

### Tests to update (red→green)

1. **`UseCurrentMatchAsync_HappyPath_LoadedMatchIsNull`** → rename to `UseCurrentMatchAsync_HappyPath_LoadedMatchIsPopulated`. Assert `LoadedMatch` is non-null, `HomeTeam` and `AwayTeam` contain expected values from fake automation.

2. **`GetTeamNamesAsync_AfterUseCurrentMatch_LoadedMatchRemainsNull`** → update assertions: `LoadedMatch` is non-null after attach. Verify `GetTeamNamesAsync` still returns correct teams.

### New tests

3. **`UseCurrentMatchAsync_HappyPath_LoadedMatchPopulatedBeforeStateChanged`** (R7) — Subscribe to `StateChanged` and inside the handler capture `LoadedMatch`. After the call completes, assert the captured value was non-null at event-fire time.

4. **`UseCurrentMatchAsync_TeamReadFails_ThrowsAndStateUnchanged`** (AC-10) — **Replaces** existing `UseCurrentMatchAsync_TeamNameReadFails_TransitionsToErrorAndReturnsSentinel` (which must be deleted). Configure fake team name automation to throw. Verify: exception propagates to caller, `CurrentState` is unchanged from pre-call value, `LoadedMatch` is null, no `StateChanged` event fired, best-effort dialog cleanup attempted.

5. **`MockPcsProAutomationServiceTests.UseCurrentMatchAsync_HappyPath_LoadedMatchIsPopulated`** (AC-8) — Call `UseCurrentMatchAsync` on the mock and assert `LoadedMatch` is non-null with expected team names and sentinel identity fields. Mirrors mock `LoadMatch` test pattern.

### Existing tests to verify unchanged

6. Run the full `PcsProAutomationServiceTests` suite — all tests not listed above (AC-8 reversals and failure-path rework) must pass unchanged.

---

## Risks

| Risk | Mitigation |
|------|------------|
| Team name reading currently happens after `AttachToMatch` trigger — reordering may introduce failures if team dialog depends on internal state | Code audit confirms team dialog automation uses PCS Pro window state (externally verified via `IsMatchLoaded()`), not internal state machine state. Low risk. |
| Constructed `MatchInfo` has sentinel `MatchId` — downstream code may assume `MatchId` is meaningful | S-002 uses `LoadedMatch` for display (team names) and button gating (non-null check). `MatchId` is not displayed or used in P1 fix path. Low risk. |
| Error trigger reordering — `FireErrorUnderLockAsync(Timeout)` invalid before `AttachToMatch` | R1 explicitly requires error handling adjustment for pre-attach failures. |

---

## Review History

| Round | Panel | Outcome | Notes |
|-------|-------|---------|-------|
| R1 | GPT-5.4, Sonnet 4.6, GPT-5.2-Codex | 0/3 APPROVE | Blocking: ambiguous failure path (3/3), missing TeamReadFails test (1/3). NB: sentinel fields, AC-9 scope, two-circuit verification. |
| R2 | GPT-5.4, Sonnet 4.6, GPT-5.2-Codex | 0/3 APPROVE | Blocking: cancellation path (1/3). NB: club names (2/3), sentinel values (1/3), dialog cleanup (1/3), AC-12 supersession (1/3), TC-16 assertions (1/3), mock test (1/3). |
| R3 | GPT-5.4, Sonnet 4.6, GPT-5.2-Codex | 1/3 APPROVE | Blocking: FormatMatchForTitle missing (2/3), concurrency race (1/3, set aside — _matchLoadedGate serializes). NB: method xmldoc (1/3), test replacement clarity (1/3). All applied. |

**Status: APPROVED** — All blocking findings resolved. Concurrency concern set aside (existing `_matchLoadedGate` semaphore serializes match-loading operations; verified in source).
