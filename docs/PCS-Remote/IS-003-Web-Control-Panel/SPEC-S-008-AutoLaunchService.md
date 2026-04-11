# SPEC-S-008: Auto-Launch IHostedService

| Field | Value |
|---|---|
| **Document** | SPEC-S-008-AutoLaunchService.md |
| **Status** | IN REVIEW |
| **Version** | 0.2 |
| **Date** | 2026-04-12 |
| **Step ID** | S-008 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Success Criteria** | W-SC-11 |
| **Dependencies** | S-003 (hub broadcasts state transitions); S-004 (error state shown by status indicator) |

---

## 1. Objective

On application start, automatically trigger `LaunchAndLoginAsync` on `IPcsProAutomationService` so that PCS Pro begins loading without requiring manual user intervention. This behaviour is controlled by a configuration flag (`PcsPro:AutoLaunch`, default `true`). If the flag is disabled or absent, the launch is suppressed; the service does nothing and awaits a future manual trigger (not in Phase 1 scope). The initial service state (regardless of this flag) is `NotRunning`, which is the default provided by `IPcsProAutomationService` before any launch — S-008 is not responsible for setting it.

---

## 2. Background

The .NET Generic Host's `BackgroundService` base class is the idiomatic way to implement long-running background work. Its `ExecuteAsync` method is called once at host startup on a background context, so it does not block `StartAsync` or the rest of the DI pipeline. This is the preferred base class for `AutoLaunchService` over raw `IHostedService`.

`IPcsProAutomationService.LaunchAndLoginAsync` is a long-running async operation that drives the state machine from `NotRunning` through `Launching`, `Running`, `LoggingIn`, and into `MatchSelection`. Any exception thrown by this call corresponds to an `Error` state in the service, which is already visually handled by the `PcsProStatusIndicator` component (S-004). The hosted service must therefore swallow non-cancellation exceptions after logging them — it must not let them propagate to the host and crash the process.

The `PcsPro:AutoLaunch` flag does not require a dedicated options class. The service may read it directly from `IConfiguration` using a default of `true` when the key is absent.

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

> **Ordering rationale:** `AutoLaunchService` must be registered **after** `PcsProStateBroadcaster` to ensure the hub subscription is active before the launch flow produces its first state transition (`NotRunning` → `Launching`). Reversing the order risks those early transitions being missed by the broadcaster.

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

Given: `AutoLaunchService` is started with `AutoLaunch=true` and the mock for `LaunchAndLoginAsync` is configured to complete a `TaskCompletionSource<bool>` sentinel when called.  
When: The `TaskCompletionSource` sentinel completes (with a test timeout of 5 s).  
Then: `LaunchAndLoginAsync` has been called exactly once, with the service's stopping token.

### AC-2 — AutoLaunch=false suppresses launch

Given: `AutoLaunchService` is started with `AutoLaunch=false`.  
When: `ExecuteAsync` completes (observable via a separate TCS sentinel that signals when `ExecuteAsync` returns).  
Then: `LaunchAndLoginAsync` is NOT called.

### AC-3 — Non-cancellation exception is logged and swallowed

Given: `AutoLaunchService` is started with `AutoLaunch=true` and `LaunchAndLoginAsync` throws an `InvalidOperationException`.  
When: `ExecuteAsync` handles the exception.  
Then: The exception is logged at Error level (the log entry must include the exception object) and does NOT propagate (the host continues running).

### AC-4 — OperationCanceledException is swallowed silently

Given: `AutoLaunchService` is started with `AutoLaunch=true` and `LaunchAndLoginAsync` throws `OperationCanceledException`.  
When: `ExecuteAsync` handles the exception.  
Then: No error is logged; the exception does NOT propagate.

### AC-5 — AutoLaunch key absent defaults to true

Given: `AutoLaunchService` is started with a configuration that contains no `PcsPro:AutoLaunch` key.  
When: The `TaskCompletionSource` sentinel completes (with a test timeout of 5 s).  
Then: `LaunchAndLoginAsync` has been called exactly once (the absent key is treated as `true`).

---

## 6. Tests

New test file: `tests/PcsRemote.Web.Tests/AutoLaunchServiceTests.cs`

All unit tests (Test-1 through Test-5) use `Moq` for `IPcsProAutomationService` and `NullLogger<AutoLaunchService>` (or a Moq `ILogger` for AC-3 verification). Configuration is provided via `Microsoft.Extensions.Configuration.ConfigurationBuilder` with an in-memory collection.

Test-6 is an integration test using `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>` with configuration overrides (`PcsPro:UseMock=true`, `PcsPro:AutoLaunch=true`), replacing `IPcsProAutomationService` with a Moq mock that has a TCS sentinel. It validates the full DI wiring chain (that `Program.cs` actually registers `AutoLaunchService`).

**Async timing:** `ExecuteAsync` runs on a background context — `StartAsync` returns before it runs. Tests MUST use a `TaskCompletionSource<bool>` sentinel: configure the mock to complete a TCS when `LaunchAndLoginAsync` is called, then `await tcs.Task` with a `CancellationTokenSource` timeout of 5 s. `Task.Delay` polling is NOT an acceptable alternative (non-deterministic on slow CI agents).

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
