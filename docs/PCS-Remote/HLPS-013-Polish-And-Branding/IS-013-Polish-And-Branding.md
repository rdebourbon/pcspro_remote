# IS-013 — Polish & Branding

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **HLPS**    | HLPS-013 (APPROVED) |
| **Author**  | Copilot |
| **Created** | 2026-04-21 |

---

## Overview

This Implementation Sequence breaks HLPS-013 into seven ordered steps across three workstreams:

1. **Login automation (S-001 → S-002):** Extend login contracts then orchestrate switch-user flow. Highest implementation risk, tackled first.
2. **Team name formatting (S-003, S-004):** Prefix stripping and broadcast title token expansion. Independent of each other — stripping applies to team display names, while club tokens are a separate rendering feature.
3. **Tray & branding (S-005, S-006, S-007):** YouTube tray shortcut, club logo/favicon, and custom tray icon. Low complexity, asset-heavy, mutually independent.

**Dependency graph:**

```
S-001 → S-002
S-003 (independent)
S-004 (independent)
S-005 (independent)
S-006 (independent)
S-007 (independent)
```

All steps are independently buildable and testable. Each step results in a squash-merge to master with 0 errors and 0 warnings.

---

## S-001 — Switch-User Login: Contracts & Configuration

**What:** Add expected-username configuration. Extend the login automation contract with capabilities to read the pre-populated username and invoke the switch-user action. Add the corresponding element identifiers. Provide a mock implementation that satisfies the same contract.

**Why:** Foundation for GAP-002 (HLPS-013 scope items 1–4). These contracts must exist before orchestration logic can be built in S-002.

**Dependencies:** None.

**Verification intent:**
- Configuration key binds correctly from appsettings.
- Mock implementation returns predictable values for all new contract members.
- Existing login tests continue to pass (C-1).

**HLPS traceability:** SC-1, SC-2, SC-3 (contracts only — orchestration in S-002).

---

## S-002 — Switch-User Login: Orchestration

**What:** Integrate username detection into the login flow. Before entering credentials, read the pre-populated username and compare against the configured expected username. On mismatch, invoke switch-user, wait for the login dialog to reappear, and re-enter credentials. Bounded to a maximum of 1 retry (C-8). On retry exhaustion, log an error and surface the failure to the UI. Log comparison results (match/mismatch) without logging credential values (C-6).

**Why:** Delivers GAP-002 end-to-end. The orchestration layer uses the contracts from S-001 to handle all username scenarios.

**Dependencies:** S-001.

**Verification intent:**
- Username matches → existing login flow unchanged (SC-2, C-1).
- Username blank → treated as no-op, existing flow proceeds (SC-2).
- Expected username not configured → existing flow unchanged (SC-2).
- Username mismatches → switch-user invoked, credentials re-entered (SC-1).
- Mismatch persists after retry → error surfaced, no infinite loop (SC-3, C-8).
- No credential values appear in log output (C-6).
- New switch-user logic ships with unit tests covering all branches above (SC-10).

**HLPS traceability:** SC-1, SC-2, SC-3, C-1, C-6, C-8.

---

## S-003 — Home-Club Prefix Stripping

**What:** Add a configurable home-club name setting. Implement anchored prefix stripping that removes the home-club name from the home team's display name when it appears as a leading prefix. Stripping applies to the home team only (asymmetric — A-6). Away-team names pass through unmodified. If neither team's club matches the configured home club, all names pass through unchanged. Stripping is idempotent.

**Why:** GAP-009 scope items 5–6. The club wants broadcast titles to read "1st XI vs Otford CC - 1st XI" rather than "High Halstow CC - 1st XI vs Otford CC - 1st XI".

**Dependencies:** None.

**Verification intent:**
- Home team with matching prefix → prefix stripped (SC-4).
- Home team without prefix → name unchanged (SC-5, idempotency).
- Away team with matching prefix → name unchanged (asymmetric).
- Neither team matches configured home club → all names unchanged (SC-4 fallback).
- Home club not configured → no stripping applied.
- Prefix stripping logic ships with unit tests covering all cases above (SC-10).

**HLPS traceability:** SC-4, SC-5, A-6, C-2.

---

## S-004 — Club Name Tokens in BroadcastTitleRenderer

**What:** Add `{HomeClub}` and `{AwayClub}` tokens to the broadcast title renderer. Bridge club name data from the match teams lifecycle stage into the renderer's data model, resolving the DEF-001/DEF-002 data flow gap. Existing templates that do not use the new tokens must render identically to today (C-7). Null or empty club names must not throw.

**Why:** GAP-009 scope items 7–8. Enables operators to include club names in YouTube broadcast title templates.

**Dependencies:** None.

**Verification intent:**
- `{HomeClub}` and `{AwayClub}` resolve to correct club names (SC-6).
- Existing templates without club tokens produce identical output (C-7, backward compat).
- Null or empty club names render as empty string without throwing (C-7).
- Templates mixing old and new tokens work correctly.
- Club token rendering ships with unit tests covering all cases above (SC-10).

**HLPS traceability:** SC-6, C-7, DEF-001, DEF-002.

---

## S-005 — YouTube Setup Tray Menu Item

**What:** Extract the OAuth setup logic from the TrayHost entry point into a reusable component. Add a "YouTube Setup..." item to the tray context menu. The menu item launches the OAuth consent flow in the default browser and stores the token via the existing data store. The item must be disabled while a setup is already in progress or while a broadcast is live (C-4). Handle cancellation and failure gracefully — re-enable the menu item so the operator can retry (R-5).

**Why:** GAP-013 scope item 9. Operators should not need to use command-line flags for routine OAuth consent.

**Dependencies:** None.

**Verification intent:**
- Menu item appears in tray context menu (SC-7).
- OAuth flow uses the same token storage path as `--setup-youtube` (SC-7, C-4).
- Menu item disables during setup and re-enables on completion or failure.
- No duplication of token acquisition logic (C-4).

**HLPS traceability:** SC-7, C-4, R-5.

---

## S-006 — Club Logo & Favicon

**What:** Copy the HHCC logo SVG into the Web UI static assets. Render it in the header alongside the application title using pure Blazor markup (C-5). Generate rasterised favicon variants (16x16, 32x32 for standard favicon; 180x180 for Apple touch icon) from the SVG. Add the appropriate link tags so the favicon appears in browser tabs and bookmarks.

**Why:** GAP-015 scope item 10. No custom branding currently exists — the Web UI has no logo and uses the default browser favicon.

**Dependencies:** None.

**Verification intent:**
- Club logo SVG renders in the Web UI header (SC-8).
- Rasterised favicon displays in the browser tab (SC-8).
- Favicon legibility verified at each target size (16, 32, 180px) (R-3).
- Apple touch icon at 180x180 included (SC-8).
- No JavaScript added (C-5).

**HLPS traceability:** SC-8, C-5, A-4, A-8.

---

## S-007 — Custom Tray Icon

**What:** Create a "PCS Remote" `.ico` file in the style of the provided PCS Pro icon reference, with two visual variants: normal operation and manual mode. Each variant must be legible at 16x16, 24x24, and 32x32 sizes (C-3). Embed the icons as resources in the TrayHost project and replace the current `SystemIcons.Application` / `SystemIcons.Warning` usage with the custom icons.

**Why:** GAP-016 scope item 11. Generic system icons give no visual identity to the application in the system tray.

**Dependencies:** None.

**Verification intent:**
- Custom icon displays in system tray at all required sizes (SC-9).
- Normal and manual-mode variants are visually distinct (SC-9).
- Icon updates correctly when toggling manual mode.
- First draft provided for user review — iterate if needed (R-4).
- Build produces 0 errors, 0 warnings (SC-10).

**HLPS traceability:** SC-9, C-3, A-5, R-4.

---

## SC Coverage Matrix

| SC | Covered By |
|----|------------|
| SC-1 | S-001, S-002 |
| SC-2 | S-001, S-002 |
| SC-3 | S-002 |
| SC-4 | S-003 |
| SC-5 | S-003 |
| SC-6 | S-004 |
| SC-7 | S-005 |
| SC-8 | S-006 |
| SC-9 | S-007 |
| SC-10 | All steps (build + test gate per step) |

---

## Review History

### R1 — 2026-04-21

**Panel:** Opus 4.7, GPT 5.4
**Verdict:** Split — Opus APPROVED, GPT REQUEST CHANGES (3 findings)

| Finding | Source | Severity | Disposition | Action |
|---------|--------|----------|-------------|--------|
| S-004 depends on S-003 unjustifiably — speculative HOW coupling | GPT F-1 | Major | Accept | Fixed: S-004 dependency removed. S-003 and S-004 are independent. Dependency graph and overview updated. |
| SC-10 test obligations not assigned per step | GPT F-2 | Major | Accept | Fixed: S-002, S-003, S-004 verification intents now explicitly require unit tests for their new logic. |
| Risk mitigations not carried into verification (R-3/R-4) | GPT F-3 | Medium | Accept | Fixed: S-006 adds favicon legibility verification at target sizes (R-3). S-007 adds user review checkpoint (R-4). |

### R2 — 2026-04-21

**Panel:** GPT 5.4 (only reviewer with R1 findings)
**Verdict:** APPROVED

All R1 findings verified as adequately addressed. No regressions found.

---

*End of IS-013.*
