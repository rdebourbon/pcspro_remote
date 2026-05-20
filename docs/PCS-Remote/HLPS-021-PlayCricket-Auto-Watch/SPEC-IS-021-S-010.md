# SPEC-IS-021-S-010 — Browser Push Notifications

| Field | Value |
|---|---|
| **Document** | SPEC-IS-021-S-010.md |
| **Step** | IS-021 S-010 |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-05-21 |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Branch** | `feature/IS-021-S-010-push-notifications` |

---

## 1. Context and Purpose

S-010 delivers browser push notifications for the auto-watch feature. When the auto-close countdown starts, the operator needs to be alerted even if the PCS Remote tab is not in focus. A secondary T-60 warning fires at the 60-second mark for operators who may have missed or dismissed the initial notification.

This step is intentionally last: it depends on all service contracts and the UI toggle (S-009), and it uses JavaScript interop — the highest-risk integration path in the codebase.

S-010 addresses G-3 (browser push notification), SC-5 (notification on countdown start and T-60 warning), C-3a (T-60 suppression when countdown ≤ 60s), and C-7 (graceful degradation on permission denial).

---

## 2. Scope

**In scope:**
- A new invisible Blazor component (`PlayCricketPushNotifications`) placed in `MainLayout.razor`.
- A lazy-loaded JavaScript module in `wwwroot` that wraps the browser `Notification` API — requesting permission and dispatching notifications.
- The component subscribes to `AutoWatchEnabledChanged`, `CountdownStarted`, and `AutoCloseT60Warning` on the watcher service.
- On first auto-watch enable per circuit, the component asks the JS module to request `Notification` permission.
- On `CountdownStarted`, the component asks the JS module to send a countdown-start notification.
- On `AutoCloseT60Warning`, the component asks the JS module to send a 60-second warning notification.
- Graceful degradation when permission is denied: JS module silently no-ops; component does not enter an error state.
- bUnit / MSTest tests for the Blazor component; the JS module itself is not unit-tested (browser API boundary).

**Out of scope:**
- Service Workers or Web Push Protocol (server-sent push to background tabs when page is closed). S-010 uses the synchronous `Notification` API, which works in a focused or background tab but not a closed tab.
- Any changes to `IPlayCricketWatcherService` or any service implementation — all required events already exist from S-004 and S-007.
- Any changes to the debug panel or countdown banner.

---

## 3. Behavioural Requirements

### Blazor Component

**R-1 — Placement and invisibility**
The component renders no HTML output. It is placed in `MainLayout.razor` alongside the other notification/banner components.

**R-2 — Permission request on first enable (including late-join)**
On component initialisation, the component subscribes to `AutoWatchEnabledChanged` first, then reads `WatcherService.IsEnabled`. If already `true` at mount time (late-join circuit), the one-time permission flow is triggered immediately. Otherwise, it is triggered when the first `AutoWatchEnabledChanged(true)` event fires. The permission flow is initiated at most once per component lifetime regardless of how many subsequent enable/disable cycles occur.

**R-3 — Countdown-start notification**
When `CountdownStarted` fires, the component asks the JS module to dispatch a browser notification informing the operator that the auto-close countdown has begun, including the total initial countdown duration.

**R-4 — T-60 warning notification**
When `AutoCloseT60Warning` fires, the component asks the JS module to dispatch a browser notification warning that 60 seconds remain before auto-close. The service guarantees this event is not raised when the countdown started at 60 seconds or less (C-3a), so the component does not need to suppress it independently.

**R-5 — Exception handling**
Event handlers and JS interop calls must not surface unhandled exceptions. The JS module guarantees it never throws for permission-denial or notification-not-created scenarios (R-9, R-10). At the component level, `JSDisconnectedException` and `ObjectDisposedException` from any JS interop call are caught and silently ignored — these are expected circuit-shutdown paths. The component does not need to catch `JSException` from notification calls because the module itself no-ops in all unsupported/denied states.

**R-6 — Disposal safety**
On component dispose, the component unsubscribes from `AutoWatchEnabledChanged`, `CountdownStarted`, and `AutoCloseT60Warning`. Event handlers check the disposed flag before issuing any JS calls. The component implements `IAsyncDisposable` and disposes the `IJSObjectReference` for the loaded module if it was loaded during the component's lifetime.

### JavaScript Module

**R-7 — Lazy loading**
The JS module is loaded on first use (not at page load). The module reference is obtained once and reused for all subsequent calls. Any of the three JS call paths (permission request, countdown-start notification, T-60 notification) may trigger the initial module load if it has not yet occurred. This ensures `CountdownStarted` and `AutoCloseT60Warning` can dispatch notifications even if `AutoWatchEnabledChanged` was never received (e.g., page load mid-countdown).

**R-8 — Permission request function**
The module exposes a function that calls `Notification.requestPermission()` if the current permission is not already `"granted"` or `"denied"`. The function is idempotent: calling it when permission is already `"granted"` is a no-op. If the `Notification` API is unavailable (unsupported browser, non-secure context, sandboxed frame), the function detects this and returns without throwing.

**R-9 — Notification dispatch function**
The module exposes a function that creates a `Notification` with a given title and body. If the current `Notification.permission` is not `"granted"`, or if the `Notification` API is unavailable (same conditions as R-8), the function does nothing — no throw, no error.

**R-10 — Feature detection**
Both module functions check for the availability of the `Notification` global before attempting any API call. Unavailability is treated as an implicit graceful-degradation path, equivalent to permission denied.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | On first `AutoWatchEnabledChanged(true)`, the JS module's permission function is invoked exactly once |
| AC-2 | On a second or subsequent `AutoWatchEnabledChanged(true)` in the same component lifetime, the permission function is not invoked again |
| AC-3 | `CountdownStarted` → JS notification function invoked with a message describing countdown start and initial duration |
| AC-4 | `AutoCloseT60Warning` → JS notification function invoked with a 60-second warning message |
| AC-5 | JS module no-ops silently on permission denial → no component error, no unhandled exception |
| AC-6 | `JSDisconnectedException` from any JS interop call is caught and ignored; component remains stable |
| AC-7 | Dispose unsubscribes from all three watcher events and disposes the `IJSObjectReference`; subsequent events cause no JS calls |
| AC-8 | Component mounts when `WatcherService.IsEnabled = true` (late-join) → permission flow triggered at mount, not waiting for an enable event |
| AC-9 | Component renders no visible HTML |
| AC-10 | All existing tests continue to pass |

---

## 5. Test Cases (9 TCs)

Tests must be written before production code (TDD). The Blazor component is tested using bUnit with a mock `IJSRuntime` (via bUnit's built-in JSInterop mock). The lazy-loaded module is mocked by setting up the expected module import on the bUnit JS interop handler. The JS module file itself is not unit-tested.

### TC-1 — First enable triggers permission request
Render the component with `IsEnabled = false`. Raise `AutoWatchEnabledChanged(true)`. The JS module's permission function is invoked exactly once.

### TC-2 — Second enable does not re-request permission
Render, raise `AutoWatchEnabledChanged(true)`, raise it again. The JS module's permission function is invoked exactly once total (not twice).

### TC-3 — CountdownStarted dispatches start notification
Raise `CountdownStarted`. The JS module's notification function is invoked with a message containing the countdown duration.

### TC-4 — AutoCloseT60Warning dispatches warning notification
Raise `AutoCloseT60Warning`. The JS module's notification function is invoked with a 60-second warning message.

### TC-5 — JS module silently no-ops on permission denial
Set up the JS interop so the permission function returns without effect (simulating denial). Raise `AutoWatchEnabledChanged(true)`. No unhandled exception is observed; the component remains rendered.

### TC-6 — JSDisconnectedException is silently caught
Set up the JS interop so the notification function throws `JSDisconnectedException`. Raise `CountdownStarted`. No unhandled exception is observed; the component remains in a stable state.

### TC-7 — Dispose cleans up: no JS calls after disposal
Render and then dispose the component. Raise `CountdownStarted`. Verify the JS notification function is not invoked after disposal. This also verifies effective event unsubscription.

### TC-8 — Late-join: already-enabled circuit requests permission at mount
Render the component with `WatcherService.IsEnabled = true` (simulating a circuit joining mid-session with auto-watch already active). Verify the JS module's permission function is invoked at mount without any `AutoWatchEnabledChanged` event firing.

### TC-9 — Component renders no HTML
Render the component. The rendered output contains no elements.

---

## 6. Incremental Commit Strategy

Commits should be small and logical:
- JS module file (`wwwroot/js/playCricketNotifications.js`)
- Tests for the Blazor component
- Production Blazor component
- `MainLayout.razor` update

---

## 7. Documentation Updates

- IS-021: Mark S-010 DELIVERED after squash-merge.

---

## 8. Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Spec | Opus 4.5 | 2×HIGH, 3×MEDIUM, 2×LOW | F1→R-6 IAsyncDisposable; F2→R-5 exception handling; F3→R-7 module lazy-load any path; F4→TC-5 clarified; F5→TC-6+TC-7 merged; F6/F7 LOW noted | REVISION REQUIRED | |
| R1 | Spec | GPT-5.4 | 3×HIGH, 1×MEDIUM | F1→R-2 late-join snapshot; F2→noted (Chrome 66+ no gesture req.); F3→R-5 contradiction resolved; F4→R-10 feature detection | REVISION REQUIRED | GPT-F2 noted but not actioned: Chrome 66+ dropped user-gesture requirement for requestPermission(); auto-watch enable is always triggered by user click |
| R2 | — | — | — | — | APPROVED v0.2 | R1 fixes applied |
