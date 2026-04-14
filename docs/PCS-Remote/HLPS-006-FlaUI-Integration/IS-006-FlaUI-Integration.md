# IS-006: PCS Pro FlaUI Integration — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-006-FlaUI-Integration.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.4 |
| **Date** | 2026-04-12 |
| **Governing HLPS** | HLPS-006-FlaUI-Integration.md v0.2 (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Prerequisites** | IS-005 delivered (all hardening features, OperationCoordinator, ManualModeService, TrayHost, E2E tests). `IPcsProAutomationService` interface fully defined in `PcsRemote.Core`. |

---

## Overview

This sequence delivers `PcsProAutomationService` — the real FlaUI-based implementation of `IPcsProAutomationService` that replaces `NotSupportedPcsProAutomationService` in production. It also delivers the `PcsRemote.Automation` project DI registration, PrintWindow scoreboard capture, and all supporting infrastructure.

All development and manual testing is performed on the garage PC where PCS Pro (cricket.exe) is installed. Blocking unknowns I-U-1 through I-U-6 are resolved during implementation using Inspect.exe. I-U-7 (already-running policy) is resolved: **kill and relaunch**.

The sequence is ordered to build confidence incrementally: project scaffold and configuration first, then the process lifecycle, then the interactive automation flows (login, match selection, team names), then the image pipeline, then the remaining interface methods (change match, stop, retry), and finally all-up integration verification.

Steps are identified with stable IDs S-001 through S-007. IDs are never renumbered.

---

## Steps

### S-001 — Project scaffold, configuration model, and DI registration

**What changes:** The `PcsRemote.Automation` project is created (or restructured if an empty stub already exists) with the correct target framework (`net8.0-windows`), FlaUI NuGet references (`FlaUI.Core`, `FlaUI.UIA3`), and two configuration models: `PcsProOptions` capturing `ExecutablePath`, `WorkingDirectory`, and `Password` (bound to the `PcsPro:` section), and `ScoreboardOptions` capturing `JpegQuality` (bound to the `Scoreboard:` section). A companion `PcsRemote.Automation.Tests` stub project is confirmed to exist (or created) targeting `net8.0-windows` (matching `PcsRemote.Automation` to avoid cross-TFM compatibility warnings, which are fatal under `TreatWarningsAsErrors`). A DI registration extension wires `PcsProAutomationService` when `PcsPro:UseMock = false`, replacing the `NotSupportedPcsProAutomationService` placeholder. The `PcsProAutomationService` class is created as a skeleton implementing all twelve `IPcsProAutomationService` members: the nine `Task`-returning methods throw `NotImplementedException`; `CurrentState` returns `PcsProState.NotRunning`; `LastErrorReason` returns `null`; `StateChanged` has empty add/remove accessors.

**Why:** Every subsequent step builds on this scaffold. Getting the project structure, dependency graph, and configuration injection right first means all later steps inherit a correct foundation. Addresses HLPS-006 §2 DI registration and configuration sourcing requirements.

**Dependencies:** None within IS-006.

**Verification intent:** `dotnet build` succeeds with zero errors and zero warnings. The skeleton compiles and the application starts with `PcsPro:UseMock = false` without crashing on startup (the `NotImplementedException` is only thrown at call time, not at startup). The existing 228 tests continue to pass.

---

### S-002 — Process lifecycle: launch, main window detection, crash watcher, and stop

**What changes:** `LaunchAndLoginAsync` gains its process-launch phase: start cricket.exe from `ExecutablePath`/`WorkingDirectory`, poll for the main FlaUI window to appear within the 40-second timeout, and fire the `Error` trigger if the timeout expires. If cricket.exe is already running at the time of the call, it is killed first before relaunching (I-U-7 resolution). A background process-exit watcher is started after the window is detected; if cricket.exe exits unexpectedly while in any non-`NotRunning` state, the watcher fires the `Error` trigger with reason "PCS Pro exited unexpectedly." `StopAsync` is fully implemented: attempt graceful window close (WM_CLOSE), wait briefly for exit, escalate to `Process.Kill()` if needed, cancel the process watcher, and fire the `Stop` trigger. All new code includes inline Serilog logging: window detection at `Debug`, state transitions at `Information`, timeout and unexpected exit at `Error`.

**Why:** Process lifecycle is the foundation for all interactive automation — nothing else can work without a live, reachable cricket.exe main window. Implementing Stop in the same step keeps process lifecycle concerns co-located. Addresses HLPS-006 §2 process management, crash detection, and `StopAsync` requirements; targets I-SC-9, I-SC-13, I-SC-16.

**Dependencies:** S-001 (scaffold and configuration).

**Verification intent:** On the garage PC: PCS Pro launches and the main window is detected within the 40s window. Launch timeout (blocked executable) fires an Error state transition with a descriptive reason. `StopAsync` closes PCS Pro cleanly. Calling `StopAsync` when cricket.exe is already absent (e.g., manually killed before the call) completes without exception and transitions to `NotRunning`. Killing PCS Pro manually while the service is in `MatchLoaded` state triggers the Error transition within a few seconds. The 228 existing tests continue to pass.

---

### S-003 — Login automation and 20-second login timeout

**Status: DELIVERED — `c12d322`**

**What changes:** `LaunchAndLoginAsync` gains its login phase: detect the login dialog, locate the password field and submit button by their AutomationId values (discovered via Inspect.exe, I-U-1), enter the password from configuration, click submit, and wait for the match selection dialog to appear within the 20-second login timeout. If the timeout expires, fire `Error` with a descriptive reason. Any unexpected dialog encountered during this phase (or any subsequent interactive phase) is handled uniformly: close the dialog if possible, then fire `Error` with a reason describing the dialog. All new code includes inline Serilog logging: element lookups at `Debug`, state transitions at `Information`, timeout and unexpected dialog at `Error`.

**Why:** Login is the first interactive FlaUI step. Isolating it makes the specific AutomationId unknowns (I-U-1) testable in one focused step. Addresses HLPS-006 §2 login automation; targets I-SC-1, I-SC-10.

**Dependencies:** S-002 (process lifecycle — requires a live main window before login dialog can appear).

**Verification intent:** On the garage PC: the service reaches `MatchSelection` state without any coordinate-based clicks. A login timeout (network disconnected) fires `Error` with a descriptive reason. The password field AutomationId is recorded in the implementation for future reference.

---

### S-004 — Match selection automation: search, DataGrid parsing, date filter, open match

**Status: DELIVERED — `97e1530`**

**What changes:** `GetTodaysMatchesAsync` and `LoadMatchAsync` are fully implemented. `GetTodaysMatchesAsync` opens the match dialog, triggers search, waits 200ms (race condition guard), polls the LoaderSpinner until cleared within the 30-second search timeout, reads DataGrid row text values, and parses each row into a match record (date, home team, away team, match type). Parsing is delegated to a pure helper class (I-U-5 text format resolved via Inspect.exe). Unit tests for the row-parsing helper and date filter logic are added to `PcsRemote.Automation.Tests`. Rows are filtered by `DateOnly.Today`. If zero rows remain after filtering, the service fires `Error` with "No matches found for today." `LoadMatchAsync` selects the chosen row in the DataGrid and clicks "Open Read-Only," waiting for `MatchLoaded` state within the 15-second open timeout. Any unexpected dialog encountered during this phase is handled uniformly: close if possible, fire `Error` with a descriptive reason. All new code includes inline Serilog logging: element lookups at `Debug`, state transitions at `Information`, timeout and error conditions at `Error`.

**Why:** Match selection is the most complex interactive flow and the primary daily use case. The 200ms guard and spinner polling are critical for correctness. Parsing is unit-testable and therefore a key quality gate. Addresses HLPS-006 §2 match selection automation; targets I-SC-2, I-SC-3, I-SC-11, I-SC-17, I-SC-18.

**Dependencies:** S-003 (login — must be at MatchSelection state before this flow can run).

**Verification intent:** On the garage PC: today's matches appear in the web UI match cards. Unit tests for row parsing and date filtering pass without PCS Pro (`dotnet test`). The 30-second search timeout fires `Error` when the search hangs. Zero-match scenario fires `Error` with the correct reason.

---

### S-005 — Team name extraction

**Status: DELIVERED — `3d06ea9`**

**What changes:** `GetTeamNamesAsync` is fully implemented: navigate to the Scoring menu, open the Match Details/Teams dialog, read home and away team names from the relevant ComboBox or text elements (I-U-2, I-U-3 resolved via Inspect.exe), close the dialog, and return the pair as a result. The ValuePattern/SelectedItem/Name resolution strategy (I-U-3) is discovered and implemented. Any unexpected dialog encountered during this phase is handled uniformly: close if possible, fire `Error` with a descriptive reason. All new code includes inline Serilog logging: element lookups at `Debug`, state transitions at `Information`, errors at `Error`.

**Why:** Team names feed the web UI displayand are read once per match load. Isolating this step keeps the ComboBox pattern discovery (I-U-3) scoped and testable independently of the more complex match selection flow. Addresses HLPS-006 §2 team name extraction; targets I-SC-4.

**Dependencies:** S-004 (match must be loaded before team names can be read).

**Verification intent:** On the garage PC: home and away team names appear correctly in the web UI after match load. The correct ComboBox access pattern (I-U-3 resolution) is documented in the commit.

---

### S-006 — Scoreboard refresh, PrintWindow capture, and `ChangeMatchAsync`

**What changes:** Three tightly related capabilities delivered together:

1. **`RefreshScoreboardAsync`**: Locate the settings cog element by HelpText, click to open the popup menu, find "Refresh All Scoreboards" in the menu (with `GetAllTopLevelWindows()` fallback if the menu renders outside the main window tree — I-U-4). Addresses I-SC-5, I-SC-12.

2. **`CaptureScoreboardImageAsync`**: Locate the scoreboard dockable tool window by ClassName and Name, call `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` to capture the full window even when occluded, crop the bitmap to the scoreboard element's `BoundingRectangle`, encode as JPEG at the quality from `ScoreboardOptions`, and return the raw `byte[]`. Addresses I-SC-6.

3. **`ChangeMatchAsync`**: Execute the FlaUI sequence discovered in I-U-6 to close the current match in PCS Pro and return to the match selection dialog. Fire the `ChangeMatch` trigger on the state machine. Addresses I-SC-14.

Any unexpected dialog encountered during any of these three flows is handled uniformly: close if possible, fire `Error` with a descriptive reason. All new code includes inline Serilog logging: element lookups at `Debug`, state transitions at `Information`, errors at `Error`.

**Why:** These three methods are grouped because they all operate on an already-loaded match and share no dependencies between them. Grouping keeps the step bite-sized while avoiding a trivially small isolated step for each. Note: `ChangeMatchAsync` depends on I-U-6, which is the most uncertain unknown in the sequence. If I-U-6 discovery proves unexpectedly complex, `ChangeMatchAsync` may be deferred to S-007 while `RefreshScoreboardAsync` and `CaptureScoreboardImageAsync` ship as planned. Addresses HLPS-006 §2 scoreboard refresh, PrintWindow capture, and `ChangeMatchAsync`.

**Why:** These three methods are grouped because they all operate on an already-loaded match and share no dependencies between them. Grouping keeps the step bite-sized while avoiding a trivially small isolated step for each. Note: `ChangeMatchAsync` depends on I-U-6, which is the most uncertain unknown in the sequence. If I-U-6 discovery proves unexpectedly complex, `ChangeMatchAsync` may be deferred to S-007 while `RefreshScoreboardAsync` and `CaptureScoreboardImageAsync` ship as planned. Addresses HLPS-006 §2 scoreboard refresh, PrintWindow capture, and `ChangeMatchAsync`; targets I-SC-5, I-SC-6, I-SC-12, I-SC-14.

**Dependencies:** S-004 (match must be loaded before scoreboard or change-match operations are meaningful; team name extraction in S-005 is not a precondition for these methods).

**Verification intent:** On the garage PC: scoreboard image appears correctly in the web UI and updates on refresh. Capturing with PCS Pro behind another window produces a correct, uncropped image. The Change Match button returns the UI to the match selection flow. The I-U-6 FlaUI sequence is discovered and documented.

---

### S-007 — `RetryAsync`, integration smoke test, and Serilog audit

**What changes:** `RetryAsync` is fully implemented: fire `Retry` trigger (Error → NotRunning), cancel the active crash watcher (preventing a spurious second `Error` trigger from the imminent kill), kill any lingering cricket.exe process, then call `LaunchAndLoginAsync` to restart the full sequence (which starts a fresh crash watcher). All nine interface methods are now implemented; `NotImplementedException` is no longer thrown anywhere in `PcsProAutomationService`. A final Serilog log-level audit is performed: every FlaUI element lookup is verified to be logged at `Debug`, every state transition at `Information`, every timeout/error at `Error`. This is an audit and gap-fill pass — each preceding step (S-002–S-006) has already added inline logging; S-007 confirms completeness and adds any missed statements.

**Why:** `RetryAsync` closes the last remaining `NotImplementedException`. The integration smoke test (manual full walkthrough: launch → login → match selection → team names → scoreboard → change match → retry) verifies all flows compose correctly end-to-end. The Serilog audit ensures the observability requirements are met. Addresses HLPS-006 §2 Retry and logging requirements; targets I-SC-7, I-SC-8, I-SC-15.

**Dependencies:** S-006 (all interface methods must exist before the smoke test is meaningful).

**Verification intent:** On the garage PC: full end-to-end walkthrough passes all 18 success criteria from HLPS-006 §3. Log file inspection confirms Debug-level element lookups and Information-level state transitions. All 228+ existing automated tests continue to pass. The `NotSupportedPcsProAutomationService` is no longer reachable in production with `PcsPro:UseMock = false`.

---

## Delivery Notes

- **All blocking unknowns resolved during S-002 to S-006**: I-U-1 through I-U-6 are discovered with Inspect.exe and implemented inline. The resolved AutomationId strings and text format patterns must be documented in code comments at the point of use.
- **I-U-7 resolved**: Kill and relaunch policy chosen for predictable clean state.
- **No automated E2E tests**: This HLPS has no Playwright E2E step. All integration verification is manual on the garage PC. Unit tests cover only the pure parsing/filtering logic (I-SC-18).
- **Architecture rule**: `PcsRemote.Automation` depends on `PcsRemote.Core` only. No dependency on `PcsRemote.Web`, `PcsRemote.TrayHost`, or `PcsRemote.Automation.Mock` is permitted.

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | Claude Opus 4.6, GPT-5.4, Claude Sonnet 4.6 | NEEDS REVIEW — 3 HIGH, 3 MEDIUM, 2 LOW |
| R1 fixes | 2026-04-12 | Orchestrator | 8 findings accepted, all applied |
| R2 | 2026-04-12 | Claude Opus 4.6, GPT-5.4 | APPROVED — 5 advisories applied (A-003/A-004/A-005); A-001/A-002 noted as HLPS-level terminology |
