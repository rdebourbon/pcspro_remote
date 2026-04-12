# SPEC-S-003 — ScoreboardPreview Blazor Component

| Field | Value |
|---|---|
| **Spec** | SPEC-S-003-ScoreboardPreview.md |
| **Step** | IS-004 S-003 |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-12 |
| **Governing IS** | IS-004-Scoreboard.md v0.3 (APPROVED) |
| **Dependencies** | S-001 (`IScoreboardService` delivered `2bc5784`), S-002 (polling service delivered `b923f18`) |

---

## 1. Purpose

Deliver the `ScoreboardPreview` Razor component — the primary operator-facing view of the live scoreboard image. The component subscribes to service events, renders the latest image when a match is loaded, and shows a placeholder in all other states. It supports late joiners by reading the cached image on initialisation rather than waiting for the next poll tick.

---

## 2. Scope

**In scope:**
- New `ScoreboardPreview.razor` Razor component in `PcsRemote.Web/Shared/`
- Embedding the component in the `MatchLoaded` section of `Pages/Index.razor`
- bUnit tests in `PcsRemote.Web.Tests/Shared/ScoreboardPreviewTests.cs` (TC-1 through TC-8, TC-10) and `PcsRemote.Web.Tests/Pages/IndexTests.cs` (TC-9 — addition to existing file)

**Out of scope:**
- Refresh button (S-004)
- Change match button (S-005)
- Playwright E2E tests (S-006)
- Any styling beyond the structural CSS class hooks required by tests
- Changes to `IScoreboardService`, `IPcsProAutomationService`, or any Core type

---

## 3. Behavioural Requirements

### 3.1 Rendering States

| Condition | Rendered output |
|---|---|
| `CurrentState == MatchLoaded` AND `CurrentImage != null` | An `<img>` element with a `data:image/jpeg;base64,` data URI containing the Base64-encoded image bytes |
| `CurrentState == MatchLoaded` AND `CurrentImage == null` | Placeholder element with text "No scoreboard data" |
| `CurrentState != MatchLoaded` (any other state) | Placeholder element with text "No scoreboard data" |

The placeholder must carry a CSS class `scoreboard-placeholder` and the exact text "No scoreboard data" (the text is used as a test assertion value and must not change without updating the tests). The image element must carry a CSS class `scoreboard-image`. These are the only structural CSS requirements; visual styling is not in scope.

### 3.2 Late-Joiner Support

On `OnInitializedAsync`, the component reads `IScoreboardService.CurrentImage` and `IPcsProAutomationService.CurrentState` to set its initial render without waiting for an event. It must subscribe to events **before** reading both state values (narrows the race window between subscription and snapshot, consistent with the pattern established in `PcsProStatusIndicator` and `Index.razor`). Note: this pattern narrows but does not fully eliminate the race window — full atomicity across both reads is not achievable without service-level locking. The accepted risk is a single incorrect placeholder render on the very first frame in an extreme cold-start edge case.

### 3.3 ScoreboardUpdated Subscription

When `IScoreboardService.ScoreboardUpdated` fires, the component must:
1. Update its internal image reference to the new bytes carried by the event argument.
2. Dispatch `StateHasChanged` via `InvokeAsync` (Blazor circuit safety — event may fire from a thread-pool thread).

The current state is not re-read from the service on each event; the component tracks state locally via the `StateChanged` subscription.

### 3.4 StateChanged Subscription

When `IPcsProAutomationService.StateChanged` fires, the component must:
1. Update its local state snapshot.
2. When transitioning **away** from `MatchLoaded` (new state ≠ `MatchLoaded`): clear the internal image reference to null so the placeholder renders correctly even if `IScoreboardService.CurrentImage` is still non-null (ClearCache has not yet run).
3. Dispatch `StateHasChanged` via `InvokeAsync`.

When transitioning **into** `MatchLoaded`: re-read `IScoreboardService.CurrentImage` to pick up any image that was captured between the previous state and the transition event. This handles the case where the cold-start polling fires before the component has received its first `ScoreboardUpdated` event. If `CurrentImage` is null at transition time, the placeholder is shown.

If the incoming state equals the current local state (`MatchLoaded → MatchLoaded`), the re-read logic is skipped as a no-op.

### 3.5 Disposal

The component implements `IDisposable`. `Dispose` must unsubscribe from both `IScoreboardService.ScoreboardUpdated` and `IPcsProAutomationService.StateChanged`. No other cleanup is required.

### 3.6 Index.razor Integration

The `ScoreboardPreview` component is embedded inside the existing `MatchLoaded` section of `Index.razor`. The component manages its own rendering state internally; `Index.razor` does not pass state or image data as parameters — it simply renders `<ScoreboardPreview />`. The team name display already present in the `MatchLoaded` section must be preserved.

---

## 4. Component API

The component has **no parameters**. All dependencies are injected via `@inject`:
- `IScoreboardService` — for `CurrentImage` and `ScoreboardUpdated`
- `IPcsProAutomationService` — for `CurrentState` and `StateChanged`

---

## 5. Test Cases

All tests are bUnit component tests in `PcsRemote.Web.Tests/Shared/ScoreboardPreviewTests.cs`.

Both `IScoreboardService` and `IPcsProAutomationService` are provided as Moq mocks registered in the bUnit `TestContext.Services` collection.

### TC-1: Placeholder rendered when state is not MatchLoaded

Implemented as a `[DataTestMethod]` with a `[DataRow]` per state: `NotRunning`, `Launching`, `LoginScreen`, `MatchSelection`, `MatchSelectionSearching`, `MatchSelectionReady`, `Error`. For each row, render the component with that initial state and assert: the `scoreboard-placeholder` element is present; no `scoreboard-image` element is present. (Seven discrete test rows in the runner output.)

### TC-2: Placeholder rendered when state is MatchLoaded but no image is cached

Render with `CurrentState = MatchLoaded` and `CurrentImage = null`. Assert: `scoreboard-placeholder` present; no `scoreboard-image`.

### TC-3: Image rendered when state is MatchLoaded and an image is cached

Render with `CurrentState = MatchLoaded` and `CurrentImage = <non-empty byte array>`. Assert: `scoreboard-image` element present; `scoreboard-placeholder` absent; the `src` attribute of the image element begins with `data:image/jpeg;base64,` and the Base64 segment decodes to the provided bytes.

### TC-4: ScoreboardUpdated event updates the rendered image

Render with `CurrentState = MatchLoaded` and `CurrentImage = <initial bytes>`. Raise `ScoreboardUpdated` with new bytes. Assert via `WaitForAssertion`: the `scoreboard-image` src attribute reflects the new bytes.

### TC-5: StateChanged away from MatchLoaded shows placeholder

Render with `CurrentState = MatchLoaded` and `CurrentImage = <bytes>`. Confirm image is visible. Raise `StateChanged` with `MatchSelection`. Assert via `WaitForAssertion`: `scoreboard-placeholder` present; `scoreboard-image` absent.

### TC-6: StateChanged into MatchLoaded with cached image shows image

Render with `CurrentState = NotRunning` and `CurrentImage = <bytes>` (pre-populated cache). Raise `StateChanged` with `MatchLoaded`. Assert via `WaitForAssertion`: `scoreboard-image` present; `scoreboard-placeholder` absent. (Verifies §3.4 re-read on MatchLoaded transition.)

### TC-7: Late-joiner reads cached image on initialisation

Arrange mocks before rendering: `CurrentState = MatchLoaded`, `CurrentImage = <bytes>`. Render component. Assert immediately (no event needed): `scoreboard-image` present. (Verifies §3.2.)

### TC-8: Disposal unsubscribes from both events

Render the component then call `cut.Instance.Dispose()` (synchronous direct-disposal pattern used project-wide — bUnit's `cut.Dispose()` defers the teardown, causing the `VerifyRemove` assertion to race). Verify via `mock.VerifyRemove` that both `ScoreboardUpdated` and `StateChanged` handlers were removed exactly once.

### TC-9: Index.razor embeds ScoreboardPreview and preserves team names

bUnit test in `Pages/IndexTests.cs`. Render `Index.razor` with `CurrentState = MatchLoaded` and a `_loadedMatch` with known home/away team name values. Assert: a `scoreboard-preview` element (or the component's root element) is present in the rendered output; the home and away team name spans still contain the expected team name values.

### TC-10: StateChanged into MatchLoaded with null cached image shows placeholder

Render with `CurrentState = NotRunning` and `CurrentImage = null`. Raise `StateChanged` with `MatchLoaded`. Assert via `WaitForAssertion`: `scoreboard-placeholder` present; `scoreboard-image` absent. (Verifies the null-guard in §3.4 re-read logic.)

---

## 6. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `ScoreboardPreview` component exists in `PcsRemote.Web/Shared/` |
| AC-2 | Component renders `scoreboard-placeholder` for all non-`MatchLoaded` states |
| AC-3 | Component renders `scoreboard-placeholder` when state is `MatchLoaded` but no image is cached |
| AC-4 | Component renders `scoreboard-image` with correct Base64 data URI when state is `MatchLoaded` and image is cached |
| AC-5 | `ScoreboardUpdated` event causes the rendered image to update |
| AC-6 | `StateChanged` away from `MatchLoaded` causes placeholder to render |
| AC-7 | `StateChanged` into `MatchLoaded` reads `CurrentImage` and renders image if available |
| AC-8 | Component reads `CurrentImage` and `CurrentState` on `OnInitializedAsync` (late-joiner support) |
| AC-9 | `Dispose` unsubscribes from both `ScoreboardUpdated` and `StateChanged` |
| AC-10 | `ScoreboardPreview` is embedded in the `MatchLoaded` section of `Index.razor`; existing team name display is preserved |
| AC-11 | All 10 TCs pass (TC-1 through TC-8, TC-9 Index integration, TC-10 null-image MatchLoaded path); all existing tests continue to pass |

---

## 7. Branch and Commit Strategy

- Branch: `feature/S-003-scoreboard-preview`
- Commit incrementally: component first, then Index integration, then tests
- Squash-merge to master with a single summary commit after adversarial review approval

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | GPT APPROVED; Sonnet NEEDS REVIEW — 1 HIGH, 2 MEDIUM (blocking), 3 LOW (non-blocking); fixes applied in v0.2 |
| R2 | 2026-04-12 | GPT-4.1, Claude Sonnet 4.6 | **UNANIMOUS APPROVED** — all R1 fixes verified; Sonnet 1 MEDIUM non-blocking (§2 omits IndexTests.cs); fix applied in v0.3 |

### R1 Findings Applied

| Finding | Reviewer | Severity | Disposition | Fix |
|---|---|---|---|---|
| AC-10 (Index integration + team names) has no TC | Sonnet | HIGH | Accept | Added TC-9 (IndexTests.cs bUnit test) |
| MIME type for Base64 data URI unspecified | Sonnet | MEDIUM | Accept | §3.1 and TC-3 updated to specify `data:image/jpeg;base64,` |
| TC-6 null-image path on MatchLoaded transition not tested | Sonnet | MEDIUM | Accept | Added TC-10 |
| TC-1 parameterization ambiguity | Sonnet | MEDIUM | Accept (as LOW clarification) | TC-1 specified as `[DataTestMethod]` with `[DataRow]` per state |
| §3.2 race closure overclaim | Sonnet | LOW | Accept | Qualified with "narrows but does not fully eliminate" |
| §3.4 MatchLoaded→MatchLoaded idempotency undefined | Sonnet | LOW | Accept | Added no-op sentence to §3.4 |
| §4 RefreshCompleted mentioned as unused distractor | Sonnet | LOW | Accept | Removed from §4 |
| TC-8 disposal idiom (`cut.Instance.Dispose()`) | Sonnet | LOW | Reject | `cut.Instance.Dispose()` is the established project pattern (see `PcsProStatusIndicatorTests.cs` with explanatory comment) — bUnit's `cut.Dispose()` defers teardown and races the `VerifyRemove` assertion |
