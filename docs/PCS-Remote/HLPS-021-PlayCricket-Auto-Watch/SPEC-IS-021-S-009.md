# SPEC-IS-021-S-009 — Auto-Watch UI Toggle and Countdown Banner

| Field | Value |
|---|---|
| **Document** | SPEC-IS-021-S-009.md |
| **Step** | IS-021 S-009 |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-05-21 |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Branch** | `feature/IS-021-S-009-autowatch-ui` |

---

## 1. Context and Purpose

S-009 delivers the operator-facing UI for the auto-watch feature. It has two independent elements:

1. **Auto-watch toggle** in the debug panel — lets the operator enable or disable auto-watch without interacting with any other UI element.
2. **Countdown banner** in the main layout — displays the active auto-close countdown prominently with a cancel button, and updates in real time.

Both elements are intentionally delivered late in the sequence (after all service contracts have stabilised) to minimise churn. They address G-3 (visible countdown with cancel), G-5 (debug panel toggle), SC-2 (visible countdown), SC-4 (cancel button), and SC-7 (disabled on every restart).

---

## 2. Scope

**In scope:**
- A new toggle control added to `DebugSection.razor`, following the existing date-selection toggle pattern.
- A new `PlayCricketCountdownBanner` Blazor component, placed in `MainLayout.razor` adjacent to the existing `ManualModeBanner` and `OperationStatusBanner`.
- bUnit tests for both controls.
- Updates to `MainLayout.razor` to include the new banner.
- No changes to `IPlayCricketWatcherService` or any service implementation — all required interface members already exist from S-004.

**Out of scope:**
- Browser push notifications (S-010).
- CSS styling beyond assigning CSS class names consistent with the project naming convention.
- Any changes to the debug section's PIN or unlock logic.

---

## 3. Behavioural Requirements

### Auto-Watch Toggle

**R-1 — Placement and initial state**
The toggle is added inside the expanded-and-unlocked region of `DebugSection.razor`, immediately following the existing date-selection toggle. On mount, the toggle reads `IPlayCricketWatcherService.IsEnabled` and renders its initial state accordingly. SC-7 guarantees the service starts with `IsEnabled = false` on every application restart, so in production the toggle always renders off on first mount. In tests, `IsEnabled` may be seeded to any value to verify that the toggle correctly reflects whatever the service reports. The toggle must reflect the live enabled state without requiring a page reload.

**R-2 — Toggle interaction**
Clicking the toggle when auto-watch is disabled calls `Enable()` on the watcher service. Clicking it when enabled calls `Disable()`. The toggle does not call both in a single click cycle. The component does not track enabled state internally beyond what the service reports — it subscribes to the `AutoWatchEnabledChanged` event to refresh its display.

**R-3 — Event-driven state updates**
When `AutoWatchEnabledChanged` fires (from any source, e.g., the service reacting to its own internal logic), the toggle updates its rendered state without a component remount. This covers the case where the service state changes externally (e.g., auto-watch is disabled programmatically from the hosted service).

**R-4 — Disposal**
On component dispose, the component unsubscribes from `AutoWatchEnabledChanged`. No further `StateHasChanged` calls are made after disposal.

---

### Countdown Banner

**R-5 — Visibility condition**
The banner is hidden when no countdown is active (when `CountdownRemaining` returns `null` at mount time and no `CountdownStarted` event has been received). The banner is visible when a countdown is active.

**R-6 — Late-join**
When the component mounts while a countdown is already active (`CountdownRemaining` is non-null at the time of `OnInitialized`), the banner renders immediately with the current remaining time without waiting for the next `CountdownStarted` event. The timer begins immediately on mount in this case.

**R-7 — Real-time updates**
While the banner is visible, a component-owned timer fires every second and re-reads `CountdownRemaining` from the watcher service. The display reflects the freshly read value on each tick. The timer is started when the banner becomes visible (either on mount if a countdown is already active, or on `CountdownStarted`) and stopped when the banner becomes hidden.

**R-8 — Countdown expiry and cancellation**
When the timer reads `CountdownRemaining == null`, the banner hides immediately. Additionally, when `CountdownCancelled` or `CountdownExpired` events fire, the banner hides immediately without waiting for the next timer tick. The timer is stopped in all hide paths.

**R-9 — Cancel button**
The banner contains a cancel button that calls `CancelCountdown()` on the watcher service when clicked. The button is always visible while the banner is shown.

**R-10 — Display format**
The remaining time is displayed as `MM:SS` (minutes and seconds), using whole seconds truncated toward zero (e.g., `TimeSpan.FromSeconds(300)` → `05:00`; `TimeSpan.FromSeconds(59)` → `00:59`; values below one second display as `00:00`). The formatted value is re-computed on each timer tick from the latest `CountdownRemaining` value.

**R-11 — Disposal**
On component dispose, the timer is stopped (disposed) and the component unsubscribes from `CountdownStarted`, `CountdownCancelled`, and `CountdownExpired`. No further `StateHasChanged` calls are made after disposal.

**R-12 — Testability**
The banner component accepts an injected timer factory (`Func<IPeriodicTimer>`) so that tests can supply a `FakePeriodicTimer` without real clock dependencies. The production path uses a 1-second `RealPeriodicTimer`.

---

## 4. Acceptance Criteria

### Auto-Watch Toggle

| ID | Criterion |
|---|---|
| AC-1 | Toggle renders in its off/disabled visual state on mount when `IsEnabled = false` |
| AC-2 | Toggle renders in its on/enabled visual state on mount when `IsEnabled = true` |
| AC-3 | Clicking the toggle when disabled calls `Enable()` exactly once on the watcher service |
| AC-4 | Clicking the toggle when enabled calls `Disable()` exactly once on the watcher service |
| AC-5 | `AutoWatchEnabledChanged` (true) → toggle updates to enabled state without remount |
| AC-6 | `AutoWatchEnabledChanged` (false) → toggle updates to disabled state without remount |
| AC-7 | Dispose unsubscribes from `AutoWatchEnabledChanged`; subsequent event raises do not cause `StateHasChanged` or exceptions |

### Countdown Banner

| ID | Criterion |
|---|---|
| AC-8 | Banner is absent from the DOM on mount when `CountdownRemaining` is null |
| AC-9 | Banner is present and shows formatted time on mount when `CountdownRemaining` is non-null (late-join) |
| AC-10 | `CountdownStarted` event → banner appears with correctly formatted initial remaining time |
| AC-11 | Timer tick → banner updates the displayed remaining time |
| AC-12 | Timer tick when `CountdownRemaining` returns null → banner disappears |
| AC-13 | `CountdownCancelled` event → banner disappears immediately |
| AC-14 | `CountdownExpired` event → banner disappears immediately |
| AC-15 | Cancel button click → `CancelCountdown()` called on watcher service |
| AC-16 | Dispose stops the timer and unsubscribes from all three watcher events; subsequent event raises and timer ticks do not cause `StateHasChanged` or exceptions |

### Integration

| ID | Criterion |
|---|---|
| AC-17 | `PlayCricketCountdownBanner` is placed in `MainLayout.razor` near the other banner components |
| AC-18 | All existing tests continue to pass |

---

## 5. Test Cases

Tests must be written before production code (TDD). All component tests use bUnit. Tests use a `Mock<IPlayCricketWatcherService>` and, for countdown banner tests, a `FakePeriodicTimer` injected via the factory.

### TC-1 — Toggle disabled on mount
Render `DebugSection` with `IPlayCricketWatcherService.IsEnabled = false`. The toggle renders with its off-state CSS class. `Enable()` is never called.

### TC-2 — Toggle enabled on mount
Render `DebugSection` with `IsEnabled = true`. The toggle renders with its on-state CSS class.

### TC-3 — Toggle click when disabled calls Enable
Render with `IsEnabled = false`. Click the auto-watch toggle. `Enable()` is called exactly once.

### TC-4 — Toggle click when enabled calls Disable
Render with `IsEnabled = true`. Click the auto-watch toggle. `Disable()` is called exactly once.

### TC-5 — AutoWatchEnabledChanged true updates toggle
Render with `IsEnabled = false`. Raise `AutoWatchEnabledChanged` with `IsEnabled = true`. The toggle updates to its on-state CSS class without remounting.

### TC-6 — AutoWatchEnabledChanged false updates toggle
Render with `IsEnabled = true`. Raise `AutoWatchEnabledChanged` with `IsEnabled = false`. The toggle updates to its off-state CSS class without remounting.

### TC-7 — Dispose unsubscribes from AutoWatchEnabledChanged
Render and dispose `DebugSection`. Verify `AutoWatchEnabledChanged` was unsubscribed.

### TC-8 — Banner hidden on mount with no active countdown
Mount `PlayCricketCountdownBanner` with `CountdownRemaining = null`. The banner element is absent from the DOM.

### TC-9 — Banner visible on mount when countdown already active (late-join)
Mount with `CountdownRemaining = TimeSpan.FromMinutes(5)`. Banner is present immediately with a formatted time string.

### TC-10 — CountdownStarted event shows banner
Mount with `CountdownRemaining = null`. Raise `CountdownStarted`. Banner appears with the remaining time from `CountdownRemaining` (now non-null).

### TC-11 — Timer tick updates display
Mount or trigger a countdown so banner is visible. Trigger a fake timer tick. The displayed remaining time updates to reflect the new `CountdownRemaining` value.

### TC-12 — Timer tick with null CountdownRemaining hides banner
Banner is visible. Set `CountdownRemaining` to return null. Trigger a fake timer tick. Banner disappears.

### TC-13 — CountdownCancelled hides banner immediately
Banner is visible. Raise `CountdownCancelled`. Banner disappears without waiting for a timer tick.

### TC-14 — CountdownExpired hides banner immediately
Banner is visible. Raise `CountdownExpired`. Banner disappears without waiting for a timer tick.

### TC-15 — Cancel button calls CancelCountdown
Banner is visible. Click the cancel button. `CancelCountdown()` is called exactly once.

### TC-16 — Dispose stops timer and unsubscribes
Render the banner component and dispose it. Verify the `FakePeriodicTimer` is disposed (via `WhenDisposed`) and that all three watcher events are unsubscribed.

---

## 6. Incremental Commit Strategy

Commits should be small and logical:
- Tests for `DebugSection` auto-watch toggle, then the production toggle code
- Tests for `PlayCricketCountdownBanner`, then the production component
- `MainLayout.razor` update (place the banner)

---

## 7. Documentation Updates

- IS-021: Mark S-009 DELIVERED after squash-merge.

---

## 8. Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 2 | Claude Opus 4.5 + GPT-5.4 | Opus: 3×LOW; GPT: 2×MEDIUM, 1×LOW | All accepted and fixed | REVISION REQUIRED | R-1 clarified (live-state vs SC-7 default); R-10 truncation rule added; AC-7/AC-16 post-disposal safety made explicit |
