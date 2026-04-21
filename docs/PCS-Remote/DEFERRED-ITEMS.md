# PCS Remote — Deferred Items Register

This document tracks all deferred functionality, accepted risks, and future work items identified during development. Items are sourced from HLPS/IS/Spec reviews, adversarial code reviews, and feature gap discovery during diagnostic testing.

**Last updated:** 2026-04-21 (IS-010 COMPLETE)

---

## Feature Gaps (from Diagnostic Testing)

Discovered during hands-on testing against real PCS Pro on the garage PC. Full details in session artifact `feature-gaps.md`.

| ID | Title | Source | Assigned HLPS | Status |
|---|---|---|---|---|
| GAP-001 | Debug / Advanced Menu in Web UI | User observation | Deferred to HLPS-011 | ❌ Not started |
| GAP-002 | Switch User / Re-Login | User observation | Deferred to HLPS-011 | ❌ Not started |
| GAP-003 | Clear Authentication Errors on Web UI | User observation | Deferred to HLPS-011 | ❌ Not started |
| GAP-004 | Reusable Safe Interaction Helpers | Diagnostic testing | ✅ Delivered (IS-010 S-002) | ✅ Complete |
| GAP-005 | Date Filter Override for Testing | Diagnostic testing | Deferred to HLPS-011 | ❌ Not started |
| GAP-006 | Match Selection — Site/Competition Filter Awareness | Diagnostic testing | Deferred to HLPS-011 | ❌ Not started |
| GAP-007 | Match Selection Grid — Structured Data Extraction | Diagnostic testing | ✅ Delivered (IS-010 S-004) | ✅ Complete |
| GAP-008 | Title Change Detection — Same Match Type | Diagnostic testing | ✅ Resolved (commit `1129b1f`) | ✅ Complete |
| GAP-009 | Team Name Display & YouTube Title Formatting Rules | User requirement | Deferred to HLPS-011 | ❌ Not started |
| GAP-010 | "Use Current Match" — Skip Match Selection | User observation | IS-010 S-011 (Tier 3) | ✅ Delivered (`acc0897`) |
| GAP-011 | Periodic PCS Pro Health-Check Poll | User observation | IS-010 S-012 (Tier 3) | ✅ Delivered (`ee716e0`) |

---

## Deferred from Spec/Code Reviews

Items identified during adversarial reviews that were accepted but deferred to later steps.

| ID | Title | Source | Deferred From | Target | Notes |
|---|---|---|---|---|---|
| DEF-001 | BroadcastTitleRenderer club token integration | S-010 spec R1 (Opus + GPT) | SPEC-S-010 | Future step | `{HomeClub}` / `{AwayClub}` tokens require a data-flow bridge between `MatchInfo` (pre-load, from MatchRowParser) and `MatchTeams` (post-load, from GetTeamNamesAsync). The renderer currently takes only `MatchInfo`. Requires either: (a) change renderer to accept `MatchTeams` too, or (b) a composite type, or (c) `MatchInfo` enrichment after team name read. |
| DEF-002 | `MatchInfo` club name fields | S-010 spec R1 (Opus + GPT) | SPEC-S-010 | With DEF-001 | `MatchInfo` does not carry club names — they come from a different lifecycle stage. Adding club fields requires defining a population path. |
| DEF-003 | FlaUI stub implementations pending garage PC | IS-006 S-006 code review | S-006 delivery | Garage PC visit | `FlaUiScoreboardAutomation` and `FlaUiChangeMatchAutomation` have implementation stubs for methods that need garage PC testing (I-U-5, I-U-6). |
| DEF-004 | Web UI club name display | S-010 spec | SPEC-S-010 | Future HLPS | Club names will be available in `MatchTeams` after S-010 but the Web UI won't display them separately. Requires UI design. |
| DEF-005 | Live stream status health signal | S-012 spec R1 (Opus + GPT) | SPEC-S-012 | Future HLPS | HLPS-010 §2.9 lists live stream status (button child text in `LiveStreamingControls`) as a monitored signal. Requires heavier FlaUI reads in the poll. Deferred to keep S-012 poll lightweight. |

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

## HLPS-010 Out of Scope → HLPS-011 Candidates

These were explicitly scoped out of HLPS-010 and earmarked for the next HLPS:

- GAP-001: Debug/Advanced menu
- GAP-002: Switch User
- GAP-003: Clear Auth Errors
- GAP-005: Date Filter Override
- GAP-006: Filter Awareness
- GAP-009: Team Name Formatting rules (HHCC prefix stripping, home team identification)
- DEF-001: BroadcastTitleRenderer club token integration
- DEF-002: MatchInfo club name fields
- DEF-004: Web UI club name display

---

## How to Use This Document

- **When creating a new HLPS:** Review this register to identify items that should be included.
- **When deferring an item:** Add it here with source, rationale, and target.
- **When completing an item:** Update status to ✅ Complete with commit/HLPS reference.
- **During retrospectives:** Review for items that have been deferred too long.
