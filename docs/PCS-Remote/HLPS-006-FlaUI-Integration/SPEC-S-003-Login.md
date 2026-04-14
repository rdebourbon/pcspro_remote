# SPEC-S-003: Login Automation — Password Entry, Submit, and MatchSelection Transition

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-Login.md |
| **Status** | DRAFT |
| **Version** | 0.2 |
| **Date** | 2026-04-13 |
| **Governing IS** | IS-006-FlaUI-Integration.md v0.4 §S-003 |
| **Step** | IS-006 S-003 |

---

## 1. Problem Statement

After S-002, `LaunchAndLoginAsync` starts cricket.exe, detects the main window, fires `LoginDetected` (→ `LoginScreen`), starts the crash watcher, and returns. The service rests in `LoginScreen` — the login dialog is on screen but no credentials have been entered.

This step extends `LaunchAndLoginAsync` with the login phase: detect the login dialog, locate the password field and submit button, enter the configured password, click submit, and wait for the match selection dialog to appear. On success the service fires `CredentialsEntered` (→ `MatchSelection`) and `LaunchAndLoginAsync` returns in `MatchSelection` state. On timeout or unexpected dialog the service fires `Timeout` (→ `Error`) with a descriptive reason.

After this step, `LaunchAndLoginAsync` is fully complete — no further phases remain in it. The service in `MatchSelection` state is ready for S-004 (match selection automation).

---

## 2. Scope

**In scope:**
- The login phase of `LaunchAndLoginAsync`, added after the crash watcher is started (the crash watcher continues to run throughout the login phase).
- Detection of the login dialog, password field, and submit button by their AutomationId values (I-U-1, discovered on the garage PC during implementation).
- Password entry from `PcsProOptions.Password` (injected via configuration — never hardcoded).
- Waiting for the match selection dialog to appear within `LoginScreenTimeoutSeconds` (20 s) after clicking submit.
- Firing `CredentialsEntered` on success and `Timeout` on timeout.
- Unexpected dialog detection: any dialog other than the expected login or match selection dialog → close if possible, fire `UnexpectedDialog` (→ `Error`) with a descriptive reason.
- An internal abstraction for login dialog interaction, enabling unit tests to exercise the login phase without a live FlaUI session (see §3.3).
- Inline Serilog logging at correct severity levels (§3.5).
- Unit tests covering: successful login, login timeout, unexpected dialog, and password sourced from configuration.

**Out of scope:**
- Match selection automation (S-004).
- Any FlaUI interaction beyond login dialog detection and password/submit entry.
- I-U-2 through I-U-6 (team names, scoreboard, match selection element IDs).
- Changes to `StopAsync` or `RetryAsync`.

---

## 3. Design Constraints

### 3.1 Placement Within LaunchAndLoginAsync

The login phase executes immediately after the crash watcher is started. From that point `LaunchAndLoginAsync`:

1. *(already done in S-002)* Detects main window, fires `LoginDetected` → `LoginScreen`, starts crash watcher.
2. *(new in S-003)* Performs login automation within the `LoginScreenTimeoutSeconds` (20 s) window.
3. *(new in S-003)* Fires `CredentialsEntered` → `MatchSelection`.
4. Returns to the caller with `CurrentState == MatchSelection`.

The crash watcher runs throughout steps 2–4. If cricket.exe exits during login automation, the watcher fires `Timeout` → `Error` and the lock guards ensure only one transition fires.

### 3.2 Timeout Measurement

The 20-second login timeout is measured using the same `TimeProvider` abstraction already injected into `PcsProAutomationService` (established in S-002, §3.10). Elapsed time is computed from the moment the login phase begins (after crash watcher start). `FakeTimeProvider` advances time in tests exactly as in the S-002 window-detection timeout tests.

The timeout fires `PcsProTrigger.Timeout` (→ `Error`), sets `LastErrorReason` to a descriptive string identifying the operation that timed out, and causes `LaunchAndLoginAsync` to return without propagating an exception.

### 3.3 Login Interaction Abstraction

The FlaUI element interactions required for login (locate login dialog, find password field, enter text, find submit button, click, detect match selection dialog) must be encapsulated behind an internal interface so that:
- Unit tests can exercise the login phase deterministically without a live FlaUI session.
- The real FlaUI implementation is isolated from `PcsProAutomationService`'s control logic.

This follows the same testability pattern as `IProcessHandle` in S-002. The interface must provide at minimum:
- A way to detect whether the login dialog is currently open (returns `false`/`null` when not yet visible; does not throw).
- A way to enter a string into the password field (throws only if the field is not found, which the service catches per §3.8).
- A way to click the submit button (throws only if the button is not found, which the service catches per §3.8).
- A way to detect whether the match selection dialog has appeared (success condition).
- A way to detect whether an unexpected dialog is present (any dialog other than login or match selection).
- A way to attempt to close an unexpected dialog (best-effort — returns silently whether close succeeds or fails).

The exact method names and signatures are a delivery-level decision. The interface is `internal` to `PcsRemote.Automation` and visible to `PcsRemote.Automation.Tests` via `InternalsVisibleTo` (already configured in S-002).

`PcsProAutomationService` receives the login interaction abstraction via constructor injection. The real implementation is registered in `AutomationServiceCollectionExtensions`; test fakes are supplied in unit tests.

**Note on existing tests:** Adding a new constructor parameter for the login interaction abstraction requires updating all existing `PcsProAutomationServiceTests` to pass a fake instance. `CreateService()` and `CreateServiceAtLoginScreenAsync()` helpers must be updated accordingly. Tests that currently assert `CurrentState == LoginScreen` as the end-state of `LaunchAndLoginAsync` must be updated to inject a fake that simulates the complete login phase, or else to stop before the login phase — depending on what each test is exercising.

### 3.4 Login Detection Poll Strategy

The login dialog may not appear instantly after the main window is detected. The login phase should poll for the login dialog using the same interval as the main window poll in S-002 (200 ms between attempts), consuming from the 20-second `LoginScreenTimeoutSeconds` budget. Once the login dialog is detected, the service enters the password and clicks submit, then polls for the match selection dialog to appear — also within the remaining budget.

All poll iterations (both "waiting for login dialog" and "waiting for match selection") count against the single 20-second timeout. There is no separate per-phase timeout within the login window.

The login-phase poll honours the caller's `CancellationToken` (the same `ct` parameter passed to `LaunchAndLoginAsync`). If `ct` is cancelled during the login phase, the poll fires `PcsProTrigger.Timeout` → `Error` with a descriptive reason and re-throws `OperationCanceledException`, following the same pattern as the S-002 window-detection poll.

**Guard against double-transition:** The crash watcher runs concurrently during the login phase. If cricket.exe exits, the crash watcher fires `Timeout` → `Error` under the operation lock first. If the login-phase poll then also attempts an error transition, it must not throw from an invalid state machine trigger. The implementation must check whether the service is already in `Error` before firing any additional trigger, and silently return if so. This ensures `LaunchAndLoginAsync` never propagates an `InvalidOperationException` from a double-transition race. AC-10 verifies this behaviour.

### 3.5 Unexpected Dialog Handling

If at any point during the login phase an unexpected dialog is detected, the service:
1. Attempts to close the dialog via the login interaction abstraction's close-dialog capability (best-effort — proceeds to fire the error regardless of close outcome).
2. Sets `LastErrorReason` to a string that identifies the unexpected dialog (e.g., its title or AutomationId if obtainable from the abstraction).
3. Fires `PcsProTrigger.UnexpectedDialog` (→ `Error`).
4. Returns from `LaunchAndLoginAsync` without propagating an exception.

`PcsProTrigger.UnexpectedDialog` is the correct trigger because the cause is explicitly a dialog, not an elapsed-time timeout. `PcsProTrigger.Timeout` is reserved for genuine timeout (elapsed time budget exhausted) and crash watcher (process exit). Using distinct triggers makes `LastErrorReason` complementary diagnostic context, not the only differentiator.

### 3.6 Password Sourcing

The password is read from `PcsProOptions.Password` — an injected configuration value bound to `PcsPro:Password` in `appsettings.json`. It must not be hardcoded, logged, or included in any `LastErrorReason` string. Serilog structured logging must never include the password as a named property or interpolated value.

### 3.7 State After LaunchAndLoginAsync

After S-003, `LaunchAndLoginAsync` returns with `CurrentState == MatchSelection`. Callers that previously observed `LoginScreen` as the resting state (e.g., any integration tests or manual observations from S-002) should be updated to expect `MatchSelection`.

The crash watcher remains active after `LaunchAndLoginAsync` returns, protecting against unexpected cricket.exe exit from `MatchSelection` state.

### 3.8 Interaction Exception Handling

Any exception thrown by the login interaction abstraction during the login phase (e.g., element not found by AutomationId, stale element, FlaUI `ElementNotAvailableException`) is caught by the service, logged at `Error` severity with a structured Serilog template, and converted to an Error transition: `FireErrorUnderLockAsync(PcsProTrigger.Timeout, reason)` where `reason` describes the exception type and failed operation. `LaunchAndLoginAsync` does not propagate these exceptions. This follows the same pattern as `TryStartProcessAsync` catching `Win32Exception` in S-002.

---

## 4. Acceptance Criteria

### AC-1 — Successful Login Transitions to MatchSelection (Unit-verifiable)
After `LaunchAndLoginAsync` detects the main window (as in S-002), performs login automation via the fake interaction abstraction (configured to return "dialog found", "submit clicked", "match selection visible"), and fires `CredentialsEntered`, the service must be in `MatchSelection` state. `StateChanged` must fire with `PcsProState.MatchSelection`. Verified by a unit test using fake interaction and fake process.

### AC-2 — Login Timeout Fires Error (Unit-verifiable)
If the login interaction abstraction never signals match selection visibility, `LaunchAndLoginAsync` fires `PcsProTrigger.Timeout` (→ `Error`) after `PcsProStateMachine.LoginScreenTimeoutSeconds` have elapsed. `LastErrorReason` is set to a non-null, non-empty string describing the timeout. `LaunchAndLoginAsync` does not propagate an exception. Verified using `FakeTimeProvider` to advance time past 20 seconds without the fake signalling success.

### AC-3 — Password From Configuration, Not Hardcoded (Unit-verifiable + Code-review-verifiable)
**Unit-verifiable:** The fake interaction abstraction captures the password string passed to it. The test asserts that the captured string matches the value in `PcsProOptions.Password` supplied to the service. A different test with a different password option value must produce a different captured string.

**Code-review-verifiable:** No password value appears in any Serilog log template string, as a named property argument, or in any `LastErrorReason` string. No string literal that could be a credential appears in `PcsProAutomationService` or the login interaction implementation.

### AC-4 — Unexpected Dialog Fires UnexpectedDialog Trigger (Unit-verifiable)
If the login interaction abstraction signals an unexpected dialog is present, `LaunchAndLoginAsync` fires `PcsProTrigger.UnexpectedDialog` (→ `Error`). `LastErrorReason` is set to a non-null, non-empty string identifying the unexpected dialog condition. `LaunchAndLoginAsync` does not propagate an exception. The close-dialog capability of the abstraction is invoked before the trigger fires. Verified using a fake configured to report an unexpected dialog and to track whether close was attempted.

### AC-5 — Login Phase Uses Correct Timeout Constant (Code-review-verifiable)
The login phase timeout is driven by `PcsProStateMachine.LoginScreenTimeoutSeconds` (= 20). No magic number `20` or similar literal appears in `PcsProAutomationService`. The timeout is measured via `TimeProvider` (same instance injected for the window-detection phase in S-002).

### AC-6 — Element Lookups Logged at Debug (Code-review-verifiable)
Each attempt to locate or interact with a login dialog element (detect dialog, find password field, find submit button, detect match selection) is logged at `Debug` severity using a structured Serilog template. Named properties must not include the password value.

### AC-7 — State Transition Logged at Information (Code-review-verifiable)
The `CredentialsEntered` transition (→ `MatchSelection`) is logged at `Information` via the existing `OnTransitioned` callback (already in place from S-002 §3.1). No additional logging is required for this AC beyond the callback.

### AC-8 — Timeout Logged at Error (Code-review-verifiable)
Login timeout and unexpected dialog events are logged at `Error` severity with a structured Serilog template. Named properties describe the event without exposing the password.

### AC-9 — No Coordinate-Based Interaction (Code-review-verifiable)
`PcsRemote.Automation` contains no coordinate-based click simulation (`mouse_event`, `SendInput`, `SetCursorPos`, raw `PostMessage` with pixel coordinates). All interaction is via FlaUI automation elements located by AutomationId. Verified by code review.

### AC-10 — Crash Watcher Remains Active During Login Phase (Unit-verifiable)
If cricket.exe exits unexpectedly while the login phase is in progress (login interaction abstraction is polling, never signalling success), `StateChanged` fires with `PcsProState.Error` and `LastErrorReason` is `"PCS Pro exited unexpectedly"` — not a login timeout. `LaunchAndLoginAsync` does not propagate an exception. Verified by signalling fake process exit while the login interaction fake holds (never returns success), and confirming that the crash-watcher error reason is set rather than the login timeout reason.

### AC-11 — Architecture Constraint (Build-verifiable)
`PcsRemote.Automation` continues to have no compile-time dependency on `PcsRemote.Web`, `PcsRemote.Automation.Mock`, or `PcsRemote.TrayHost`. `dotnet build` produces zero errors and zero warnings.

### AC-12 — Existing Tests Updated and Passing (Build-verifiable)
The `PcsProAutomationService` constructor gains a new parameter for the login interaction abstraction. All existing tests in `PcsRemote.Automation.Tests` are updated to: (a) pass a fake login interaction abstraction through the modified `CreateService()` helper, and (b) update any assertions or helper methods that assumed `LoginScreen` as the end-state of `LaunchAndLoginAsync` to instead inject a fully-configured fake. No existing test is deleted. `dotnet test` exits with code 0.

### AC-13 — Discovered AutomationId Values Retained in Code (Code-review-verifiable)
The AutomationId strings for the login dialog password field and submit button, discovered on the garage PC via Inspect.exe during implementation, are stored as named `private const string` or `internal const string` fields in the real login interaction implementation. They are not inline magic strings. Their names must clearly describe the element they identify.

---

## 5. Known Unknowns

| ID | Description | Resolution |
|---|---|---|
| I-U-1 | Exact AutomationId values for login dialog password field and submit button | Resolved during implementation on garage PC with Inspect.exe. Strings documented in code comments at point of use. |

No other blocking unknowns for this step.

---

## 6. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-13 | Claude Opus 4.6, GPT-5.4 | NEEDS REVIEW — 3 blocking (trigger contradiction, constructor breaks tests, missing close-dialog capability), 6 non-blocking |
| R1 fixes | 2026-04-13 | Orchestrator | All 9 findings applied; v0.2 produced |
