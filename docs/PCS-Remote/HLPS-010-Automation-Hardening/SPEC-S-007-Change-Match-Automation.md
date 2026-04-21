# SPEC-S-007: Change Match Automation

| Field | Value |
|---|---|
| **Document** | SPEC-S-007-Change-Match-Automation.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-22 |
| **Step ID** | S-007 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-007-change-match` |

---

## 1. Purpose

Replace the `FlaUiChangeMatchAutomation` stub with a working implementation ported from the diagnostic tool's Step 10 (`Step10_FindChangeMatchElement`). The implementation navigates File → Open Match... to re-open the match selection dialog, enabling the user to select a different match without restarting PCS Pro.

Refer to `tools/AutomationDiagnostic/DiagnosticRunner.cs` (`Step10_FindChangeMatchElement`) for the proven patterns.

---

## 2. Scope

### 2.1 Modified Class: `FlaUiChangeMatchAutomation`

- R1: Constructor accepts `PcsProWindowLocator`, `ILogger<FlaUiChangeMatchAutomation>`. No configuration options needed.
- R2: Remove all `TODO_REPLACE_ON_GARAGE_PC` constants — use `KnownElements` constants instead.
- R3: **`ExecuteChangeMatchSequence()`** — Navigates to the match selection dialog:
  1. Find the main window via `PcsProWindowLocator`.
  2. Click the File menu (`KnownElements.FileMenuAutomationId`).
  3. Wait for and click the Open Match menu item (`KnownElements.OpenMatchMenuItemAutomationId`). Note: this reuses the same element as the initial match selection — no duplicate constant needed.
  4. Wait for the Open Match dialog to appear (identified by `Name = KnownElements.MatchSelectionDialogName`) with a 15-second timeout.
  5. Throw `InvalidOperationException` if any element is not found or the dialog does not appear.

> **IS-010 reconciliation:** IS-010 §S-007 states the method "blocks until match load is confirmed." This end-to-end guarantee is satisfied by the **service layer**, which chains `IChangeMatchAutomation.ExecuteChangeMatchSequenceAsync()` → `IMatchSelectionAutomation.SelectMatchAsync()`. The latter handles row selection, spinner wait, and match-load confirmation. `ExecuteChangeMatchSequence` is responsible only for opening the dialog; the service orchestration layer delivers the full blocking contract.

- R4: **`IsUnexpectedDialogPresent()`** — Delegates to `UIAutomationHelpers.HasUnexpectedDialog`. Must not throw.

- R5: **`TryCloseUnexpectedDialog()`** — Delegates to `UIAutomationHelpers.TryCloseFirstUnexpectedDialog`. Best-effort, no throw.

### 2.2 KnownElements

No new constants needed — the change match sequence reuses `FileMenuAutomationId` and `OpenMatchMenuItemAutomationId` already registered for S-004.

### 2.3 Out of Scope

- Match row re-search and re-selection after dialog opens — handled by the service layer calling `IMatchSelectionAutomation` again.
- "Use Current Match" workflow — deferred to S-011.

---

## 3. Test Strategy

- **T1: Build verification** — 0 warnings, 0 errors.
- **T2: Existing tests remain green** — No behavioural changes to service-layer tests (they use mock automation).
- **T3: No unit tests for FlaUiChangeMatchAutomation** — FlaUI interactions require a live PCS Pro instance. Verified via garage PC walkthrough.

---

## 4. Acceptance Criteria

- AC-1: Zero `TODO_REPLACE_ON_GARAGE_PC` constants remain in `FlaUiChangeMatchAutomation.cs`.
- AC-2: All three `IChangeMatchAutomation` methods are implemented (no `NotImplementedException`).
- AC-3: `ExecuteChangeMatchSequence` navigates File → Open Match... and waits for the dialog. The full "blocks until loaded" contract is satisfied by the service layer chaining `IMatchSelectionAutomation`.
- AC-4: No duplicate AutomationId constant — reuses `KnownElements.OpenMatchMenuItemAutomationId`.
- AC-5: Build: 0 warnings, 0 errors. All existing tests pass.

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | The Open Match dialog may take longer than 15 seconds to appear when PCS Pro is saving match data | 15-second timeout is generous based on diagnostic tool observation. If needed, timeout can be made configurable in a future step. |
| R-2 | If PCS Pro prompts "Save changes?" before opening match dialog, an unexpected dialog blocks progress | The service layer calls `IsUnexpectedDialogPresent` / `TryCloseUnexpectedDialog` **before** invoking `ExecuteChangeMatchSequence`. If an unexpected dialog appears mid-navigation, the 15-second timeout will expire and throw `InvalidOperationException`, which the service layer catches and reports. |
