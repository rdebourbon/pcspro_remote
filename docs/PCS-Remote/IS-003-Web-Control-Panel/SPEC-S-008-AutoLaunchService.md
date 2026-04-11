# SPEC-S-008: Auto-Launch IHostedService

| Field | Value |
|---|---|
| **Document** | SPEC-S-008-AutoLaunchService.md |
| **Status** | IN REVIEW |
| **Version** | 0.4 |
| **Date** | 2026-04-12 |
| **Step ID** | S-008 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Success Criteria** | W-SC-11 |
| **Dependencies** | S-003 (hub broadcasts state transitions); S-004 (error state shown by status indicator) |

---

## 1. Objective

On application start, automatically trigger `LaunchAndLoginAsync` on `IPcsProAutomationService` so that PCS Pro begins loading without requiring manual user intervention. This behaviour is controlled by a configuration flag (`PcsPro:AutoLaunch`, default `true`). If the flag is explicitly set to `false`, the launch is suppressed and the service does nothing (manual trigger is out of Phase 1 scope). If the key is absent the flag defaults to `true` (see R-1 and AC-5). The initial service state (regardless of this flag) is `NotRunning`, which is the default provided by `IPcsProAutomationService` before any launch — S-008 is not responsible for setting it.

---

## 2. Background

The .NET Generic Host's `BackgroundService` base class is the idiomatic way to implement long-running background work. In .NET 8, `BackgroundService.StartAsync` calls `ExecuteAsync` **inline** (no `Task.Run` wrapper). For paths containing a real `await` (e.g., `AutoLaunch=true` awaiting `LaunchAndLoginAsync`), execution suspends at the first suspension point, `StartAsync` returns `Task.CompletedTask`, and the `ExecuteAsync` continuation resumes asynchronously (on the thread pool in the Generic Host's default context) — it does not block the host startup pipeline. For paths with no `await` (e.g., `AutoLaunch=false`), `ExecuteAsync` completes synchronously before `StartAsync` returns. In both cases `BackgroundService.ExecuteTask` (public in .NET 8) holds the backing `Task` and is the correct synchronization handle for tests.

Any exception thrown by `LaunchAndLoginAsync` corresponds to an `Error` state in the service, which is already visually handled by the `PcsProStatusIndicator` component (S-004). The hosted service must therefore catch and log exceptions from `LaunchAndLoginAsync` — it must not let them propagate to the host and crash the process. Configuration reading in `ExecuteAsync` (Step 1) is a plain dictionary lookup and is not subject to this swallowing rule.

---

## 3. Design

### 3.1 New class: `AutoLaunchService`

Location: `src/PcsRemote.Web/` (same project as Program.cs)

Inherits `BackgroundService`. Receives `IPcsProAutomationService`, `IConfiguration`, and `ILogger<AutoLaunchService>` via constructor injection. `ILogger<AutoLaunchService>` is a **mandatory** constructor parameter — `ILogger<T>` is always resolvable from the Generic Host container and must not be optional or null-guarded, as R-3 requires unconditional error logging.

**Behaviour of `ExecuteAsync(CancellationToken stoppingToken)`:**

```
1. Read PcsPro:AutoLaunch from IConfiguration (default true if key absent)
2. If AutoLaunch == false → return immediately (no-op)
3. Try: await AutomationService.LaunchAndLoginAsync(stoppingToken)
4. Catch OperationCanceledException → return silently (app is shutting down)
5. Catch any other Exception → log at Error level (including the exception object), return
```

> **Design note — OCE swallowing:** Step 4 catches all `OperationCanceledException` regardless of which cancellation token caused it. This is a deliberate simplification for Phase 1: in the mock implementation `LaunchAndLoginAsync` does not create internal `CancellationTokenSource` instances, so all OCEs during this phase are host-shutdown driven. If Phase 2 introduces internal timeouts within the automation layer, this rule should be revisited to discriminate on `ex.CancellationToken == stoppingToken`.

### 3.2 Configuration

Add `"AutoLaunch": true` to the `PcsPro` section of `appsettings.json`:

```json
"PcsPro": {
  "AutoLaunch": true,
  "UseMock": false,
  "Mock": {}
}
```

### 3.3 DI Registration

Add to `Program.cs` after existing `AddHostedService<PcsProStateBroadcaster>()`:

```
builder.Services.AddHostedService<AutoLaunchService>();
```

> **Ordering rationale:** `AutoLaunchService` must be registered **after** `PcsProStateBroadcaster` to ensure the hub subscription is active before the launch flow produces its first state transition (`NotRunning` → `Launching`). `PcsProStateBroadcaster` implements `IHostedService` directly (not `BackgroundService`) and subscribes to `StateChanged` synchronously in `StartAsync`, returning `Task.CompletedTask` immediately — the subscription is guaranteed to be active before the host proceeds to start the next service. Reversing the order risks those early transitions being missed by the broadcaster.

No other Program.cs changes, except adding `public partial class Program { }` at the end of the file to expose the implicit Program class for `WebApplicationFactory` in the integration test (Test-6).

---

## 4. Requirements

| ID | Requirement |
|---|---|
| **R-1** | When `PcsPro:AutoLaunch` is `true` (or absent), `AutoLaunchService` MUST call `LaunchAndLoginAsync` exactly once during host startup. |
| **R-2** | When `PcsPro:AutoLaunch` is `false`, `AutoLaunchService` MUST NOT call `LaunchAndLoginAsync`. |
| **R-3** | An exception thrown by `LaunchAndLoginAsync` (other than `OperationCanceledException`) MUST be caught, logged at Error level, and must NOT propagate to the host. |
| **R-4** | `OperationCanceledException` (signalling application shutdown) MUST be caught silently — it must not be logged as an error. |
| **R-5** | `AutoLaunchService` MUST pass the `stoppingToken` it receives from `ExecuteAsync` through to `LaunchAndLoginAsync` so that in-flight automation is cancellable on app stop. |

---

## 5. Acceptance Criteria

### AC-1 — AutoLaunch=true triggers launch on startup

Given: `AutoLaunchService` is started with `AutoLaunch=true` and the mock for `LaunchAndLoginAsync` is configured via a Moq `Callback` to: (a) complete a `TaskCompletionSource<bool>` sentinel and (b) capture the `CancellationToken` argument.  
When: The `TaskCompletionSource` sentinel completes (with a 5 s timeout via `await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5))`).  
Then: `LaunchAndLoginAsync` has been called exactly once.  
Token verification (R-5): call `service.StopAsync(CancellationToken.None)` and assert the captured token's `IsCancellationRequested == true`, proving the service's stopping token was forwarded.

### AC-2 — AutoLaunch=false suppresses launch

Given: `AutoLaunchService` is started with `AutoLaunch=false`.  
When: `await service.StartAsync(CancellationToken.None)` returns (`BackgroundService.StartAsync` does not use its `cancellationToken` parameter in .NET 8; the `AutoLaunch=false` path contains no `await`, so `ExecuteAsync` completes synchronously before `StartAsync` returns; alternatively use `await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))` for runtime-version robustness).  
Then: `LaunchAndLoginAsync` is NOT called.

### AC-3 — Non-cancellation exception is logged and swallowed

Given: `AutoLaunchService` is started with `AutoLaunch=true` and `LaunchAndLoginAsync` is set up to throw `InvalidOperationException` (via `.ThrowsAsync<InvalidOperationException>()`).  
When: `await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))` completes (guarantees `ExecuteAsync` has returned and the catch block has fully executed).  
Then: The exception is logged at Error level (the log entry must include the exception object) and does NOT propagate (the host continues running).

### AC-4 — OperationCanceledException is swallowed silently

Given: `AutoLaunchService` is started with `AutoLaunch=true` and `LaunchAndLoginAsync` is set up to throw `OperationCanceledException` (via `.ThrowsAsync<OperationCanceledException>()`).  
When: `await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))` completes.  
Then: No error is logged; the exception does NOT propagate.

### AC-5 — AutoLaunch key absent defaults to true

Given: `AutoLaunchService` is started with a configuration that contains no `PcsPro:AutoLaunch` key and the mock is configured with a TCS sentinel.  
When: The `TaskCompletionSource` sentinel completes (with a 5 s timeout).  
Then: `LaunchAndLoginAsync` has been called exactly once (the absent key is treated as `true`).

---

## 6. Tests

New test file: `tests/PcsRemote.Web.Tests/AutoLaunchServiceTests.cs`

**Unit tests (Test-1 through Test-5):** Use `Moq` for `IPcsProAutomationService`. For AC-3, use a `Mock<ILogger<AutoLaunchService>>` and verify with the exact Moq 4.16+ pattern (requires `It.IsAnyType` for the open-generic `TState` parameter):

```csharp
loggerMock.Verify(
    x => x.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.Is<Exception>(e => e is InvalidOperationException),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once);
```

Using `It.IsAny<object>()` for the `TState` position does **not** match in Moq — the verify fires zero times and `Times.Once` passes vacuously. For all other tests use `NullLogger<AutoLaunchService>`. Configuration is provided via `new ConfigurationBuilder().AddInMemoryCollection(...)`.

**Test-6 (integration):** Use `WebApplicationFactory<Program>` with:
- Configuration overrides: `PcsPro:UseMock=true`, `PcsPro:AutoLaunch=true`
- DI override via `ConfigureTestServices` (runs after application services, guaranteeing last-wins ordering):
  ```csharp
  factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
      services.Replace(ServiceDescriptor.Singleton<IPcsProAutomationService>(moqMock.Object))));
  ```
  Use `services.Replace` to explicitly remove the prior descriptor. The Moq mock must set up `CurrentState` (returns `PcsProState.NotRunning`) and the TCS sentinel on `LaunchAndLoginAsync`. Build the factory client to trigger host startup, then await the TCS sentinel.

**Async synchronization rules:**
- **AC-1, AC-5, Test-6:** `LaunchAndLoginAsync` mock configured with:
  ```csharp
  .Callback<CancellationToken>(ct => { capturedToken = ct; tcs.TrySetResult(true); })
  .Returns(Task.CompletedTask);
  ```
  The `.Returns(Task.CompletedTask)` is **mandatory** — without it Moq returns `null`, causing `await LaunchAndLoginAsync(...)` to throw `NullReferenceException`. It also ensures `ExecuteAsync` returns promptly so `StopAsync(CancellationToken.None)` does not hang waiting for a non-completing task. Use `TrySetResult` to guard against double-calls. Construct all TCS sentinels as `new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)` to prevent inline continuation execution on the triggering thread. Await via `await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5))`.
- **AC-2:** `await service.StartAsync(CancellationToken.None)` is the synchronization barrier (`BackgroundService.StartAsync` ignores its `cancellationToken` parameter in .NET 8; `CancellationToken.None` is the correct argument). For runtime-version robustness, prefer `await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))`.
- **AC-3, AC-4:** `await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5))` guarantees `ExecuteAsync` has fully returned (including the catch block) before any assertions run. Do NOT use the TCS pattern here — the exception paths have no call site for `tcs.SetResult`.

**Test list (6 tests):**

| Test name | AC |
|---|---|
| `AutoLaunch_True_CallsLaunchAndLoginAsync` | AC-1 |
| `AutoLaunch_False_DoesNotCallLaunchAndLoginAsync` | AC-2 |
| `LaunchThrowsException_IsLoggedAndSwallowed` | AC-3 |
| `LaunchThrowsOperationCancelled_IsSwallowedSilently` | AC-4 |
| `AutoLaunch_KeyAbsent_CallsLaunchAndLoginAsync` | AC-5 |
| `Integration_AutoLaunchService_RegisteredAndTriggersLaunch` | IS-003 §S-008 DI wiring |

---

## 7. Files Changed

| File | Change |
|---|---|
| `src/PcsRemote.Web/AutoLaunchService.cs` | **NEW**: `BackgroundService` implementation |
| `src/PcsRemote.Web/Program.cs` | Add `AddHostedService<AutoLaunchService>()`; add `public partial class Program { }` |
| `src/PcsRemote.Web/appsettings.json` | Add `PcsPro:AutoLaunch: true` |
| `tests/PcsRemote.Web.Tests/AutoLaunchServiceTests.cs` | **NEW**: 5 unit tests + 1 integration test |
| `tests/PcsRemote.Web.Tests/PcsRemote.Web.Tests.csproj` | Add `Microsoft.AspNetCore.Mvc.Testing` package reference |

---

## 8. Out of Scope

- Manual trigger UI for when `AutoLaunch=false` — deferred to HLPS-005
- Re-launch on `Error` state — deferred to HLPS-005
- Any change to how `LaunchAndLoginAsync` is implemented — that is HLPS-006
- `StopAsync` cancellation logic beyond what `BackgroundService` provides by default
