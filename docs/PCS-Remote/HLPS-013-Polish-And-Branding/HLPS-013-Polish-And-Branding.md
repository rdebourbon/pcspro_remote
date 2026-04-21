# HLPS-013 — Polish & Branding

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **Version** | 0.3 |
| **Author**  | Copilot |
| **Created** | 2026-04-21 |

---

## 1. Problem Statement

PCS Remote is functionally complete (HLPS-001 through HLPS-012 delivered), but five operational gaps remain that affect production-readiness, operator experience, and club identity. These are bundled into a single HLPS because each is individually low-complexity and all relate to production polish — each scope item is independently revertible.

1. **Wrong-user login (GAP-002):** If PCS Pro's login dialog shows a pre-populated username that does not match the configured expected username, the automation blindly enters the password and submits. This fails silently or logs in as the wrong account. The automation should detect the mismatch, click "Switch User", and re-enter the correct credentials automatically — no operator involvement.

2. **Team name formatting (GAP-009):** YouTube broadcast titles use raw team names from the PCS Pro UI (e.g. "High Halstow CC - 1st XI"). The club wants the configured club name stripped from whichever team(s) it appears in, so titles read naturally (e.g. "1st XI vs Otford CC - 1st XI"). The team matching the configured club name should always appear first in the title — the order of teams in the PCS Pro dialog is unreliable and must not dictate title order. Additionally, `BroadcastTitleRenderer` only supports `{HomeTeam}` / `{AwayTeam}` tokens — `{HomeClub}` / `{AwayClub}` tokens are not yet implemented because club names come from a different lifecycle stage than match selection. These deferred items are:
   - **DEF-001:** BroadcastTitleRenderer needs access to club names, which are only available in `MatchTeams` (populated post-load), not `MatchInfo` (populated pre-load from match selection grid).
   - **DEF-002:** `MatchInfo` record has no club name fields — they must be added or a composite type used.
   - **DEF-004:** Web UI display of club names as a dedicated element (deferred — not in this HLPS).

3. **YouTube setup tray shortcut (GAP-013):** OAuth consent currently requires running `--setup-youtube` from the command line. Operators should be able to trigger this from a right-click option on the system tray icon.

4. **Club logo (GAP-015):** No custom branding exists — no favicon, no logo in the Web UI header. A club logo SVG (`HHCC logo.svg`) is already provided in the project resources folder. It needs to be integrated into the Web UI header and converted to a rasterised favicon.

5. **Tray icon (GAP-016):** The tray host uses generic `SystemIcons.Application` and `SystemIcons.Warning`. A custom "PCS Remote" `.ico` is needed, styled to match the existing PCS Pro icon (`PCSPRO icon.ico` in `D:\Local\WinUIAutomationTester\resources\`), with distinct states for normal and manual mode (manual mode disables remote automation — see HLPS-005).

---

## 2. Assumptions

| ID | Assumption |
|----|------------|
| A-1 | PCS Pro's login dialog has a username field with AutomationId `txtUsername` that can be read via UIA. |
| A-2 | The "Switch User" element is a hyperlink-class element with Name "Switch User". Clicking it causes the main form to disappear visually (process remains running) and the login dialog reappears with both username and password fields editable. Login then proceeds normally. |
| A-3 | The expected username is the club's own PCS Pro account — a single fixed value stored in configuration. |
| A-4 | Club logo is provided as `HHCC logo.svg` in `D:\Local\WinUIAutomationTester\resources\`. It will be served as SVG in the Web UI header and rasterised to PNG/ICO for the favicon. |
| A-5 | The provided `PCSPRO icon.ico` (in `D:\Local\WinUIAutomationTester\resources\`) is a visual style reference only. A new "PCS Remote" icon must be created in the same style. |
| A-6 | Club name stripping is symmetric — the configured club name is stripped from both team names when matched as an anchored prefix. The team(s) matching the club name appear first in the broadcast title. If neither team matches, names pass through in their original order. If both match, both are stripped. |
| A-7 | PCS Pro UI is always English on the target garage PC. Element names (e.g. "Switch User") are English strings. |
| A-8 | This is a single-tenant deployment — branding assets (logo, favicon, icon) are committed to the repository, not runtime-configurable. |

---

## 3. Constraints

| ID | Constraint |
|----|------------|
| C-1 | Login automation changes must not break the existing happy-path flow (correct user already logged in). |
| C-2 | Team name formatting must be configurable, not hardcoded to HHCC. The club name is a configuration value used for symmetric prefix stripping and title ordering. |
| C-3 | Tray icon must work at 16x16, 24x24, and 32x32 sizes without becoming illegible. |
| C-4 | YouTube setup from the tray must use the same OAuth flow as `--setup-youtube` — no duplication of token acquisition logic. The menu item must be disabled while a setup is already in progress or while a broadcast is live. |
| C-5 | Club logo integration must not require any JavaScript — Blazor Server rendering only. |
| C-6 | Credentials (username and password) must never appear in log output. The switch-user flow must log the comparison result (match/mismatch) without logging the actual values. |
| C-7 | Existing `BroadcastTitleRenderer` templates that do not use `{HomeClub}` / `{AwayClub}` tokens must continue to render identically to today. Null or empty club names must not throw. |
| C-8 | Switch-user automation is bounded to a maximum of 1 retry. If the login dialog still shows a mismatched username after one switch-user cycle, the automation must log an error and escalate (e.g. surface error to UI). |

---

## 4. Scope

| # | Item | GAP/DEF |
|---|------|---------|
| 1 | Read pre-populated username from PCS Pro login dialog | GAP-002 |
| 2 | Compare detected username against configured expected username | GAP-002 |
| 3 | If mismatch: click "Switch User", wait for clean login dialog, enter correct credentials | GAP-002 |
| 4 | Add `PcsPro:ExpectedUsername` configuration key | GAP-002 |
| 5 | Configurable club name for team name formatting | GAP-009 |
| 6 | Strip configured club name prefix from team display names and reorder so the club team appears first (e.g. "High Halstow CC - 1st XI" vs "Otford CC" → "1st XI vs Otford CC") | GAP-009 |
| 7 | Add `{HomeClub}` / `{AwayClub}` tokens to BroadcastTitleRenderer | GAP-009, DEF-001 |
| 8 | Bridge club names from `MatchTeams` into BroadcastTitleRenderer (resolve DEF-001/DEF-002 data flow) | DEF-001, DEF-002 |
| 9 | Add "YouTube Setup..." tray context menu item that launches OAuth flow | GAP-013 |
| 10 | Integrate club logo SVG into Web UI header. Generate a rasterised favicon (`.ico` or `.png` at 16x16, 32x32, 180x180) from the SVG for browser tab/bookmark use. | GAP-015 |
| 11 | Create a new "PCS Remote" tray `.ico` in the style of the provided PCS Pro icon, with distinct normal/manual-mode variants at 16x16, 24x24, 32x32 | GAP-016 |

### Out of Scope

- GAP-005 (date filter override) — testing convenience, not production-facing.
- GAP-006 (site/competition filter awareness) — complex UI, low urgency.
- DEF-005 (live stream status health signal) — performance concern; deferred.
- Web UI club name display as a separate field (DEF-004) — club names will be available via tokens but a dedicated UI element is deferred.

---

## 5. Success Criteria

| SC | Criterion |
|----|-----------|
| SC-1 | When the login dialog username does not match `PcsPro:ExpectedUsername`, automation clicks "Switch User" and re-enters credentials without operator involvement. |
| SC-2 | When the login dialog username matches, is blank (first launch), or `PcsPro:ExpectedUsername` is not configured, existing login flow is unchanged. |
| SC-3 | If switch-user fails after 1 retry (C-8), the automation surfaces an error to the UI and does not enter an infinite loop. |
| SC-4 | Club name stripping follows this matrix: (1) One team matches club name → strip that team, place it first in title; (2) Dialog order reversed from club-first → reorder so club team is first; (3) Neither team matches → names unchanged, original order; (4) Both teams match → strip both. Stripping uses anchored prefix match. |
| SC-5 | Prefix stripping is idempotent — a team name that does not contain the prefix is returned unchanged. |
| SC-6 | `{HomeClub}` and `{AwayClub}` tokens render correctly in broadcast title templates. |
| SC-7 | "YouTube Setup..." tray menu item launches OAuth consent in the default browser and stores the token via the existing `DpapiFileDataStore` path. |
| SC-8 | Club logo SVG renders in the Web UI header. A rasterised favicon (including Apple touch icon at 180x180) derived from the SVG displays in the browser tab. |
| SC-9 | Custom tray icon displays at all required sizes (16/24/32px) with distinct normal and manual-mode variants. |
| SC-10 | All changes build with 0 errors and 0 warnings. All existing tests pass. New logic (switch-user branch, club token rendering, prefix stripping) ships with unit tests. |

---

## 6. Risks

| ID | Risk | Likelihood | Mitigation |
|----|------|------------|------------|
| R-1 | After "Switch User" click, main form disappears and login dialog reappears — automation must re-locate the login dialog window | Low | Existing login dialog detection (`IsLoginDialogVisible`) already waits for the password field; same pattern works after switch user. |
| R-2 | Process continues running during switch user transition — crash watcher must not misinterpret the visual disappearance | Low | Process handle remains valid; crash watcher checks process existence, not window visibility. |
| R-3 | SVG to PNG/favicon conversion may lose quality at small sizes | Low | SVG is resolution-independent; render at target sizes (16px, 32px, 180px) and verify legibility. |
| R-4 | Generated "PCS Remote" icon may not match user expectations for quality or style | Medium | Provide first draft for user feedback; iterate. User can supply a hand-designed replacement at any time. |
| R-5 | OAuth consent flow launched from tray may fail or be cancelled by the operator (browser closed, denied consent, network issue) | Low | Tray menu item re-enables after failure/cancellation. Operator can retry. Existing `GoogleWebAuthorizationBroker` handles cancellation gracefully. |

---

## 7. Unknowns

| ID | Description | Owner | Blocking? |
|----|-------------|-------|-----------|
| U-1 | Whether the username field is pre-populated with the current account or blank on first launch | Agent (garage PC) | No — automation reads whatever is there and compares (SC-2 handles blank case) |
| U-2 | Exact visual style expectations for "PCS Remote" icon (colour, font, cricket elements) | User | No — agent will generate a draft in the PCS Pro icon's style for user review |

---

## 8. Review History

### R1 — 2026-04-21

**Panel:** Opus 4.7 (13 findings), GPT 5.4 (4 findings), Sonnet 4.6 (10 findings)
**Verdict:** REQUEST CHANGES (3/3)

**Triage summary (27 findings → 18 accepted, 9 rejected/duplicate):**

| Finding | Source | Severity | Disposition | Action |
|---------|--------|----------|-------------|--------|
| Problem statement §4 says "PNG" but A-4 says SVG | GPT F-001, Sonnet F-01 | HIGH | Accept | Fixed: §4 now says "SVG" with rasterised favicon |
| Unknowns skip U-2 | Opus F-5, Sonnet F-02 | HIGH | Accept | Fixed: renumbered U-3 → U-2 |
| A-1/A-2 "unverified" | Sonnet F-03 | HIGH | Reject | User provided these values directly — they are confirmed facts, not unverified assumptions |
| Away-team stripping ambiguity | Opus F-1 | MAJOR | Accept | Fixed: A-6 clarifies symmetric stripping + club-first ordering (v0.3 update per user correction). SC-4 specifies full 4-case matrix. SC-5 adds idempotency. |
| Infinite switch-user loop | Opus F-2 | MAJOR | Accept | Fixed: C-8 bounds retry to 1. SC-3 defines failure escalation. |
| English locale assumption | Opus F-3 | MAJOR | Accept | Fixed: A-7 added. |
| Credential logging risk | Opus F-4 | MAJOR | Accept | Fixed: C-6 added — log comparison result only, never values. |
| OAuth failure/cancellation modes | GPT F-003, Sonnet F-07 | MEDIUM | Accept | Fixed: C-4 extended (disable while in progress/live). R-5 added. |
| Backward compat for templates | Sonnet F-06 | MEDIUM | Accept | Fixed: C-7 added — null/empty club names must not throw, existing templates unchanged. |
| Blank username edge case | Sonnet F-05 | MEDIUM | Accept | Fixed: SC-2 now explicitly covers blank username (no-op). |
| DEF references undefined | Sonnet F-04 | MEDIUM | Accept | Fixed: problem statement §2 now glosses DEF-001, DEF-002, DEF-004 inline. |
| Test coverage SC missing | Opus F-12, Sonnet F-08 | MEDIUM | Accept | Fixed: SC-10 added — new logic ships with unit tests. |
| No-match fallback for club name | Opus F-6 | Minor | Accept | Fixed: SC-4 explicitly states "if neither team's club matches, all names pass through unmodified". |
| 180x180 rationale | Opus F-7 | Minor | Accept | Fixed: SC-8 mentions "Apple touch icon at 180x180". |
| Prefix stripping edge cases | Opus F-10 | Minor | Accept | Fixed: SC-4 says "anchored prefix match". SC-5 says "idempotent". |
| OAuth concurrency | Opus F-11 | Minor | Accept | Fixed: C-4 extended to disable menu while setup in progress. |
| Bundle justification | Opus F-13 | Info | Accept | Fixed: problem statement says "bundled because each is individually low-complexity and all relate to production polish". |
| Manual mode definition | Opus F-9 | Minor | Accept | Fixed: GAP-016 in problem statement §5 cross-references HLPS-005. |
| Logo path configurability | Opus F-8 | Minor | Accept | Fixed: A-8 added — single-tenant, assets committed, not runtime-configurable. |
| PCSPRO icon.ico path | Sonnet F-09 | LOW | Accept | Fixed: A-5 now includes full path. |
| Formatting rules ambiguity | GPT F-002 | Medium | Duplicate | Addressed by Opus F-1/F-10 fixes above. |
| Branding approval | GPT F-004 | Medium | Reject | R-4 already covers this — user can replace generated icon at any time. |
| Date | Sonnet F-10 | LOW | Reject | The date 2026-04-21 is correct. |

### R2 — 2026-04-21

**Panel:** Opus 4.7, GPT 5.4, Sonnet 4.6
**Verdict:** APPROVED (3/3 unanimous)

All R1 findings verified as adequately addressed. No regressions found. Two rejected findings (Sonnet F-03 re: A-1/A-2 being "unverified", Sonnet F-10 re: date) accepted as reasonable rejections by all panelists.

---

*End of HLPS-013.*
