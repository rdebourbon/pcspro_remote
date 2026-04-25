# PCS Remote — Deferred Items Register

This document tracks all deferred functionality, accepted risks, and future work items identified during development. Items are sourced from HLPS/IS/Spec reviews, adversarial code reviews, and feature gap discovery during diagnostic testing.

**Last updated:** 2026-04-25 (HLPS-016 COMPLETE)

---

## Feature Gaps (from Diagnostic Testing)

Discovered during hands-on testing against real PCS Pro on the garage PC. Full details in session artifact `feature-gaps.md`.

| ID | Title | Source | Assigned HLPS | Status |
|---|---|---|---|---|
| GAP-001 | Debug / Advanced Menu in Web UI | User observation | ✅ Delivered (HLPS-011 S-007/S-008) | ✅ Complete |
| GAP-002 | Auto Switch User on Wrong Login | User observation | ✅ Delivered (HLPS-013 S-001/S-002) | ✅ Complete |
| GAP-003 | Clear Authentication Errors on Web UI | User observation | ✅ Delivered (HLPS-011 S-001) | ✅ Complete |
| GAP-004 | Reusable Safe Interaction Helpers | Diagnostic testing | ✅ Delivered (IS-010 S-002) | ✅ Complete |
| GAP-005 | Date Filter Override for Testing | Diagnostic testing | Unassigned | ❌ Not started |
| GAP-006 | Match Selection — Site/Competition Filter Awareness | Diagnostic testing | Unassigned | ❌ Not started |
| GAP-007 | Match Selection Grid — Structured Data Extraction | Diagnostic testing | ✅ Delivered (IS-010 S-004) | ✅ Complete |
| GAP-008 | Title Change Detection — Same Match Type | Diagnostic testing | ✅ Resolved (commit `1129b1f`) | ✅ Complete |
| GAP-009 | Team Name Display & YouTube Title Formatting Rules | User requirement | ✅ Delivered (HLPS-013 S-003/S-004) | ✅ Complete |
| GAP-010 | "Use Current Match" — Skip Match Selection | User observation | IS-010 S-011 (Tier 3) | ✅ Delivered (`acc0897`) |
| GAP-011 | Periodic PCS Pro Health-Check Poll | User observation | IS-010 S-012 (Tier 3) | ✅ Delivered (`ee716e0`) |

---

## Deferred from Spec/Code Reviews

Items identified during adversarial reviews that were accepted but deferred to later steps.

| ID | Title | Source | Deferred From | Target | Notes |
|---|---|---|---|---|---|
| DEF-001 | ~~BroadcastTitleRenderer club token integration~~ | S-010 spec R1 (Opus + GPT) | SPEC-S-010 | **Resolved (S-004)** | Resolved in HLPS-013 S-004: added `HomeClub`/`AwayClub` to `MatchInfo`, enriched after `GetTeamNamesAsync`, `OrderClubNamesForTitle` aligns with S-003 reorder. |
| DEF-002 | ~~`MatchInfo` club name fields~~ | S-010 spec R1 (Opus + GPT) | SPEC-S-010 | **Resolved (S-004)** | Resolved in HLPS-013 S-004: `MatchInfo` now carries `HomeClub = ""` and `AwayClub = ""` with default empty strings; populated after team names read. |
| DEF-003 | FlaUI stub implementations pending garage PC | IS-006 S-006 code review | S-006 delivery | Garage PC visit | `FlaUiScoreboardAutomation` and `FlaUiChangeMatchAutomation` have implementation stubs for methods that need garage PC testing (I-U-5, I-U-6). |
| DEF-004 | Web UI club name display | S-010 spec | SPEC-S-010 | Future HLPS | Club names will be available in `MatchTeams` after S-010 but the Web UI won't display them separately. Requires UI design. |
| DEF-005 | Live stream status health signal | S-012 spec R1 (Opus + GPT) | SPEC-S-012 | Future HLPS | HLPS-010 §2.9 lists live stream status (button child text in `LiveStreamingControls`) as a monitored signal. Requires heavier FlaUI reads in the poll. Deferred to keep S-012 poll lightweight. |
| DEF-006 | Per-operation hysteresis wiring for login-phase dialogs | S-003 code review (GPT 5.4) | SPEC-S-003 §2.4 | Future HLPS | `FlaUiLoginAutomation.GetUnexpectedDialogName()` and `TryCloseFirstUnexpectedDialog()` do not apply the popup ClassName filter. Login phase is out of scope for S-003 but could benefit from the same classification pipeline. |

---

## Operational Unknowns (from PROJECT-CONTEXT.md)

| ID | Description | Status |
|---|---|---|
| U-6 | CI/CD pipeline | Deferred |
| U-7 | PCS Pro password ownership | Deferred (operational) |
| U-8 | Remote scorer connection | Deferred (operational) |

---

## IS-010 Tier 3 Remaining Steps

These are in-scope for IS-010 but marked "Can-Defer" — they can be deferred to HLPS-011 without invalidating IS-010.

| Step | Title | Status | Notes |
|---|---|---|---|
| S-010 | Team Names Structured Return Type | ✅ Delivered (`40412e0`) | Core type change, FlaUI club read |
| S-011 | Use Current Match (GAP-010) | ✅ Delivered (`acc0897`) | State machine extension + attach flow |
| S-012 | Health-Check Poll (GAP-011) | ✅ Delivered (`ee716e0`) | Periodic state scan, configurable interval |

---

## HLPS-010 Out of Scope → Remaining Candidates

These were explicitly scoped out of earlier HLPS and remain unassigned:

- ~~GAP-002~~: Resolved in S-001/S-002
- GAP-005: Date filter override (testing convenience)
- GAP-006: Filter awareness (site/competition filter UI)
- ~~GAP-009~~: Resolved in S-003/S-004
- ~~GAP-013~~: Resolved in S-005
- ~~GAP-015~~: Resolved in S-006
- ~~GAP-016~~: Resolved in S-007
- ~~DEF-001/DEF-002~~: Resolved in S-004
- DEF-004: Web UI club name display (future HLPS)

---

## New Feature Gaps (User-Requested 2026-04-21)

| ID | Title | Description | Target HLPS | Status |
|---|---|---|---|---|
| GAP-012 | WiX MSI Installer | Full MSI installer replacing manual setup. Writes YouTube API credentials and PCS Pro path to `appsettings.json`, stores PCS Pro password as a System environment variable, registers Task Scheduler entry, opens firewall port, and handles upgrades. Replaces HLPS-007 manual deployment model entirely. | ✅ Delivered (HLPS-014) | ✅ Complete |
| GAP-013 | YouTube Setup Tray Shortcut | Add a right-click context menu item on the tray icon ("YouTube Setup...") that launches the OAuth flow in the default browser. Replaces the current `--youtube-setup` CLI argument approach. | ✅ Delivered (HLPS-013 S-005) | ✅ Complete |
| GAP-014 | Disable UI During Automation | User observation | ✅ Delivered (HLPS-011 S-005) | ✅ Complete |
| GAP-015 | Club Logo PNG | Use the proper club logo PNG everywhere: Web UI header/navbar, favicon, tray icon, installer splash. User will provide the file. | ✅ Delivered (HLPS-013 S-006) | ✅ Complete |
| GAP-016 | Tray Host Icon | Custom .ico for the system tray — simple text-based "PCS Remote" design mimicking the existing PCS Pro icon style. Needs to work at 16x16/24x24/32x32. | ✅ Delivered (HLPS-013 S-007) | ✅ Complete |
| GAP-017 | Real-Time Automation Log | User observation | ✅ Delivered (HLPS-011 S-003/S-004/S-006) | ✅ Complete |

---

## How to Use This Document

- **When creating a new HLPS:** Review this register to identify items that should be included.
- **When deferring an item:** Add it here with source, rationale, and target.
- **When completing an item:** Update status to ✅ Complete with commit/HLPS reference.
- **During retrospectives:** Review for items that have been deferred too long.
