# SPEC-S-006: Scoreboard Refresh, Capture, and Change Match

| Field | Value |
|---|---|
| **Document** | SPEC-S-006-ScoreboardAndChangeMatch.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-14 |
| **Step** | IS-006 S-006 |
| **Governing HLPS** | HLPS-006-FlaUI-Integration.md v0.2 (APPROVED) |
| **Governing IS** | IS-006-FlaUI-Integration.md v0.4 (APPROVED) |
| **Dependencies** | S-005 DELIVERED — service is in `MatchLoaded` state after `LoadMatchAsync` + `GetTeamNamesAsync` |

---

## 1. Objective

Implement three methods on `PcsProAutomationService`, all operating from `MatchLoaded` state:

1. **`RefreshScoreboardAsync`** — finds the settings cog element by HelpText, clicks it to open the popup menu, and clicks "Refresh All Scoreboards". If the menu renders outside the main window tree (I-U-4), falls back to `GetAllTopLevelWindows()` search.
2. **`CaptureScoreboardImageAsync`** — locates the scoreboard dockable tool window by ClassName and Name, captures it via `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)`, crops to the element's `BoundingRectangle`, encodes as JPEG at the quality from `ScoreboardOptions`, and returns the encoded `byte[]`.
3. **`ChangeMatchAsync`** — executes the FlaUI sequence discovered in I-U-6 to close the current match and return to the match selection dialog, then fires the `ChangeMatch` trigger.

All three methods follow the structural conventions from S-003 through S-005: per-operation automation interfaces, `FlaUi*` production stubs, `Fake*` test doubles, and `{Method}CoreAsync` extraction.

---

## 2. Background

### 2.1 State machine context

```
RefreshScoreboardAsync (no state change on success):
  MatchLoaded → [UnexpectedDialog] → Error
  MatchLoaded → [Timeout]          → Error

CaptureScoreboardImageAsync (no state change on success):
  MatchLoaded → [UnexpectedDialog] → Error
  MatchLoaded → [Timeout]          → Error

ChangeMatchAsync (state changes on success):
  MatchLoaded → [ChangeMatch]      → MatchSelection  ← success path
  MatchLoaded → [UnexpectedDialog] → Error
  MatchLoaded → [Timeout]          → Error
```

### 2.2 Unknowns I-U-4 and I-U-6

- **I-U-4**: The popup menu "Refresh All Scoreboards" may render outside the main window tree. The fallback strategy (`GetAllTopLevelWindows()` search) is already designed — no additional spec change is needed; runtime confirmation happens on the garage PC.
- **I-U-6**: The exact FlaUI sequence to close a loaded match and return to match selection is discovered with Inspect.exe on the garage PC. This unknown blocks only the `ExecuteChangeMatchSequence()` implementation inside `FlaUiChangeMatchAutomation`. The interface, service-layer design, and test double can be completed without I-U-6 being resolved (using a stub that throws `NotImplementedException`).

If I-U-6 resolution proves unexpectedly complex during garage PC development, `ChangeMatchAsync` may be deferred to S-007 while the two scoreboard methods ship as planned. This decision is made at delivery time. **If deferral occurs, it requires a formal IS amendment to IS-006 before S-006 can be marked DELIVERED** — shipping without `ChangeMatchAsync` without an IS amendment does not satisfy this step.

### 2.3 Sentinel return for `CaptureScoreboardImageAsync`

On any error path that does not re-throw, `CaptureScoreboardImageAsync` returns `Array.Empty<byte>()`. The `Error` state transition is the primary signal to callers; the sentinel prevents null reference issues in callers that do not check state before consuming the return value.

### 2.4 No internal timeout

All three methods rely on synchronous FlaUI / Win32 calls. A `CancellationTokenSource` deadline cannot interrupt a hung synchronous FlaUI call. Cancellation is checked at method entry; callers supply a deadline via `CancellationToken` if needed.

### 2.5 Shared concurrency guard

A single `_isMatchLoadedOperationInProgress` Interlocked flag is shared by all three methods. This prevents any two S-006 operations from running concurrently and mirrors the S-004 pattern where `GetTodaysMatchesAsync` and `LoadMatchAsync` share `_isMatchSelectionOperationInProgress`.

The `{Method}CoreAsync` extraction pattern (established and proven correct in S-005) is mandatory here: the `finally` block that resets the Interlocked flag lives in the outer public method, and the state check lives inside the Core method. This ensures a wrong-state throw can never permanently latch the flag.

### 2.6 Constructor parameter growth

After S-006, `PcsProAutomationService` would have 10 constructor parameters. This is addressed by introducing an automation-dependencies aggregate (an internal value type or class grouping all per-operation automation interfaces) that replaces the individually injected automation parameters. The constructor should be reduced to ≤ 7 parameters after this consolidation. The DI registration and all test helpers must be updated accordingly.

This refactor is part of the S-006 delivery, not a deferred concern.

---

## 3. New Interfaces, Stubs, and Fakes

### 3.1 `IScoreboardAutomation`

Internal interface in `PcsRemote.Automation`. Covers both `RefreshScoreboardAsync` and `CaptureScoreboardImageAsync`, which share the scoreboard UI domain.

| Method | Classification | Contract |
|---|---|---|
| `ClickSettingsCog()` | Interaction | Finds the settings cog element by HelpText and clicks it. May throw. |
| `ClickRefreshAllScoreboards()` | Interaction | Finds "Refresh All Scoreboards" in the opened popup (with `GetAllTopLevelWindows()` fallback per I-U-4) and clicks it. May throw. |
| `CaptureScoreboardImage() → byte[]` | Interaction | Locates the scoreboard dockable tool window by ClassName and Name; calls `PrintWindow` passing the tool window's HWND; the bitmap is captured in the tool window's own client coordinate space; crops to the element's `BoundingRectangle` (screen coordinates translated to bitmap-relative origin at window top-left); encodes as JPEG at `ScoreboardOptions.JpegQuality`; returns encoded bytes. May throw. Crop and HWND strategy are internal to `FlaUiScoreboardAutomation` and are verified during the garage PC session. |
| `IsUnexpectedDialogPresent() → bool` | Probe | Returns `true` when an unexpected dialog is present. Never throws. |
| `TryCloseUnexpectedDialog()` | Probe-like | Best-effort unexpected-dialog dismissal. Never throws. |

### 3.2 `FlaUiScoreboardAutomation`

Production stub implementing `IScoreboardAutomation`.
- `ClickSettingsCog()`, `ClickRefreshAllScoreboards()`, `CaptureScoreboardImage()`: throw `NotImplementedException` with a garage-PC session message, consistent with the S-003/S-004/S-005 convention.
- Two or more `private const string` fields (AutomationId / HelpText / ClassName) initialised to `"TODO_REPLACE_ON_GARAGE_PC"`.
- `IsUnexpectedDialogPresent()`: returns `false`. `TryCloseUnexpectedDialog()`: no-op.
- Receives `IOptions<ScoreboardOptions>` via constructor so `CaptureScoreboardImage()` can access `JpegQuality`.

### 3.3 `FakeScoreboardAutomation`

Test double implementing `IScoreboardAutomation`.

| Member | Type | Default | Purpose |
|---|---|---|---|
| `ThrowOnClickSettingsCog` | `bool` | `false` | Causes `ClickSettingsCog()` to throw |
| `ThrowOnClickRefreshAllScoreboards` | `bool` | `false` | Causes `ClickRefreshAllScoreboards()` to throw |
| `ThrowOnCaptureScoreboardImage` | `bool` | `false` | Causes `CaptureScoreboardImage()` to throw |
| `CapturedImageBytes` | `byte[]` | Arbitrary non-empty `byte[]` (e.g., `new byte[] { 0xFF, 0xD8 }`) — no valid JPEG structure required | Value returned by `CaptureScoreboardImage()` |
| `UnexpectedDialogPresent` | `bool` | `false` | Value returned by `IsUnexpectedDialogPresent()` |
| `ClickCogAttempted` | `bool` | `false` | Set to `true` on first `ClickSettingsCog()` call |
| `ClickRefreshAttempted` | `bool` | `false` | Set to `true` on first `ClickRefreshAllScoreboards()` call |
| `CaptureAttempted` | `bool` | `false` | Set to `true` on first `CaptureScoreboardImage()` call |
| `CloseUnexpectedDialogAttempted` | `bool` | `false` | Set to `true` on first `TryCloseUnexpectedDialog()` call |

### 3.4 `IChangeMatchAutomation`

Internal interface in `PcsRemote.Automation`.

| Method | Classification | Contract |
|---|---|---|
| `ExecuteChangeMatchSequence()` | Interaction | Executes the I-U-6 FlaUI sequence to close the current match. May throw. |
| `IsUnexpectedDialogPresent() → bool` | Probe | Never throws. |
| `TryCloseUnexpectedDialog()` | Probe-like | Never throws. |

### 3.5 `FlaUiChangeMatchAutomation`

Production stub. `ExecuteChangeMatchSequence()` throws `NotImplementedException` with garage-PC message. One or more `private const string` fields initialised to `"TODO_REPLACE_ON_GARAGE_PC"`. Probe methods: safe defaults.

### 3.6 `FakeChangeMatchAutomation`

Test double.

| Member | Type | Default | Purpose |
|---|---|---|---|
| `ThrowOnExecuteChangeMatchSequence` | `bool` | `false` | Causes `ExecuteChangeMatchSequence()` to throw |
| `UnexpectedDialogPresent` | `bool` | `false` | Value returned by `IsUnexpectedDialogPresent()` |
| `ExecuteChangeMatchAttempted` | `bool` | `false` | Set to `true` on first `ExecuteChangeMatchSequence()` call |
| `CloseUnexpectedDialogAttempted` | `bool` | `false` | Set to `true` on first `TryCloseUnexpectedDialog()` call |

---

## 4. Service Method Implementations

All three methods follow the `LoadMatchAsync` / `LoadMatchCoreAsync` structural pattern:

- **Public method**: entry log, Interlocked guard on `_isMatchLoadedOperationInProgress`, `try { await CoreAsync } finally { reset flag }`.
- **Core method**: state check, main logic in try/catch, OCE caught in outer catch.

The state check is always the first statement inside the Core method — inside the try/finally scope — so that a wrong-state throw can never permanently latch the Interlocked flag.

### 4.1 `RefreshScoreboardAsync`

Entry log message: `"RefreshScoreboardAsync starting; current state {State}"`

Core logic:

```
state check: require MatchLoaded

try:
  §4.1.1 — Entry unexpected-dialog check:
    if IsUnexpectedDialogPresent():
      TryCloseUnexpectedDialog()
      fire UnexpectedDialog: "Unexpected dialog blocked scoreboard refresh"
      return

  ct.ThrowIfCancellationRequested()

  §4.1.2 — Click settings cog:
    try: ClickSettingsCog()
    catch (non-OCE): log Error; fire Timeout: "Failed to open settings menu"; return

  §4.1.3 — Click refresh menu item:
    try: ClickRefreshAllScoreboards()
    catch (non-OCE): log Error; fire Timeout: "Failed to click Refresh All Scoreboards"; return

  log Information: "RefreshScoreboardAsync complete"

catch OCE:
  log Warning; fire Timeout: "RefreshScoreboardAsync was cancelled"; rethrow
```

### 4.2 `CaptureScoreboardImageAsync`

Entry log message: `"CaptureScoreboardImageAsync starting; current state {State}"`

Core logic:

```
state check: require MatchLoaded

try:
  §4.2.1 — Entry unexpected-dialog check:
    if IsUnexpectedDialogPresent():
      TryCloseUnexpectedDialog()
      fire UnexpectedDialog: "Unexpected dialog blocked scoreboard capture"
      return Array.Empty<byte>()

  ct.ThrowIfCancellationRequested()

  §4.2.2 — Capture image:
    try: bytes = CaptureScoreboardImage()
    catch (non-OCE): log Error; fire Timeout: "Failed to capture scoreboard image"; return Array.Empty<byte>()

  log Information: "CaptureScoreboardImageAsync complete — {ByteCount} bytes"
  return bytes

catch OCE:
  log Warning; fire Timeout: "CaptureScoreboardImageAsync was cancelled"; rethrow
```

### 4.3 `ChangeMatchAsync`

Entry log message: `"ChangeMatchAsync starting; current state {State}"`

Core logic:

```
state check: require MatchLoaded

try:
  §4.3.1 — Entry unexpected-dialog check:
    if IsUnexpectedDialogPresent():
      TryCloseUnexpectedDialog()
      fire UnexpectedDialog: "Unexpected dialog blocked match change"
      return

  ct.ThrowIfCancellationRequested()

  §4.3.2 — Execute change-match sequence:
    try: ExecuteChangeMatchSequence()
    catch (non-OCE): log Error; fire Timeout: "Change match sequence failed"; return

  §4.3.3 — Fire state transition:
    await FireUnderLockAsync(ChangeMatch, guardTerminal: true)
    log Information: "ChangeMatchAsync complete — state {State}"

catch OCE:
  log Warning; fire Timeout: "ChangeMatchAsync was cancelled"; rethrow
```

`guardTerminal: true` ensures that if the crash watcher fires `Error` between the FlaUI sequence and the `ChangeMatch` trigger, the trigger is silently skipped rather than throwing from Stateless.

---

## 5. DI Registration

`AutomationServiceCollectionExtensions` must register:
- `IScoreboardAutomation` → `FlaUiScoreboardAutomation` (singleton)
- `IChangeMatchAutomation` → `FlaUiChangeMatchAutomation` (singleton)

The automation-dependencies aggregate (§2.6) is also registered as a singleton, composing all automation interfaces.

---

## 6. Tests

Tests are added to `PcsProAutomationServiceTests.cs` in `PcsRemote.Automation.Tests`.

### 6.1 Test helpers

- `CreateServiceAtMatchLoadedAsync` already exists (added in S-005). No new helper is required; it accepts the automation-dependencies aggregate per the constructor refactor.

### 6.2 Acceptance criteria and test cases

| AC | Description | Test name pattern |
|---|---|---|
| AC-1 | `RefreshScoreboardAsync` happy path — `ClickSettingsCog` and `ClickRefreshAllScoreboards` both called; state stays `MatchLoaded` | `RefreshScoreboardAsync_HappyPath_CallsCogAndRefreshAndStaysInMatchLoaded` |
| AC-2 | Concurrent S-006 operation (shared guard, same method) → `InvalidOperationException` | `RefreshScoreboardAsync_WhenOperationAlreadyInProgress_ThrowsInvalidOperationException` |
| AC-3 | `ClickSettingsCog` throws → `Error` state; `Timeout` trigger; reason contains "settings menu" | `RefreshScoreboardAsync_WhenClickCogThrows_TransitionsToError` |
| AC-4 | `ClickRefreshAllScoreboards` throws (cog click succeeded) → `Error` state; `Timeout` trigger | `RefreshScoreboardAsync_WhenClickRefreshThrows_TransitionsToError` |
| AC-5 | Unexpected dialog at entry → `TryCloseUnexpectedDialog` called; `UnexpectedDialog` trigger; `Error` | `RefreshScoreboardAsync_WhenUnexpectedDialogPresent_ClosesAndFiresError` |
| AC-6 | Wrong state → `InvalidOperationException`; state unchanged | `RefreshScoreboardAsync_WhenNotInMatchLoadedState_ThrowsInvalidOperationException` |
| AC-7 | Cancellation → `Error` state; `OperationCanceledException` propagated | `RefreshScoreboardAsync_WhenAlreadyCancelled_ThrowsOCEAndTransitionsToError` |
| AC-8 | `CaptureScoreboardImageAsync` happy path — `CaptureScoreboardImage` called; non-empty `byte[]` returned; state stays `MatchLoaded` | `CaptureScoreboardImageAsync_HappyPath_ReturnsImageBytesAndStaysInMatchLoaded` |
| AC-9 | `CaptureScoreboardImage` throws → `Error` state; `Timeout` trigger; sentinel `byte[]` returned | `CaptureScoreboardImageAsync_WhenCaptureThrows_ReturnsSentinelAndTransitionsToError` |
| AC-10 | Unexpected dialog at entry → `TryCloseUnexpectedDialog` called; `UnexpectedDialog` trigger; `Error`; sentinel returned | `CaptureScoreboardImageAsync_WhenUnexpectedDialogPresent_ReturnsSentinelAndTransitionsToError` |
| AC-11 | Wrong state → `InvalidOperationException` | `CaptureScoreboardImageAsync_WhenNotInMatchLoadedState_ThrowsInvalidOperationException` |
| AC-12 | Cancellation → `Error` state; `OperationCanceledException` propagated | `CaptureScoreboardImageAsync_WhenAlreadyCancelled_ThrowsOCEAndTransitionsToError` |
| AC-13 | `ChangeMatchAsync` happy path — `ExecuteChangeMatchSequence` called; `ChangeMatch` trigger fired; state = `MatchSelection` | `ChangeMatchAsync_HappyPath_ExecutesSequenceAndTransitionsToMatchSelection` |
| AC-14 | `ExecuteChangeMatchSequence` throws → `Error` state; `Timeout` trigger | `ChangeMatchAsync_WhenSequenceThrows_TransitionsToError` |
| AC-15 | Unexpected dialog at entry → `TryCloseUnexpectedDialog` called; `UnexpectedDialog` trigger; `Error` | `ChangeMatchAsync_WhenUnexpectedDialogPresent_ClosesAndFiresError` |
| AC-16 | Wrong state → `InvalidOperationException` | `ChangeMatchAsync_WhenNotInMatchLoadedState_ThrowsInvalidOperationException` |
| AC-17 | Cancellation → `Error` state; `OperationCanceledException` propagated | `ChangeMatchAsync_WhenAlreadyCancelled_ThrowsOCEAndTransitionsToError` |
| AC-18 | Crash watcher fires `Error` before `ChangeMatch` trigger — `guardTerminal: true` silently skips the trigger without throwing | `ChangeMatchAsync_WhenCrashWatcherWins_NoDoubleTransition` |
| AC-19 | `IScoreboardAutomation` wired to `FlaUiScoreboardAutomation`; `IChangeMatchAutomation` wired to `FlaUiChangeMatchAutomation` in DI | Code review |
| AC-20 | All interaction methods on `FlaUiScoreboardAutomation` and `FlaUiChangeMatchAutomation` throw `NotImplementedException` with garage-PC message; `ClickRefreshAllScoreboards()` implementation contains the `GetAllTopLevelWindows()` fallback search path (I-U-4); crop correctness, JPEG quality, and occluded-window capture are verified during the garage PC session | Code review |
| AC-21 | All existing automated tests continue to pass | `dotnet test` |
| AC-22 | Cross-method shared guard: `CaptureScoreboardImageAsync` (or `ChangeMatchAsync`) is rejected with `InvalidOperationException` while `RefreshScoreboardAsync` is concurrently in progress — proves the flag is shared, not per-method | `CaptureScoreboardImageAsync_WhenRefreshInProgress_ThrowsInvalidOperationException` |
| AC-23 | Automation-dependencies aggregate: `PcsProAutomationService` constructor accepts ≤ 7 parameters after aggregate refactor; aggregate is registered as a singleton in DI and correctly composes all per-operation automation interfaces | Code review |

---

## 7. Structural Notes

### 7.1 `guardTerminal` applies only to `ChangeMatchAsync`

`RefreshScoreboardAsync` and `CaptureScoreboardImageAsync` do not fire a trigger on the success path (state stays `MatchLoaded`). No `guardTerminal` is required for those methods. `ChangeMatchAsync` fires `ChangeMatch` on success, so `guardTerminal: true` is required.

### 7.2 Unexpected-dialog check at entry vs. between steps

Unlike S-005 (which checked between dialog-open and read-names steps), all three S-006 methods perform the unexpected-dialog check at method entry, before any FlaUI interaction. This is appropriate because:
- The interactions are short and atomic — there is no meaningful inter-step window where a dialog could appear between FlaUI calls.
- If an unexpected dialog appears mid-interaction, the FlaUI call will throw; this is caught by the interaction catch block and routed to `Timeout` error — an acceptable outcome.
- Checking at entry is simpler and sufficient for all three flows.

### 7.3 Serilog structured logging

- Method entry (entry log before guard): `Information`
- Element lookups within `FlaUiScoreboardAutomation` (when implemented): `Debug`
- Success completion: `Information`
- Unexpected-dialog detected: `Warning`
- Interaction failure caught: `Error`
- Cancellation caught: `Warning`
- No string interpolation in log calls (project rule).

### 7.4 `_isMatchLoadedOperationInProgress` error message

When the shared Interlocked guard fires, the `InvalidOperationException` message is:
`"A match-loaded operation is already in progress."` — generic but unambiguous, consistent with S-004's `"A lifecycle operation is already in progress."` pattern.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-14 | Claude Opus 4.6, GPT-5.4 | APPROVED — 5 non-blocking findings resolved (shared-guard cross-method AC added as AC-22; aggregate AC added as AC-23; ChangeMatchAsync deferral policy clarified in §2.2; `CapturedImageBytes` default clarified; `CaptureScoreboardImage()` crop contract note added to §3.1) |
