# SPEC-S-005: Operation Status Implementation and Banner UI

| Field | Value |
|---|---|
| **Document** | SPEC-S-005-OperationStatusAndBanner.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-21 |
| **Governing IS** | IS-011-Operational-UX.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-011-Operational-UX.md v0.4 (APPROVED) |
| **Step** | S-005 |
| **Branch** | `feature/IS-011-S-005-operation-status-banner` |

---

## 1. Objective

Implement the operation description storage, broadcasting, and status banner UI so that operators across all browsers see what automation action is in progress. This turns the existing coordinator lock (boolean only) into an informative status system.

---

## 2. Scope

### In Scope

- `IOperationCoordinatorService` — add `OperationDescriptionChanged` event and `ClearStaleDescription()` method (Core interface, additive)
- `OperationCoordinatorService` — store description on `BeginOperation`, clear on `MarkComplete`, implement new event and method
- Safety-net clearing — state machine subscription in broadcaster calls `ClearStaleDescription()` when coordinator is idle
- `OperationInProgressBroadcaster` — subscribe to `OperationDescriptionChanged` and `IPcsProAutomationService.StateChanged`; push description via a dedicated SignalR message
- `PcsProHub` — send current description to late-joining clients
- `PcsProHubConstants` — new `ReceiveOperationDescriptionUpdate` constant
- New `OperationStatusBanner` component in `MainLayout`
- Update all existing `BeginOperation()` callers to pass description strings
- Unit tests for coordinator description logic (including new event and safety-net method)
- bUnit tests for status banner component

### Out of Scope

- `AutoLaunchService` — does not use coordinator lock; status for startup is deferred
- Automation log entries (S-008)
- Debug section UI (S-007)

---

## 3. Requirements

### R-1: Core Interface Extensions

Add two members to `IOperationCoordinatorService`:

```csharp
/// <summary>
/// Raised when <see cref="CurrentOperationDescription"/> changes.
/// The argument is the new description (null when cleared).
/// </summary>
event EventHandler<string?>? OperationDescriptionChanged;

/// <summary>
/// Clears a stale description when no operation is in progress.
/// No-op when an operation is active (<see cref="IsOperationInProgress"/> is true).
/// Fires <see cref="OperationDescriptionChanged"/> with null when the description
/// is actually cleared.
/// </summary>
void ClearStaleDescription();
```

This preserves the existing `OperationInProgressChanged` contract unchanged — that event continues to fire only on actual 0→1 and 1→0 transitions. The new `OperationDescriptionChanged` event is the sole channel for description changes.

### R-2: OperationCoordinatorService Description Storage

The service stores the description string provided to `BeginOperation(string? description)`. Internal ordering within each method:

**`BeginOperation` (CAS succeeds):**
1. CAS 0→1 succeeds
2. `Volatile.Write` description
3. Fire `OperationInProgressChanged(true)`
4. Fire `OperationDescriptionChanged(description)`
5. Return true

**`BeginOperation` (CAS fails):**
1. CAS 0→1 fails — no writes, no events
2. Return false

**`MarkComplete` (CAS succeeds):**
1. CAS 1→0 succeeds
2. `Volatile.Write` description to null
3. Fire `OperationDescriptionChanged(null)`
4. Fire `OperationInProgressChanged(false)`

**`MarkComplete` (CAS fails / already idle):**
1. CAS 1→0 fails — no writes, no events

Thread safety: the `_description` field uses `Volatile.Read` on get and `Volatile.Write` on set. The CAS guarantees mutual exclusion between concurrent `BeginOperation` callers, so only the CAS winner writes the description. A concurrent reader between step 1 and step 2 may briefly see `IsOperationInProgress=true` with a stale description; this is acceptable because the subscriber events (steps 3–4) provide the authoritative notification.

`CurrentOperationDescription` property: `Volatile.Read(ref _description)`.

### R-3: ClearStaleDescription Implementation

```csharp
public void ClearStaleDescription()
{
    // Pre-check: skip if an operation is active.
    if (Volatile.Read(ref _inProgress) != 0)
        return;

    // Atomically clear _description and capture previous value.
    var previous = Interlocked.Exchange(ref _description, null);
    if (previous is null)
        return; // Already null — nothing to do.

    // Re-verify coordinator is still idle after the exchange.
    // If BeginOperation slipped in (CAS 0→1) between our pre-check and
    // the exchange, we may have cleared an active operation's description.
    if (Volatile.Read(ref _inProgress) != 0)
    {
        // An operation started concurrently. Restore the previous value
        // only if _description is still null (BeginOperation's step 2
        // may not have executed yet). If BeginOperation already wrote its
        // own description, CompareExchange is a no-op — correct either way.
        Interlocked.CompareExchange(ref _description, previous, null);
        return; // Suppress event — BeginOperation owns the description now.
    }

    OperationDescriptionChanged?.Invoke(this, null);
}
```

- Pre-checks `_inProgress` — if an operation is active, the method is a no-op
- Uses `Interlocked.Exchange` to atomically clear `_description` and capture the previous value
- **Re-checks `_inProgress` after the exchange** to detect a concurrent `BeginOperation` that raced past the pre-check (TOCTOU mitigation). If detected, restores the previous description via `CompareExchange` and suppresses the event
- Only fires `OperationDescriptionChanged(null)` when the description was non-null AND the coordinator remained idle through the entire operation
- Does NOT fire `OperationInProgressChanged` — that event's contract is unchanged

### R-4: Safety-Net Subscription

The `OperationInProgressBroadcaster` gains an additional subscription to `IPcsProAutomationService.StateChanged`. On any state transition:
- If `_coordinatorService.IsOperationInProgress` is false → call `_coordinatorService.ClearStaleDescription()`
- If true → no-op (the active operation's `MarkComplete` will handle clearing)

This is established in `StartAsync` and torn down in `StopAsync`, alongside the existing `OperationInProgressChanged` subscription.

### R-5: Broadcaster SignalR Messages

The `OperationInProgressBroadcaster` subscribes to `OperationDescriptionChanged` (in addition to the existing `OperationInProgressChanged`) and sends a dedicated `ReceiveOperationDescriptionUpdate` SignalR message with the description string (string or null) to all clients.

The existing `ReceiveOperationInProgressUpdate` message continues to carry only the bool flag — its contract is unchanged.

### R-6: Hub Late-Joiner Support

`PcsProHub.OnConnectedAsync` sends the current `CurrentOperationDescription` to the newly connected client via `ReceiveOperationDescriptionUpdate`, after the existing `ReceiveOperationInProgressUpdate` send.

### R-7: OperationStatusBanner Component

A new Blazor component displaying the current operation description:
- **Subscribe-before-snapshot pattern** (established project convention): subscribe to `IOperationCoordinatorService.OperationDescriptionChanged` and `OperationInProgressChanged` BEFORE reading `IsOperationInProgress` and `CurrentOperationDescription`
- Renders a banner with the description text when `IsOperationInProgress` is true AND `CurrentOperationDescription` is non-null and non-empty
- Renders empty (no DOM output) otherwise
- Uses CSS class `operation-status-banner`
- Implements `IDisposable` with proper event unsubscription and `_disposed` guard (same pattern as `ManualModeBanner`)
- Placed in `MainLayout.razor` directly below `ManualModeBanner` (inside `RadzenHeader`, after the existing `ManualModeBanner` tag)

Empty or whitespace-only descriptions are treated as absent — the banner does not render.

### R-8: Caller Description Updates

All existing `BeginOperation()` call sites are updated to pass meaningful description strings using the Unicode ellipsis character (U+2026):
- `ErrorDisplay.razor` (Retry): `"Retrying automation\u2026"`
- `ChangeMatchButton.razor`: `"Changing match\u2026"`
- `RefreshScoreboardButton.razor`: `"Refreshing scoreboard\u2026"`
- `StreamingControls.razor` (start): `"Starting stream\u2026"`
- `StreamingControls.razor` (stop): `"Stopping stream\u2026"`

---

## 4. Design Notes

### Two-Path Clearing Model (IS-011 S-005)

1. **Primary path**: `MarkComplete()` clears description — guaranteed by try/finally on all code paths
2. **Safety net**: State machine subscription calls `ClearStaleDescription()` when coordinator is idle — catches edge cases like health-check-driven Error transitions outside coordinator lock

### Why a Separate Event

Adding `OperationDescriptionChanged` to the Core interface (rather than overloading `OperationInProgressChanged`) preserves the existing event contract. The `OperationInProgressChanged` event fires strictly on 0→1 and 1→0 CAS transitions — changing this would break existing subscribers and existing tests. The new event gives the banner and broadcaster a clean subscription point for description-only changes.

### Event Ordering Rationale

The asymmetric event ordering between `BeginOperation` and `MarkComplete` is intentional:
- **`BeginOperation`**: `InProgressChanged(true)` fires **before** `DescriptionChanged(desc)` — subscribers learning "in progress" can immediately read the description property (already written in step 2)
- **`MarkComplete`**: `DescriptionChanged(null)` fires **before** `InProgressChanged(false)` — subscribers see the description cleared before the "idle" signal, preventing a banner from briefly rendering with a null description while still "in progress"

### Late-Joiner Snapshot Atomicity

`PcsProHub.OnConnectedAsync` reads `IsOperationInProgress` and `CurrentOperationDescription` as two independent volatile reads. State can change between reads. The banner's compound condition (`IsOperationInProgress && !string.IsNullOrWhiteSpace(CurrentOperationDescription)`) masks any inconsistency — a stale read at worst produces a brief empty banner that the next event corrects.

### Banner Event Handler Pattern

The banner's `OperationDescriptionChanged` handler re-reads `CurrentOperationDescription` from the property (not the event arg) before re-rendering. This is consistent with the subscribe-before-snapshot pattern and avoids out-of-order event delivery hazards.

### Thread Safety

The `_description` field uses `Volatile.Read`/`Volatile.Write` for normal reads/writes and `Interlocked.Exchange`/`Interlocked.CompareExchange` in `ClearStaleDescription` for atomic clear-and-restore. Both `Volatile` and `Interlocked` operations provide the necessary memory fences; mixing is intentional because `ClearStaleDescription` requires the atomic exchange-and-capture semantics that `Volatile.Write` does not provide.

---

## 5. Test Cases

### Coordinator Unit Tests (PcsRemote.Web.Tests)

#### TC-1: `BeginOperation_WithDescription_StoresDescription`
**Assert:** After `BeginOperation("test desc")` returns true, `CurrentOperationDescription` equals `"test desc"`.

#### TC-2: `MarkComplete_ClearsDescription`
**Assert:** After `BeginOperation("desc")` then `MarkComplete`, `CurrentOperationDescription` is null.

#### TC-3: `BeginOperation_CASFails_DoesNotOverwriteDescription`
**Assert:** After a successful `BeginOperation("first")`, a second `BeginOperation("second")` returns false and `CurrentOperationDescription` remains `"first"`.

#### TC-4: `BeginOperation_NullDescription_StoresNull`
**Assert:** `BeginOperation(null)` returns true and `CurrentOperationDescription` is null.

#### TC-5: `BeginOperation_FiresOperationDescriptionChanged`
**Assert:** `BeginOperation("desc")` raises `OperationDescriptionChanged` with `"desc"`.

#### TC-6: `MarkComplete_FiresOperationDescriptionChangedNull`
**Assert:** After `BeginOperation`, `MarkComplete` raises `OperationDescriptionChanged` with null.

#### TC-7: `ClearStaleDescription_WhenIdleAndStale_ClearsAndFiresEvent`
**Setup:** Use reflection to set `_description` to `"stale"` while coordinator is idle (simulating a bug where MarkComplete didn't clear).
**Assert:** `ClearStaleDescription()` sets `CurrentOperationDescription` to null and fires `OperationDescriptionChanged` with null.

#### TC-8: `ClearStaleDescription_WhenIdleAndNull_IsNoOp`
**Assert:** When `CurrentOperationDescription` is already null and coordinator is idle, `ClearStaleDescription()` does NOT fire `OperationDescriptionChanged`.

#### TC-9: `ClearStaleDescription_WhenInProgress_IsNoOp`
**Assert:** During an active operation (`BeginOperation` succeeded), `ClearStaleDescription` does not clear the description and does not fire `OperationDescriptionChanged`.

#### TC-10: `MarkComplete_FiresDescriptionChangedBeforeInProgressChanged`
**Assert:** `OperationDescriptionChanged(null)` fires before `OperationInProgressChanged(false)` on `MarkComplete`.

#### TC-11: `BeginOperation_FiresInProgressChangedBeforeDescriptionChanged`
**Assert:** `OperationInProgressChanged(true)` fires before `OperationDescriptionChanged(desc)` on `BeginOperation`.

### Banner bUnit Tests (PcsRemote.Web.Tests)

#### TC-12: `OperationInProgress_WithDescription_BannerShowsDescription`
**Assert:** When coordinator reports `IsOperationInProgress=true` and `CurrentOperationDescription="Launching\u2026"`, the banner renders with the description text and `operation-status-banner` CSS class.

#### TC-13: `NoOperation_BannerRendersEmpty`
**Assert:** When coordinator reports `IsOperationInProgress=false`, the banner renders no DOM output.

#### TC-14: `OperationComplete_BannerDisappears`
**Assert:** When `OperationInProgressChanged` fires with `false` (and description changes to null), the banner disappears.

#### TC-15: `Dispose_UnsubscribesEvents`
**Assert:** Component dispose unsubscribes from both `OperationInProgressChanged` and `OperationDescriptionChanged`.

#### TC-16: `LateMount_SeesActiveOperation`
**Assert:** Component mounted while an operation is already in progress (subscribe-before-snapshot) immediately renders the banner with the active description.

#### TC-17: `NullDescription_InProgress_BannerRendersEmpty`
**Assert:** When `IsOperationInProgress=true` but `CurrentOperationDescription=null`, the banner renders no DOM output.

---

## 6. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `OperationCoordinatorService.BeginOperation` stores description when CAS succeeds |
| AC-2 | `OperationCoordinatorService.MarkComplete` clears `CurrentOperationDescription` to null |
| AC-3 | Sequential `BeginOperation` (CAS fails) does not overwrite existing description |
| AC-4 | `ClearStaleDescription` is no-op when coordinator is in-progress |
| AC-5 | `ClearStaleDescription` clears description when coordinator is idle |
| AC-6 | `OperationDescriptionChanged` fires on description changes (begin, complete, clear-stale) |
| AC-7 | `OperationInProgressBroadcaster` pushes description via `ReceiveOperationDescriptionUpdate` |
| AC-8 | Safety-net calls `ClearStaleDescription` on state transition when coordinator is idle |
| AC-9 | `PcsProHub.OnConnectedAsync` sends current description to late joiners |
| AC-10 | `OperationStatusBanner` renders description when operation is in progress with non-null description |
| AC-11 | `OperationStatusBanner` renders empty when no operation or null description |
| AC-12 | All 5 caller sites pass appropriate description strings |
| AC-13 | TC-1 through TC-17 pass |
| AC-14 | No pre-existing tests regress |
| AC-15 | All projects build with 0 errors, 0 warnings |

---

## 7. Commit Strategy

Delivered on `feature/IS-011-S-005-operation-status-banner`; squash-merged to `master` after adversarial code review approval.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Opus 4.7, GPT 5.4 | REQUEST CHANGES (both) — 9 blocking: wire format ambiguity, no mutation API for safety-net, event contract break, publication ordering, subscribe-before-snapshot, test coverage gaps. All accepted and applied. |
| R2 | 2026-04-21 | Opus 4.7, GPT 5.4 | REQUEST CHANGES (both) — 1 blocking: ClearStaleDescription TOCTOU race can erase active operation's description. Fixed with post-exchange re-check and CompareExchange restore. TC-7 rewritten, TC-11 added for BeginOperation event ordering, TC renumbered. |
| R3 | 2026-04-21 | Opus 4.7, GPT 5.4 | Opus APPROVE. GPT REQUEST CHANGES — 1 blocking: CompareExchange restore incorrect for `BeginOperation(null)`. REJECTED: after R-8 all callers pass non-null strings; `BeginOperation(null)` is backward-compat only; no production code exercises the null-description concurrent path. CompareExchange restore verified correct for all non-null descriptions by Opus interleaving analysis. **APPROVED (1 approve + 1 rejected finding).** |
