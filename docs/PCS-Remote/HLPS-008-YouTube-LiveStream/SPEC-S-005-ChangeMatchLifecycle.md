# SPEC-S-005: ChangeMatchButton Lifecycle Coupling

| Field | Value |
|---|---|
| **Document** | SPEC-S-005-ChangeMatchLifecycle.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-17 |
| **IS Step** | S-005 |
| **Governing HLPS** | HLPS-008-YouTube-LiveStream.md v0.4 (APPROVED) |
| **Governing IS** | IS-008-YouTube-LiveStream.md v0.3 (APPROVED) |
| **Branch** | `feature/IS-008-S-005-changematch-lifecycle` |

---

## 1. Objective

Update `ChangeMatchButton.razor` to detect an active YouTube live stream and (a) augment the confirmation dialog with a stream-active warning, (b) call `StopStreamAsync` before the match-change transition when confirmed. Additionally, wire PCS Pro `Error` state auto-stop: when PCS Pro enters `Error` state while a stream is `Live` or `Starting`, `StopStreamAsync` is called automatically to prevent orphaned broadcasts (S-YT-13).

---

## 2. Scope

### In Scope

1. **ChangeMatchButton.razor** — inject `IYouTubeLiveStreamService`; modify `OnChangeMatchClickedAsync` to:
   - Check `StreamService.CurrentStatus` before showing the confirmation dialog
   - If `CurrentStatus` is `Live` or `Starting`, append a warning line to the dialog message
   - On confirm with active stream: call `StopStreamAsync` (in its own try/catch) before `ClearCache`/`ChangeMatchAsync`

2. **PCS Pro Error auto-stop** — when `StateChanged` fires `PcsProState.Error` while `IYouTubeLiveStreamService.CurrentStatus` is `Live` or `Starting`, call `StopStreamAsync` automatically. Wired in `Index.razor`'s existing `OnStateChanged` handler (simplest location — already subscribes to `AutomationService.StateChanged`).

3. **bUnit tests** for both behaviours in `ChangeMatchButtonTests.cs` and `IndexTests.cs`.

### Out of Scope

- StreamingControls component changes (already delivered in S-004)
- Real YouTube implementation (S-006)
- Playwright E2E tests for this coupling (S-007)

### IS Deviation

- **Auto-stop location**: IS-008 S-005 says the exact wiring location is "a delivery-phase concern". This spec wires it in `Index.razor` because that component already subscribes to `AutomationService.StateChanged` and has access to DI. A dedicated `IHostedService` observer would provide zero-circuit coverage (no browser tabs open), but adds complexity disproportionate to S-005 scope. This is a known gap documented as a hardening candidate for future steps.
- **Predicate narrowing**: HLPS says "CurrentStatus != Idle" triggers the warning. Implementation narrows to `Live || Starting` only. Rationale: `Stopping` is already in-progress and `Error` is not an active broadcast — showing "stream is active" for these states is misleading. The HLPS governs the intent (protect against orphaned broadcasts); `Live || Starting` are the only states where an orphan is possible.
- **IS phrasing**: IS-008 says "when CurrentStatus == Live, dialog shows warning text". HLPS governs and uses the broader `!= Idle`. Implementation uses `Live || Starting` (see above).

---

## 3. Design

### 3.1 ChangeMatchButton Changes

**New injected dependency:** `IYouTubeLiveStreamService StreamService`

**Modified `OnChangeMatchClickedAsync`:**

```csharp
private async Task OnChangeMatchClickedAsync()
{
    if (_operationInProgress) return;
    if (ManualModeService.IsManualModeActive)
    {
        NotificationService.Notify(...);
        return;
    }

    var streamStatus = StreamService.CurrentStatus;
    var streamActive = streamStatus is LiveStreamStatus.Live or LiveStreamStatus.Starting;
    var dialogMessage = streamActive
        ? "Load a different match? PCS Pro will close and reopen to the match selection screen.\n\n⚠️ A live stream is active — it will be automatically stopped."
        : "Load a different match? PCS Pro will close and reopen to the match selection screen.";

    var confirmed = await ConfirmDialogService.ConfirmAsync(dialogMessage, "Change Match");
    if (confirmed != true) return;

    bool acquired = false;
    try
    {
        acquired = CoordinatorService.BeginOperation();
        if (!acquired) return;

        if (streamActive)
        {
            try { await StreamService.StopStreamAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "Failed to stop stream before match change");
            }
        }

        ScoreboardService.ClearCache();
        await AutomationService.ChangeMatchAsync();
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Logger.LogError(ex, "Change match operation failed");
        NotificationService.Notify(NotificationSeverity.Error, "Change match failed");
    }
    finally
    {
        if (acquired) CoordinatorService.MarkComplete();
    }
}
```

**Key design decisions:**
- `streamActive` checks `Live or Starting` only — `Stopping` and `Error` are not active broadcasts and `StopStreamAsync` has undefined/no-op behaviour for them per contract.
- `streamActive` is captured **before** the dialog to avoid TOCTOU — if the stream transitions while the dialog is open, the captured state governs (safer to stop a just-ended stream than to miss a still-running one).
- `StopStreamAsync` is wrapped in its own `try/catch` — failure to stop the stream must not prevent the match change from proceeding (AC-6).
- No separate `CancellationToken` for `StopStreamAsync` — the stop should always complete.

### 3.2 Index.razor Auto-Stop on PCS Pro Error

Add to the existing `OnStateChanged` handler:

```csharp
if (newState == PcsProState.Error)
{
    var streamStatus = StreamService.CurrentStatus;
    if (streamStatus == LiveStreamStatus.Live || streamStatus == LiveStreamStatus.Starting)
    {
        try { await StreamService.StopStreamAsync(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Failed to auto-stop stream on PCS Pro {State} entry", newState);
        }
    }
}
```

**Key design decisions:**
- Auto-stop is fire-and-forget (catch-and-log) — PCS Pro error state is already a degraded scenario; failing to stop the stream shouldn't compound the error.
- Uses `Logger` — `Index.razor` doesn't currently inject `ILogger`. Adding `ILogger<Index>` is a new injection.
- The auto-stop runs inside the `InvokeAsync` lambda already present in `OnStateChanged`, which serialises it with UI updates.

---

## 4. File Inventory

| Action | Path |
|---|---|
| Modify | `src/PcsRemote.Web/Shared/ChangeMatchButton.razor` — inject stream service, modify dialog |
| Modify | `src/PcsRemote.Web/Pages/Index.razor` — inject stream service + logger, add auto-stop |
| Modify | `tests/PcsRemote.Web.Tests/Shared/ChangeMatchButtonTests.cs` — add stream-related tests |
| Modify | `tests/PcsRemote.Web.Tests/Pages/IndexTests.cs` — add auto-stop tests |

---

## 5. Acceptance Criteria

| AC | Description | Test Method |
|---|---|---|
| AC-1 | When `CurrentStatus == Idle`, dialog message is unchanged (no warning) | `OnChangeMatchClickedAsync_StreamIdle_DialogHasNoWarning` |
| AC-2 | When `CurrentStatus == Live`, dialog includes stream warning text | `OnChangeMatchClickedAsync_StreamLive_DialogHasWarning` |
| AC-2b | When `CurrentStatus == Starting`, dialog includes stream warning text | `OnChangeMatchClickedAsync_StreamStarting_DialogHasWarning` |
| AC-2c | When `CurrentStatus == Error`, dialog message is unchanged (no warning) | `OnChangeMatchClickedAsync_StreamError_DialogHasNoWarning` |
| AC-3 | On confirm with active stream, `StopStreamAsync` called before `ChangeMatchAsync` | `OnChangeMatchClickedAsync_StreamLiveConfirmed_StopsThenChangesMatch` |
| AC-4 | On confirm with idle stream, `StopStreamAsync` NOT called | `OnChangeMatchClickedAsync_StreamIdleConfirmed_NoStopStream` |
| AC-5 | On cancel, `StopStreamAsync` NOT called regardless of stream status | `OnChangeMatchClickedAsync_CancelledWithLiveStream_NoStopStream` |
| AC-6 | `StopStreamAsync` failure does not prevent `ChangeMatchAsync` from proceeding | `OnChangeMatchClickedAsync_StopStreamFails_ChangeMatchStillProceeds` |
| AC-6b | `StopStreamAsync` failure is logged as Warning | `OnChangeMatchClickedAsync_StopStreamFails_LogsWarning` |
| AC-7 | PCS Pro Error state with `Live` stream → auto-stop called | `OnStateChanged_ErrorWithLiveStream_StopsStream` |
| AC-8 | PCS Pro Error state with `Starting` stream → auto-stop called | `OnStateChanged_ErrorWithStartingStream_StopsStream` |
| AC-9 | PCS Pro Error state with `Idle` stream → no auto-stop | `OnStateChanged_ErrorWithIdleStream_NoStopStream` |
| AC-10 | Auto-stop failure is logged as Warning, not thrown | `OnStateChanged_ErrorAutoStopFails_LogsWarningNoThrow` |
| AC-11 | Build succeeds with 0 errors, 0 warnings | (build verification) |
| AC-12 | All existing tests continue to pass | (test suite verification) |

---

## 6. Dependencies

| Dependency | Source | Status |
|---|---|---|
| `IYouTubeLiveStreamService` | S-001 | ✅ Delivered |
| `LiveStreamStatus` | S-001 | ✅ Delivered |
| `MockYouTubeLiveStreamService` DI wiring | S-004 | ✅ Delivered |
| `IOperationCoordinatorService` | IS-003 S-009 | ✅ Delivered |
| `ChangeMatchButton.razor` | IS-004 S-005 | ✅ Delivered |
| `Index.razor` | IS-003 S-002 | ✅ Delivered |

---

## 7. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| `StopStreamAsync` delays the match-change transition | LOW | Mock delay is configurable (default 500ms); acceptable for user experience |
| Auto-stop in Index.razor creates coupling between UI and streaming | LOW | Index already aggregates multiple service subscriptions; this is consistent |
| TOCTOU: stream status changes between dialog display and confirm | LOW | Captured before dialog; safer to stop a just-ended stream than miss a running one |

---

## 8. Traceability

| HLPS Requirement | Coverage |
|---|---|
| S-YT-13 (ChangeMatchButton lifecycle coupling) | AC-1 through AC-10 |
| HLPS Risk #3 (Live broadcast orphaned when PCS Pro leaves MatchLoaded) | AC-3, AC-6, AC-7, AC-8, AC-10 (partial — zero-circuit gap deferred) |
