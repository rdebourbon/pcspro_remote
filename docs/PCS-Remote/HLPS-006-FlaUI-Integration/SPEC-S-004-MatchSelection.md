# SPEC-S-004: Match Selection Automation — `GetTodaysMatchesAsync` and `LoadMatchAsync`

| Field | Value |
|---|---|
| **Document** | SPEC-S-004-MatchSelection.md |
| **Status** | IN REVIEW |
| **Version** | 0.2 |
| **Date** | 2026-04-14 |
| **Step** | IS-006 S-004 |
| **Governing HLPS** | HLPS-006-FlaUI-Integration.md v0.2 (APPROVED) |
| **Governing IS** | IS-006-FlaUI-Integration.md v0.4 (APPROVED) |
| **Dependencies** | S-003 DELIVERED — service enters `MatchSelection` state after successful login |

---

## 1. Objective

Implement `GetTodaysMatchesAsync` and `LoadMatchAsync` on `PcsProAutomationService`.

`GetTodaysMatchesAsync` drives PCS Pro from the match selection dialog through the search/spinner sequence, parses the DataGrid rows, filters to today's date, and returns the list. `LoadMatchAsync` selects a specific row and clicks "Open Read-Only," advancing the state machine to `MatchLoaded`.

Both methods follow the same structural conventions established in S-003: an `IMatchSelectionAutomation` interface isolates the raw FlaUI primitives; a pure `MatchRowParser` helper handles row text parsing; `FakeMatchSelectionAutomation` serves as the test double.

---

## 2. Background

### 2.1 State machine transitions

`GetTodaysMatchesAsync` is called when the service is in `MatchSelection` state. It drives through:

```
MatchSelection
  → [SearchTriggered]     → MatchSelectionSearching
  → [SpinnerGone]         → MatchSelectionReady
```

`LoadMatchAsync` is called from `MatchSelectionReady` and drives:

```
MatchSelectionReady
  → [MatchOpened]         → MatchLoaded
```

Any phase may transition to `Error` via `Timeout` or `UnexpectedDialog`.

### 2.2 Relevant constants (from `PcsProStateMachine`)

- `MatchSelectionSearchingTimeoutSeconds` = 30 — spinner must clear within this window
- `MatchSelectionReadyTimeoutSeconds` = 15 — match must open within this window

### 2.3 Unknown I-U-5

The exact text format of DataGrid rows is unknown and must be discovered via Inspect.exe on the garage PC during development. The `MatchRowParser` class will contain a `TODO_REPLACE_ON_GARAGE_PC` format constant placeholder. Unit tests for `MatchRowParser` use placeholder strings that are updated once the format is discovered. The row parsing contract (inputs, outputs, failure semantics) is fully specified here; only the format string is deferred.

### 2.4 Match row identity for `LoadMatchAsync`

`GetTodaysMatchesAsync` returns `MatchInfo` records with a `MatchId` that must be stable and unique enough for `LoadMatchAsync` to re-locate the corresponding DataGrid row. The `MatchId` strategy — e.g., derived from a row index, a unique column value, or a hash of the row text — is resolved during garage PC development as part of I-U-5 discovery.

---

## 3. New Components

### 3.1 `IMatchSelectionAutomation` (internal interface, `PcsRemote.Automation`)

Provides the raw FlaUI primitives for both `GetTodaysMatchesAsync` and `LoadMatchAsync`. Methods:

| Method | Purpose |
|---|---|
| `OpenMatchDialogAndSearch()` | Opens the match selection dialog in PCS Pro and triggers the search. AutomationId placeholders `TODO_REPLACE_ON_GARAGE_PC`. **Interaction method — may throw.** |
| `IsSpinnerVisible()` | Returns true when the loading spinner element is visible. AutomationId placeholder. Does not throw. |
| `IsUnexpectedDialogPresent()` | Returns true when an unexpected dialog is detected. Does not throw. |
| `TryCloseUnexpectedDialog()` | Attempts to dismiss the unexpected dialog. Does not throw. |
| `ReadDataGridRowTexts()` | Returns the text content of each DataGrid row as a list of strings. AutomationId placeholder. **Interaction method — may throw.** Returns empty list on failure by convention (service-level catch handles exceptions). |
| `SelectAndOpenMatch(MatchInfo match)` | Locates the DataGrid row corresponding to the given match (using `MatchId`) and clicks "Open Read-Only." AutomationId placeholders. **Interaction method — may throw.** |
| `IsMatchLoaded()` | Returns true when the match has loaded (dialog closed / match view visible). Does not throw. |

### 3.2 `FlaUiMatchSelectionAutomation` (stub, `PcsRemote.Automation`)

Production implementation of `IMatchSelectionAutomation`. All probe methods (`IsSpinnerVisible`, `IsUnexpectedDialogPresent`, `IsMatchLoaded`) return `false` as safe defaults. `ReadDataGridRowTexts()` returns an empty list as its safe default. The interaction methods (`OpenMatchDialogAndSearch`, `SelectAndOpenMatch`) throw `NotImplementedException` with a message referencing the garage PC session (AC-13 convention from S-003). Two or more `private const string` AutomationId fields are initialised to `"TODO_REPLACE_ON_GARAGE_PC"`.

### 3.3 `MatchRowParser` (pure internal static class, `PcsRemote.Automation`)

Parses a raw DataGrid row text string into a `MatchInfo` record. Contract:

- `TryParse(string rowText, out MatchInfo? result)` — returns `true` on success. Returns `false` and sets `result = null` if the row text does not match the expected format (defensive: never throws).
- `FilterToday(IEnumerable<MatchInfo> matches, DateOnly today)` — returns only those entries where `MatchDate == today`. The service passes a value derived from `_timeProvider` (not `DateOnly.Today` directly) to keep this testable. Accepts `today` as a parameter to remain a pure function with no clock dependency.
- Contains a `TODO_REPLACE_ON_GARAGE_PC` format constant or comment that must be updated during garage PC development once the actual text format is discovered.

### 3.4 `FakeMatchSelectionAutomation` (test double, `PcsRemote.Automation.Tests`)

Configurable test double:

| Property | Default | Purpose |
|---|---|---|
| `SpinnerVisible` | `false` | Controls `IsSpinnerVisible()` |
| `UnexpectedDialogPresent` | `false` | Controls `IsUnexpectedDialogPresent()` |
| `MatchLoaded` | `true` | Controls `IsMatchLoaded()` |
| `RowTexts` | one well-formed today-dated row | Returned by `ReadDataGridRowTexts()` |
| `ThrowOnInteraction` | `false` | Causes `OpenMatchDialogAndSearch` / `SelectAndOpenMatch` to throw |

Captures: `SearchTriggered` (bool), `OpenAttemptedFor` (MatchInfo?), `CloseDialogAttempted` (bool).

### 3.5 `MatchRowParserTests` (new test class, `PcsRemote.Automation.Tests`)

Standalone test class exercising `MatchRowParser` independently of the service (AC-22–AC-25).

---

## 4. `GetTodaysMatchesAsync` Behaviour

### 4.1 Pre-condition

Method may only be called when `CurrentState == MatchSelection`. If called from any other state, the result is undefined — the caller (OperationCoordinator or web hub) is responsible for only invoking from the correct state.

### 4.2 Phase — Search

1. Record `startTimestamp` for the 30-second search timeout.
2. Call `_matchSelectionAutomation.OpenMatchDialogAndSearch()`. If this throws, fall through to the interaction exception handler (§4.6).
3. Fire `SearchTriggered` trigger using `guardTerminal: true` (`MatchSelection → MatchSelectionSearching`). If the guard fires (state already terminal from crash watcher), return an empty list.
4. Wait 200ms (race condition guard — PCS Pro may not have started the spinner immediately). This delay is hard-coded, not configurable.

### 4.3 Phase — Wait for spinner

Poll `IsSpinnerVisible()` in a loop with a 200ms poll interval:

- On each iteration:
  - Check for terminal state (crash watcher may have fired): if in `Error` or `NotRunning`, abort and return empty list.
  - Check `IsUnexpectedDialogPresent()`: if true, close dialog, fire `UnexpectedDialog → Error`, return empty list.
  - If `IsSpinnerVisible()` is false: spinner cleared — proceed to §4.4.
  - Check search timeout (`MatchSelectionSearchingTimeoutSeconds`): if elapsed, fire `Timeout → Error` with reason `"Match search did not complete within {n} seconds"`, return empty list.
  - If `CancellationToken` is cancelled: fire `Timeout → Error` with reason `"Match search cancelled by caller"`, propagate `OperationCanceledException` to the caller.
  - Else: delay by poll interval, then loop.

### 4.4 Phase — Parse and filter

1. Fire `SpinnerGone` trigger using `guardTerminal: true` (`MatchSelectionSearching → MatchSelectionReady`). If the guard fires (crash watcher won the race), return an empty list.
2. Call `ReadDataGridRowTexts()`. If this throws, fall through to the interaction exception handler (§4.6).
3. For each row text, call `MatchRowParser.TryParse(rowText, out MatchInfo? m)`. Rows that fail to parse are logged at `Warning` and skipped.
4. Call `MatchRowParser.FilterToday(parsed, today)` where `today` is derived from `_timeProvider` to retain only today's matches.
5. If the filtered list is empty: fire `Timeout → Error` with reason `"No matches found for today"`, return empty list.
6. Return the filtered list.

### 4.5 Logging

- `OpenMatchDialogAndSearch` call: `Debug`
- `SearchTriggered` fired: `Debug`
- 200ms guard: no log required
- Spinner poll iterations: `Verbose` (not `Debug` — high frequency)
- Spinner cleared: `Debug`
- `SpinnerGone` fired: `Debug`
- Row parse failure (per row): `Warning`
- No matches today: `Warning`
- Timeout: `Warning`
- `Error` trigger: `Error`

### 4.6 Interaction exception handling

`OpenMatchDialogAndSearch` and `ReadDataGridRowTexts` are FlaUI interaction methods that may throw diverse exception types (COMException, ElementNotAvailableException, etc.). These are caught with a `catch (Exception)` block per spec §3.8 precedent (S-003). On catch: log at `Error`, fire `Timeout → Error` with reason `"Match selection interaction failed"`, return empty list.

---

## 5. `LoadMatchAsync` Behaviour

### 5.1 Pre-condition

Method may only be called when `CurrentState == MatchSelectionReady`. The caller is responsible for the state check.

### 5.2 Phase — Select and open

1. Call `_matchSelectionAutomation.SelectAndOpenMatch(match)`.
2. Record `startTimestamp` for timeout tracking.

### 5.3 Phase — Wait for match loaded

Poll `IsMatchLoaded()` in a loop with a 200ms poll interval:

- On each iteration:
  - Check for terminal state: if in `Error` or `NotRunning`, return immediately.
  - Check `IsUnexpectedDialogPresent()`: if true, close dialog, fire `UnexpectedDialog → Error`, return.
  - If `IsMatchLoaded()` is true: proceed to §5.4.
  - Check open timeout (`MatchSelectionReadyTimeoutSeconds`): if elapsed, fire `Timeout → Error` with reason `"Match did not open within {n} seconds"`, return.
  - If `CancellationToken` is cancelled: fire `Timeout → Error` with reason `"Match open cancelled by caller"`, propagate `OperationCanceledException`.
  - Else: delay by poll interval, then loop.

### 5.4 Phase — Complete

1. Fire `MatchOpened` trigger (`MatchSelectionReady → MatchLoaded`). Use `guardTerminal: true` (same pattern as `CredentialsEntered` in S-003 — crash watcher may have fired concurrently).

### 5.5 Interaction exception handling

`SelectAndOpenMatch` is a FlaUI interaction method that may throw diverse exception types (COMException, ElementNotAvailableException, etc.). Per spec §3.8 precedent (S-003), caught with a `catch (Exception)` block. On catch: log at `Error`, fire `Timeout → Error` with reason `"Match selection interaction failed"`, return.

`GetTodaysMatchesAsync` follows the same pattern for its interaction methods — see §4.6.

### 5.6 Logging

- `SelectAndOpenMatch` call: `Debug`
- `IsMatchLoaded` poll iterations: `Verbose`
- Match loaded: `Debug`
- `MatchOpened` fired: `Information`
- Timeout: `Warning`
- `Error` trigger: `Error`

---

## 6. DI Registration

`IMatchSelectionAutomation` → `FlaUiMatchSelectionAutomation` registered as singleton in `AutomationServiceCollectionExtensions`, alongside the existing `ILoginAutomation` registration.

## 7. Concurrency

`GetTodaysMatchesAsync` and `LoadMatchAsync` are lifecycle operations on shared PCS Pro UI state. Concurrent calls are prevented by the same `_operationLock` guard pattern used in `LaunchAndLoginAsync`: a non-blocking `Wait(0)` at entry throws `InvalidOperationException` if another lifecycle operation is already in progress.

---

## 8. Branch Strategy

Branch: `feature/IS-006-S-004-match-selection`

Commits should be small and follow the same pattern as S-003:
- Interface + stub + fake + parser skeleton
- `PcsProAutomationService` changes (`GetTodaysMatchesAsync`)
- `PcsProAutomationService` changes (`LoadMatchAsync`)
- Tests

---

## 9. Acceptance Criteria

### `GetTodaysMatchesAsync`

| ID | Criterion |
|---|---|
| AC-1 | When called from `MatchSelection` state, `SearchTriggered` is fired (with `guardTerminal: true`) and service transitions to `MatchSelectionSearching`; if terminal state already reached, returns empty list |
| AC-2 | `startTimestamp` is recorded before `OpenMatchDialogAndSearch()` is called, and the 30s timeout is measured from that point |
| AC-3 | A 200ms delay occurs between `SearchTriggered` firing and the first spinner poll |
| AC-4 | The poll loop continues while `IsSpinnerVisible()` returns `true` |
| AC-5 | When spinner clears within timeout, `SpinnerGone` is fired (with `guardTerminal: true`) and service transitions to `MatchSelectionReady`; if terminal state already reached, returns empty list |
| AC-6 | When 30s timeout elapses before spinner clears, service transitions to `Error` with a descriptive reason |
| AC-7 | When unexpected dialog appears during spinner wait, service transitions to `Error` via `UnexpectedDialog` trigger |
| AC-8 | Each DataGrid row text is passed through `MatchRowParser.TryParse` |
| AC-9 | Rows that fail to parse are skipped (not thrown), and a `Warning` is logged per skipped row |
| AC-10 | Only `MatchInfo` records where `MatchDate` equals the `today` value derived from `_timeProvider` are returned |
| AC-11 | When zero rows remain after today's date filter, service transitions to `Error` with reason `"No matches found for today"` |
| AC-12 | CancellationToken cancellation during spinner wait fires `Error` and propagates `OperationCanceledException` to the caller |
| AC-13 | If crash watcher has moved the service to `Error/NotRunning` before the method completes, the method returns empty list without attempting a second state transition |
| AC-14 | When `OpenMatchDialogAndSearch` throws, service transitions to `Error` with reason `"Match selection interaction failed"` |
| AC-15 | When `ReadDataGridRowTexts` throws, service transitions to `Error` with reason `"Match selection interaction failed"` |

### `LoadMatchAsync`

| ID | Criterion |
|---|---|
| AC-16 | When called from `MatchSelectionReady` state, `SelectAndOpenMatch` is called with the provided `MatchInfo` |
| AC-17 | When `IsMatchLoaded()` returns true within the 15s timeout, `MatchOpened` is fired (with `guardTerminal: true`) and service transitions to `MatchLoaded` |
| AC-18 | When 15s timeout elapses before match loads, service transitions to `Error` with a descriptive reason |
| AC-19 | When unexpected dialog appears during open wait, service transitions to `Error` via `UnexpectedDialog` trigger |
| AC-20 | CancellationToken cancellation during open wait fires `Error` and propagates `OperationCanceledException` to the caller |
| AC-21 | When `SelectAndOpenMatch` throws, service transitions to `Error` with reason `"Match selection interaction failed"` |

### `MatchRowParser`

| ID | Criterion |
|---|---|
| AC-22 | `TryParse` returns `true` and populates all `MatchInfo` fields for a well-formed row text |
| AC-23 | `TryParse` returns `false` (and does not throw) for malformed or empty row text |
| AC-24 | `FilterToday(matches, today)` returns only entries where `MatchDate` equals the provided `today` value |
| AC-25 | `FilterToday` returns an empty list when no entries match today (does not throw) |

### General

| ID | Criterion |
|---|---|
| AC-26 | `FlaUiMatchSelectionAutomation` probe methods (`IsSpinnerVisible`, `IsUnexpectedDialogPresent`, `IsMatchLoaded`) do not throw — return `false` or empty list |
| AC-27 | `FlaUiMatchSelectionAutomation` interaction methods (`OpenMatchDialogAndSearch`, `SelectAndOpenMatch`) throw `NotImplementedException` referencing garage PC session |
| AC-28 | `IMatchSelectionAutomation` is registered in DI as `FlaUiMatchSelectionAutomation` singleton |
| AC-29 | All existing tests continue to pass |

---

## 10. Review History

| Round | Date | Reviewers | Outcome | Issues Addressed |
|---|---|---|---|---|
| R1 | 2026-04-14 | Claude Opus 4.6, GPT-5.4 | NEEDS REVIEW | O1–O7, G1–G6 triaged; HIGH findings accepted and applied in v0.2 |
