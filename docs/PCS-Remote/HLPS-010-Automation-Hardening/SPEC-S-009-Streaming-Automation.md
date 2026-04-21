# SPEC-S-009: Streaming Automation

| Field | Value |
|---|---|
| **Document** | SPEC-S-009-Streaming-Automation.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-22 |
| **Step ID** | S-009 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-009-streaming-automation` |

---

## 1. Purpose

Create a new `IStreamingAutomation` interface and `FlaUiStreamingAutomation` implementation to replace the inline `NotImplementedException` stubs in `PcsProAutomationService.StartStreamingAsync()` and `StopStreamingAsync()`. This unblocks IS-009 S-003 (FlaUI streaming automation — previously gated on SA-U-2 and SA-U-3) and satisfies H-SC-2 (zero `NotImplementedException` in the Automation project) and H-SC-5 (streaming cycle).

Refer to `tools/AutomationDiagnostic/DiagnosticRunner.cs` (`Step11_StartStopLiveStream`) for the proven patterns.

---

## 2. Scope

### 2.1 New Interface: `IStreamingAutomation` (Automation)

- R1: Define `IStreamingAutomation` as an `internal interface` in `PcsRemote.Automation`, consistent with the five existing per-operation interfaces (`ILoginAutomation`, `IMatchSelectionAutomation`, etc.). All methods are synchronous (FlaUI is a synchronous API).
  - `void ClickStartLiveStream()` — Finds and clicks the "Start Live Stream" button. Throws on failure.
  - `void HandleConsentDialogs()` — Handles the Video Consent dialog (clicks "Video Consented") and the optional "Add Live Stream to Match Centre?" dialog (clicks "No"). Throws if the mandatory Video Consent dialog does not appear or cannot be dismissed.
  - `void ClickStopLiveStream()` — Finds and clicks the "Stop Live Stream" button (same physical button as start — child TextBlock label toggles). Throws on failure.
  - `bool IsStreamingActive()` — Returns `true` if the stop button label is visible (indicating active streaming). Returns `false` if the Video Display ToolWindow cannot be found or if any exception is caught. Must not throw.
  - `bool IsUnexpectedDialogPresent()` — Delegates to shared unexpected dialog detection. Must not throw.
  - `void TryCloseUnexpectedDialog()` — Delegates to shared unexpected dialog close. Best-effort, no throw.

### 2.2 New Class: `FlaUiStreamingAutomation` (Automation)

- R2: Constructor accepts `PcsProWindowLocator` and `ILogger<FlaUiStreamingAutomation>`.

- R3: **`ClickStartLiveStream()`** implementation:
  1. Find the main window via `PcsProWindowLocator`.
  2. Find the Video Display ToolWindow by AutomationId (`twdVideoCapture`) or by Name containing "Video Display".
  3. Activate it using `UIAutomationHelpers.ActivateToolWindow`.
  4. Locate the `LiveStreamingControls` container by AutomationId, falling back to the video pane if not found.
  5. Find the "Start Live Stream" button using `UIAutomationHelpers.FindButtonByChildText` (buttons have no Name — label is in a child TextBlock).
  6. Click using `UIAutomationHelpers.InvokeButtonSafely`.
  7. Throw `InvalidOperationException` if any element is not found or the invoke fails.

- R4: **`HandleConsentDialogs()`** implementation:
  1. Wait for the Video Consent dialog to appear (search top-level windows and child windows for a window with title containing "Video Consent"). Use a timeout of ~5 seconds.
  2. Find and click the "Video Consented" button (by Name, falling back to AutomationId "btnAction").
  3. After consent, check for the optional "Add Live Stream to Match Centre?" dialog (title containing "Match Centre" or "Add Live Stream").
  4. If present, find and click the "No" button to dismiss it. If the dialog is found but the "No" button cannot be located, log a warning and continue — do not throw.
  5. Throw `InvalidOperationException` if the mandatory Video Consent dialog does not appear or the consent button cannot be clicked.
  6. The Match Centre dialog is optional — log a note if it doesn't appear but don't throw.

- R5: **`ClickStopLiveStream()`** implementation:
  1. Re-find the Video Display ToolWindow and activate it (elements may have refreshed since start).
  2. Find the "Stop Live" button using `UIAutomationHelpers.FindButtonByChildText` (the same physical button as start — its child TextBlock label changes when streaming is active).
  3. Click using `UIAutomationHelpers.InvokeButtonSafely`.
  4. Throw `InvalidOperationException` if the button is not found or the invoke fails.

- R6: **`IsStreamingActive()`** — Best-effort check: finds the Video Display ToolWindow, searches for a button with child text containing "Stop Live". Returns `true` if found, `false` otherwise. Returns `false` if the Video Display ToolWindow cannot be found or if any exception is caught. Must not throw — wrap in try/catch.

- R7: **`IsUnexpectedDialogPresent()`** and **`TryCloseUnexpectedDialog()`** — Delegate to the shared `UIAutomationHelpers` helpers, consistent with all other FlaUI automation classes.

### 2.3 KnownElements Additions

- R8: Register the following streaming-specific identifiers in `KnownElements.cs`:
  - Video Display ToolWindow AutomationId (e.g., `twdVideoCapture`)
  - LiveStreamingControls AutomationId
  - Video Consent dialog name pattern
  - Video Consented button name and fallback AutomationId
  - Match Centre dialog name pattern
  - Start/Stop button child text patterns

### 2.4 Update `AutomationDependencies`

- R9: Add `IStreamingAutomation StreamingAutomation` as a sixth field to the `AutomationDependencies` record. Update the XML doc.

### 2.5 Update `PcsProAutomationService`

- R10: Replace the `NotImplementedException` stubs in `StartStreamingAsync()` and `StopStreamingAsync()` with delegation to the injected `IStreamingAutomation` via `AutomationDependencies`.
- R11: `StartStreamingAsync` should call `ClickStartLiveStream()` followed by `HandleConsentDialogs()`.
- R12: `StopStreamingAsync` should call `ClickStopLiveStream()`.

### 2.6 Update DI Registration

- R13: Register `FlaUiStreamingAutomation` as `IStreamingAutomation` in `AutomationServiceCollectionExtensions`.

### 2.7 Mock and Fake Implementations

- R14: Create `FakeStreamingAutomation` in `PcsRemote.Automation.Tests` for unit testing the delegation pattern.
- R15: Update **all** test helpers and factory methods that construct `AutomationDependencies` to include the new streaming parameter — the build will surface any missed sites as CS7036 errors.

### 2.8 Out of Scope

- Changes to `MockPcsProAutomationService` — the mock service implements `IPcsProAutomationService` directly (it does not compose sub-automation interfaces). Its `StartStreamingAsync`/`StopStreamingAsync` are already implemented and require no changes. HLPS-010 §2.7 "Updating mock service" is satisfied by the mock already working.
- Streaming overlay automation (the `StreamingOverlayTabAutomationId` in KnownElements) — this is a separate concern from the Start/Stop live stream cycle.
- Integration with `YouTubeLiveStreamService` — the existing calls to `StartStreamingAsync`/`StopStreamingAsync` on `IPcsProAutomationService` will continue to work since the interface contract doesn't change.
- Stream health monitoring — deferred to S-012 (Health-Check Poll).

---

## 3. Test Strategy

- **T1: Build verification** — 0 warnings, 0 errors.
- **T2: Existing tests remain green** — All service-layer, state-machine, and YouTube integration tests must pass. Tests are updated to include the new `AutomationDependencies` field.
- **T3: New delegation tests** — Verify that `PcsProAutomationService.StartStreamingAsync` delegates to `IStreamingAutomation.ClickStartLiveStream` + `HandleConsentDialogs`.
- **T4: New delegation tests** — Verify that `PcsProAutomationService.StopStreamingAsync` delegates to `IStreamingAutomation.ClickStopLiveStream`.
- **T5: No unit tests for FlaUiStreamingAutomation** — FlaUI interactions require a live PCS Pro instance with streaming capability. Manual verification on garage PC (H-SC-5).

---

## 4. Acceptance Criteria

- AC-1: `IStreamingAutomation` interface exists in `PcsRemote.Automation` as an `internal interface` with all six methods defined, consistent with the existing per-operation interfaces.
- AC-2: `FlaUiStreamingAutomation` exists in `PcsRemote.Automation` implementing `IStreamingAutomation`.
- AC-3: `ClickStartLiveStream` finds the button via `FindButtonByChildText` in the Video Display ToolWindow.
- AC-4: `HandleConsentDialogs` handles the mandatory Video Consent dialog and optional Match Centre dialog.
- AC-5: `ClickStopLiveStream` finds the toggle button via `FindButtonByChildText`.
- AC-6: `IsStreamingActive` returns `true` when a "Stop Live" button is present. Must not throw.
- AC-7: `AutomationDependencies` has 6 fields (added `IStreamingAutomation`).
- AC-8: `StartStreamingAsync` and `StopStreamingAsync` on `PcsProAutomationService` no longer throw `NotImplementedException`.
- AC-9: Zero `NotImplementedException` remains in `PcsRemote.Automation` project when combined with completed S-003–S-007 (H-SC-2 fully satisfied).
- AC-10: `FlaUiStreamingAutomation` is registered in DI.
- AC-11: All streaming element identifiers are in `KnownElements`, not private constants.
- AC-12: New unit tests verify the delegation pattern for both start and stop.
- AC-13: Build: 0 warnings, 0 errors. All existing tests pass.

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | The Video Consent dialog has been observed as mandatory in all diagnostic runs to date. If a future PCS Pro version changes this behaviour, the current implementation will throw, requiring a code change to add a skip-if-absent path. | R4 implements the mandatory pattern. The `HandleConsentDialogs` method throws if the dialog does not appear within the timeout, ensuring a clear error signal if PCS Pro behaviour changes. |
| R-2 | The Match Centre dialog is optional and its appearance may vary | R4 explicitly treats this as optional — log and continue if absent. |
| R-3 | Streaming buttons have no Name — identification relies on child TextBlock text which may change across PCS Pro versions | This is the only identification strategy available. `FindButtonByChildText` is a shared helper used across the codebase. The text patterns are registered in `KnownElements` for easy updating. |
| R-4 | The `AutomationDependencies` record change (5→6 fields) requires updating all test helper methods | Mechanical change — all constructors and factory methods are updated. Risk is compilation errors, not runtime issues. |
