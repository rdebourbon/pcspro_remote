# SPEC-S-006: TrayHost Restructure — In-Process Web Hosting and STA Message Loop

| Field | Value |
|---|---|
| **Document** | SPEC-S-006-TrayHostRestructure.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-12 |
| **Step ID** | S-006 |
| **Governing IS** | IS-005-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/IS-005-S-006-tray-host-restructure` |

---

## 1. Context

`PcsRemote.TrayHost` is currently a standalone WinForms shell with an empty `Form1` — it has no awareness of the web application. `PcsRemote.Web` is the application entry point with `Program.cs` building and running the ASP.NET Core host. This separation means the WinForms tray cannot integrate with Kestrel, the DI container, or `IManualModeService`.

This step restructures `TrayHost` to become the combined process entry point: it builds and runs the ASP.NET Core `WebApplication`, while a dedicated STA background thread runs the WinForms message pump. `PcsRemote.Web` retains its own `Program.cs` so it continues to work as a standalone host for testing (Playwright `WebApplicationFactory` tests are unaffected).

---

## 2. Requirements

### R-1 — Service and Middleware Extraction from `PcsRemote.Web`

The DI service registrations and middleware pipeline in `PcsRemote.Web/Program.cs` must be extractable so they can be consumed by both `PcsRemote.Web` (standalone) and `PcsRemote.TrayHost` (hosted mode) without duplication.

A public static extension class is added to `PcsRemote.Web` that provides two methods. Both accept a `WebApplicationBuilder` (not raw `IServiceCollection`) as their parameter, enabling access to `builder.Services`, `builder.Configuration`, and `builder.Host` without restricting the call site:
- One method registers all web-layer services, including `AutoLaunchService`. `AutoLaunchService` is included in the shared registration regardless of which project is the entry point — both TrayHost and Web standalone auto-initiate the automation service on startup.
- A second method applies the middleware pipeline and endpoint routing onto a `WebApplication`.

`PcsRemote.Web/Program.cs` is updated to delegate to these extension methods while retaining the Serilog bootstrap/replace pattern, the try/catch/finally shutdown boundary, and the `public partial class Program {}` marker for `WebApplicationFactory`. No test references to `Program` may break.

### R-2 — TrayHost Project References and Build Configuration

`PcsRemote.TrayHost.csproj` gains:
- A `<ProjectReference>` to `PcsRemote.Web.csproj`.
- A `<FrameworkReference Include="Microsoft.AspNetCore.App" />` so the ASP.NET Core hosting APIs are available at compile time.
- `<OutputType>WinExe</OutputType>` and `net8.0-windows` remain unchanged.

No new NuGet packages beyond those already present in `PcsRemote.Web` are required.

### R-3 — TrayHost `Program.cs` Restructure

`PcsRemote.TrayHost/Program.cs` becomes the combined process entry point:
- The `[STAThread]` attribute is removed from `Main` — `app.Run()` drives the generic host's async machinery, which requires an MTA-compatible synchronisation context; an STA synchronisation context on the main thread can cause deadlocks with Task continuations inside the host infrastructure.
- The program builds a `WebApplication` using the same extension methods introduced in R-1.
- It registers a `WinFormsHostedService` (R-4) with the DI container before building the app.
- The Serilog two-stage bootstrap pattern (bootstrap logger → replace) is applied, consistent with `PcsRemote.Web/Program.cs`.
- The `try / catch(Exception) / finally { Log.CloseAndFlush() }` shutdown boundary is applied.
- `app.Run()` blocks the main thread until shutdown.
- The program must ensure TrayHost's content root and configuration sources (application URLs, Kestrel settings) are equivalent to `PcsRemote.Web` standalone. Practically this means TrayHost includes or references the same `appsettings.json` configuration or sets the content root explicitly so Kestrel binds to the same configured URLs.
- `Form1.cs` and `Form1.Designer.cs` are deleted; they are replaced by the new types in R-4 and R-5.

### R-4 — `WinFormsHostedService`

A new `IHostedService` implementation, `WinFormsHostedService`, is added to `PcsRemote.TrayHost`:
- `StartAsync`: creates a `Thread`, sets its apartment state to STA, marks it as a background thread (so it does not prevent process exit), and starts the thread. On the STA thread, the startup sequence is: (1) call `ApplicationConfiguration.Initialize()` to configure DPI-awareness, visual styles, and default font; (2) construct the `TrayApplicationContext` instance — this creates the `NotifyIcon` and establishes the Win32 message queue for the thread; (3) set the readiness signal (e.g., `ManualResetEventSlim`) to indicate the queue exists and the pump is about to start; (4) call `Application.Run(context)`. `StartAsync` returns as soon as the thread is started — it does not wait for `Application.Run()` to complete.
- `StopAsync`: waits (with a bounded timeout) for the readiness signal established in `StartAsync`, then calls `Application.Exit()` on the STA thread so the message pump processes the quit message. This ensures the exit request is never silently discarded in the window between thread start and message pump entry. If `StartAsync` was never called or the readiness signal times out, the method returns without throwing.
- When the WinForms message loop exits (the STA thread terminates), `WinFormsHostedService` calls `IHostApplicationLifetime.StopApplication()` so the ASP.NET Core host also terminates. This ensures either side can initiate a full shutdown.

### R-5 — `TrayApplicationContext`

A new `ApplicationContext` subclass, `TrayApplicationContext`, is added to `PcsRemote.TrayHost`:
- Creates a `NotifyIcon` in its constructor and assigns it a visible icon. For S-006 a programmatically-generated icon (e.g., a solid-colour 16×16 bitmap) or a bundled `.ico` resource is acceptable — no final icon asset is required at this step. S-007 will replace it with mode-specific assets.
- Sets the `NotifyIcon.Visible = true` and a tooltip text (`"PCS Remote"`).
- Overrides `Dispose(bool)` to hide (`NotifyIcon.Visible = false`) and dispose the `NotifyIcon` before calling the base.
- The constructor accepts no arguments from the service layer (S-007 will inject dependencies via a factory or direct field injection). For S-006, the context is self-contained.

### R-6 — Web Standalone Compatibility

After the R-1 extraction:
- Running `PcsRemote.Web` directly (via `dotnet run` in the Web project) continues to start the full Blazor application on the configured URL.
- The `public partial class Program {}` marker in `PcsRemote.Web/Program.cs` is preserved, ensuring `WebApplicationFactory<Program>` in `PcsRemote.Web.Tests` and `PcsRemote.E2E.Tests` continues to resolve correctly.
- No changes to any test project `.csproj` files are required.

---

## 3. Acceptance Criteria

| ID | Criterion | Verification |
|---|---|---|
| AC-1 | `dotnet build` for the full solution succeeds with zero new build errors or warnings. | Automated: `dotnet build` exit code 0 |
| AC-2 | Running `PcsRemote.TrayHost.exe` causes a tray icon to appear in the Windows notification area. | Manual: launch exe, observe tray |
| AC-3 | After starting TrayHost.exe, navigating to the configured URL (`https://localhost:7071` or `http://localhost:5070`) in a browser renders the PCS Remote Blazor application. | Manual: browser navigation |
| AC-4 | All pre-existing unit and bUnit tests continue to pass unchanged; new TC-1 through TC-6 tests are written and pass (TC-4 may be `[Ignore]` in headless CI). | Automated: `dotnet test` |
| AC-5 | `WebApplicationFactory<Program>` in `PcsRemote.Web.Tests` and `PcsRemote.E2E.Tests` continues to resolve `Program` from `PcsRemote.Web` and all Playwright/bUnit tests pass. | Automated: `dotnet test` |
| AC-6 | Shutdown is bidirectional: (a) WinForms loop exit → `StopApplication()` called; (b) `StopAsync` called on a running service → STA thread terminates within a bounded timeout. Both directions verified by unit tests (TC-3 for direction a; TC-6 for direction b). | Automated: unit tests TC-3 + TC-6 |
| AC-7 | `TrayApplicationContext.Dispose` hides and disposes the `NotifyIcon`. | Code review + unit test (TC-4; may be `[Ignore]` in headless CI — see §4) |
| AC-8 | `PcsRemote.Web` can still be started standalone (`dotnet run` on Web project) and serves the application on the configured URL. | Manual/CI: build succeeds; `WebApplicationFactory` tests pass |

---

## 4. Test Cases

### TC-1 — `WinFormsHostedService`: `StartAsync` starts an STA thread

Build a `WinFormsHostedService` with a mock `IHostApplicationLifetime`. Call `StartAsync`. Assert: the hosted service's tracking indicates an STA thread was started (verify via `Thread.GetApartmentState()` on the created thread or by asserting the service completed `StartAsync` without error). Because `Application.Run()` blocks, the thread must be marked as a background thread so the test can complete without hanging.

*Scope note*: This test verifies the STA thread is created and started with the correct apartment state. It does not assert that a visible tray icon appeared — that is manual (AC-2).

### TC-2 — `WinFormsHostedService`: `StopAsync` exits cleanly when the message loop has not started

Call `StopAsync` on a `WinFormsHostedService` that has never had `StartAsync` called. Assert: no exception is thrown and the method returns a completed task.

### TC-3 — `WinFormsHostedService`: STA thread exit calls `StopApplication`

Provide a `WinFormsHostedService` with a mock `IHostApplicationLifetime`. Use a test-double `ApplicationContextFactory` that immediately returns without blocking (simulating a message loop that exits immediately). Call `StartAsync`. Wait for the thread to terminate. Assert: `IHostApplicationLifetime.StopApplication()` was called.

*Design note*: To make `WinFormsHostedService` testable, the application context creation should be injectable via a factory delegate or interface so the production code creates `TrayApplicationContext` while tests can substitute a no-op context. The exact injection mechanism is a delivery decision.

### TC-4 — `TrayApplicationContext`: `Dispose` hides and disposes the `NotifyIcon`

The `NotifyIcon` cannot easily be constructed in a headless test environment. This test is accepted as a **build-only verification** (the test project must compile with the new type). If a `NotifyIcon` can be constructed without throwing in the test runner (which may lack a Windows message loop), assert `Visible = false` after `Dispose`. Otherwise, the test is a stub with a `[Ignore]` marker documenting the environment constraint.

### TC-5 — Web extension methods: services and middleware registration

Verify that the Web extension method registers the expected key services (e.g., `IManualModeService`, `IOperationCoordinatorService`, `IScoreboardService`) onto an `IServiceCollection`. This is a lightweight unit test using `ServiceCollection` directly — no `WebApplication` required.

### TC-6 — `WinFormsHostedService`: `StopAsync` on running service causes STA thread to terminate

Start `WinFormsHostedService` with a mock `IHostApplicationLifetime` and an injectable factory that enters a real or simulated message loop. Call `StartAsync`, wait for the readiness signal to be set (confirming the pump is live), then call `StopAsync`. Assert: the STA thread terminates within a defined timeout (e.g., 5 seconds). This directly validates AC-6 direction (b): host shutdown → WinForms exit.

*Design note*: The injectable factory approach established for TC-3 applies equally here. The factory provides a lightweight `ApplicationContext` substitute that can exit cleanly when `Application.Exit()` is called.

---

## 5. Out of Scope

The following are explicitly deferred to later steps:

- **Context menu wiring** (S-007): the `TrayApplicationContext` created in this step has no context menu. The right-click menu items (Manual Mode toggle, Open Browser, Exit) are S-007 deliverables.
- **Mode-specific tray icons** (S-007): S-006 uses a placeholder icon. The final distinct icons per mode are S-007 deliverables.
- **Graceful Exit from tray** (S-007): `StopAsync` on `IPcsProAutomationService` as part of tray exit is S-007 scope.

---

## 6. Risk Register

| Risk | Likelihood | Mitigation |
|---|---|---|
| STA thread vs. Kestrel thread-pool contention | Low | Kestrel operates on thread-pool (MTA) threads. The STA thread runs only the WinForms message pump. No cross-thread WinForms API calls occur in S-006. |
| `Application.Exit()` race (pump not yet started) | Medium | `StartAsync` constructs `TrayApplicationContext` (establishing the Win32 message queue) before setting the readiness signal. `StopAsync` waits on the signal before calling `Application.Exit()`, ensuring the queue exists when the quit message is posted. Mitigated by R-4 startup ordering requirement. |
| `StopAsync` returns before STA thread cleanup completes | Low | The STA thread is a background thread; the CLR will abort it if the process exits before cleanup. Visible symptom: ghost tray icon until user hovers. Mitigated by implementation Thread.Join with bounded timeout in `StopAsync`. |
| `Application.Run()` blocking test execution | Medium | `WinFormsHostedService` uses an injectable factory (TC-3, TC-6) to avoid spawning real WinForms loops in unit tests. |
| `WebApplicationFactory<Program>` breaks | Low | The `public partial class Program {}` marker in `PcsRemote.Web/Program.cs` is preserved (R-6, AC-5). |
| Duplicate Serilog bootstrap in TrayHost and Web | Low | Both programs call `Log.CloseAndFlush()` only if they are the process entry point. Separate binaries sharing the same configuration pattern. |
| TrayHost content-root / URL mismatch with Web standalone | Medium | R-3 requires explicit configuration parity (same `appsettings.json` content root or equivalent). AC-3 manual verification gates this at delivery. |

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R3 | 2026-04-12 | GPT-5.4 | APPROVED — both R2 residuals verified |
