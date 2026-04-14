# SPEC-S-005: Team Name Extraction — `GetTeamNamesAsync`

| Field | Value |
|---|---|
| **Document** | SPEC-S-005-TeamNames.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.3 |
| **Date** | 2026-04-14 |
| **Step** | IS-006 S-005 |
| **Governing HLPS** | HLPS-006-FlaUI-Integration.md v0.2 (APPROVED) |
| **Governing IS** | IS-006-FlaUI-Integration.md v0.4 (APPROVED) |
| **Dependencies** | S-004 DELIVERED — service enters `MatchLoaded` state after `LoadMatchAsync` |

---

## 1. Objective

Implement `GetTeamNamesAsync` on `PcsProAutomationService`.

`GetTeamNamesAsync` navigates to the Match Details/Teams dialog in PCS Pro while the service is in `MatchLoaded` state, reads the home and away team names, closes the dialog, and returns the pair as a `MatchTeams` record. On success the state machine remains in `MatchLoaded`. On any failure the service transitions to `Error` via the appropriate trigger.

The method follows the structural conventions established in S-003 and S-004: an `ITeamNamesAutomation` interface isolates the raw FlaUI primitives, and `FakeTeamNamesAutomation` serves as the test double.

---

## 2. Background

### 2.1 State machine context

`GetTeamNamesAsync` is called when the service is in `MatchLoaded` state. Unlike S-003 and S-004, this operation does **not** fire a trigger on success — the state remains `MatchLoaded`. Triggers are only fired on failure:

```
MatchLoaded (no state change on success)
  → [UnexpectedDialog]   → Error  (unexpected dialog detected)
  → [Timeout]            → Error  (cancellation, interaction failure, or operation timeout)
```

### 2.2 Return sentinel

`MatchTeams` is a non-nullable record type (`record MatchTeams(string HomeTeam, string AwayTeam)`). On any error path that does not re-throw, the method returns `new MatchTeams(string.Empty, string.Empty)` as a safe sentinel. The Error state transition is the primary signal to callers that the operation failed; the sentinel prevents null reference issues in the event a caller does not check state before using the return value.

`OperationCanceledException` is the only exception that escapes: it re-throws after the Error trigger is fired.

### 2.3 Unknowns I-U-2 and I-U-3

- **I-U-2**: AutomationId values for the Scoring menu item, Match Details/Teams dialog, and the home/away team ComboBox elements are unknown and must be discovered with Inspect.exe on the garage PC during development. All fields are initialised to `"TODO_REPLACE_ON_GARAGE_PC"` in the stub.

- **I-U-3**: The correct property access pattern for a read-only ComboBox value — `ValuePattern.Current.Value`, `SelectionPattern.Current.GetSelection()[0].Current.Name`, or element `.Name` — is unknown and resolved during garage PC development. The interface abstracts this behind a single method call, so the resolution does not affect the service-layer design.

### 2.4 No poll loop and no internal timeout

There is no long-running spinner to poll. The interaction sequence is a single-pass open → read → close. Since all FlaUI calls are synchronous, an internal `CancellationTokenSource` deadline cannot interrupt a hung FlaUI call — it would only take effect between async awaits. Therefore, **no internal timeout is added**. The method respects the caller's `CancellationToken` only; cancellation is checked at each `await` point. Callers that require a hard timeout should pass a `CancellationToken` with a deadline.

### 2.5 Concurrent operation guard

Follows the S-004 `Interlocked.CompareExchange` pattern. A new `_isTeamNamesOperationInProgress` int field (0 = idle, 1 = active) prevents concurrent calls to `GetTeamNamesAsync`. Concurrent calls throw `InvalidOperationException("A lifecycle operation is already in progress.")`.

---

## 3. New Components

### 3.1 `ITeamNamesAutomation` (internal interface, `PcsRemote.Automation`)

Provides the raw FlaUI primitives for `GetTeamNamesAsync`. Follows the probe/interaction classification from S-004.

| Method | Classification | Purpose |
|---|---|---|
| `OpenTeamsDialog()` | Interaction — may throw | Navigates to the Scoring menu and opens the Match Details/Teams dialog. AutomationId placeholders `TODO_REPLACE_ON_GARAGE_PC`. |
| `ReadHomeTeamName()` | Interaction — may throw | Returns the home team name string using the access pattern resolved for I-U-3. AutomationId placeholders. |
| `ReadAwayTeamName()` | Interaction — may throw | Returns the away team name string. AutomationId placeholders. |
| `TryCloseTeamsDialog()` | Probe-like — never throws | Best-effort close of the dialog. Called on both success and error paths. |
| `IsUnexpectedDialogPresent()` | Probe — never throws | Returns `true` when a dialog other than the expected teams dialog is present. |
| `TryCloseUnexpectedDialog()` | Probe-like — never throws | Best-effort dismissal of an unexpected dialog. |

### 3.2 `FlaUiTeamNamesAutomation` (stub, `PcsRemote.Automation`)

Production stub implementing `ITeamNamesAutomation`. All interaction methods (`OpenTeamsDialog`, `ReadHomeTeamName`, `ReadAwayTeamName`) throw `NotImplementedException` with a message referencing the garage PC session (consistent with S-003/S-004 convention). Probe-like methods (`TryCloseTeamsDialog`, `IsUnexpectedDialogPresent`, `TryCloseUnexpectedDialog`) are no-ops or return `false`. Two or more `private const string` AutomationId fields are initialised to `"TODO_REPLACE_ON_GARAGE_PC"`.

### 3.3 `FakeTeamNamesAutomation` (test double, `PcsRemote.Automation.Tests`)

Controllable test double implementing `ITeamNamesAutomation`. Configurable properties:

| Property | Default | Purpose |
|---|---|---|
| `ThrowOnOpenDialog` | `false` | When `true`, `OpenTeamsDialog()` throws `InvalidOperationException` |
| `ThrowOnReadHomeTeamName` | `false` | When `true`, `ReadHomeTeamName()` throws `InvalidOperationException` |
| `ThrowOnReadAwayTeamName` | `false` | When `true`, `ReadAwayTeamName()` throws `InvalidOperationException` |
| `HomeTeamName` | `"Home XI"` | Value returned by `ReadHomeTeamName()` |
| `AwayTeamName` | `"Away XI"` | Value returned by `ReadAwayTeamName()` |
| `UnexpectedDialogPresent` | `false` | Value returned by `IsUnexpectedDialogPresent()` |

Capture properties: `OpenAttempted` (bool), `CloseAttempted` (bool).

---

## 4. `GetTeamNamesAsync` — Detailed Flow

### 4.1 Entry guard

If `Interlocked.CompareExchange(ref _isTeamNamesOperationInProgress, 1, 0) != 0` → throw `InvalidOperationException`. Otherwise set the flag and proceed; clear it in a `finally` block.

If `CurrentState != MatchLoaded` → throw `InvalidOperationException` with a message identifying the wrong state. This guard runs after the Interlocked check and before any FlaUI interaction.

### 4.2 Open teams dialog

Call `_teamNamesAutomation.OpenTeamsDialog()`. If it throws:
- Call `TryCloseTeamsDialog()` (best-effort — the dialog may have partially opened)
- Log at `Error` level
- Fire `Timeout` trigger via `FireErrorUnderLockAsync` with reason `"Team names interaction failed"`
- Return sentinel

### 4.3 Unexpected dialog check

Call `IsUnexpectedDialogPresent()`. If `true`:
- Log at `Warning` level
- Call `TryCloseUnexpectedDialog()`
- Fire `UnexpectedDialog` trigger via `FireErrorUnderLockAsync` with reason `"An unexpected dialog appeared during team name extraction"`
- Return sentinel

### 4.4 Read team names

Call `ReadHomeTeamName()` then `ReadAwayTeamName()`. If either throws:
- Log at `Error` level
- Call `TryCloseTeamsDialog()` (best-effort — never throws)
- Fire `Timeout` trigger via `FireErrorUnderLockAsync` with reason `"Team names interaction failed"`
- Return sentinel

> **Note:** The unexpected-dialog check in §4.3 covers the post-open phase. If an unexpected dialog appears *during* a read (e.g., between the two read calls), the FlaUI interaction will throw and be routed through this interaction-failure path. This is intentional — checking for unexpected dialogs between each read would add complexity without meaningful benefit given the fast, synchronous nature of these calls.

### 4.5 Close dialog and return

Call `TryCloseTeamsDialog()` (best-effort). Log at `Information` level. Return `new MatchTeams(homeTeam, awayTeam)`. State remains `MatchLoaded`.

### 4.6 Cancellation

If the incoming `CancellationToken` is cancelled during any `await` (including `FireUnderLockAsync` awaits), catch `OperationCanceledException`:
- Call `TryCloseTeamsDialog()` (best-effort)
- Log at `Warning` level ("GetTeamNamesAsync: cancelled by caller")
- Fire `Timeout` trigger with reason `"Team name extraction cancelled by caller"`
- Re-throw the `OperationCanceledException`

### 4.7 Crash watcher race

`FireErrorUnderLockAsync` already guards against double-transition to `Error` (pre-existing `guardTerminal` behaviour established in S-003). No additional guard is required.

---

## 5. Modified Components

### 5.1 `PcsProStateMachine`

No changes. No new state, trigger, or timeout constant is needed for this step.

### 5.2 `PcsProAutomationService`

- Add `private int _isTeamNamesOperationInProgress;` field (Interlocked guard, 0 = idle).
- Add `ITeamNamesAutomation _teamNamesAutomation` field, injected via constructor.
- Replace the `NotImplementedException` stub with the full `GetTeamNamesAsync` implementation described in §4.

### 5.3 `AutomationServiceCollectionExtensions`

Register `ITeamNamesAutomation → FlaUiTeamNamesAutomation` as a singleton, consistent with `ILoginAutomation` and `IMatchSelectionAutomation` registrations.

---

## 6. Tests

### 6.1 Test location

`PcsRemote.Automation.Tests` — appended to `PcsProAutomationServiceTests.cs`.

### 6.2 Test helper

Add `CreateServiceAtMatchLoadedAsync()` — brings the service to `MatchLoaded` state ready for `GetTeamNamesAsync` testing. Builds on `CreateServiceAtMatchSelectionReadyAsync` (S-004) by firing the `MatchOpened` trigger on the state machine via the same reflection-based approach.

### 6.3 Acceptance criteria and test cases

| AC | Description | Test name pattern |
|---|---|---|
| AC-1 | Happy path — `OpenTeamsDialog` succeeds, names read, dialog closed, `MatchTeams` returned with correct values; state remains `MatchLoaded` | `GetTeamNamesAsync_HappyPath_ReturnsTeamNamesAndStateRemainsMatchLoaded` |
| AC-2 | Concurrent call during active operation → `InvalidOperationException` thrown immediately | `GetTeamNamesAsync_ConcurrentCall_ThrowsInvalidOperationException` |
| AC-3 | `OpenTeamsDialog` throws → `TryCloseTeamsDialog` called; `Error` state; sentinel `MatchTeams` returned; reason `"Team names interaction failed"` | `GetTeamNamesAsync_OpenDialogThrows_ClosesDialogFiresErrorReturnsSentinel` |
| AC-4 | `ReadHomeTeamName` throws → `TryCloseTeamsDialog` called; `Error` state; sentinel returned | `GetTeamNamesAsync_ReadHomeTeamThrows_ClosesDialogFiresError` |
| AC-5 | `ReadAwayTeamName` throws (home read succeeds) → `TryCloseTeamsDialog` called; `Error` state; sentinel returned | `GetTeamNamesAsync_ReadAwayTeamThrows_ClosesDialogFiresError` |
| AC-6 | Unexpected dialog present after open → `TryCloseUnexpectedDialog` called; `Error` via `UnexpectedDialog` trigger; reason contains "unexpected dialog"; sentinel returned | `GetTeamNamesAsync_UnexpectedDialog_ClosesAndFiresError` |
| AC-7 | Cancelled `CancellationToken` → `Error` state; `OperationCanceledException` propagated; `TryCloseTeamsDialog` called | `GetTeamNamesAsync_Cancelled_FiresErrorAndPropagatesOCE` |
| AC-8 | Crash watcher fires `Error` first (state already terminal before `GetTeamNamesAsync` can transition) → `FireErrorUnderLockAsync` does not double-transition; sentinel returned | `GetTeamNamesAsync_CrashWatcherWins_NoDoubleTransition` |
| AC-9 | `TryCloseTeamsDialog` called on success path (dialog is closed after successful read) | Covered by AC-1 (`CloseAttempted == true`) |
| AC-10 | `ITeamNamesAutomation` wired to `FlaUiTeamNamesAutomation` in DI registration | Compile-time / code review |
| AC-11 | `FlaUiTeamNamesAutomation` interaction methods throw `NotImplementedException` with garage-PC message | Code review |
| AC-12 | `FlaUiTeamNamesAutomation` probe-like methods do not throw | Code review |
| AC-13 | All existing 402 tests continue to pass | `dotnet test` |
| AC-14 | Called when service is not in `MatchLoaded` state → `InvalidOperationException` thrown; state unchanged | `GetTeamNamesAsync_WrongState_ThrowsInvalidOperationException` |

---

## 7. Structural Notes

### 7.1 No `FireUnderLockAsync` on success path

Unlike S-003/S-004, no trigger is fired on the success path. `FireUnderLockAsync` is only called via `FireErrorUnderLockAsync` on failure paths.

### 7.2 `guardTerminal` not required on `TryCloseTeamsDialog`

Since `TryCloseTeamsDialog` is a probe-like method that never throws and does not touch the state machine, it requires no guard. It is safe to call in both success and failure paths.

### 7.3 Serilog structured logging

- `OpenTeamsDialog` call → `Debug` ("GetTeamNamesAsync: opening teams dialog")
- Dialog open → `Debug` ("GetTeamNamesAsync: teams dialog opened, reading team names")
- Names read → `Debug` ("GetTeamNamesAsync: home={HomeTeam}, away={AwayTeam}")
- Success → `Information` ("GetTeamNamesAsync complete — home={HomeTeam}, away={AwayTeam}")
- Error paths → `Error` or `Warning` as appropriate (consistent with S-004 conventions)
- No string interpolation in log calls (project rule).

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-14 | Claude Opus 4.6, GPT-5.4 | NEEDS REVIEW — 4 findings accepted; 3 rejected (see triage below) |
| R2 | 2026-04-14 | Claude Opus 4.6, GPT-5.4 | APPROVED — Opus: no new issues; GPT: 1 LOW finding (§4.6 log-level ambiguity) resolved in v0.3 |

### R1 Triage

| Finding | Source | R1 Severity | Disposition | Resolution |
|---|---|---|---|---|
| Single `ThrowOnReadTeamName` flag makes AC-4/AC-5 independently untestable | Opus | HIGH | Accept | Split into `ThrowOnReadHomeTeamName` + `ThrowOnReadAwayTeamName` in §3.3 |
| Internal 15s timeout CTS cannot interrupt synchronous FlaUI calls | GPT | HIGH→MEDIUM | Accept | Removed `TeamNamesTimeoutSeconds` and CTS from §2.4 and §5.1; method respects caller CT only |
| No state precondition guard — wrong-state invocation undefined | Opus | MEDIUM | Accept | Added `CurrentState != MatchLoaded` guard to §4.1; added AC-14 |
| §4.2 open-failure omits `TryCloseTeamsDialog()` | Opus | LOW | Accept | Added best-effort close to §4.2 error path |
| Unexpected dialog not uniformly checked during read phase | GPT | MEDIUM→LOW | Accept (clarification) | Added explanatory note to §4.4; routing via interaction-failure path is intentional |
| Concurrency scoped only to re-entrant calls | GPT | HIGH | Reject | State machine ordering (MatchLoaded vs MatchSelection) provides natural cross-operation exclusion |
| Web-UI outcome unowned | GPT | HIGH | Reject | Web-UI consumption is implemented in IS-003; out of scope for this spec |
| Logging levels: Warning vs Error for unexpected dialog | GPT | MEDIUM | Reject | Warning for unexpected-dialog-handled condition is consistent with S-004 convention; §7.3 covers level policy |
