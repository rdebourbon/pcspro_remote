# SPEC-S-009: Playwright E2E Project and Smoke Test

| Field | Value |
|---|---|
| **Document** | SPEC-S-009-PlaywrightE2E.md |
| **Status** | IN REVIEW |
| **Version** | 0.1 |
| **Date** | 2026-04-12 |
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
- SignalR carries real-time events from the server to browser clients
- The connected user count and state indicator components update reactively

Because `MockPcsProAutomationService` uses configurable delays and error probabilities, E2E tests must configure it with zero delays and zero error probability to eliminate timing dependencies.

---

## 3. Design

### 3.1 Test host strategy

Use `WebApplicationFactory<Program>` (from `Microsoft.AspNetCore.Mvc.Testing`) configured to start a Kestrel HTTP listener on a random available port (not the default in-memory `TestServer`). This provides a genuine HTTP endpoint that Playwright can navigate to. The test host is created once per test class (shared fixture) and disposed after all tests in the class complete.

**Configuration overrides** applied to every test host instance:
- `PcsPro:UseMock = true` — use the mock automation service
- `PcsPro:AutoLaunch = false` — suppress auto-launch so tests control when state changes occur
- `PcsPro:Mock:LaunchDelay = 0`, `PcsPro:Mock:LoginDetectedDelay = 0`, `PcsPro:Mock:CredentialsEnteredDelay = 0` — zero delays so state transitions are instantaneous in tests
- `PcsPro:Mock:ErrorProbability = 0` — no random errors

### 3.2 Playwright setup

Playwright browsers must be installed before tests can run. The build process must include a step to install the required browsers (Chromium is sufficient). Tests use **headless Chromium** by default.

Each test method creates its own `IBrowserContext` (and `IPage` within that context) and disposes them after the test. The `IBrowser` instance is shared across the test class.

### 3.3 W-SC-9 state trigger

Because `WebApplicationFactory` provides access to the DI container, the W-SC-9 test can resolve `IPcsProAutomationService` from `factory.Services` and, since `UseMock=true`, cast it to `MockPcsProAutomationService` (by referencing the `PcsRemote.Automation.Mock` project). After both pages have connected, the test calls `LaunchAndLoginAsync` on the resolved service to trigger state transitions, then asserts that both pages reflect the new state text.

### 3.4 Project changes

The `PcsRemote.E2E.Tests` project must:
- Change target framework from `net8.0` to `net8.0-windows` (required to reference `PcsRemote.Web`, which targets `net8.0-windows`)
- Add project references to `PcsRemote.Web` and `PcsRemote.Automation.Mock`
- Add package references: `Microsoft.Playwright`, `Microsoft.AspNetCore.Mvc.Testing`
- Add these to `Directory.Packages.props` (Central Package Management)

---

## 4. Requirements

| ID | Requirement |
|---|---|
| **R-1** | The E2E test project must produce at least one passing test via `dotnet test`. |
| **R-2** | The smoke test MUST verify HTTP 200 at the root URL AND the presence of the `.pcs-status-indicator` CSS class in the rendered DOM. |
| **R-3** | The W-SC-3 test MUST use two separate Playwright browser contexts connecting to the same server. The `.connected-user-count` element in Context 1 must update to reflect 2 users when Context 2 opens, and back to 1 user when Context 2 closes. |
| **R-4** | The W-SC-9 test MUST verify that a state change triggered after both contexts are connected is reflected in BOTH contexts' `.pcs-status-indicator` elements. |
| **R-5** | All Playwright waits MUST use `WaitForSelectorAsync` or similar expectation-based mechanisms — NOT `Task.Delay` fixed sleeps. |
| **R-6** | The Playwright browser must be launched in headless mode. |
| **R-7** | All solution tests (unit + E2E) MUST pass after this step: `dotnet test` at solution level. |

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
1. Context 1's page navigates to the root URL and waits for the Blazor circuit to connect.
2. Context 2's page navigates to the root URL and waits for its circuit to connect.
Then: Context 1's `.connected-user-count` element eventually shows `"2 users online"`.  
When: Context 2's page is closed.  
Then: Context 1's `.connected-user-count` eventually shows `"1 user online"`.

> **Note:** "Eventually" is enforced by Playwright's `WaitForFunctionAsync` or `Locator.WaitForAsync` with a reasonable timeout (e.g., 5 s), not by a fixed sleep.

### AC-3 — W-SC-9: State change broadcast to all connected contexts

Given: The test host is started with `AutoLaunch=false` and zero-delay mock.  
When:
1. Context 1's page navigates to the root URL.
2. Context 2's page navigates to the root URL.
3. Both pages wait for their SignalR circuits to be ready (i.e., `.pcs-status-indicator` is visible with initial text).
4. The test resolves `IPcsProAutomationService` from `factory.Services`, casts to `MockPcsProAutomationService`, and calls `LaunchAndLoginAsync(CancellationToken.None)`.
Then:
- Both Context 1 and Context 2's `.pcs-status-indicator` elements update away from `"PCS Pro not running"` to reflect the new state (at minimum `"PCS Pro starting…"`).
- Both contexts show the same state text.

---

## 6. Tests

New test file: `tests/PcsRemote.E2E.Tests/PcsProE2ETests.cs`

| Test name | AC |
|---|---|
| `Smoke_RootUrl_Returns200AndStatusIndicatorInDom` | AC-1 (W-SC-10) |
| `ConnectedUserCount_TwoContexts_UpdatesCorrectly` | AC-2 (W-SC-3) |
| `StateChange_BroadcastToBothContexts` | AC-3 (W-SC-9) |

Test class setup:
- Create and start the `WebApplicationFactory` once per class (`[ClassInitialize]`).
- Launch one Playwright `IBrowser` (headless Chromium) once per class.
- Dispose both in `[ClassCleanup]`.

Each test method creates fresh `IBrowserContext` and `IPage` instances and disposes them after the test.

---

## 7. Files Changed

| File | Change |
|---|---|
| `tests/PcsRemote.E2E.Tests/PcsRemote.E2E.Tests.csproj` | Change `TargetFramework` to `net8.0-windows`; add `Microsoft.Playwright` and `Microsoft.AspNetCore.Mvc.Testing` package refs; add project refs to `PcsRemote.Web` and `PcsRemote.Automation.Mock` |
| `Directory.Packages.props` | Add `Microsoft.Playwright` version entry |
| `tests/PcsRemote.E2E.Tests/PcsProE2ETests.cs` | **NEW**: 3 E2E tests |

---

## 8. Out of Scope

- Tests for W-SC-2 (real-time status indicator update via live hardware) — deferred; requires actual PCS Pro
- Tests for W-SC-4, W-SC-5, W-SC-6, W-SC-7, W-SC-8 (match flow) — covered by bUnit in prior steps
- CI pipeline integration for Playwright (browser download on CI) — infrastructure concern outside HLPS-003
- `appsettings.json` changes to E2E test host defaults — configuration is provided inline by the test host factory
