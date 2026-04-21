# SPEC-S-006 — Automation Log Service Implementation and Broadcasting

**IS Step:** S-006
**Version:** 0.2
**Status:** DRAFT

---

## Scope

Implement `IAutomationLogService` (defined in S-003) as a thread-safe singleton in `PcsRemote.Web.Services`. Create an `AutomationLogBroadcaster` (`IHostedService`) that pushes new entries to all SignalR clients. Update `PcsProHub.OnConnectedAsync` to send the full buffer snapshot to late-joining clients.

---

## Requirements

### R-1 — AutomationLogService implementation

A class `AutomationLogService` in `PcsRemote.Web.Services` implements `IAutomationLogService`. The service maintains a bounded rolling buffer of `AutomationLogEntry` records. The buffer capacity is 200 entries (OUX-C-4). The implementation must be thread-safe for concurrent `AddEntry` calls from different automation threads.

### R-2 — Buffer eviction

When `AddEntry` is called and the buffer is at capacity, the oldest entry is evicted before the new entry is appended. `GetRecentEntries()` always returns entries ordered oldest-to-newest.

### R-3 — Timestamp generation

`AddEntry(string, AutomationLogOutcome)` generates the timestamp internally using `DateTimeOffset.UtcNow`. Callers supply only the action text and outcome — the timestamp is not a parameter.

### R-4 — Publish-after-commit event ordering

`EntryAdded` fires only after the new entry is committed to the buffer. Any subscriber that calls `GetRecentEntries()` inside the event handler is guaranteed to see the new entry.

### R-5 — Immutable snapshot

`GetRecentEntries()` returns an immutable snapshot. Subsequent `AddEntry` calls do not mutate a previously returned list.

### R-6 — AutomationLogBroadcaster

A new `AutomationLogBroadcaster : IHostedService` in `PcsRemote.Web.Hubs` subscribes to `IAutomationLogService.EntryAdded` on `StartAsync` and unsubscribes on `StopAsync`. On each new entry, it sends the entry to all connected clients via `IHubContext<PcsProHub>`.

The wire message is `ReceiveAutomationLogEntry` carrying the serialised `AutomationLogEntry`.

The broadcaster must inject `ILogger<AutomationLogBroadcaster>` and wrap the `SendAsync` call in try/catch with error-level logging — matching the established pattern in `OperationInProgressBroadcaster`. The event handler is `async void`; exceptions must not propagate to the event source.

### R-7 — Hub late-joiner support

`PcsProHub.OnConnectedAsync` sends the full buffer snapshot to the connecting client via a `ReceiveAutomationLogSnapshot` message. The snapshot is an `IReadOnlyList<AutomationLogEntry>` retrieved from `IAutomationLogService.GetRecentEntries()`.

### R-8 — Hub constants

Two new constants are added to `PcsProHubConstants`:
- `ReceiveAutomationLogEntry` — individual entry push
- `ReceiveAutomationLogSnapshot` — full buffer for late-joiners

### R-9 — DI registration

`WebApplicationBuilderExtensions.AddPcsRemoteServices()` registers `AutomationLogService` as a singleton implementation of `IAutomationLogService`, and registers `AutomationLogBroadcaster` as a hosted service.

---

## Implementation Notes

- **Data structure:** A `Queue<AutomationLogEntry>` (or `LinkedList`) provides O(1) enqueue/dequeue. The `lock` keyword is sufficient for thread safety — contention is low (entries arrive at operator-visible action granularity, not per-frame).
- **Snapshot:** `GetRecentEntries()` returns `_buffer.ToList().AsReadOnly()` inside the lock to guarantee immutability.
- **Event invocation:** `EntryAdded` must be raised **outside** the lock to avoid invoking subscriber code while holding the buffer lock. The pattern is: lock → evict/enqueue → capture handler delegate → unlock → invoke.
- **Broadcaster pattern:** Follows the established `OperationInProgressBroadcaster` pattern — subscribe on `StartAsync`, unsubscribe on `StopAsync`, `async void` handler with try/catch + error logging.

---

## Test Cases

| ID | Category | Scenario | Expectation |
|---|---|---|---|
| TC-1 | Service | AddEntry stores entry | `GetRecentEntries()` contains the added entry with matching action and outcome |
| TC-2 | Service | Entries ordered oldest-to-newest | Three entries added in order A, B, C → `GetRecentEntries()` returns [A, B, C] |
| TC-3 | Service | Buffer evicts oldest at capacity | Add 201 entries → buffer size is 200, first entry is missing, last entry is present |
| TC-4 | Service | EntryAdded fires after commit | Event handler calls `GetRecentEntries()` and finds the new entry |
| TC-5 | Service | Snapshot is immutable | Snapshot taken before a second `AddEntry` does not contain the second entry |
| TC-6 | Service | Timestamp is generated internally | Entry timestamp is close to `DateTimeOffset.UtcNow` (within 1 second tolerance) |
| TC-7 | Service | Thread-safe concurrent adds | 100 parallel `AddEntry` calls → `GetRecentEntries().Count` == 100, no exceptions |
| TC-8 | Broadcaster | Entry pushed to hub clients | On `EntryAdded`, broadcaster calls `SendAsync(ReceiveAutomationLogEntry, entry)` on `Clients.All` |
| TC-9 | Broadcaster | Subscribe on start, unsubscribe on stop | After `StopAsync`, `AddEntry` does not trigger broadcast |
| TC-10 | Hub | Late-joiner receives snapshot | `OnConnectedAsync` sends `ReceiveAutomationLogSnapshot` with buffer contents to `Clients.Caller` |
| TC-11 | DI | Registration resolves | `IAutomationLogService` resolves from service provider to `AutomationLogService` singleton |
| TC-12 | Service | Concurrent adds exceeding capacity | 300 parallel `AddEntry` calls → buffer size is 200, contains the most recent 200, no exceptions |

---

## Acceptance Criteria

1. `AutomationLogService` is registered as singleton, thread-safe, bounded at 200 entries.
2. Oldest entry evicted when buffer full.
3. `GetRecentEntries()` returns immutable oldest-to-newest snapshot.
4. `EntryAdded` fires after entry is committed (publish-after-commit).
5. `AutomationLogBroadcaster` pushes entries to all SignalR clients via `ReceiveAutomationLogEntry`.
6. Late-joiner receives full buffer via `ReceiveAutomationLogSnapshot`.
7. All existing tests pass (458+).
8. Build produces 0 errors, 0 warnings.
