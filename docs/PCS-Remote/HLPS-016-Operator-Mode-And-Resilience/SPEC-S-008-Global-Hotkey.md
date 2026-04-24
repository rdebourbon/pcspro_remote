# SPEC-S-008: Global Ctrl+Alt+P Hotkey via TrayHost

| Field | Value |
|---|---|
| **Document** | SPEC-S-008-Global-Hotkey.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-24 |
| **Step ID** | S-008 |
| **Governing HLPS** | HLPS-016-Operator-Mode-And-Resilience.md (DRAFT v0.1) |
| **Governing IS** | IS-016-Operator-Mode-And-Resilience.md (DRAFT v0.1) |
| **Branch** | `feature/016-S-008-global-hotkey` |

---

## 1. Purpose

This step registers a global Ctrl+Alt+P hotkey via Win32 `RegisterHotKey` in `PcsRemote.TrayHost`. The hotkey rides the existing WinForms STA message loop managed by `WinFormsHostedService`. On receipt, it calls `IManualModeService.Toggle()` to pause/resume automation. A tray balloon notification confirms each toggle.

---

## 2. Scope

### 2.1 Hidden `NativeWindow` — `HotkeyWindow`

**Requirements:**

- R1: Create a new `NativeWindow` subclass (e.g., `HotkeyWindow`) in `PcsRemote.TrayHost`. The class is `internal sealed`.
- R2: The constructor accepts `IManualModeService` and a logger. On construction (which must occur on the STA thread), the window handle is created via `CreateHandle(new CreateParams())` and `RegisterHotKey` is called with:
  - `hWnd` = this window's `Handle`
  - `id` = a constant integer (e.g., `1`)
  - `fsModifiers` = `MOD_CONTROL | MOD_ALT` (values: `0x0002 | 0x0001 = 0x0003`)
  - `vk` = `0x50` (VK_P)
- R3: Override `WndProc`. When `msg.Msg == WM_HOTKEY` (value `0x0312`) and `msg.WParam == id`, call `IManualModeService.Toggle()`.
- R4: If `RegisterHotKey` returns `false` (hotkey already registered by another application), log a `Warning` with the Win32 error code (`Marshal.GetLastWin32Error()`). The application continues without the hotkey — the tray menu remains the only toggle method. Do not throw.
- R5: Implement `IDisposable`. On disposal, call `UnregisterHotKey(Handle, id)` and `DestroyHandle()`. Disposal must be safe to call multiple times (guard against double-dispose).

### 2.2 `TrayApplicationContext` Integration

**Requirements:**

- R6: `TrayApplicationContext` creates the `HotkeyWindow` instance during construction (on the STA thread, after the invoker control is created). It receives `IManualModeService` via its existing constructor parameter.
- R7: `TrayApplicationContext.Dispose` disposes `HotkeyWindow` before the `NotifyIcon` (hotkey must be unregistered before the window message loop exits).
- R8: The existing `ManualModeChanged` handler in `TrayApplicationContext` is extended to show a tray balloon on every toggle:
  - Mode ON: `ShowBalloonTip(3000, "PCS Remote", "Manual Mode ON — automation paused", ToolTipIcon.Info)`
  - Mode OFF: `ShowBalloonTip(3000, "PCS Remote", "Manual Mode OFF — automation resumed", ToolTipIcon.Info)`
  - The balloon is in addition to the existing menu item text and icon update.

### 2.3 Win32 P/Invoke Declarations

**Requirements:**

- R9: P/Invoke declarations for `RegisterHotKey`, `UnregisterHotKey`, and `WM_HOTKEY` / `MOD_CONTROL` / `MOD_ALT` / `VK_P` constants can be placed in the `HotkeyWindow` class itself (private static) or in a shared interop class. The delivery phase decides based on reuse needs.
- R10: P/Invoke declarations must use `[DllImport("user32.dll", SetLastError = true)]` to ensure `Marshal.GetLastWin32Error()` is valid after the call.

### 2.4 Out of Scope

- The `Toggle()` implementation on `IManualModeService` (delivered in S-001).
- Service-layer wiring (S-006).
- UI changes (S-007).
- Making the hotkey combination configurable (Ctrl+Alt+P is hardcoded per user decision).

---

## 3. Test Strategy

### 3.1 Unit Tests

- **T1 — `HotkeyWindow` construction does not throw:** Create a `HotkeyWindow` with mocked `IManualModeService`. Assert no exception. (Note: `RegisterHotKey` will fail in a non-STA or non-interactive test runner — T1 tests the constructor logic around the failure path.)
- **T2 — `RegisterHotKey` failure logs warning:** Mock the P/Invoke to return `false`. Assert a warning is logged containing the error code.
- **T3 — Dispose is idempotent:** Call `Dispose()` twice. Assert no exception.

### 3.2 Integration / Manual Tests

- **T4 — Hotkey toggles manual mode:** On the garage PC, press Ctrl+Alt+P. Assert manual mode toggles (balloon appears, banner appears/clears, scoreboard polling pauses/resumes).
- **T5 — Hotkey conflict:** Temporarily register Ctrl+Alt+P with another application. Start PCS Remote. Assert a warning is logged and the tray menu still works.
- **T6 — Application exit cleans up:** Exit PCS Remote. Assert Ctrl+Alt+P is no longer registered (another application can register it).

### 3.3 Limitations

The `RegisterHotKey` API requires an interactive desktop session with a Win32 message loop. Automated unit tests cannot fully test the hotkey registration path without an STA thread and real message pump. The test strategy relies on:
- Testing the failure/logging paths (T1–T3) in automated unit tests.
- Testing the success path (T4–T6) via manual verification on the garage PC.

---

## 4. Acceptance Criteria

| ID | Criterion | Verification |
|----|-----------|-------------|
| AC-1 | Ctrl+Alt+P toggles manual mode when PCS Remote is running | T4 (manual) |
| AC-2 | Tray balloon shows "Manual Mode ON" / "Manual Mode OFF" on toggle | T4 (manual) |
| AC-3 | `RegisterHotKey` failure logs warning, app continues | T2 (unit) + T5 (manual) |
| AC-4 | `HotkeyWindow` disposal calls `UnregisterHotKey` | Code review + T3 (unit) |
| AC-5 | `HotkeyWindow` created on STA thread (same thread as `TrayApplicationContext`) | Code review |
| AC-6 | No test regressions | Full test run |

---

## 5. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Another application registers Ctrl+Alt+P, blocking our registration | Low | Low | R4 handles gracefully — warning logged, tray menu remains available. |
| `WM_HOTKEY` arrives on a non-STA thread | Very Low | Medium | `NativeWindow.WndProc` always runs on the thread that created the handle. Since `HotkeyWindow` is created on the STA thread, dispatch is guaranteed STA. |
| `Toggle()` throws from `WndProc` | Low | Medium | `Toggle()` is designed to never throw (CAS-based). Even if it did, the WPF message loop exception handling would catch it. Consider a try-catch in `WndProc` as a safety net. |
| Hotkey may interfere with PCS Pro's own keyboard shortcuts | Low | Low | Ctrl+Alt+P is not a standard PCS Pro shortcut. Can be changed in a future HLPS if conflict is discovered. |

---

## 6. Documentation Updates

- No external documentation changes for this step. Operational Guide update is in S-009.

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | DRAFT — not yet submitted for review |
