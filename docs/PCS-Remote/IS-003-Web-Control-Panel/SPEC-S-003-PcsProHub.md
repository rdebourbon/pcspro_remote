# SPEC-IS003-S-003: PcsProHub — SignalR Hub, Connection Tracking, and State Broadcast

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-PcsProHub.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-11 |
| **Step ID** | S-003 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Branch** | `feature/hlps003-S003-pcspro-hub` |
| **Depends on** | SPEC-S-001-Blazor-Middleware.md (APPROVED, delivered) |

---

## 1. Purpose

S-001 established the Blazor Server + SignalR middleware pipeline. S-002 created the visual application shell. S-003 introduces the SignalR hub that is the real-time backbone of the entire application: it broadcasts PCS Pro lifecycle state transitions to all connected browsers simultaneously and maintains a live count of connected clients for display in the header (wired in S-005).

This step is strictly backend — no Razor components or UI changes are introduced. The hub and its supporting services are independently testable units. S-004, S-005, and S-008 are the downstream consumers.

When this step is complete, the application has a registered SignalR hub endpoint; an automation service state change is broadcast to all connected clients; a new browser client immediately receives the current state on connection (late-joiner support); and the connection count is tracked and notified on every connect/disconnect.

---

## 2. Shared Client Contract

The SignalR client method name used to deliver state updates to browsers is **`"ReceiveStateUpdate"`**. This is a shared contract between the hub (S-003) and the Blazor components that subscribe (S-004, S-005). It must be used consistently across all broadcast calls in this step. The payload is a single value of type `PcsProState` (the new state).

This constant must be defined in one place within the hub infrastructure (not duplicated inline as string literals) so that downstream steps reference the same definition.

---

## 3. Requirements

### R-1 — Connection tracking service

A dedicated singleton service must be created to own the connected-client count. It must:

- Maintain a thread-safe integer counter representing the number of currently connected SignalR clients; the counter starts at zero
- Expose the current count as a readable property
- Raise a notification (event or callback) whenever the count changes, passing the new count value to subscribers — this is the mechanism S-005 will use to re-render the header without polling
- Expose increment and decrement operations; the counter must never go below zero (a decrement on zero is a no-op)
- Be registered in DI as a singleton before `builder.Build()` is called

The service must be injectable into both the hub and Blazor components, so an interface must define its public surface.

The notification may be raised from any thread that calls increment or decrement; subscribers must be prepared to receive concurrent notifications from multiple threads. Count ordering across concurrent operations is not guaranteed at the notification layer — only the final settled count is authoritative.

### R-2 — State broadcaster service

A dedicated service must be created to bridge `IPcsProAutomationService.StateChanged` to all connected SignalR clients. It must:

- Subscribe to `IPcsProAutomationService.StateChanged` when the application starts
- On each state-change event, broadcast the new `PcsProState` value to all connected clients using the client method name defined in §2
- Use `IHubContext<PcsProHub>` (not a hub instance) to broadcast, as hub instances are transient and cannot hold cross-request subscriptions
- Be active for the full application lifetime; the subscription must be established before any client connects
- Unsubscribe from `StateChanged` on application shutdown to prevent memory leaks

The broadcaster must be registered as an `IHostedService` so the framework manages its lifetime and start/stop sequencing automatically.

### R-3 — `PcsProHub` SignalR hub

A hub class must be created in `PcsRemote.Web/Hubs/`. It must:

- Inherit from the `Hub` base class
- Override `OnConnectedAsync`:
  - Increment the connection count via the connection tracking service
  - Send the current `PcsProState` (read from `IPcsProAutomationService.CurrentState`) to the newly connected caller only, using the client method name from §2 (late-joiner support)
  - Call the base implementation
- Override `OnDisconnectedAsync`:
  - Decrement the connection count via the connection tracking service
  - Call the base implementation (passing through the exception argument)
- Receive its dependencies (connection tracking service, automation service) via constructor injection

### R-4 — Hub endpoint registration

`Program.cs` must be updated with the following three changes:

1. Map the hub endpoint by calling `MapHub<PcsProHub>` at the path `/hubs/pcspro`, placed after `UseRouting()` in the existing middleware pipeline
2. Register the connection tracking service interface and singleton implementation in the service collection before `builder.Build()`
3. Register the broadcaster service via `AddHostedService` before `builder.Build()`

No other changes to `Program.cs` are required in this step.

### R-5 — Unit tests: connection tracking service

Unit tests must be added to `PcsRemote.Web.Tests` covering:

- Counter starts at zero on construction
- A single increment produces a count of one
- Multiple sequential increments produce the correct final count
- A decrement after one increment returns the count to zero
- Decrement on a zero counter leaves the count at zero (no negative counts)
- The change notification is raised with the correct new count value on each increment and decrement (including the no-op decrement-at-zero case: no notification must fire if the count does not change)

### R-6 — Unit tests: state broadcaster

Unit tests must be added to `PcsRemote.Web.Tests` covering:

- When the automation service raises `StateChanged`, the broadcaster calls the hub context to send the new state to all clients using the correct client method name (§2)
- The broadcaster subscribes to `StateChanged` and is ready to broadcast; the test must verify this by confirming that a `StateChanged` event raised on the automation service after `StartAsync` completes results in a call to the hub context with the correct client method name
- When the broadcaster is stopped, it does not forward subsequent `StateChanged` events (event unsubscription verified)

Tests must mock `IHubContext<PcsProHub>` and `IPcsProAutomationService` using the project's existing mocking library (Moq).

### R-7 — Unit tests: hub `OnConnectedAsync` and `OnDisconnectedAsync`

Unit tests must be added to `PcsRemote.Web.Tests` covering:

- `OnConnectedAsync` calls increment on the connection tracking service exactly once
- `OnConnectedAsync` sends the current state to the calling client using the correct client method name (§2)
- `OnDisconnectedAsync` calls decrement on the connection tracking service exactly once

Tests must mock the SignalR hub context infrastructure (caller client proxy) and the connection tracking service dependency.

---

## 4. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | A connection tracking service interface and singleton implementation exist in `PcsRemote.Web`; the implementation is registered in `Program.cs` |
| AC-2 | The connection tracking service counter is thread-safe and never goes below zero |
| AC-3 | The connection tracking service raises a count-change notification on every connect/disconnect that changes the count |
| AC-4 | A state broadcaster `IHostedService` exists in `PcsRemote.Web`; it is registered in `Program.cs` via `AddHostedService` |
| AC-5 | The state broadcaster subscribes to `IPcsProAutomationService.StateChanged` on `StartAsync` and unsubscribes on `StopAsync` |
| AC-6 | When `IPcsProAutomationService.StateChanged` fires, the broadcaster sends the new state to all connected clients using the client method name `"ReceiveStateUpdate"` |
| AC-7 | `PcsProHub` exists in `PcsRemote.Web/Hubs/`; it inherits from `Hub` and is registered at `/hubs/pcspro` |
| AC-8 | On `OnConnectedAsync`, the hub increments the connection count and pushes the current state to the caller via `"ReceiveStateUpdate"` |
| AC-9 | On `OnDisconnectedAsync`, the hub decrements the connection count |
| AC-10 | The `"ReceiveStateUpdate"` method name is defined as a single constant with at minimum `internal` visibility (so downstream steps S-004 and S-005 can reference it directly without re-declaring it) and reused across all usages; no inline string duplication |
| AC-11 | All R-5 connection tracker unit tests pass |
| AC-12 | All R-6 broadcaster unit tests pass |
| AC-13 | All R-7 hub unit tests pass |
| AC-14 | Build completes with 0 errors and 0 warnings |
| AC-15 | All 80 existing tests continue to pass; total test count ≥ 80 + new tests |

---

## 5. Accepted Risks

| ID | Risk | Justification |
|----|------|---------------|
| AR-1 | No client-side Blazor component wires up to `"ReceiveStateUpdate"` in this step | By design — S-004 and S-005 are the downstream consumers. The broadcast mechanism is complete and tested server-side; client consumption is their responsibility |
| AR-2 | `async void` event handler in broadcaster is used to invoke async hub broadcast | The `IPcsProAutomationService.StateChanged` event signature is `EventHandler<PcsProState>`, which returns `void`. The broadcaster must use an `async void` handler or a fire-and-forget `Task.Run` pattern; exceptions in `async void` handlers that escape are unobserved. This risk is accepted for Phase 1; HLPS-005 hardening is the appropriate resolution point |
| AR-3 | HTTP-only, 0.0.0.0 binding | Inherited from S-001 (SPEC-S-001 §6 AR-1); no change in this step |

---

## 6. Out of Scope

- Client-side Blazor component subscription to `"ReceiveStateUpdate"` (S-004, S-005)
- Connected-user count display in the header UI (S-005)
- Status indicator component and state-to-colour mapping (S-004)
- Auto-launch hosted service (S-008)
- Any JavaScript client file for the hub (Blazor Server uses the .NET SignalR client internally)

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | Sonnet 4.6, GPT-4.1 | NEEDS REVIEW — 3 MEDIUM accepted (R-4 contradiction, F-2 notification thread-safety, F-3 ambiguous test intent); 1 LOW rejected (F-4 baseline count — 80 is correct: 32+47+1); 1 LOW accepted (F-5 constant visibility); v0.2 fixes applied |
| R2 | 2026-04-11 | Sonnet 4.6, GPT-4.1 | APPROVED — unanimous, 0 blocking, 0 non-blocking; all 5 R1 fixes verified, 0 regressions |
