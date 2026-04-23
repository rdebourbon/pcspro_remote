# SPEC-S-002 — Two-Panel Dashboard Layout

**Status:** APPROVED  
**Version:** 0.2
**Governing Documents:** HLPS-015 (APPROVED), IS-015 (APPROVED)  
**Branch:** `feature/S-002-dashboard-layout`  
**Created:** 2026-04-23

---

## 1. Problem

The MatchLoaded view currently stacks all elements in a single narrow column (constrained to 600px by `.rz-body > *`). Match information, scoreboard, and streaming controls run vertically down the page with no visual separation between scoring and streaming concerns.

HLPS-015 specifies a two-panel side-by-side layout for the MatchLoaded state, keeping pre-match states in the existing single-column centred layout.

---

## 2. Requirements

| ID | Requirement |
|----|------------|
| R-1 | Wrap the MatchLoaded section in a CSS Grid container with two equal columns at viewports ≥ 768px |
| R-2 | Left panel contains: match title card (with ChangeMatchButton relocated to its header), ScoreboardPreview, and RefreshScoreboardButton |
| R-3 | Right panel contains: StreamingControls (unchanged component) |
| R-4 | At viewports < 768px, the grid collapses to a single column with match panel on top and streaming panel below |
| R-5 | Scope the existing `.rz-body > *` max-width: 600px constraint to pre-match states only by narrowing its selector (e.g. `:not(.dashboard-grid)`), so the dashboard grid gets full available width up to 1200px without needing `!important` |
| R-6 | `.scoreboard-image` max-width changes from 600px to 100% to fill the left panel |
| R-7 | `.streaming-controls` margin-top resets to 0 inside the panel (panel gap provides separation) |
| R-8 | No component logic changes — CSS + Razor markup restructure only |
| R-9 | Pre-match states (Login, Launching, MatchSelection, MatchSelectionSearching) retain the existing single-column centred layout |

---

## 3. Markup Structure

The MatchLoaded block in `Index.razor` (currently lines 45–58) will be restructured:

```razor
@* MatchLoaded — two-panel dashboard *@
<div class="dashboard-grid">
    <div class="dashboard-panel dashboard-panel--match">
        <div class="match-loaded">
            <!-- match title (home vs away) -->
            <ChangeMatchButton />
        </div>
        <ScoreboardPreview />
        <RefreshScoreboardButton />
    </div>
    <div class="dashboard-panel dashboard-panel--streaming">
        <StreamingControls />
    </div>
</div>
```

The ChangeMatchButton moves from after ScoreboardPreview into the `.match-loaded` card, appearing as a header-inline action.

---

## 4. CSS Changes

| Selector | Change |
|----------|--------|
| `.dashboard-grid` | New: `display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem; max-width: 1200px; margin: 0 auto; padding: 1.25rem 1rem;` |
| `.dashboard-grid` (< 768px) | `@media (max-width: 767px) { grid-template-columns: 1fr; }` |
| `.rz-body > *` | Narrow selector to `.rz-body > *:not(.dashboard-grid)` so the 600px max-width applies only to pre-match states |
| `.scoreboard-image` | Change `max-width: 600px` → `max-width: 100%` |
| `.dashboard-panel .streaming-controls` | `margin-top: 0` (panel gap provides spacing) |
| `.match-loaded` | ChangeMatchButton sits inline at the end of the flex row; the existing `justify-content: space-between` pushes it right. No flex-wrap needed — at narrow widths the grid collapses to single column providing sufficient room |

---

## 5. Test Approach

CSS + markup restructure with no component logic changes. Verification:

- **Build:** Zero errors, zero warnings
- **Regression:** All existing tests pass (`dotnet test`)
- **Manual verification:** Two panels side-by-side at desktop width; stacked at mobile width; pre-match states unaffected
- **Publish verification:** Run `dotnet publish` on TrayHost and confirm `wwwroot/css/app.css` and `wwwroot/_content/` are present in the output

No new automated tests — layout cannot be meaningfully unit-tested in bUnit.

---

## 6. Acceptance Criteria

| ID | Criterion | Source |
|----|-----------|--------|
| AC-1 | MatchLoaded state renders as a two-column grid at ≥ 768px | SC-1, SC-2 |
| AC-2 | Left panel contains match title, scoreboard, and refresh button | SC-3 |
| AC-3 | Right panel contains streaming controls | SC-4 |
| AC-4 | Panels stack to single column at < 768px (match on top) | SC-5 |
| AC-5 | ChangeMatchButton appears in the match title card header | SC-2 |
| AC-6 | Pre-match states retain single-column centred layout | SC-6 |
| AC-7 | Scoreboard image fills its panel width (no 600px cap) | HLPS A-2 |
| AC-8 | All existing tests pass (`dotnet test`) | SC-8 |
| AC-9 | Build succeeds with zero errors and zero warnings | SC-8 |
| AC-10 | Published output includes `wwwroot/css/app.css` and `wwwroot/_content/` static assets | SC-9 |

---

## 7. Risks

| ID | Risk | Mitigation |
|----|------|-----------|
| DR-1 | Radzen CSS may override new grid/panel styles | Use `!important` only if specificity proves insufficient (per HLPS R-1). The `.dashboard-grid` class is ours, not Radzen's, so conflicts are unlikely |
| DR-2 | Scoping `.rz-body > *` selector may break unexpected content | Only the MatchLoaded branch uses `.dashboard-grid`; all other states render individual components directly |

---

## 8. Documentation Updates

None required — this is a layout change with no API or architectural changes.

---

## 9. Review History

| Round | Reviewers | Status | Notes |
|-------|-----------|--------|-------|
| R1 | Opus 4.6, Sonnet 4.6 | Opus REVISION REQUIRED (2M, 2L), Sonnet CONDITIONAL APPROVAL (2H, 1M, 1L) | Convergent findings: AC-5/AC-6 source citations transposed (both), SC-9 missing AC (both), `.rz-body > *` override vs scope wording (both), `.match-loaded` flex-wrap ambiguous (Sonnet), no risks section (Opus). All findings accepted and incorporated in v0.2. |
| R2 | — | APPROVED | No R2 needed — all findings addressed, no contested items |
