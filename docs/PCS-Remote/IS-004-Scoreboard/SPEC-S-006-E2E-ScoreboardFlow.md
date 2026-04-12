# SPEC-S-006 — Playwright E2E: Scoreboard Flow

| Field | Value |
|---|---|
| **Spec** | SPEC-S-006-E2E-ScoreboardFlow.md |
| **Step** | IS-004 S-006 |
| **Status** | APPROVED |
| **Version** | 0.6 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-004-Scoreboard.md v0.2 (APPROVED) |
| **Dependencies** | S-003 (ScoreboardPreview), S-004 (RefreshScoreboardButton), S-005 (ChangeMatchButton), IS-003 S-009 (E2E infrastructure) |

---

## 1. Purpose

Add Playwright E2E tests for the complete IS-004 scoreboard flow: image rendering, refresh, multi-browser simultaneous update (S-SC-8), and change-match modal interaction. All tests run in headless Chromium against the existing `PcsProWebApplicationFactory` Kestrel host.

---

## 2. Scope

**In scope:**
- `PcsProWebApplicationFactory.BuildKestrelHost()` extended to include `IScoreboardService`, `ScoreboardPollingService`, `IConfirmDialogService`, and a 1-second capture interval configuration key.
- Four new Playwright test methods added to `PcsProE2ETests` (TC-1–TC-4).
- A private `DriveToMatchLoadedAsync` helper shared by all tests.
- A per-test reset strategy (try/finally: `StopAsync` + `ClearCache`) consistent with the existing AC-3 pattern.

**Out of scope:**
- Firefox or WebKit browsers.
- Visual regression / screenshot comparison.
- Load testing or concurrency beyond two browser contexts.
- Error and failure path E2E tests — these are covered by bUnit tests in S-004 and S-005; S-006 extends coverage to the end-to-end happy paths only.
- Any changes to production source files outside `PcsProWebApplicationFactory.cs` and `PcsProE2ETests.cs`.

---

## 3. Factory Changes — `PcsProWebApplicationFactory.BuildKestrelHost()`

The following must be added to the Kestrel host so that scoreboard components render correctly and `IConfirmDialogService` can be resolved by `ChangeMatchButton`:

- Register `IScoreboardService` as a singleton (same pattern as `Program.cs`).
- Register `ScoreboardPollingService` as a hosted service.
- Register `IConfirmDialogService` as scoped, wrapping `DialogService`. Note: `AddRadzenComponents()` registers the concrete `DialogService` but does NOT register `IConfirmDialogService` — that is the project's own wrapper interface and must be registered explicitly (same as `Program.cs`).
- Add the following configuration keys to the existing `AddInMemoryCollection` block:
  - `Scoreboard:CaptureIntervalSeconds=1` — keeps test wall-clock time short; the polling loop fires within 1 second of entering `MatchLoaded`.
  - `PcsPro:Mock:ImageVariationProbability=1` — guarantees every `CaptureScoreboardImageAsync` call generates a distinct new image so that each polling tick produces an observable `src` attribute change, enabling TC-3 to reliably detect simultaneous update across browser contexts.

No changes to `ConfigureWebHost` (the WAF TestServer host) are needed — scoreboard services are only required by the Kestrel host that Playwright connects to.

---

## 4. Test Cases

All tests share a `DriveToMatchLoadedAsync(IPage page)` helper that:
1. Navigates the page to `_serverAddress`.
2. Resolves `MockPcsProAutomationService` from `_factory.RealServices` (cast from `IPcsProAutomationService`).
3. Calls `LaunchAndLoginAsync()` on the service — transitions `NotRunning → MatchSelection`.
4. Calls `GetTodaysMatchesAsync()` to retrieve available matches, then calls `LoadMatchAsync(matches[0])` — transitions `MatchSelection → MatchLoaded`.
5. Waits for the `.change-match-button` element to become visible on the page (confirms the Blazor circuit has received the `MatchLoaded` state and rendered the MatchLoaded section).

The helper does not assert image presence — TC-1 covers that explicitly. The `LaunchAndLoginAsync → GetTodaysMatchesAsync → LoadMatchAsync` call sequence is mandatory; calling `LoadMatchAsync` from `NotRunning` throws `InvalidOperationException`.

---

### TC-1: Scoreboard image appears in MatchLoaded

**Precondition:** Service in `NotRunning`.

**Steps:**
1. Call `DriveToMatchLoadedAsync` for `_page1`.
2. Wait for `.scoreboard-image` element with a `src` attribute starting with `data:image/jpeg;base64,`.

**Assertion timeout:** 3 000 ms (aligns with S-SC-1 ≤3s SLA; the polling interval is 1 s, so at most 2 ticks are needed).

**Teardown:** In `finally`: call `mockAutomationService.StopAsync()` (transitions to `NotRunning`, which stops the polling loop via its existing `StateChanged` subscription); call `scoreboardService.ClearCache()` (resets the image cache).

---

### TC-2: Refresh button visible; click produces success notification

**Precondition:** Service driven to `MatchLoaded`; initial image present (wait as per TC-1 before clicking).

**Steps:**
1. `DriveToMatchLoadedAsync` + wait for image (TC-1 condition).
2. Assert `.refresh-scoreboard-button` is visible and enabled.
3. Click `.refresh-scoreboard-button`.
4. Wait for a Radzen notification message containing text "Scoreboard refreshed".

**Assertion timeout:** 5 000 ms for the toast (covers one `ForceRefreshAsync` round-trip).

**Teardown:** In `finally`: call `mockAutomationService.StopAsync()`; call `scoreboardService.ClearCache()`.

---

### TC-3: Multi-browser simultaneous image update (S-SC-8)

**Precondition:** Both `_page1` (context 1) and a second context `_page2` / `_context2` in `MatchLoaded` with an initial image visible. `CaptureIntervalSeconds=1` + `ImageVariationProbability=1` guarantee a new image is produced on every polling tick.

**Steps:**
1. `DriveToMatchLoadedAsync` for `_page1`; wait for initial `.scoreboard-image` src (TC-1 condition).
2. Open `_context2` / `_page2`; navigate to `_serverAddress`; wait for `.scoreboard-image` on `_page2` (singleton service is already in `MatchLoaded`, page renders immediately).
3. Capture the current `src` attribute from each page as stable baselines: `p1Base` (page1), `p2Base` (page2).
4+5. In parallel (`Task.WhenAll`): wait (≤3 000 ms) for `_page1` src to differ from `p1Base` AND wait (≤3 000 ms) for `_page2` src to differ from `p2Base`. Both waits run concurrently so both pages resolve within the same tick window — eliminates the scenario where page1 advances to tick N+1 while step 5 is still polling.
6. In parallel (`Task.WhenAll`): capture `p1New = page1.src` and `p2New = page2.src`. Parallel capture reduces the inter-read window from one CDP RTT (5–25 ms) to scheduler jitter (sub-100 µs), making a tick firing between the two reads genuinely negligible.
7. Assert `p1New == p2New` — confirms both circuits received the same `ScoreboardUpdated` broadcast (S-SC-8).

**Assertion:** Both src values changed from their own baselines within 3 s; captured values are equal. Race-free: parallel waiters eliminate the step 4→5 tick-skew window; parallel capture eliminates the sequential-read CDP-RTT window.

**Assertion:** Both src values changed from their own baselines within 3 s; captured values are equal — S-SC-8 broadcast verified.

**Teardown:** In `finally`: call `mockAutomationService.StopAsync()`; call `scoreboardService.ClearCache()`. `_page2`/`_context2` are closed by the existing `TestCleanup` pattern.

---

### TC-4: Change match button click shows confirmation dialog; cancel preserves scoreboard

**Precondition:** Service in `MatchLoaded`; initial image present.

**Steps:**
1. `DriveToMatchLoadedAsync` for `_page1`; wait for image.
2. Click `.change-match-button`.
3. Wait for the Radzen confirmation dialog to appear — the dialog contains a Cancel button. Locator: `.rz-dialog-confirm-buttons .rz-secondary` (or `GetByRole(AriaRole.Button, Name: "Cancel")`).
4. Click the Cancel button.
5. Assert the dialog is no longer visible — locator: `.rz-dialog-wrapper` with state `Hidden` (or `page.GetByRole(AriaRole.Dialog).WaitForAsync(new() { State = WaitForSelectorState.Hidden })`), timeout 3 000 ms.
6. Assert `.change-match-button` is visible and enabled (operation in progress flag cleared).
7. Assert `.scoreboard-image` is still present (scoreboard not cleared by cancel).

**Assertion timeout:** 5 000 ms for dialog appearance; 3 000 ms for post-cancel re-enable assertion.

**Teardown:** In `finally`: call `mockAutomationService.StopAsync()`; call `scoreboardService.ClearCache()`.

---

## 5. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | All four TC-1–TC-4 tests pass in headless Chromium |
| AC-2 | Multi-browser test (TC-3) verifies both contexts receive the updated image within 3 s |
| AC-3 | Factory `BuildKestrelHost()` includes `IScoreboardService`, `ScoreboardPollingService`, and `IConfirmDialogService` |
| AC-4 | No regression in the existing 230 non-E2E tests |
| AC-5 | No regression in the existing 3 E2E tests (AC-1 smoke, AC-2 user count, AC-3 state broadcast) |
| AC-6 | Each test resets singleton state in a `finally` block so test ordering does not matter |

---

## 6. Branch and Commit Strategy

- Branch: `feature/S-006-e2e-scoreboard-flow`

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | NEEDS REVIEW — Sonnet 1 HIGH (TC-3 non-deterministic), 2 MEDIUM (DriveToMatchLoaded undefined, teardown unqualified); GPT 1 MEDIUM (error scenarios — rejected, out of scope); fixes applied in v0.2 |
| R2 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | GPT: APPROVED. Sonnet: NEEDS REVIEW — 1 MEDIUM (TC-3 background-poll race makes ForceRefreshAsync assertion vacuous), 2 LOW; fixes applied in v0.3 |
| R3 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | GPT: APPROVED. Sonnet: NEEDS REVIEW — 1 HIGH (TC-3 sequential wait tick-skew: page1 captures tick N, tick N+1 may fire before page2 resolves, equality assertion compares different ticks), 1 LOW (TC-4 step 5 dialog-hidden locator absent); fixes applied in v0.4 |
| R4 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | GPT: APPROVED. Sonnet: NEEDS REVIEW — 1 HIGH (TC-3 v0.4 "wait for equal page1NewSrc" is permanently unsatisfiable once page2 advances past tick N; images are non-replayable with ImageVariationProbability=1); fixes applied in v0.5 |
| R6 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | **UNANIMOUS APPROVED** — parallel waiters and parallel capture are race-free; no blocking issues |

### R1 Findings Applied (v0.2)

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| TC-3 non-deterministic: ImageVariationProbability=0.2 means same bytes 80% of runs | Sonnet | HIGH | Accept | Added `PcsPro:Mock:ImageVariationProbability=1` to factory config keys in §3 |
| DriveToMatchLoadedAsync steps not defined | Sonnet | MEDIUM | Accept | Added explicit 5-step definition to §4 preamble |
| Teardown "StopAsync + ClearCache" unqualified | Sonnet | MEDIUM | Accept | All 4 teardown entries now specify `mockAutomationService.StopAsync()` and `scoreboardService.ClearCache()` |
| IConfirmDialogService "already covered by AddRadzenComponents" | Sonnet | LOW | Reject | AddRadzenComponents() registers concrete DialogService only; IConfirmDialogService is project wrapper requiring explicit registration. Spec note added to §3 |
| Insufficient error scenario coverage | GPT | MEDIUM | Reject | Out of IS-004 S-006 defined scope; error paths covered by bUnit tests in S-004/S-005. §2 Out of scope updated with explicit note |
| Reset strategy ambiguity | GPT | LOW | Accept (via MEDIUM-3 fix) | Addressed by qualifying teardown service names |
| Test data isolation not stated | GPT | LOW | Defer | Per-test StopAsync+ClearCache is the isolation mechanism; self-evident from teardown spec |
| Timeout justification absent | GPT | LOW | Reject | 3s directly tied to S-SC-1 SLA; 5s is standard Playwright dialog/toast wait |

### R5 Findings Applied (v0.6)

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| Sequential reads in step 6 create ~1-10% CI false-negative: CDP RTT (5-25ms) is non-negligible vs 1s tick boundary | Sonnet | MEDIUM | Accept | Steps 4+5 and step 6 both parallelised with `Task.WhenAll`; inter-read window reduced to scheduler jitter (~100µs) |
| Step 7 correctness argument gap: page1 may advance to tick N+1 while step 5 is still polling | Sonnet | LOW | Accept | Resolved by same parallel waiters fix: both pages resolve within same `Task.WhenAll` completion window |

### R4 Findings Applied (v0.5)

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| TC-3 "wait for equal page1NewSrc" permanently unsatisfiable: once page2 advances past tick N, the unique non-replayable image is gone; condition times out deterministically | Sonnet | HIGH | Accept | Reverted to two independent "differ from own baseline" waiters (steps 4 & 5); capture both src values after both waits complete (step 6); assert p1New == p2New (step 7). Race-free: if tick N+1 fires between waits, both pages are on N+1 at capture time — equality still holds. |

### R3 Findings Applied (v0.4)

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| TC-3 sequential wait tick-skew: page1 captures tick N, tick N+1 may fire before page2's "differ from baseline" resolves, equality assertion compares different ticks | Sonnet | HIGH | Accept | Step 5 rewritten: "wait for page2 src to **equal** `page1NewSrc`" — pins both contexts to the same broadcast tick; step 4 captures value as `page1NewSrc`; step 6 is now a documentation assertion |
| TC-4 step 5 dialog-hidden locator absent | Sonnet | LOW | Accept | Added `.rz-dialog-wrapper` `Hidden` / `GetByRole(AriaRole.Dialog)` hidden locator to TC-4 step 5 |


| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| TC-3 background-poll race: 1s interval fires during DriveToMatchLoadedAsync setup; `ForceRefreshAsync()` assertion is vacuously passable | Sonnet | MEDIUM | Accept (Option B) | Removed `ForceRefreshAsync()` from TC-3 steps; test now asserts both contexts receive a background-polling update within 3s and that both receive the same image (src values equal) — directly verifies S-SC-8 broadcast without relying on a specific trigger |
| TC-4 Radzen cancel button locator not specified | Sonnet | LOW | Accept | Added locator hint `.rz-dialog-confirm-buttons .rz-secondary` / `GetByRole(AriaRole.Button, Name: "Cancel")` to TC-4 step 3 |
| "Teardown: same" fragile cross-reference | Sonnet | LOW | N/A | Already inline in all TCs from v0.2 fix; finding superseded |
