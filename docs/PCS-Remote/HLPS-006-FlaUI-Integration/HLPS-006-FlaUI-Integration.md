# HLPS-006: PCS Pro FlaUI Integration

| Field | Value |
|---|---|
| **Document** | HLPS-006-FlaUI-Integration.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.2 |
| **Date** | 2026-04-12 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | HLPS-001 (original interface contract); interface extended in HLPS-003/005 — refer to current `IPcsProAutomationService.cs`. Can run in parallel with HLPS-003–005 if a second developer is available. |

---

## 1. Problem Statement

The existing PCS Pro automation uses coordinate-based clicking (AutoHotkey) and browser automation (Selenium) — techniques that break on any UI change, resolution change, or window repositioning. The system has no error detection, no state awareness, and no ability to recover from unexpected dialogs or timeouts.

This HLPS delivers the real `PcsProAutomationService` — the FlaUI-based implementation of `IPcsProAutomationService` that interacts with PCS Pro (cricket.exe) using UIAutomation3 accessible properties. It also delivers the PrintWindow-based scoreboard image capture, replacing the mock's placeholder images.

This HLPS is developed and tested exclusively on the garage PC where PCS Pro is installed.

---

## 2. Scope

### Target Environment Constraint

This HLPS targets a single, fixed garage PC installation. PCS Pro version, Windows DPI/scaling, and UI layout are treated as stable for this deployment. Any change to PCS Pro's UIAutomation tree will require re-discovery with Inspect.exe and is outside the current scope.

### In Scope

- **`PcsProAutomationService`** in `PcsRemote.Automation` implementing `IPcsProAutomationService` — all nine interface methods
- **Configuration sourcing**: `PcsProAutomationService` reads `PcsPro:ExecutablePath`, `PcsPro:WorkingDirectory`, `PcsPro:Password`, and `Scoreboard:JpegQuality` from injected configuration (`IOptions<T>` or `IConfiguration`). No credentials are hardcoded.
- **DI registration**: A `PcsRemote.Automation` service registration extension wires `PcsProAutomationService` as the concrete implementation when `PcsPro:UseMock = false`, replacing `NotSupportedPcsProAutomationService`.
- **Process management**: Launch cricket.exe, detect main window via FlaUI, handle graceful process lifecycle. If cricket.exe is already running at launch time, the service applies the policy resolved in I-U-7.
- **Process crash detection**: A background process watcher monitors cricket.exe after launch. If the process exits unexpectedly while in any non-`NotRunning` state, the service fires an `Error` trigger with reason "PCS Pro exited unexpectedly."
- **Login automation**: Locate password field and submit button by AutomationId; enter credentials; wait for match selection dialog (per PRD §7)
- **Match selection automation**: Open match dialog, trigger search, wait 200ms (race condition guard per PRD §8 — allows the LoaderSpinner to appear), poll LoaderSpinner until cleared, read DataGrid rows, filter by today's date, select row, click "Open Read-Only" (per PRD §8). If zero rows match today's date, transition to Error with reason "No matches found for today."
- **Team name extraction**: Navigate Scoring menu, open Match Details/Teams dialog, read ComboBox values via ValuePattern or SelectedItem fallback, close dialog (per PRD §9)
- **Match close / change match** (`ChangeMatchAsync`): Close the current match in PCS Pro via the FlaUI sequence resolved in I-U-6, returning to the match selection dialog. Fires the `ChangeMatch` trigger on the state machine.
- **Stop** (`StopAsync`): Attempt graceful close of the PCS Pro main window (WM_CLOSE); if the process does not exit within a short wait, escalate to `Process.Kill()`. Fires the `Stop` trigger on the state machine, transitioning to `NotRunning`.
- **Retry** (`RetryAsync`): Fires the `Retry` trigger (Error → NotRunning), kills any lingering cricket.exe process, then calls `LaunchAndLoginAsync` to restart the full automation sequence.
- **Scoreboard refresh**: Find settings cog by HelpText, click for popup menu, find "Refresh All Scoreboards" with all-windows fallback search (per PRD §11)
- **PrintWindow scoreboard capture**: Capture full PCS Pro window via `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)`, crop to scoreboard element BoundingRectangle, encode as JPEG at the configured quality (per PRD §10). Returns raw `byte[]` — delta detection is the responsibility of `ScoreboardPollingService` (delivered in IS-004).
- **Scoreboard element location**: Find dockable tool window by ClassName `"itemsControlItem"` and Name `"Cricket.ReplayScreen.ViewModels.ScoreboardCommandViewModel"` (per PRD §10)
- **Element discovery and verification**: Log all element lookups at Debug level (per PRD §5b). Verify AutomationIds and Names against PRD §7 element map using Inspect.exe.
- **Timeout enforcement**: All transition timeouts from PRD §6 — 40s launch, 20s login, 30s search, 15s open. Each timeout fires an `Error` trigger with a descriptive message identifying the timed-out operation.
- **Unexpected dialog handling**: Any dialog not in the expected FlaUI interaction flow → close the dialog if possible, fire `Error` trigger with reason describing the dialog.
- **Serilog logging**: Every element lookup, state transition, timeout, and error logged at appropriate level (per PRD §5b).
- **Unit-testable parsing logic**: DataGrid row text parsing (extracting date, home team, away team, match type) and date filtering logic are implemented in pure, injectable helper methods and covered by unit tests without requiring a live PCS Pro instance.

### Out of Scope

- Web UI (already built in HLPS-003–005)
- Mock service (HLPS-002)
- SHA-256 delta detection — handled by `ScoreboardPollingService` (IS-004 S-002); the automation service returns raw image bytes
- Deployment / Task Scheduler (HLPS-007)
- YouTube streaming automation (Phase 2)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| I-SC-1 | PCS Pro launches, logs in, and reaches MatchSelection — zero coordinate-based clicks | Code review — no `mouse_event`, `SendInput`, or coordinate-based P/Invoke calls in `PcsRemote.Automation` |
| I-SC-2 | Today's matches retrieved from DataGrid, filtered correctly, presented to web UI | Manual walkthrough on garage PC — operator observes today's match cards appear in web UI |
| I-SC-3 | MatchSelectionReady → MatchLoaded (match open operation) completes within the 15s timeout | Timing measurement on garage PC |
| I-SC-4 | Team names correctly extracted and displayed in web UI | Manual verification against PCS Pro |
| I-SC-5 | `RefreshAllScoreboards` FlaUI click executes — confirmed by observable PCS Pro UI reload | Manual observation of PCS Pro scoreboard data refresh in the application window |
| I-SC-6 | PrintWindow captures scoreboard correctly regardless of window occlusion; captured image dimensions match scoreboard BoundingRectangle; JPEG encoding at configured quality (verified by code review of encoder parameters) | Manual test with PCS Pro behind other windows; code review |
| I-SC-7 | All FlaUI element lookups logged at Debug level | Log file inspection |
| I-SC-8 | All state transitions logged at Information level | Log file inspection |
| I-SC-9 | 40s launch timeout → `Error` state with descriptive reason surfaced to web UI | Manual test — slow/blocked cricket.exe startup |
| I-SC-10 | 20s login timeout → `Error` state with descriptive reason | Manual test — disconnect network to prevent credential validation |
| I-SC-11 | 30s search timeout → `Error` state with descriptive reason | Manual test — fault injection or breakpoint during search spinner |
| I-SC-12 | Popup menu fallback search works when "Refresh All Scoreboards" renders outside main window tree | Manual test |
| I-SC-13 | `StopAsync` closes cricket.exe and transitions to `NotRunning`; if process is already absent, completes cleanly without error | Manual test on garage PC |
| I-SC-14 | `ChangeMatchAsync` closes current match and returns web UI to `MatchSelection` state | Manual test — click Change Match while match is loaded |
| I-SC-15 | `RetryAsync` recovers from `Error` state and restarts the full launch sequence | Manual test — trigger an error, then click Retry |
| I-SC-16 | Unexpected cricket.exe exit (any non-`NotRunning` state) → `Error` state with reason "PCS Pro exited unexpectedly" | Manual test — kill cricket.exe via Task Manager while match is loaded |
| I-SC-17 | Zero matches for today's date → `Error` state with reason "No matches found for today" | Manual test — run on a day with no fixtures, or inject a mock date |
| I-SC-18 | DataGrid row parsing and date filtering unit tests pass without a live PCS Pro instance | `dotnet test` |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| I-U-1 | Exact AutomationId values for login password field and submit button — PRD §7 says "AutomationId" but doesn't specify the exact strings. Must verify with Inspect.exe on garage PC. | Agent | Yes — blocks login automation. Resolved during development on garage PC. |
| I-U-2 | Exact AutomationId values for team name panes and ComboBoxes — same issue as I-U-1 | Agent | Yes — blocks team name extraction. Resolved during development. |
| I-U-3 | `ValuePattern.Current.Value` vs `SelectedItem?.Text` vs `.Name` for read-only ComboBox — PRD §7 notes uncertainty. Must test during development. | Agent | Yes — blocks team name extraction. Quick test resolves this. |
| I-U-4 | Popup menu scope for "Refresh All Scoreboards" — may render outside main window tree. Scope explicitly specifies `GetAllTopLevelWindows()` as the fallback — no additional design required; needs runtime confirmation. | Agent | No — fallback already designed |
| I-U-5 | DataGrid row Text property format — PRD §8 says "parse each row's Text property to extract date, home team, away team, match type" but exact format unknown. Must inspect during development. | Agent | Yes — blocks match parsing. Resolved during development on garage PC. |
| I-U-6 | `ChangeMatchAsync` FlaUI sequence — the exact PCS Pro UI gesture to close a loaded match and return to match selection dialog is not specified in the PRD. Must inspect with Inspect.exe and identify the correct menu/button sequence (e.g., File → Open Match, or a close-match toolbar button). | Agent | Yes — blocks `ChangeMatchAsync` implementation. Resolved during development on garage PC. |
| I-U-7 | Behavior when cricket.exe is already running at `LaunchAndLoginAsync` call time — policy: kill and relaunch (ensures clean state). **RESOLVED: kill and relaunch.** | Agent | Yes — **RESOLVED: kill existing process, then relaunch** |

> **Note**: All blocking unknowns in this HLPS are "resolved during development" — they require hands-on access to PCS Pro with Inspect.exe. They do not block spec creation or code structure; they block specific element locator strings and behavioral policies that will be discovered and coded during implementation. Exception: I-U-7 policy choice should be confirmed before implementation begins.

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | Claude Opus 4.6, GPT-5.4, Claude Sonnet 4.6 | NEEDS REVIEW — 10 accepted findings applied (v0.2) |
| R2 | 2026-04-12 | Claude Opus 4.6, GPT-5.4 | APPROVED — all 19 R1 findings verified; one I-U-6/I-U-7 cross-reference typo corrected (editorial, no semantic impact) |
