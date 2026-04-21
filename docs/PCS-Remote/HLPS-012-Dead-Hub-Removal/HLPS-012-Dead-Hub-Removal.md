# HLPS-012 — Remove Dead SignalR Hub Infrastructure

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **Author**  | Copilot |
| **Created** | 2026-04-21 |

---

## 1. Problem Statement

The PCS Remote Blazor Server application contains a custom SignalR hub (`PcsProHub`) and four `IHostedService` broadcaster classes that were built to push real-time state changes to connected browsers. However, **no client ever connects to this hub**. There is no JavaScript, no `HubConnectionBuilder`, and no Blazor component that establishes a connection to the `/hubs/pcspro` endpoint.

Instead, all Blazor components already subscribe directly to singleton service events (e.g. `AutomationService.StateChanged`, `ManualModeService.ManualModeChanged`) and call `InvokeAsync(StateHasChanged)` to re-render. This is the standard Blazor Server cross-circuit communication pattern — the framework's own SignalR connection (via `/_blazor`) pushes UI diffs to each browser automatically.

The result is **dead code that runs on every application startup** — four `IHostedService` instances subscribe to domain events and broadcast to zero listeners, a hub is mapped to an endpoint that receives no connections, and `AddSignalR()` is called solely for infrastructure that nothing consumes.

## 2. Assumptions

| ID | Assumption | Basis |
|----|------------|-------|
| A-1 | The `/hubs/pcspro` endpoint has zero live consumers — no JavaScript client, no `HubConnectionBuilder`, and no Blazor component establishes a connection. | Verified by exhaustive `grep` across all `.js`, `.ts`, `.html`, `.razor`, and `.cs` files in the repository (2026-04-21). No external or non-repo consumers are known. |
| A-2 | `AddServerSideBlazor()` internally registers SignalR services, making the separate `AddSignalR()` call redundant when no custom hub is present. | Microsoft ASP.NET Core framework behaviour — to be confirmed by build/test verification during implementation. |
| A-3 | The test baseline is 700 tests as of commit `16701ae` on `master`. | Verified by `dotnet test` output during IS-011 remediation (2026-04-21). |

## 3. Constraints

| ID | Constraint |
|----|------------|
| C-1 | The stale-description safety-net in `OperationInProgressBroadcaster` (calls `ClearStaleDescription` on state transitions while the coordinator is idle) must be preserved. This is the only behavioural side-effect in the broadcaster layer. |
| C-2 | `PcsProCircuitHandler` and `IConnectionTracker` / `ConnectionTracker` are NOT part of the hub infrastructure — they track Blazor circuits and must be retained. |
| C-3 | The existing component event-subscription pattern (`service.Event += handler` / `InvokeAsync(StateHasChanged)` / unsubscribe in `Dispose`) must not be altered. |
| C-4 | All tests not directly testing deleted code must continue to pass. Hub/broadcaster tests (scope item 9) are intentionally deleted alongside the code they test. |
| C-5 | Zero build warnings, zero build errors. |

## 4. Scope

### In Scope — Remove

| # | Artifact | Location |
|---|----------|----------|
| 1 | `PcsProHub` | `src/PcsRemote.Web/Hubs/PcsProHub.cs` |
| 2 | `PcsProStateBroadcaster` | `src/PcsRemote.Web/Hubs/PcsProStateBroadcaster.cs` |
| 3 | `ManualModeBroadcaster` | `src/PcsRemote.Web/Hubs/ManualModeBroadcaster.cs` |
| 4 | `OperationInProgressBroadcaster` | `src/PcsRemote.Web/Hubs/OperationInProgressBroadcaster.cs` |
| 5 | `AutomationLogBroadcaster` | `src/PcsRemote.Web/Hubs/AutomationLogBroadcaster.cs` |
| 6 | `PcsProHubConstants` | `src/PcsRemote.Web/Hubs/PcsProHubConstants.cs` |
| 7 | Hub DI registrations | `AddHostedService` calls for items 2–5, `MapHub<PcsProHub>`, and `AddSignalR()` in `WebApplicationBuilderExtensions.cs` |
| 8 | Hub DI registrations (test) | Mirrored registrations in `PcsProWebApplicationFactory.cs` |
| 9 | Hub/broadcaster unit tests | `tests/PcsRemote.Web.Tests/Hubs/PcsProHubTests.cs`, `PcsProStateBroadcasterTests.cs`, `ManualModeBroadcasterTests.cs`, `OperationInProgressBroadcasterTests.cs`, `AutomationLogBroadcasterTests.cs` |
| 10 | Now-unused package references | Any explicit SignalR package references in `.csproj` files that are no longer required after hub removal. |

### In Scope — Relocate

| # | Behaviour | Current Location | Target Constraint |
|---|-----------|-----------------|-------------------|
| 1 | Stale-description safety-net: on `StateChanged`, if coordinator is idle, call `ClearStaleDescription()` | `OperationInProgressBroadcaster.OnStateChanged` | Must remain within `PcsRemote.Web`. Candidate approaches include: (a) a lightweight `IHostedService` with no hub dependency, (b) integration into `OperationCoordinatorService`, (c) an event subscriber in an existing Web-layer service. Final selection deferred to IS phase. |

### Out of Scope

| # | Item | Rationale |
|---|------|-----------|
| 1 | `PcsProCircuitHandler` | Tracks Blazor circuits, not hub connections — actively used |
| 2 | `IConnectionTracker` / `ConnectionTracker` | Used by `PcsProCircuitHandler` — actively used |
| 3 | `ConnectedUserCount.razor` | Consumes `IConnectionTracker` — actively used |
| 4 | Component event-subscription pattern | Already correct; not being modified |
| 5 | `ScoreboardPollingService` / `AutoLaunchService` | Separate `IHostedService` instances with active functionality |

## 5. Success Criteria

| ID | Criterion |
|----|-----------|
| SC-1 | All six dead files (hub, 4 broadcasters, constants) are deleted from the codebase. |
| SC-2 | All DI registrations for the dead infrastructure are removed from both `WebApplicationBuilderExtensions.cs` and `PcsProWebApplicationFactory.cs`. |
| SC-3 | The `/hubs/pcspro` endpoint is no longer mapped. |
| SC-4 | The stale-description safety-net behaviour is preserved and tested. Observable behaviour: when `StateChanged` fires and the coordinator is idle (`IsOperationInProgress == false`), `ClearStaleDescription()` is called. A unit test must assert this path directly against the new host. |
| SC-5 | All hub/broadcaster unit tests are deleted (they test deleted code). |
| SC-6 | The application builds with 0 errors and 0 warnings. |
| SC-7 | All remaining tests pass. The before/after delta must be documented (baseline: 700 tests as of A-3; expected reduction: hub/broadcaster test count). |
| SC-8 | Cross-circuit state synchronisation continues to work correctly via the singleton event-subscription pattern, verified by `OperationalUxE2ETests` (TC-1 through TC-4 exercise late-joiner and cross-circuit state sync across multiple browser pages). |

## 6. Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|----|------|------------|--------|------------|
| R-1 | Removing `AddSignalR()` breaks Blazor Server if `AddServerSideBlazor()` does not internally register the required SignalR services | Low | High | Verify by building and running E2E tests. If the build or tests fail, retain `AddSignalR()` as a standalone call — this is an acceptable final state since it is a lightweight no-op when no custom hub is mapped. |
| R-2 | The stale-description safety-net relocation introduces a behavioural regression | Low | Medium | Port the exact same logic to the new host and verify with a unit test that asserts `ClearStaleDescription()` is called when `StateChanged` fires while the coordinator is idle. |

> **Note on reversibility:** The singleton service event infrastructure (`StateChanged`, `ManualModeChanged`, etc.) remains intact after this change. If a future requirement introduces an external (non-Blazor) SignalR consumer, the hub can be reintroduced without altering the domain services.

## 7. Unknowns Register

| ID | Description | Owner | Blocking |
|----|-------------|-------|----------|
| U-1 | Whether `AddSignalR()` can be safely removed when `AddServerSideBlazor()` is present — resolved by build/test verification during implementation. If removal breaks the build, `AddSignalR()` is retained standalone. | Agent | No |
| U-2 | Final relocation target for the stale-description safety-net — bounded to `PcsRemote.Web` layer, with candidate approaches listed in Scope §Relocate. Resolved during IS phase. | Agent | No |

## 8. Review History

### R1 — 2026-04-21
**Panel:** Claude Opus 4.7, GPT 5.4, Claude Sonnet 4.6

| Reviewer | Decision | Key Findings |
|----------|----------|--------------|
| Opus 4.7 | APPROVE | LOWs: C-4/SC-7 tension, no layer constraint on relocation, R-1 fallback unclear. INFOs: relocation as U-2, package refs, SC-8 naming. |
| GPT 5.4 | REQUEST CHANGES | HIGH: No assumptions section. MEDIUMs: SC-8 vague, external consumer risk. LOW: SC-7 approximate count. |
| Sonnet 4.6 | REQUEST CHANGES | CRITICAL: C-4 contradicts SC-7. HIGHs: No assumptions section, relocation unbounded. MEDIUMs: SC-4 unverifiable, SC-8 unnamed tests. LOWs: AddSignalR resolvable now, R-3 not execution risk. |

**Resolution:** All findings accepted or downgraded (Sonnet HIGH F-03 on relocation downgraded to MEDIUM — full selection is IS detail, but layer constraint and candidates added). Fixes applied: Added Assumptions section (A-1/A-2/A-3), rewrote C-4, expanded SC-4/SC-7/SC-8, added scope item 10 (package refs), moved AddSignalR to Remove with fallback, demoted R-3 to reversibility note, added U-2, clarified R-1 fallback.

### R2 — 2026-04-21
**Panel:** GPT 5.4, Claude Sonnet 4.6 (Opus 4.7 approved in R1)

| Reviewer | Decision | Notes |
|----------|----------|-------|
| GPT 5.4 | APPROVE | All R1 findings verified as adequately addressed. No new findings. |
| Sonnet 4.6 | APPROVE | All 8 R1 findings verified as adequately addressed. Noted relocation candidate (b) auto-disqualified by layer constraint if OperationCoordinatorService is in Core — no contradiction. No new findings. |

**Result:** Unanimous approval (3/3). Document status → APPROVED.

---

*End of HLPS-012.*
