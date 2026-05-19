# IS-021: Play-Cricket Auto-Watch — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-021-PlayCricket-Auto-Watch.md |
| **Status** | IN REVIEW |
| **Version** | 0.8 |
| **Date** | 2026-05-19 |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` |
| **Prerequisites** | HLPS-001 (foundation), HLPS-006 (FlaUI), HLPS-010 (automation hardening), HLPS-016 (operator mode) — all delivered. |

---

## Overview

This sequence implements the HLPS-021 scope in ten atomic steps.

The feature has two independent runtime concerns: **auto-load** (FlaUI-based detection of scorer-started matches) and **auto-close** (Play-Cricket API-based detection of match completion). These share a single hosted service and a single polling interval.

Steps proceed in dependency order: Core contracts first, then implementations, then the two hosted-service polling loops, then peripheral concerns (midnight reset, UI, push notifications).

> **Note on auto-load method:** `IPcsProAutomationService` already exposes `GetTodaysMatchesAsync()`, which drives a PCS Pro UI interaction and returns the list of converted matches for today. The JIT Spec for S-005 will confirm whether this existing method is sufficient (if it already includes a UI refresh step) or whether a dedicated new method is required. **Either outcome must satisfy the SC-1 constraint: the match list read must be preceded by a UI refresh (Clear Filters) to capture newly converted matches that appeared since the last poll.** The HLPS requirement for a "new method" (SC-1) is satisfied by either outcome.

Steps use stable IDs S-001 through S-010. IDs are never renumbered; deferred steps leave gaps.

---

## Accepted Risks from Approved HLPS-021

| Risk Summary | Citation |
|---|---|
| Fixture ID resolution may fail (no unique match); auto-close is skipped and operator manages end manually | HLPS-021 SC-6, C-9 |
| Play-Cricket API outage suppresses auto-close but does not affect auto-load | HLPS-021 SC-9 |
| Multiple simultaneous scorer-started matches suppress auto-load silently (no operator alert) | HLPS-021 SC-10 — explicit user requirement |
| Auto-watch always starts disabled on restart; no unattended startup-to-end operation | HLPS-021 SC-7, A-7 |
| Browser push notifications are opt-in; graceful degradation on denial | HLPS-021 C-7 |
| OQ-1 (exact Play-Cricket status strings) and OQ-3 (team name normalisation algorithm) are deferred to JIT Spec | HLPS-021 §9 Open Questions |

---

## Steps

### S-001 — Project scaffold + Core contracts + configuration

**What changes:**

- New `PcsRemote.PlayCricket` class library project (`net8.0`) referencing `PcsRemote.Core`.
- New `PcsRemote.PlayCricket.Mock` class library project (`net8.0`) referencing `PcsRemote.Core`.
- `IPlayCricketApiClient` interface added to `PcsRemote.Core`: a single method that queries fixtures for a given site ID and season, returning a list of `PlayCricketFixture` records.
- `PlayCricketFixture` immutable record added to `PcsRemote.Core`: represents a single fixture returned by the Play-Cricket API (fixture ID, status string, match date, home/away team names, home/away club names).
- `PlayCricketOptions` configuration POCO added to `PcsRemote.PlayCricket` (mirrors `YouTubeOptions` pattern): `ApiKey`, `SiteIds` (list of strings), `PollIntervalSeconds` (default 90, minimum 60), `AutoCloseCountdownSeconds` (default 300), `UseMock` flag.
- `PlayCricket` configuration section scaffolded in `appsettings.json` with placeholder values and XML doc comments.
- `PcsRemote.PlayCricket` and `PcsRemote.PlayCricket.Mock` added to the solution.

**Why:** All downstream steps depend on these types and projects existing. Placing `IPlayCricketApiClient` and `PlayCricketFixture` in `PcsRemote.Core` maintains the zero-external-dependency rule. The configuration section must exist before any service can read it.

**Dependencies:** None — this is the first step.

**Verification intent:** Solution builds with zero warnings. `PlayCricketFixture` is constructable. `PlayCricketOptions` binds from configuration with expected defaults. `IPlayCricketApiClient` is in the `PcsRemote.Core` namespace. Both new projects reference only `PcsRemote.Core` (no Windows-specific dependencies). All existing tests pass.

---

### S-002 — `PlayCricketApiClient` — real HTTP implementation

**What changes:**

- `PlayCricketApiClient` class added to `PcsRemote.PlayCricket`, implementing `IPlayCricketApiClient`.
- Uses `IHttpClientFactory` to make HTTP GET requests to the Play-Cricket `matches.json` endpoint with `site_id`, `season`, and `api_token` query parameters.
- Deserialises the JSON response and maps it to `IReadOnlyList<PlayCricketFixture>`. Internal JSON DTOs are private to this project.
- Returns an empty list (with a warning log) on HTTP error or deserialization failure rather than throwing; callers treat an empty list as "no data available".
- Registered in DI in `PcsRemote.Web` via `PlayCricketOptions.UseMock` flag (real implementation when `false`).

**Why:** The auto-close loop and fixture ID resolution both depend on a working API client. Building and testing the real client independently ensures the HTTP and mapping concerns are verified before they are composed with polling logic.

**Dependencies:** S-001.

**Verification intent:** Unit tests covering: successful response deserialised to correct `PlayCricketFixture` records; HTTP error returns empty list + warning log; malformed JSON returns empty list + warning log; `CancellationToken` propagated to HTTP call. Build and all existing tests pass.

---

### S-003 — `MockPlayCricketApiClient`

**What changes:**

- `MockPlayCricketApiClient` class added to `PcsRemote.PlayCricket.Mock`, implementing `IPlayCricketApiClient`.
- Returns configurable in-memory fixture lists; never makes network calls.
- Supports multiple `SiteId` → fixture list mappings to simulate multi-site scenarios.
- Registered in DI when `PlayCricketOptions.UseMock` is `true`.

**Why:** All development and local testing uses mock mode. The mock must be complete before the fixture ID resolution (S-006) and auto-close polling (S-007) steps can be developed and tested without network access. Also serves as the contract reference for the real implementation.

**Dependencies:** S-001.

**Verification intent:** Unit tests verifying: returns configured fixtures for a given site ID; returns empty list for an unconfigured site ID; never throws on any input. Implements `IPlayCricketApiClient` contract identically to the real client. Build and all existing tests pass.

---

### S-004 — `IPlayCricketWatcherService` + service state implementation

**What changes:**

- `IPlayCricketWatcherService` interface added to `PcsRemote.Core`:
  - Properties: `bool IsAutoWatchEnabled`, `string? CurrentFixtureId`, `TimeSpan? AutoCloseRemainingTime`.
  - Methods: `EnableAutoWatch()`, `DisableAutoWatch()`, `StartCountdown(string fixtureId, TimeSpan duration)`, `bool? TickCountdown()`, `CancelCountdown()`, `ClearAutoLoadSuppression()`, `void RecordAutoLoadSuppressed(string matchId)`, `bool IsAutoLoadSuppressed(string matchId)`, `void DismissAutoClose(string fixtureId)`, `bool IsAutoCloseDismissed(string fixtureId)`.
  - Events (following the project `EventHandler<TSnapshot>` pattern): `AutoLoadTriggered`, `AutoCloseCountdownStarted`, `AutoCloseT60Warning`, `AutoCloseCountdownCancelled`, `AutoCloseFired`.
  - Corresponding immutable snapshot types for each event.
- `PlayCricketWatcherService` implementation added to `PcsRemote.Web`: manages state (enabled/disabled, `CurrentFixtureId`, countdown state) and fires events. Contains no polling or timer logic — those belong to the hosted service.
  - `EnableAutoWatch()` / `DisableAutoWatch()` toggle the enabled flag. `DisableAutoWatch()` delegates to `CancelCountdown()` if a countdown is active (single code path; no duplicate event firing).
  - `StartCountdown(string fixtureId, TimeSpan duration)` records the fixture ID this countdown belongs to (`countdownFixtureId`) along with initial duration, sets `AutoCloseRemainingTime`, and fires `AutoCloseCountdownStarted`. Called by the hosted service when auto-close triggers.
  - `bool? TickCountdown()` — called by the hosted service once per second. Return semantics: `null` when no countdown is active (`AutoCloseRemainingTime` is null — loop should exit); `false` when countdown is running and has time remaining — loop should continue; `true` when this tick caused expiry — loop should execute expiry sequence. Fires `AutoCloseT60Warning` when remaining time crosses below 60 seconds (suppressed if started ≤ 60 seconds). On expiry: clears `AutoCloseRemainingTime` and clears `countdownFixtureId` but does **not** fire `AutoCloseFired` — that event is fired by the hosted service after successful execution of the expiry sequence.
  - `CancelCountdown()` clears `AutoCloseRemainingTime` and fires `AutoCloseCountdownCancelled`.
  - `ClearAutoLoadSuppression()` — called exclusively by `MidnightResetHostedService` (S-008) as part of the midnight reset sequence. Clears the internal auto-load suppression set directly (same class ownership). This is the only mechanism to clear suppression (other than process restart). It does not affect countdown or dismissed state.
  - `RecordAutoLoadSuppressed(string matchId)` — called by `PlayCricketWatcherHostedService` after a successful auto-load to register the match ID as suppressed. `IsAutoLoadSuppressed(string matchId)` — called by the hosted service before triggering an auto-load; returns `true` if the match ID has already been auto-loaded in this session.
  - `DismissAutoClose(string fixtureId)` — called by `PlayCricketWatcherHostedService` when a countdown is cancelled or expires (prevents countdown restart). `IsAutoCloseDismissed(string fixtureId)` — called by the hosted service before starting a countdown; returns `true` if the fixture has been dismissed.
  - **State ownership:** `PlayCricketWatcherService` owns ALL state: the enabled flag, `CurrentFixtureId`, countdown state, the suppression set, and the dismissed fixture ID. `PlayCricketWatcherHostedService` is entirely stateless with respect to domain state — it reads and writes only through the `IPlayCricketWatcherService` interface. This ensures that `ClearAutoLoadSuppression()` can clear the suppression set directly, and `EnableAutoWatch()` can clear the dismissed fixture ID directly, with no cross-service call paths.
  - **Re-enable resets dismissed state (intentional):** Calling `EnableAutoWatch()` after `DisableAutoWatch()` signals operator intent to resume automation; `EnableAutoWatch()` clears the dismissed fixture ID directly (same class). This allows auto-close to restart for the still-loaded fixture if the operator re-enables.
  - **Thread safety:** All state mutations are protected by an internal lock. Events are fired after releasing the lock.
  - `CurrentFixtureId` resets to `null` whenever the system leaves `PcsProState.MatchLoaded` (subscribed to `IPcsProAutomationService.StateChanged`).
- Registered in DI as singleton.
- **Responsibility boundary:** `PlayCricketWatcherService` owns all observable state and events. `PlayCricketWatcherHostedService` (S-005 / S-007) owns all scheduling and timing logic (auto-load poll loop, second-resolution countdown loop, API poll loop) and calls into `PlayCricketWatcherService` via the interface to mutate state and raise events.

**Why:** Separating state management from polling keeps the hosted service focused on scheduling concerns and makes the watcher service independently testable. All UI components and the hosted service depend on `IPlayCricketWatcherService`.

**Dependencies:** S-001 (for snapshot types living in Core).

**Verification intent:** Unit tests covering: `EnableAutoWatch` / `DisableAutoWatch` toggle; `EnableAutoWatch` clears dismissed fixture ID; `DisableAutoWatch` delegates to `CancelCountdown()` (single event); `StartCountdown(fixtureId, duration)` sets state + fires; `bool? TickCountdown()` returns null when inactive, false when running, true on expiry; `TickCountdown` fires `AutoCloseT60Warning` when crossing below 60s (suppressed if ≤60s start); `TickCountdown` clears state on expiry but does NOT fire `AutoCloseFired`; `CancelCountdown` fires `AutoCloseCountdownCancelled`; `ClearAutoLoadSuppression` clears suppression set (same object); `RecordAutoLoadSuppressed` + `IsAutoLoadSuppressed` round-trip; `DismissAutoClose` + `IsAutoCloseDismissed` round-trip; `CurrentFixtureId` resets on `StateChanged` away from `MatchLoaded`; concurrent calls do not corrupt state or miss events; event snapshots carry expected data. Build and all existing tests pass.

---

### S-005 — Auto-load polling loop

**What changes:**

- `PlayCricketWatcherHostedService` (`IHostedService`) added to `PcsRemote.Web`. This step implements the auto-load polling half only; S-007 adds the auto-close half.
- Polling runs on `PlayCricketOptions.PollIntervalSeconds` interval (minimum 60 seconds enforced). The hosted service runs as a single, awaited loop — each iteration must complete before the next begins, preventing concurrent tick executions.
- On each tick, when `IPlayCricketWatcherService.IsAutoWatchEnabled` is `true`, `IManualModeService.IsManualModeActive` is `false`, and `IPcsProAutomationService.CurrentState` is in `{MatchSelection, MatchSelectionSearching, MatchSelectionReady}`:
  - Calls the appropriate method on `IPcsProAutomationService` to obtain today's converted match list. The method **must** include a UI refresh step (Clear Filters) before reading — this is a firm requirement. The JIT Spec confirms whether the existing `GetTodaysMatchesAsync()` satisfies this or whether a new dedicated method is required.
  - Applies duplicate-suppression: if the result contains exactly one match and `IPlayCricketWatcherService.IsAutoLoadSuppressed(matchId)` returns `false`, performs a final re-check (`IsAutoWatchEnabled` still true, `IsManualModeActive` still false) immediately before calling `LoadMatchAsync`. If re-check fails, no action is taken. On successful load, calls `IPlayCricketWatcherService.RecordAutoLoadSuppressed(matchId)` and fires `IPlayCricketWatcherService.AutoLoadTriggered`. Suppression is recorded only on a successful load.
  - The suppression set is owned by `PlayCricketWatcherService` (S-004). It is **not** cleared by state transitions — it persists for the process lifetime. The only mechanism to clear it is `IPlayCricketWatcherService.ClearAutoLoadSuppression()`, called exclusively by `MidnightResetHostedService` (S-008) as part of the midnight reset sequence (per SC-1). `PlayCricketWatcherHostedService` is stateless with respect to suppression — it only calls interface methods.
  - If result count ≠ 1, takes no action (0 = wait; 2+ = log ambiguity per SC-10).
- All exceptions from automation calls are caught, logged, and do not crash the hosted service.
- Registered in DI.

**Why:** This is the primary user-facing value: the scorer starts their match and the scoreboard follows. Implementing the auto-load loop in isolation (before auto-close) allows it to be independently tested and verified on match day before the more complex Play-Cricket API integration is required.

**Dependencies:** S-001, S-004. S-002/S-003 not required for this step (no API calls).

**Verification intent:** Unit tests covering: auto-load fires when exactly one match returned; final re-check before `LoadMatchAsync` aborts when disabled/manual mid-tick; auto-load suppressed on repeat `MatchId`; suppression NOT cleared on StateChanged→MatchSelection; suppression cleared when `ClearAutoLoadSuppression()` called; auto-load suppressed when disabled/manual/wrong state; 2+ matches logs ambiguity; `AutoLoadTriggered` fires after successful load; exception does not crash service. Build and all existing tests pass.

---

### S-006 — Post-load fixture ID resolution

**What changes:**

- `PlayCricketWatcherHostedService` subscribes to `IPcsProAutomationService.StateChanged`.
- On transition to `PcsProState.MatchLoaded`: cancels any in-flight identification task via `CancellationToken`, then starts a new async task that:
  - Queries `IPlayCricketApiClient` for each configured `SiteId` **in parallel** using the new `CancellationToken`, collecting results independently. Per-site HTTP or parsing errors are caught and logged; other sites proceed normally.
  - Client-side filters results to the loaded match's date (using `IPcsProAutomationService.LoadedMatch.MatchDate`) rather than today's date, ensuring fixture resolution works for manually loaded non-today matches. **Guard:** if `LoadedMatch.MatchDate` is `DateOnly.MinValue` (unset), skip resolution with a warning log — `CurrentFixtureId` remains null.
  - **Partial site failure policy:** Since `IPlayCricketApiClient` converts HTTP errors to an empty list (S-002), a failing site is indistinguishable from a site with genuinely no fixtures at the client level. This is an accepted limitation per SC-9 (API outage suppresses auto-close). In practice, each site covers distinct fixtures, so false-positive uniqueness from a partial failure is low risk. The JIT Spec will evaluate whether a discriminated result type (empty-due-to-error vs. empty-due-to-no-fixtures) is warranted. Until then, an empty list from any site is treated as "no fixtures at that site."
  - Applies team name normalisation (using the existing `TeamNameFormatter` as the primary candidate) to compare `PlayCricketFixture` team names against `IPcsProAutomationService.LoadedMatch` team names.
  - If exactly one fixture across all site queries matches: performs a final state check (`CurrentState` still `MatchLoaded`); if a different `CurrentFixtureId` is already set (concurrent resolution for a prior load), calls `CancelCountdown()` before overwriting; then sets `CurrentFixtureId`.
  - If zero or multiple matches: logs a warning and leaves `CurrentFixtureId` as `null` (auto-close polling is then skipped per SC-6).
- The `CancellationToken` is also cancelled on any `StateChanged` transition away from `MatchLoaded`.
- `CurrentFixtureId` is cleared automatically by `PlayCricketWatcherService` on any `StateChanged` away from `MatchLoaded` (S-004).

**Why:** The fixture ID is the bridge between the PCS Pro domain and the Play-Cricket API. Without it, auto-close cannot function. Implementing resolution separately from auto-close polling keeps each concern focused and independently testable.

**Dependencies:** S-002 (or S-003 for mock), S-004, S-005 (hosted service scaffold).

**Verification intent:** Unit tests covering: unique match found → `CurrentFixtureId` set; filters by loaded match's `MatchDate` (not today); `MatchDate == MinValue` → skip resolution + warning; cancel-before-assign when existing `CurrentFixtureId` differs; multi-site overlap handling; zero → null + warning; multiple → null + warning; exception → null + warning; partial site failure (one site errors, one returns unique match) → uniqueness accepted + warning logged; task cancelled via token on state exit; task cancelled via token on new `MatchLoaded`; final state check prevents stale write; team name normalisation edge cases. Build and all existing tests pass.

---

### S-007 — Auto-close polling + countdown

**What changes:**

- `PlayCricketWatcherHostedService` extended with the auto-close polling half:
  - On each API poll tick (same `PollIntervalSeconds` interval), when `IPlayCricketWatcherService.CurrentFixtureId` is not null, no countdown is already active, **`IPlayCricketWatcherService.IsAutoWatchEnabled` is `true`**, **`IManualModeService.IsManualModeActive` is `false`**, and **`IPcsProAutomationService.CurrentState` is `MatchLoaded`**:
    - Queries `IPlayCricketApiClient` for the fixture matching `CurrentFixtureId` (re-uses the multi-site query pattern from S-006, or a targeted single-fixture query if the API supports it — JIT Spec decides).
    - Performs a final state re-check (same conditions above) immediately before calling `StartCountdown`.
    - If status equals the confirmed "completed" status string (OQ-1 — resolved in JIT Spec) and re-check passes: calls `IPlayCricketWatcherService.StartCountdown(CurrentFixtureId, TimeSpan.FromSeconds(AutoCloseCountdownSeconds))`.
  - Active countdown: a second-resolution loop calls `IPlayCricketWatcherService.TickCountdown()` once per second. Return semantics: `null` = no countdown active (exit loop); `false` = running (continue); `true` = expired (execute expiry sequence). When expired (`true`): **immediately calls `IPlayCricketWatcherService.DismissAutoClose(countdownFixtureId)`** (preventing any retry regardless of subsequent outcome), then perform a final re-check (`IsAutoWatchEnabled`, `!IsManualModeActive`, `CurrentState == MatchLoaded`, `CurrentFixtureId == countdownFixtureId`). If re-check fails: log and abort. If all pass: calls `IPcsProAutomationService.StopStreamingAsync()` then `ChangeMatchAsync()` in sequence; on success fires `IPlayCricketWatcherService.AutoCloseFired` (the event is raised here, not inside `TickCountdown()`).
  - **Per-fixture dismissed flag:** Owned entirely by `PlayCricketWatcherService` (S-004). The hosted service calls `DismissAutoClose(fixtureId)` and `IsAutoCloseDismissed(fixtureId)` via the interface. The dismissed state is set: (a) via `DismissAutoClose()` called immediately when `TickCountdown()` returns `true` (before re-check); (b) via `DismissAutoClose()` called on any cancellation via cancel triggers (a–e). The API poll/start-countdown path calls `IsAutoCloseDismissed(CurrentFixtureId)` before proceeding, preventing countdown restart. Dismissed state is cleared by `EnableAutoWatch()` on the service (S-004).
  - **Expiry exception handling:** Exceptions from `StopStreamingAsync` or `ChangeMatchAsync` are caught and logged. A failure leaves the system in `MatchLoaded` state; the fixture is already dismissed, so the operator must intervene manually.
  - The countdown is cancelled by calling `PlayCricketWatcherService.CancelCountdown()` in any of the following situations: (a) `CancelCountdown()` called directly by the user via the UI; (b) `DisableAutoWatch()` called — `PlayCricketWatcherService` delegates to `CancelCountdown()` internally per S-004; (c) `IManualModeService.IsManualModeActive` becomes `true` — `PlayCricketWatcherHostedService` subscribes to `IManualModeService.ManualModeChanged` and immediately calls `CancelCountdown()` when the new value is `true`; (d) `IPcsProAutomationService.StateChanged` fires a transition away from `PcsProState.MatchLoaded` — hosted service detects and calls `CancelCountdown()`; (e) `CurrentFixtureId` changes while `MatchLoaded` — hosted service detects and calls `CancelCountdown()` before recording the new fixture. All cancellations call `DismissAutoClose(fixtureId)` via the interface.

**Why:** This is the second half of the hosted service and the mechanism that makes the scoreboard self-managing at match end. Implementing it after S-006 ensures the fixture ID resolution is solid before the close logic depends on it.

**Dependencies:** S-002 (or S-003), S-004, S-005, S-006.

**Verification intent:** Unit tests covering: completed status triggers countdown when all pre-conditions met; `IsAutoCloseDismissed` checked before `StartCountdown`; `StartCountdown` called with correct fixtureId and duration; start-countdown suppressed when disabled/manual/wrong state/dismissed; dismissed state cleared on re-enable (not on fixture change); final re-check before `StartCountdown` prevents race; second-resolution loop: null exits, false continues, true triggers expiry; `DismissAutoClose` called immediately on `TickCountdown()=true` before re-check; expiry re-check validates fixtureId match; expiry re-check aborts on failed conditions; expiry calls `StopStreamingAsync` then `ChangeMatchAsync` then fires `AutoCloseFired` on success; `AutoCloseFired` NOT fired on expiry re-check failure; expiry exception caught + logged (no `AutoCloseFired`); cancel trigger (c) fires immediately on `ManualModeChanged=true` (not delayed to next poll); all cancel triggers (a–e) call `DismissAutoClose`; API exception does not crash service. Build and all existing tests pass.

---

### S-008 — Midnight reset hosted service

**What changes:**

- `MidnightResetHostedService` (`IHostedService`) added to `PcsRemote.Web`.
- Calculates the time until the next 00:00:00 local time and uses `Task.Delay` to wait; wakes at midnight and re-arms for the following midnight.
- On wake: if `IPcsProAutomationService.CurrentState == PcsProState.MatchLoaded` and `IManualModeService.IsManualModeActive == false`, calls `ChangeMatchAsync()`. All other states are a no-op for `ChangeMatchAsync`.
- After `ChangeMatchAsync` (or if state was not `MatchLoaded`): always calls `IPlayCricketWatcherService.ClearAutoLoadSuppression()` as part of the midnight reset sequence regardless of automation state (per SC-1 and SC-8 — suppression resets at midnight unconditionally).
- Exceptions from `ChangeMatchAsync` are caught and logged; the service continues to the next midnight cycle. `ClearAutoLoadSuppression()` is still called even if `ChangeMatchAsync` throws.
- Registered in DI.

**Why:** An independent safety net that is completely decoupled from auto-watch state. This is a well-scoped, self-contained step that can be delivered and tested independently of the polling logic.

**Dependencies:** None beyond the existing `IPcsProAutomationService` and `IManualModeService` — no Play-Cricket dependencies.

**Verification intent:** Unit tests covering: fires `ChangeMatchAsync` at midnight when `MatchLoaded` and manual mode off; no `ChangeMatchAsync` when state ≠ `MatchLoaded`; no `ChangeMatchAsync` when manual mode active; `ClearAutoLoadSuppression()` called in all wake paths (MatchLoaded, non-MatchLoaded, manual active); `ClearAutoLoadSuppression()` called even when `ChangeMatchAsync` throws; exception during `ChangeMatchAsync` is caught and logged; service re-arms for the next midnight after firing. Build and all existing tests pass.

---

### S-009 — Web UI — auto-watch toggle + countdown banner

**What changes:**

- **Debug panel toggle**: Auto-watch enabled/disabled toggle added to the debug panel, following the date override toggle pattern from HLPS-019. Bound to `IPlayCricketWatcherService.EnableAutoWatch()` / `DisableAutoWatch()`. Renders the current state of `IPlayCricketWatcherService.IsAutoWatchEnabled`. Since the service never persists its enabled state (SC-7, C-4), this will be `false` on every fresh application start; the toggle correctly reflects live service state within a running session.
- **Countdown banner**: A banner component appears in the main web UI when `IPlayCricketWatcherService.AutoCloseRemainingTime` is not null. Displays remaining time, updating in real time using a component-level `PeriodicTimer` at a 1-second interval that reads `AutoCloseRemainingTime` and calls `StateHasChanged()` — no additional service event is required for tick-by-tick updates. The banner subscribes to `AutoCloseCountdownStarted` to begin showing and to `AutoCloseCountdownCancelled` / `AutoCloseFired` to dismiss. Includes a "Cancel" button that calls `IPlayCricketWatcherService.CancelCountdown()`.

**Why:** The UI changes are isolated from the service logic and can be developed and tested independently once the service interfaces are stable. Keeping UI as a late step avoids UI churn while the service contracts settle.

**Dependencies:** S-004 (for `IPlayCricketWatcherService` interface).

**Verification intent:** bUnit tests covering: toggle renders `false` on mount (service starts disabled per SC-7); toggle calls `EnableAutoWatch`/`DisableAutoWatch`; toggle reflects live `IsAutoWatchEnabled` state on re-render; countdown banner hidden when `AutoCloseRemainingTime` is null; countdown banner visible with correct time display when active; banner updates on `PeriodicTimer` tick (reads `AutoCloseRemainingTime` + `StateHasChanged`); banner disappears on `AutoCloseCountdownCancelled` or `AutoCloseFired` events; cancel button calls `CancelCountdown`. Build and all existing tests pass.

---

### S-010 — Browser push notifications

**What changes:**

- JavaScript interop module added to `PcsRemote.Web` to manage the browser `Notification` API:
  - On first `EnableAutoWatch()` call: requests `Notification` permission via `Notification.requestPermission()`. Gracefully handles denied permission — no crash, no error state.
  - On `AutoCloseCountdownStarted` event: fires a browser push notification via JS interop with a descriptive message.
  - On `AutoCloseT60Warning` event from `IPlayCricketWatcherService`: fires a second push notification (suppressed if countdown started at ≤ 60 seconds, per C-3a — note: `PlayCricketWatcherService` handles suppression internally via `TickCountdown`; this event simply will not fire in that case).
- If the browser has denied notification permission, the JS calls are no-ops. The web UI countdown banner (S-009) remains the sole feedback channel in that case.
- The JS module is lazy-loaded; it does not affect page load when auto-watch has never been enabled.

**Why:** Push notifications are the last isolated concern and the highest-risk step (JS interop, browser permission model). Delivering them last means all other functionality is operational before this optional enhancement is layered on.

**Dependencies:** S-004 (events), S-009 (UI integration point).

**Verification intent:** bUnit / JS interop tests covering: permission request fires on first `EnableAutoWatch`; notification fires on `AutoCloseCountdownStarted`; T-60 notification fires on `AutoCloseT60Warning` event; T-60 notification not fired when countdown started at ≤ 60 seconds (event not raised by service); no exception when permission denied; no notification fires when permission not yet granted. Build and all existing tests pass.

---

## Step Dependency Summary

```
S-001 (scaffold + contracts)
  └── S-002 (real API client)
  └── S-003 (mock API client)
  └── S-004 (watcher service state)
        └── S-005 (auto-load polling)
              └── S-006 (fixture ID resolution) ← also needs S-002/S-003
                    └── S-007 (auto-close + countdown)
S-008 (midnight reset) — independent, no Play-Cricket dependency
S-009 (UI) ← depends on S-004
  └── S-010 (push notifications) ← also depends on S-004 (events)
```

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| — | — | — | — | — | DRAFT | Awaiting review |
| R1 | 3 | claude-opus-4.5, gpt-5.4 | 12 findings (4×HIGH, 3×MEDIUM, 5×LOW) | All 12 accepted | REVISION REQUIRED | Countdown timer decoupled; suppression lifecycle defined; stale ID cancellation added; auto-cancel triggers added; non-reentrancy specified; refresh constraint firmed up; toggle binding corrected; parallel queries committed; service location committed |
| R2 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 3 findings (1×MEDIUM, 2×LOW, 1×INFO). GPT: 2×HIGH | All accepted except GPT-F2(b) rejected (midnight reset is intentionally independent of IsAutoWatchEnabled per SC-8) | REVISION REQUIRED (GPT) / APPROVED (Opus) | StartCountdown() added to interface; responsibility boundary documented; suppression set ownership specified; resolver cancellation on state exit; DisableAutoWatch() hard-off cancels countdown |
| R3 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 2×LOW. GPT: 1×HIGH, 1×MEDIUM | All 4 accepted | REVISION REQUIRED (GPT) / APPROVED (Opus) | TickCountdown() + AutoCloseT60Warning added to interface; start-countdown gated on IsAutoWatchEnabled + !manual + MatchLoaded; T-60 suppression in S-010 clarified |
| R4 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 2×MEDIUM, 2×LOW. GPT: 2×HIGH, 1×MEDIUM | All 6 accepted (Opus F4 + GPT F3 same finding) | REVISION REQUIRED | TickCountdown() returns false when inactive; countdown loop exit on null; expiry exception handling; thread safety lock spec; per-fixture dismissed flag prevents restart after cancel; final re-check before expiry actions |
| R5 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 1×MEDIUM, 2×LOW. GPT: 2×HIGH, 1×MEDIUM | All 6 accepted | REVISION REQUIRED | bool? TickCountdown() (null=inactive, false=running, true=expired); StartCountdown is fixture-scoped (fixtureId param); expiry re-check validates fixtureId; CurrentFixtureId change as explicit cancel trigger (e); pre-LoadMatchAsync re-check; CancellationToken for S-006; dismiss-on-retoggle noted intentional |
| R6 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 2×LOW, 1×INFO. GPT: 2×HIGH, 1×MEDIUM | All 6 accepted | REVISION REQUIRED (GPT) / APPROVED (Opus) | ClearAutoLoadSuppression() added to interface; suppression cleared only by S-008 (not StateChanged); DisableAutoWatch() delegates to CancelCountdown(); TickCountdown() does NOT fire AutoCloseFired; AutoCloseFired fired in S-007 after successful expiry; _dismissedFixtureId set immediately on TickCountdown()=true; S-006 filters by loaded match MatchDate (not today); S-006 cancel-before-assign on stale CurrentFixtureId |
| R7 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 1×MEDIUM, 2×LOW. GPT: 2×HIGH, 2×MEDIUM (1 rejected) | Accepted: Opus-F1+GPT-F1(HIGH, same finding), GPT-F2(MEDIUM→partial failure policy), GPT-F3(LOW→MatchDate guard), GPT-F4(MEDIUM→banner update mechanism), Opus-F2(LOW→ManualModeChanged sub). Rejected: GPT-F5 (SC-5 not in HLPS; single-user system) | REVISION REQUIRED | Suppression+dismissed ownership moved to PlayCricketWatcherService; 4 interface methods added (RecordAutoLoadSuppressed/IsAutoLoadSuppressed/DismissAutoClose/IsAutoCloseDismissed); hosted service is fully stateless; MatchDate==MinValue guard in S-006; partial failure policy documented; ManualModeChanged subscription explicit in S-007; banner uses PeriodicTimer for real-time updates |
