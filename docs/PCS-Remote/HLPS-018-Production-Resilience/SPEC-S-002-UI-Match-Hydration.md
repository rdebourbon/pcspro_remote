# SPEC-S-002 — UI Match Hydration and ChangeMatch Button Resilience

| Field           | Value |
|-----------------|-------|
| **Status**      | APPROVED |
| **Step ID**     | S-002 |
| **Governs**     | IS-018 S-002 |
| **Branch**      | `feature/S-002-ui-match-hydration` |
| **SC Coverage** | SC1, SC3 |
| **Depends On**  | S-001 (DONE) |

---

## Problem

`Index.razor` stores `_loadedMatch` as a local field set only during the original user interaction (match selection or UseCurrentMatch). On Blazor circuit reconnect (page refresh, SignalR drop, tab wake), `OnInitializedAsync` never reads `LoadedMatch` from the singleton service, so `_loadedMatch` is null. This causes the team names and ChangeMatch button to vanish (P1 in HLPS-018). Similarly, a second browser tab never receives `_loadedMatch` because the service event was consumed by the original tab's circuit.

---

## Requirements

### R1 — Hydrate `_loadedMatch` from service on initialisation

`OnInitializedAsync` must read `AutomationService.LoadedMatch` and assign it to `_loadedMatch`. This ensures that on circuit reconnect or new tab, the current match metadata is immediately available for rendering.

### R2 — Hydrate `_loadedMatch` on `StateChanged(MatchLoaded)`

In the `OnStateChanged` handler, when the new state is `MatchLoaded`, assign `_loadedMatch = AutomationService.LoadedMatch`. This assignment must occur **before** `StateHasChanged()` is called, ensuring the renderer sees the populated `_loadedMatch` in the same render pass. This requires a dedicated `MatchLoaded` branch in the handler (not falling through to the generic `else` block where `StateHasChanged()` fires first). This ensures cross-tab live updates: when Tab A loads a match, Tab B's event handler picks up the populated `LoadedMatch` and renders it.

### R3 — Clear `_loadedMatch` on non-MatchLoaded transitions

When state transitions to `MatchSelection` or `NotRunning`, clear `_loadedMatch = null`. The `NotRunning` path already does this; add clearing for `MatchSelection` as well.

### R4 — Fallback rendering when `_loadedMatch` is null in MatchLoaded state

If `_currentState == MatchLoaded` but `_loadedMatch` is somehow null (defensive edge case), render a fallback label (e.g., "Match loaded") and still render the ChangeMatch button. This ensures the operator always has a way to change the match.

### R5 — Simplify `UseCurrentMatchAsync` local construction

The `UseCurrentMatchAsync` method currently constructs a local `MatchInfo("current", ...)` from the returned `MatchTeams`. Since S-001 now populates `LoadedMatch` in the service, this local construction is redundant. After `AutomationService.UseCurrentMatchAsync()` returns, simply read `_loadedMatch = AutomationService.LoadedMatch` (consistent with R2 pattern). Remove the manual MatchInfo construction.

---

## Acceptance Criteria

| AC | Description |
|----|-------------|
| AC-1 | After page refresh while in MatchLoaded state, team names and ChangeMatch button are visible. |
| AC-2 | In a second browser tab opened while in MatchLoaded state, team names and ChangeMatch button are visible. |
| AC-3 | When Tab A loads a match, Tab B updates to show team names and ChangeMatch without refresh. |
| AC-4 | When Tab A triggers ChangeMatch, Tab B clears team metadata and shows MatchSelection UI without refresh. |
| AC-5 | If LoadedMatch is null but state is MatchLoaded, a fallback label and ChangeMatch button render. |
| AC-6 | After UseCurrentMatch, a page refresh shows team names and ChangeMatch button (attach-flow specific). |
| AC-7 | After UseCurrentMatch, a second tab shows team names and ChangeMatch button. |
| AC-8 | With two tabs open, UseCurrentMatch in Tab A updates Tab B without refresh. |
| AC-9 | UseCurrentMatchAsync handler reads match data from the service rather than constructing it locally. |

---

## Test Strategy

S-002 changes are primarily in `Index.razor` (Blazor component). Testing approach:

1. **bUnit test updates (required):** The existing `BuildMock` helper in `IndexTests.cs` creates a `Mock<IPcsProAutomationService>` (Moq) that does NOT set up `LoadedMatch` — Moq returns `null` by default. After R2, `OnStateChanged(MatchLoaded)` will overwrite `_loadedMatch` with `null` from the mock, breaking all team-name rendering tests. Fix: update `BuildMock` (or per-test setups) to configure `mock.Setup(s => s.LoadedMatch).Returns(...)` with the expected match, so the Moq mock mirrors the real service's post-S-001 behaviour.

2. **New bUnit tests (required):**
   - `OnInit_MountedInMatchLoadedState_HydratesLoadedMatch` — mount component when mock reports `MatchLoaded` state with a configured `LoadedMatch`, verify team names and ChangeMatch button render without any user interaction.
   - `StateChanged_MatchLoaded_HydratesLoadedMatchFromService` — mount component, then raise `StateChanged(MatchLoaded)` from mock, verify `_loadedMatch` is hydrated and team names render.

3. **Manual verification** for AC-1 through AC-4 and AC-6 through AC-8 (circuit reconnect and cross-tab behaviour require a running Blazor server).

4. **Code review verification** for AC-5 (fallback rendering markup) and AC-9 (UseCurrentMatchAsync simplification).

---

## Risks

| Risk | Mitigation |
|------|------------|
| `OnStateChanged` runs on a background thread — reading `AutomationService.LoadedMatch` outside `InvokeAsync` could cause a race | The assignment `_loadedMatch = AutomationService.LoadedMatch` is already inside the `InvokeAsync` lambda in `OnStateChanged`. The `LoadedMatch` property is set atomically (reference assignment) before `StateChanged` fires (S-001 ordering guarantee). |
| Fallback label might cause confusion if it appears during normal operation | The fallback is a defensive edge case — with S-001, `LoadedMatch` should always be non-null in MatchLoaded state. The fallback prevents a blank panel if something unexpected occurs. |

---

## Review History

| Round | Panel | Outcome | Notes |
|-------|-------|---------|-------|
| R1 | GPT-5.4, Sonnet 4.6 | 0/2 APPROVE | Blocking: attach flow not in ACs, Moq BuildMock, R2 ordering. NB: AC-6 prescriptive, no test procedure. All addressed. |
| R2 | GPT-5.4, Sonnet 4.6 | 2/2 APPROVE | NB: MatchSelection clearing test (1/2), R2 test initial state (1/2). Both incorporated during delivery. |
