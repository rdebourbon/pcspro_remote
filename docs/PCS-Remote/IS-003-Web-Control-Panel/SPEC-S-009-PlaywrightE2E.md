# SPEC-S-009: Playwright E2E Project and Smoke Test

| Field | Value |
|---|---|
| **Document** | SPEC-S-009-PlaywrightE2E.md |
| **Status** | IN REVIEW |
| **Version** | 0.5 |
| **Date** | 2026-04-13 |
| **Step ID** | S-009 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Success Criteria** | W-SC-3, W-SC-9, W-SC-10 |
| **Dependencies** | S-001 (app runs + serves HTTP); S-002 (app shell present); S-003 (hub + connection tracking); S-004 (status indicator in DOM); S-005 (connected user count component) |

---

## 1. Objective

Upgrade the existing `PcsRemote.E2E.Tests` project (scaffolded but empty) with Playwright packages and a real HTTP test host. Implement three tests:

1. **Smoke test** (W-SC-10): The root URL returns HTTP 200 and the status indicator element is present in the DOM.
2. **W-SC-3 test**: Two browser contexts open against the same server; the connected user count component updates as contexts connect and disconnect.
3. **W-SC-9 test**: Two browser contexts are simultaneously connected; when a state change is triggered in the test host's automation service, both contexts receive and display the updated state.

---

## 2. Background

`PcsRemote.E2E.Tests` was scaffolded during HLPS-001 with only MSTest and FluentAssertions. No tests currently exist in it. The project must now be wired to a live HTTP endpoint so Playwright can drive real browser interactions.

For W-SC-3 and W-SC-9, the tests must exercise the full stack:
- Blazor Server renders components in the browser
- The connected user count and state indicator components update reactively via server-side event subscriptions

Because `MockPcsProAutomationService` uses configurable delays and error probabilities, E2E tests must configure it with zero delays and zero error probability to eliminate timing dependencies.

**Architectural note — connection counting:** `PcsProHub.OnConnectedAsync` currently calls `IConnectionTracker.Increment()`, but browsers do not connect to `/hubs/pcspro` (no browser-side JavaScript SignalR client exists). This means `ConnectionTracker` is never incremented by page loads, making W-SC-3 impossible without an architectural fix. This step corrects that by introducing `PcsProCircuitHandler`, a Blazor `CircuitHandler` that increments/decrements `IConnectionTracker` when Blazor circuits open and close (i.e., when browser tabs connect and disconnect). `IConnectionTracker` is removed from `PcsProHub` as part of this change.

---

## 3. Design

### 3.1 Test host strategy

Use `WebApplicationFactory<Program>` (from `Microsoft.AspNetCore.Mvc.Testing`) configured to start a **real Kestrel HTTP listener** on a random available port (not the default in-memory `TestServer`, which has no TCP socket and cannot be reached by Playwright).

**Single-host pattern** (one DI container, shared by Playwright and service resolution):

Override `CreateHost(IHostBuilder builder)` in the `PcsProWebApplicationFactory` subclass:
1. Call `builder.ConfigureWebHost(wb => wb.UseKestrel().UseUrls("http://127.0.0.1:0"))` — configures Kestrel on the single host.
2. Call `var host = base.CreateHost(builder)` — builds the host and returns it; WAF owns the lifecycle; do NOT call `host.Start()` here.
3. Register `host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted` callback that reads `host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()` and calls `_serverAddressTcs.SetResult(addr)` on a `TaskCompletionSource<string>` stored on the factory subclass.
4. Return `host`.

> **Why this order matters:** `IHostApplicationLifetime` is a DI service that only exists in `host.Services` after `base.CreateHost(builder)` has built the container. Registering the callback in step 3 (after step 2) gives the implementer a valid `host` reference to resolve services from. The callback fires later — when WAF starts the host.

In `[ClassInitialize]`, after instantiating the factory, trigger host startup by calling `factory.CreateClient()` (which forces WAF to call `EnsureServer()` and start the host). Then `await` the `TaskCompletionSource<string>` to obtain `ServerAddress`. The `HttpClient` returned by `CreateClient()` may be discarded. Because there is only one host and one DI container, `factory.Services` and the live Kestrel app share the same singleton instances.

The app does not use `app.UseHttpsRedirection()`, so all test navigation is plain HTTP with no redirect concerns.

**Configuration overrides** applied inside `PcsProWebApplicationFactory` by overriding `protected override void ConfigureWebHost(IWebHostBuilder builder)` in the subclass (NOT via the public `WithWebHostBuilder()` method — that returns a new `WebApplicationFactory<Program>` base instance, losing access to `ServerAddressTask`):

IConfiguration key-value overrides (via `UseSetting`):
- `PcsPro:UseMock = true` — use the mock automation service
- `PcsPro:AutoLaunch = false` — suppress auto-launch so tests control when state changes occur
- `PcsPro:Mock:LaunchDelay = 0`, `PcsPro:Mock:LoginDetectedDelay = 0`, `PcsPro:Mock:CredentialsEnteredDelay = 0` — zero delays so state transitions are instantaneous in tests
- `PcsPro:Mock:ErrorProbability = 0` — no random errors

DI options override (via `ConfigureServices`):
- `services.Configure<CircuitOptions>(o => o.DisconnectedCircuitRetentionPeriod = TimeSpan.FromSeconds(1))` — ensures `OnCircuitClosedAsync` fires within ~1 second of connection loss (default is 3 minutes; `CircuitOptions` is not IConfiguration-bindable and must be set through the DI options pipeline), making the W-SC-3 disconnect assertion reliably complete within the 10-second wait timeout

### 3.2 Playwright setup

Playwright browsers must be installed before tests can run. The `.csproj` must include a post-build MSBuild `Exec` target that runs `playwright.ps1 install chromium` after the output is produced:

```xml
<Target Name="InstallPlaywrightBrowsers" AfterTargets="Build">
  <Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -File &quot;$(TargetDir)playwright.ps1&quot; install chromium" />
</Target>
```

Uses `powershell` (Windows PowerShell 5.1, guaranteed on all Windows machines) rather than `pwsh` (PowerShell 7, optional install). The `-NoProfile` and `-ExecutionPolicy Bypass` flags ensure the script runs correctly regardless of the machine's configured execution policy (which is `Restricted` by default on Windows client SKUs). The `-File` flag is the canonical way to invoke a `.ps1` file with parameters. `$(TargetDir)` is used rather than `$(OutputPath)` to avoid relative-path ambiguity. The path is quoted to handle directories containing spaces. Playwright's install command is idempotent — it skips the download if the browser is already installed. Tests use **headless Chromium** by default.

Each test method creates its own `IBrowserContext` (and `IPage` within that context) and disposes them after the test. The `IBrowser` instance is shared across the test class.

### 3.3 W-SC-9 state trigger

Because `WebApplicationFactory` provides access to the DI container, the W-SC-9 test can resolve `IPcsProAutomationService` from `factory.Services` and, since `UseMock=true`, cast it to `MockPcsProAutomationService` (by referencing the `PcsRemote.Automation.Mock` project). `MockPcsProAutomationService` is registered as `AddSingleton`, so resolving from the root `factory.Services` scope reaches the same instance that Blazor components subscribe to. After both pages have connected, the test calls `LaunchAndLoginAsync(CancellationToken.None)` on the resolved service to trigger state transitions, then asserts that both pages reflect the final state.

With zero-delay mock configuration, `LaunchAndLoginAsync` fires all three state transitions synchronously before returning (`Launching → LoginScreen → MatchSelection`). Each transition fires `StateChanged`, which `PcsProStatusIndicator` handles via `async void OnStateChanged` → `InvokeAsync(StateHasChanged)`. The `InvokeAsync` dispatch is asynchronous — the DOM update is queued on the Blazor circuit dispatcher and has NOT completed by the time `LaunchAndLoginAsync` returns. AC-3 must therefore use `WaitForFunctionAsync` to await the final text, not a direct text assertion.

### 3.4 Circuit-based connection counting

The current `PcsProHub.OnConnectedAsync` calls `IConnectionTracker.Increment()`, but no browser-side JavaScript connects to `/hubs/pcspro`. Blazor Server circuits use the `/_blazor` endpoint, not `PcsProHub`. This step fixes the counting by:

1. **Adding `PcsProCircuitHandler`** — a `CircuitHandler` subclass that calls `IConnectionTracker.Increment()` in `OnCircuitOpenedAsync` and `IConnectionTracker.Decrement()` in `OnCircuitClosedAsync`. This fires once per browser tab/session, making each Playwright `IBrowserContext` page navigation count as one connected user.
2. **Registering the handler** — `builder.Services.AddCircuitHandler<PcsProCircuitHandler>()` in `Program.cs`.
3. **Removing counting from `PcsProHub`** — `IConnectionTracker` is removed from `PcsProHub`'s constructor and calls. `PcsProHub` retains its `IPcsProAutomationService` injection and the `OnConnectedAsync` late-joiner state push (for future JavaScript clients).
4. **Updating `PcsProHubTests`** — the four test methods that verify `Increment`/`Decrement` calls on `PcsProHub` must be deleted; a new `PcsProCircuitHandlerTests.cs` adds equivalent coverage for the handler.

> **Unit test implementation note — `Circuit` constructor:** `Microsoft.AspNetCore.Components.Server.Circuit` is a `sealed` class with an `internal` constructor. It cannot be instantiated from an external test assembly via `new Circuit(...)` or mocked via Moq/NSubstitute. Since `PcsProCircuitHandler` does not access the `Circuit` parameter in either `OnCircuitOpenedAsync` or `OnCircuitClosedAsync` (it only calls `IConnectionTracker.Increment()`/`Decrement()`), tests MUST pass `null!` for the `circuit` argument — e.g. `await handler.OnCircuitOpenedAsync(null!, CancellationToken.None)`. A comment in the test file should document this as an intentional consequence of Blazor's internal API surface.

> **Production retention note:** `OnCircuitClosedAsync` fires only after `DisconnectedCircuitRetentionPeriod` elapses (default 3 minutes in production). For this application (a local single-PC remote controller), a brief lag in count accuracy after a tab close is acceptable. If tighter real-time accuracy is required, reduce `DisconnectedCircuitRetentionPeriod` in `appsettings.json` (e.g., `"Blazor": { "DisconnectedCircuitRetentionPeriod": "00:00:30" }` via options binding, or directly in `Program.cs`). This is a production configuration concern outside the scope of this spec.

### 3.5 Project changes

The `PcsRemote.E2E.Tests` project must:
- Change target framework from `net8.0` to `net8.0-windows` (required to reference `PcsRemote.Web`, which targets `net8.0-windows` because it is a Windows-only automation controller; this makes the E2E project Windows-only — `dotnet test` at solution level will fail on Linux/macOS for this project)
- Add project references to `PcsRemote.Web` and `PcsRemote.Automation.Mock`
- Add package references: `Microsoft.Playwright`, `Microsoft.AspNetCore.Mvc.Testing` (`Microsoft.AspNetCore.Mvc.Testing` is already in `Directory.Packages.props`; only `Microsoft.Playwright` is a new CPM entry)
- Add the post-build browser install target described in §3.2

---

## 4. Requirements

| ID | Requirement |
|---|---|
| **R-2** | The smoke test MUST verify HTTP 200 at the root URL AND the presence of the `.pcs-status-indicator` CSS class in the rendered DOM. |
| **R-3** | The W-SC-3 test MUST use two separate Playwright browser contexts connecting to the same server. The `.connected-user-count` element in Context 1 must update to reflect 2 users when Context 2 opens, and back to 1 user when Context 2 closes. Each Playwright page navigating to the root URL (and establishing a Blazor circuit) constitutes one connected user (via `PcsProCircuitHandler`). |
| **R-4** | The W-SC-9 test MUST verify that a state change triggered after both contexts are connected is reflected in BOTH contexts' `.pcs-status-indicator` elements showing `"Loading matches…"`. |
| **R-5** | All Playwright waits MUST use `WaitForSelectorAsync`, `WaitForFunctionAsync`, or `Locator.WaitForAsync` — NOT `Task.Delay` fixed sleeps. |
| **R-6** | The Playwright browser must be launched in headless mode. |
| **R-7** | All solution tests (unit + E2E) MUST pass after this step: `dotnet test` at solution level (on Windows). |

---

## 5. Acceptance Criteria

### AC-1 — Smoke test passes

Given: The test host is started with `UseMock=true`, `AutoLaunch=false`.  
When: Playwright navigates to the root URL.  
Then:
- The HTTP response status is 200.
- The DOM contains an element with CSS class `pcs-status-indicator`.
- The element's text content is `"PCS Pro not running"` (the `NotRunning` state label).

### AC-2 — W-SC-3: Connected user count updates with two contexts

Given: The test host is started.  
When:
1. Context 1's page navigates to the root URL.
2. Context 1's page waits until `.connected-user-count` shows `"1 user online"` — this confirms the Blazor circuit has opened and `PcsProCircuitHandler.OnCircuitOpenedAsync` has fired.

Then: Context 1's `.connected-user-count` shows `"1 user online"` (asserted as part of the readiness wait above).

When:
3. Context 2's page navigates to the root URL.
4. Context 2's page waits until its `.connected-user-count` shows `"2 users online"`.

Then: Context 1's `.connected-user-count` eventually shows `"2 users online"`.

When:
5. `await page2.CloseAsync()` is called (closes the tab; triggers the circuit disconnect timer — do NOT close `context2` here as it must remain open for orderly disposal in test cleanup).

Then: Context 1's `.connected-user-count` eventually shows `"1 user online"`.

> **Note:** All "eventually" assertions use `WaitForFunctionAsync` or `Locator.WaitForAsync` with a **10-second** timeout — not fixed sleeps.

### AC-3 — W-SC-9: State change broadcast to all connected contexts

Given: The test host is started with `AutoLaunch=false` and zero-delay mock.  
When:
1. Context 1's page navigates to the root URL.
2. Context 2's page navigates to the root URL.
3. Both pages wait until `.pcs-status-indicator` is visible and contains `"PCS Pro not running"` (confirms circuit connected and component rendered).
4. The test resolves `IPcsProAutomationService` from `factory.Services`, casts to `MockPcsProAutomationService`, and **awaits** `LaunchAndLoginAsync(CancellationToken.None)`.

Then:
- Both Context 1 and Context 2's `.pcs-status-indicator` elements eventually show `"Loading matches…"` (the `MatchSelection` label — the final state after zero-delay `LaunchAndLoginAsync` completes). Both assertions use `WaitForFunctionAsync` or `Locator.WaitForAsync` with a **10-second timeout** — not a direct text query. The synchronous mock transitions do not imply synchronous DOM rendering (state events dispatch `InvokeAsync(StateHasChanged)` asynchronously on the circuit).
- Both contexts show the same text.

---

## 6. Tests

New test file: `tests/PcsRemote.E2E.Tests/PcsProE2ETests.cs`

| Test name | AC |
|---|---|
| `Smoke_RootUrl_Returns200AndStatusIndicatorInDom` | AC-1 (W-SC-10) |
| `ConnectedUserCount_TwoContexts_UpdatesCorrectly` | AC-2 (W-SC-3) |
| `StateChange_BroadcastToBothContexts` | AC-3 (W-SC-9) |

Test class setup:
- Implement a `PcsProWebApplicationFactory : WebApplicationFactory<Program>` subclass that exposes `Task<string> ServerAddressTask { get; } = _serverAddressTcs.Task` (where `_serverAddressTcs` is the `TaskCompletionSource<string>` set in the `ApplicationStarted` callback — see §3.1). There is no separate `string ServerAddress` property; all references to the bound address go through `await ServerAddressTask`.
- Create and start the factory once per class (`[ClassInitialize]`): instantiate the factory, call `factory.CreateClient()` to trigger startup, then `await factory.ServerAddressTask` to obtain the bound address.
- Create `_playwright` via `await Playwright.CreateAsync()`, then launch one Playwright `IBrowser` (headless Chromium) once per class.
- In `[ClassCleanup]`: dispose `IBrowser` first (await `DisposeAsync`), then call `_playwright.Dispose()`, then dispose the factory. This order prevents Playwright from emitting connection-reset exceptions when the Kestrel server stops.
- Decorate the test class with `[DoNotParallelize]` to guarantee serial execution within the class and prevent connection-count pollution from concurrently running tests.

Each test method creates fresh `IBrowserContext` and `IPage` instances. Context and page disposal MUST occur in a `[TestCleanup]` method (not inline in the test body) to guarantee disposal even when the test throws — an undisposed context keeps a live Blazor circuit open against the shared host, inflating the connection count seen by subsequent tests.

---

## 7. Files Changed

| File | Change |
|---|---|
| `src/PcsRemote.Web/Hubs/PcsProCircuitHandler.cs` | **NEW**: `CircuitHandler` subclass — increments `IConnectionTracker` in `OnCircuitOpenedAsync`, decrements in `OnCircuitClosedAsync` |
| `src/PcsRemote.Web/Hubs/PcsProHub.cs` | Remove `IConnectionTracker` constructor parameter and `Increment`/`Decrement` calls; retain `IPcsProAutomationService` and late-joiner state push |
| `src/PcsRemote.Web/Program.cs` | Add `builder.Services.AddCircuitHandler<PcsProCircuitHandler>()` |
| `tests/PcsRemote.Web.Tests/Hubs/PcsProHubTests.cs` | Remove the four Increment/Decrement verification tests (behaviour moved to CircuitHandler) |
| `tests/PcsRemote.Web.Tests/Hubs/PcsProCircuitHandlerTests.cs` | **NEW**: unit tests for `PcsProCircuitHandler` (OnCircuitOpenedAsync increments, OnCircuitClosedAsync decrements) |
| `tests/PcsRemote.E2E.Tests/PcsRemote.E2E.Tests.csproj` | Change `TargetFramework` to `net8.0-windows`; add `Microsoft.Playwright` and `Microsoft.AspNetCore.Mvc.Testing` package refs; add project refs to `PcsRemote.Web` and `PcsRemote.Automation.Mock`; add `InstallPlaywrightBrowsers` MSBuild target (§3.2) |
| `Directory.Packages.props` | Add `Microsoft.Playwright` version entry (`Microsoft.AspNetCore.Mvc.Testing` is already present — do not add a duplicate) |
| `tests/PcsRemote.E2E.Tests/PcsProWebApplicationFactory.cs` | **NEW**: `WebApplicationFactory<Program>` subclass that starts a real Kestrel host and exposes `Task<string> ServerAddressTask` |
| `tests/PcsRemote.E2E.Tests/PcsProE2ETests.cs` | **NEW**: 3 E2E tests |

---

## 8. Out of Scope

- Tests for W-SC-2 (real-time status indicator update via live hardware) — deferred; requires actual PCS Pro
- Tests for W-SC-4, W-SC-5, W-SC-6, W-SC-7, W-SC-8 (match flow) — covered by bUnit in prior steps
- CI pipeline integration for Playwright (browser download on CI) — infrastructure concern outside HLPS-003
- `appsettings.json` changes to E2E test host defaults — configuration is provided inline by the test host factory
