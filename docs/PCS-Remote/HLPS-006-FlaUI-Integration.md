# HLPS-006: PCS Pro FlaUI Integration

| Field | Value |
|---|---|
| **Document** | HLPS-006-FlaUI-Integration.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | HLPS-001 (interface to implement). Can run in parallel with HLPS-003–005 if a second developer is available. |

---

## 1. Problem Statement

The existing PCS Pro automation uses coordinate-based clicking (AutoHotkey) and browser automation (Selenium) — techniques that break on any UI change, resolution change, or window repositioning. The system has no error detection, no state awareness, and no ability to recover from unexpected dialogs or timeouts.

This HLPS delivers the real `PcsProAutomationService` — the FlaUI-based implementation of `IPcsProAutomationService` that interacts with PCS Pro (cricket.exe) using UIAutomation3 accessible properties. It also delivers the PrintWindow-based scoreboard image capture, replacing the mock's placeholder images.

This HLPS is developed and tested exclusively on the garage PC where PCS Pro is installed.

---

## 2. Scope

### In Scope

- **`PcsProAutomationService`** in `PcsRemote.Automation` implementing `IPcsProAutomationService`
- **Process management**: Launch cricket.exe, detect main window via FlaUI, handle process lifecycle
- **Login automation**: Locate password field and submit button by AutomationId; enter credentials; wait for match selection dialog (per PRD §7)
- **Match selection automation**: Open match dialog, trigger search, wait for LoaderSpinner to clear, read DataGrid rows, filter by today's date, select row, click "Open Read-Only" (per PRD §8)
- **Team name extraction**: Navigate Scoring menu, open Match Details/Teams dialog, read ComboBox values via ValuePattern or SelectedItem fallback, close dialog (per PRD §9)
- **Scoreboard refresh**: Find settings cog by HelpText, click for popup menu, find "Refresh All Scoreboards" with all-windows fallback search (per PRD §11)
- **PrintWindow scoreboard capture**: Capture full PCS Pro window via `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)`, crop to scoreboard element BoundingRectangle, encode as JPEG quality 75 (per PRD §10)
- **Scoreboard element location**: Find dockable tool window by ClassName `"itemsControlItem"` and Name `"Cricket.ReplayScreen.ViewModels.ScoreboardCommandViewModel"` (per PRD §10)
- **Element discovery and verification**: Log all element lookups at Debug level (per PRD §5b). Verify AutomationIds and Names against PRD §7 element map using Inspect.exe.
- **Timeout enforcement**: All transition timeouts from PRD §6 (40s launch, 20s login, 30s search, 15s load)
- **Error state transitions**: Any timeout or unexpected dialog → Error state with descriptive reason
- **Serilog logging**: Every element lookup, state transition, timeout, and error logged at appropriate level (per PRD §5b)

### Out of Scope

- Web UI (already built in HLPS-003–005)
- Mock service (HLPS-002)
- Deployment / Task Scheduler (HLPS-007)
- YouTube streaming automation (Phase 2)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| I-SC-1 | PCS Pro launches, logs in, and reaches MatchSelection — zero coordinate-based clicks | Manual test on garage PC |
| I-SC-2 | Today's matches retrieved from DataGrid, filtered correctly, presented to web UI | End-to-end test on garage PC |
| I-SC-3 | Match selection → MatchLoaded within 15s | Timing measurement |
| I-SC-4 | Team names correctly extracted and displayed in web UI | Manual verification against PCS Pro |
| I-SC-5 | Scoreboard refresh triggers PCS Pro refresh and LED board updates | Manual observation of LED scoreboard |
| I-SC-6 | PrintWindow captures scoreboard correctly regardless of window occlusion | Test with PCS Pro behind other windows |
| I-SC-7 | All FlaUI element lookups logged at Debug level | Log file inspection |
| I-SC-8 | All state transitions logged at Information level | Log file inspection |
| I-SC-9 | Timeout → Error state with descriptive message surfaced to web UI | Timeout simulation (slow PCS Pro startup) |
| I-SC-10 | Popup menu fallback search works when "Refresh All Scoreboards" renders outside main window tree | Manual test |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| I-U-1 | Exact AutomationId values for login password field and submit button — PRD §7 says "AutomationId" but doesn't specify the exact strings. Must verify with Inspect.exe on garage PC. | Agent | Yes — blocks login automation. Must be resolved during development on garage PC. |
| I-U-2 | Exact AutomationId values for team name panes and ComboBoxes — same issue as I-U-1 | Agent | Yes — blocks team name extraction. Resolved during development. |
| I-U-3 | `ValuePattern.Current.Value` vs `SelectedItem?.Text` vs `.Name` for read-only ComboBox — PRD §7 notes uncertainty. Must test during development. | Agent | Yes — blocks team name extraction. Quick test resolves this. |
| I-U-4 | Popup menu scope for "Refresh All Scoreboards" — may render outside main window tree (PRD §7 note). Fallback search specified but needs verification. | Agent | No — fallback already designed; just needs confirmation |
| I-U-5 | DataGrid row Text property format — PRD §8 says "parse each row's Text property to extract date, home team, away team, match type" but exact format unknown. Must inspect during development. | Agent | Yes — blocks match parsing. Resolved during development on garage PC. |

> **Note**: All blocking unknowns in this HLPS are "resolved during development" — they require hands-on access to PCS Pro with Inspect.exe. They do not block spec creation or code structure; they block specific element locator strings that will be discovered and coded during implementation.

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | Not yet reviewed |
