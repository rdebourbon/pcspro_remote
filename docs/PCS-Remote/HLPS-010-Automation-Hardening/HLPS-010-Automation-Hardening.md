# HLPS-010: Automation Hardening — Diagnostic Findings Port

| Field | Value |
|---|---|
| **Document** | HLPS-010-Automation-Hardening.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-21 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Dependencies** | HLPS-006 (delivered skeleton FlaUI service), HLPS-009 (streaming automation — S-003 gated) |
| **Reference Implementation** | `tools/AutomationDiagnostic/DiagnosticRunner.cs` (~2500 lines, 12 passing steps) and `tools/AutomationDiagnostic/KnownElements.cs` |

---

## 1. Problem Statement

HLPS-006 delivered `PcsRemote.Automation` with a well-structured service layer: `PcsProAutomationService` orchestrating five sub-automation interfaces, state machine integration, concurrency guards, crash watcher, and retry logic — all fully implemented and tested (587 unit tests).

However, **every `FlaUi*` implementation class is a stub**. All interaction methods throw `NotImplementedException`. All element locator constants are `"TODO_REPLACE_ON_GARAGE_PC"`. Additionally, `MatchRowParser` contains `"TODO_REPLACE_ON_GARAGE_PC"` for its row format pattern and always returns `false`.

In parallel, the `tools/AutomationDiagnostic` tool was iteratively built and tested against real PCS Pro on the garage PC. All 12 diagnostic steps pass. The diagnostic tool contains the working implementations and discovered element identifiers that the `FlaUi*` stubs need.

This HLPS ports proven diagnostic patterns into the production `FlaUi*` classes, replacing every placeholder and stub with working code. It also extends the automation layer with streaming support, a "use current match" shortcut, and periodic health-check polling.

---

## 2. Scope

### Reference Implementation

The diagnostic tool is the authoritative source for all implementation patterns and element identifiers:

- **`tools/AutomationDiagnostic/KnownElements.cs`** — all discovered AutomationId, ClassName, Name, and HelpText constants
- **`tools/AutomationDiagnostic/DiagnosticRunner.cs`** — proven FlaUI interaction patterns for every automation step

IS step specs should reference specific diagnostic step method names (e.g., `Step2_EnterPassword()`, `FindButtonByChildText()`) rather than line ranges, which are brittle as the file evolves.

Key discoveries that differ from original PRD assumptions:
- **No search button** in the Open Match dialog — Enter key triggers search
- Scoreboard capture target is `ReplayScreenPreview` (AutomationId), not `itemsControlItem` (ClassName)
- Scoreboard capture uses `Capture.Rectangle()` (screen-copy) — the scoreboard window will always be visible on the garage PC (H-U-1 resolved)
- Streaming buttons have **no Name** — label is in a child TextBlock element
- Spinner detection uses ClassName `LoaderSpinner` + `IsOffscreen` polling (no AutomationId)
- DPI scaling requires `SetProcessDPIAware()` P/Invoke at process startup
- Match selection requires a site-name filter to narrow results to the correct club

### Design Rules — Element Lifetime

FlaUI elements (`AutomationElement`) must not be cached across operation boundaries. The production service is long-lived (unlike the one-shot diagnostic tool), so:

- **Reacquire controls** at the start of every operation method call — do not store `AutomationElement` references as instance fields
- **Reacquire after dialog transitions** — after closing a dialog, the parent window's automation tree may be rebuilt
- **Stale-element recovery** — if an element access throws `COMException` or `ElementNotAvailableException`, re-find the element from the nearest stable ancestor (main window)
- **No UIA3Automation sharing** — if multiple threads need FlaUI access, each must use its own `UIA3Automation` instance or access must be serialised via the operation lock

### In Scope

#### 2.1 KnownElements Registry

A centralized `KnownElements` static class in `PcsRemote.Automation`, becoming the **canonical source** for all element identifiers. The diagnostic tool's `KnownElements.cs` is the initial source, but the production version becomes authoritative going forward. All `FlaUi*` classes reference the production `KnownElements` instead of local constants. The diagnostic tool may be updated to reference the production registry or remain a standalone copy.

Any constants currently set to bare `"TODO"` (as distinct from `"TODO_REPLACE_ON_GARAGE_PC"`) must be resolved or removed — e.g., `SettingsCogHelpText`, `ScoreboardWindowClassName`, `ScoreboardWindowName` — before being imported.

#### 2.2 DPI-Aware Process Startup

`SetProcessDPIAware()` (user32.dll P/Invoke) called at process startup. Without this, the garage PC's 150% DPI scaling produces incorrect scoreboard captures (4KB garbage vs 57KB correct).

**Call site:** First line of `PcsRemote.TrayHost/Program.cs`, before `WebApplication.CreateBuilder()`. Alternatively, an `app.manifest` with `<dpiAware>true/pm</dpiAware>` may be used to avoid a code change. The IS spec will determine the preferred approach.

#### 2.3 FlaUi* Sub-Automation Classes — Replace All Stubs

All **five** existing `FlaUi*` classes are replaced with working implementations ported from the diagnostic tool:

| Class | Diagnostic Steps | Summary |
|-------|-----------------|---------|
| `FlaUiLoginAutomation` | `Step2_EnterPassword`, `Step3_ClickLogin` | Password entry, submit, login-failure dialog detection |
| `FlaUiMatchSelectionAutomation` | `Step4_OpenMatchDialog`, `Step5_SearchMatch`, `Step6_SelectMatch` | Site filter via `PcsProOptions.SiteName`, Enter-key search, spinner polling, grid extraction, row selection |
| `FlaUiTeamNamesAutomation` | `Step8_ReadTeamNames` | Menu navigation, MatchTeamView ComboBox reads (see §2.10 for return type change) |
| `FlaUiScoreboardAutomation` | `Step9_CaptureScoreboard` | ToolWindow tab activation, popup menu, DPI-aware `Capture.Rectangle()` screen capture |
| `FlaUiChangeMatchAutomation` | `Step10_ChangeMatch` | File → Open Match, wait for dialog, select row, click "Open Read Only," **block until match load confirmed** (sync-status "Up to Date" detection, matching diagnostic Step 10 behaviour) |

**`FlaUiMatchSelectionAutomation` site-name filter:** `PcsProOptions` gains a `SiteName` property. `FlaUiMatchSelectionAutomation` receives `IOptions<PcsProOptions>` via constructor injection and applies the site filter before searching, matching the diagnostic tool's approach.

#### 2.4 MatchRowParser — Replace Stub

`MatchRowParser.cs` contains `"TODO_REPLACE_ON_GARAGE_PC"` for its row format regex and `TryParse()` always returns `false`. This must be replaced with a working parser that converts DataGrid row text into `MatchInfo` records. The diagnostic tool's `Step6_SelectMatch` method extracts and parses DataGrid rows — use as the reference.

#### 2.5 Reusable Automation Helpers

Common interaction patterns extracted from the diagnostic runner into shared utilities (e.g., `FindButtonByChildText()`, `InvokeButtonSafely()`, element polling, `ActivateToolWindow()`, popup menu search, UIAutomation tree traversal). Exact helper set determined during IS creation.

#### 2.6 Login Timeout — Configurable

`LoginScreenTimeoutSeconds` moves from a `public const int` in `PcsProStateMachine` to a configurable value in `PcsProOptions`, with a default of `30` seconds. This is a Core-layer change. Existing tests that reference the timeout constant or use literal `20` in timeout-related assertions must be updated.

#### 2.7 Streaming Automation — New Interface + Class (Unblocks IS-009 S-003)

Creates a **new** `IStreamingAutomation` interface and `FlaUiStreamingAutomation` implementation. The current streaming methods are inline stubs in `PcsProAutomationService` that throw `NotImplementedException`. This requires:

- Creating `IStreamingAutomation` in `PcsRemote.Core` (contract)
- Creating `FlaUiStreamingAutomation` in `PcsRemote.Automation`
- Adding the streaming field to `AutomationDependencies` (currently a 5-field record → 6-field)
- Modifying `PcsProAutomationService.StartStreamingAsync()` and `StopStreamingAsync()` to delegate to the new interface (replacing existing inline `NotImplementedException` stubs)
- Updating DI registration in `AutomationServiceCollectionExtensions`
- Updating mock service to implement the new interface

Resolves the two blocking unknowns from IS-009 S-003:
- **SA-U-2 RESOLVED:** "Start Live Stream" shows a Video Consent dialog, then an optional "Add to Match Centre?" dialog
- **SA-U-3 RESOLVED:** Start/Stop is the same button — the child TextBlock label toggles

#### 2.8 "Use Current Match" — Skip Match Selection (GAP-010)

When PCS Pro already has a match loaded (e.g., opened manually before automation started, or after a web UI reconnect), the operator can skip the match selection flow.

**State machine extension:** A new trigger `PcsProTrigger.AttachToLoadedMatch` is added to `PcsProStateMachine` in `PcsRemote.Core`. It permits a transition from `LoginScreen` (or a new entry state such as `Attaching`) directly to `MatchLoaded`, bypassing `MatchSelection → MatchSelectionSearching → MatchSelectionReady`.

**Detection mechanism:**
- Window title contains a match identifier (pattern from diagnostic `Step7_WaitForMatchLoad`)
- `twdScoreSummary` ToolWindow is present and populated
- Status bar shows "Up to Date" sync status

**Match identity verification:** The detection signals confirm *a* match is loaded. Identity verification (confirming it is the *correct* match) is deferred to a future HLPS — the initial implementation trusts the operator's assertion that the loaded match is correct.

**Service-layer changes:**
- New method on `IPcsProAutomationService` (e.g., `AttachToCurrentMatchAsync()`)
- Reads team names from the already-loaded match using `ITeamNamesAutomation`
- Fires the new trigger to transition the state machine

**Tests:** New unit tests in `PcsRemote.Core.Tests` for the new trigger/transition path. New unit tests in `PcsRemote.Automation.Tests` for the attach flow with mock sub-automations.

#### 2.9 Periodic Health-Check Poll (GAP-011)

A lightweight periodic scan that reads PCS Pro state signals and pushes changes to the web UI via the existing `StateChanged` event infrastructure.

**Configuration:** `PcsProOptions.HealthCheckIntervalSeconds` (default: `10`, min: `5`, max: `60`).

**Signals monitored** (all proven readable in diagnostic steps 7, 9, 11):
- Live stream status (button child text in `LiveStreamingControls`)
- Server connection (status bar text)
- Correct match loaded (window title)
- Scoring sync status (status bar text)
- Match status (status bar text)

**Concurrency contract:** The health-check poll lives **inside** `PcsProAutomationService`. It uses `_operationLock.Wait(0)` (try-acquire without blocking). If the lock is already held by an active automation operation, that poll cycle is silently skipped. This guarantees no concurrent FlaUI access.

**Lifecycle:** Starts when state machine reaches `MatchLoaded`. Pauses during active operations (via lock). Stops on `ChangeMatch` or application shutdown.

**Tests:** New unit tests verifying poll-skip behaviour when lock is held, and correct `StateChanged` emission on detected state changes.

#### 2.10 Team Names — Structured Return Type

`ITeamNamesAutomation.ReadHomeTeamName()` and `ReadAwayTeamName()` currently return `string`. The diagnostic tool reads two ComboBox values per team: `cboClub` (e.g., "Northampton Town CC") and `cbxTeam` (e.g., "2nd XI"). To support proper YouTube stream title formatting, the interface return type changes to a structured type (e.g., `TeamNameInfo { ClubName, TeamName }`). This is a Core interface change.

### Delivery Tiers

Items are grouped to allow partial delivery if blocking issues arise:

| Tier | Items | Description |
|------|-------|-------------|
| **Tier 1 — Must-ship** | §2.1, §2.2, §2.3, §2.4, §2.5 | Core stub replacement — the minimum viable delivery |
| **Tier 2 — Should-ship** | §2.6, §2.7 | Configurable timeout and streaming |
| **Tier 3 — Can-defer** | §2.8, §2.9, §2.10 | GAP-010, GAP-011, and team name type change — can be deferred to HLPS-011 without invalidating this HLPS |

### Out of Scope

- No changes to **existing** orchestration control flow (`LaunchAndLoginAsync`, `GetTodaysMatchesAsync`, `LoadMatchAsync`, etc.) except where explicitly specified in §2.7–§2.9
- Web UI changes
- Mock service changes beyond updating mock implementations to match new interface signatures
- Debug/Advanced menu (GAP-001), Switch User (GAP-002), Clear Auth Errors (GAP-003), Date Filter Override (GAP-005), Filter Awareness (GAP-006), Team Name Formatting rules (GAP-009) — deferred to HLPS-011
- PCS Pro version support beyond the build tested during diagnostic development (identifiers may need re-validation after PCS Pro updates)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| H-SC-1 | Zero `TODO_REPLACE_ON_GARAGE_PC` or bare `"TODO"` placeholders remain in `PcsRemote.Automation` or `PcsRemote.Core` | `grep -rE "(TODO_REPLACE\|\"TODO\")" src/PcsRemote.Automation src/PcsRemote.Core` returns zero results |
| H-SC-2 | Zero `NotImplementedException` throws remain in `PcsRemote.Automation` project (FlaUi* classes and service-layer streaming stubs) | `grep -r "NotImplementedException" src/PcsRemote.Automation` returns zero results |
| H-SC-3 | Full end-to-end flow completes on garage PC: launch → login → match selection → load → team names → scoreboard → change match → retry | Manual walkthrough with web UI |
| H-SC-4 | Scoreboard capture produces valid JPEG (> 10KB) at 150% DPI | Manual test |
| H-SC-5 | Start/Stop live stream cycle completes including consent dialogs | Manual test |
| H-SC-6 | Zero new warnings, zero test regressions. All new service-layer, state-machine, and helper-utility code paths covered by unit tests. `FlaUi*` implementation classes verified by integration walkthrough (H-SC-3) and manual tests only | `dotnet build` + `dotnet test` |
| H-SC-7 | "Use Current Match" correctly detects an already-loaded match, verifies state signals, and transitions to `MatchLoaded` | Manual test + new unit tests for trigger/transition path |
| H-SC-8 | Health-check poll detects stream death / server disconnect / match state change within one configured poll interval (10s at default settings); correctly skips poll when operation lock is held | Manual test + new unit tests for poll-skip behaviour |
| H-SC-9 | `KnownElements` constant values in `PcsRemote.Automation` are verified against working diagnostic tool | Manual comparison or automated diff |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| H-U-1 | ~~`Capture.Rectangle()` vs `PrintWindow`~~ — **RESOLVED:** Scoreboard will always be visible on the garage PC. `Capture.Rectangle()` is the sanctioned approach. `IScoreboardAutomation` XML doc to be updated to remove `PrintWindow` reference. This decision supersedes `PROJECT-CONTEXT.md` v1.1 §2 which references `PrintWindow`; the context document should be updated to reflect this resolution. | User | ~~Yes~~ Resolved |
| H-U-2 | `SetProcessDPIAware()` in ASP.NET context — low risk but needs garage PC verification. Call site specified as `PcsRemote.TrayHost/Program.cs` or `app.manifest`. | Agent | No |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Opus, GPT-5.4, Sonnet | 16 fixes accepted, 1 accepted with note, 1 dismissed → v0.2 |
| R2 | 2026-04-21 | Opus, GPT-5.4, Sonnet | Opus: APPROVED. GPT: 2 findings (1 HIGH downgraded to MEDIUM, 1 MEDIUM). Sonnet: 1 MEDIUM. All 3 accepted → v0.3. Sonnet waived re-review. |
