# SPEC-S-001: Project Scaffold, Configuration Models, and DI Registration

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-Scaffold.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.3 |
| **Date** | 2026-04-12 |
| **IS Step** | IS-006 S-001 |
| **Governing IS** | IS-006-FlaUI-Integration.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-006-FlaUI-Integration.md v0.2 (APPROVED) |

---

## 1. Purpose

This step creates the `PcsRemote.Automation` project infrastructure required for all subsequent IS-006 steps. It produces a compiling skeleton, correct configuration models, a companion test stub, and the DI registration plumbing — everything except working automation logic. No interaction with PCS Pro or FlaUI occurs at this step.

---

## 2. Branch

`feature/IS-006-S-001-scaffold`

---

## 3. Scope

### 3.1 What changes

**FlaUI package declaration (`Directory.Packages.props`)**  
Add centrally-managed versions for `FlaUI.Core` and `FlaUI.UIA3`. Use the latest stable versions compatible with .NET 8 and Windows UI Automation v3.

**`PcsRemote.Automation` project (`src/PcsRemote.Automation/`)**  
- Add `FlaUI.Core` and `FlaUI.UIA3` as `<PackageReference>` entries (version from central packages).
- Add `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options.ConfigurationExtensions`, and `Microsoft.Extensions.Logging.Abstractions` as `<PackageReference>` entries (versions already declared centrally).
- Create `PcsProOptions` — a configuration POCO capturing the three PCS Pro launch settings: executable path, working directory, and password. Bound to the `PcsPro:` configuration section. Note: the `PcsPro:` section already contains `AutoLaunch` and `UseMock` keys; the options binder silently ignores keys not declared in the POCO — this is correct behaviour.
- Create `ScoreboardOptions` — a configuration POCO capturing JPEG quality for scoreboard capture. Bound to the `Scoreboard:` configuration section.
- Create `PcsProAutomationService` — a class implementing all twelve members of `IPcsProAutomationService`. The three non-method members have safe scaffold implementations: `CurrentState` returns `PcsProState.NotRunning`, `LastErrorReason` returns `null`, and `StateChanged` has empty add/remove accessors. All nine `Task`-returning method bodies throw `NotImplementedException` with a message identifying the unimplemented method. The constructor accepts `IOptions<PcsProOptions>`, `IOptions<ScoreboardOptions>`, and `ILogger<PcsProAutomationService>`. The class is `internal` to the project.
- Create `AutomationServiceCollectionExtensions` — a static class with a single extension method named **`AddPcsProAutomation`** that registers `PcsProAutomationService` as the singleton implementation of `IPcsProAutomationService`, and wires both options classes from configuration. The method is named `AddPcsProAutomation` (not `AddPcsProAutomationService`) to avoid a CS0121 ambiguous-invocation compile error: `PcsRemote.Automation.Mock` already exports an extension method named `AddPcsProAutomationService`, and the composition root in `PcsRemote.Web` must import both namespaces to perform the branching.
- Add an `InternalsVisibleTo` assembly attribute targeting `PcsRemote.Automation.Tests`, consistent with the pattern in `PcsRemote.Web.csproj`.

**`PcsRemote.Web` project (`src/PcsRemote.Web/`)**  
- Add `<ProjectReference>` to `PcsRemote.Automation`.
- The composition root (`Program.cs` or a new composite registration helper in `PcsRemote.Web`) is updated to branch: when `PcsPro:UseMock = true`, call `services.AddPcsProAutomationService(config)` from `PcsRemote.Automation.Mock`; when `PcsPro:UseMock = false`, call `services.AddPcsProAutomation(config)` from `PcsRemote.Automation`. The `MockServiceCollectionExtensions.AddPcsProAutomationService()` method in `PcsRemote.Automation.Mock` is updated to handle only the `UseMock = true` path — the `else` branch registering `NotSupportedPcsProAutomationService` is removed. The existing `if (UseMock)` guard inside `MockServiceCollectionExtensions` may also be removed since the composition root now owns the branching; if retained, it must be converted to a defensive `InvalidOperationException` throw (not a silent no-op) so that accidental miscalls produce a clear diagnostic. `NotSupportedPcsProAutomationService` and its associated constant are **deleted** at this step; the class is no longer needed.
- Verify that `PcsRemote.Automation.Mock.Tests` contains no tests asserting the old `UseMock = false` / `NotSupportedPcsProAutomationService` behaviour. If any such tests exist, update them to reflect the new registration path.

**`appsettings.json` (in `PcsRemote.Web`)**  
Add placeholder entries for the new configuration keys so the configuration structure is immediately visible to developers: `PcsPro:ExecutablePath`, `PcsPro:WorkingDirectory`, `PcsPro:Password`, and `Scoreboard:JpegQuality`. Values are empty strings / defaults at this stage. Additionally, change `PcsPro:AutoLaunch` from `true` to `false` — the skeleton throws `NotImplementedException` from all nine methods, and AutoLaunchService calls `LaunchAndLoginAsync()` on startup; the safe default is `AutoLaunch: false` while the skeleton is active. The garage PC deployment will supply `AutoLaunch: true` via its deployment configuration.

**Security note:** `PcsPro:Password` in `appsettings.json` is schema documentation only — the placeholder value must remain empty. The actual password must be supplied via a gitignored `appsettings.Development.json`, environment variable, or .NET User Secrets. Committing a non-empty password value to source control is prohibited.

**`PcsRemote.Automation.Tests` project (`tests/PcsRemote.Automation.Tests/`)**  
Create a stub test project targeting `net8.0-windows` (matching `PcsRemote.Automation`'s target framework — using `net8.0` would create a cross-TFM reference with Windows-only FlaUI transitive dependencies, violating the zero-warnings build gate). Add `MSTest` and `FluentAssertions` package references. The project is added to the solution. The stub contains a single placeholder test class with no test methods — sufficient to satisfy `dotnet test` with zero failures. Add `<ProjectReference>` to `PcsRemote.Automation`.

**`PCS_Remote.slnx`**  
Add `PcsRemote.Automation.Tests` to the `/tests/` folder.

### 3.2 What does NOT change

- `IPcsProAutomationService` — the interface is complete and must not be modified.
- `MockPcsProAutomationService` — the mock service is out of scope for this step.
- `PcsProStateMachine`, `PcsProState`, `PcsProTrigger` — Core types are out of scope.
- Playwright E2E tests — the E2E harness is not affected.
- Any FlaUI automation logic — that begins at S-002.

---

## 4. Architectural Constraints

- `PcsRemote.Automation` must depend **only** on `PcsRemote.Core`. No reference to `PcsRemote.Web`, `PcsRemote.TrayHost`, `PcsRemote.Automation.Mock`, or any test project is permitted.
- `PcsRemote.Automation.Mock` must **not** gain a reference to `PcsRemote.Automation`. The branching logic (mock vs real) lives at the composition root in `PcsRemote.Web`.
- `PcsRemote.Automation.Tests` targets `net8.0-windows` to match `PcsRemote.Automation` and avoid cross-TFM compatibility warnings (which are treated as errors by the global `TreatWarningsAsErrors` setting). Parsing tests run on a Windows CI agent without PCS Pro — the CI portability concern is satisfied by the tests not invoking FlaUI, not by a different TFM.

---

## 5. Test Requirements

### 5.1 Pre-existing tests (regression gate)

All 228 currently passing tests must continue to pass after this step. The `dotnet test` run must complete with zero failures and zero errors. This is the primary quality gate for S-001 since there is no new logic to test.

### 5.2 Stub test class

`PcsRemote.Automation.Tests` must contain at least one compilable test class with the `[TestClass]` attribute. No test methods are required at this stage. The class should be named to indicate it is a placeholder (e.g., `PlaceholderTests`).

### 5.3 Build gate

`dotnet build` across the entire solution must succeed with zero errors and zero warnings. This includes the new `PcsRemote.Automation.Tests` project.

---

## 6. Configuration Requirements

`appsettings.json` (in `PcsRemote.Web`) must be updated to include the new sections:

```
PcsPro:
  ExecutablePath: ""         (path to cricket.exe — populated at deployment)
  WorkingDirectory: ""       (working directory for cricket.exe)
  Password: ""               (PCS Pro login password — populated at deployment)

Scoreboard:
  JpegQuality: 85            (default JPEG quality; 85 is a reasonable default)
```

The values are placeholders. The JPEG quality default of 85 is a reasonable starting point; the delivered code must not hardcode this value — it must always be read from configuration.

---

## 7. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` succeeds with zero errors and zero warnings across the entire solution. |
| AC-2 | All 228 pre-existing automated tests pass (`dotnet test` with zero failures). |
| AC-3 | `PcsRemote.Automation.Tests` project exists in the solution, compiles, and `dotnet test` reports zero failures (the stub class compiles and runs). |
| AC-4 | `PcsProAutomationService` implements all twelve members of `IPcsProAutomationService`: the nine async methods each throw `NotImplementedException`; `CurrentState` returns `PcsProState.NotRunning`; `LastErrorReason` returns `null`; `StateChanged` has empty add/remove accessors. |
| AC-5 | Starting the application with `PcsPro:UseMock = false` and `PcsPro:AutoLaunch = false` does not throw any unhandled exception on startup. |
| AC-6 | Starting the application with `PcsPro:UseMock = true` continues to use the mock service — mock path is unaffected. |
| AC-7 | `PcsProOptions` is bound to `PcsPro:` section; `ScoreboardOptions` is bound to `Scoreboard:` section — validated by injecting both into `PcsProAutomationService` and confirming the constructor receives them without exception at startup. |
| AC-8 | `PcsRemote.Automation` project dependency graph: references `PcsRemote.Core` only. No reference to Web, TrayHost, Mock, or any test project. |
| AC-9 | `PcsRemote.Automation.Tests` targets `net8.0-windows`. |
| AC-10 | `NotSupportedPcsProAutomationService` no longer exists in the codebase. |

---

## 8. Commit Strategy

Small and focused:
1. Package declarations (`Directory.Packages.props` + `PcsRemote.Automation.csproj` NuGet references)
2. Configuration models (`PcsProOptions`, `ScoreboardOptions`) and `appsettings.json` additions
3. `PcsProAutomationService` skeleton + `AutomationServiceCollectionExtensions`
4. DI wiring update in `PcsRemote.Web` (project reference + composition root update)
5. `PcsRemote.Automation.Tests` stub project + solution file update

The number of commits is a delivery decision — the above is a guide, not a prescription.

---

## 9. Documentation Updates

No external documentation updates are required at this step. The implementation notes (AutomationId strings, config keys) that belong to subsequent steps are out of scope here.

---

## 10. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | Claude Opus 4.6, GPT-5.4, Claude Sonnet 4.6 | NEEDS REVIEW — 1 CRITICAL, 2 HIGH, 4 MEDIUM, 4 LOW |
| R1 fixes | 2026-04-12 | Orchestrator | 9 findings accepted (A–E, G, I, J, L), 4 deferred (F, H, K, M) |
| R2 | 2026-04-12 | Claude Opus 4.6, GPT-5.4 | NEEDS REVIEW — Opus: N-1 BLOCKING (extension method naming collision), N-2 advisory; GPT: IS-006 governing-doc inconsistency (net8.0/nine-methods already fixed in IS-006 v0.4) |
| R2 fixes | 2026-04-12 | Orchestrator | N-1: renamed extension method to `AddPcsProAutomation`; N-2: added defensive guard note; IS-006 bumped to v0.4 |
