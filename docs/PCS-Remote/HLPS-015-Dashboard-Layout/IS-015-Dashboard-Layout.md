# IS-015 — Dashboard Layout Redesign

**Status:** APPROVED  
**Version:** 0.2  
**Governing Document:** HLPS-015-Dashboard-Layout.md (APPROVED)  
**Created:** 2026-04-23

---

## Overview

This Implementation Sequence decomposes HLPS-015 into two atomic steps. The scope is limited to CSS and Razor markup changes in `PcsRemote.Web` — no component logic, service, or state machine changes.

---

## Step Sequence

### S-001 — Header Alignment Fix

**What:** Diagnose and fix the persistent header wrapping issue. The header (`RadzenHeader` → `RadzenStack`) must render as a single non-wrapping row: Logo | Title | User Count | Status Pill.

**Why:** Addresses HLPS-015 §1 (header alignment problem) and SC-7. The existing CSS rules (`flex-wrap: nowrap`, `width: 100%`) are insufficient — the root cause must be identified (likely Radzen CSS specificity or missing flex-shrink/min-width constraints on child elements) before the fix can be applied.

**Dependencies:** None.

**Verification:** Header renders as a single horizontal row across the viewport width (per HLPS A-3, verified at ≥ 768px). No element wraps to a second line. Existing tests pass unmodified.

**Branch:** `feature/S-001-header-fix`

---

### S-002 — Two-Panel Dashboard Layout

**What:** Restructure the `MatchLoaded` section of the main page to use a two-panel grid layout. The left panel contains the match title card (with co-located Change Match button), scoreboard image, and Refresh Scoreboard button. The right panel contains the StreamingControls component. Pre-match and error states retain the existing single-column centred layout.

**Why:** Addresses HLPS-015 §1 (single-column waste), SC-1 through SC-6, SC-8, SC-9.

**Dependencies:** S-001 (header fix should land first so any CSS interactions between the header and body layout are visible).

**Scope of changes:**
- Add wrapper `<div>` elements to the main page's MatchLoaded branch to create a CSS grid container with left/right panel children
- Co-locate the Change Match button with the match title in the left panel header (deliberate design decision per HLPS-015 §2)
- Add CSS grid rules for the dashboard panels with a responsive breakpoint at 768px (collapse to single column, match/score on top)
- Scope the existing body content max-width constraint to apply only to pre-match states, not the dashboard
- Update the scoreboard image max-width so it fills its panel container
- Reset the streaming controls top margin when inside the right panel to maintain visual alignment

**Verification:** Side-by-side panels visible on desktop (≥ 768px). Panels stack on narrow screens. Pre-match states remain centred single-column. All existing tests pass. Published build includes all static assets.

**Branch:** `feature/S-002-dashboard-layout`

---

## SC Coverage Matrix

| Success Criterion | Covered By |
|-------------------|-----------|
| SC-1 Side-by-side panels ≥ 768px | S-002 |
| SC-2 Match title + Change Match at top of left panel | S-002 |
| SC-3 Scoreboard + Refresh in left panel | S-002 |
| SC-4 StreamingControls in right panel | S-002 |
| SC-5 Responsive collapse < 768px | S-002 |
| SC-6 Pre-match states single-column | S-002 |
| SC-7 Header non-wrapping row | S-001 |
| SC-8 All tests pass | S-001, S-002 |
| SC-9 Static assets in publish | S-002 |

---

## Review History

| Round | Reviewers | Status | Notes |
|-------|-----------|--------|-------|
| R1 | Opus 4.6, GPT 5.4 | **Unanimous Approval** | Opus: full pass, 1 advisory LOW. GPT: 1 MEDIUM downgraded to LOW (consistent with HLPS A-3), 1 LOW deferred to delivery. |
