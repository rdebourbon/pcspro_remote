# IS-016: Operator Mode & Resilience — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-016-Operator-Mode-And-Resilience.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.4 |
| **Date** | 2026-04-24 |
| **Governing HLPS** | HLPS-016-Operator-Mode-And-Resilience.md (APPROVED v0.4) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Prerequisites** | HLPS-010 (automation hardening round 1 — all 12 steps delivered) |

---

## Overview

This IS breaks HLPS-016 into 9 atomic steps. Each step is independently mergeable to `master` via squash-merge after passing the adversarial quality gate. Steps are ordered to minimise merge conflicts and establish infrastructure before consumers.

**Ordering rationale:**
- S-001 delivers the `IManualModeService.Toggle()` extension and concrete relocation — foundation for S-006 (service-layer wiring) and S-008 (hotkey).
- S-002 and S-003 address the unexpected-dialog false-positive problem (state machine + probe sites, then detection tightening). S-002 comes before S-003 because the demotion (stop crashing) is the highest-priority fix and is independent of the detection improvements.
- S-004 and S-005 are small surgical fixes with no dependencies on S-001–S-003 and can be delivered in any order.
- S-006 wires manual-mode into backend services (depends on S-001 for `Toggle()`).
- S-007 delivers the Blazor UI enhancements (depends on S-001 for `Toggle()` and S-008 for the hotkey the banner advertises).
- S-008 delivers the global hotkey (depends on S-001 for `Toggle()`). Delivered before S-007 so the banner does not advertise a non-existent hotkey.
- S-009 is documentation-only and is always last.

Step IDs are stable. Deferred steps leave gaps; IDs are never renumbered.

---

## S-001 — `IManualModeService` Contract Extension and Concrete Relocation

**SPEC required:** Yes — `SPEC-S-001-ManualModeService-Extension.md`

**What changes:** The existing `IManualModeService` interface in `PcsRemote.Core` gains a `Toggle()` method. The concrete `ManualModeService` class moves from `PcsRemote.Web.Services` to `PcsRemote.Core` (it has no Web-layer dependencies). DI registration in `WebApplicationBuilderExtensions` is updated to reference the Core type. Test mocks are updated.

**Why:** HLPS-016 §2.5. The hotkey handler (S-008) needs a single `Toggle()` call. The relocation aligns with the architecture rule that Core has zero dependencies on Web/Automation/TrayHost and ensures the concrete is co-located with its interface. Addresses AC-1, AC-2 (infrastructure for toggle behaviour).

**Dependencies:** None — this is the foundation step.

**Files affected:**
- `src/PcsRemote.Core/IManualModeService.cs` (add `Toggle()`)
- `src/PcsRemote.Core/ManualModeService.cs` (moved from `src/PcsRemote.Web/Services/ManualModeService.cs`)
- `src/PcsRemote.Web/Services/ManualModeService.cs` (deleted)
- `src/PcsRemote.Web/WebApplicationBuilderExtensions.cs` (update DI registration)
- Test files: mock updates for new `Toggle()` method

**Acceptance criteria:**
- `IManualModeService.Toggle()` exists and is atomic (single CAS operation).
- `ManualModeService` resides in `PcsRemote.Core` namespace.
- Existing tests pass (no behavioural change to `Enable()`/`Disable()`).
- New unit tests for `Toggle()`: inactive → active, active → inactive, event fired on each transition, no-op on concurrent identical toggles.

**Verification intent:** Build + test. No manual verification needed.

---

## S-002 — Demote Runtime `UnexpectedDialog` from Fatal to Warning

**SPEC required:** No — the changes are mechanical: remove four state machine transitions, change probe-site error handling from fire-trigger-and-abort to log-warning-and-continue, add structured diagnostic fields. All design decisions are specified in HLPS-016 §2.1.

**What changes:** Two layers are modified:

1. **State machine:** Remove `PcsProTrigger.UnexpectedDialog → PcsProState.Error` from `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, `MatchLoaded`. Keep on `Launching` and `LoginScreen`.

2. **Probe sites in `PcsProAutomationService`:** At each runtime probe site, replace the current `FireErrorUnderLockAsync(PcsProTrigger.UnexpectedDialog, ...)` pattern with:
   - Structured `Warning` log including `Name`, `AutomationId`, `ClassName`, `BoundingRectangle` of the unexpected dialog.
   - Continue the operation — no state transition, no early return, no error, and **no attempt to close or interact with the dialog**.

**Why:** HLPS-016 §2.1. This is the highest-priority fix — stops the automation from crashing on false positives. Addresses AC-4 (structured warning + continue), AC-5 (login retains fatal behaviour).

**Dependencies:** None — can be delivered independently.

**Files affected:**
- `src/PcsRemote.Core/PcsProStateMachine.cs` (remove 4 transitions)
- `src/PcsRemote.Automation/PcsProAutomationService.cs` (~7 probe sites)
- `src/PcsRemote.Automation/FlaUiLoginAutomation.cs` — **not modified** (login probe site retains fatal behaviour unchanged; listed in HLPS Appendix A for context only)
- Test files: state machine tests (new negative-path tests for removed transitions), automation service tests (verify continue-after-dialog behaviour)

**Acceptance criteria:**
- `PcsProTrigger.UnexpectedDialog` throws `InvalidOperationException` from `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, `MatchLoaded` (trigger not permitted).
- `PcsProTrigger.UnexpectedDialog` succeeds from `Launching` and `LoginScreen` (unchanged).
- Runtime probe sites log structured warning with 4 diagnostic fields and continue.
- Login probe site (`HandleUnexpectedDialogAsync`) is unchanged.
- When the structured warning fires and the dialog is genuinely blocking, the next FlaUI interaction in the same operation surfaces a failure through the existing error-handling path (not suppressed by the demotion change).
- No test regressions.

**Verification intent:** Unit tests + code review. Manual verification on next match day (log inspection).

---

## S-003 — `IsKnownDialog` Whitelist Extension + WPF Popup Filter + Hysteresis

**SPEC required:** Yes — `SPEC-S-003-Dialog-Detection-Hardening.md`

**What changes:** Three improvements to dialog detection in `UIAutomationHelpers`:

1. **Whitelist extension:** `IsKnownDialog` gains `StartsWith` matches for `VideoConsentDialogNamePattern`, `MatchCentreDialogNamePattern`, `MatchCentreDialogFallbackPattern` (constants already in `KnownElements.cs`).

2. **WPF popup filter:** `HasUnexpectedDialog` filters out child windows whose `ClassName` contains WPF popup-host patterns before reaching the `IsKnownDialog` check.

3. **Two-tick hysteresis:** A per-call-site counter ensures a non-known dialog must be seen on two consecutive probe ticks before being confirmed. Counter state lives on each automation instance. **Single-shot probe sites** (one check per operation invocation) are exempt from hysteresis and log immediately on detection. Counter implementation strategy (shared abstraction vs. per-class field) is resolved in `SPEC-S-003`.

**Why:** HLPS-016 §2.2. These three changes work together to virtually eliminate false-positive dialog detections. S-002 makes false positives non-fatal; S-003 prevents them from occurring in the first place. Addresses AC-6 (popup filter), AC-7 (hysteresis with single-shot exemption).

**Dependencies:** S-002 — required for merge ordering (both steps modify the same probe-site code in `PcsProAutomationService.cs`; S-003 must not be squash-merged to `master` before S-002 is merged).

**Files affected:**
- `src/PcsRemote.Automation/UIAutomationHelpers.cs` (`IsKnownDialog`, `HasUnexpectedDialog`)
- `src/PcsRemote.Automation/FlaUi*Automation.cs` classes (hysteresis counter fields)
- Test files: new tests for whitelist, popup filter, hysteresis counter

**Acceptance criteria:**
- `IsKnownDialog` returns `true` for windows named "Video Consent — [match]", "Match Centre — [match]", "Add Live Stream to [match]".
- `HasUnexpectedDialog` returns `false` for child windows with `ClassName` matching WPF popup patterns.
- Hysteresis (looped probes): first non-known sighting returns `false`; second consecutive sighting returns `true`; clean tick resets counter; different dialog on second tick resets counter.
- Hysteresis (persistent dialog): after the two-tick confirmation fires (warning logged), the counter resets. A dialog persisting across subsequent ticks logs once per two-tick cycle, not every tick.
- Hysteresis (single-shot probes): non-known dialog is reported immediately (no two-tick wait).
- Existing `IsKnownDialog` tests pass (no regressions in current whitelist).

**Verification intent:** Unit tests. Manual verification deferred to garage PC (AC-6, AC-7 observed via absence of false positives in logs).

---

## S-004 — Scoreboard Capture Interval Default 2s → 10s

**SPEC required:** No — single-line default change + config file update. No design decisions.

**What changes:** The default scoreboard capture interval changes from 2 seconds to 10 seconds, including the fallback default in the invalid-value guard. The `appsettings.json` file is updated to include the new default for discoverability.

**Why:** HLPS-016 §2.3. Reduces machine impact from constant BringToForeground + Capture cycles. Addresses AC-8.

**Dependencies:** None — independent surgical fix.

**Files affected:**
- `src/PcsRemote.Web/ScoreboardPollingService.cs` (default value + fallback guard)
- `src/PcsRemote.Web/appsettings.json` (add/update config key)
- Test files: any tests that assert the default interval value

**Acceptance criteria:**
- Default interval is 10 seconds when no config override is present.
- Existing config overrides continue to work.
- Invalid-value fallback is 10 seconds.
- No test regressions.

**Verification intent:** Unit tests + config review.

---

## S-005 — `BringToForeground` Preserves Window Placement

**SPEC required:** No — small Win32 interop addition with clear specification in HLPS-016 §2.7. Logic table is fully defined.

**What changes:** `UIAutomationHelpers.BringToForeground` calls `GetWindowPlacement` before `ShowWindow` and selects the appropriate show command based on the window's current placement. Applied to both the main window and modal-popup branches.

**Why:** HLPS-016 §2.7. Prevents maximised PCS Pro from being un-maximised during BringToForeground. Addresses AC-9.

**Dependencies:** None — independent surgical fix.

**Files affected:**
- `src/PcsRemote.Automation/UIAutomationHelpers.cs` (BringToForeground logic + new `GetWindowPlacement` P/Invoke declaration)
- Test files: seam-based tests for placement logic (verify correct `ShowWindow` command for each `showCmd` value)

**Acceptance criteria:**
- Maximised window → `SW_SHOW` (stays maximised).
- Minimised window → `SW_RESTORE` (restored to previous size).
- Normal window → `SW_SHOW` (no change).
- Both main-window and popup branches use the placement-aware logic.
- No test regressions.

**Verification intent:** Unit tests (seam-based). Manual verification on garage PC.

---

## S-006 — Manual-Mode Wiring in Backend Services

**SPEC required:** Yes — `SPEC-S-006-ManualMode-Service-Wiring.md`

**What changes:** `IManualModeService` is wired into `ScoreboardPollingService` and `PcsProAutomationService`:

1. **`ScoreboardPollingService`:** On each timer tick, checks `IsManualModeActive` — if active, skips the capture. When manual mode transitions from active to inactive, fires one immediate capture so the stream overlay catches up.

2. **`PcsProAutomationService`:** At the entry of every automation-driving public method, checks `IsManualModeActive` — if active, logs and returns early. Lifecycle methods (`LaunchAndLoginAsync`, `StopAsync`, `RecoverFromErrorAsync`, `DismissErrorAsync`) bypass the gate. The health-check tick also checks manual-mode state and skips if active.

3. **In-flight operations:** If an automation operation is already executing when manual mode is toggled, that operation runs to completion. Manual mode only prevents new operations from starting.

**Why:** HLPS-016 §2.4. Without this, manual mode only disables Blazor buttons — backend polling and health-checks continue running. Addresses AC-1 (pause — new operations rejected, in-flight complete), AC-2 (immediate resume capture), AC-3 (lifecycle bypasses gate).

**Dependencies:** S-001 (for `Toggle()` on the interface and Core-located concrete, though the gate itself only uses `IsManualModeActive`).

**Files affected:**
- `src/PcsRemote.Web/ScoreboardPollingService.cs` (constructor injection of `IManualModeService`, tick skip, resume capture)
- `src/PcsRemote.Automation/PcsProAutomationService.cs` (constructor injection of `IManualModeService`, entry gate in ~12 public methods)
- Test files: `ScoreboardPollingServiceTests.cs` (skip + resume), `PcsProAutomationServiceTests.cs` (gate + bypass)

**Acceptance criteria:**
- Scoreboard timer tick is skipped when manual mode is active.
- Resuming from manual mode fires one immediate capture.
- Health-check tick is skipped when manual mode is active; resumes on next scheduled tick after toggle-off.
- Automation-driving methods return early when manual mode is active.
- `LaunchAndLoginAsync`, `StopAsync`, `RecoverFromErrorAsync`, `DismissErrorAsync` execute normally regardless of manual mode.
- No `Thread.Sleep` in tests.

**Verification intent:** Unit tests. Manual verification on garage PC.

---

## S-007 — Blazor UI Manual-Mode Enhancements

**SPEC required:** No — minor extensions to existing components. Pattern is well-established.

**What changes:**
1. **`StreamingControls.razor`:** Add `IManualModeService` injection, subscribe to `ManualModeChanged`, disable Start/Stop buttons when manual mode is active. Follow the established pattern from `ChangeMatchButton.razor`.
2. **`ManualModeBanner.razor`:** Update banner text to include hotkey hint: "Manual mode — automation paused (Ctrl+Alt+P to resume)".

**Why:** HLPS-016 §2.8. `StreamingControls.razor` is the only automation-driving component that does not currently check manual mode. The banner text update gives the operator a reminder of how to resume. Addresses AC-1 (banner appears).

**Dependencies:** S-001 (for the `ManualModeService` in Core — ensures DI registration is correct). S-008 (for the global hotkey — the banner must not advertise a hotkey that doesn't exist yet).

**Files affected:**
- `src/PcsRemote.Web/Shared/StreamingControls.razor` (add manual-mode subscription + button disable)
- `src/PcsRemote.Web/Shared/ManualModeBanner.razor` (update banner text)
- Test files: bUnit tests for `StreamingControls` (buttons disabled when manual mode active), bUnit test for updated banner text

**Acceptance criteria:**
- `StreamingControls` Start/Stop buttons disabled when manual mode active.
- Banner text includes "(Ctrl+Alt+P to resume)" when active.
- Existing bUnit tests pass.

**Verification intent:** bUnit tests.

---

## S-008 — Global Ctrl+Alt+P Hotkey via TrayHost

**SPEC required:** Yes — `SPEC-S-008-Global-Hotkey.md`

**What changes:** A hidden `NativeWindow` subclass is created in `PcsRemote.TrayHost` that registers `Ctrl+Alt+P` as a global hotkey via Win32 `RegisterHotKey`. On `WM_HOTKEY`, it calls `IManualModeService.Toggle()`. The `NativeWindow` is owned by `TrayApplicationContext` and created on the STA thread. `TrayApplicationContext` subscribes to `ManualModeChanged` and shows a tray balloon on every toggle — this ensures the balloon fires regardless of toggle origin (hotkey, tray menu, or future Blazor toggle).

**Why:** HLPS-016 §2.6. The operator needs a quick keyboard shortcut to pause/resume without reaching for the tray menu or opening the browser. Addresses AC-1, AC-2 (hotkey triggers mode change).

**Dependencies:** S-001 (for `Toggle()` method). Can be delivered in parallel with S-006/S-007.

**Files affected:**
- `src/PcsRemote.TrayHost/HotkeyWindow.cs` (new — `NativeWindow` subclass)
- `src/PcsRemote.TrayHost/TrayApplicationContext.cs` (create/dispose `HotkeyWindow`, balloon on toggle)
- Test files: unit tests for `HotkeyWindow` lifecycle (register/unregister on create/dispose)

**Acceptance criteria:**
- Ctrl+Alt+P toggles manual mode when PCS Remote is running.
- Tray balloon shows "Manual Mode ON" / "Manual Mode OFF".
- Tray balloon fires on toggle regardless of origin (hotkey, tray menu, future UI toggle).
- If `RegisterHotKey` fails (conflict), a warning is logged and a tray balloon notifies the operator ("Manual-mode hotkey unavailable — use tray menu"). The tray menu remains the only toggle method.
- `HotkeyWindow` is disposed cleanly on application exit (calls `UnregisterHotKey`).
- Integration test verifies the full toggle-to-pause/resume flow: hotkey → Toggle → ManualModeChanged → polling skip + automation gate + health-check skip + banner + balloon → resume capture.

**Verification intent:** Unit tests (lifecycle) + integration test (AC-1/AC-2 flow). Manual verification on garage PC (hotkey + balloon).

---

## S-009 — Documentation Updates and DEFERRED-ITEMS Reconciliation

**SPEC required:** No — documentation only.

**What changes:**
1. **Operational Guide** (`docs/guides/Operational-Guide.md`): Add section on Ctrl+Alt+P manual-mode hotkey, manual-mode behaviour (what pauses, what doesn't, how to resume).
2. **Configuration Guide** (`docs/guides/Configuration-Guide.md`): Update `Scoreboard:CaptureIntervalSeconds` documentation (new default 10s, rationale).
3. **DEFERRED-ITEMS.md**: Update status of any items resolved by HLPS-016. Add any new deferred items identified during delivery.

**Why:** HLPS-016 §2.9. Operational documentation must reflect the new behaviour for the match-day operator.

**Dependencies:** S-004 (config default is documented), S-006 (manual-mode wiring defines "what pauses"), S-007 (UI behaviour), S-008 (hotkey is documented). Should be the last step.

**Files affected:**
- `docs/guides/Operational-Guide.md`
- `docs/guides/Configuration-Guide.md`
- `docs/PCS-Remote/DEFERRED-ITEMS.md`

**Acceptance criteria:**
- Operational Guide describes Ctrl+Alt+P behaviour.
- Configuration Guide reflects 10s default.
- DEFERRED-ITEMS.md is current.

**Verification intent:** Document review.

---

## Success Criteria Coverage

| AC | Covered By |
|----|-----------|
| AC-1 (Ctrl+Alt+P pauses + banner + balloon) | S-001 + S-006 + S-007 + S-008 (integration test in S-008) |
| AC-2 (Resume + immediate capture + banner clear + balloon) | S-001 + S-006 + S-007 + S-008 (integration test in S-008) |
| AC-3 (Lifecycle controls functional in manual mode) | S-006 |
| AC-4 (Runtime unexpected dialog → warning + continue) | S-002 |
| AC-5 (Login retains fatal behaviour) | S-002 (regression) |
| AC-6 (WPF popup filter) | S-003 |
| AC-7 (Two-tick hysteresis) | S-003 |
| AC-8 (Default capture interval 10s) | S-004 |
| AC-9 (BringToForeground preserves maximised) | S-005 |
| AC-10 (All new code covered by tests) | Every step |

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-24 | Opus 4.7, GPT 5.4, Sonnet 4.6 | REVISE — 3 HIGH (post-fire reset AC, integration test gap, over-specification), 5 MEDIUM, 7 LOW. All HIGH/MEDIUM accepted. 2 LOW rejected (S-001 split, S-001 independent value). Resolved in v0.3. |
| R2 | 2026-04-24 | Opus 4.7, GPT 5.4, Sonnet 4.6 | Opus APPROVE, Sonnet APPROVE, GPT REVISE — 1 MEDIUM (health-check omitted from integration test flow). Resolved in v0.4. |
| R3 | 2026-04-24 | GPT 5.4 (targeted) | APPROVE — unanimous approval achieved. |
