# SPEC-S-006: Match Selection Flow — Cards, Loading State, Auto-Select, Interactivity Gating

| Field | Value |
|---|---|
| **Document** | SPEC-S-006-MatchSelectionFlow.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-11 |
| **Step ID** | S-006 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Success Criteria** | W-SC-4, W-SC-5, W-SC-6, W-SC-8 |
| **Dependencies** | S-002 (app shell + routing), S-003 (state-change notification mechanism) |

---

## 1. Objective

Implement the match selection workflow as the home page (`/`) of the control panel. The page shows different content based on the current PCS Pro lifecycle state: a loading indicator while matches are being fetched or the service is searching, selectable match cards when results are ready, an empty-state message when no matches are found, and nothing match-specific in all other states. A `MatchCard` Razor component handles the rendering and interaction of a single fixture. When exactly one match is returned, it is selected automatically without user interaction.

---

## 2. Background

`IPcsProAutomationService` exposes three members relevant to this step:

- `StateChanged` (`EventHandler<PcsProState>`) — fires on every lifecycle state transition; the page subscribes to this to react to state changes in real time.
- `GetTodaysMatchesAsync()` — returns the list of today's available fixtures as `Task<IReadOnlyList<MatchInfo>>`. In the real FlaUI implementation this call drives the state machine through `MatchSelectionSearching` and ultimately to `MatchSelectionReady`; in the mock service it returns immediately without advancing state (accepted per AR-3).
- `LoadMatchAsync(MatchInfo)` — selects and loads the given fixture. The real FlaUI service drives state through `MatchSelectionSearching` → `MatchSelectionReady` → `MatchLoaded`. Because `LoadMatchAsync` causes the service to re-enter `MatchSelectionReady` as an intermediate state before reaching `MatchLoaded`, the page must clear its cached match list before calling `LoadMatchAsync` to prevent auto-select from firing again on the intermediate `MatchSelectionReady` event (see R-8).

**State transitions guaranteed by the state machine:** `MatchSelection` is always entered before `MatchSelectionSearching`, and `MatchSelectionSearching` is always entered before `MatchSelectionReady`. The page may rely on this ordering.

`MatchInfo` carries the following display fields:
- `HomeTeam` (string), `AwayTeam` (string), `MatchType` (string), `MatchDate` (DateOnly).

`MatchDate` must be formatted as `d MMM yyyy` using `CultureInfo.InvariantCulture` (e.g., `15 Jan 2026`). English month abbreviations (Jan, Feb, …) are always used regardless of the server's runtime locale. This format is used in both the component implementation and all text-content test assertions.

The existing `Index.razor` home page is replaced by the match selection page in this step. The route remains `/`.

---

## 3. Component Architecture

### 3.1 `MatchCard` component (`Shared/`)
A simple, reusable presentation component that receives a single `MatchInfo`, an interactivity flag, and an `OnSelected` event callback. It renders the fixture details and handles click events when interactive. It has no knowledge of global state.

The root element of `MatchCard` is a `<div>` element. It always carries the CSS class `match-card`. When non-interactive it additionally carries `match-card--disabled`. Click handling must be conditional in Blazor component code — the `@onclick` handler must be present only when the component is interactive. Relying on CSS (`pointer-events: none` or similar) alone is not acceptable because bUnit dispatches DOM click events regardless of CSS, making click-suppression tests incorrect.

### 3.2 Match selection page (`Pages/Index.razor`)
The home page. It subscribes to `StateChanged`, tracks the current state and the fetched match list in private fields, and coordinates the three behaviours: fetching, rendering, and selecting. It has no direct knowledge of the SignalR hub or connection tracker.

**Page render mode table:**

| Condition | Match-content area renders |
|---|---|
| `_fetchInProgress == true` OR state is `MatchSelectionSearching` | Loading indicator (`match-loading` class) |
| State is `MatchSelectionReady` AND `_matches` is empty list | Empty state message (`match-empty-state` class) |
| State is `MatchSelectionReady` AND `_matches` has one or more items | One `MatchCard` per match (interactive) |
| All other states | Nothing (blank match-content area) |

Cards are rendered **only** when state is `MatchSelectionReady`. They are never rendered as non-interactive — if the state is not `MatchSelectionReady`, no cards are shown at all.

---

## 4. Requirements

### R-1 — Match selection page location and route
The existing `Index.razor` is updated in-place. Its route remains `@page "/"`. The match-content area is empty when the service is outside the match-selection lifecycle states (`NotRunning`, `Launching`, `LoginScreen`, `MatchLoaded`, `Error`).

### R-2 — Dependency injection
The page receives `IPcsProAutomationService` via DI injection. It does not accept the service as a Razor parameter.

### R-3 — Subscribe-before-read initialisation
On `OnInitializedAsync`, the page subscribes to `StateChanged` before reading `CurrentState`. After subscribing, it reads `CurrentState` and acts as follows:
- If state is `MatchSelection`: immediately trigger a match fetch (same as responding to a `StateChanged` event for `MatchSelection`).
- If state is `MatchSelectionReady`: immediately trigger a match fetch. This handles browser refresh mid-flow where the service is already ready but the page has no cached match list.
- If state is `MatchSelectionSearching`: set internal state to `MatchSelectionSearching` and render the loading indicator; wait for further `StateChanged` events.
- Any other state: record the state and render accordingly (blank match-content area).

### R-4 — Match fetch trigger and stale-result clearing
The page calls `GetTodaysMatchesAsync` in two circumstances only:
- When a `StateChanged` event delivers `MatchSelection` (and `_fetchInProgress == false`).
- When R-3 mount initialisation directs it to, for an initial state of `MatchSelection` or `MatchSelectionReady`.

A `StateChanged` event delivering `MatchSelectionReady` does **not** trigger a fetch. `MatchSelectionReady` via `StateChanged` is the signal to evaluate the auto-select condition (R-8) or render cards — not to re-fetch.

Before beginning any fetch, the page sets `_matches` to `null` so that no stale cards are rendered during the new fetch cycle. The page does not start a new fetch while one is already in progress (`_fetchInProgress == true`).

### R-5 — Loading state display
The page renders a loading indicator element while `_fetchInProgress == true` OR the current state is `MatchSelectionSearching`. The loading indicator must carry the CSS class `match-loading`. No `MatchCard` components are rendered while loading.

### R-6 — Match card rendering
`MatchCard` components are rendered only when state is `MatchSelectionReady` AND `_matches` is non-null and non-empty. Each card displays: home team name, away team name, match type, and match date formatted as `d MMM yyyy`. Cards rendered in this state are always interactive.

### R-7 — Empty state
If `GetTodaysMatchesAsync` returns an empty list and the current state is `MatchSelectionReady`, the page renders an empty-state element with CSS class `match-empty-state` in place of the cards. No `MatchCard` components are rendered.

### R-8 — Auto-select with single match
When the auto-select condition is met — `_matches` contains exactly one item AND state is `MatchSelectionReady` — the page calls `LoadMatchAsync` for that match automatically.

The condition is evaluated at two points:
- **(a) On state change:** when `StateChanged` fires `MatchSelectionReady` and `_matches` already contains exactly one item (fetch completed before state reached ready).
- **(b) On fetch completion:** when `GetTodaysMatchesAsync` returns exactly one item and the current state is already `MatchSelectionReady` (state reached ready before or at the same time as fetch completion).

Before invoking `LoadMatchAsync`, the page **must** set `_matches = null`. This prevents the intermediate `MatchSelectionReady` event emitted during `LoadMatchAsync` execution from satisfying the auto-select condition again, which would create an infinite call loop.

### R-9 — Card selection
When the user clicks a match card, the page calls `LoadMatchAsync` with the selected `MatchInfo`. Before calling `LoadMatchAsync`, the page sets `_matches = null` (same guard as auto-select, ensuring no cards flash during the intermediate `MatchSelectionReady` event).

### R-10 — `MatchCard` component interface
`MatchCard` renders a single `MatchInfo` as a `<div class="match-card">` element. Parameters:
- `MatchInfo Match` — the fixture to render (required).
- `bool IsInteractive` — controls whether click events are dispatched.
- `EventCallback<MatchInfo> OnSelected` — invoked with the rendered `Match` when the card is clicked and `IsInteractive` is `true`.

Click suppression must be implemented by conditionally attaching the Blazor `@onclick` handler (e.g., `@onclick="@(IsInteractive ? () => OnSelected.InvokeAsync(Match) : null)"` or equivalent), NOT by CSS-only means. This ensures bUnit click dispatch honours the interactivity state.

`MatchInfo` equality for test assertions is by value equality (all properties equal), as `MatchInfo` is a record type.

### R-11 — Disposal
The page implements `IDisposable` and unsubscribes from `StateChanged` in `Dispose`.

---

## 5. Acceptance Criteria

### MatchCard Component

#### AC-1 — Data rendering
When a `MatchCard` is rendered with a `MatchInfo` where `HomeTeam = "High Halstow"`, `AwayTeam = "Visitors CC"`, `MatchType = "League"`, and `MatchDate = new DateOnly(2026, 6, 20)`, the rendered HTML contains the strings `"High Halstow"`, `"Visitors CC"`, `"League"`, and `"20 Jun 2026"` (date formatted as `d MMM yyyy` with `CultureInfo.InvariantCulture`).

#### AC-2 — Interactive CSS class
When a `MatchCard` is rendered with `IsInteractive = true`, the root `<div>` has CSS class `match-card` and does NOT have CSS class `match-card--disabled`.

#### AC-3 — Non-interactive CSS class
When a `MatchCard` is rendered with `IsInteractive = false`, the root `<div>` has both CSS classes `match-card` and `match-card--disabled`.

#### AC-4 — Click raises callback when interactive
When a `MatchCard` is rendered with `IsInteractive = true` and the root element is clicked, the `OnSelected` callback is invoked with a `MatchInfo` value-equal to the rendered `Match` parameter.

#### AC-5 — Click does not raise callback when non-interactive
When a `MatchCard` is rendered with `IsInteractive = false` and the root element is clicked, the `OnSelected` callback is NOT invoked.

---

### Match Selection Page

#### AC-6 — Loading indicator shown while fetch is in progress
When the page mounts in `MatchSelection` state and `GetTodaysMatchesAsync` is configured to return a never-completing `Task` (via `new TaskCompletionSource<IReadOnlyList<MatchInfo>>().Task`), the page renders an element with CSS class `match-loading` and renders no `MatchCard` components.

#### AC-7 — Loading indicator shown in MatchSelectionSearching state
When the current state transitions to `MatchSelectionSearching` (with no fetch in progress), the page renders an element with CSS class `match-loading` and no `MatchCard` components.

#### AC-8 — Cards shown and interactive in MatchSelectionReady (multiple matches)
When `GetTodaysMatchesAsync` returns two or more matches and the current state is `MatchSelectionReady`, the page renders one `MatchCard` per match. Each card has CSS class `match-card` and does NOT have `match-card--disabled`.

#### AC-9 — Empty state in MatchSelectionReady with zero matches
When `GetTodaysMatchesAsync` returns an empty list and the current state is `MatchSelectionReady`, the page renders an element with CSS class `match-empty-state` and renders no `MatchCard` components.

#### AC-10 — Auto-select (a): state reaches ready after fetch
When the page mounts in `MatchSelection` state, `GetTodaysMatchesAsync` returns exactly one match, and the `StateChanged` event then fires `MatchSelectionReady`, `LoadMatchAsync` is called automatically with the single match. No user interaction is required.

#### AC-11 — Auto-select (b): fetch completes when state already ready
When the page mounts in `MatchSelectionReady` state (simulating a browser refresh) and `GetTodaysMatchesAsync` returns exactly one match, `LoadMatchAsync` is called automatically with the single match. No user interaction is required.

#### AC-12 — Auto-select fires exactly once despite MatchSelectionReady re-entry
Using the AC-10 scenario as a precondition (mount in `MatchSelection`, single match returned, first `MatchSelectionReady` fires and triggers auto-select), raise a second `StateChanged` event for `MatchSelectionReady` via `mock.Raise(s => s.StateChanged += null, this, PcsProState.MatchSelectionReady)`. Verify that `LoadMatchAsync` was called exactly once in total (`mock.Verify(s => s.LoadMatchAsync(It.IsAny<MatchInfo>()), Times.Once)`).

#### AC-13 — Card click calls LoadMatchAsync with correct match
When the page is in `MatchSelectionReady` state with two or more matches and the user clicks the card for a specific match, `LoadMatchAsync` is called with a `MatchInfo` value-equal to that match.

#### AC-14 — GetTodaysMatchesAsync called on MatchSelection re-entry; stale results cleared
When the `StateChanged` event fires `MatchSelection` a second time (after a previous fetch cycle), `GetTodaysMatchesAsync` is called again. At the point the second fetch begins, the page renders the loading indicator (not the stale match cards from the previous cycle).

*Test setup note: configure the second `GetTodaysMatchesAsync` call to return a never-completing `Task` (`new TaskCompletionSource<IReadOnlyList<MatchInfo>>().Task`) so that the loading-indicator state is observable at assertion time — mirroring the pattern in AC-6.*

#### AC-15 — Mount in MatchSelectionSearching state renders loading indicator
When the page mounts with `CurrentState == MatchSelectionSearching`, it renders an element with CSS class `match-loading` and no `MatchCard` components (loading via the R-3 mount branch, not via a `StateChanged` event).

#### AC-16 — Disposal unsubscribes StateChanged
After `cut.Instance.Dispose()` is called (which invokes `IDisposable.Dispose()` synchronously), a subsequent `StateChanged` event does not invoke the page's handler. Verified with `mock.VerifyRemove(s => s.StateChanged -= It.IsAny<EventHandler<PcsProState>>(), Times.Once)`.

*Note: use `cut.Instance.Dispose()` (not `cut.Dispose()`). `cut.Instance.Dispose()` fires `IDisposable.Dispose()` synchronously, so `VerifyRemove` assertions complete before the bUnit context teardown. `cut.Dispose()` routes through Blazor's renderer pipeline and defers teardown asynchronously, causing the `VerifyRemove` to see zero calls.*

---

## 6. Out of Scope

- CSS visual design and Radzen card component styling — the `match-card`, `match-card--disabled`, `match-loading`, and `match-empty-state` classes satisfy testability; visual polish is out of scope.
- `MatchDate` time-of-day component — `MatchInfo.MatchDate` is `DateOnly`; no time is available and none is shown.
- Error handling for `GetTodaysMatchesAsync` or `LoadMatchAsync` failures — exception handling is HLPS-005 scope.
- `MatchLoaded` state content (team names, scoreboard controls) — S-007 and S-009 scope.
- Cancellation token threading — accepted as per AR-1.
- Optimistic UI disabling after card click (cards staying interactive until `StateChanged` fires) — accepted as per AR-4.
- Post-click intermediate `MatchSelectionReady` flash for N>1 match scenario — `_matches` is cleared in `R-9` before `LoadMatchAsync` is called, so no stale cards render during the intermediate state. No AC required as this is a side-effect of R-9's guard mechanism.

---

## 7. Branch Convention

`feature/hlps003-S006-match-selection-flow`

---

## 8. Accepted Risks

| ID | Description | Rationale |
|---|---|---|
| AR-1 | `StateChanged` fires from automation-service threads; `InvokeAsync` handles the synchronisation-context marshal, but unobserved exceptions from the `async void` handler and from fire-and-forget calls to `GetTodaysMatchesAsync` or `LoadMatchAsync` are not caught. Hardening is HLPS-005 scope. | Consistent with AR-1 in SPEC-S-004/S-005 and AR-2 in SPEC-S-003. |
| AR-2 | Re-fetch guard (`R-4`: "does not start a new fetch while one is in progress") is not race-condition-safe under concurrent thread access. The single-threaded Blazor Server rendering context makes a racing double-fetch extremely unlikely. A robust lock is HLPS-005 scope. | Acceptable for Phase 1 on a low-concurrency local network application. |
| AR-3 | The mock service's `GetTodaysMatchesAsync` does not advance state through `MatchSelectionSearching` → `MatchSelectionReady` (it returns immediately without state change). The bUnit tests use `Mock<IPcsProAutomationService>` and set state explicitly, so they are unaffected. | Known mock simplification; FlaUI integration is HLPS-006 scope. |
| AR-4 | After the user clicks a match card, cards remain interactive until the next `StateChanged` event fires (no optimistic UI disabling). Given the low latency of the local network and the automation service's immediate state transition, this is imperceptible in practice. | Phase 1 simplicity; UX hardening is post-MVP. |
| AR-5 | When the page mounts in `MatchSelectionSearching` state (browser refresh mid-search), it shows the loading indicator and waits for the next `StateChanged` event. If the service never emits another event (e.g., due to an unhandled exception in the automation layer), the UI will remain in a loading state indefinitely. No timeout or recovery mechanism is provided. | Low probability on a local network; error-recovery is HLPS-005 scope. |

---

## 9. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | GPT-5.2, Claude Sonnet 4.6 | NEEDS REVIEW — both reviewers. 4 HIGH, 5 MEDIUM, 2 LOW (GPT); 3 HIGH, 6 MEDIUM, 4 LOW (Sonnet). v0.2 addresses all HIGH and MEDIUM findings; LOW findings addressed or accepted. |
| R2 | 2026-04-11 | GPT-5.2, Claude Sonnet 4.6 | NEEDS REVIEW — both reviewers. 1 new HIGH (NF-1): R-4 `MatchSelectionReady` StateChanged fetch trigger re-created F-2 loop and broke AC-10. 1 MEDIUM (NF-2): AC-14 missing TaskCompletionSource pattern. 2 LOW (NF-3, NF-4): MatchSelectionSearching mount AC missing; AC-12 setup underspecified. v0.3 addresses all findings. |
| R3 | 2026-04-11 | GPT-5.2, Claude Sonnet 4.6 | **APPROVED** — unanimous. All R2 findings resolved. GPT LOW: InvariantCulture clarification (applied). Sonnet LOWs: AC-13/AC-14 setup notes (non-blocking; accepted). |
