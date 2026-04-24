# HLPS-016: Operator Mode & Resilience — Post-Production Hardening Round 2

| Field | Value |
|---|---|
| **Document** | HLPS-016-Operator-Mode-And-Resilience.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.4 |
| **Date** | 2026-04-24 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Dependencies** | HLPS-010 (delivered automation hardening round 1), HLPS-005 (manual mode infrastructure), HLPS-011 (operational UX) |
| **Reference Implementation** | Existing production code — `src/PcsRemote.Automation/`, `src/PcsRemote.Core/`, `src/PcsRemote.Web/`, `src/PcsRemote.TrayHost/` |

---

## 1. Problem Statement

The first production run exposed four operator-facing pain points:

> "The automation keeps freaking out because of 'unexpected dialog' when there is no dialog visible on the app. Reduce screen grab frequency — it's impossible to use the machine when the automation is running. We need a special key combination that toggles automation into/out of manual mode. The make-foreground window is also un-maximising the window — which is a pain because the scoreboard capture shrinks and is barely readable when not maximised."

These decompose into four distinct problems:

1. **False-positive unexpected-dialog crashes.** The unexpected-dialog detection probe misidentifies transient WPF popup elements as unexpected dialogs. A single false sighting fires `PcsProTrigger.UnexpectedDialog`, which transitions the state machine to `Error` from runtime states (`MatchLoaded`, `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`). This halts the automation and requires operator intervention to recover. Root causes:
   - The known-dialog whitelist only matches two dialog names ("Open Match" and "Match Details/Teams") plus a login password-field check. Constants already exist in `KnownElements` for Video Consent, Match Centre, and Add Live Stream dialogs, but these are **not referenced** by the whitelist.
   - Visible WPF popup host elements pass the existing offscreen/zero-size filter and are flagged as unexpected dialogs.
   - A single transient sighting (one probe tick) is treated as a confirmed dialog. No hysteresis exists.
   - Runtime probe sites log "unexpected dialog detected" with no identifying information — only the login phase captures and logs the dialog name.

2. **Scoreboard capture too frequent.** The scoreboard polling service defaults to a 2-second capture interval. The constant bring-to-foreground + screen-capture cycle at this frequency makes the garage PC unusable during a loaded match.

3. **No quick escape from automation.** The operator needs a keyboard shortcut to instantly pause all automation (scoreboard polling, health-check tick, UI-driven automation commands) so they can use PCS Pro manually. The existing `IManualModeService` and `ManualModeService` provide the contract and implementation, and the tray menu already has a toggle — but there is no global hotkey, the scoreboard polling loop does not check manual mode, the automation service does not short-circuit on manual mode, and resuming from manual mode does not fire an immediate scoreboard capture to bring the stream overlay back up to date.

4. **`BringToForeground` un-maximises PCS Pro.** The bring-to-foreground helper calls `ShowWindow` with `SW_RESTORE` unconditionally. `SW_RESTORE` against a maximised window restores it to its previous "normal" size, shrinking the scoreboard capture to an unreadable size on the live stream.

---

## 2. Scope

### Existing Infrastructure

This HLPS builds on established manual-mode infrastructure delivered in HLPS-005 and subsequent rounds:

| Component | Location | Current State |
|---|---|---|
| `IManualModeService` | `src/PcsRemote.Core/IManualModeService.cs` | Interface with `IsManualModeActive`, `Enable()`, `Disable()`, `ManualModeChanged` event |
| `ManualModeService` | `src/PcsRemote.Web/Services/ManualModeService.cs` | Thread-safe singleton (Interlocked CAS). Currently in the Web layer. |
| `ManualModeBanner.razor` | `src/PcsRemote.Web/Shared/ManualModeBanner.razor` | Renders "Manual mode — automation paused by local operator" banner |
| Tray menu toggle | `src/PcsRemote.TrayHost/TrayApplicationContext.cs` | "Switch to Manual Mode" / "Resume Automation" context menu item |
| UI button disabling | `Index.razor`, `ChangeMatchButton.razor`, `RefreshScoreboardButton.razor`, `ErrorDisplay.razor` | Buttons already check `ManualModeService.IsManualModeActive` and disable accordingly |
| `WinFormsHostedService` | `src/PcsRemote.TrayHost/WinFormsHostedService.cs` | Dedicated STA thread with WinForms message loop — available for `WM_HOTKEY` dispatch |

### In Scope

#### 2.1 Unexpected Dialogs: Stop Crashing — Log a Warning and Carry On

**This is the single most important change in this HLPS.** Today, when the automation detects anything it considers an "unexpected dialog" during a runtime operation (scoreboard refresh, scoreboard capture, change-match, team-names, load-match, use-current-match, match-search), it fires `PcsProTrigger.UnexpectedDialog`, the state machine transitions to `PcsProState.Error`, the operation aborts, and the operator must manually recover. In production, these detections are overwhelmingly false positives — there is no visible dialog on the application.

**The fundamental change:** unexpected dialogs detected during runtime operations are **demoted from fatal errors to logged warnings**. The automation logs diagnostic information and then **continues the operation as if nothing happened** — it does **not** attempt to close or interact with the dialog in any way, because clicking unknown buttons (Cancel, Close, confirmation prompts) risks making the situation worse. The state machine never transitions to `Error` from this trigger during runtime states.

Specifically:

**State machine:** Remove the `UnexpectedDialog → Error` transition from these four runtime states: `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, and `MatchLoaded`. The trigger is **retained** on `Launching` and `LoginScreen` only (these transitions already exist in the current state machine — no additions required).

**Runtime probe sites:** Replace the current pattern of `detect → fire trigger → abort operation` with:
1. Log a structured `Warning` including the dialog's `Name`, `AutomationId`, `ClassName`, and `BoundingRectangle`. This diagnostic data is essential for investigating any genuine future issues.
2. **Continue the operation** — no state transition, no early return, no error, and **no attempt to close or interact with the dialog**.

**Login phase** retains its current fatal behaviour unchanged.

**Downstream behaviour when the dialog is genuine:** If a real dialog is present and blocking the UI, subsequent FlaUI interactions within the same operation may fail through normal exception paths (element not found, click timeout, etc.). These failures are handled by the existing error-handling infrastructure — they are not suppressed, swallowed, or retried. The automation surfaces the failure to the dashboard through its existing mechanisms. This HLPS does not introduce any new retry or recovery logic for this scenario.

#### 2.2 Defence-in-Depth: Reduce False-Positive Detection Noise

The demotion in §2.1 means false positives no longer crash the automation. The following three improvements are **secondary refinements** that reduce the volume of false-positive warning entries in the logs, making genuine issues easier to spot:

##### 2.2a `IsKnownDialog` Whitelist Extension

Extend the known-dialog whitelist to recognise three dialog patterns whose constants already exist in `KnownElements` but are **not referenced** by the whitelist:

| Dialog | Match Type |
|---|---|
| Video Consent | `StartsWith` — name includes match-specific suffixes |
| Match Centre | `StartsWith` |
| Add Live Stream | `StartsWith` |

These are legitimate PCS Pro modals that appear during streaming setup. Whitelisting them eliminates a known source of false-positive warnings.

##### 2.2b WPF Popup ClassName Filter

Add a `ClassName`-based filter to the unexpected-dialog detection so that child windows whose `ClassName` matches WPF popup-host patterns are excluded before reaching the known-dialog check. Provisional baseline patterns (pending garage-PC verification per U-1): `Popup`, `PopupRoot`, `AdornerDecorator`, and any `ClassName` containing `HwndWrapper` and `Popup`. These are WPF framework elements that expose `ControlType.Window` but are not user-facing dialogs.

##### 2.2c Two-Tick Hysteresis for Dialog Detection

Introduce a per-call-site hysteresis counter so that a non-known visible dialog must be observed on **two consecutive probe ticks of the same call site** before it is reported as a warning. A single transient sighting (one tick) is suppressed silently. The hysteresis state must live on the automation instance (the shared helper is stateless).

**Applicability:** Hysteresis applies only to **looped probe sites** (e.g., scoreboard polling, health-check tick) where consecutive ticks are a natural concept. **Single-shot probe sites** (one dialog check per operation invocation) are exempt from hysteresis — they log a warning immediately on detection, because a "second consecutive tick" would not occur until the next independent operation.

**Edge cases:** If a different dialog (different `Name` or `AutomationId`) is observed on the second tick, the counter resets and the new dialog is treated as tick 1 of a new sequence. After the counter fires (warning logged), it resets so that a persistent dialog logs once per two-tick cycle, not every tick thereafter.

#### 2.3 Scoreboard Capture Interval Default Change

Change the default scoreboard capture interval from 2 seconds to 10 seconds. This reduces the bring-to-foreground + screen-capture frequency to a level that does not dominate the machine's foreground.

Any existing override in `appsettings.json` continues to take precedence.

#### 2.4 Manual-Mode Service-Layer Wiring

Wire the existing `IManualModeService` into backend services that currently do not check it:

**Scoreboard polling:** On each timer tick, check manual-mode state. If active, skip the capture (do not stop the loop). When manual mode is toggled OFF, fire one immediate capture so the stream overlay catches up.

**Health-check tick:** The periodic health-check tick must also check manual-mode state. If active, skip the health check (do not stop the loop). Resume checking on the next tick after manual mode is toggled OFF — no immediate health check is needed on resume.

**Automation service:** At the entry of every public automation-driving method, check manual-mode state. If active, log and return early — no state change, no exception. **Lifecycle methods bypass the gate** (`LaunchAndLoginAsync`, `StopAsync`, `RecoverFromErrorAsync`, `DismissErrorAsync`) so the operator can still recover or restart from the dashboard.

**In-flight operations:** If an automation operation is already executing when the hotkey is pressed, that operation runs to completion. Manual mode only prevents **new** operations from starting. The worst-case latency between hotkey press and effective pause is bounded by the duration of the longest in-flight automation operation.

#### 2.5 `IManualModeService` Contract Extension — `Toggle()` Method

Add a `Toggle()` method to the existing `IManualModeService` interface so the hotkey handler can call a single method without needing to read-then-branch on current state.

**Concrete implementation relocation:** `ManualModeService` currently resides in the Web layer but has no Web-layer dependencies. Move it to `PcsRemote.Core` so it is co-located with its interface, consistent with the architecture rule that Core has zero dependencies on Web/Automation/TrayHost. This is a namespace move — no behavioural change.

#### 2.6 Global Ctrl+Alt+P Hotkey

Register a global hotkey (Ctrl+Alt+P) via Win32 `RegisterHotKey` in `PcsRemote.TrayHost`. The hotkey rides the existing WinForms STA message loop. On receipt, it calls `IManualModeService.Toggle()`.

**Tray feedback:** On every toggle, show a tray balloon notification indicating whether manual mode is ON or OFF. If hotkey registration fails at startup, show a tray balloon warning the operator ("Manual-mode hotkey unavailable — use tray menu") so they learn immediately rather than discovering it when Ctrl+Alt+P does nothing.

#### 2.7 `BringToForeground` Window Placement Preservation

Modify the bring-to-foreground helper to query the current window placement before calling `ShowWindow` and choose the correct show command:

- **Maximised window** → use a show command that preserves the maximised state (not `SW_RESTORE`)
- **Minimised window** → restore to previous size (existing behaviour)
- **Normal window** → bring to front without changing size

#### 2.8 Blazor UI Enhancements for Manual Mode

The existing Blazor infrastructure (§2, Existing Infrastructure table) already handles banner display and button disabling. Enhancements needed:

- **`StreamingControls.razor`**: Currently does not check `ManualModeService`. Add manual-mode subscription and button disabling consistent with the pattern used in `ChangeMatchButton.razor` and `RefreshScoreboardButton.razor`.
- **Banner text enhancement**: Update `ManualModeBanner.razor` to include the hotkey hint: "Manual mode — automation paused (Ctrl+Alt+P to resume)".

#### 2.9 Documentation Updates

- **Configuration Guide** (`docs/guides/Configuration-Guide.md`): Document the updated `Scoreboard:CaptureIntervalSeconds` default (10s).
- **Operational Guide** (`docs/guides/Operational-Guide.md`): Document the Ctrl+Alt+P hotkey and manual-mode behaviour.
- **DEFERRED-ITEMS.md** reconciliation: Update status of any items resolved by this HLPS.

### Out of Scope

- Replacing `Capture.Rectangle` with DWM thumbnail capture — would reduce CPU further but is a significantly larger change with different failure modes.
- Persisting manual-mode state across restarts — toggle is session-scoped by design. Default at startup is automation-active; operators must re-engage manual mode after any restart.
- Tray menu duplicate of the hotkey — the existing "Switch to Manual Mode" menu item is sufficient. A hotkey-only alternative can be added later if the hotkey is intercepted by another application.
- Rate-limiting or bursting the polling cadence based on detected scoreboard change.
- Extending `GetUnexpectedDialogName()` to all `FlaUi*Automation` classes (currently login-only). The structured warning logging in §2.1 provides equivalent diagnostic data at every probe site.

---

## 3. Non-Goals

- No new testing frameworks. All tests use the existing MSTest 3.x + FluentAssertions ≥ 8.0 stack; bUnit for Blazor components.
- No changes to the state machine beyond removing the four `UnexpectedDialog` transitions specified in §2.1.
- No changes to the automation orchestration flow (operation ordering, lock semantics, crash watcher) beyond the manual-mode gate and unexpected-dialog demotion.
- No PCS Pro version discovery or version-specific element handling.

---

## 4. Architecture & Design

### 4.1 Manual-Mode Data Flow

```
Ctrl+Alt+P (global hotkey)
    │
    ▼
Hotkey handler (TrayHost STA thread)
    │
    ▼
IManualModeService.Toggle()
    │
    ├── ManualModeChanged event ──► ScoreboardPollingService (skip tick / resume capture)
    │                            ├► PcsProAutomationService (checks gate at method entry)
    │                            ├► ManualModeBanner.razor (banner text)
    │                            ├► UI components (button disable)
    │                            └► TrayApplicationContext (icon + menu text + balloon)
    │
    └── (return to message loop)
```

### 4.2 Layer Dependencies

```
PcsRemote.Core (zero external dependencies)
  ├── IManualModeService (interface) ← existing
  ├── ManualModeService (concrete) ← MOVED from Web
  └── PcsProStateMachine ← transitions edited

PcsRemote.Automation (depends on Core)
  ├── UIAutomationHelpers ← BringToForeground fix, whitelist, popup filter, hysteresis
  └── PcsProAutomationService ← manual-mode gate, probe-site demotion, health-check gate

PcsRemote.Web (depends on Core)
  ├── ScoreboardPollingService ← manual-mode gate, default interval, resume capture
  ├── StreamingControls.razor ← manual-mode subscription
  └── ManualModeBanner.razor ← banner text update

PcsRemote.TrayHost (depends on Core, Web)
  ├── Hotkey registrar (new) ← global hotkey → Toggle()
  └── TrayApplicationContext ← balloon on toggle
```

### 4.3 Hysteresis Design

Each looped probe call site maintains its own independent counter on the automation instance (not shared across call sites). A non-known visible dialog must be observed on two consecutive ticks at the same call site before it is reported as a warning. A single sighting increments the counter but suppresses the warning. A clean tick resets the counter. If a different dialog is observed on the second tick, the counter resets. After firing, the counter resets so a persistent dialog logs once per two-tick cycle. Single-shot probe sites (one check per operation) are exempt and log immediately.

---

## 5. Acceptance Criteria

| ID | Criterion | Verification |
|---|---|---|
| AC-1 | Operator triggering Ctrl+Alt+P pauses scoreboard polling, health checks, and all UI-driven automation actions. New operations are rejected; in-flight operations complete. Banner appears in the dashboard. Tray balloon shows "Manual Mode ON". | Integration test + manual verification |
| AC-2 | Releasing manual mode (second Ctrl+Alt+P) fires one immediate scoreboard capture; banner clears; tray balloon shows "Manual Mode OFF". | Integration test + manual verification |
| AC-3 | Lifecycle controls (`LaunchAndLoginAsync`, `StopAsync`, `RecoverFromErrorAsync`, `DismissErrorAsync`) remain functional in manual mode. | Unit tests |
| AC-4 | An unexpected dialog detected during a runtime probe (scoreboard refresh / capture / change-match / team-names / load-match / use-current-match / match-search) logs a structured warning with `Name`, `AutomationId`, `ClassName`, `BoundingRectangle`, does **not** attempt to close or interact with the dialog, and the operation continues — state never transitions to `Error` from this trigger in those states. | Unit tests + code review |
| AC-5 | Login phase retains current behaviour: an unexpected dialog still transitions to `Error`. | Existing tests (regression) |
| AC-6 | A WPF popup whose `ClassName` matches the WPF popup-host pattern is not flagged as an unexpected dialog. | Unit tests |
| AC-7 | For **looped probe sites**, a non-known visible dialog seen on a single probe tick does not fire — it must be observed on two consecutive probe ticks of the same call site (hysteresis). **Single-shot probe sites** (one check per operation) are exempt and log immediately on detection. | Unit tests |
| AC-8 | Default `Scoreboard:CaptureIntervalSeconds` is `10`. Existing override in `appsettings.json` continues to take precedence. | Unit tests + config review |
| AC-9 | `BringToForeground` against a maximised PCS Pro window leaves it maximised. Against a minimised window, it restores it. Against a normal-sized window, it brings to front without changing size. | Unit tests (seam-based) + manual verification |
| AC-10 | All new code paths covered by tests using existing frameworks (MSTest 3.x + FluentAssertions ≥ 8.0; bUnit for Blazor). No `Thread.Sleep` in tests. | Test run + code review |

---

## 6. Risks & Open Questions

| ID | Risk / Question | Likelihood | Impact | Mitigation / Status |
|---|---|---|---|---|
| R-1 | WPF popup `ClassName` patterns may vary across PCS Pro versions or .NET WPF runtime versions. The proposed filter is based on standard WPF internals but has not been exhaustively catalogued on the garage PC. | Medium | Low | The new structured warning logging (§2.1) will expose any remaining false positives with full `ClassName` data. Pattern can be refined in a subsequent release. Hysteresis (§2.2c) provides a second defence layer. |
| R-2 | `RegisterHotKey` may fail if Ctrl+Alt+P is already registered by another application on the garage PC. | Low | Low | Log a warning, show a tray balloon ("Manual-mode hotkey unavailable — use tray menu"), and fall back to tray-menu-only toggle. Banner text omission of hotkey hint is accepted as a minor cosmetic inconsistency in this rare scenario. |
| R-3 | Moving `ManualModeService` from Web to Core changes namespace. All `using` statements referencing `PcsRemote.Web.Services.ManualModeService` need updating. | Low | Low | Mechanical — caught by compiler. No behavioural change. |
| R-4 | The immediate-resume capture (§2.4) fires after manual mode is toggled off. If the operator has closed PCS Pro or the match is no longer loaded, the capture will fail. | Low | Low | Capture failures are already handled gracefully by the existing `ScoreboardPollingService` loop (catches and logs, continues). |
| R-5 | `GetWindowPlacement` P/Invoke adds a new Win32 interop dependency to `UIAutomationHelpers`. | Low | Low | Class already uses multiple P/Invoke declarations (`ShowWindow`, `SetForegroundWindow`, `GetForegroundWindow`, etc.). Consistent pattern. |

---

## 7. Unknowns Register

| ID | Description | Owner | Blocking? | Resolution |
|---|---|---|---|---|
| U-1 | Exact set of WPF `ClassName` patterns to exclude in the popup filter — need to confirm against live PCS Pro window tree dump on garage PC. | Agent | No | Non-blocking: proposed patterns cover documented WPF internals. Structured warning logging (§2.1) will capture any remaining false positives for iterative refinement. |
| U-2 | Adding `Toggle()` to `IManualModeService` requires updating all in-tree implementations and test mocks. Confirm no external consumers exist beyond `ManualModeService` and test mocks. | Agent | No | Codebase audit shows only `ManualModeService` implements the interface. Test mocks use `Moq` or manual stubs — all in-tree. Compiler-enforced; safe to extend. |

---

## 8. Test Strategy

### 8.1 Unit Tests (MSTest 3.x + FluentAssertions ≥ 8.0)

| Area | Coverage |
|---|---|
| `ManualModeService.Toggle()` | Toggle from inactive → active, active → inactive, event firing, thread-safety (concurrent toggles) |
| `PcsProStateMachine` | Verify `UnexpectedDialog` is **not** permitted from `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, `MatchLoaded`. Verify it **is** permitted from `Launching`, `LoginScreen`. |
| `UIAutomationHelpers.IsKnownDialog` | New whitelist entries match expected dialog names (substring/StartsWith) |
| `UIAutomationHelpers` popup filter | WPF popup classnames are excluded before reaching `IsKnownDialog` |
| Hysteresis | Counter increments on non-known dialog, resets on clean probe, fires on second consecutive tick; single-shot probes fire immediately; different dialog on second tick resets counter; counter resets after firing (persistent dialog logs once per two-tick cycle) |
| `BringToForeground` placement logic | Seam-based: verify correct `ShowWindow` command selected for maximised/minimised/normal `showCmd` values |
| `ScoreboardPollingService` | Default interval is 10s; skip-when-manual; immediate-capture-on-resume |
| Health-check tick | Skip-when-manual; resume on next scheduled tick after toggle-off |
| `PcsProAutomationService` manual-mode gate | Automation-driving methods return early when manual mode active; lifecycle methods bypass gate |

### 8.2 Component Tests (bUnit)

| Component | Coverage |
|---|---|
| `ManualModeBanner.razor` | Banner text includes hotkey hint when active |
| `StreamingControls.razor` | Buttons disabled when manual mode active |

### 8.3 Manual Verification (Garage PC)

| Test | Coverage |
|---|---|
| Ctrl+Alt+P toggles manual mode, balloon appears | AC-1, AC-2 |
| Scoreboard capture resumes immediately on mode-off | AC-2 |
| Maximised PCS Pro stays maximised after `BringToForeground` | AC-9 |
| Unexpected-dialog probe with live WPF popups does not crash | AC-4, AC-6, AC-7 |

---

## 9. Rollout

1. Deliver all IS steps derived from this HLPS on feature branches, squash-merged to `master` after quality gate.
2. Build and publish updated installer (HLPS-014 WiX pipeline).
3. Deploy to garage PC and run manual verification suite (§8.3).
4. Monitor structured warning logs on next match day for any remaining false-positive dialog detections.

---

## 10. Appendix

### A. File Reference Map

| File | HLPS Section |
|---|---|
| `src/PcsRemote.Core/IManualModeService.cs` | §2.5 |
| `src/PcsRemote.Core/PcsProStateMachine.cs` | §2.1 |
| `src/PcsRemote.Web/Services/ManualModeService.cs` (moves to Core) | §2.5 |
| `src/PcsRemote.Web/ScoreboardPollingService.cs` | §2.3, §2.4 |
| `src/PcsRemote.Automation/UIAutomationHelpers.cs` | §2.2, §2.7 |
| `src/PcsRemote.Automation/KnownElements.cs` | §2.2a |
| `src/PcsRemote.Automation/PcsProAutomationService.cs` | §2.1, §2.4 |
| `src/PcsRemote.Automation/FlaUiLoginAutomation.cs` | §2.1 |
| `src/PcsRemote.TrayHost/TrayApplicationContext.cs` | §2.6 |
| `src/PcsRemote.TrayHost/WinFormsHostedService.cs` | §2.6 |
| `src/PcsRemote.Web/Shared/ManualModeBanner.razor` | §2.8 |
| `src/PcsRemote.Web/Shared/StreamingControls.razor` | §2.8 |

### B. Decisions Already Made (Do Not Re-Litigate)

These decisions were made with the user prior to HLPS authoring and are recorded here for traceability:

1. `UnexpectedDialog` → warning, not crash, on runtime states only. Stays fatal on `Launching` / `LoginScreen`.
2. **No close/interaction attempt** on unexpected dialogs — log details and carry on. Clicking unknown buttons risks making things worse.
3. Scoreboard capture default: 2s → 10s (configurable via existing `Scoreboard:CaptureIntervalSeconds`).
4. Hotkey: Ctrl+Alt+P registered globally via Win32 `RegisterHotKey` from `PcsRemote.TrayHost`.
5. Manual-mode semantics: pause scoreboard polling, health-check tick, and all automation-driving button-handlers. Lifecycle methods bypass the gate. Session-scoped.
6. `BringToForeground` must preserve maximised state — query placement before showing.
7. If hotkey registration fails, the banner text may still show the hotkey hint — accepted as a minor cosmetic inconsistency in this rare scenario (R-2). Tray balloon warns operator at startup.

---

## 11. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-24 | Opus 4.7, GPT 5.4, Sonnet 4.6 | REVISE — 4 HIGH (health-check gap, hysteresis contradiction, single-shot suppression, no downstream contract), 7 MEDIUM accepted, 6 LOW accepted, 6 deferred to delivery, 1 downgraded, 1 rejected. All accepted findings resolved in v0.3. |
| R2 | 2026-04-24 | Opus 4.7, GPT 5.4, Sonnet 4.6 | Opus APPROVE, Sonnet APPROVE (1 LOW accepted), GPT REVISE — 1 HIGH (AC-7 missing single-shot exemption), 1 MEDIUM (§8.1 missing post-fire test). All resolved in v0.4. |
| R3 | 2026-04-24 | GPT 5.4 (targeted) | APPROVE — R2 fixes verified, no regressions. **Unanimous approval: Opus 4.7 ✅, GPT 5.4 ✅, Sonnet 4.6 ✅** |
