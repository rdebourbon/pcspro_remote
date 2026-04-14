# SPEC-S-007: RetryAsync, Serilog Audit, and Integration Smoke Test

| Field | Value |
|---|---|
| **Document** | SPEC-S-007-RetryAsyncAndSerilogAudit.md |
| **IS Step** | IS-006 S-007 |
| **Branch** | `feature/IS-006-S-007-retry-and-serilog-audit` |
| **Status** | APPROVED |
| **Date** | 2026-04-12 |
| **Dependencies** | S-006 merged to master |

---

## 1. Overview

S-007 closes the last `NotImplementedException` in `PcsProAutomationService` by implementing `RetryAsync`. After this step, all nine interface methods declared in `IPcsProAutomationService` are fully implemented. A Serilog log-level audit is performed as a structured code-review pass, confirming completeness and filling any gaps. An integration smoke test (manual, garage PC only) verifies end-to-end composition.

**Addresses:** I-SC-7, I-SC-8, I-SC-15.

---

## 2. Scope

### Deliverables

| # | Deliverable | File(s) |
|---|---|---|
| D-1 | `RetryAsync` implementation | `PcsRemote.Automation/PcsProAutomationService.cs` |
| D-2 | `FakeProcessManager` multi-start support | `tests/PcsRemote.Automation.Tests/FakeProcessManager.cs` |
| D-3 | `CreateServiceAtErrorAsync` test helper | `tests/PcsRemote.Automation.Tests/PcsProAutomationServiceTests.cs` |
| D-4 | RetryAsync unit tests (AC-1 – AC-8) | `tests/PcsRemote.Automation.Tests/PcsProAutomationServiceTests.cs` |
| D-5 | Serilog audit (code review pass — no new test file needed unless gaps found) | `PcsRemote.Automation/PcsProAutomationService.cs` |
| D-6 | `NotImplementedException` no longer thrown anywhere in production code | All `PcsRemote.*` projects except `FlaUi*` placeholder stubs |

### Out of Scope

- FlaUI-backed implementations of `FlaUiScoreboardAutomation` and `FlaUiChangeMatchAutomation` (those are I-U-5 / I-U-6, resolved on garage PC)
- Playwright E2E — no automated E2E step in IS-006 (all integration verification is manual on garage PC)
- Any change to `MockPcsProAutomationService` or Web UI

---

## 3. RetryAsync — Behavioural Contract

### 3.1 Pre-condition

`RetryAsync` requires the service to be in `PcsProState.Error`. If the current state is anything other than `Error`, the method throws `InvalidOperationException` with message:

```
RetryAsync called from {CurrentState} — only valid from Error state.
```

This check happens before any side effects.

### 3.2 Implementation Order

The following steps are executed in strict sequence:

1. **Log entry** — `LogInformation("RetryAsync called; current state {State}", CurrentState)`
2. **State guard** — throw `InvalidOperationException` if `CurrentState != PcsProState.Error` (see 3.1)
3. **Fire `Retry` trigger under lock** — acquire `_operationLock`, fire `PcsProTrigger.Retry` (transitions `Error → NotRunning`), clear `_lastErrorReason = null`, release lock
4. **Flush state events** — call `FlushStateChangedEvents()` so subscribers observe `NotRunning` before the process is killed
5. **Cancel and await crash watcher** — same pattern as `StopAsync` lines 159–167:
   - `_crashWatcherCts.Cancel()` → crash watcher's pending `WaitForExitAsync(crashCt)` throws `OperationCanceledException` and returns cleanly
   - `await _crashWatcherTask` (no-op if null)
   - `_crashWatcherCts.Dispose()` → set to null
   - `_crashWatcherTask = null`
6. **Kill lingering process** — graceful-close-then-force-kill using a **standalone timeout CTS only** (not linked with `ct` — see §3.3): `CloseMainWindow()`, then `WaitForExitAsync` with a `CancellationTokenSource(GracefulCloseTimeoutSeconds)`, then `Kill()` if still alive. Same logic as `StopAsync` lines 170–196 but without linking `ct` into the graceful close.
7. **Dispose process** — `_process?.Dispose(); _process = null;`
7.5. **Guard cancellation** — `ct.ThrowIfCancellationRequested()` — ensures a pre-cancelled token throws `OperationCanceledException` from `RetryAsync` with state = `NotRunning`, before any new process is started. Without this guard, cancellation is only observed inside `LaunchAndLoginAsync`, which fires a state transition to `Launching` and ultimately to `Error` before propagating the exception — leaving the service in the wrong state for a cancelled caller.
8. **Relaunch** — `await LaunchAndLoginAsync(ct)` (starts fresh process, new crash watcher, login, MatchSelection)
9. **Log completion** — `LogInformation("RetryAsync complete; current state {State}", CurrentState)`

### 3.3 Cancellation

The `CancellationToken ct` is explicitly checked at step 7.5 (`ct.ThrowIfCancellationRequested()`) and then passed to `LaunchAndLoginAsync(ct)`. Steps 1–7 do **not** observe `ct`:
- The lock acquisition (step 3) uses `CancellationToken.None` — teardown must complete atomically
- The graceful close (step 6) uses a **standalone** `CancellationTokenSource(GracefulCloseTimeoutSeconds)` — caller cancellation does not shorten the 5-second graceful close window (consistent with `StopAsync`'s intent; the maximum wait is bounded and acceptable)

Step 7.5 is the earliest safe point to honour cancellation: teardown is complete, no new state has been created. When step 7.5 throws, `CurrentState == NotRunning` and `_process == null` — a clean, consistent state for the caller to observe.

### 3.4 Why Fire `Retry` First (Before Cancelling the Crash Watcher)

Firing `Retry` (Error → NotRunning) before cancelling the crash watcher is intentional:

- Once in `NotRunning`, `WatchForCrashAsync` has a hard guard at line 464: `if (state is PcsProState.NotRunning or PcsProState.Error) return;`
- Even if the crash watcher task happens to race (e.g., the process exits between step 3 and step 5), it will observe `NotRunning` and return without firing an error trigger
- The `crashCt` cancellation in step 5 then cleans up the waiting `WaitForExitAsync` call, ensuring the task completes before the process is killed

### 3.5 Logging Requirements

| Level | Message |
|---|---|
| `Information` | `"RetryAsync called; current state {State}"` |
| `Information` | `"RetryAsync complete; current state {State}"` |
| `Debug` | `"RetryAsync cancelling crash watcher"` |
| `Warning` | `"Force-killed cricket.exe {ProcessId}"` (only if force-kill is used — reuses existing constant in StopAsync) |

No string interpolation. All log calls use structured templates.

---

## 4. FakeProcessManager — Multi-Start Support (D-2)

`RetryAsync` calls `LaunchAndLoginAsync` which calls `_processManager.Start(...)`. In the happy-path test, `Start` is called twice: once during the initial `CreateServiceAtMatchSelectionAsync` setup and once during `RetryAsync`. The current `FakeProcessManager.Start()` returns the single `StartedHandle`, which will not be null on the second call — but there is no way to configure a different handle for the second start, and `StartCallCount` is not tracked.

**Required changes to `FakeProcessManager`:**

```csharp
/// <summary>
/// Number of times <see cref="Start"/> has been called.
/// </summary>
public int StartCallCount { get; private set; }

/// <summary>
/// Queue of handles returned by sequential <see cref="Start"/> calls. When non-empty,
/// handles are dequeued in order. Falls back to <see cref="StartedHandle"/> when the
/// queue is empty. Supports multi-start scenarios such as <see cref="RetryAsync"/>.
/// </summary>
public Queue<FakeProcessHandle> StartedHandleQueue { get; } = new();
```

The `Start()` implementation becomes:

```csharp
public IProcessHandle Start(ProcessStartInfo startInfo)
{
    CapturedStartInfo = startInfo;
    StartCallCount++;
    if (ShouldThrowOnStart)
        throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");
    if (StartedHandleQueue.Count > 0)
        return StartedHandleQueue.Dequeue();
    return StartedHandle
        ?? throw new InvalidOperationException("FakeProcessManager.StartedHandle was not configured.");
}
```

No existing tests are affected: they all set `StartedHandle` and call `Start` exactly once.

---

## 5. `CreateServiceAtErrorAsync` Test Helper (D-3)

Add this helper alongside the existing `CreateServiceAtMatchLoadedAsync`:

```csharp
/// <summary>
/// Creates a service at <see cref="PcsProState.Error"/> by driving the state machine
/// through MatchSelection → Error via reflection. The crash watcher is still running
/// (blocked on <see cref="FakeProcessHandle.WaitForExitAsync"/>).
/// Used for S-007 RetryAsync tests.
/// </summary>
private static async Task<(PcsProAutomationService Service, FakeProcessHandle Handle, FakeProcessManager ProcessManager, FakeTimeProvider TimeProvider)>
    CreateServiceAtErrorAsync(
        FakeLoginAutomation? loginAutomation = null,
        IMatchSelectionAutomation? matchSelectionAutomation = null,
        ITeamNamesAutomation? teamNamesAutomation = null,
        IScoreboardAutomation? scoreboardAutomation = null,
        IChangeMatchAutomation? changeMatchAutomation = null)
{
    var handle = new FakeProcessHandle { MainWindowVisible = true };
    var pm = new FakeProcessManager { StartedHandle = handle }; // uses StartedHandle fallback for initial launch
    var tp = new FakeTimeProvider();
    var svc = CreateService(
        pm, tp,
        loginAutomation ?? new FakeLoginAutomation(),
        matchSelectionAutomation ?? new FakeMatchSelectionAutomation(),
        teamNamesAutomation ?? new FakeTeamNamesAutomation(),
        scoreboardAutomation ?? new FakeScoreboardAutomation(),
        changeMatchAutomation ?? new FakeChangeMatchAutomation());
    await svc.LaunchAndLoginAsync();

    // Drive state machine directly: MatchSelection → Error (Timeout trigger).
    var machine = (PcsProStateMachine)typeof(PcsProAutomationService)
        .GetField("_stateMachine", BindingFlags.NonPublic | BindingFlags.Instance)!
        .GetValue(svc)!;
    machine.Fire(PcsProTrigger.Timeout);

    // Set _lastErrorReason via reflection so AC-11 (clearing verification) is meaningful.
    typeof(PcsProAutomationService)
        .GetField("_lastErrorReason", BindingFlags.NonPublic | BindingFlags.Instance)!
        .SetValue(svc, "Simulated crash for test");

    return (svc, handle, pm, tp);
}
```

> **Note:** `PcsProTrigger.Timeout` fires the transition `MatchSelection → Error`. The crash watcher is still running at this point — it is blocked on `FakeProcessHandle.WaitForExitAsync` because `handle.HasExited` is false. This is the realistic test scenario for `RetryAsync`.

---

## 6. Acceptance Criteria

### 6.1 RetryAsync — Functional

| ID | Description | Test Method Name |
|---|---|---|
| AC-1 | Happy path: Error state → RetryAsync completes → service ends in MatchSelection; `LastErrorReason` is null after retry | `RetryAsync_FromErrorState_RelaunchesAndReachesMatchSelection` |
| AC-2 | Process manager Start called exactly twice (original launch + retry launch) | (asserted in AC-1 test via `pm.StartCallCount == 2`) |
| AC-3 | Wrong state NotRunning → throws InvalidOperationException | `RetryAsync_FromNotRunning_ThrowsInvalidOperationException` |
| AC-4 | Wrong state MatchSelection → throws InvalidOperationException | `RetryAsync_FromMatchSelection_ThrowsInvalidOperationException` |
| AC-5 | Wrong state MatchLoaded → throws InvalidOperationException | `RetryAsync_FromMatchLoaded_ThrowsInvalidOperationException` |
| AC-6 | Crash watcher CTS is NOT null after RetryAsync completes (replaced by new non-null CTS from LaunchAndLoginAsync — different instance from the original) — verifies via reflection that teardown + fresh watcher occurred | `RetryAsync_CrashWatcherIsReplacedAfterRetry` |
| AC-7 | CancellationToken already cancelled → OperationCanceledException thrown; CurrentState is NotRunning (step 7.5 throws before any new process is started) | `RetryAsync_CancelledToken_ThrowsOperationCanceledException` |
| AC-8 | LaunchAndLoginAsync failure (second process handle has no visible window → launch-phase timeout) → service ends in Error state; no exception propagates | `RetryAsync_LaunchFailure_ServiceEndsInErrorState` |

### 6.2 Serilog Audit — Code Review

| ID | Description | Verification |
|---|---|---|
| AC-9 | Every log call in `PcsProAutomationService.cs` uses a structured template (no `$"..."` string interpolation) | Source scan: `Select-String -Pattern '_logger\.Log(Debug\|Information\|Warning\|Error)\(\$"' PcsProAutomationService.cs` must return zero matches |
| AC-10 | RetryAsync entry and completion are logged at `Information` level | Code review |
| AC-11 | Crash watcher cancellation during RetryAsync is logged at `Debug` level | Code review |

### 6.3 Structural

| ID | Description |
|---|---|
| AC-12 | `NotImplementedException` no longer thrown anywhere in `PcsRemote.Automation/PcsProAutomationService.cs` production methods (NIE stubs in `FlaUi*` concrete classes remain acceptable) |
| AC-13 | Build: 0 warnings, 0 errors |
| AC-14 | All 419+ existing tests continue to pass; 7 new RetryAsync test methods added (AC-2 is asserted within AC-1's test method, not a separate method) |

---

## 7. Test Case Specifications

### AC-1 / AC-2: Happy Path
```
Given: service in Error state (via CreateServiceAtErrorAsync)
       _lastErrorReason set to "Simulated crash for test" (by CreateServiceAtErrorAsync)
       pm.StartedHandleQueue contains a second FakeProcessHandle with MainWindowVisible=true
When:  RetryAsync() is called with default CancellationToken
Then:  - CurrentState == MatchSelection
       - pm.StartCallCount == 2
       - svc.LastErrorReason == null (cleared in step 3)
       - No exception thrown
```

### AC-6: Crash Watcher Replacement
```
Given: service in Error state (crash watcher still running, blocked on WaitForExitAsync)
       pm.StartedHandleQueue contains a second FakeProcessHandle with MainWindowVisible=true
When:  RetryAsync() completes
Then:  - _crashWatcherCts field (via reflection) is NOT null (new CTS installed by LaunchAndLoginAsync)
       - The CTS instance is different from the one captured before RetryAsync was called
         (confirms old CTS was disposed and a fresh watcher was installed)
```

### AC-7: Cancelled Token
```
Given: service in Error state
       pm.StartedHandleQueue is empty (safety backstop: if step 7.5 were accidentally omitted,
         Start() would be called and reuse the original handle — providing a clear failure signal;
         the primary mechanism is step 7.5, not the empty queue)
When:  RetryAsync(new CancellationToken(canceled: true)) is called
Then:  - OperationCanceledException thrown
       - CurrentState == NotRunning (step 7.5 throws after teardown, before relaunch)
       - pm.StartCallCount == 1 (original launch only; no second Start call)
```

### AC-8: Launch Failure (Launch-Phase Timeout)
```
Given: service in Error state (via CreateServiceAtErrorAsync returning tp)
       second FakeProcessHandle with MainWindowVisible=false added to pm.StartedHandleQueue
When:  var retryTask = svc.RetryAsync() (not awaited yet)
       await Task.Delay(100)            // let teardown complete + polling loop enter
       tp.Advance(TimeSpan.FromSeconds(PcsProStateMachine.LaunchingTimeoutSeconds + 1))
       await retryTask.WaitAsync(TimeSpan.FromSeconds(5))
Then:  - CurrentState == Error (PollForMainWindowAsync times out, fires Timeout trigger)
       - No exception propagates out of RetryAsync
       - pm.StartCallCount == 2 (retry did start a second process)
```

> **FakeTimeProvider mechanism for AC-8**: The same pattern as `LaunchAndLoginAsync_WhenTimeoutExceeded_TransitionsToError` (line ~244 of test file): do not `await` the task immediately; let the async state machine enter the polling loop via a short `Task.Delay`; then `Advance(...)` to simulate elapsed time; then `WaitAsync(5s)` on the task. `AutoAdvanceAmount` is NOT used — `Advance(...)` is the correct API for this test infrastructure.

---

## 8. Serilog Audit Scope

The audit is a code-review pass, not a new test class. The following gap check must be performed against `PcsProAutomationService.cs`:

| Check | Expected | Corrective action if gap found |
|---|---|---|
| All `_logger.Log*` calls use named-property structured templates | Zero occurrences of string interpolation (`$"..."`) inside any log call | Replace with structured template + argument |
| All lifecycle entry/completion milestones (RetryAsync, StopAsync, LaunchAndLoginAsync) log at `Information` | Added per-step in S-002–S-007 | Add missing `LogInformation` at the milestone site |
| Crash watcher teardown steps log at `Debug` | Crash watcher cancel in RetryAsync and StopAsync | Add missing `LogDebug` if absent |
| RetryAsync-specific log statements added in D-1 | See §3.5 | (Part of implementation) |

> **Note on transition logging**: State transitions are already logged centrally via `OnTransitioned` in the state machine setup (established in S-002). The audit does **not** add per-call-site transition logs — those would be duplicates. The audit confirms only lifecycle milestones and element-lookup logs are present.

> **Note**: This audit does not require a new test file. The source scan in AC-9 confirms no string interpolation. The remaining checks are code review sign-off items.

---

## 9. Integration Smoke Test (Manual — Garage PC Only)

This step is **not automated**. It is documented here as the verification intent for I-SC-15.

| Step | Expected outcome |
|---|---|
| Start service (mock off) | cricket.exe launches; web UI shows MatchSelection |
| Select match, load it | MatchLoaded; team names visible |
| Trigger Error via Task Manager (kill cricket.exe) | Web UI shows Error banner within ~2s |
| Click Retry in web UI | cricket.exe relaunches; MatchSelection restored |
| Inspect Serilog log file | `RetryAsync called` and `RetryAsync complete` entries visible at Information level |

---

## 10. Files to Change

| File | Change |
|---|---|
| `src/PcsRemote.Automation/PcsProAutomationService.cs` | Implement `RetryAsync`; add Debug log in crash-watcher cancel block; fill any Serilog gaps from audit |
| `tests/PcsRemote.Automation.Tests/FakeProcessManager.cs` | Add `StartCallCount`, `StartedHandleQueue`, update `Start()` |
| `tests/PcsRemote.Automation.Tests/PcsProAutomationServiceTests.cs` | Add `CreateServiceAtErrorAsync` helper; add 7 new RetryAsync test methods |

No other files require modification.

---

## 11. Review Sign-off

| Reviewer | Model | Verdict | Blocking | Non-blocking |
|---|---|---|---|---|
| R1 | Claude Opus 4.6 | NEEDS REVISION | 5 | 4 |
| R1 | GPT-5.4 | NEEDS REVISION | 3 | 4 |
| R1 fixes | Orchestrator | All 14 findings triaged and applied | 0 | 0 |
| R2 | Pending | — | — | — |

### R1 Triage Summary

| Finding | Decision |
|---|---|
| AC-7 expected state wrong (Opus CRITICAL) | Accepted — fixed by adding step 7.5 `ct.ThrowIfCancellationRequested()` |
| AC-7 test setup incomplete — no second handle (Opus CRITICAL) | Accepted — step 7.5 means Start is never called; Given updated |
| Re-entrancy race from early FlushStateChangedEvents (GPT HIGH) | Accepted Risk — identical to StopAsync pattern, established since S-003 |
| AC-6 table says "null" contradicting §7 "NOT null" (GPT HIGH) | Accepted — AC-6 table fixed to "NOT null" |
| §3.3 says no ct but step 6 linked ct (Opus HIGH / GPT MEDIUM) | Accepted — clarified: step 6 uses standalone timeout CTS; ct explicitly not linked |
| AC-9 grep regex wrong (ILogger vs Log static) (Opus HIGH) | Accepted — regex fixed to `_logger\.Log(Debug\|Information\|...)` |
| AC-14 count wrong: 7 methods not 8 (Opus HIGH) | Accepted — count corrected to 7 |
| AC-8 wrong failure mode label (GPT MEDIUM) | Accepted — label corrected to "launch-phase timeout" |
| Smoke test "no interpolation in log file" unverifiable (GPT MEDIUM) | Accepted — removed from §9 |
| CreateServiceAtErrorAsync missing FakeTimeProvider (Opus MEDIUM) | Accepted — added to return tuple |
| _lastErrorReason not verified with non-null value (Opus MEDIUM) | Accepted — helper sets value; AC-1 asserts null after |
| AC-8 FakeTimeProvider mechanism unspecified (Opus MEDIUM) | Accepted — Task.Delay + Advance pattern documented in §7 |
| Audit would duplicate central transition logging (GPT LOW) | Accepted — §8 clarified: audit covers milestones, not transitions |
| Typo "Relaundes" in AC-1 test name (Opus LOW) | Accepted — corrected to "Relaunches" |

### R2 Triage Summary

| Reviewer | Verdict | Blocking | Non-blocking |
|---|---|---|---|
| Claude Opus 4.6 | **APPROVED** | 0 | 0 |
| Claude Sonnet 4.6 | NEEDS REVISION | 1 | 2 |

| Finding | Decision |
|---|---|
| §8/§9 absent from review submission (Sonnet HIGH) | Rejected — prompt-truncation artifact; full spec confirmed on disk at lines 277–303 |
| AC-7 Given causally misleading — empty queue looks like trigger not backstop (Sonnet MEDIUM) | Accepted — Given reworded to clarify empty queue is safety backstop, not primary mechanism |
| §5 silent on StartedHandle vs StartedHandleQueue for initial launch (Sonnet LOW) | Accepted — inline comment added: "uses StartedHandle fallback for initial launch" |

**Composite R2 verdict: APPROVED** — Opus approved unconditionally; Sonnet's only blocking finding rejected as a submission artifact; two valid MEDIUM/LOW wording fixes applied.
