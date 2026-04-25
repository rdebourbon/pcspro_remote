# SPEC-S-003: Dialog Detection Hardening — Whitelist, Popup Filter, Hysteresis

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-Dialog-Detection-Hardening.md |
| **Status** | DRAFT |
| **Version** | 0.5 |
| **Date** | 2026-04-24 |
| **Step ID** | S-003 |
| **Governing HLPS** | HLPS-016-Operator-Mode-And-Resilience.md (APPROVED v0.4) |
| **Governing IS** | IS-016-Operator-Mode-And-Resilience.md (APPROVED v0.5) |
| **Branch** | `feature/016-S-003-dialog-detection-hardening` |

---

## 1. Purpose

This step hardens the unexpected-dialog detection pipeline in `UIAutomationHelpers` with three complementary improvements:

1. **Whitelist extension** — recognise three PCS Pro dialog patterns whose constants already exist in `KnownElements.cs` but are not referenced by `IsKnownDialog`.
2. **WPF popup ClassName filter** — exclude WPF framework elements that expose `ControlType.Window` but are not user-facing dialogs.
3. **Two-tick hysteresis** — require a non-known dialog to be observed on two consecutive probe ticks before being confirmed, preventing transient WPF popups from causing false positives.

After S-002 demotes unexpected-dialog detections from fatal to warning, S-003 prevents the warnings from firing in the first place for known-benign cases. The two steps are complementary: S-002 ensures resilience (continue even if detection is wrong); S-003 ensures accuracy (reduce false positives).

---

## 2. Scope

### 2.1 Whitelist Extension — `IsKnownDialog`

**Requirements:**

- R1: `IsKnownDialog` must return `true` for child windows whose `Name` starts with any of the following patterns (case-insensitive ordinal comparison):
  - `KnownElements.VideoConsentDialogNamePattern` (`"Video Consent"`)
  - `KnownElements.MatchCentreDialogNamePattern` (`"Match Centre"`)
  - `KnownElements.MatchCentreDialogFallbackPattern` (`"Add Live Stream"`)
- R2: The existing exact-match comparisons for `MatchSelectionDialogName` and `MatchDetailsDialogName` must remain unchanged (ordinal exact match).
- R3: The existing password-field detection for the login dialog must remain unchanged.
- R4: `IsKnownDialog` must continue to never throw (probe semantics — catch-all on any exception).

### 2.2 WPF Popup ClassName Filter — `HasUnexpectedDialog`

**Requirements:**

- R5: Before the `IsKnownDialog` check in the `HasUnexpectedDialog` loop, each child window's `ClassName` is inspected. If it matches a WPF popup-host pattern, the child window is skipped (treated as invisible infrastructure, not a dialog).
- R6: The popup-host patterns to exclude are WPF framework elements that expose `ControlType.Window` but are not user-facing dialogs — e.g., `"Popup"`, `"PopupHost"`, and `HwndWrapper` class names containing `"Popup"`. The matching strategy is a delivery-phase decision.
- R7: `ClassName` access must be wrapped in a try-catch (may throw on stale elements). On exception, do not exclude the element (fail open — let `IsKnownDialog` decide).
- R8: The existing `IsOffscreenOrInvisible` filter remains as the first check (before the `ClassName` filter). The pipeline order is: offscreen check → ClassName popup filter → `IsKnownDialog` check.

### 2.3 Two-Tick Hysteresis

**Requirements:**

- R9: **Looped probes** (polling loops in `GetTodaysMatchesAsync`, `LoadMatchAsync`): `HasUnexpectedDialog` only returns `true` when a non-known, non-popup, visible dialog has been observed on **two consecutive calls** from the same probe context. A single sighting returns `false` (suppressed).
- R9b: **Single-shot probes** (one check per operation invocation — `RefreshScoreboardAsync`, `CaptureScoreboardImageAsync`, `ChangeMatchAsync`, team-name entry checks): the detector returns `true` immediately on first qualifying detection (no two-tick wait). These sites only execute once per operation, so hysteresis is not applicable.
- R10: The hysteresis counter resets to zero when:
  - A probe tick yields **zero qualifying unexpected dialogs** after classification (known dialogs or popup-filtered windows do not count — only truly unexpected dialogs maintain the counter). A tick containing only known dialogs is a clean tick.
  - A different unexpected dialog is observed on the second tick (dialog identity changed — prevents two unrelated transient dialogs from triggering a false confirmation).
  - After a two-tick confirmation (detector returns `true`), the counter resets. If the same dialog persists, the detector returns `false` on tick 3 (new cycle), `true` on tick 4, and so on. This means the detector alternates between suppressed and confirmed states for persistent dialogs — callers see `true` once per two-tick cycle.
- R11: Hysteresis state must be per-probe-context, not global. A transient popup in one probe site must not prime the counter for a different site. The same automation instance may be used across multiple probe sites, so per-instance state is insufficient — state must be scoped to the specific probe context for that operation/probe path.
- R12: The implementation approach for per-probe-context state is a delivery-phase decision. Delivery may extract pure classification and state-transition logic into testable helpers.
- R13: The hysteresis threshold is `2` (hardcoded). Configurability is not needed.

### 2.4 Out of Scope

- Changes to probe-site code in `PcsProAutomationService` (handled in S-002).
- Adding new constants to `KnownElements.cs` (existing constants are sufficient).
- Changes to `GetUnexpectedDialogName()` in `FlaUiLoginAutomation` (login phase is out of scope for this step).

---

## 3. Test Strategy

### 3.1 Whitelist Tests

- **T1 — Video Consent match:** `IsKnownDialog` returns `true` for a child window named `"Video Consent — Some Match Name"`.
- **T2 — Match Centre match:** `IsKnownDialog` returns `true` for a child window named `"Match Centre — Some Match Name"`.
- **T3 — Add Live Stream match:** `IsKnownDialog` returns `true` for a child window named `"Add Live Stream to Some Match Name"`.
- **T4 — Existing matches preserved:** `IsKnownDialog` returns `true` for `"Open Match"` and `"Match Details/Teams"` (exact match, unchanged).
- **T5 — Unknown dialog:** `IsKnownDialog` returns `false` for `"Some Random Dialog"`.

### 3.2 Popup Filter Tests

- **T6 — ClassName "Popup" excluded:** `HasUnexpectedDialog` skips a child window with `ClassName = "Popup"`.
- **T7 — ClassName "PopupHost" excluded:** Skips child window with `ClassName = "PopupHost"`.
- **T8 — HwndWrapper containing "Popup" excluded:** Skips child window with `ClassName = "HwndWrapper[PcsProApp;;Popup]"`.
- **T9 — Non-popup ClassName not excluded:** Child window with `ClassName = "SomeDialog"` is not excluded by the popup filter (proceeds to `IsKnownDialog`).
- **T10 — Stale ClassName does not exclude:** If `ClassName` throws, the element proceeds to `IsKnownDialog` (fail-open).

### 3.3 Hysteresis Tests

- **T11 — First sighting suppressed (looped):** First call to a looped probe with a non-known dialog present returns `false`.
- **T12 — Second consecutive sighting fires (looped):** Second consecutive call (same probe context, same non-known dialog still present) returns `true`.
- **T13 — Clean tick resets counter:** Non-known dialog on tick 1 → clean tick 2 → non-known dialog on tick 3 → returns `false` (counter was reset).
- **T14 — Per-context isolation:** Two separate probe contexts. One sees a non-known dialog; the other does not. The first context's counter does not affect the second.
- **T15 — Single-shot immediate detection:** A single-shot probe returns `true` immediately on first qualifying detection (no two-tick wait).
- **T16 — Different dialog resets counter:** Non-known dialog A on tick 1 → different non-known dialog B on tick 2 → returns `false` (identity changed, counter reset).
- **T17 — Post-confirm reset:** After two-tick confirmation (detector returns `true` on tick 2), counter resets. Same persistent dialog on tick 3 → detector returns `false` (new cycle starts).
- **T18 — Persistent dialog cadence:** Dialog persists across ticks 1–4. Detector returns `false, true, false, true` (alternating per two-tick cycle).
- **T19 — Known-dialog-only tick is clean:** Unknown dialog A on tick 1 → known-dialog-only tick 2 → unknown dialog A on tick 3 → returns `false` (counter was reset by clean tick).

---

## 4. Acceptance Criteria

| ID | Criterion | Verification |
|----|-----------|-------------|
| AC-1 | `IsKnownDialog` recognises Video Consent, Match Centre, and Add Live Stream patterns via `StartsWith` | T1–T3 |
| AC-2 | Existing `IsKnownDialog` matches (exact) are unchanged | T4 |
| AC-3 | `HasUnexpectedDialog` excludes WPF popup ClassName patterns | T6–T8 |
| AC-4 | ClassName filter fails open on stale element | T10 |
| AC-5 | Looped probes: hysteresis requires two consecutive sightings of the same dialog | T11–T12 |
| AC-6 | Single-shot probes: detector returns `true` immediately on first qualifying detection (no hysteresis) | T15 |
| AC-7 | Clean tick resets hysteresis counter | T13 |
| AC-8 | Different dialog identity resets counter | T16 |
| AC-9 | Post-confirmation reset: detector returns `true` once per two-tick cycle for persistent dialog | T17–T18 |
| AC-10 | Hysteresis is per-probe-context, not global | T14 |
| AC-11 | No test regressions | Full test run |

---

## 5. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Substring `"Popup"` in `ClassName` may match a legitimate dialog class | Low | Medium | WPF dialog classes do not typically contain "Popup". If a false negative is discovered via the structured warning logging (S-002), the filter can be narrowed. |
| Hysteresis may delay detection of a real unexpected dialog by one poll cycle | Accepted | Low | Accepted risk per HLPS-016 §2.4. One tick delay (~200ms at typical probe frequency) is negligible. After S-002, unexpected dialogs are non-fatal anyway. |
| `StartsWith` matching for dialog names may match future PCS Pro dialogs that should not be whitelisted | Low | Low | New dialogs starting with "Video Consent", "Match Centre", or "Add Live Stream" are inherently PCS Pro dialogs and should be whitelisted. |

---

## 6. Documentation Updates

- No external documentation changes for this step.

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | DRAFT — not yet submitted for review |
