# SPEC-S-006: Scoreboard Automation

| Field | Value |
|---|---|
| **Document** | SPEC-S-006-Scoreboard-Automation.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-22 |
| **Step ID** | S-006 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-006-scoreboard` |

---

## 1. Purpose

Replace the `FlaUiScoreboardAutomation` stub with a working implementation ported from the diagnostic tool's Step 9 (`Step9_FindScoreboardElements`). The implementation activates the Main Scoreboard ToolWindow, opens its settings popup via a multi-strategy approach, clicks "Refresh all Scoreboards", and captures a DPI-aware JPEG screenshot of the scoreboard content area using `Capture.Rectangle()`.

Refer to `tools/AutomationDiagnostic/DiagnosticRunner.cs` (`Step9_FindScoreboardElements`, `CaptureScoreboardImage`) for the proven patterns.

---

## 2. Scope

### 2.1 Modified Class: `FlaUiScoreboardAutomation`

- R1: Constructor accepts `PcsProWindowLocator`, `ILogger<FlaUiScoreboardAutomation>`, and `IOptions<ScoreboardOptions>` for configurable JPEG quality.
- R2: Remove all `TODO_REPLACE_ON_GARAGE_PC` constants — use `KnownElements` constants instead.
- R3: **`ClickSettingsCog()`** — Opens the scoreboard settings popup:
  1. Find the Main Scoreboard ToolWindow (by ClassName + Name matching — prefer "Main Scoreboard" exact, fall back to any ToolWindow with "Scoreboard" in name).
  2. Activate the ToolWindow using `UIAutomationHelpers.ActivateToolWindow`.
  3. Walk up to the parent ToolWindowContainer, find the title bar (PART_TitleBar or TitleBarPanel).
  4. Find the Settings PopupButton (ClassName = "PopupButton", HelpText contains "Settings").
  5. Open it using a multi-strategy fallback: ExpandCollapsePattern → InvokePattern → Focus+Click. Each strategy checks for popup visibility before proceeding to the next. Throw if all strategies fail.
  6. **Must not use coordinate-based clicks** (PROJECT-CONTEXT.md §4 "never coordinates").

- R4: **`ClickRefreshAllScoreboards()`** — Clicks the "Refresh all Scoreboards" menu item:
  1. Search main window first, then Desktop (WPF popups may render at desktop level).
  2. Throw if not found.

- R5: **`CaptureScoreboardImage()`** — Returns a JPEG byte array:
  1. Find and activate the Main Scoreboard ToolWindow.
  2. Prefer the `ReplayScreenPreview` element (content area only) over the full ToolWindow (which includes chrome).
  3. Validate BoundingRectangle dimensions > 0.
  4. Use `Capture.Rectangle()` for DPI-aware screen capture (per H-U-1 resolution).
  5. Encode as JPEG using configurable quality from `ScoreboardOptions`.
  6. Properly dispose `EncoderParameters` and `EncoderParameter` using `using` statements.

- R6: **`IsUnexpectedDialogPresent()`** — Delegates to `UIAutomationHelpers.HasUnexpectedDialog`. Must not throw.

- R7: **`TryCloseUnexpectedDialog()`** — Delegates to `UIAutomationHelpers.TryCloseFirstUnexpectedDialog`. Best-effort, no throw.

### 2.2 KnownElements Additions

The following scoreboard-specific identifiers must be registered in `KnownElements.cs` (not kept as private constants in the class):
- ToolWindow ClassName
- PopupButton ClassName for settings
- Settings HelpText prefix
- ReplayScreenPreview AutomationId

### 2.3 Documentation Updates

- Update `IScoreboardAutomation` XML doc for `CaptureScoreboardImageAsync` to document the `Capture.Rectangle()` capture method (resolving the interface-level `TODO` annotation).
- Update `PROJECT-CONTEXT.md` §2 to reflect the DPI-aware capture method if not already accurate.

### 2.4 Out of Scope

- Streaming overlay automation — separate step (S-009).
- Scoreboard refresh scheduling — handled by the service layer.

---

## 3. Test Strategy

- **T1: Build verification** — 0 warnings, 0 errors.
- **T2: Existing tests remain green** — No behavioural changes to service-layer tests (they use mock automation).
- **T3: No unit tests for FlaUiScoreboardAutomation** — FlaUI interactions require a live PCS Pro instance. Verified via garage PC walkthrough (H-SC-4).

---

## 4. Acceptance Criteria

- AC-1: Zero `TODO_REPLACE_ON_GARAGE_PC` constants remain in `FlaUiScoreboardAutomation.cs`.
- AC-2: All five `IScoreboardAutomation` methods are implemented (no `NotImplementedException`).
- AC-3: `ClickSettingsCog` uses multi-strategy popup open with popup visibility check after each strategy.
- AC-4: `ClickSettingsCog` does not use coordinate-based mouse clicks.
- AC-5: `CaptureScoreboardImage` uses `Capture.Rectangle()` for DPI-aware capture.
- AC-6: `CaptureScoreboardImage` prefers `ReplayScreenPreview` over full ToolWindow.
- AC-7: JPEG encoding uses configurable quality from `ScoreboardOptions`.
- AC-8: `EncoderParameters` is properly disposed.
- AC-9: Scoreboard element identifiers are in `KnownElements`, not private constants.
- AC-10: Interface XML doc and `PROJECT-CONTEXT.md` updated to reflect the `Capture.Rectangle()` capture method.
- AC-11: Build: 0 warnings, 0 errors. All existing tests pass.

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | PopupButton may not respond to any of the 3 strategies on certain PCS Pro versions | Throw `InvalidOperationException` on all-strategy failure so the caller can report the error and retry. |
| R-2 | WPF popup menu items may render at Desktop level rather than inside the main window | `ClickRefreshAllScoreboards` searches Desktop as fallback. |
| R-3 | BoundingRectangle may return zero dimensions if the ToolWindow is collapsed | Explicit dimension validation before capture with descriptive exception. |
