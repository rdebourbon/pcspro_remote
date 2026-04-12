# SPEC-S-002 — Scoreboard Polling Hosted Service

| Field | Value |
|---|---|
| **Step ID** | S-002 |
| **IS Reference** | IS-004-Scoreboard.md §S-002 |
| **HLPS Reference** | HLPS-004-Scoreboard.md |
| **Status** | APPROVED |
| **Version** | v0.2 |

---

## §1 Governing Acceptance Criteria

This spec satisfies the following IS-004 HLPS-004 success criteria:

| SC | Requirement |
|---|---|
| S-SC-1 | Scoreboard image appears in all connected browsers within 3s of the automation service capturing it |
| S-SC-3 | Polling is tied to the `MatchLoaded` state — starts only when the state machine enters that state and stops when it leaves |
| S-SC-9 | The polling interval is configurable via `Scoreboard:CaptureIntervalSeconds` |

---

## §2 What This Step Delivers

A new background hosted service — `ScoreboardPollingService` — is added to `PcsRemote.Web`. It owns the `PeriodicTimer` that drives periodic capture, and its lifecycle is entirely controlled by the `IPcsProAutomationService.StateChanged` event and the ASP.NET Core host lifecycle.

The service does **not** own the capture logic; it delegates all capture to `IScoreboardService.CaptureAndBroadcastAsync`. It is registered in `Program.cs` via `AddHostedService`.

---

## §3 Behavioural Contract

### 3.1 Startup (cold-start)

On `StartAsync`, the service must:

1. Subscribe to `IPcsProAutomationService.StateChanged` **before** reading `CurrentState`.
2. Read `CurrentState` immediately after subscribing.
3. If `CurrentState` is already `MatchLoaded`, start the polling loop immediately without waiting for a state-change event.

This subscribe-before-read ordering closes the race window where state transitions to `MatchLoaded` between the read and the subscribe. The `StateChanged` handler is registered exactly once for the full service lifetime; it is not re-subscribed or unsubscribed between polling cycles.

### 3.2 Polling loop start

When the polling loop starts (either from cold-start or from a `StateChanged` event targeting `MatchLoaded`):

- A new loop-scoped `CancellationTokenSource` is created.
- A new `PeriodicTimer` is created using the configured interval.
- On each tick, the loop calls `IScoreboardService.CaptureAndBroadcastAsync`, passing the loop-scoped `CancellationToken`. This ensures in-flight captures are responsive to loop cancellation and host shutdown.
- The start is **idempotent**: if a polling loop is already active, a second start attempt is a no-op. The idempotency guard must be implemented with an atomic compare-and-swap (e.g., `Interlocked.CompareExchange` on an integer flag) or an equivalent thread-safe mechanism. A non-atomic read-then-write is insufficient.
- Rapid `MatchLoaded` transitions are not debounced; each transition is handled immediately. The idempotency guard is the only protection against duplicate loops.

### 3.3 Polling loop stop

When `StateChanged` fires with any state other than `MatchLoaded`, the teardown sequence is:

1. Cancel the loop-scoped `CancellationTokenSource`. This causes `WaitForNextTickAsync` to throw `OperationCanceledException`, exiting the loop.
2. Await the loop task to completion (using a short timeout). The timer must not be disposed while `WaitForNextTickAsync` is still awaited.
3. Dispose the timer.
4. Dispose the loop-scoped `CancellationTokenSource`.
5. Reset the idempotency flag so a subsequent `MatchLoaded` event can start a new loop.

The loop exits cleanly from its own task. The `OperationCanceledException` from the loop-scoped CTS does **not** propagate out of the service method; it is swallowed at the loop-method boundary. The service remains running and continues to listen for future `StateChanged` events.

### 3.4 Exception handling

Two categories of cancellation must be handled distinctly:

**Loop-scoped cancellation** (state transition away from `MatchLoaded`): The `OperationCanceledException` exits the inner polling loop. It is caught at the loop-method boundary and swallowed. The service remains alive and awaits the next state change.

**Host shutdown** (host `stoppingToken` cancelled): The `OperationCanceledException` exits the inner polling loop and is allowed to propagate out of `ExecuteAsync`. ASP.NET Core handles this as a normal shutdown signal.

For all other exceptions thrown by `CaptureAndBroadcastAsync` on a given tick:

- The exception is caught inside the polling loop (not by the loop-cancellation handler).
- It is logged at **Error** level via `ILogger<ScoreboardPollingService>` using a structured template with the exception as a structured parameter (no string interpolation). This is a code-review gate, not a runtime assertion.
- The loop **continues polling** on subsequent ticks; a single capture failure is not fatal.

### 3.5 Configuration

The polling interval is read from `Scoreboard:CaptureIntervalSeconds` (integer seconds). If the key is absent, the default is **2 seconds**. If the configured value is ≤ 0, the service falls back to the 2s default and logs a warning. The interval is read once at service construction and does not reload at runtime.

### 3.6 Unsubscription and shutdown

In `StopAsync`, the teardown sequence is:

1. Unsubscribe from `IPcsProAutomationService.StateChanged` first — this prevents a racing `StateChanged` event from attempting to start a new polling loop during teardown.
2. Cancel and clean up any active polling loop (following §3.3 steps 1–5).

The `StateChanged` handler is registered once for the service lifetime and unsubscribed once in `StopAsync`. There is no partial or per-cycle subscription management.

---

## §4 Dependencies

- `IScoreboardService` (S-001, delivered) — `CaptureAndBroadcastAsync(CancellationToken)` method.
- `IPcsProAutomationService` — `StateChanged` event + `CurrentState` property (delivered in IS-003).
- `IConfiguration` — for reading `Scoreboard:CaptureIntervalSeconds`.
- `ILogger<ScoreboardPollingService>` — constructor-injected logger (follows `AutoLaunchService` pattern).

---

## §5 Test Cases

All tests use the `MSTest` + `FluentAssertions` + `Moq` stack. Follow the `AutoLaunchServiceTests` builder helper pattern: a static `Build(...)` factory that instantiates `ScoreboardPollingService` directly with injected mocks and configuration.

| TC | Method name | Scenario | Expected outcome |
|---|---|---|---|
| TC-1 | `StartAsync_StateAlreadyMatchLoaded_StartsPollingImmediately` | `CurrentState = MatchLoaded` at startup | `CaptureAndBroadcastAsync` called within the test's wait window |
| TC-2 | `StartAsync_StateNotMatchLoaded_DoesNotStartPolling` | `CurrentState = NotRunning` at startup | `CaptureAndBroadcastAsync` not called during service start |
| TC-3 | `StateChanged_ToMatchLoaded_StartsPolling` | State transitions to `MatchLoaded` after startup | `CaptureAndBroadcastAsync` called within the wait window |
| TC-4 | `StateChanged_AwayFromMatchLoaded_StopsPolling` | State transitions from `MatchLoaded` → `MatchSelection` after polling has started | Polling stops; no further `CaptureAndBroadcastAsync` calls observed after transition |
| TC-5 | `CaptureThrows_ErrorIsLogged_LoopContinues` | `CaptureAndBroadcastAsync` throws on first call, succeeds on second | Error logged once with the exception object as a structured parameter; second tick still calls capture (loop did not terminate) |
| TC-6 | `IntervalConfig_KeyAbsent_DefaultsToTwoSeconds` | Config has no `Scoreboard:CaptureIntervalSeconds` key | Service constructs without error; interval defaults to 2s |
| TC-7 | `IntervalConfig_KeyPresent_UsesConfiguredValue` | Config sets `Scoreboard:CaptureIntervalSeconds = 1` | Ticks arrive within the expected window for a 1s interval |
| TC-8 | `StopAsync_WhilePolling_ExitsCleanly` | `StopAsync` called while polling loop is active | Service stops without throwing; no unhandled exceptions |
| TC-9 | `StartLoop_CalledConcurrently_IsIdempotent` | Two `StateChanged` events fire for `MatchLoaded` concurrently | `CaptureAndBroadcastAsync` is not double-invoked per tick; only one loop is active |
| TC-10 | `StateChanged_AwayFromMatchLoaded_LoopTaskCompletesWithoutException` | State transitions away from `MatchLoaded` | The loop task completes in `RanToCompletion` status, not `Faulted` or `Cancelled` |
| TC-11 | `StopAsync_WhileIdle_ExitsCleanly` | Start service with `CurrentState = NotRunning`, call `StopAsync` without any `StateChanged` events | No exception; service unsubscribes cleanly |
| TC-12 | `CaptureAndBroadcastAsync_ReceivesLoopCancellationToken` | Service starts polling; capture mock captures the token passed to it | The captured token is the loop-scoped token; assert `IsCancellationRequested` on the captured token immediately after the loop task completes (before CTS disposal), or use a mock callback to verify the token identity at point-of-capture |

**Test timing note:** Tests that require polling to fire (TC-1, TC-3, TC-5, TC-7) should use a very short configured interval (e.g., 50ms or less) and `WaitAsync` with a generous timeout (e.g., 5s) via `TaskCompletionSource` callbacks on the mock, following the `AutoLaunchServiceTests` pattern. No `Thread.Sleep`.

**TC-9 concurrency guidance:** Fire the `StateChanged` handler from two concurrent `Task.Run` continuations simultaneously (e.g., `Task.WhenAll`). A well-designed test may use a mock gate (e.g., a semaphore in the mock's start path) to hold both calls until both have passed the idempotency check point, ensuring the race window is genuinely exercised.

---

## §6 Acceptance Criteria

| # | Criterion |
|---|---|
| AC-1 | `ScoreboardPollingService` is added to `PcsRemote.Web`, registered via `AddHostedService` in `Program.cs` |
| AC-2 | The service subscribes to `StateChanged` exactly once in `StartAsync` and unsubscribes in `StopAsync` before cancelling any active loop |
| AC-3 | Cold-start: if state is `MatchLoaded` on startup, polling begins without a `StateChanged` event |
| AC-4 | Polling-loop start is idempotent and thread-safe — concurrent starts result in exactly one active loop |
| AC-5 | A `CaptureAndBroadcastAsync` exception is caught, logged at Error with the exception as a structured parameter, and does not terminate the loop |
| AC-6 | Loop-scoped OCE is swallowed at the loop-method boundary; the service remains alive for future state changes. Host-shutdown OCE propagates out of `ExecuteAsync` normally |
| AC-7 | Polling interval defaults to 2s when `Scoreboard:CaptureIntervalSeconds` is absent or ≤ 0 |
| AC-8 | The loop-scoped `CancellationToken` is forwarded to every `CaptureAndBroadcastAsync` call |
| AC-9 | TC-1 through TC-12 all pass; all existing tests continue to pass |

---

## §7 Out of Scope

- Integration with `ScoreboardPreview` Razor component (S-003).
- `ForceRefreshAsync` — owned by S-004.
- Any changes to `IScoreboardService` or `ScoreboardService`.

---

## §8 Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | GPT: NEEDS REVIEW — 1 HIGH, 2 MEDIUM, 3 LOW; Sonnet: NEEDS REVIEW — 3 HIGH, 5 MEDIUM, 4 LOW; fixes applied in v0.2 |
| R2 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | **UNANIMOUS APPROVED** — all 13 R1 fixes verified; Sonnet 1 LOW (TC-12 timing clarification); fix applied in v0.2 final |


| Field | Value |
|---|---|
| **Step ID** | S-002 |
| **IS Reference** | IS-004-Scoreboard.md §S-002 |
| **HLPS Reference** | HLPS-004-Scoreboard.md |
| **Status** | IN REVIEW |
| **Version** | v0.1 |

---

## §1 Governing Acceptance Criteria

This spec satisfies the following IS-004 HLPS-004 success criteria:

| SC | Requirement |
|---|---|
| S-SC-1 | Scoreboard image appears in all connected browsers within 3s of the automation service capturing it |
| S-SC-3 | Polling is tied to the `MatchLoaded` state — starts only when the state machine enters that state and stops when it leaves |
| S-SC-9 | The polling interval is configurable via `Scoreboard:CaptureIntervalSeconds` |

---

## §2 What This Step Delivers

A new background hosted service — `ScoreboardPollingService` — is added to `PcsRemote.Web`. It owns the `PeriodicTimer` that drives periodic capture, and its lifecycle is entirely controlled by the `IPcsProAutomationService.StateChanged` event and the ASP.NET Core host lifecycle.

The service does **not** own the capture logic; it delegates all capture to `IScoreboardService.CaptureAndBroadcastAsync`. It is registered in `Program.cs` via `AddHostedService`.

---

## §3 Behavioural Contract

### 3.1 Startup (cold-start)

On `StartAsync`, the service must:

1. Subscribe to `IPcsProAutomationService.StateChanged` **before** reading `CurrentState`.
2. Read `CurrentState` immediately after subscribing.
3. If `CurrentState` is already `MatchLoaded`, start the polling loop immediately without waiting for a state-change event.

This subscribe-before-read ordering closes the race window where state transitions to `MatchLoaded` between the read and the subscribe.

### 3.2 Polling loop start

When the polling loop starts (either from cold-start or from a `StateChanged` event targeting `MatchLoaded`):

- A new `PeriodicTimer` is created using the configured interval.
- The loop calls `IScoreboardService.CaptureAndBroadcastAsync` on each tick.
- The start is **idempotent**: if a polling loop is already active, a second start attempt is a no-op.

### 3.3 Polling loop stop

When `StateChanged` fires with any state other than `MatchLoaded`:

- The active polling loop is cancelled (via a loop-scoped `CancellationTokenSource`).
- The timer is disposed.
- The loop exits cleanly; no exception propagates out of the loop on cancellation.

On `StopAsync` (host shutdown), the host's `stoppingToken` is cancelled, which propagates into `PeriodicTimer.WaitForNextTickAsync`. The loop exits cleanly.

### 3.4 Exception handling

If `CaptureAndBroadcastAsync` throws on a given tick:

- The exception is caught inside the polling loop.
- It is logged at **Error** level via `ILogger<ScoreboardPollingService>` using a structured template (no string interpolation).
- `OperationCanceledException` (from host shutdown or loop cancellation) is **not** caught by the error handler — it is allowed to propagate and terminate the loop.
- The loop **continues polling** on subsequent ticks; a single capture failure is not fatal.

### 3.5 Configuration

The polling interval is read from `Scoreboard:CaptureIntervalSeconds` (integer seconds). If the key is absent, the default is **2 seconds**. The interval is read once at service construction and does not reload at runtime.

---

## §4 Dependencies

- `IScoreboardService` (S-001, delivered) — `CaptureAndBroadcastAsync` method.
- `IPcsProAutomationService` — `StateChanged` event + `CurrentState` property (delivered in IS-003).
- `IConfiguration` — for reading `Scoreboard:CaptureIntervalSeconds`.
- `ILogger<ScoreboardPollingService>` — constructor-injected logger (follows `AutoLaunchService` pattern).

---

## §5 Test Cases

All tests use the `MSTest` + `FluentAssertions` + `Moq` stack. Follow the `AutoLaunchServiceTests` builder helper pattern: a static `Build(...)` factory that instantiates `ScoreboardPollingService` directly with injected mocks and configuration.

| TC | Method name | Scenario | Expected outcome |
|---|---|---|---|
| TC-1 | `StartAsync_StateAlreadyMatchLoaded_StartsPollingImmediately` | `CurrentState = MatchLoaded` at startup | `CaptureAndBroadcastAsync` called within the test's wait window |
| TC-2 | `StartAsync_StateNotMatchLoaded_DoesNotStartPolling` | `CurrentState = NotRunning` at startup | `CaptureAndBroadcastAsync` not called during service start |
| TC-3 | `StateChanged_ToMatchLoaded_StartsPolling` | State transitions to `MatchLoaded` after startup | `CaptureAndBroadcastAsync` called within the wait window |
| TC-4 | `StateChanged_AwayFromMatchLoaded_StopsPolling` | State transitions from `MatchLoaded` → `MatchSelection` after polling has started | Polling stops; no further `CaptureAndBroadcastAsync` calls observed after transition |
| TC-5 | `CaptureThrows_ErrorIsLogged_LoopContinues` | `CaptureAndBroadcastAsync` throws on first call, succeeds on second | Error logged once; second tick still calls capture (loop did not terminate) |
| TC-6 | `IntervalConfig_KeyAbsent_DefaultsToTwoSeconds` | Config has no `Scoreboard:CaptureIntervalSeconds` key | Service constructs without error; interval defaults to 2s (verifiable via a config-reading integration or constructor assertion) |
| TC-7 | `IntervalConfig_KeyPresent_UsesConfiguredValue` | Config sets `Scoreboard:CaptureIntervalSeconds = 1` | Service constructs without error; ticks arrive within the expected window for a 1s interval |
| TC-8 | `StopAsync_WhilePolling_ExitsCleanly` | `StopAsync` called while polling loop is active | Service stops without throwing; no unhandled exceptions |
| TC-9 | `StartLoop_CalledConcurrently_IsIdempotent` | Two `StateChanged` events fire for `MatchLoaded` in quick succession | `CaptureAndBroadcastAsync` is not double-invoked per tick; only one loop is active |

**Test timing note:** Tests that require polling to fire (TC-1, TC-3, TC-5, TC-7) should use a very short configured interval (e.g., 50ms or less) and `WaitAsync` with a generous timeout (e.g., 5s) via `TaskCompletionSource` callbacks on the mock, following the `AutoLaunchServiceTests` pattern. No `Thread.Sleep`.

---

## §6 Acceptance Criteria

| # | Criterion |
|---|---|
| AC-1 | `ScoreboardPollingService` is added to `PcsRemote.Web`, registered via `AddHostedService` in `Program.cs` |
| AC-2 | The service subscribes to `StateChanged` in `StartAsync` and unsubscribes in `StopAsync` |
| AC-3 | Cold-start: if state is `MatchLoaded` on startup, polling begins without a `StateChanged` event |
| AC-4 | Polling-loop start is idempotent — a second concurrent start for `MatchLoaded` has no observable effect |
| AC-5 | A `CaptureAndBroadcastAsync` exception is caught, logged at Error, and does not terminate the loop |
| AC-6 | `OperationCanceledException` (host shutdown / loop stop) is NOT swallowed — it exits the loop cleanly |
| AC-7 | Polling interval defaults to 2s when `Scoreboard:CaptureIntervalSeconds` is absent |
| AC-8 | TC-1 through TC-9 all pass; all existing tests continue to pass |

---

## §7 Out of Scope

- Integration with `ScoreboardPreview` Razor component (S-003).
- `ForceRefreshAsync` — owned by S-004.
- Any changes to `IScoreboardService` or `ScoreboardService`.

---

## §8 Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | — | GPT-4.1, Claude Sonnet 4.6 | Pending |
