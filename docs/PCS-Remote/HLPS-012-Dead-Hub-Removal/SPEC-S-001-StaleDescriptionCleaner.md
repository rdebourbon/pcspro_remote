# SPEC-S-001 — Relocate Stale-Description Safety-Net

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **Step**    | S-001 |
| **IS**      | IS-012-Dead-Hub-Removal (APPROVED) |
| **HLPS**    | HLPS-012-Dead-Hub-Removal (APPROVED) |
| **Branch**  | `feature/S-001-stale-description-cleaner` |

---

## 1. Requirement

Extract the stale-description safety-net from `OperationInProgressBroadcaster` into a standalone `IHostedService` that has no dependency on any SignalR hub or `IHubContext`. The new service subscribes to the automation service's state-changed event and, when the coordinator is idle, calls `ClearStaleDescription()`.

This satisfies HLPS-012 C-1 and SC-4.

## 2. Design

A new class in `PcsRemote.Web` (within the `Hubs` folder, since it replaces logic currently there) implements `IHostedService`. It takes `IPcsProAutomationService` and `IOperationCoordinatorService` via constructor injection. On `StartAsync`, it subscribes to the state-changed event. On `StopAsync`, it unsubscribes. The event handler checks whether the coordinator is idle and calls `ClearStaleDescription()` if so.

This is the exact logic currently at lines 82–88 of `OperationInProgressBroadcaster`, extracted verbatim with no behavioural change.

## 3. Test Cases

| TC | Scenario | Expected |
|----|----------|----------|
| TC-1 | State changes while coordinator is idle | `ClearStaleDescription()` is called once |
| TC-2 | State changes while coordinator is busy | `ClearStaleDescription()` is NOT called |
| TC-3 | `StopAsync` called, then state changes | `ClearStaleDescription()` is NOT called (unsubscribed) |

## 4. DI Registration

Register the new service as `AddHostedService` in `WebApplicationBuilderExtensions.cs`. Mirror the registration in `PcsProWebApplicationFactory.cs`.

## 5. Acceptance Criteria

| AC | Criterion |
|----|-----------|
| AC-1 | The new service exists, compiles, has no dependency on `PcsProHub` or `IHubContext`, and is registered in both `WebApplicationBuilderExtensions.cs` and `PcsProWebApplicationFactory.cs`. |
| AC-2 | All 3 test cases pass. |
| AC-3 | The full test suite (700 tests) continues to pass — no regressions. |
| AC-4 | Build produces 0 errors and 0 warnings. |

## 6. Review History

### R1 — 2026-04-21
**Panel:** Claude Opus 4.7, GPT 5.4

| Reviewer | Decision | Key Findings |
|----------|----------|--------------|
| Opus 4.7 | APPROVE | LOWs: class name implicit, StopAsync-without-StartAsync edge case. INFO: risk coverage adequate. |
| GPT 5.4 | REQUEST CHANGES | MEDIUM F-01: AC-1 doesn't verify no hub dependency — could pass while retaining hub coupling. |

**Resolution:** Accept F-01 — expanded AC-1 to explicitly require no `PcsProHub`/`IHubContext` dependency and DI registration in both files.

---

*End of SPEC-S-001.*
