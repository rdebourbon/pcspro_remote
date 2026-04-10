# HHCC Streaming Automation — Phase 1 PRD
## Match Selection & Scoreboard Control

**Version:** 1.0 — Phase 1
**Prepared:** April 2026
**Author:** High Halstow Cricket Club
**Status:** Ready for Developer Handoff

---

## 1. Background & Purpose

High Halstow Cricket Club (HHCC) operates a garage PC that automates the setup of live cricket scoring and streaming using PlayCricket Scorer Pro (PCS Pro / NV Play ECB) and YouTube Live.

The existing system was built by a former volunteer using Python, AutoHotkey, and Selenium. It is fragile (coordinate-based clicking, arbitrary sleep delays, hardcoded values) and is no longer maintainable. This document specifies a complete rebuild in C#/.NET.

**Phase 1** delivers a robust, reliable control panel for match selection and scoreboard management. Phase 2 (specified separately) adds YouTube live streaming on top of this foundation.

---

## 2. Scope

### In Scope — Phase 1
- PCS Pro automation (login, match selection, match loading, team name extraction, scoreboard refresh)
- Web-based control panel with real-time status
- Live scoreboard image preview in the browser
- Multi-user awareness
- PCS Pro state machine with error detection and reporting

### Out of Scope — Phase 1
- YouTube / live streaming (Phase 2)
- LED scoreboard configuration (managed by NV Play directly)
- Camera management (managed by NV Play directly)
- PCS Pro scoring data entry (the garage PC is always a read-only consumer)
- Mobile / responsive layout (desktop browser only for v1)

---

## 3. How the Current System Works (Context)

PCS Pro (cricket.exe) is a WPF application developed by NV Play for the ECB. On the garage PC it runs in **read-only mode** — it receives live scoring data from a separate scoring device operated at the boundary, displays it, and drives the LED scoreboard via COM5 and NV Play's scoreboard overlay system.

NV Play outputs scoring data to the LED scoreboard continuously whenever data changes. Occasionally a data message is lost in transit — a "refresh" command forces PCS Pro to resend a complete fresh data message to all connected scoreboards.

Team names are mandatory match metadata set when a fixture is created in the Play-Cricket system. They are always present when a match is loaded, regardless of whether scoring has begun.

---

## 4. System Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Operator Browser                     │
│              (Chrome / Edge on local network)           │
└───────────────────────┬─────────────────────────────────┘
                        │ HTTP + WebSocket (SignalR)
┌───────────────────────▼─────────────────────────────────┐
│              ASP.NET Core Web Application               │
│                                                         │
│  ┌─────────────────┐    ┌────────────────────────────┐  │
│  │   Minimal API   │    │      SignalR Hub            │  │
│  │   (commands)    │    │  (real-time state push)     │  │
│  └────────┬────────┘    └───────────────┬────────────┘  │
│           │                             │               │
│  ┌────────▼─────────────────────────────▼────────────┐  │
│  │           PCS Pro Automation Service               │  │
│  │                                                   │  │
│  │  ┌──────────────────┐  ┌──────────────────────┐   │  │
│  │  │  State Machine   │  │  Scoreboard Capture  │   │  │
│  │  │  (Stateless lib) │  │  (background worker) │   │  │
│  │  └────────┬─────────┘  └──────────────────────┘   │  │
│  │           │                                        │  │
│  │  ┌────────▼─────────┐                             │  │
│  │  │   FlaUI (UIA3)   │                             │  │
│  │  └────────┬─────────┘                             │  │
│  └───────────┼─────────────────────────────────────── ┘  │
└──────────────┼──────────────────────────────────────────┘
               │ UIAutomation3 COM API
┌──────────────▼──────────────────────────────────────────┐
│              PCS Pro  (cricket.exe)                     │
│              NV Play ECB — WPF Application              │
└─────────────────────────────────────────────────────────┘
                        │ COM5 (serial)
┌───────────────────────▼─────────────────────────────────┐
│              LED Scoreboard                             │
└─────────────────────────────────────────────────────────┘
```

---

## 5. Tech Stack

| Component | Technology | Rationale |
|---|---|---|
| Web application | ASP.NET Core 8 Minimal API | Lightweight, native C#, no ceremony |
| Real-time communication | SignalR | State push to all connected browsers without polling |
| PCS Pro automation | FlaUI (UIA3) | UIAutomation3 — modern, robust, WPF-native. Replaces AHK coordinate clicking |
| State machine | Stateless NuGet package | Lightweight formal state machine, prevents invalid state transitions |
| Scoreboard capture | PrintWindow Win32 API (`PW_RENDERFULLCONTENT`) | Captures WPF window content off-screen regardless of window visibility or occlusion |
| Image delta detection | SHA-256 hash comparison | Prevents unnecessary SignalR broadcasts when scoreboard has not changed |
| Configuration | .NET User Secrets (dev) / Environment variables (prod) | PCS Pro credentials never stored in source code |
| Logging | Serilog (console + rolling file sink) | Structured logging from day one — see Section 5a |

---

## 5a. Developer Experience

### Mock Interface — Develop Without PCS Pro

The PCS Pro automation layer must be hidden behind a clean interface so that the web layer, SignalR hub, state machine logic, and UI can be built and tested entirely on the development machine without PCS Pro installed.

Define the following interface as the sole contract between the web layer and the automation layer:

```csharp
public interface IPcsProAutomationService
{
    PcsProState CurrentState { get; }
    event EventHandler<PcsProState> StateChanged;

    Task LaunchAndLoginAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MatchInfo>> GetTodaysMatchesAsync(CancellationToken ct = default);
    Task LoadMatchAsync(MatchInfo match, CancellationToken ct = default);
    Task<MatchTeams> GetTeamNamesAsync(CancellationToken ct = default);
    Task RefreshScoreboardAsync(CancellationToken ct = default);
    Task<byte[]> CaptureScoreboardImageAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
```

Two concrete implementations are required:

| Implementation | Purpose | Used when |
|---|---|---|
| `PcsProAutomationService` | Real FlaUI implementation | Running on the garage PC against live PCS Pro |
| `MockPcsProAutomationService` | Returns realistic fake data, simulates state transitions and timing | Development and testing on any machine without PCS Pro |

Register via dependency injection with an `appsettings.json` flag:

```json
{
  "PcsPro": {
    "UseMock": true
  }
}
```

```csharp
if (config.GetValue<bool>("PcsPro:UseMock"))
    services.AddSingleton<IPcsProAutomationService, MockPcsProAutomationService>();
else
    services.AddSingleton<IPcsProAutomationService, PcsProAutomationService>();
```

The mock implementation should:
- Simulate realistic state transitions with short delays (e.g. 2s for "logging in", 3s for "searching matches")
- Return a hardcoded list of 1–2 today's matches with realistic team names
- Return a placeholder JPEG image for the scoreboard preview (a solid colour with text is sufficient)
- Randomly vary the scoreboard image occasionally to exercise the delta detection logic
- Fire `StateChanged` events as the real implementation would

This allows the entire web application — including SignalR real-time updates, match card selection, status indicators, button state management, multi-user behaviour, and error display — to be built, tested, and refined on the development machine before the FlaUI layer is written.

---

### 5b. Structured Logging — Serilog

Add **Serilog** with console and rolling file sinks from the very first commit. Every FlaUI element lookup, state transition, and automation step should be logged. This allows issues on the garage PC to be diagnosed by reading a log file without needing a debugger or remote session.

**NuGet packages required:**
- `Serilog.AspNetCore`
- `Serilog.Sinks.Console`
- `Serilog.Sinks.File`

**Minimum logging configuration:**

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "HHCC.Automation": "Debug"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/hhcc-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 14
        }
      }
    ]
  }
}
```

**Logging requirements for the automation layer:**

Every FlaUI element lookup must log at `Debug` level before the lookup and after:

```csharp
_logger.LogDebug("Locating element: {Identifier}", "AutomationId=mnuScoring");
// ... FlaUI call ...
_logger.LogDebug("Element located: Name={Name} IsEnabled={Enabled}", element.Name, element.IsEnabled);
```

Every state transition must log at `Information` level:

```csharp
_logger.LogInformation("PCS Pro state: {From} → {To}", previousState, newState);
```

Every timeout or error must log at `Warning` or `Error` level with full context:

```csharp
_logger.LogWarning("LoaderSpinner did not clear within {TimeoutSeconds}s — transitioning to Error state", 30);
_logger.LogError(ex, "Unexpected exception during match selection");
```

Log files are written to a `logs/` subfolder relative to the application working directory and retained for 14 days.

---

## 6. PCS Pro State Machine

Implemented using the **Stateless** NuGet package. Each state has a **detection method** (FlaUI query) and an **entry action**. Transitions are guarded — invalid operations are rejected before any automation runs.

### States

| State | Description | Detection Method |
|---|---|---|
| `NotRunning` | cricket.exe not present | `Process.GetProcessesByName("cricket")` empty |
| `Launching` | Process started, main window not yet ready | Process exists, main window not found by FlaUI |
| `LoginScreen` | Login dialog visible | Login password field element found |
| `MatchSelection` | Match selection dialog open | Dialog window with DataGrid found |
| `MatchSelectionSearching` | Search in progress | `ClassName("LoaderSpinner").IsOffscreen == false` |
| `MatchSelectionReady` | Search complete, results visible | `IsOffscreen == true`, DataGrid has rows |
| `MatchLoaded` | Match open in read-only mode | Main window active, stream/capture checkboxes found |
| `Error` | Unexpected state — unknown dialog, crash, timeout | None of the above match within timeout |

### Transitions

```
NotRunning
    │── [Launch] ──► Launching
            │── [LoginDetected] ──► LoginScreen
                    │── [CredentialsEntered] ──► MatchSelection
                            │── [SearchTriggered] ──► MatchSelectionSearching
                                    │── [SpinnerGone] ──► MatchSelectionReady
                                            │── [MatchOpened] ──► MatchLoaded
                                                    │── [ChangeMatch] ──► MatchSelection
                                                    │── [Stop] ──► NotRunning

Any state ──► [UnexpectedDialog / Timeout] ──► Error
Error      ──► [Retry] ──► NotRunning
```

### Timeouts

| Transition | Timeout |
|---|---|
| Launching → LoginScreen | 40 seconds |
| LoginScreen → MatchSelection | 20 seconds |
| MatchSelectionSearching → MatchSelectionReady | 30 seconds |
| MatchSelection → MatchLoaded | 15 seconds |

All timeouts transition to `Error` state with the timeout reason surfaced to the web UI via SignalR.

---

## 7. FlaUI UI Element Map

All elements identified via Inspect.exe. No coordinate-based interaction anywhere in the system.

| Element | Located By | Notes |
|---|---|---|
| Login password field | AutomationId | Standard Edit control |
| Login submit | AutomationId | Standard Button |
| Match selection dialog | ControlType = Window | Parent of all match selection elements |
| Match DataGrid | AutomationId | Standard DataGrid — rows enumerable |
| DataGrid rows | ControlType = DataGridRow | Text property exposes match data |
| LoaderSpinner | ClassName = `"LoaderSpinner"` | No AutomationId — detect via `IsOffscreen` |
| "Open Read-Only" button | Name = `"Open Read-Only"` | AutomationId `btnAdditionalCancel` — unusable, use Name |
| Stream/capture checkboxes | AutomationId | Used for state detection (`IsChecked`) |
| Scoring menu | AutomationId = `"mnuScoring"` | Top-level menu item |
| "Match Details/Teams…" | Name = `"Match Details/Teams…"` | No AutomationId — use Name |
| Home team pane | AutomationId | Container for home team ComboBox |
| Away team pane | AutomationId | Container for away team ComboBox |
| Team name ComboBox | AutomationId | Read-only — use `ValuePattern` or `.SelectedItem` |
| Settings cog (scoreboard) | HelpText = `"Settings"` | PopupButton — no AutomationId |
| "Refresh All Scoreboards" | Name = `"Refresh All Scoreboards"` | May render outside parent tree — search all windows if not found |

> **Note on team name ComboBox:** The ComboBox is read-only (match opened in read-only mode). Use `ValuePattern.Current.Value` as first attempt; fall back to `.SelectedItem?.Text` or `.Name` if null. Verify correct property during development.

> **Note on popup menu rendering:** When the settings cog is clicked, the menu containing "Refresh All Scoreboards" may render as a separate top-level element outside the main window's automation tree. If `FindFirstDescendant` from the main window fails, search across `automation.GetAllTopLevelWindows()`.

---

## 8. Match Selection Logic

### Filter Approach
The match dialog filter fields (date pickers, combo boxes) have inconsistent or missing AutomationIds and do not persist state between sessions. Rather than attempting to set UI filters, the system loads all matches with default (empty) filters and performs date filtering in C# code.

### Sequence

```
1.  Open match dialog (Alt+F → Down 2 → Enter via FlaUI keyboard)
2.  Wait for dialog to appear
3.  Press Enter on search field — triggers search with empty/default filters
4.  Wait 200ms (race condition guard — allows spinner to appear)
5.  Poll LoaderSpinner.IsOffscreen every 100ms until true (max 30s timeout)
6.  Read all DataGrid rows via FlaUI
7.  Parse each row's Text property to extract: date, home team, away team, match type
8.  Filter rows where date == today (DateTime.Today)
9.  If 0 results → surface "No matches found for today" error to UI
10. If 1 result → auto-select and proceed
11. If 2+ results → present as selectable cards in web UI, wait for operator selection
12. On selection → find corresponding DataGrid row, select it, click "Open Read-Only" by Name
13. Wait for MatchLoaded state detection (max 15s)
```

### Match Card Display (multiple matches)
When more than one fixture is found for today, the web UI presents selectable cards before proceeding:

```
┌─────────────────────────────────────────┐
│  Select Today's Match                   │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │  1st XI vs Bexley CC            │    │
│  │  Home · T20 · 1:00pm            │    │
│  └─────────────────────────────────┘    │
│                                         │
│  ┌─────────────────────────────────┐    │
│  │  3rd XI vs Global CC, Kent      │    │
│  │  Away · 40 overs · 11:00am      │    │
│  └─────────────────────────────────┘    │
└─────────────────────────────────────────┘
```

---

## 9. Team Name Extraction

Executed immediately after match load, before the web UI shows "Match Loaded" status. Team names are stored in application state and will feed directly into Phase 2 (YouTube title update) with no additional work.

### Sequence

```
1. Invoke mnuScoring menu (by AutomationId)
2. Find and click "Match Details/Teams…" menu item (by Name)
3. Wait for dialog to appear (ControlType = Window, child of main window)
4. Find home team pane (by AutomationId)
5. Find home team ComboBox within pane (by AutomationId)
6. Read team name via ValuePattern.Current.Value
7. Repeat steps 4–6 for away team pane
8. Close dialog
9. Store { HomeTeam, AwayTeam } in application state
10. Push updated match state to all connected browsers via SignalR
```

---

## 10. Scoreboard Image Capture

### Capture Method
`PrintWindow` Win32 API with flag `PW_RENDERFULLCONTENT (0x00000002)`. This renders the WPF window content off-screen correctly regardless of:
- Window being behind other windows
- Hardware acceleration / DirectComposition
- Window being minimised

### Target Element
The scoreboard is a **dockable tool window within cricket.exe** (confirmed — not a separate process).

Located by:
- ClassName = `"itemsControlItem"`
- Name = `"Cricket.ReplayScreen.ViewModels.ScoreboardCommandViewModel"`

### Capture Sequence

```
1. Get main PCS Pro window handle (HWND) via FlaUI
2. Find scoreboard dockable window element, get BoundingRectangle
3. Capture full PCS Pro window via PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)
4. Crop bitmap to scoreboard element BoundingRectangle (relative to window origin)
5. Encode as JPEG (quality 75 — balance of size vs clarity)
6. SHA-256 hash the encoded bytes
7. Compare to previous frame hash — if identical, skip broadcast
8. If changed: store new hash, send base64-encoded JPEG via SignalR "ScoreboardUpdate" event
```

### Polling
Every **3 seconds** via `PeriodicTimer` background service. Only active when state = `MatchLoaded`. Stops automatically on transition to any other state.

### Browser Rendering
```javascript
connection.on("ScoreboardUpdate", (base64Jpeg) => {
    document.getElementById("scoreboard-preview").src =
        `data:image/jpeg;base64,${base64Jpeg}`;
});
```

---

## 11. Scoreboard Refresh

Triggered by operator clicking [⟳ Refresh Scoreboard] in the web UI. Forces PCS Pro to resend a complete data message to all connected scoreboards, recovering from lost COM port messages.

Only available (button enabled) when state = `MatchLoaded`.

### Sequence

```
1. Verify state == MatchLoaded (guard — reject if not)
2. Find settings cog by HelpText "Settings" within scoreboard tool window
3. Click cog — popup menu opens
4. Wait 200ms for menu to render
5. Search for "Refresh All Scoreboards" by Name
   Primary:  FindFirstDescendant from main window
   Fallback: Search across automation.GetAllTopLevelWindows()
6. Click "Refresh All Scoreboards"
7. Push "ScoreboardRefreshComplete" notification via SignalR
```

> **Note:** "Refresh All Scoreboards" refreshes all NV Play scoreboard outputs simultaneously — LED scoreboard on COM5 and any additional outputs (overlays, virtual scoreboards) if configured.

---

## 12. Web UI — Functional Specification

### Layout

```
┌──────────────────────────────────────────────────────────────┐
│  🏏 HHCC Match Control                    ● 1 user online   │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  MATCH & SCOREBOARD              🟢 Match Loaded             │
│  ┌────────────────────────────────────────────────────────┐  │
│  │                                                        │  │
│  │   High Halstow CC 3rd XI  vs  Global CC, Kent          │  │
│  │   Saturday 10 April 2026 · Away · 40 overs · 11:00am  │  │
│  │                                                        │  │
│  │   ┌────────────────────────────────────────────────┐   │  │
│  │   │                                                │   │  │
│  │   │        [scoreboard image preview]              │   │  │
│  │   │         updates automatically when data changes│   │  │
│  │   │                                                │   │  │
│  │   └────────────────────────────────────────────────┘   │  │
│  │                                                        │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                              │
│  PCS Pro: Match loaded (read-only)                           │
│                                                              │
│  [⟳ Refresh Scoreboard]          [⇄ Change Match]           │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### PCS Pro Status Indicator

| State | Indicator |
|---|---|
| `NotRunning` | ⚪ PCS Pro not running |
| `Launching` | 🟡 PCS Pro starting… |
| `LoginScreen` | 🟡 Logging in… |
| `MatchSelection` | 🟡 Loading matches… |
| `MatchSelectionSearching` | 🟡 Searching… |
| `MatchSelectionReady` | 🟡 Select a match |
| `MatchLoaded` | 🟢 Match loaded (read-only) |
| `Error` | 🔴 Error: [reason] |

### Button States

| Button | Enabled when | Disabled when |
|---|---|---|
| [⟳ Refresh Scoreboard] | `MatchLoaded` | Any other state |
| [⇄ Change Match] | `MatchLoaded` | Automation in progress |

### Change Match Flow

```
User clicks [⇄ Change Match]
    ↓
Modal: "Load a different match?
        PCS Pro will close and reopen with the new match.
        Are you sure?"
        [Cancel]   [Change Match]
    ↓ (on confirm)
Transition → MatchSelection — re-run full automation sequence
```

### Error Display

```
┌────────────────────────────────────────────────────┐
│  🔴 Error                                          │
│                                                    │
│  PCS Pro login timed out after 20 seconds.         │
│  Check that PCS Pro is reachable and the           │
│  login credentials are correct.                    │
│                                                    │
│  [⟳ Retry]                                         │
└────────────────────────────────────────────────────┘
```

### Multi-User Behaviour
- Connected user count shown in header via SignalR presence tracking
- All control buttons disabled for all connected users while automation is executing, with "Automation in progress…" tooltip
- All state changes pushed to all connected clients simultaneously — no client ever has a stale view
- State machine prevents conflicting actions naturally — no additional locking required

---

## 13. Configuration

| Setting | Type | Notes |
|---|---|---|
| `PcsPro:ExecutablePath` | string | Full path to `cricket.exe` |
| `PcsPro:WorkingDirectory` | string | Working directory for process launch |
| `PcsPro:Password` | **secret** | Login credential — never in source control |
| `Scoreboard:CaptureIntervalSeconds` | int | Default: 3 |
| `Scoreboard:JpegQuality` | int | Default: 75 (0–100) |
| `Web:Port` | int | Default: 5050 |
| `Web:AllowedHosts` | string | Restrict to local network if required |

> ⚠️ **PCS Pro Password Risk:** Two different passwords were found in the recovered scripts, indicating the credential has been reset at least once. It is unknown whether this password is club-controlled or managed by ECB / NV Play. Ensure the password update process is documented and the config location is known to more than one committee member.

---

## 14. Non-Functional Requirements

| Requirement | Target |
|---|---|
| State transition latency | < 500ms from user action to UI feedback |
| Scoreboard image latency | ≤ 3 seconds from data change to browser update |
| Automation reliability | Zero coordinate-based clicking anywhere in the system |
| Error handling | All errors surface to UI with actionable message — no silent failures |
| Concurrent users | 2 simultaneous browser sessions supported gracefully |
| Browser support | Chrome / Edge — desktop only for v1 |
| OS | Windows 10 / Windows 11 |
| Runtime | .NET 8 |
| Testability | All automation behind `IPcsProAutomationService` — full UI testable via mock without PCS Pro |
| Observability | Serilog structured logging on every element lookup, state transition, and error — diagnosable from log file alone |

---

## 15. Open Items — Verify During Development

| # | Item | Action Required |
|---|---|---|
| 1 | Exact AutomationId values for login fields, team panes, team name ComboBoxes | Confirm with Inspect.exe during implementation |
| 2 | `ValuePattern.Current.Value` vs `SelectedItem?.Text` for read-only team name ComboBox | Quick test during development — use whichever returns the team name string |
| 3 | Popup menu render scope for "Refresh All Scoreboards" | Test whether found via `FindFirstDescendant` from main window, or requires all-windows search |
| 4 | PCS Pro password ownership (club-controlled vs ECB-managed) | Clarify with NV Play / ECB support |
| 5 | Remote scorer connection method (club LAN vs 4G / mobile data) | Check with club network administrator — affects read-only data feed reliability |

---

## 16. Known Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| PCS Pro UI update changes element Names or AutomationIds | Low | High | Log all FlaUI element lookups; surface element-not-found as a named error state rather than exception |
| PCS Pro password reset by ECB without notice | Medium | High | Clear error message on login failure; password stored in config for easy update |
| LoaderSpinner race condition | Low | Medium | 200ms pre-poll guard + 30s timeout with structured error |
| Popup menu renders outside main window automation tree | Medium | Low | All-windows fallback search already specified |
| COM5 port reassignment on USB replug | Low | Medium | Documented as known operational risk; requires NV Play config update if it occurs |

---

## 17. Phase 2 Preview — Live Streaming

Phase 2 builds directly on Phase 1 with no architectural changes required. The following will be added:

**New capabilities:**
- YouTube Data API v3 integration (OAuth2 with stored refresh token)
- `liveBroadcasts.insert` — create a new broadcast per match
- `liveStreams.list` — retrieve existing stream ID (stream key remains in NV Play config)
- `liveBroadcasts.bind` — bind new broadcast to existing stream
- `liveBroadcasts.update` — set title from team names (already available from Phase 1)
- `liveBroadcasts.transition(complete)` — cleanly end broadcast at close of play
- Start Stream FlaUI automation (Start Live Stream button in PCS Pro)
- End Stream FlaUI automation

**Web UI additions:**
- YouTube Stream section (below Match & Scoreboard section)
- Stream status indicator (Idle / Connecting / Live / Ended)
- Live viewer count, stream duration, YouTube watch link
- [▶ Start Stream] / [⏹ End Stream] buttons
- End Stream two-step confirmation modal with interval guidance
- Change Match warning modal when stream is active

**State machine additions:**
- `StreamConnecting` — NV Play Start Live Stream clicked, awaiting RTMP confirmation
- `StreamLive` — RTMP active, broadcast live on YouTube
- `StreamEnding` — transition(complete) called, awaiting confirmation

**Key operational note for Phase 2:**
Stream should remain running throughout tea, lunch, and rain delays. NV Play continues pushing RTMP with whatever the cameras and scoreboard are showing. [⏹ End Stream] is only used at the close of play. A holding screen or camera view during breaks is preferable to stopping and restarting the stream, as YouTube cannot resume a broadcast once ended.

---

*Document version 1.0 — Phase 1 only*
*Phase 2 PRD to be produced separately once Phase 1 is delivered and validated*
*Based on full analysis of recovered HHCC Garage PC files and live PCS Pro UI inspection via Inspect.exe*
*April 2026*
