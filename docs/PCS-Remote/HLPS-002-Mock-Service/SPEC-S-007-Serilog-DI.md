# SPEC-S-007: Serilog Integration and DI Registration

| Field | Value |
|---|---|
| **Document** | SPEC-S-007-Serilog-DI.md |
| **Status** | APPROVED |
| **Version** | 0.5 |
| **Step** | IS-002 S-007 |
| **Date** | 2026-04-11 |
| **Governing Docs** | HLPS-002-Mock-Service.md (APPROVED v0.3), IS-002-Mock-Service.md (APPROVED v0.2) |
| **Branch** | `feature/hlps002-S007-serilog-di` |
| **Branch target** | `master` |

---

## 1. Context

S-006 (Scoreboard Image) is complete. `MockPcsProAutomationService` is fully implemented.  
This final IS-002 step has two responsibilities:

1. **Logging finalisation** — audit and confirm all structured `ILogger` calls in `MockPcsProAutomationService` use the correct severity levels per the IS-002 contract.
2. **DI registration** — add a `IServiceCollection` extension method that reads `PcsPro:UseMock` from configuration and registers `IPcsProAutomationService` accordingly; wire this into `PcsRemote.Web/Program.cs`.

Completing this step closes M-SC-5 (mock selectable via configuration) and M-SC-7 (logging observable at correct levels), and satisfies all remaining HLPS-002 success criteria.

---

## 2. Scope

### 2.1 Logging Level Audit

The IS-002 logging contract is:

| Event type | Required level |
|---|---|
| Lifecycle method entry (LaunchAndLoginAsync, LoadMatchAsync, StopAsync) | `Information` |
| Each state transition reached (Launching, LoginScreen, etc.) | `Debug` |
| Lifecycle method completion | `Information` |
| Error injection triggered | `Warning` |
| ChangeMatch intermediate path | `Debug` |

Review all `_logger` calls in `MockPcsProAutomationService`. Confirm they comply. Correct any that deviate. Do not add unnecessary calls — the service is not required to log `GetTodaysMatchesAsync`, `CaptureScoreboardImageAsync`, or the helper methods.

> **Note:** `StopAsync` logs entry (`"StopAsync starting from {State}"`) **before** the `NotRunning` early-return guard. This means calling `StopAsync` when already in `NotRunning` produces 1 `Information` log with no corresponding completion log. This is the existing intended behaviour and must **not** be changed.

### 2.2 DI Extension Method

Add a new file `MockServiceCollectionExtensions.cs` to `PcsRemote.Automation.Mock` containing a `public static class MockServiceCollectionExtensions` with one public extension method on `IServiceCollection` that **returns `IServiceCollection`** (enabling fluent chaining, consistent with ASP.NET Core conventions):

- Accepts an `IConfiguration` argument.
- Reads a boolean flag from key `PcsPro:UseMock`.
- **If `true`**: binds `MockPcsProOptions` from configuration section `PcsPro:Mock`; registers `MockPcsProAutomationService` as `IPcsProAutomationService` with singleton lifetime.
- **If `false`**: registers `NotSupportedPcsProAutomationService` (an `internal sealed` class within the same file) as `IPcsProAutomationService` singleton. The placeholder throws `NotSupportedException` (with message `"Real PCS Pro automation service is not yet implemented."`) from every `Task`-returning interface method (`LaunchAndLoginAsync`, `GetTodaysMatchesAsync`, `LoadMatchAsync`, `GetTeamNamesAsync`, `RefreshScoreboardAsync`, `CaptureScoreboardImageAsync`, `StopAsync`). The `CurrentState` property getter returns `PcsProState.NotRunning` (safe for startup reads; does not throw). The `StateChanged` event add/remove accessors are no-ops. This ensures the DI graph resolves cleanly at startup.

`NotSupportedPcsProAutomationService` is internal and its concrete type is not directly assertable from the test assembly without `InternalsVisibleTo`. **AC-8 therefore uses a behaviour-based assertion**: resolve `IPcsProAutomationService`, call `LaunchAndLoginAsync`, assert `NotSupportedException` is thrown.

### 2.3 Web Wiring

- `PcsRemote.Web.csproj`: add `<ProjectReference>` to `PcsRemote.Automation.Mock`.
- `PcsRemote.Web/Program.cs`: call the DI extension immediately after the Serilog configuration block — `builder.Services.AddPcsProAutomationService(builder.Configuration)`.
- `appsettings.json`: add a `PcsPro` section with `UseMock: false` and an empty `Mock` sub-object as a default skeleton.
- `appsettings.Development.json`: set `PcsPro:UseMock` to `true` so development mode runs the mock automatically.

### 2.4 New Package Dependencies

The following packages must be added to `Directory.Packages.props` and the noted projects:

| Package | Version | Add to |
|---|---|---|
| `Microsoft.Extensions.DependencyInjection` | `8.0.2` | CPM + `PcsRemote.Automation.Mock.csproj` (production: `AddSingleton<>` extension methods) + `PcsRemote.Automation.Mock.Tests.csproj` (tests: `ServiceCollection` concrete class) |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | `8.0.2` | CPM + `PcsRemote.Automation.Mock.csproj` (production: `services.Configure<MockPcsProOptions>(configuration.GetSection("PcsPro:Mock"))` extension method; also brings `IConfiguration` / `IConfigurationSection` transitively via its dependency on `Microsoft.Extensions.Configuration.Abstractions`) |
| `Microsoft.Extensions.Configuration` | `8.0.2` | CPM + `PcsRemote.Automation.Mock.Tests.csproj` (tests: `ConfigurationBuilder`) |
| `Microsoft.Extensions.Configuration.Memory` | `8.0.2` | CPM + `PcsRemote.Automation.Mock.Tests.csproj` (tests: `AddInMemoryCollection` extension) |

> **Note on transitivity:** `Microsoft.Extensions.DependencyInjection.Abstractions` (`IServiceCollection` interface) is available transitively via `Microsoft.Extensions.Options`. `IConfiguration` / `IConfigurationSection` are available transitively via `Microsoft.Extensions.Options.ConfigurationExtensions`. The full DI package and the Configuration packages are not available transitively and must be added explicitly.

> **IS-002 override:** IS-002 §S-007 verification intent states "unit tests inject an in-memory Serilog sink." This spec supersedes that intent: a minimal `ILogger<MockPcsProAutomationService>` implementation capturing `(LogLevel, message)` pairs is used in place of a Serilog sink. M-SC-7 is satisfied by this approach. The IS-002 verification intent will be updated on delivery.

---

## 3. Requirements

| Ref | Requirement |
|---|---|
| R-1 | All `_logger` calls in `MockPcsProAutomationService` comply with the IS-002 log-level contract: lifecycle entry/completion = `Information`; per-state `Debug`; error injection = `Warning`. |
| R-2 | `MockServiceCollectionExtensions.AddPcsProAutomationService` exists as a public extension method on `IServiceCollection`, accepts `IConfiguration`, and compiles without warnings. |
| R-3 | When `PcsPro:UseMock=true`, the DI container resolves `IPcsProAutomationService` as an instance of `MockPcsProAutomationService`. |
| R-4 | When `PcsPro:UseMock=false`, the DI container resolves `IPcsProAutomationService` as an instance of the internal placeholder (not `MockPcsProAutomationService`). |
| R-5 | `PcsRemote.Web` references `PcsRemote.Automation.Mock` and calls the extension method in `Program.cs`. |
| R-6 | `appsettings.json` contains `PcsPro:UseMock=false`; `appsettings.Development.json` contains `PcsPro:UseMock=true`. |
| R-7 | `dotnet build` exits 0 with 0 warnings for the full solution. |
| R-8 | All pre-existing tests continue to pass. New logging + DI tests pass. |

---

## 4. Acceptance Criteria

### AC-1 — LaunchAndLoginAsync logs Information on entry and completion (EP=0.0)
With a captured test logger and `ErrorProbability=0.0`, calling `LaunchAndLoginAsync` emits **exactly 2** `Information`-level messages: one at method entry (`"LaunchAndLoginAsync starting"`) and one at completion (`"LaunchAndLoginAsync complete — reached MatchSelection"`).

### AC-2 — Each state transition in LaunchAndLoginAsync logs at Debug level (EP=0.0)
With a captured test logger and `ErrorProbability=0.0`, calling `LaunchAndLoginAsync` emits **exactly 2** `Debug`-level messages (one for `Launching`, one for `LoginScreen`). The `MatchSelection` arrival is an `Information`-level completion message (covered by AC-1), not a `Debug` message.

### AC-3 — Error injection logs at Warning level (EP=1.0)
With a captured test logger and `ErrorProbability=1.0`, calling `LaunchAndLoginAsync` emits **exactly 1** `Warning`-level message whose message contains `"Error before Launching transition"` (the first error checkpoint fires deterministically at EP=1.0; no `RngSeed` control is required). Additionally emits **exactly 1** `Information`-level message (the entry log fires before the first error checkpoint; no completion log is emitted).

### AC-4 — StopAsync logs Information entry and completion
With a captured test logger, after reaching `MatchSelection` state (via `LaunchAndLoginAsync` with `ErrorProbability=0.0` and zero delays), calling `StopAsync` emits **exactly 2** `Information`-level messages: one at entry (`"StopAsync starting from MatchSelection"`) and one at completion (`"StopAsync complete — reached NotRunning"`).

### AC-5 — LoadMatchAsync logs Information entry and completion, Debug state transitions (EP=0.0, from MatchSelection)
With a captured test logger, after reaching `MatchSelection` state, calling `LoadMatchAsync` with `ErrorProbability=0.0` emits **exactly 2** `Information`-level messages (entry + completion) and **exactly 2** `Debug`-level messages (`MatchSelectionSearching`, `MatchSelectionReady`).

### AC-6 — LoadMatchAsync ChangeMatch path logs Debug intermediate (EP=0.0, from MatchLoaded)
With a captured test logger, after reaching `MatchLoaded` state (via `LaunchAndLoginAsync` + `LoadMatchAsync`, both EP=0.0), calling `LoadMatchAsync` again with `ErrorProbability=0.0` emits **exactly 2** `Information`-level messages and **exactly 3** `Debug`-level messages (ChangeMatch→`MatchSelection` intermediate, `MatchSelectionSearching`, `MatchSelectionReady`).

### AC-7 — DI: UseMock=true resolves MockPcsProAutomationService
A `ServiceProvider` built with `PcsPro:UseMock=true` configuration resolves `IPcsProAutomationService` as an instance of `MockPcsProAutomationService`.

### AC-8 — DI: UseMock=false placeholder throws NotSupportedException
A `ServiceProvider` built with `PcsPro:UseMock=false` configuration resolves `IPcsProAutomationService` as a non-null instance that is **not** a `MockPcsProAutomationService`. Calling `LaunchAndLoginAsync(CancellationToken.None)` on that instance throws `NotSupportedException`.

### AC-9 — Full solution builds clean
`dotnet build` on the solution root exits 0 with 0 warnings and 0 errors after all changes.

### AC-10 — Existing tests unaffected
All pre-existing tests in `PcsRemote.Automation.Mock.Tests` and `PcsRemote.Core.Tests` continue to pass without modification.

---

## 5. Out of Scope

- Real `PcsProAutomationService` implementation (future HLPS)
- Serilog enrichers, output templates, or sink configuration changes
- Any change to the existing `PcsRemote.Web` Serilog bootstrap or rolling-file configuration
- `MockPcsProOptions` binding from configuration (binds section `PcsPro:Mock` but options validation is out of scope)
- Testing the placeholder type beyond DI resolution (AC-8 is behaviour-based via `NotSupportedException`; no further placeholder tests)

---

## 6. Files Affected

| File | Change |
|---|---|
| `src/PcsRemote.Automation.Mock/MockServiceCollectionExtensions.cs` | **New** — DI extension method + `NotSupportedPcsProAutomationService` placeholder |
| `src/PcsRemote.Automation.Mock/MockPcsProAutomationService.cs` | Logging audit — correct any level deviations; no structural changes |
| `src/PcsRemote.Automation.Mock/PcsRemote.Automation.Mock.csproj` | Add `PackageReference` for `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Options.ConfigurationExtensions` |
| `src/PcsRemote.Web/PcsRemote.Web.csproj` | Add `ProjectReference` to Mock |
| `src/PcsRemote.Web/Program.cs` | Call `AddPcsProAutomationService` |
| `src/PcsRemote.Web/appsettings.json` | Add `PcsPro` section skeleton |
| `src/PcsRemote.Web/appsettings.Development.json` | Set `PcsPro:UseMock=true` |
| `tests/PcsRemote.Automation.Mock.Tests/MockPcsProAutomationServiceTests.cs` | Add logging tests (AC-1–AC-6) |
| `tests/PcsRemote.Automation.Mock.Tests/MockServiceCollectionExtensionsTests.cs` | **New** — DI tests (AC-7–AC-8) |
| `tests/PcsRemote.Automation.Mock.Tests/PcsRemote.Automation.Mock.Tests.csproj` | Add `PackageReference` for `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, and `Microsoft.Extensions.Configuration.Memory` |
| `Directory.Packages.props` | Add `Microsoft.Extensions.DependencyInjection` v`8.0.2`, `Microsoft.Extensions.Options.ConfigurationExtensions` v`8.0.2`, `Microsoft.Extensions.Configuration` v`8.0.2`, `Microsoft.Extensions.Configuration.Memory` v`8.0.2` |

---

## 7. Test Strategy

### Captured Test Logger
A minimal test helper implementing `ILogger<MockPcsProAutomationService>` captures `(LogLevel, message)` pairs in a list. The helper is created per-test; the SUT is constructed with it instead of `NullLogger`. Tests then assert on the captured list.

The `message` string in each captured pair **must be the rendered output** — obtain it via `formatter(state, exception)` in the `ILogger.Log` implementation, not the raw format template. This is required for AC-3's substring assertion (`"Error before Launching transition"`) to match the structured log parameter value.

No new packages are required for the logger helper. It can be a private nested class or a top-level internal class within the test project.

### DI Tests (new file: `MockServiceCollectionExtensionsTests.cs`)
Tests create a `ServiceCollection`, add an in-memory `IConfiguration` (using `ConfigurationBuilder` with `AddInMemoryCollection(new Dictionary<string, string> { ... })`), call `AddPcsProAutomationService`, build the `ServiceProvider`, and resolve `IPcsProAutomationService`.

`MockPcsProAutomationService` has a two-parameter constructor (`IOptions<MockPcsProOptions>` + `ILogger<MockPcsProAutomationService>`). `IOptions<MockPcsProOptions>` is satisfied automatically by `services.Configure<T>()` (which calls `AddOptions()` internally). `ILogger<MockPcsProAutomationService>` has **no implicit DI fallback** and must be registered explicitly. Add the following to the `ServiceCollection` setup before building the `ServiceProvider`:

```csharp
services.AddSingleton<ILogger<MockPcsProAutomationService>>(
    NullLogger<MockPcsProAutomationService>.Instance);
```

`NullLogger<T>` is in `Microsoft.Extensions.Logging.Abstractions`, which is available transitively through the project reference to `PcsRemote.Automation.Mock` — no new package reference required.

Both `ServiceCollection` and `ConfigurationBuilder.AddInMemoryCollection` require explicit packages not available transitively in the test project:
- `Microsoft.Extensions.DependencyInjection 8.0.2` — add to CPM and reference from Mock.csproj (production) and Mock.Tests.csproj (tests).
- `Microsoft.Extensions.Configuration 8.0.2` — add to CPM and reference from Mock.Tests.csproj (provides `ConfigurationBuilder`).
- `Microsoft.Extensions.Configuration.Memory 8.0.2` — add to CPM and reference from Mock.Tests.csproj (provides `AddInMemoryCollection` extension).

### AC-1 through AC-6 (logging, in `MockPcsProAutomationServiceTests.cs`)

`ErrorProbability` is a **construction-time option**, not a call-site argument. Construct the SUT as:
```csharp
var sut = new MockPcsProAutomationService(
    Options.Create(new MockPcsProOptions { ErrorProbability = <ep> }),
    capturedLogger);
```
All delay fields in `MockPcsProOptions` default to `TimeSpan.Zero`. The EP value annotated in each AC heading is the value to pass at construction.

- **AC-1** (EP=0.0): after `LaunchAndLoginAsync()`, assert `capturedLogs.Count(l => l.Level == LogLevel.Information) == 2`.
- **AC-2** (EP=0.0): after `LaunchAndLoginAsync()`, assert `capturedLogs.Count(l => l.Level == LogLevel.Debug) == 2`.
- **AC-3** (EP=1.0): after `LaunchAndLoginAsync()`, assert exactly 1 `Warning` whose message contains `"Error before Launching transition"`. Additionally assert `capturedLogs.Count(l => l.Level == LogLevel.Information) == 1` (only the entry log fires before the first error checkpoint).
- **AC-4** (EP=0.0): reach `MatchSelection` via `LaunchAndLoginAsync()`, then **reset the captured list** (`capturedLogs.Clear()`), then call `StopAsync()`; assert `capturedLogs.Count(l => l.Level == LogLevel.Information) == 2`.
- **AC-5** (EP=0.0, from `MatchSelection`): reach `MatchSelection` via `LaunchAndLoginAsync()`, then **reset the captured list**, then call `LoadMatchAsync(new MatchInfo("test-match"))`; assert `capturedLogs.Count(l => l.Level == LogLevel.Information) == 2` and `capturedLogs.Count(l => l.Level == LogLevel.Debug) == 2`.
- **AC-6** (EP=0.0, from `MatchLoaded`): reach `MatchLoaded` via `LaunchAndLoginAsync()` + `LoadMatchAsync(new MatchInfo("test-match"))`, then **reset the captured list**, then call `LoadMatchAsync(new MatchInfo("test-match-2"))` again; assert `capturedLogs.Count(l => l.Level == LogLevel.Information) == 2` and `capturedLogs.Count(l => l.Level == LogLevel.Debug) == 3`.

### AC-7 and AC-8 (DI, in `MockServiceCollectionExtensionsTests.cs`)

- **AC-7**: resolve, assert `service is MockPcsProAutomationService`.
- **AC-8**: resolve, assert `service is not null`, assert `service is not MockPcsProAutomationService`, then call `await Assert.ThrowsExceptionAsync<NotSupportedException>(() => service.LaunchAndLoginAsync(CancellationToken.None))` to verify placeholder behaviour without requiring type visibility.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | Default (pr-review-agent), Claude Sonnet 4.6 (pr-review-agent) | NEEDS REVIEW — 2 CRITICAL, 3 HIGH, 4 MEDIUM, 4 LOW |
| R2 | 2026-04-11 | Default (pr-review-agent), Claude Sonnet 4.6 (pr-review-agent) | NEEDS REVIEW — 1 CRITICAL, 2 HIGH, 2 MEDIUM, 2 LOW |
| R3 | 2026-04-11 | Default (pr-review-agent), Claude Sonnet 4.6 (pr-review-agent) | NEEDS REVIEW — 1 HIGH, 1 MEDIUM, 2 LOW |
| R4 | 2026-04-11 | Default (pr-review-agent), Claude Sonnet 4.6 (pr-review-agent) | **APPROVED** — 0 blockers; 1 LOW (R1) + 1 MEDIUM + 2 LOW (R2) non-blocking polish applied in v0.5 |
