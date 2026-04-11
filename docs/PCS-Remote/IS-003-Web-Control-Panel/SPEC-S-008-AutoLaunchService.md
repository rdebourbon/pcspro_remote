# SPEC-S-008: Auto-Launch IHostedService

| Field | Value |
|---|---|
| **Document** | SPEC-S-008-AutoLaunchService.md |
| **Status** | IN REVIEW |
| **Version** | 0.1 |
| **Date** | 2026-04-12 |
| **Step ID** | S-008 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Success Criteria** | W-SC-11 |
| **Dependencies** | S-003 (hub broadcasts state transitions); S-004 (error state shown by status indicator) |

---

## 1. Objective

On application start, automatically trigger `LaunchAndLoginAsync` on `IPcsProAutomationService` so that PCS Pro begins loading without requiring manual user intervention. This behaviour is controlled by a configuration flag (`PcsPro:AutoLaunch`, default `true`). If the flag is disabled, the service starts in `NotRunning` state and awaits a future manual trigger (not in Phase 1 scope).

---

## 2. Background

The .NET Generic Host's `BackgroundService` base class is the idiomatic way to implement long-running background work. Its `ExecuteAsync` method is called once at host startup on a background context, so it does not block `StartAsync` or the rest of the DI pipeline. This is the preferred base class for `AutoLaunchService` over raw `IHostedService`.

`IPcsProAutomationService.LaunchAndLoginAsync` is a long-running async operation that drives the state machine from `NotRunning` through `Launching`, `Running`, `LoggingIn`, and into `MatchSelection`. Any exception thrown by this call corresponds to an `Error` state in the service, which is already visually handled by the `PcsProStatusIndicator` component (S-004). The hosted service must therefore swallow non-cancellation exceptions after logging them — it must not let them propagate to the host and crash the process.

The `PcsPro:AutoLaunch` flag does not require a dedicated options class. The service may read it directly from `IConfiguration` using a default of `true` when the key is absent.

---

## 3. Design

### 3.1 New class: `AutoLaunchService`

Location: `src/PcsRemote.Web/` (same project as Program.cs)

Inherits `BackgroundService`. Receives `IPcsProAutomationService` and `IConfiguration` via constructor injection. Optionally receives `ILogger<AutoLaunchService>` for exception logging.

**Behaviour of `ExecuteAsync(CancellationToken stoppingToken)`:**

```
1. Read PcsPro:AutoLaunch from IConfiguration (default true if key absent)
2. If AutoLaunch == false → return immediately (no-op)
3. Try: await AutomationService.LaunchAndLoginAsync(stoppingToken)
4. Catch OperationCanceledException → return silently (app is shutting down)
5. Catch any other Exception → log at Error level, return (error visible via S-004 indicator)
```

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

No other Program.cs changes.

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

Given: `AutoLaunchService` is started with `AutoLaunch=true`.  
When: `StartAsync` completes.  
Then: `LaunchAndLoginAsync` is called exactly once, with the service's stopping token.

### AC-2 — AutoLaunch=false suppresses launch

Given: `AutoLaunchService` is started with `AutoLaunch=false`.  
When: `StartAsync` completes.  
Then: `LaunchAndLoginAsync` is NOT called.

### AC-3 — Non-cancellation exception is logged and swallowed

Given: `AutoLaunchService` is started with `AutoLaunch=true` and `LaunchAndLoginAsync` throws an `InvalidOperationException`.  
When: `ExecuteAsync` handles the exception.  
Then: The exception is logged at Error level and does NOT propagate (the host continues running).

### AC-4 — OperationCanceledException is swallowed silently

Given: `AutoLaunchService` is started with `AutoLaunch=true` and `LaunchAndLoginAsync` throws `OperationCanceledException`.  
When: `ExecuteAsync` handles the exception.  
Then: No error is logged; the exception does NOT propagate.

---

## 6. Tests

New test file: `tests/PcsRemote.Web.Tests/AutoLaunchServiceTests.cs`

All tests use `Moq` for `IPcsProAutomationService` and `Microsoft.Extensions.Logging.NullLogger<AutoLaunchService>` for the logger (or a Moq logger for AC-3 verification). Configuration is provided via `Microsoft.Extensions.Configuration.ConfigurationBuilder` with an in-memory collection.

**Test list (4 tests):**

| Test name | AC |
|---|---|
| `AutoLaunch_True_CallsLaunchAndLoginAsync` | AC-1 |
| `AutoLaunch_False_DoesNotCallLaunchAndLoginAsync` | AC-2 |
| `LaunchThrowsException_IsLoggedAndSwallowed` | AC-3 |
| `LaunchThrowsOperationCancelled_IsSwallowedSilently` | AC-4 |

> **Test note on async timing:** `ExecuteAsync` runs on a background context and `StartAsync` returns before it completes. Tests must use `Task.Delay` polling or a `TaskCompletionSource` sentinel (e.g., set up the mock to complete a TCS when called) to observe the async behaviour. `WaitForAssertion` is a bUnit concern and does not apply here; use `await Task.Delay(...)` with `Assert` or a `TCS`-based approach.

---

## 7. Files Changed

| File | Change |
|---|---|
| `src/PcsRemote.Web/AutoLaunchService.cs` | **NEW**: `BackgroundService` implementation |
| `src/PcsRemote.Web/Program.cs` | Add `AddHostedService<AutoLaunchService>()` |
| `src/PcsRemote.Web/appsettings.json` | Add `PcsPro:AutoLaunch: true` |
| `tests/PcsRemote.Web.Tests/AutoLaunchServiceTests.cs` | **NEW**: 4 unit tests |

---

## 8. Out of Scope

- Manual trigger UI for when `AutoLaunch=false` — deferred to HLPS-005
- Re-launch on `Error` state — deferred to HLPS-005
- Any change to how `LaunchAndLoginAsync` is implemented — that is HLPS-006
- `StopAsync` cancellation logic beyond what `BackgroundService` provides by default
