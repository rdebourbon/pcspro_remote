# HLPS-021 — Play-Cricket Auto-Watch

| Field | Value |
|---|---|
| **Document** | HLPS-021-PlayCricket-Auto-Watch.md |
| **Type** | High-Level Problem Statement |
| **Status** | APPROVED — Pending user approval |
| **Autopilot** | DISABLED |
| **Date** | 2026-05-18 |
| **Author** | Copilot (GitHub Copilot CLI) |
| **Standards Loaded** | `sefe-dev/dev-standards/dotnet-standards.md`, `copilot-instructions.md` |
| **Dependencies** | HLPS-001 (foundation), HLPS-006 (FlaUI), HLPS-010 (automation hardening), HLPS-016 (operator mode) |

---

## 1. Background & Problem Statement

Match-day operations at High Halstow Cricket Club are time-pressured. The scorer must:

1. Open PCS Pro on the scoring laptop and configure the match.
2. Separately navigate to the garage PC web UI, find the correct match in the Play-Cricket list, and start the scoreboard automation — ideally before the first ball is bowled.
3. At the end of the match, return to the web UI and manually navigate back to match selection.

When the scorer is busy and the above steps don't happen in time, the scoreboard either never starts or stays live after the match ends. The existing automation reduces some of this burden, but the remaining manual steps still cause recurring match-day panics.

**The desired improvement** is an auto-watch service that autonomously handles both the "match started" and "match ended" lifecycle transitions, leaving the operator free to focus on scoring:

- **Auto-load**: When the system is idle in match selection, periodically refresh the PCS Pro match list via automation and detect whether exactly one match is in a *scorer-started* state — meaning a scorer has opened the match in PCS Pro on the scoring laptop, causing it to become selectable in the match list. If exactly one such match is found, automatically select and load it.
- **Auto-close**: When a match is loaded and Play-Cricket reports it as *completed*, start a visible countdown and then automatically stop PCS Pro's RTMP streaming and return to match selection.

This is expected to cover ~95% of home match days. For the remaining 5% (edge cases: multiple simultaneous home matches, API outage, unusual match types), operators continue to use the web UI manually.

---

## 2. Goals

| ID | Goal |
|---|---|
| G-1 | When auto-watch is enabled and the system is idle in match selection, automatically select and load a match when exactly one match is in a *scorer-started* state in the PCS Pro match list, detected via a periodic FlaUI-triggered refresh of the match selection screen. |
| G-2 | When auto-watch is enabled and a match is loaded, automatically stop PCS Pro RTMP streaming (if active) and return to match selection after a configurable countdown when the loaded match transitions to *completed* on Play-Cricket. |
| G-3 | Display the auto-close countdown prominently in the web UI with a cancel button, and fire a browser push notification so the operator is alerted even when the tab is not in focus. |
| G-4 | For every match load (auto or manual), attempt to resolve and cache the corresponding Play-Cricket fixture ID so that auto-close can track the correct fixture. The resolved ID is held by the auto-watch watcher service — not on the PCS Pro match record. |
| G-5 | Expose an auto-watch enabled/disabled toggle in the debug panel. Auto-watch must always start disabled on every application restart. |
| G-6 | At midnight every day, if a match is currently loaded, automatically call `ChangeMatchAsync` to return to match selection (all HHCC matches are single-day). |

---

## 3. Non-Goals

| ID | Non-Goal |
|---|---|
| NG-1 | Auto-starting the YouTube broadcast. Live streaming always requires explicit manual operator action. |
| NG-2 | Auto-stopping the YouTube broadcast. Stopping PCS Pro RTMP naturally causes YouTube to auto-end the broadcast after a few minutes of no data. No explicit YouTube API call is required. |
| NG-3 | HHCC away fixtures (matches played at other grounds) are not in scope — they do not appear in this PCS Pro installation. The watcher handles all matches appearing in the local PCS Pro installation, including those played by other clubs using the same ground. |
| NG-4 | Play-Cricket data entry or write operations of any kind. The integration is read-only. |
| NG-5 | Auto-launching PCS Pro. Auto-load assumes PCS Pro is already running and the system is in a match selection state. |
| NG-6 | Automatic re-enabling of auto-watch after midnight reset. Auto-watch remains enabled/disabled as set by the operator until the app restarts. |

---

## 4. Success Criteria

| ID | Criterion |
|---|---|
| SC-1 | When auto-watch is enabled and the system is in one of the match selection states (`MatchSelection`, `MatchSelectionSearching`, or `MatchSelectionReady`), the system periodically triggers a FlaUI-driven refresh of the PCS Pro match list via a new method on `IPcsProAutomationService` (no direct FlaUI calls — see C-6). If exactly one match is in a scorer-started state following the refresh, the system automatically calls `LoadMatchAsync` for that match within the configured polling interval. Once auto-load has fired and successfully transitioned the system to `MatchLoaded` for a given match (identified by `MatchInfo.MatchId`), it must not fire again for the same `MatchId` until the system restarts or midnight reset clears the state (duplicate-trigger suppression). The exact mechanism for detecting the scorer-started state in the PCS Pro match list is deferred to JIT Spec (see OQ-4). |
| SC-2 | When auto-watch is enabled, a match is loaded, and Play-Cricket reports that match as *completed*, a visible countdown appears in the web UI (default 5 minutes) with a cancel button. |
| SC-3 | When the auto-close countdown expires without cancellation, `StopStreamingAsync` is called unconditionally (it is idempotent — a no-op when not streaming), followed by `ChangeMatchAsync`, returning the system to match selection. |
| SC-4 | When the operator cancels the countdown (or manually calls `ChangeMatchAsync` or `StopAsync`), the countdown is silently cancelled and no further auto-close action is taken for the current match. |
| SC-5 | A browser push notification is sent to all connected operator browsers when the auto-close countdown begins, and again as a final warning when 60 seconds remain. |
| SC-6 | For every match load (auto or manual), the auto-watch service performs a best-effort async resolution of the corresponding Play-Cricket fixture ID once the system enters the match-loaded state. Resolution queries all configured site IDs in parallel; the result is considered uniquely resolved when exactly one fixture across all site results matches the loaded match by team name (normalisation algorithm defined in JIT Spec — OQ-3). If resolution fails (no unique match found, or API unreachable), the fixture ID remains unresolved, a warning is logged, and auto-close polling is skipped. The fixture ID is cleared automatically whenever the system leaves the match-loaded state. |
| SC-7 | The auto-watch toggle in the debug panel defaults to disabled on every application restart, regardless of its state when the app last shut down (it is not persisted). |
| SC-8 | The midnight reset fires independently of the auto-watch toggle state — it is an unconditional safety net for stale match state. At midnight, if the system is in `PcsProState.MatchLoaded` and `IManualModeService.IsManualModeActive` is `false`, `ChangeMatchAsync` is called automatically. At any other state, or while manual mode is active, the midnight action is a no-op. |
| SC-9 | If the Play-Cricket API is unreachable or returns an error, fixture ID resolution and auto-close polling are affected: the watcher logs a warning, takes no automated action for those API-dependent functions, and retries on the next poll cycle. FlaUI-based auto-load detection continues unaffected during API outages. |
| SC-10 | When more than one match is in a scorer-started state in the PCS Pro match list, the auto-load action is suppressed and a log entry noting the ambiguity is written. No error or alert is shown to the operator. |
| SC-11 | Auto-watch does not trigger while `IManualModeService.IsManualModeActive` is `true`. All polling continues, but all automated actions (auto-load, auto-close, and countdown firing) are suppressed. The midnight reset is also suppressed when manual mode is active (see SC-8). |

---

## 5. Constraints

| ID | Constraint |
|---|---|
| C-1 | `PcsRemote.Core` must remain free of dependencies on any Play-Cricket API client or HTTP concerns. New interfaces belong in `PcsRemote.Core`; implementations in a new `PcsRemote.PlayCricket` project. |
| C-2 | Play-Cricket API credentials (API key and one or more site IDs) are stored in configuration only — never in source control. They follow the same pattern as YouTube and PCS Pro credentials. |
| C-3 | The Play-Cricket API is polled at a minimum interval of 60 seconds to respect rate limits. Both the FlaUI-based auto-load polling and the Play-Cricket API auto-close polling share this single configurable interval (default 90 seconds, minimum enforced at 60 seconds). The minimum applies to both polling loops. |
| C-3a | When the configured auto-close countdown is 60 seconds or less, the T-60 warning notification is not fired (only the initial countdown-start notification is sent). |
| C-4 | The auto-watch toggle is not persisted across restarts. It always initialises to disabled. |
| C-5 | The resolved Play-Cricket fixture ID is held by the auto-watch watcher service, not added to the existing match identity record. The match identity record remains a PCS Pro domain object only. A null/unresolved fixture ID is a normal condition — it is not an error. |
| C-6 | All automated actions (auto-load, auto-close, midnight reset) must go through the existing `IPcsProAutomationService` interface. No new direct FlaUI calls are introduced. |
| C-7 | Browser push notifications are opt-in (browser permission prompt). If the operator denies permission, the feature degrades gracefully to web-UI-only countdown display with no crash or error. |
| C-8 | The midnight reset fires `ChangeMatchAsync` only. It does not call `StopAsync` or restart PCS Pro. |
| C-9 | Team name matching between Play-Cricket API fixture data and the loaded PCS Pro match (for fixture ID resolution post-load) must be resilient to formatting differences (e.g., club name prefixes, Roman numeral vs ordinal suffixes). The matching algorithm must be deterministic. When no Play-Cricket fixture can be uniquely matched to the loaded match, the fixture ID remains unresolved, a warning is logged, and auto-close polling is skipped — the operator must manage match end manually. The exact normalisation algorithm is defined in the JIT Spec for the fixture ID resolution step. |
| C-10 | All new mock implementations must implement the same contracts as the real implementations. The Play-Cricket API mock never triggers real network calls and returns configurable test data. |

---

## 6. Assumptions

| ID | Assumption |
|---|---|
| A-1 | The Play-Cricket REST API is publicly accessible from the garage PC with a valid API key. The operator already has the HHCC club ID and API key from the Play-Cricket admin panel. |
| A-2 | Play-Cricket match statuses include a distinct *in-progress* state (e.g., `"Playing"`) and a distinct *completed* state (e.g., `"Result"`). The exact status strings will be confirmed during JIT Spec authoring by consulting the Play-Cricket API documentation. |
| A-3 | Typically, at most one match will be actively scored at the ground on any given day, regardless of which club is playing. The "exactly one scorer-started match" guard handles the exceptional case where this assumption is violated. |
| A-4 | All HHCC competitive matches are single-day. No multi-day fixture will ever be loaded when the midnight reset fires. |
| A-5 | **Unconfirmed (pending OQ-2)** — The Play-Cricket API is expected to return enough data (fixture ID, team names, match status) per match in a single season query to support fixture ID resolution and auto-close polling without requiring per-fixture detail queries. To be confirmed during JIT Spec authoring. |
| A-6 | The resolution of a Play-Cricket fixture ID at match load time can be accomplished within 2–3 seconds without blocking the UI thread, using an async background task. |
| A-7 | The auto-watch feature will always be enabled manually by an operator before the first match of the day. There is no requirement for fully unattended startup-to-end operation. |

---

## 7. Technical Direction Notes

> **Advisory only.** This section captures the current technical thinking to guide IS and JIT Spec authoring. It is not subject to HLPS review, is not binding on the implementation, and may be revised as delivery progresses without triggering a new HLPS review round.

### 7.1 New Project: `PcsRemote.PlayCricket`

A new `net8.0` class library (no Windows-specific dependencies) responsible for all Play-Cricket API interaction. **Used exclusively for fixture ID resolution and auto-close status polling** — not for auto-load trigger detection:

- `IPlayCricketApiClient` — interface (defined in `PcsRemote.Core`) for querying fixtures by season and site ID.
- `PlayCricketApiClient` — real HTTP implementation using `HttpClient` + `IHttpClientFactory`.
- `PlayCricketOptions` — configuration POCO (`PlayCricket:ApiKey`, `PlayCricket:SiteIds` (list of strings), `PlayCricket:PollIntervalSeconds`).
- Fixture DTOs mapping the Play-Cricket JSON response.

### 7.2 New Project: `PcsRemote.PlayCricket.Mock`

- `MockPlayCricketApiClient` — returns configurable fixture lists for development/testing without network access.

### 7.3 New Service: `IPlayCricketWatcherService` (Core)

Defined in `PcsRemote.Core`. Responsible for:
- Tracking auto-watch enabled/disabled state (never persisted).
- Exposing `string? CurrentFixtureId` — the resolved Play-Cricket fixture ID for the currently loaded match; `null` when not in `MatchLoaded` state or when resolution failed.
- Firing `AutoLoadTriggered` and `AutoCloseCountdownStarted` events. All events follow the project's `EventHandler<TSnapshot>` pattern with immutable snapshot types; specific snapshot type definitions are deferred to JIT Spec.
- Exposing the current countdown state (`TimeSpan? AutoCloseRemainingTime`).

### 7.4 New Hosted Service: `PlayCricketWatcherHostedService`

A background `IHostedService` (in `PcsRemote.Web`) that operates two independent polling mechanisms:

- **Auto-load polling** (FlaUI-based): Triggers a refresh of the PCS Pro match selection screen via a new method on `IPcsProAutomationService` (no direct FlaUI calls — see C-6). Counts matches in a scorer-started state. If exactly one, calls `LoadMatchAsync`. Does not use the Play-Cricket API.
- **Auto-close polling** (Play-Cricket API-based): Once a match is loaded and `CurrentFixtureId` is resolved, polls `IPlayCricketApiClient` on the configured interval for the fixture's completion status. Drives the countdown and eventual `StopStreamingAsync` + `ChangeMatchAsync` sequence.
- Manages the countdown timer.
- Suppresses all actions when manual mode is active.

### 7.5 `MatchInfo` — No Changes Required

`MatchInfo` remains unchanged. It is a PCS Pro identity record and is not extended with Play-Cricket data. The resolved Play-Cricket fixture ID is held exclusively by `IPlayCricketWatcherService.CurrentFixtureId`, decoupled from the PCS Pro domain model.

### 7.6 UI Changes

- **Debug panel**: Add auto-watch toggle (mirrors the date override toggle pattern from HLPS-019).
- **Web UI**: Countdown banner (appears when auto-close countdown is active, includes cancel button).
- **Browser push notifications**: JavaScript interop to request `Notification` permission on first auto-watch enable, and to fire notifications at countdown start and at T-60 seconds.

---

## 8. Out of Scope

- Play-Cricket API write operations.
- YouTube broadcast lifecycle automation.
- Auto-launching PCS Pro (system must already be in a match selection state).
- Persisting auto-watch state across restarts.
- HHCC away fixtures (matches played at other grounds) and multi-day fixtures.
- GAP-018 (VOD Chapter Markers) and GAP-019 (Match Center Embed) — separate enhancement requests.

---

## 9. Open Questions

| ID | Question | Owner | Blocking Status | Status |
|---|---|---|---|---|
| OQ-1 | Exact Play-Cricket API status strings for *in-progress* and *completed* states. | Dev | Blocks S-007 (JIT Spec) | Open — to be confirmed during JIT Spec authoring via API documentation review. |
| OQ-2 | Play-Cricket API endpoint structure for season queries and site ID filtering, to be used for fixture ID resolution. Server-side date filtering availability is unknown; client-side date filtering may be required. | Dev | Blocks S-002/S-006 (JIT Spec) | Partially resolved — endpoint confirmed as `matches.json` with site ID, season, and API token parameters; no confirmed server-side date filter. Full confirmation deferred to JIT Spec. |
| OQ-3 | Whether team name normalisation (stripping club name, Roman numeral → ordinal) is sufficient to correlate Play-Cricket fixture teams with the loaded PCS Pro match for fixture ID resolution, or whether a more sophisticated match is required. | Dev | Blocks S-006 (JIT Spec) | Open — scope reduced: only needed for post-load fixture ID resolution (auto-close), not for auto-load. To be assessed during JIT Spec. Existing `TeamNameFormatter` is a candidate. |
| OQ-4 | What UI property or visual indicator in the PCS Pro match selection screen distinguishes a scorer-started (selectable) match from one that has not yet been opened? Requires inspection of PCS Pro's FlaUI element tree to confirm the detection mechanism. | Dev | Blocks S-005 (JIT Spec) — resolved | **Resolved** — A fixture only appears in the PCS Pro match selection dialog after the scorer on the scoring laptop completes team/match details and records the toss (fixture → match conversion). No special UI property detection is required: presence in the dialog IS the scorer-started indicator. The match selection dialog does NOT auto-refresh; the automation must trigger an explicit UI refresh before reading the list. Three confirmed mechanisms: (1) click "Clear Filters", (2) change selection criteria, (3) close and reopen the dialog. Preferred mechanism deferred to JIT Spec; "Clear Filters" is the likely choice as it is the least disruptive for a periodic background operation. |

---

## 10. Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 3 | Claude Opus 4.5, GPT-5.4 (GPT-5.2-Codex substituted — format incompatibility; panel valid: 2 architectures) | CRITICAL: 0 / HIGH: 4 / MEDIUM: 8 / LOW: 6 | Accept: 10, Downgrade: 2, Reject: 1, Defer: 3 | REVISION | Two HIGH accepted: wrong property name (`IsManualModeEnabled` → `IsManualModeActive`), immutable record mutation (resolved by removing `MatchInfo.PlayCricketFixtureId` — fixture ID held by `IPlayCricketWatcherService` instead). Two HIGH downgraded: API contract unresolved (OQ-1/OQ-2 deferred to JIT Spec per A-2 — normal for HLPS), match correlation (OQ-3 constraint strengthened in C-9). SC-10 silent suppression finding rejected — explicit user requirement. |
| R2 | Tier 3 | Claude Opus 4.5, GPT-5.4 | CRITICAL: 0 / HIGH: 0 / MEDIUM: 2 / LOW: 1 | Accept: 3 | APPROVED | Opus: all 11 R1 fixes verified, no regressions — APPROVE. GPT-5.4 REVISE — 3 targeted regressions fixed: G-4 still referenced `LoadedMatch` (updated to `IPlayCricketWatcherService.CurrentFixtureId`); Background/G-1 still used "transitions to in-progress" language inconsistent with SC-1 state-polling semantics (aligned); C-3a self-contradictory hard-minimum + exception clause (simplified to: T-60 notification skipped when countdown ≤ 60s, no hard minimum enforced). Unanimous approval after fixes. |
| R3 | Tier 3 | Claude Opus 4.5, GPT-5.4 | CRITICAL: 0 / HIGH: 5 / MEDIUM: 4 / LOW: 3 | Accept: 9, Downgrade: 1 | REVISION | Material architecture change submitted: auto-load switched from Play-Cricket API polling to FlaUI-based match list refresh. Five HIGHs: NG-3/§8 away-match scope contradiction (Accept — NG-3 reworded as proper non-goal, §8 updated); SC-9 incorrectly suppressed FlaUI auto-load on API outage (Accept — SC-9 rewritten to carve out FlaUI path); missing `IPcsProAutomationService` refresh method spec (Accept — SC-1/§7.4 updated to reference new interface method); SC-1 testability on unresolved OQ-4 (Downgrade to MEDIUM — note added to SC-1 deferring to OQ-4). MEDIUMs: polling interval shared between two mechanisms (Accept — C-3 clarified); SC-6 uniqueness semantics across multi-site queries (Accept — one-sentence clarification added); scorer-started terminology tightened in Background. All R1/R2 fixes verified intact. |
| R4 | Tier 3 | Claude Opus 4.5, GPT-5.4 | CRITICAL: 0 / HIGH: 0 / MEDIUM: 0 / LOW: 0 | — | APPROVED | All 10 R3 fixes verified correct by both reviewers. Zero regressions. Unanimous approval. |
| R5 | Tier 0 | Self-Cert | — | — | SELF-CERTIFIED | OQ-4 updated with confirmed refresh mechanism detail. Match selection dialog does NOT auto-refresh — `GetSelectableMatchesAsync()` must trigger an active UI refresh before reading entries. Three mechanisms confirmed by user: (1) Clear Filters, (2) change selection criteria, (3) close/reopen. Preferred choice deferred to JIT Spec. No scope change; no functional text altered. |
| R6 | Tier 0 | Self-Cert | — | — | SELF-CERTIFIED | Abstraction-level editorial pass per HLPS guidance. Fixes: SC-6 rewritten to remove proposed interface/property names; C-3 config key name removed; C-5 rewritten to remove interface and property name; C-10 class name removed; §7 renamed from "Proposed Architecture" to "Technical Direction Notes" with advisory disclaimer added; §9 Unknowns table updated to add required Blocking Status column. No Success Criteria, Constraint, or scope changed. |
