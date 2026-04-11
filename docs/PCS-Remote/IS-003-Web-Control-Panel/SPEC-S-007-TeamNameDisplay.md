# SPEC-S-007: Team Name Display After Match Load

| Field | Value |
|---|---|
| **Document** | SPEC-S-007-TeamNameDisplay.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-12 |
| **Step ID** | S-007 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Success Criteria** | W-SC-7 |
| **Dependencies** | S-006 (match selection flow, `_matches` lifecycle, `SelectMatchAsync`, auto-select paths) |

---

## 1. Objective

After a match is loaded (`MatchLoaded` state), display the home and away team names on the home page. The team names come from the `MatchInfo` object used in the load call — no additional service call is required in Phase 1. The page already drives state via `StateChanged`; this step adds a stored `_loadedMatch` field and a new render branch.

---

## 2. Background

`IPcsProAutomationService` exposes `GetTeamNamesAsync()` for reading team names from the live PCS Pro window (PRD §9). In the real FlaUI implementation this is called internally by `LoadMatchAsync` before transitioning to `MatchLoaded`. In Phase 1, the mock service does not implement this call, and the `MatchInfo` returned by `GetTodaysMatchesAsync()` already carries accurate `HomeTeam` and `AwayTeam` display strings. Storing the `MatchInfo` passed to `LoadMatchAsync` is therefore the correct Phase 1 approach for obtaining team names — it avoids an unnecessary mock setup and faithfully represents the data the UI will use.

**State context:** After `LoadMatchAsync` completes (whether via user card click or auto-select), the service transitions to `MatchLoaded`. The existing `OnStateChanged` handler already calls `StateHasChanged()` for any state not explicitly handled — including `MatchLoaded` — which means the `MatchLoaded` render branch will be shown automatically once `_loadedMatch` is populated.

---

## 3. Design

### 3.1 New field

Add one field to `Index.razor`:

```csharp
private MatchInfo? _loadedMatch;
```

### 3.2 Set `_loadedMatch` at all three load call sites

`LoadMatchAsync` is called from three places. In all three, `_loadedMatch` must be set **before `await AutomationService.LoadMatchAsync(match)`** — the await is the yield point where `StateChanged(MatchLoaded)` can arrive. By convention `_loadedMatch = match` is written before `_matches = null`; see R-1 for the normative constraint and the reason the intra-line ordering is immaterial.

| Call site | Location | Change required |
|---|---|---|
| User card click | `SelectMatchAsync(match)` | `_loadedMatch = match;` before `await LoadMatchAsync` |
| Auto-select path (a) | `OnStateChanged` / `MatchSelectionReady` branch | `_loadedMatch = match;` before `await LoadMatchAsync` |
| Auto-select path (b) | `FetchMatchesAsync` / post-result branch | `_loadedMatch = match;` before `await LoadMatchAsync` |

### 3.3 Render branch

Add an `else if` clause to the existing state-switched render block **after** the `MatchSelectionReady` branch:

```razor
else if (_currentState == PcsProState.MatchLoaded && _loadedMatch is not null)
{
    <div class="match-loaded">
        <span class="match-loaded__home">@_loadedMatch.HomeTeam</span>
        <span class="match-loaded__away">@_loadedMatch.AwayTeam</span>
    </div>
}
```

**CSS classes used:**

| Class | Element | Purpose |
|---|---|---|
| `match-loaded` | `div` (root) | Wrapper for the loaded-match display |
| `match-loaded__home` | `span` | Home team name text |
| `match-loaded__away` | `span` | Away team name text |

No other styling decisions are made in this spec; visual presentation is out of Phase 1 scope.

### 3.4 Complete updated render template

After the change, the top-level conditional structure is:

```
@if (_fetchInProgress || MatchSelectionSearching)     → loading indicator
else if (MatchSelectionReady)
    @if (_matches empty)                              → empty state
    else if (_matches non-null)                       → match cards
else if (MatchLoaded && _loadedMatch non-null)        → team names
else if (MatchLoading)                                → blank (load in progress; no indicator in Phase 1)
(else: blank — NotRunning, Launching, Running, etc.)
```

> **Important:** The two `MatchSelectionReady` sub-branches remain **nested inside** the outer `else if (_currentState == PcsProState.MatchSelectionReady)` block — they are NOT promoted to top-level `else if` branches. The flat representation above is pseudocode only.

No changes are needed to `MatchCard.razor`.

---

## 4. Requirements

| ID | Requirement |
|---|---|
| **R-1** | `_loadedMatch` MUST be assigned **before** `await AutomationService.LoadMatchAsync(match)` at all three call sites. The `await` is the yield point at which `StateChanged(MatchLoaded)` can arrive on the Blazor sync-context and trigger a render; if `_loadedMatch` is not yet set at that point, the `_loadedMatch is not null` guard in the render branch silently fails and team names are never shown. The relative order between `_loadedMatch = match` and `_matches = null` is immaterial (Blazor cannot render between two consecutive synchronous assignments), but by convention `_loadedMatch = match` is written first. |
| **R-2** | `_loadedMatch` is populated from the `MatchInfo` passed to `LoadMatchAsync`. `GetTeamNamesAsync()` is NOT called in Phase 1. |
| **R-3** | The `match-loaded` div MUST NOT render in any state other than `MatchLoaded` (regardless of `_loadedMatch` value). |

---

## 5. Acceptance Criteria

### AC-1 — Team names render in `MatchLoaded` state

Given: State is `MatchLoaded` and `_loadedMatch` was set to a match with `HomeTeam = "Home XI"` and `AwayTeam = "Away XI"`.  
When: Component renders.  
Then: A `div.match-loaded` is present; it contains a `span.match-loaded__home` with text `"Home XI"` and a `span.match-loaded__away` with text `"Away XI"`. The mock's `GetTeamNamesAsync()` method was never called (verify call count = 0 — R-2).

### AC-2 — Team names absent when not in `MatchLoaded` state

Given: State is `MatchSelectionReady` (or any state other than `MatchLoaded`).  
When: Component renders.  
Then: No element with CSS class `match-loaded` is present in the DOM.

> **Test note:** A `[DataRow]`-driven variant covering `MatchLoading`, `MatchSelection`, and `Error` states is strongly encouraged to prevent regressions on state-boundary rendering.

### AC-3 — Card click stores match before loading

Given: State is `MatchSelectionReady` with one card rendered (HomeTeam = `"Alpha CC"`, AwayTeam = `"Beta CC"`).  
When: The user clicks the card, then `StateChanged(MatchLoaded)` is raised.  
Then: `div.match-loaded` is present and shows `"Alpha CC"` (home) and `"Beta CC"` (away).

### AC-4 — Auto-select stores match before loading (path a — state arrives after fetch)

Given: State arrives as `MatchSelection`. Mock `GetTodaysMatchesAsync` returns one match via `ReturnsAsync` (synchronous task completion — `_matches` is populated before any subsequent state change fires).  
When: `StateChanged(MatchSelectionReady)` is raised after the fetch has already completed (so `_matches` is populated when the `OnStateChanged` handler evaluates `_matches?.Count == 1`), then `StateChanged(MatchLoaded)` is raised.  
Then: `div.match-loaded` is present and shows the correct home and away team names without any user interaction.

> **Timing note:** Using `ReturnsAsync`/`Task.FromResult` ensures the fetch completes synchronously at the await-point, pinning this test to path (a). Path (b) is covered separately in AC-5.

### AC-5 — Auto-select stores match before loading (path b — fetch completes while state is already `MatchSelectionReady`)

Given: Component is mounted with initial state `MatchSelectionReady`. Mock `GetTodaysMatchesAsync` is set up to return one match (HomeTeam = `"FetchPath Home"`, AwayTeam = `"FetchPath Away"`).  
When: `OnInitializedAsync` fires (which calls `FetchMatchesAsync` because initial state is `MatchSelectionReady`), the fetch completes with `_currentState == MatchSelectionReady` (triggering the path-b auto-select branch in `FetchMatchesAsync`), then `StateChanged(MatchLoaded)` is raised.  
Then: `div.match-loaded` is present and shows `"FetchPath Home"` (home) and `"FetchPath Away"` (away).

---

## 6. Tests

All tests live in `tests/PcsRemote.Web.Tests/Pages/IndexTests.cs`, added to the existing class. No new test file is required.

Use the existing `BuildMock(PcsProState)` and `TestMatch(int id)` helpers.

**Test list (5 tests):**

| Test name | AC |
|---|---|
| `MatchLoaded_WithLoadedMatch_RendersTeamNames` | AC-1 |
| `NonMatchLoadedState_DoesNotRenderMatchLoaded` | AC-2 |
| `CardClick_ThenMatchLoaded_RendersTeamNames` | AC-3 |
| `AutoSelectPathA_ThenMatchLoaded_RendersTeamNames` | AC-4 |
| `AutoSelectPathB_ThenMatchLoaded_RendersTeamNames` | AC-5 |

---

## 7. Files Changed

| File | Change |
|---|---|
| `src/PcsRemote.Web/Pages/Index.razor` | Add `_loadedMatch` field; add `_loadedMatch = match;` at three call sites; add `else if (MatchLoaded)` render branch |
| `tests/PcsRemote.Web.Tests/Pages/IndexTests.cs` | Append 5 new test methods |

No other files change.

---

## 8. Out of Scope

- Visual styling of the `match-loaded` section (out of Phase 1 scope)
- Calling `GetTeamNamesAsync()` — this is deferred to the FlaUI integration phase (HLPS-006)
- Any match-actions UI (Refresh Scoreboard, Change Match) — covered in later IS-003 steps
- Clearing `_loadedMatch` on state transition away from `MatchLoaded` — not required because the `match-loaded` div is only rendered when `_currentState == MatchLoaded`
- **Known Phase 1 limitation:** If the page is mounted while the service is already in `MatchLoaded` state (e.g., a page refresh mid-session), `_loadedMatch` will be null and the `match-loaded` div will not render. In Phase 1 the mock service resets when the application restarts so this cannot be reached. Resolution is deferred: once the real FlaUI service persists state across browser refreshes (HLPS-006+), the fix is to expose a `CurrentMatch` property on `IPcsProAutomationService` and read it in `OnInitializedAsync`.
