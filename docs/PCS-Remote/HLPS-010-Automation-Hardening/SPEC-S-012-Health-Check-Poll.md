# SPEC-S-012: Health-Check Poll

| Field | Value |
|---|---|
| **Document** | SPEC-S-012-Health-Check-Poll.md |
| **Status** | IN REVIEW |
| **Version** | 0.2 |
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
- `ProbeSucceeded`: `true` if the probe completed without FlaUI exceptions. `false` when a COMException, ElementNotAvailableException, or similar transient FlaUI error interrupted the read. When `false`, all other fields reflect the last successfully-read values (or defaults for the first probe). The poll loop does NOT act on a failed probe — it treats it as "no data" and retries on the next cycle.

**Signal coverage note:** HLPS-010 §2.9 lists five signal categories: (1) live stream status, (2) server connection, (3) correct match loaded, (4) scoring sync status, (5) match status. This spec collapses (2), (4), and (5) into `SyncStatus` (all are status bar text). Signal (1) — live stream status — requires reading the `LiveStreamingControls` button text, which is a heavier FlaUI operation. It is deferred to a future HLPS and tracked in `DEFERRED-ITEMS.md` as DEF-005.

### 2.3 `FlaUiHealthCheckAutomation` Implementation

Implements `IHealthCheckAutomation`. Injected with `PcsProWindowLocator` and `ILogger<FlaUiHealthCheckAutomation>`.

`ReadHealthSignals()`:
1. Calls `_locator.FindMainWindow()` — if `null`, returns `HealthCheckResult(false, false, null, null, ProbeSucceeded: true)`. A null window is a valid signal (window genuinely absent), not a probe failure.
2. Reads the window title from the found window.
3. Probes for `twdScoreSummary` (match-loaded signal) — sets `IsMatchLoaded`.
4. Probes for status bar element by `ScorePanelStatusBar` class name — reads its text content for `SyncStatus`. If not found or read fails, `SyncStatus = null`.
5. Returns the populated `HealthCheckResult` with `ProbeSucceeded = true`.

Exception handling: all FlaUI calls are wrapped in a single outer try-catch. If any FlaUI exception is thrown (COMException, ElementNotAvailableException, etc.), the method returns `HealthCheckResult(false, false, null, null, ProbeSucceeded: false)`. It never throws.

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

**Stop condition:** When the state machine leaves `MatchLoaded` (detected in `OnTransitioned` — new state is NOT `MatchLoaded` AND _healthPollTask is running), or when `StopAsync`/`DisposeAsync` is called:

```
StopHealthPoll():
  1. _healthPollCts?.Cancel()
  2. if _healthPollTask is not null, await _healthPollTask (suppress OperationCanceledException)
  3. _healthPollCts?.Dispose(); _healthPollCts = null
  4. _healthPollTask = null
  5. _lastHealthCheck = null
```

`StopAsync` calls `StopHealthPoll()` before its existing cleanup. `DisposeAsync` calls `StopHealthPoll()` before its existing disposal.

**Restart:** If the state returns to `MatchLoaded` after leaving it (e.g., `ChangeMatchAsync` completes successfully), `OnTransitioned` calls `StartHealthPoll()` again — which creates a fresh CTS and loop.

### 2.6 Poll Loop Implementation

```
HealthPollLoopAsync(CancellationToken ct):
  Log at Information level: "Health-check poll starting, interval {IntervalSeconds}s"
  Clamp interval to [5, 60] with warning log if out of range

  LOOP:
    ct.ThrowIfCancellationRequested()

    1. Attempt _operationLock.Wait(0) (try-acquire, no block)
       - If lock NOT acquired: log at Verbose, skip to step 5
       - If lock acquired: proceed to step 2

    2. Inside lock (try-finally to guarantee release):
       - Guard: if state != MatchLoaded → release lock, log, exit loop
       - result = _healthCheckAutomation.ReadHealthSignals()
       - Release lock

    3. If !result.ProbeSucceeded:
       - Log at Warning: "Health probe failed, skipping cycle"
       - Skip to step 5 (do NOT update _lastHealthCheck, do NOT act)

    4. Compare result against _lastHealthCheck (null on first cycle → no comparison):
       a. If IsMainWindowPresent changed from true → false:
          - Reacquire _operationLock
          - Guard: if state is Error or NotRunning → release lock, skip (crash watcher or StopAsync already handled it)
          - Fire Timeout trigger via FireErrorUnderLockAsync pattern (Timeout → Error)
          - Release lock, flush StateChanged events
          - Enqueue HealthAlert(WindowLost, "PCS Pro main window lost", result)
          - Exit loop (state is now Error, poll stops)

       b. Else if IsMatchLoaded changed from true → false (window still present):
          - Reacquire _operationLock
          - Guard: if state is Error or NotRunning → release lock, skip
          - Fire Timeout trigger via FireErrorUnderLockAsync pattern (Timeout → Error)
          - Release lock, flush StateChanged events
          - Enqueue HealthAlert(MatchLost, "Match-loaded signal lost", result)
          - Exit loop

       c. If SyncStatus changed (and both old and new are non-null):
          - Enqueue HealthAlert(SyncStatusChanged, "Sync status changed: {old} → {new}", result)
          - (No state transition — informational only)

    5. _lastHealthCheck = result (only if ProbeSucceeded)

    6. await Task.Delay(interval, ct)  // throws OCE on cancellation → exits loop
```

**Trigger choice rationale:** Both window-lost and match-lost fire the `Timeout` trigger. From `MatchLoaded`, the available triggers reaching `Error` are `Timeout` and `UnexpectedDialog`. `Timeout` is semantically correct — the poll detected an unexpected absence after a timeout-like probe interval. `UnexpectedDialog` is reserved for dialog detection during active operations.

**Process lifecycle:** The poll does NOT directly clean up `_process` or kill PCS Pro. It fires `Timeout → Error`, and the operator invokes `RetryAsync` (which handles full process lifecycle cleanup via `LaunchAndLoginAsync`). If the crash watcher also detects the process exit, its existing terminal-state guard (`CurrentState is Error or NotRunning`) ensures it silently skips the redundant transition.

### 2.7 Concurrency Contract

The poll uses `_operationLock.Wait(0)` (synchronous try-acquire). If the lock is held by `RefreshScoreboardAsync`, `CaptureScoreboardImageAsync`, `ChangeMatchAsync`, `StartStreamingAsync`, `StopStreamingAsync`, or any other operation, the poll cycle is **silently skipped**. This guarantees no concurrent FlaUI access.

State-machine triggers from the poll are fired inside `_operationLock` using the `FireErrorUnderLockAsync` pattern: reacquire lock → terminal-state guard → fire → release → flush. This prevents races with the crash watcher and concurrent `StopAsync`.

The poll does NOT use `_isMatchLoadedOperationInProgress` — it uses the `_operationLock` directly because the poll must also respect lifecycle operations (like `StopAsync`), not just MatchLoaded-phase operations.

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
| AC-7 | Poll loop uses `_operationLock.Wait(0)` — skips cycle if lock is held. |
| AC-8 | Poll fires `Timeout` trigger (inside `_operationLock` with terminal-state guard) when `IsMainWindowPresent` changes to `false`. |
| AC-9 | Poll fires `Timeout` trigger (inside `_operationLock` with terminal-state guard) when `IsMatchLoaded` changes to `false` (window still present). |
| AC-10 | Poll raises `HealthAlert` event outside `_operationLock` for all three alert kinds. |
| AC-11 | `PcsProOptions.HealthCheckIntervalSeconds` defaults to `10`, clamped to `[5, 60]`. |
| AC-12 | `IPcsProAutomationService` exposes `HealthAlert` event. |
| AC-13 | `MockPcsProAutomationService` implements `HealthAlert` as no-op. |
| AC-14 | `FakeHealthCheckAutomation` test double exists with configurable `Result`. |
| AC-15 | Poll does NOT act on `ProbeSucceeded == false` results — skips cycle and retries next interval. |
| AC-16 | `StopHealthPoll()` cancels CTS, awaits poll task, and disposes — called from `StopAsync` and `DisposeAsync`. |
| AC-17 | `StartHealthPoll()` is idempotent — calling while poll is running returns without creating a second loop. |
| AC-18 | `AutomationDependencies` includes `IHealthCheckAutomation`. DI registration updated. |
| AC-19 | Build: 0W/0E. All existing tests pass. New tests cover poll-start, poll-stop, poll-skip, window-lost, match-lost, sync-change, probe-failure-skip, config clamping, cancellation-exit, and crash-watcher/poll race. |

---

## 4. Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| RISK-1 | FlaUI calls in poll are slow (>1s), causing poll drift | Medium | Low | Poll uses `Task.Delay` after each cycle (not fixed-rate), so drift is expected. Interval is a minimum gap, not a period. |
| RISK-2 | Status bar text format varies across PCS Pro versions | Medium | Low | `SyncStatus` is returned as raw string — no parsing. Consumer interprets. |
| RISK-3 | Poll detects false-positive window loss during PCS Pro modal dialog | Low | Medium | Modal dialogs do not hide the main window in the automation tree; they appear as child windows. The `ProbeSucceeded` flag prevents transient FlaUI errors from triggering false alerts. |
| RISK-4 | Concurrent state transition from poll + crash watcher or StopAsync | Low | Low | Both use `_operationLock` and terminal-state guards (`CurrentState is Error or NotRunning`). Whichever fires first succeeds; the other silently skips. |
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
