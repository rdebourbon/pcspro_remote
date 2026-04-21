# SPEC-S-003: Login Automation

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-Login-Automation.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-22 |
| **Step ID** | S-003 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-003-login-automation` |

---

## 1. Purpose

Replace the `FlaUiLoginAutomation` stub with a working implementation that enters the password, clicks submit, and detects login dialogs, match selection visibility, and unexpected dialogs. This also establishes the shared FlaUI window-access pattern used by all subsequent FlaUi\* classes (S-004 through S-007).

Refer to `tools/AutomationDiagnostic/DiagnosticRunner.cs` (`Step2_FindLoginElements`, `Step3_SubmitCredentials`) for the proven patterns.

---

## 2. Scope

### 2.1 New Class: `PcsProWindowLocator`

**Location:** `src/PcsRemote.Automation/PcsProWindowLocator.cs`

The FlaUi\* stub classes currently have no mechanism to access the PCS Pro main window. All five classes need this capability. This step establishes the shared pattern.

**Requirements:**

- R1: Create `internal sealed class PcsProWindowLocator : IDisposable` in `PcsRemote.Automation`.
- R2: Owns a single `UIA3Automation` instance (created in constructor). Exposes it via a read-only property for FlaUi\* classes that need the `ConditionFactory`.
- R3: Provides a `FindMainWindow()` method that finds the PCS Pro process via `Process.GetProcessesByName("cricket")`, attaches via `FlaUI.Core.Application.Attach`, and returns the main window. Returns `null` if the process is not found **or** if `GetMainWindow()` returns `null` (process starting up, no visible window yet). Must dispose all `Process` objects returned by `GetProcessesByName` after extracting the PID to avoid handle leaks under repeated polling. Must not throw — wrap `Application.Attach` and `GetMainWindow` in a try/catch returning `null` on any failure.
- R4: Per HLPS element lifetime rules, `FindMainWindow()` must re-find the process and window on each call — no caching of `Window` or `AutomationElement` references.
- R5: Register as singleton in DI via `AutomationServiceCollectionExtensions`.
- R6: Implements `IDisposable` to dispose the `UIA3Automation` instance at shutdown.

### 2.2 Modified Class: `FlaUiLoginAutomation`

**Requirements:**

- R7: Constructor accepts `PcsProWindowLocator` and `ILogger<FlaUiLoginAutomation>` (both injected via DI). This establishes the logging pattern for all subsequent FlaUi\* implementations.
- R8: Remove the two local `TODO_REPLACE_ON_GARAGE_PC` constants — use `KnownElements` constants instead.
- R9: Remove the TODO comment block referencing `Application.Attach`.
- R10: **`IsLoginDialogVisible()`** — Use `FindMainWindow()` then search for all child elements with `ControlType.Window` via `FindAllDescendants`. Identify the login dialog as a child Window that contains a descendant with `AutomationId = KnownElements.LoginPasswordFieldAutomationId`. This discriminates it from the match selection dialog, which shares `AutomationId = "window"`. Return `true` if found, `false` otherwise (including when process is not running). Must not throw — wrap all FlaUI calls in try/catch, return `false` on any exception (including `COMException`, `ElementNotAvailableException`).
- R11: **`EnterPassword(string password)`** — Find the password field by `KnownElements.LoginPasswordFieldAutomationId`, focus it, pause briefly (~200ms per diagnostic tool pattern) to allow WPF message pump synchronization, select-all (Ctrl+A), then type the password via `FlaUI.Core.Input.Keyboard`. Include a brief pause (~300ms) after typing before returning, to allow keystroke delivery to complete. Throw if the element cannot be found. The PasswordBox may not support ValuePattern — keyboard simulation is the proven approach from the diagnostic tool.
- R12: **`ClickSubmit()`** — Find the submit button by `KnownElements.LoginSubmitButtonAutomationId` and invoke it using `UIAutomationHelpers.InvokeButtonSafely`. Throw if the element cannot be found or invocation returns an error.
- R13: **`IsMatchSelectionVisible()`** — Search the main window for a child `Window` with `Name = KnownElements.MatchSelectionDialogName`. Return `true` if found. Must not throw — wrap all FlaUI calls in try/catch, return `false` on any exception.
- R14: **`IsUnexpectedDialogPresent()`** — Search for all child elements with `ControlType.Window` via `FindAllDescendants`. Exclude any Window with `Name = KnownElements.MatchSelectionDialogName` (match selection). Exclude any Window that contains a descendant with `AutomationId = KnownElements.LoginPasswordFieldAutomationId` (login dialog). If any remaining child Windows exist, return `true`. Must not throw — wrap all FlaUI calls in try/catch, return `false` on any exception.
- R15: **`TryCloseUnexpectedDialog()`** — If an unexpected dialog is found (per R14 classification), attempt to close it by searching for buttons in order: `FindButtonByChildText("Cancel")` first, then `"Close"`, then `"OK"`. If none found, fall back to any button via `FindDescendant` with `ControlType.Button`. Invoke using `InvokeButtonSafely`. Best-effort, no throw.

### 2.3 Out of Scope

- Modifying any other FlaUi\* class to use `PcsProWindowLocator` — deferred to S-004+.
- Login failure detection (error text search in dialog) — the current `ILoginAutomation` interface does not expose this; the `PcsProAutomationService` handles failure via timeout.
- Username verification or Switch User — not in `ILoginAutomation` interface.

---

## 3. Test Strategy

- **T1: Build verification** — 0 warnings, 0 errors.
- **T2: Existing tests remain green** — No behavioural changes to service-layer tests (they use `FakeLoginAutomation`).
- **T3: No unit tests for FlaUiLoginAutomation** — FlaUI interactions require a live PCS Pro instance. Verified via garage PC end-to-end walkthrough (H-SC-3).
- **T4: FlaUiLoginAutomation no longer throws NotImplementedException** — All methods have real implementations (verified by code review).

---

## 4. Acceptance Criteria

- AC-1: `PcsProWindowLocator.cs` exists with `FindMainWindow()` returning `Window?` and a read-only `UIA3Automation` property.
- AC-2: `PcsProWindowLocator` is registered as singleton in `AutomationServiceCollectionExtensions`.
- AC-3: `FlaUiLoginAutomation` constructor accepts `PcsProWindowLocator` and `ILogger<FlaUiLoginAutomation>`.
- AC-4: Zero `TODO_REPLACE_ON_GARAGE_PC` constants remain in `FlaUiLoginAutomation.cs`.
- AC-5: All six `ILoginAutomation` methods are implemented (no `NotImplementedException`).
- AC-6: `EnterPassword` uses keyboard simulation (Ctrl+A, type) with inter-operation delays per diagnostic tool pattern.
- AC-7: `IsLoginDialogVisible` and `IsMatchSelectionVisible` return `false` (not throw) when the process is not running or on any FlaUI/COM exception.
- AC-8: `IsUnexpectedDialogPresent` detects windows other than login (identified by password field descendant) and match selection (identified by Name) dialogs.
- AC-9: Build: 0 warnings, 0 errors. All existing tests pass.

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | `Process.GetProcessesByName` per call may be slow under heavy polling | PcsProAutomationService polls at 200ms intervals — process lookup is sub-millisecond |
| R-2 | UIA3Automation singleton may have COM threading issues | All FlaUi* calls are serialized via the service's operation lock — serialized access prevents concurrent COM calls. FlaUI's UIA3 COM interface supports cross-thread access when serialized. |
| R-3 | Keyboard.Type may conflict with user input | PCS Pro runs headless on garage PC — no user keyboard input expected |
