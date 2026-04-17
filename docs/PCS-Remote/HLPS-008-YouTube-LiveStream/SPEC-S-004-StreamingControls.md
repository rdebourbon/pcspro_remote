# SPEC-S-004: StreamingControls.razor, DI Wiring, and Configuration Schema

| Field | Value |
|---|---|
| **Document** | SPEC-S-004-StreamingControls.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.1 |
| **Date** | 2026-04-17 |
| **IS Step** | S-004 |
| **Governing HLPS** | HLPS-008-YouTube-LiveStream.md v0.4 (APPROVED) |
| **Governing IS** | IS-008-YouTube-LiveStream.md v0.3 (APPROVED) |
| **Branch** | `feature/IS-008-S-004-streaming-controls` |

---

## 1. Objective

Create the `StreamingControls.razor` Blazor component, wire `IYouTubeLiveStreamService` → `MockYouTubeLiveStreamService` in DI, add the YouTube configuration schema to `appsettings.json` / `appsettings.Development.json`, and register `BroadcastTitleRenderer` in DI. This step delivers the primary user-facing streaming UI — Start, Stop, Cancel, Dismiss, status badge, and watch URL link — all testable against the mock service.

---

## 2. Scope

### In Scope

1. **`StreamingControls.razor`** — located at `PcsRemote.Web/Shared/StreamingControls.razor` (consistent with existing component location pattern). Implements the full UI defined in HLPS-008 §5.8.

2. **DI registration** — `WebApplicationBuilderExtensions.AddPcsRemoteServices` gains YouTube service wiring:
   - `BroadcastTitleRenderer` registered as singleton
   - `IYouTubeLiveStreamService` → `MockYouTubeLiveStreamService` (hardcoded mock for now — S-006 adds the `UseMock` conditional branch)
   - `MockYouTubeOptions` bound from `YouTube:Mock` config section via `IOptions<MockYouTubeOptions>`

3. **Configuration schema** — `appsettings.json` gains `YouTube` section per HLPS-008 §5.3. `appsettings.Development.json` gains `YouTube:UseMock: true` and `YouTube:Mock` section with development-friendly delays.

4. **`Index.razor` update** — add `<StreamingControls />` in the `MatchLoaded` block, after `ChangeMatchButton`.

5. **CSS** — streaming-specific styles added to `app.css` using existing CSS variables.

6. **bUnit tests** — `StreamingControlsTests.cs` in `PcsRemote.Web.Tests`.

### Out of Scope

- Real YouTube implementation (S-006)
- `UseMock` conditional branching in DI (S-006)
- ChangeMatchButton lifecycle coupling (S-005)
- YouTubeStreamException handling (S-006 — mock doesn't throw it)

### IS Deviation

- **DI wiring simplified**: IS-008 S-004 describes the full `UseMock` conditional branch. This spec hardcodes mock registration only — the `YouTubeLiveStreamService` class doesn't exist yet. S-006 will add the conditional.
- **Component path**: HLPS-008 §5.8 and IS-008 specify `PcsRemote.Web/Components/Streaming/`. All existing components (`ChangeMatchButton`, `ScoreboardPreview`, `RefreshScoreboardButton`, etc.) live in `PcsRemote.Web/Shared/`. This spec follows the established project convention.
- **RadzenCard**: HLPS-008 §5.8 specifies a `RadzenCard` wrapper. No existing component uses RadzenCard — all use custom HTML + CSS with project design tokens. This spec follows the established pattern.
- **S-YT-12 deferred**: IS-008 assigns partial S-YT-12 to S-004 (bUnit test for `YouTubeStreamException`). The `YouTubeStreamException` class does not exist yet — it is created in S-006. Deferred to S-006.
- **Configuration subset**: HLPS-008 §5.3 defines 8 keys. This step adds only the mock-relevant subset (`UseMock`, `BroadcastTitleTemplate`, `Mock.*`). Production keys (`ClientId`, `ClientSecret`, `LiveStreamId`, etc.) are added in S-006 when the real implementation exists.

---

## 3. Design

### 3.1 StreamingControls.razor Component

**Injected dependencies:** `IYouTubeLiveStreamService`, `IOperationCoordinatorService`

**Lifecycle:**
- Implements `IDisposable`
- `OnInitialized()` subscribes to `IYouTubeLiveStreamService.StatusChanged` and `IOperationCoordinatorService.OperationInProgressChanged`
- Reads initial `CurrentStatus`, `CurrentBroadcast`, and `IsOperationInProgress` after subscribing
- `Dispose()` sets `_disposed = true` then unsubscribes from both events, disposes `_startCts`

**State fields:**
- `_currentStatus` (LiveStreamStatus) — mirrors service
- `_currentBroadcast` (LiveBroadcastInfo?) — mirrors service
- `_errorMessage` (string?) — from StatusChanged snapshot
- `_operationInProgress` (bool) — from coordinator
- `_disposed` (bool) — post-dispose guard
- `_startCts` (CancellationTokenSource?) — for cancel during Starting

**UI Elements:**

| Element | Render condition | Enabled condition |
|---|---|---|
| Status badge (text + colour) | Always | N/A |
| "▶ Start Stream" button | Always | `_currentStatus == Idle && !_operationInProgress` |
| "Cancel" button | `_currentStatus == Starting` | Always when visible |
| "⏹ Stop Stream" button | Always | `_currentStatus == Live && !_operationInProgress` |
| Watch URL link | `_currentStatus == Live && _currentBroadcast != null` | N/A |
| Error message + "Dismiss" button | `_currentStatus == Error` | Always when visible |

**Status badge colours** (using CSS variables):
- Idle: `var(--pcs-grey)`
- Starting: `var(--pcs-yellow)`
- Live: `var(--pcs-green)` (gold accent)
- Stopping: `var(--pcs-yellow)`
- Error: `var(--pcs-red)`

**Operation flow:**
- **Start**: `BeginOperation()` → create `_startCts` → `StartStreamAsync(_startCts.Token)` → `MarkComplete()` in finally
- **Stop**: `BeginOperation()` → `StopStreamAsync()` → `MarkComplete()` in finally
- **Cancel**: `_startCts?.Cancel()` — no coordinator involvement (the in-flight Start handles cleanup)
- **Dismiss**: `ResetAsync()` — no coordinator involvement

### 3.2 StatusChanged Handler

```csharp
private async void OnStatusChanged(object? sender, StreamStateSnapshot snapshot)
{
    if (_disposed) return;
    try
    {
        await InvokeAsync(() =>
        {
            _currentStatus = snapshot.Status;
            _currentBroadcast = snapshot.CurrentBroadcast;
            _errorMessage = snapshot.ErrorMessage;
            StateHasChanged();
        });
    }
    catch (ObjectDisposedException) { }
}
```

Both event handlers use the `_disposed` guard + `try/catch (ObjectDisposedException)` pattern established by `ChangeMatchButton.razor`.

### 3.3 DI Registration

In `WebApplicationBuilderExtensions.AddPcsRemoteServices`:

```csharp
builder.Services.AddSingleton<BroadcastTitleRenderer>();
builder.Services.Configure<MockYouTubeOptions>(
    builder.Configuration.GetSection("YouTube:Mock"));
builder.Services.AddSingleton<IYouTubeLiveStreamService, MockYouTubeLiveStreamService>();
```

### 3.4 Configuration Schema

**appsettings.json** — add after `Scoreboard` section:
```json
"YouTube": {
    "UseMock": false,
    "BroadcastTitleTemplate": "{HomeTeam} vs {AwayTeam}"
}
```

**appsettings.Development.json** — add:
```json
"YouTube": {
    "UseMock": true,
    "Mock": {
        "StartDelayMs": 1500,
        "StopDelayMs": 500,
        "SimulateStartFailure": false,
        "SimulateActiveOnStartup": false
    }
}
```

---

## 4. File Inventory

| Action | Path |
|---|---|
| Create | `src/PcsRemote.Web/Shared/StreamingControls.razor` |
| Create | `tests/PcsRemote.Web.Tests/StreamingControlsTests.cs` |
| Modify | `src/PcsRemote.Web/WebApplicationBuilderExtensions.cs` |
| Modify | `src/PcsRemote.Web/PcsRemote.Web.csproj` — add `ProjectReference` for `PcsRemote.YouTube.Mock` |
| Modify | `src/PcsRemote.Web/Pages/Index.razor` |
| Modify | `src/PcsRemote.Web/wwwroot/css/app.css` |
| Modify | `src/PcsRemote.Web/appsettings.json` |
| Modify | `src/PcsRemote.Web/appsettings.Development.json` |

---

## 5. Acceptance Criteria

| AC | Description | Test Method |
|---|---|---|
| AC-1 | `StreamingControls` renders when `PcsProState == MatchLoaded` (via Index.razor) | (verified by existing Index integration — not directly testable in isolation) |
| AC-2 | Status badge shows correct text and CSS class for each `LiveStreamStatus` | `StatusBadge_EachStatus_ShowsCorrectClassAndText` |
| AC-3 | Start button enabled only when `Idle` and no operation in progress | `StartButton_IdleAndNoOperation_IsEnabled` |
| AC-4 | Start button disabled when not `Idle` or operation in progress | `StartButton_NotIdleOrOperationInProgress_IsDisabled` |
| AC-5 | Stop button enabled only when `Live` and no operation in progress | `StopButton_LiveAndNoOperation_IsEnabled` |
| AC-6 | Stop button disabled when not `Live` or operation in progress | `StopButton_NotLiveOrOperationInProgress_IsDisabled` |
| AC-7 | Cancel button visible only during `Starting` | `CancelButton_Starting_IsVisible` |
| AC-8 | Cancel button not visible when not `Starting` | `CancelButton_NotStarting_IsNotVisible` |
| AC-9 | Error message and Dismiss button visible when `Error` | `ErrorArea_WhenError_ShowsMessageAndDismiss` |
| AC-10 | Dismiss calls `ResetAsync` and status returns to `Idle` | `DismissButton_Click_CallsResetAsync` |
| AC-11 | Watch URL link visible when `Live` with non-null broadcast | `WatchLink_WhenLive_IsVisible` |
| AC-12 | Watch URL link not visible when not `Live` | `WatchLink_WhenNotLive_IsNotVisible` |
| AC-13 | `StatusChanged` event updates component | `StatusChanged_FiresUpdate_ComponentReRenders` |
| AC-14 | Component disposes cleanly — no post-dispose StateHasChanged | `Dispose_UnsubscribesFromStatusChanged` |
| AC-15 | Start click calls `BeginOperation` and `StartStreamAsync` | `StartButton_Click_CallsBeginOperationAndStartStream` |
| AC-16 | Stop click calls `BeginOperation` and `StopStreamAsync` | `StopButton_Click_CallsBeginOperationAndStopStream` |
| AC-17 | DI resolves `IYouTubeLiveStreamService` as `MockYouTubeLiveStreamService` | (integration — verified by app startup) |
| AC-18 | Configuration schema present in `appsettings.json` and `appsettings.Development.json` | (file inspection) |
| AC-19 | Build succeeds with 0 errors, 0 warnings | (build verification) |
| AC-20 | All existing tests continue to pass | (test suite verification) |

---

## 6. Dependencies

| Dependency | Source | Status |
|---|---|---|
| `IYouTubeLiveStreamService` | S-001 | ✅ Delivered |
| `LiveStreamStatus`, `LiveBroadcastInfo`, `StreamStateSnapshot` | S-001 | ✅ Delivered |
| `BroadcastTitleRenderer` | S-002 | ✅ Delivered |
| `MockYouTubeLiveStreamService` + `MockYouTubeOptions` | S-003 | ✅ Delivered |
| `IOperationCoordinatorService` | IS-003 S-009 | ✅ Delivered |

---

## 7. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| bUnit tests require mock setup for multiple services | LOW | Follow established patterns from ChangeMatchButton tests |
| Component CSS may conflict with existing Radzen overrides | LOW | Use BEM-style class names scoped to `streaming-controls` |

---

## 8. Traceability

| HLPS Requirement | Coverage |
|---|---|
| S-YT-1 (conditional rendering) | AC-1 |
| S-YT-4 (button states) | AC-3 through AC-8 |
| S-YT-5 (error recovery) | AC-9, AC-10 (partial — Dismiss/Reset path) |
| S-YT-7 (StatusChanged) | AC-13 |
| S-YT-8 (mock implementation) | AC-17 (partial — DI wiring) |
| S-YT-14 (dispose) | AC-14 |
| S-YT-16 (maroon/gold theming) | CSS styles in app.css use project design tokens |
