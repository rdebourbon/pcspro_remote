# SPEC-S-001 — Header Alignment Fix

**Status:** APPROVED  
**Version:** 0.2
**Governing Documents:** HLPS-015 (APPROVED), IS-015 (APPROVED)  
**Branch:** `feature/S-001-header-fix`  
**Created:** 2026-04-23

---

## 1. Problem

The page header wraps its child elements instead of rendering them in a single horizontal row. Existing CSS rules (`flex-wrap: nowrap`, `width: 100%` on `.rz-header .rz-stack`) are present but insufficient.

### Root Cause Analysis

Investigation of the Radzen 10.2.2 CSS reveals three contributing factors:

1. **Radzen sets `width: 100vw` on `.rz-header`** — This is a well-known CSS issue: `100vw` includes the vertical scrollbar width (~17px on Windows), causing the header content to overflow the viewport. When flex children cannot shrink below their intrinsic width, this overflow manifests as wrapping or horizontal scroll.

2. **`RadzenStack` renders flex properties as inline styles** — The component outputs `display: flex`, `flex-direction`, and `flex-wrap` directly in the element's `style` attribute. Inline styles override stylesheet class rules regardless of specificity. Our `.rz-header .rz-stack { flex-wrap: nowrap }` is therefore redundant (the default is already NoWrap) — the wrapping is caused by overflow, not by `flex-wrap: wrap`.

3. **Missing `min-width: 0` on flex children** — In flexbox, children default to `min-width: auto`, which prevents them from shrinking below their content width. If any child (status pill, user count, title text) has intrinsic content wider than expected, it forces siblings to overflow.

---

## 2. Requirements

| ID | Requirement |
|----|------------|
| R-1 | Override Radzen's `width: 100vw` on `.rz-header` with `width: 100%` to prevent scrollbar-induced overflow |
| R-2 | Ensure flex children of the header stack can shrink by applying `min-width: 0`. Text-bearing children (page title) use `overflow: hidden; text-overflow: ellipsis; white-space: nowrap` for graceful truncation. Non-text children (status pill, user count) shrink but never truncate |
| R-3 | Retain the existing visual order: Logo → Title → User Count (pushed right) → Status Pill |
| R-4 | Header must not wrap at any viewport width ≥ 768px (per HLPS A-3). Below 768px, wrapping is acceptable as progressive degradation |
| R-5 | No changes to component logic — CSS only |
| R-6 | Overrides targeting Radzen-generated CSS rules must use `!important`, consistent with the existing override pattern in `app.css` and HLPS-015 §8 R-1 |
| R-7 | Remove the redundant `.rz-header .rz-stack { flex-wrap: nowrap }` rule as part of this step — the wrapping root cause is overflow, not `flex-wrap` |

---

## 3. Test Approach

This is a CSS-only change with no component logic modifications. Verification is visual:

- **Manual verification:** Inspect the header at desktop width (≥ 1024px), tablet width (~768px), and narrow width (~480px). Confirm single-row rendering at ≥ 768px.
- **Regression:** Run existing bUnit test suite to confirm no markup-dependent tests break.
- **Build:** Confirm zero errors, zero warnings.

No new automated tests are needed — CSS layout cannot be meaningfully unit-tested in bUnit.

---

## 4. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | Header renders as a single horizontal row at viewports ≥ 768px |
| AC-2 | The `.rz-header` width override does not introduce a horizontal scrollbar |
| AC-3 | All four header elements (Logo, Title, User Count, Status Pill) are visible and appear in the specified left-to-right order at ≥ 768px, with User Count pushed to the right |
| AC-4 | All existing tests pass (`dotnet test`) |
| AC-5 | Build succeeds with zero errors and zero warnings |

---

## 5. Documentation Updates

None required — this is a CSS bugfix with no API or architectural changes.

---

## 6. Review History

| Round | Reviewers | Status | Notes |
|-------|-----------|--------|-------|
| R1 | Opus 4.6, Sonnet 4.6 | Opus APPROVE, Sonnet CONDITIONAL PASS | Opus: 3 LOW advisories (AC-2 scope, overflow ambiguity, sub-768px). Sonnet: 2 required (add `!important` req, define overflow behaviour), 4 recommended (AC-3 expansion, sub-768px note, editorial "two"→"three", redundant rule disposition). All findings accepted and incorporated in v0.2. |
| R2 | — | APPROVED | No R2 needed — Opus approved outright, all Sonnet findings addressed |
