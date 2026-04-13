# SPEC-S-002: Process Lifecycle — Launch, Main Window Detection, Crash Watcher, and Stop

| Field | Value |
|---|---|
| **Document** | SPEC-S-002-Lifecycle.md |
| **Status** | DRAFT |
| **Version** | 0.2 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-006-FlaUI-Integration.md v0.4 §S-002 |
| **Step** | IS-006 S-002 |

---

## 1. Problem Statement

`PcsProAutomationService` currently exists as a twelve-member skeleton; all nine `Task`-returning methods throw `NotImplementedException`. This step delivers the process-lifecycle subset of `LaunchAndLoginAsync` — start cricket.exe, detect the main window, and start the crash watcher — and delivers `StopAsync` in full.

After this step, `LaunchAndLoginAsync` will have launched cricket.exe, detected the main window, fired `LoginDetected`, and returned with the service in `LoginScreen` state. It will **not** yet perform login automation (S-003). `StopAsync` will be fully implemented.

The `AutoLaunch=false` default config means `AutoLaunchService` does not call `LaunchAndLoginAsync` during S-002's delivery window, so no caller observes the intentionally-incomplete `LoginScreen` resting state in production. S-003 completes the state machine advancement to `MatchSelection`.

---

## 2. Scope

**In scope:**
- `PcsProAutomationService` acquires a `PcsProStateMachine` instance and wires `CurrentState`, `LastErrorReason`, `StateChanged`, and state transition helpers.
- `LaunchAndLoginAsync` process-launch phase: kill any pre-existing cricket.exe (best-effort), start a new process, poll for the main FlaUI application window within `LaunchingTimeoutSeconds` (40 s), fire `LoginDetected` on success.
- Crash watcher: background task started when the service reaches `LoginScreen`; transitions to `Error` state if cricket.exe exits unexpectedly.
- `StopAsync`: fully implemented from **all** states — graceful close → kill escalation, crash-watcher cancellation, state-appropriate trigger sequence.
- Inline Serilog logging at correct severity levels.
- `PcsRemote.Automation.Tests` tests covering crash-watcher detection and the absent-process `StopAsync` case using a test double for `Process`.

**Out of scope:**
- Login dialog automation (S-003).
- Any FlaUI element interaction beyond main window detection via `IProcessHandle.TryGetMainWindow()`.
- I-U-1 through I-U-6 (AutomationId strings) — none are required for this step.

---

## 3. Design Constraints

### 3.1 State Machine Ownership

`PcsProAutomationService` owns a `PcsProStateMachine` instance created in its constructor.
- `CurrentState` returns `_stateMachine.CurrentState`.
- `LastErrorReason` is a `string?` field maintained by the service, not the state machine.
- Transitions are fired via `_stateMachine.Fire(trigger)`. Firing an invalid trigger throws `InvalidOperationException`; this is the intended contract.

`PcsProAutomationService` registers a callback on `_stateMachine.OnTransitioned` that raises the `StateChanged` event — so all callers receive notifications on every state transition.

### 3.2 Mutual Exclusion

State-machine trigger-firing and `LastErrorReason` writes must be protected by a single `SemaphoreSlim(1, 1)` instance (`_operationLock`). This prevents concurrent state transitions from:
- Two parallel `LaunchAndLoginAsync` calls.
- A crash watcher firing simultaneously with `StopAsync`.

The `SemaphoreSlim.Wait(0)` non-blocking probe pattern (as used in `MockPcsProAutomationService`) is the correct approach for detecting a concurrent operation already in progress in `LaunchAndLoginAsync` — if the lock is held, the method throws `InvalidOperationException` immediately.

**Critical constraint:** The crash watcher must acquire `_operationLock` using `WaitAsync(ct)` where `ct` is the crash watcher's own `CancellationToken`. If the token is cancelled while the watcher is waiting for the lock, `OperationCanceledException` is thrown internally and the watcher exits without firing any trigger. This prevents the deadlock scenario where `StopAsync` holds the lock waiting to `await` the watcher while the watcher is blocked waiting for the lock.

The lock wraps only the `LastErrorReason` write and the `_stateMachine.Fire()` call — it is not held across blocking I/O such as process start, window polling, or graceful-close wait.

### 3.3 Crash Watcher Cancellation Order

`StopAsync` **must** cancel and await the crash watcher task *before* killing the process. Reversing this order creates a window where the crash watcher detects the intentional kill as unexpected exit and fires a spurious `Error` transition. This ordering is mandatory and must be verified in tests.

### 3.4 Crash Watcher Trigger Selection

There is no `ProcessExited` trigger in `PcsProTrigger`. When the crash watcher detects unexpected exit, it fires `PcsProTrigger.Timeout` — which is valid from all non-terminal states (`Launching`, `LoginScreen`, `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, `MatchLoaded`) and transitions each to `Error`. The `Timeout` trigger is the only appropriate choice because it is universally applicable.

The watcher must check `_stateMachine.CurrentState` inside the lock and not fire if the current state is `NotRunning` or `Error` (either the service was cleanly stopped, or the watcher raced with `StopAsync` and has already been superseded).

### 3.5 Already-Running Cricket.exe (I-U-7 Resolution)

Before firing `PcsProTrigger.Launch` (and therefore while still in `NotRunning` state), `LaunchAndLoginAsync` enumerates processes named `cricket` (case-insensitive, without extension) via `IProcessManager.GetByName`. Any found processes are killed. Kill is **best-effort**: if a `Win32Exception` or `InvalidOperationException` is thrown (e.g., access denied, process already exited), the exception is caught, logged at `Warning`, and the launch continues. After all kills are attempted, `LaunchAndLoginAsync` proceeds to fire `Launch` and start the new process.

### 3.6 Main Window Detection Strategy

The main window is detected via `IProcessHandle.TryGetMainWindow()`, polled in a loop until non-null or timeout. The real implementation delegates to FlaUI's `Application.Attach(process)` + `app.GetMainWindow(automation)`; tests supply a fake. On every poll iteration the loop also checks `IProcessHandle.HasExited`: if the process has already exited before a window is detected, `LaunchAndLoginAsync` immediately sets `LastErrorReason` and fires `Timeout` without waiting for the full `LaunchingTimeoutSeconds`. This prevents a 40-second hang when cricket.exe dies in the first second.

No specific AutomationId or window title is required at this step. I-U-1 (exact login dialog AutomationId) is deferred to S-003.

### 3.7 `StopAsync` State Transitions

`StopAsync` must work from **any** state without throwing. The following trigger sequences are required:

| Current state | Trigger sequence | Final state | LastErrorReason |
|---|---|---|---|
| `NotRunning` | (no-op, return immediately) | `NotRunning` | unchanged |
| `Launching`, `LoginScreen`, `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady` | `Timeout` → `Error`, then `Retry` → `NotRunning` | `NotRunning` | `null` (cleared after `Retry`) |
| `MatchLoaded` | `Stop` → `NotRunning` | `NotRunning` | `null` (cleared) |
| `Error` | `Retry` → `NotRunning` | `NotRunning` | `null` (cleared) |

For all non-`NotRunning` paths: cancel and await the crash watcher first (§3.3), then kill the process (skipping `CloseMainWindow` if `HasExited`), then fire the trigger sequence while holding `_operationLock`.

**Special case — crash watcher already fired:** If `StopAsync` is called when the service is in `Error` state (the crash watcher detected an unexpected exit before `StopAsync` ran), the process is already dead. `StopAsync` kills any lingering process handle (no-op if `HasExited`), fires `Retry` → `NotRunning`, clears `LastErrorReason`, and returns normally without throwing.

> **Note on Mock divergence:** `MockPcsProAutomationService.StopAsync` transitions directly to `NotRunning` from any state without state-machine validation. The real service uses the trigger sequences above. This is an intentional implementation-level difference with no observable effect on callers using `IPcsProAutomationService`. The Mock will be brought into alignment in a later step.

### 3.8 Process and Window Abstraction

To enable deterministic unit testing without OS processes or a real FlaUI runtime, the following `internal` interfaces must be defined in `PcsRemote.Automation` (accessible to `PcsRemote.Automation.Tests` via `InternalsVisibleTo`):

```csharp
internal interface IProcessManager
{
    IReadOnlyList<IProcessHandle> GetByName(string executableName);
    IProcessHandle Start(ProcessStartInfo startInfo);
}

internal interface IProcessHandle : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    Task WaitForExitAsync(CancellationToken ct);
    bool CloseMainWindow();
    void Kill();
    /// <summary>
    /// Returns non-null when the process main window is visible and ready.
    /// Real implementation: FlaUI Application.Attach(process).GetMainWindow(automation).
    /// Test fake: controlled by test via flag or TaskCompletionSource.
    /// Returns null while window not yet visible.
    /// </summary>
    object? TryGetMainWindow();
}
```

`PcsProAutomationService` receives `IProcessManager` via constructor injection (registered in DI by `AutomationServiceCollectionExtensions`). `IProcessHandle` is obtained from `IProcessManager.Start()` and from `IProcessManager.GetByName()`. The crash watcher uses `IProcessHandle.WaitForExitAsync(ct)` to await process exit.

### 3.9 Graceful-Close Timeout Constant

The wait time between `CloseMainWindow()` and escalation to `Kill()` in `StopAsync` is defined as a named constant `GracefulCloseTimeoutSeconds = 5` in `PcsProStateMachine`. This is consistent with the existing timeout constant pattern and prevents magic numbers in `PcsProAutomationService`.

`StopAsync` must kill the process regardless of the `CancellationToken` state — process termination is a cleanup operation that must complete. The `ct` parameter may be used to abort the graceful-close wait (escalating immediately to `Kill`), but must not prevent the kill itself.

### 3.10 Time Abstraction for Testable Timeouts

The 40-second window-detection polling loop uses `TimeProvider` (built into .NET 8) to measure elapsed time. `PcsProAutomationService` receives a `TimeProvider` via constructor injection (defaulting to `TimeProvider.System` in DI registration). Tests supply `Microsoft.Extensions.Time.Testing.FakeTimeProvider` to advance time deterministically. This is consistent with the existing timer-abstraction pattern used in `PcsRemote.Web`.

---

## 4. Acceptance Criteria

### AC-1 — State Machine Wiring (Unit-verifiable)
`PcsProAutomationService.CurrentState` returns the value from its internal `PcsProStateMachine`. Verified by creating an instance via DI and asserting `CurrentState == PcsProState.NotRunning` on construction.

### AC-2 — StateChanged Event (Unit-verifiable)
`StateChanged` is raised with the correct new state on every state machine transition. Verified by subscribing to the event and asserting it fires with `PcsProState.Launching` when `LaunchAndLoginAsync` begins.

### AC-3 — Kill Pre-Existing Process (Unit-verifiable)
If `IProcessManager.GetByName("cricket")` returns a non-empty list, each handle is killed **before** `PcsProTrigger.Launch` is fired. If a kill throws (access denied), the exception is caught, logged at `Warning`, and the launch continues. Verified via fake `IProcessManager` injected into the service under test.

### AC-4 — Process Started from Config (Unit-verifiable)
`IProcessManager.Start()` is called with `ProcessStartInfo.FileName` set to `PcsProOptions.ExecutablePath` and `ProcessStartInfo.WorkingDirectory` set to `PcsProOptions.WorkingDirectory`. Verified via fake `IProcessManager`.

### AC-5 — Launch Trigger Fired Before Process Start (Unit-verifiable)
`LaunchAndLoginAsync` fires `PcsProTrigger.Launch` (transitioning `NotRunning` → `Launching`) **before** calling `IProcessManager.Start()`. `StateChanged` is raised with `PcsProState.Launching`. Verified by recording call order in the fake.

### AC-6 — Window Detection Timeout (Unit-verifiable)
If `IProcessHandle.TryGetMainWindow()` returns null on every poll and `IProcessHandle.HasExited` remains false, `LaunchAndLoginAsync` fires `PcsProTrigger.Timeout` (→ `Error`) after `PcsProStateMachine.LaunchingTimeoutSeconds` have elapsed. `LastErrorReason` is set to a non-null, non-empty string. Verified using `FakeTimeProvider` to advance time without real delay.

### AC-6b — Window Detection: Early Process Exit (Unit-verifiable)
If `IProcessHandle.HasExited` becomes true before the main window is detected, `LaunchAndLoginAsync` fires `PcsProTrigger.Timeout` (→ `Error`) immediately without waiting for the full 40-second timeout. `LastErrorReason` is set. Verified with `FakeTimeProvider` held at zero and the fake handle reporting `HasExited = true`.

### AC-6c — Process Start Failure (Unit-verifiable)
If `IProcessManager.Start()` throws (e.g., `Win32Exception` for file not found), `LaunchAndLoginAsync` catches the exception, sets `LastErrorReason` to a descriptive message, fires `PcsProTrigger.Timeout` (→ `Error`), and does not propagate the exception. Verified via fake `IProcessManager` configured to throw.

### AC-7 — Window Detected Log (Code-review-verifiable)
When `IProcessHandle.TryGetMainWindow()` returns non-null, the event is logged at `Debug` severity using a structured template. The log message includes `{ProcessId}` as a named property.

### AC-8 — Crash Watcher Active Behavior (Unit-verifiable)
After `LaunchAndLoginAsync` successfully detects the main window and returns (service in `LoginScreen`), if the fake process is subsequently signalled to exit (via `WaitForExitAsync` completing), `StateChanged` fires with `PcsProState.Error` and `LastErrorReason` equals `"PCS Pro exited unexpectedly"`. Verified using `TaskCompletionSource` in the fake handle's `WaitForExitAsync`.

### AC-9 — Crash Watcher Unexpected Exit Details (Unit-verifiable)
When the crash watcher detects unexpected exit from any active state (`Launching`, `LoginScreen`, `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, `MatchLoaded`): `LastErrorReason` is set to `"PCS Pro exited unexpectedly"`, `PcsProTrigger.Timeout` is fired (→ `Error`), and `StateChanged` fires with `PcsProState.Error`. Verified for at least `LoginScreen` state.

### AC-10 — Crash Watcher No-Op When Not Running (Unit-verifiable)
If the fake process exit is signalled while the service is in `NotRunning` or `Error` state, no state transition occurs and no exception is thrown. Verified by asserting `StateChanged` was not raised.

### AC-11 — Crash Watcher Cancellation Order (Unit-verifiable)
After `StopAsync` completes, `StateChanged` was raised exactly with `PcsProState.NotRunning` as the final state and no spurious `PcsProState.Error` transition occurred. This verifies the correct cancellation-before-kill ordering by its observable outcome. Verified using fake handles with a `TaskCompletionSource` that records whether `WaitForExitAsync` was cancelled before `Kill()` was called.

### AC-12 — StopAsync Graceful Close (Code-review-verifiable)
`StopAsync` calls `IProcessHandle.CloseMainWindow()` and waits up to `PcsProStateMachine.GracefulCloseTimeoutSeconds` (= 5) for the process to exit before calling `IProcessHandle.Kill()`. The constant `GracefulCloseTimeoutSeconds = 5` exists in `PcsProStateMachine`.

### AC-13 — StopAsync Absent Process, Crash Watcher Not Yet Fired (Unit-verifiable)
If `IProcessHandle.HasExited` is true when `StopAsync` is called while in `MatchLoaded` state (crash watcher was cancelled before it could fire), `StopAsync` skips `CloseMainWindow`, cancels the crash watcher, fires `Stop` → `NotRunning`, clears `LastErrorReason`, and returns without throwing. Verified using fake handle pre-set to `HasExited = true`.

### AC-13b — StopAsync When Crash Watcher Already Fired (Unit-verifiable)
If the crash watcher has already transitioned the service to `Error` state before `StopAsync` is called, `StopAsync` kills the process (no-op if `HasExited`), fires `Retry` → `NotRunning`, clears `LastErrorReason`, and returns without throwing. Verified by pre-setting service state to `Error` via fake process exit, then calling `StopAsync`.

### AC-14 — StopAsync from MatchLoaded (Unit-verifiable)
`StopAsync` fires `PcsProTrigger.Stop` transitioning from `MatchLoaded` to `NotRunning`. `LastErrorReason` is cleared to `null`. `StateChanged` is raised with `PcsProState.NotRunning`. Verified in a unit test.

### AC-15 — StopAsync NotRunning Guard (Unit-verifiable)
Calling `StopAsync` when the current state is `NotRunning` returns immediately without firing any trigger and without throwing. Verified in a unit test.

### AC-15b — StopAsync from Active Non-MatchLoaded States (Unit-verifiable)
Calling `StopAsync` from `Launching` or `LoginScreen` state kills the process, fires `Timeout` → `Error` then `Retry` → `NotRunning`, clears `LastErrorReason`, and returns without throwing. `StateChanged` fires with `Error` and then `NotRunning` in that order. Verified in a unit test for at least `LoginScreen` state (the reachable state after S-002).

### AC-16 — Error Logging (Code-review-verifiable)
Launch timeout, process start failure, and crash watcher unexpected exit are all logged at `Error` severity. State transitions are logged at `Information`. Window polling iterations (including `TryGetMainWindow` return values) are logged at `Debug`. All log calls use structured Serilog templates with no string interpolation. Minimum required named properties: `{ExecutablePath}` on launch start, `{ProcessId}` on window detection, `{State}` on all state transitions, `{Reason}` on timeout and crash watcher fire.

### AC-17 — Architecture Constraint (Build-verifiable)
`PcsRemote.Automation` has no compile-time or project reference dependency on `PcsRemote.Web`, `PcsRemote.Automation.Mock`, or `PcsRemote.TrayHost`. `dotnet build` produces zero errors and zero warnings.

### AC-18 — Existing Tests Unbroken (Build-verifiable)
All 228 Web tests, 64 Mock tests, 37 Core tests, 15 TrayHost tests (5 skipped), and 12 E2E tests continue to pass. `dotnet test` exits with code 0.

### AC-19 — LoginDetected Fired on Window Detection (Unit-verifiable)
After `IProcessHandle.TryGetMainWindow()` returns non-null, `LaunchAndLoginAsync` fires `PcsProTrigger.LoginDetected` transitioning from `Launching` to `LoginScreen`. `StateChanged` fires with `PcsProState.LoginScreen`. The crash watcher is started. `LaunchAndLoginAsync` returns to the caller with `CurrentState == PcsProState.LoginScreen`. Verified in a unit test.

---

## 5. Known Unknowns Addressed

| ID | Description | Resolution |
|---|---|---|
| I-U-7 | Already-running cricket.exe policy | Kill and relaunch — implemented in AC-3 |

No other blocking unknowns for this step. I-U-1 (main window AutomationId) is deferred to S-003; `IProcessHandle.TryGetMainWindow()` polling without a specific identifier is sufficient here.

**Note:** `PcsProStateMachine` requires two new public constants: `GracefulCloseTimeoutSeconds = 5` (§3.9, AC-12). This is a minor additive change to `PcsRemote.Core` with no breaking impact. No other Core changes are required.

---

## 6. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | Claude Opus 4.6, GPT-5.4, Claude Sonnet 4.6 | NEEDS REVIEW — 3 unanimous blockers (happy-path end-state, StopAsync non-MatchLoaded states, crash-watcher deadlock), plus additional blockers from individual reviewers |
| R1 fixes | 2026-04-12 | Orchestrator | All 15 accepted findings applied; v0.2 produced |
