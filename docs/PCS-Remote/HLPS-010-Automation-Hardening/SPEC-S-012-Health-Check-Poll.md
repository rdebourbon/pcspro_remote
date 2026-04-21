# SPEC-S-012: Health-Check Poll

| Field | Value |
|---|---|
| **Document** | SPEC-S-012-Health-Check-Poll.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-21 |
| **IS Step** | S-012 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md §2.9 |
| **Governing IS** | IS-010-Automation-Hardening.md |

---

## 1. Problem Statement

After a match is loaded (either via `LoadMatchAsync` or `UseCurrentMatchAsync`), PCS Pro can silently lose its server connection, have its match state change, or encounter scoring sync problems. The operator has no way to detect these conditions without manually checking PCS Pro. A periodic health-check poll reads PCS Pro state signals and pushes detected changes to the web UI, giving operators early warning of problems.

---

## 2. Design

### 2.1 New Interface: `IHealthCheckAutomation`

A new automation interface in `PcsRemote.Automation`:

```csharp
internal interface IHealthCheckAutomation
{
    HealthCheckResult ReadHealthSignals();
}
```

`ReadHealthSignals()` reads PCS Pro state signals via FlaUI and returns a snapshot. It must be safe to call rapidly and must not open dialogs or modify application state.

### 2.2 `HealthCheckResult` Record

New immutable record in `PcsRemote.Core`:

```csharp
public sealed record HealthCheckResult(
    bool IsMainWindowPresent,
    bool IsMatchLoaded,
    string? SyncStatus,
    string? WindowTitle,
    bool ProbeSucceeded);
```

- `IsMainWindowPresent`: `true` if the PCS Pro main window is still discoverable.
- `IsMatchLoaded`: `true` if the match-loaded detection signal (`twdScoreSummary`) is present.
- `SyncStatus`: the text content of the status bar element (`ScorePanelStatusBar`), or `null` if not readable.
- `WindowTitle`: the main window title, or `null` if the window is not present.
- `ProbeSucceeded`: `true` if the probe completed without FlaUI exceptions during element probing (after `FindMainWindow`). `false` when a COMException, ElementNotAvailableException, or similar transient FlaUI error interrupted element reads. When `false`, the other fields contain default values and must not be acted upon. The poll loop (§2.6 step 3) retains the previous `_lastHealthCheck` baseline and retries on the next cycle.

**`FindMainWindow` null semantics:** `PcsProWindowLocator.FindMainWindow()` returns `null` both for "window genuinely absent" and for some transient FlaUI errors (it catches exceptions internally). The health check treats `null` as **window genuinely absent** (`ProbeSucceeded = true`, `IsMainWindowPresent = false`). This is conservative — a transient error could trigger a false window-lost detection. However: (a) the poll only acts on *changes* from present→absent, so a single transient null is mitigated by the previous baseline, (b) the crash watcher monitors the Process object independently, and (c) the operator can `RetryAsync` to recover. This trade-off avoids requiring changes to the existing locator contract.

**Signal coverage note:** HLPS-010 §2.9 lists five signal categories: (1) live stream status, (2) server connection, (3) correct match loaded, (4) scoring sync status, (5) match status. This spec collapses (2), (4), and (5) into `SyncStatus` (all are status bar text). Signal (1) — live stream status — requires reading the `LiveStreamingControls` button text, which is a heavier FlaUI operation. It is deferred to a future HLPS and tracked in `DEFERRED-ITEMS.md` as DEF-005.

### 2.3 `FlaUiHealthCheckAutomation` Implementation

Implements `IHealthCheckAutomation`. Injected with `PcsProWindowLocator` and `ILogger<FlaUiHealthCheckAutomation>`.

`ReadHealthSignals()`:
1. Calls `_locator.FindMainWindow()` — if `null`, returns `HealthCheckResult(false, false, null, null, ProbeSucceeded: true)`. A null window is a valid signal (window genuinely absent), not a probe failure.
2. Reads the window title from the found window.
3. Probes for `twdScoreSummary` (match-loaded signal) — sets `IsMatchLoaded`.
4. Probes for status bar element by `ScorePanelStatusBar` class name — reads its text content for `SyncStatus`. If not found or read fails, `SyncStatus = null`.
5. Returns the populated `HealthCheckResult` with `ProbeSucceeded = true`.

Exception handling: Steps 1–4 are wrapped in a try-catch. If `FindMainWindow()` returns `null`, the method returns `HealthCheckResult(false, false, null, null, ProbeSucceeded: true)` — this is a valid signal (window genuinely absent), not a probe failure. If any FlaUI exception is thrown during steps 3–4 (element probing), the method returns `HealthCheckResult(false, false, null, null, ProbeSucceeded: false)`. The method never throws.

### 2.4 `FakeHealthCheckAutomation` Test Double

In `tests/PcsRemote.Automation.Tests`:

```csharp
internal sealed class FakeHealthCheckAutomation : IHealthCheckAutomation
{
    public HealthCheckResult Result { get; set; }
        = new(true, true, "Up to Date", "Play-Cricket Scorer Pro - Match 123", true);

    public HealthCheckResult ReadHealthSignals() => Result;
}
```

Configurable per-test via the `Result` property.

### 2.5 Health-Check Poll Lifecycle

The health-check poll is a `Task`-based loop **inside** `PcsProAutomationService`. It is NOT a separate `BackgroundService` — it runs in the same class to access the `_operationLock` and state machine directly.

**Fields:**

```csharp
private CancellationTokenSource? _healthPollCts;
private Task? _healthPollTask;
private HealthCheckResult? _lastHealthCheck;
```

**Start condition:** The `OnTransitioned` callback checks if the new state is `MatchLoaded`. If so, it calls `StartHealthPoll()`:

```
StartHealthPoll():
  1. If _healthPollTask is not null and not completed, return (idempotent — already running)
  2. Create new CancellationTokenSource → _healthPollCts
  3. Reset _lastHealthCheck = null
  4. _healthPollTask = Task.Run(() => HealthPollLoopAsync(_healthPollCts.Token))
```

**Stop condition — two modes:**

The `OnTransitioned` callback is synchronous (`Action<PcsProState>`) and runs *inside* `_operationLock` (called from `_stateMachine.Fire()` within `FireUnderLockAsync`). It must NOT await. When the state leaves `MatchLoaded`, the callback calls `CancelHealthPoll()` — a cancel-only operation:

```
CancelHealthPoll():
  1. _healthPollCts?.Cancel()
  // Do NOT await _healthPollTask here — that would deadlock if the poll
  // is waiting to reacquire _operationLock (held by the caller).
```

Full cleanup happens in `StopAsync` and `DisposeAsync`, called *before* acquiring `_operationLock`:

```
StopHealthPollAsync():
  1. _healthPollCts?.Cancel()
  2. if _healthPollTask is not null:
     try { await _healthPollTask } catch (OperationCanceledException) { }
  3. _healthPollCts?.Dispose(); _healthPollCts = null
  4. _healthPollTask = null
  5. _lastHealthCheck = null
```

`StopAsync` calls `StopHealthPollAsync()` **before** acquiring `_operationLock` for its stop sequence (matching the existing crash-watcher teardown pattern at lines 178–186). `DisposeAsync` calls `StopHealthPollAsync()` before its existing disposal.

**Restart:** If the state returns to `MatchLoaded` after leaving it (e.g., `ChangeMatchAsync` completes successfully), `OnTransitioned` calls `StartHealthPoll()` again — which creates a fresh CTS and loop.

### 2.6 Poll Loop Implementation

```
HealthPollLoopAsync(CancellationToken ct):
  Log at Information level: "Health-check poll starting, interval {IntervalSeconds}s"
  Clamp interval to [5, 60] with warning log if out of range

  LOOP:
    ct.ThrowIfCancellationRequested()

    1. Skip-guard: attempt to set _isMatchLoadedOperationInProgress via CAS
       (same Interlocked.CompareExchange pattern as RefreshScoreboardAsync etc.)
       - If NOT acquired (another FlaUI op in progress): log at Verbose, skip to step 6
       - If acquired: proceed to step 2

    2. FlaUI probe phase (protected by _isMatchLoadedOperationInProgress):
       try:
         - Guard: if state != MatchLoaded → clear flag, exit loop
         - result = _healthCheckAutomation.ReadHealthSignals()
       finally:
         - Interlocked.Exchange(ref _isMatchLoadedOperationInProgress, 0)

    3. If !result.ProbeSucceeded:
       - Log at Warning: "Health probe failed, skipping cycle"
       - Skip to step 6 (do NOT update _lastHealthCheck, do NOT act)

    4. Compare result against _lastHealthCheck (null on first cycle → no comparison):
       a. If IsMainWindowPresent changed from true → false:
          - Acquire _operationLock via WaitAsync(ct)
            (cancellation-aware — if cancelled, exit loop via OCE)
          - Guard: if state is Error or NotRunning → release lock, continue loop
            (crash watcher or StopAsync already handled it)
          - Fire Timeout trigger
          - Release lock, flush StateChanged events
          - Enqueue HealthAlert(WindowLost, "PCS Pro main window lost", result)
          - Exit loop (state is now Error, poll stops)

       b. Else if IsMatchLoaded changed from true → false (window still present):
          - Acquire _operationLock via WaitAsync(ct)
          - Guard: if state is Error or NotRunning → release lock, continue loop
          - Fire Timeout trigger
          - Release lock, flush StateChanged events
          - Enqueue HealthAlert(MatchLost, "Match-loaded signal lost", result)
          - Exit loop

       c. If SyncStatus changed (and both old and new are non-null):
          - Enqueue HealthAlert(SyncStatusChanged, "Sync status changed: {old} → {new}", result)
          - (No state transition — informational only, no lock needed)

    5. _lastHealthCheck = result (only if ProbeSucceeded)

    6. await Task.Delay(interval, ct)  // throws OCE on cancellation → exits loop
```

**Two-guard design rationale:** The poll uses `_isMatchLoadedOperationInProgress` (CAS) to prevent concurrent FlaUI access with `RefreshScoreboardAsync`, `CaptureScoreboardImageAsync`, `ChangeMatchAsync`, `StartStreamingAsync`, `StopStreamingAsync`, and `UseCurrentMatchAsync` — all of which use the same CAS guard. This is the correct serialization point for FlaUI operations. The poll uses `_operationLock` only for trigger-firing (step 4a/4b), matching the existing `FireErrorUnderLockAsync` pattern that serializes state-machine transitions with `StopAsync`, `RetryAsync`, and the crash watcher.

**Trigger choice rationale:** Both window-lost and match-lost fire the `Timeout` trigger. From `MatchLoaded`, the available triggers reaching `Error` are `Timeout` and `UnexpectedDialog`. `Timeout` is semantically correct — the poll detected an unexpected absence after a timeout-like probe interval. `UnexpectedDialog` is reserved for dialog detection during active operations.

**Process lifecycle:** The poll does NOT directly clean up `_process` or kill PCS Pro. It fires `Timeout → Error`, and the operator invokes `RetryAsync` (which handles full process lifecycle cleanup via `LaunchAndLoginAsync`). If the crash watcher also detects the process exit, its existing terminal-state guard (`CurrentState is Error or NotRunning`) ensures it silently skips the redundant transition.

**Terminal-state guard skip semantics:** When the guard at step 4a/4b triggers (state is already `Error` or `NotRunning`), the poll releases the lock and continues the loop. On the next iteration, the step 2 guard (`state != MatchLoaded`) will exit the loop cleanly.

### 2.7 Concurrency Contract

The poll uses a two-guard approach:

**FlaUI probe phase (step 1–2):** Uses `_isMatchLoadedOperationInProgress` (Interlocked CAS), the same guard used by `RefreshScoreboardAsync`, `CaptureScoreboardImageAsync`, `ChangeMatchAsync`, `StartStreamingAsync`, `StopStreamingAsync`, and `UseCurrentMatchAsync`. If the flag is already set (another FlaUI operation in progress), the poll cycle is **silently skipped**. This guarantees no concurrent FlaUI access.

**Trigger-fire phase (step 4a/4b):** Uses `_operationLock` via `WaitAsync(ct)` (cancellation-aware). This serializes state-machine transitions with `StopAsync`, `RetryAsync`, `LaunchAndLoginAsync`, and the crash watcher. The lock acquisition passes the poll's `CancellationToken` so the poll remains cancellable even while waiting for the lock.

**Crash-watcher / poll race:** Both the crash watcher and the health poll may detect PCS Pro disappearance. The crash watcher fires `Timeout` inside `_operationLock` with a terminal-state guard (line 483). The poll follows the same guard pattern. Whichever fires first succeeds; the other detects the terminal state and silently skips. No double-transition is possible.

### 2.8 Configuration

`PcsProOptions` gains:

```csharp
/// <summary>
/// Interval in seconds between health-check poll cycles.
/// Clamped to [5, 60] at poll start; out-of-range values produce a warning log.
/// </summary>
public int HealthCheckIntervalSeconds { get; set; } = 10;
```

### 2.9 New Event: `HealthAlert`

`HealthAlertEventArgs` (new record in `PcsRemote.Core`):

```csharp
public sealed record HealthAlertEventArgs(
    HealthAlertKind Kind,
    string Message,
    HealthCheckResult CurrentResult);

public enum HealthAlertKind
{
    WindowLost,
    MatchLost,
    SyncStatusChanged
}
```

`HealthAlert` events are enqueued inside the poll loop and raised **outside** `_operationLock`, following the same deferred-raise pattern as `FlushStateChangedEvents`. The poll loop collects pending alerts into a local list, then raises them after releasing the lock. This prevents re-entrant deadlock if a subscriber calls back into the service.

### 2.10 `IPcsProAutomationService` Interface Changes

New members added to the interface:
- `event EventHandler<HealthAlertEventArgs> HealthAlert;`

No new methods — the poll is fully internal. The consumer (web UI) subscribes to `HealthAlert` to show warnings. State transitions (window-lost, match-lost) also flow through the existing `StateChanged` event.

### 2.11 `AutomationDependencies` Update

`AutomationDependencies` gains an `IHealthCheckAutomation HealthCheckAutomation` parameter:

```csharp
internal sealed record AutomationDependencies(
    ILoginAutomation LoginAutomation,
    IMatchSelectionAutomation MatchSelectionAutomation,
    ITeamNamesAutomation TeamNamesAutomation,
    IScoreboardAutomation ScoreboardAutomation,
    IChangeMatchAutomation ChangeMatchAutomation,
    IStreamingAutomation StreamingAutomation,
    IHealthCheckAutomation HealthCheckAutomation);
```

DI registration in `PcsRemote.Web` adds `FlaUiHealthCheckAutomation`. Mock DI adds a no-op implementation.

### 2.12 Mock Implementation

`MockPcsProAutomationService` implements `HealthAlert` as a no-op event (never raised). The mock does not run a poll loop — no health-check automation dependency is needed.

---

## 3. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `HealthCheckResult` record exists in `PcsRemote.Core` with `IsMainWindowPresent`, `IsMatchLoaded`, `SyncStatus`, `WindowTitle`, `ProbeSucceeded` properties. |
| AC-2 | `HealthAlertEventArgs` record and `HealthAlertKind` enum exist in `PcsRemote.Core`. |
| AC-3 | `IHealthCheckAutomation` interface exists in `PcsRemote.Automation` with `ReadHealthSignals()`. |
| AC-4 | `FlaUiHealthCheckAutomation` implements `IHealthCheckAutomation` using `PcsProWindowLocator` and reads all four signals. FlaUI exceptions produce `ProbeSucceeded = false`. |
| AC-5 | Poll loop starts when state machine transitions to `MatchLoaded`. |
| AC-6 | Poll loop stops when state machine leaves `MatchLoaded`, and on `StopAsync`/`DisposeAsync`. |
| AC-7 | Poll probe phase uses `_isMatchLoadedOperationInProgress` CAS — skips cycle if flag is set. |
| AC-8 | Poll fires `Timeout` trigger (inside `_operationLock` via `WaitAsync(ct)` with terminal-state guard) when `IsMainWindowPresent` changes to `false`. |
| AC-9 | Poll fires `Timeout` trigger (inside `_operationLock` via `WaitAsync(ct)` with terminal-state guard) when `IsMatchLoaded` changes to `false` (window still present). |
| AC-10 | Poll raises `HealthAlert` event outside `_operationLock` for all three alert kinds. |
| AC-11 | `PcsProOptions.HealthCheckIntervalSeconds` defaults to `10`, clamped to `[5, 60]`. |
| AC-12 | `IPcsProAutomationService` exposes `HealthAlert` event. |
| AC-13 | `MockPcsProAutomationService` implements `HealthAlert` as no-op. |
| AC-14 | `FakeHealthCheckAutomation` test double exists with configurable `Result`. |
| AC-15 | Poll does NOT act on `ProbeSucceeded == false` results — skips cycle and retries next interval. |
| AC-16 | `CancelHealthPoll()` is called from `OnTransitioned` (cancel-only, no await). `StopHealthPollAsync()` is called from `StopAsync`/`DisposeAsync` (cancel, await, dispose). |
| AC-17 | `StartHealthPoll()` is idempotent — calling while poll is running returns without creating a second loop. |
| AC-18 | `AutomationDependencies` includes `IHealthCheckAutomation`. DI registration updated. |
| AC-19 | Build: 0W/0E. All existing tests pass. New tests cover poll-start, poll-stop, poll-skip (CAS guard), window-lost, match-lost, sync-change, probe-failure-skip, config clamping, cancellation-exit, crash-watcher/poll race, and OnTransitioned cancel-only behavior. |

---

## 4. Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| RISK-1 | FlaUI calls in poll are slow (>1s), causing poll drift | Medium | Low | Poll uses `Task.Delay` after each cycle (not fixed-rate), so drift is expected. Interval is a minimum gap, not a period. |
| RISK-2 | Status bar text format varies across PCS Pro versions | Medium | Low | `SyncStatus` is returned as raw string — no parsing. Consumer interprets. |
| RISK-3 | Poll detects false-positive window loss during PCS Pro modal dialog | Low | Medium | Modal dialogs do not hide the main window in the automation tree; they appear as child windows. The `ProbeSucceeded` flag prevents transient FlaUI errors from triggering false alerts. |
| RISK-4 | Concurrent state transition from poll + crash watcher or StopAsync | Low | Low | Both use `_operationLock` and terminal-state guards (`CurrentState is Error or NotRunning`). Whichever fires first succeeds; the other silently skips. Poll lock acquisition is cancellation-aware (`WaitAsync(ct)`). |
| RISK-5 | Transient FlaUI exception causes false window-lost detection | Medium | High | Mitigated by `ProbeSucceeded` flag — failed probes are skipped, not acted upon. Only a successful probe with `IsMainWindowPresent == false` triggers a state transition. |

---

## 5. Out of Scope

- Web UI changes to display health alerts (future HLPS).
- Automatic recovery actions (e.g., auto-retry on match-lost).
- Stream-death detection via YouTube API (separate from PCS Pro health).
- Live stream status signal from `LiveStreamingControls` button text (DEF-005 — deferred to future HLPS).
- Customisable alert thresholds (e.g., "alert only after N consecutive failures").
- Health check reporting/logging dashboard.

---

## 6. Traceability

| HLPS-010 Reference | Spec Section |
|---|---|
| §2.9 (GAP-011 Health-Check Poll) | §2.5–§2.7 (poll lifecycle and concurrency) |
| §2.9 Configuration | §2.8 (`HealthCheckIntervalSeconds`) |
| §2.9 Signals | §2.2 (`HealthCheckResult` fields + coverage note) |
| §2.9 Concurrency contract | §2.7 (`_operationLock.Wait(0)`, crash-watcher race) |
| §2.9 Lifecycle | §2.5 (start at MatchLoaded, stop on leave, CTS/task fields) |
| §2.9 Tests | AC-19 |
| H-SC-8 | AC-5, AC-7, AC-8, AC-9, AC-10, AC-15 |

---

## 7. Review History

| Round | Reviewers | Verdict | Key Findings Fixed |
|---|---|---|---|
| R1 | Opus (claude-opus-4.6), GPT (gpt-5.4) | NEEDS REVIEW | F-1/F-2: replaced non-existent `Shutdown`/`Error` triggers with `Timeout` (valid from MatchLoaded). F-3: trigger firing now inside `_operationLock` with terminal-state guard. F-4: live-stream signal deferred as DEF-005 with explicit note. F-5: poll no longer cleans up `_process` — routes through `Timeout → Error` for operator `RetryAsync`. F-6: terminal-state guard added. F-7: pseudocode shows explicit lock→fire→release→flush→raise sequence. F-8/F-13: all Shutdown references removed. F-9: crash-watcher race documented in §2.7. F-10: noted that state transitions flow through existing `StateChanged`. F-11: AC-16/AC-17 added for CTS lifecycle. F-12: signal coverage note added to §2.2. GPT-2: `HealthAlertEventArgs` compiles without EventArgs base in .NET (no constraint on TEventArgs). GPT-4: `ProbeSucceeded` flag added to distinguish transient FlaUI errors from genuine signal absence. GPT-5: CTS, task ownership, idempotent start, cancel+await stop fully specified in §2.5. GPT-6: _process cleanup removed from poll. GPT-7: HealthAlert justified — state transitions use existing StateChanged; HealthAlert is for non-transition informational alerts. GPT-8: FlaUiElementLocator → PcsProWindowLocator, AutomationDependencies update added (§2.11). |
| R2 | Opus (claude-opus-4.6), GPT (gpt-5.4) | APPROVED | Opus NF-1 HIGH: split StopHealthPoll into CancelHealthPoll (cancel-only from OnTransitioned) and StopHealthPollAsync (cancel+await from StopAsync/DisposeAsync) — prevents deadlock from synchronous callback awaiting poll task while holding _operationLock. Opus NF-2 MEDIUM: poll lock reacquisition in step 4a/4b now uses WaitAsync(ct) (cancellation-aware) instead of CancellationToken.None. Opus NF-3 LOW: ProbeSucceeded description reworded — fields contain defaults when false, poll retains previous baseline. GPT NF-1 CRITICAL: fixed concurrency contract — poll probe phase now uses _isMatchLoadedOperationInProgress (CAS) to serialize with FlaUI operations; _operationLock used only for trigger-firing (step 4a/4b). Two-guard design rationale documented. GPT NF-2 HIGH: FindMainWindow null semantics clarified — null treated as valid "window absent" signal per existing locator contract, with trade-off documented. GPT NF-3 HIGH: OnTransitioned stop fully specified as cancel-only. All 13 R1 PASS (Opus), all 8 R1 PASS (GPT). |
