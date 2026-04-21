# SPEC-S-012: Health-Check Poll

| Field | Value |
|---|---|
| **Document** | SPEC-S-012-Health-Check-Poll.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
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
    string? WindowTitle);
```

- `IsMainWindowPresent`: `true` if the PCS Pro main window is still discoverable.
- `IsMatchLoaded`: `true` if the match-loaded detection signal (`twdScoreSummary`) is present.
- `SyncStatus`: the text content of the status bar element (`ScorePanelStatusBar`), or `null` if not readable.
- `WindowTitle`: the main window title, or `null` if the window is not present.

### 2.3 `FlaUiHealthCheckAutomation` Implementation

Implements `IHealthCheckAutomation`. Injected with `FlaUiElementLocator` and `ILogger<FlaUiHealthCheckAutomation>`.

`ReadHealthSignals()`:
1. Calls `_locator.FindMainWindow()` — if `null`, returns `HealthCheckResult(false, false, null, null)`.
2. Reads the window title from the found window.
3. Probes for `twdScoreSummary` (match-loaded signal) — sets `IsMatchLoaded`.
4. Probes for status bar element by `ScorePanelStatusBar` class name — reads its text content for `SyncStatus`. If not found or read fails, `SyncStatus = null`.
5. Returns the populated `HealthCheckResult`.

All FlaUI calls are wrapped in try-catch — exceptions return a degraded result (partial nulls) rather than throwing. This is a probe, not an operation.

### 2.4 `FakeHealthCheckAutomation` Test Double

In `tests/PcsRemote.Automation.Tests`:

```csharp
internal sealed class FakeHealthCheckAutomation : IHealthCheckAutomation
{
    public HealthCheckResult Result { get; set; }
        = new(true, true, "Up to Date", "Play-Cricket Scorer Pro - Match 123");

    public HealthCheckResult ReadHealthSignals() => Result;
}
```

Configurable per-test via the `Result` property.

### 2.5 Health-Check Poll Lifecycle

The health-check poll is a `Task`-based loop **inside** `PcsProAutomationService`. It is NOT a separate `BackgroundService` — it runs in the same class to access the `_operationLock` and state machine directly.

**Start condition:** The poll loop starts when the state machine transitions to `MatchLoaded`. This is detected in the existing `OnTransitioned` callback.

**Stop condition:** The poll loop stops when:
- The state machine leaves `MatchLoaded` (any transition away — `ChangeMatch`, `Error`, `Shutdown`, `Stop`).
- `StopAsync` is called.
- `DisposeAsync` is called.

**Restart:** If the state returns to `MatchLoaded` after leaving it (e.g., `ChangeMatchAsync` completes successfully), the poll restarts.

### 2.6 Poll Loop Implementation

```
LOOP (every HealthCheckIntervalSeconds):
  1. Attempt _operationLock.Wait(0) (try-acquire, no block)
     - If lock NOT acquired: skip this cycle (log at Verbose level), continue loop
     - If lock acquired: proceed to step 2

  2. Inside lock:
     - Check state == MatchLoaded; if not, release lock, stop loop
     - Call _healthCheckAutomation.ReadHealthSignals()
     - Release lock

  3. Compare result against previous result:
     - If IsMainWindowPresent changed to false → fire Shutdown trigger → raise HealthAlert event
     - If IsMatchLoaded changed to false → fire Error trigger → raise HealthAlert event
     - If SyncStatus changed → raise HealthAlert event (informational, no state transition)

  4. Store result as _lastHealthCheck for next comparison

  5. await Task.Delay(interval, cancellationToken)
```

### 2.7 Concurrency Contract

The poll uses `_operationLock.Wait(0)` (synchronous try-acquire). If the lock is held by `RefreshScoreboardAsync`, `CaptureScoreboardImageAsync`, `ChangeMatchAsync`, `StartStreamingAsync`, `StopStreamingAsync`, or any other operation, the poll cycle is **silently skipped**. This guarantees no concurrent FlaUI access.

The poll does NOT use `_isMatchLoadedOperationInProgress` — it uses the `_operationLock` directly because the poll must also respect lifecycle operations (like `StopAsync`), not just MatchLoaded-phase operations.

### 2.8 Configuration

`PcsProOptions` gains:

```csharp
public int HealthCheckIntervalSeconds { get; set; } = 10;
```

Clamped at poll-start to `[5, 60]` range. Values outside this range are clamped with a warning log.

### 2.9 New Event: `HealthAlert`

```csharp
event EventHandler<HealthAlertEventArgs> HealthAlert;
```

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

Raised outside `_operationLock` to prevent re-entrant deadlock (same pattern as `FlushStateChangedEvents`).

### 2.10 `IPcsProAutomationService` Interface Changes

New members added to the interface:
- `event EventHandler<HealthAlertEventArgs> HealthAlert;`

No new methods — the poll is fully internal. The consumer (web UI) subscribes to `HealthAlert` to show warnings.

### 2.11 Mock Implementation

`MockPcsProAutomationService` implements `HealthAlert` as a no-op event (never raised). The mock does not run a poll loop.

### 2.12 Process Lifecycle — State Transitions from Poll

When the poll detects `IsMainWindowPresent == false`:
- Fire `Shutdown` trigger to transition to `NotRunning`.
- Clean up `_process` reference (if any).
- Raise `HealthAlert(WindowLost, ...)`.

When the poll detects `IsMatchLoaded == false` (but window still present):
- Fire `Error` trigger to transition to `Error`.
- Raise `HealthAlert(MatchLost, ...)`.

`SyncStatusChanged` is informational only — no state transition.

---

## 3. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `HealthCheckResult` record exists in `PcsRemote.Core` with `IsMainWindowPresent`, `IsMatchLoaded`, `SyncStatus`, `WindowTitle` properties. |
| AC-2 | `HealthAlertEventArgs` record and `HealthAlertKind` enum exist in `PcsRemote.Core`. |
| AC-3 | `IHealthCheckAutomation` interface exists in `PcsRemote.Automation` with `ReadHealthSignals()`. |
| AC-4 | `FlaUiHealthCheckAutomation` implements `IHealthCheckAutomation` and reads all four signals. |
| AC-5 | Poll loop starts when state machine transitions to `MatchLoaded`. |
| AC-6 | Poll loop stops when state machine leaves `MatchLoaded`. |
| AC-7 | Poll loop uses `_operationLock.Wait(0)` — skips cycle if lock is held. |
| AC-8 | Poll fires `Shutdown` trigger when `IsMainWindowPresent` changes to `false`. |
| AC-9 | Poll fires `Error` trigger when `IsMatchLoaded` changes to `false` (window still present). |
| AC-10 | Poll raises `HealthAlert` event for all three alert kinds. |
| AC-11 | `PcsProOptions.HealthCheckIntervalSeconds` defaults to `10`, clamped to `[5, 60]`. |
| AC-12 | `IPcsProAutomationService` exposes `HealthAlert` event. |
| AC-13 | `MockPcsProAutomationService` implements `HealthAlert` as no-op. |
| AC-14 | `FakeHealthCheckAutomation` test double exists with configurable `Result`. |
| AC-15 | Build: 0W/0E. All existing tests pass. New tests cover poll-start, poll-stop, poll-skip, window-lost, match-lost, sync-change, and config clamping. |

---

## 4. Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| RISK-1 | FlaUI calls in poll are slow (>1s), causing poll drift | Medium | Low | Poll uses `Task.Delay` after each cycle (not fixed-rate), so drift is expected. Interval is a minimum gap, not a period. |
| RISK-2 | Status bar text format varies across PCS Pro versions | Medium | Low | `SyncStatus` is returned as raw string — no parsing. Consumer interprets. |
| RISK-3 | Poll detects false-positive window loss during PCS Pro modal dialog | Low | Medium | Modal dialogs do not hide the main window in the automation tree; they appear as child windows. |
| RISK-4 | Concurrent `Shutdown` trigger from poll + `StopAsync` call | Low | Medium | `Shutdown` from `NotRunning` is idempotent (Stateless permits self-transition or the poll loop exits before firing). Guard in poll: check state before firing. |

---

## 5. Out of Scope

- Web UI changes to display health alerts (future HLPS).
- Automatic recovery actions (e.g., auto-retry on match-lost).
- Stream-death detection via YouTube API (separate from PCS Pro health).
- Customisable alert thresholds (e.g., "alert only after N consecutive failures").
- Health check reporting/logging dashboard.

---

## 6. Traceability

| HLPS-010 Reference | Spec Section |
|---|---|
| §2.9 (GAP-011 Health-Check Poll) | §2.5–§2.7 (poll lifecycle and concurrency) |
| §2.9 Configuration | §2.8 (`HealthCheckIntervalSeconds`) |
| §2.9 Signals | §2.2 (`HealthCheckResult` fields) |
| §2.9 Concurrency contract | §2.7 (`_operationLock.Wait(0)`) |
| §2.9 Lifecycle | §2.5 (start at MatchLoaded, stop on leave) |
| §2.9 Tests | AC-15 |
| H-SC-8 | AC-5, AC-7, AC-8, AC-9, AC-10 |
