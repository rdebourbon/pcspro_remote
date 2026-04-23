# HLPS-015 — Dashboard Layout Redesign

**Status:** APPROVED  
**Version:** 0.2  
**Author:** Copilot  
**Created:** 2026-04-23

---

## 1. Problem Statement

The current PCS Remote web UI renders all content in a single narrow column (max-width 600px, centred). When a match is loaded, the match header, scoreboard image, refresh button, change-match button, and streaming controls all stack vertically, producing a long scrollable page that wastes horizontal screen space and forces the operator to scroll between the two primary functional areas: **match/scoring** and **live streaming**.

Additionally, the page header (logo, title, connected-user count, status pill) has a persistent horizontal alignment issue where elements wrap or collapse rather than staying in a single row across the full header width. The stylesheet already contains rules targeting this (`flex-wrap: nowrap`, `width: 100%` on `.rz-header .rz-stack`), yet the wrapping persists — the probable root cause is Radzen's own CSS injecting conflicting styles or specificity overrides. The implementing step must diagnose the actual cause before applying a fix.

---

## 2. Proposed Solution

Introduce a **two-panel dashboard layout** for the `MatchLoaded` state:

| Panel | Content | Purpose |
|-------|---------|---------|
| **Left (primary)** | Match title card (home vs away + Change Match button), scoreboard image, Refresh Scoreboard button | Core scoring workflow |
| **Right (secondary)** | StreamingControls component | Live stream management |

**Design note — ChangeMatchButton relocation:** In the current markup, `ChangeMatchButton` renders as a standalone sibling *after* the scoreboard. This redesign deliberately co-locates it with the match title in a wrapper element at the top of the left panel (match title row with Change Match alongside). This is a conscious layout decision to group match identity and match-switching together.

### Layout Rules

1. **Two-panel layout activates only when `PcsProState == MatchLoaded`.**  
   All pre-match states (`NotRunning`, `Launching`, `LoginScreen`, `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`) and the `Error` state continue to use a single-column centred layout.

2. **Responsive breakpoint:** On narrow screens (< 768px), the two panels collapse to a single column with the match/score panel stacked on top and streaming below.

3. **Header fix:** The header must render as a single non-wrapping horizontal row: `Logo | Title | User Count | Status Pill`. The existing CSS rules are insufficient — the implementing step must diagnose whether the issue is Radzen specificity, missing `min-width: 0` on flex children, or another cause, and apply the appropriate fix.

### What This Is NOT

- This is **not** a component refactor — the existing Razor components (`ScoreboardPreview`, `RefreshScoreboardButton`, `ChangeMatchButton`, `StreamingControls`) remain unchanged in functionality.
- This is a **CSS and Razor markup restructuring** of `Index.razor` and `app.css` only.
- No new Blazor components are introduced (though wrapper `<div>` elements will be added to `Index.razor` for the two-panel grid and match title card).

---

## 3. Assumptions

| ID | Assumption | Rationale |
|----|-----------|-----------|
| A-1 | Radzen layout components (`RadzenBody`, `RadzenHeader`) do not inject inline styles that would override CSS grid on their children | Observed behaviour in development — Radzen uses class-based styling |
| A-2 | The scoreboard image renders acceptably at widths both narrower and wider than the current 600px cap | The image is a screenshot capture; its aspect ratio is fixed but it scales via CSS `width: 100%` |
| A-3 | Header content (logo + title + user count + status pill) fits in a single row at viewport widths ≥ 768px without truncation | Combined content width is approximately 400px; only extremely long content would overflow |
| A-4 | Existing `flex-wrap: nowrap` on `.rz-header .rz-stack` is being overridden by Radzen's CSS, not a fundamental layout incompatibility | The rule exists but wrapping persists — suggests specificity conflict |

---

## 4. Constraints

| ID | Constraint | Rationale |
|----|-----------|-----------|
| C-1 | CSS and markup changes only — no new JavaScript | Keep the solution minimal and within the existing tech stack |
| C-2 | Existing component interfaces unchanged | No changes to component parameters, events, or service contracts |
| C-3 | Must work in Radzen `RadzenLayout` / `RadzenBody` context | The app uses Radzen's layout scaffolding; CSS must cooperate with it |
| C-4 | Dark cricket-green theme preserved | All new CSS must use existing design tokens from `:root` |
| C-5 | No bUnit test changes expected | Layout is visual/structural — existing bUnit tests should pass unmodified |

---

## 5. Success Criteria

| ID | Criterion | Verification |
|----|-----------|-------------|
| SC-1 | When a match is loaded, scoreboard and streaming controls render side by side on screens ≥ 768px wide | Visual inspection on desktop browser |
| SC-2 | Match title card with home/away and Change Match button appears at the top of the left panel | Visual inspection |
| SC-3 | Scoreboard image and Refresh Scoreboard button are inside the left panel below the match title | Visual inspection |
| SC-4 | StreamingControls component occupies the right panel | Visual inspection |
| SC-5 | On screens < 768px, panels stack vertically (match/score on top, streaming below) | Browser responsive mode test |
| SC-6 | Pre-match states (login, match selection, load-matches prompt) remain single-column centred | Visual inspection at each state |
| SC-7 | Header renders as a single non-wrapping row across the full viewport width | Visual inspection at various widths |
| SC-8 | All existing bUnit and automation tests pass without modification | `dotnet test` — all green |
| SC-9 | Published build includes all static assets (app.css, Radzen CSS/JS) | Verify `wwwroot/css/app.css` and `wwwroot/_content/` present in publish output |

---

## 6. Scope

### In Scope

- Restructure `Index.razor` MatchLoaded markup to use a two-panel grid layout with wrapper `<div>` elements
- Co-locate `ChangeMatchButton` with the match title in a wrapper element at the top of the left panel
- Add CSS grid/flexbox rules to `app.css` for the dashboard panels
- Update `.rz-body > *` CSS rule that currently constrains all body content to 600px — scope it to pre-match states only
- Update `.scoreboard-image` max-width so the image fills its panel container rather than being capped at 600px
- Reset `.streaming-controls` top margin when rendered inside the right panel to maintain visual alignment with the left panel
- Diagnose and fix header alignment (`.rz-header .rz-stack` CSS) — investigate Radzen specificity conflicts
- Ensure responsive collapse at 768px breakpoint

### Out of Scope

- Component logic changes (no C# code changes to existing components)
- New Blazor components
- Changes to the state machine, automation service, or streaming service
- bUnit test changes (layout changes should be transparent to component tests)
- Changes to any project other than `PcsRemote.Web`
- Accessibility enhancements (WCAG compliance, ARIA attributes) — this is a visual layout change only

---

## 7. Unknowns Register

| ID | Description | Owner | Blocking? | Resolution |
|----|-------------|-------|-----------|------------|
| U-1 | Exact split ratio for the two panels (50/50 vs 60/40 vs auto) | User | No | Will default to equal split; user can adjust after visual inspection. Scoreboard image may benefit from a wider left panel — can be tuned in CSS without code changes. |
| U-2 | Whether the match title card should be visually distinct from the scoreboard area or merged as one card | User | No | Default: single card with match title as header and scoreboard as content. Change Match is inside the card header. |

---

## 8. Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|----|------|-----------|--------|------------|
| R-1 | Radzen layout components inject their own CSS that fights the dashboard grid | Low | Medium | Follow the existing `!important` pattern for Radzen overrides and test against Radzen's `default.css` |
| R-2 | Existing `.rz-body > *` 600px constraint breaks the dashboard | High | High | Scope that rule to only apply to pre-match states (add a wrapper class) |
| R-3 | Responsive breakpoint choice (768px) doesn't suit actual usage devices | Low | Low | Easily adjustable CSS value; no code change needed |

---

## 9. Review History

| Round | Reviewers | Status | Notes |
|-------|-----------|--------|-------|
| R1 | Opus 4.6, GPT 5.4, Sonnet 4.6 | Revision | 10 accepted, 1 downgraded, 1 rejected (see triage below) |
| R2 | Opus 4.6, GPT 5.4, Sonnet 4.6 | **Unanimous Approval** | Opus + Sonnet: full pass. GPT: 2 LOW findings — 1 rejected (A-4/§2 coherence confirmed by other reviewers), 1 accepted (accessibility out-of-scope made explicit). |

### R1 Triage Summary

| Finding | Reviewer | Severity | Disposition | Resolution |
|---------|----------|----------|-------------|------------|
| F1: Wrong state names (Login → LoginScreen, Launching omitted) | Opus | HIGH | Accept | Fixed in §2 Layout Rule 1 |
| F2: C-1 says CSS-only, but markup changes in scope | Opus | HIGH | Accept | Reworded C-1 |
| F3: Missing Assumptions section | Opus | HIGH | Accept | Added §3 Assumptions |
| F4: Header root cause undiagnosed | Opus | MEDIUM | Accept | Acknowledged existing CSS in §1, §2, A-4 |
| F5: ChangeMatchButton reordering is design decision | Opus | MEDIUM | Accept | Added design note in §2 |
| F6: SC-9 should verify app.css too | Opus | LOW | Accept | Amended SC-9 |
| F7: Missing Version field | Opus | LOW | Accept | Added to header |
| F8: Abstraction too low for HLPS | GPT | MEDIUM | Downgrade→LOW | Project convention references specific files in HLPS |
| F9: Missing accessibility/viewport constraints | GPT | MEDIUM | Downgrade→LOW | Added header overflow assumption (A-3); accessibility out of scope for layout-only change |
| F10: Success criteria too subjective | GPT | MEDIUM | Reject | Visual inspection is the standard verification for CSS layout in this project |
| F11: Assumptions embedded in prose | GPT | MEDIUM | Accept | Aligns with F3 — extracted into §3 |
| F12: .scoreboard-image max-width caps image in panel | Sonnet | HIGH | Accept | Added to §6 In Scope |
| F13: ChangeMatchButton co-location ambiguous | Sonnet | MEDIUM | Accept | Aligns with F5 — made explicit in §2 and §6 |
| F14: .streaming-controls margin-top misaligns panels | Sonnet | LOW-MED | Accept | Added to §6 In Scope |
