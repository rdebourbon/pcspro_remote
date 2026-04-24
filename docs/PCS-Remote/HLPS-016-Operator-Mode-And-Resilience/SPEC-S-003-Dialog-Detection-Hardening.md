# SPEC-S-003: Dialog Detection Hardening — Whitelist, Popup Filter, Hysteresis

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-Dialog-Detection-Hardening.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-24 |
| **Step ID** | S-003 |
| **Governing HLPS** | HLPS-016-Operator-Mode-And-Resilience.md (DRAFT v0.1) |
| **Governing IS** | IS-016-Operator-Mode-And-Resilience.md (DRAFT v0.1) |
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
- R6: The popup-host patterns to match are:
  - Exact match: `"Popup"`, `"PopupHost"`
  - Contains match: `"Popup"` within a `HwndWrapper` class name (e.g., `"HwndWrapper[PcsProApp;;Popup]"`)
  - The matching logic should be a single substring check for `"Popup"` in the `ClassName` — this covers all three patterns. If a child window's `ClassName` contains the substring `"Popup"` (ordinal, case-sensitive — WPF class names are PascalCase), it is excluded.
- R7: `ClassName` access must be wrapped in a try-catch (may throw on stale elements). On exception, do not exclude the element (fail open — let `IsKnownDialog` decide).
- R8: The existing `IsOffscreenOrInvisible` filter remains as the first check (before the `ClassName` filter). The pipeline order is: offscreen check → ClassName popup filter → `IsKnownDialog` check.

### 2.3 Two-Tick Hysteresis

**Requirements:**

- R9: Introduce a mechanism so that `HasUnexpectedDialog` only returns `true` when a non-known, non-popup, visible dialog has been observed on **two consecutive calls** from the same call site. A single sighting returns `false` (suppressed).
- R10: The hysteresis counter resets to zero when a probe finds no non-known dialog (clean tick). It does not reset on finding a known dialog — only a completely clean probe (no non-known dialogs at all) resets the counter.
- R11: The counter must be per-call-site, not global. Each `FlaUi*Automation` instance that calls `HasUnexpectedDialog` (or equivalent probing logic) maintains its own counter. This prevents a transient popup in one probe site from priming the counter for a different site.
- R12: The implementation approach for per-call-site state is a design decision resolved during delivery. Options include:
  - (a) Each `FlaUi*Automation` instance holds a `_dialogHysteresisCount` field and passes it by ref to the helper.
  - (b) `HasUnexpectedDialog` gains a `ref int consecutiveCount` parameter.
  - (c) A lightweight `DialogProbe` struct/class encapsulates the state and the probing logic.
  - The delivery phase selects the approach that best balances simplicity and testability.
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

- **T11 — First sighting suppressed:** First call to the hysteresis-aware probe with a non-known dialog present returns `false`.
- **T12 — Second consecutive sighting fires:** Second consecutive call (same call site, non-known dialog still present) returns `true`.
- **T13 — Clean tick resets counter:** Non-known dialog on tick 1 → clean tick 2 → non-known dialog on tick 3 → returns `false` (counter was reset).
- **T14 — Per-instance isolation:** Two separate probe instances. One sees a non-known dialog; the other does not. The first instance's counter does not affect the second.

---

## 4. Acceptance Criteria

| ID | Criterion | Verification |
|----|-----------|-------------|
| AC-1 | `IsKnownDialog` recognises Video Consent, Match Centre, and Add Live Stream patterns via `StartsWith` | T1–T3 |
| AC-2 | Existing `IsKnownDialog` matches (exact) are unchanged | T4 |
| AC-3 | `HasUnexpectedDialog` excludes WPF popup ClassName patterns | T6–T8 |
| AC-4 | ClassName filter fails open on stale element | T10 |
| AC-5 | Hysteresis requires two consecutive sightings | T11–T12 |
| AC-6 | Clean tick resets hysteresis counter | T13 |
| AC-7 | Hysteresis is per-call-site, not global | T14 |
| AC-8 | No test regressions | Full test run |

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
