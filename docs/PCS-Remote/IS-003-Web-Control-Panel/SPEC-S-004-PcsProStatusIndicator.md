# SPEC-S-004: PcsProStatusIndicator — Real-Time PCS Pro State Indicator

| Field | Value |
|---|---|
| **Document** | SPEC-S-004-PcsProStatusIndicator.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-11 |
| **Step ID** | S-004 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Success Criteria** | W-SC-2 |
| **Dependencies** | S-002 (layout shell), S-003 (state-change notification mechanism) |

---

## 1. Objective

Create a `PcsProStatusIndicator` Razor component that reflects the current PCS Pro lifecycle state as a coloured indicator in the application header. The component subscribes to state-change notifications from the automation service and re-renders in real-time without a page refresh. It is wired into `MainLayout` so it is visible on every page.

---

## 2. Background

`IPcsProAutomationService` exposes `CurrentState` (current snapshot) and `StateChanged` (event fired on every transition). Both are available to Blazor Server components via DI because the entire application runs in-process on the server. There is no need for a SignalR client connection from the component — the component can subscribe to the server-side event directly.

The PRD §12 defines 8 states with specific colour groups and descriptive labels. The component must honour this mapping exactly.

---

## 3. State-to-Indicator Mapping

The following mapping is normative and derived from PRD §12. All eight states must be handled; the component must not have a fallback that silently ignores an unknown state.

| State | Colour Group | Label Text |
|---|---|---|
| `NotRunning` | Grey | "PCS Pro not running" |
| `Launching` | Yellow | "PCS Pro starting…" |
| `LoginScreen` | Yellow | "Logging in…" |
| `MatchSelection` | Yellow | "Loading matches…" |
| `MatchSelectionSearching` | Yellow | "Searching…" |
| `MatchSelectionReady` | Yellow | "Select a match" |
| `MatchLoaded` | Green | "Match loaded" |
| `Error` | Red | "Error" |

---

## 4. Requirements

### R-1 — Component location and namespace
The component is created in `src/PcsRemote.Web/Shared/` so that it is accessible from `MainLayout` under the existing `@using PcsRemote.Web.Shared` import.

### R-2 — Dependency injection
The component receives `IPcsProAutomationService` via Blazor's standard DI injection mechanism. It does not accept the service as a Razor parameter.

### R-3 — Initialisation
On `OnInitializedAsync`, the component registers a handler on `IPcsProAutomationService.StateChanged` **before** reading `IPcsProAutomationService.CurrentState` for the initial snapshot. This ordering eliminates the window in which a state transition could fire after the snapshot is taken but before the handler is attached. It must not render a stale default — it reflects the live state from the moment it is first displayed.

### R-4 — Real-time update
When the `StateChanged` event fires, the component updates its internal state representation and marshals a re-render back to the Blazor renderer (the component runs on the server but the update must be dispatched on the component's synchronisation context to be safe). After re-render, the indicator reflects the new state. Every event triggers a re-render; no debouncing or coalescing of rapid transitions is applied.

### R-5 — Colour and label rendering
The component renders a coloured indicator element (visual dot or badge) and a text label. The indicator element carries a CSS class that encodes the colour group (one of: grey, yellow, green, red) so that the rendered output is verifiable in tests without inspecting inline styles or images. The label text matches the mapping in §3 exactly.

### R-6 — Coverage of all states
All eight `PcsProState` values must produce a distinct, defined rendering. The component must not fall through to a silent default for any state. Any value not in the mapping table (e.g., a future enum addition) must render the red indicator with the label "Error" — failing visibly rather than silently. A `default` branch that causes an assertion failure in debug builds is the recommended approach for surfacing unhandled states during development without silently swallowing them in production.

### R-7 — Disposal
The component implements `IDisposable`. The `Dispose` method unsubscribes the handler from `IPcsProAutomationService.StateChanged` to prevent memory leaks and zombie callbacks after the component is torn down.

### R-8 — Layout integration
The `MainLayout.razor` comment placeholder `@* S-004: PCS Pro status indicator placeholder *@` is replaced with a `<PcsProStatusIndicator />` element. No other changes to `MainLayout.razor` are required in this step.

---

## 5. Acceptance Criteria

### AC-1 through AC-8 — State-to-colour mapping (one AC per state)
For each of the eight `PcsProState` values, when the component is rendered with the service in that state, the rendered HTML contains:
- An indicator element bearing a CSS class that correctly identifies the colour group (grey/yellow/green/red) for that state, as defined in §3.
- The exact label text for that state, as defined in §3.

These eight cases are the primary bUnit test targets.

### AC-9 — Real-time state update
When a `StateChanged` event is raised (via the mock service) after the component is rendered, the component re-renders and the indicator colour and label text reflect the new state. The test must use a transition that crosses at least one colour-group boundary (e.g., `NotRunning` → `MatchLoaded`) so that both the colour class and label are verified to update. Tests must verify the rendered output *after* the event, not just that an event was subscribed to.

### AC-10 — Initialisation from current state
When the component is mounted with the mock service already in a non-default state (e.g., `MatchLoaded`), the rendered indicator immediately shows `MatchLoaded` (green) — not `NotRunning`. This verifies that the component reads `CurrentState` on mount, not just subscribes passively.

### AC-11 — Disposal unsubscribes
After the component is disposed, raising a `StateChanged` event on the mock service does not result in a callback being fired on the disposed component. Tests should verify no `StateHasChanged` call is made post-disposal, or that the event handler is cleanly detached.

### AC-12 — Layout wire-up
The `MainLayout.razor` placeholder comment is absent from the final build; `<PcsProStatusIndicator />` is rendered in its place. A bUnit test rendering `MainLayout` verifies the component is present in the layout output.

### AC-13 — Unknown-state fallback
When the component's state-mapping logic receives a `PcsProState` value outside the eight defined values, the rendered HTML contains a red indicator (colour group: red) and the label text "Error". In debug builds, a `Debug.Assert` or equivalent fires to make the unhandled case visible during development.

---

## 6. Out of Scope

- `Error` state label details (e.g., reason text) — the label is the static string "Error" in this step; dynamic error messages are HLPS-005 scope.
- CSS styling and visual polish — the colour-class approach satisfies testability requirements; full visual design is implementation detail.
- Any changes to the `_Imports.razor` file beyond what is already present (the `@using PcsRemote.Web.Shared` import was added in S-002 and covers this component).

---

## 7. Branch Convention

`feature/hlps003-S004-status-indicator`

---

## 8. Accepted Risks

| ID | Description | Rationale |
|---|---|---|
| AR-1 | `StateChanged` event fires from automation-service threads; `InvokeAsync(StateHasChanged)` handles the marshal, but unobserved exceptions from the handler are not caught in this component (consistent with AR-2 in SPEC-S-003). Hardening is HLPS-005 scope. | Acceptable for Phase 1 as established in HLPS-003 §Out of Scope. |

---

## 9. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | Sonnet 4.6, GPT-4.1 | NEEDS REVIEW — 2 MEDIUM + 3 LOW accepted; 2 deferred; 1 rejected; v0.2 fixes applied |
| R2 | 2026-04-11 | Sonnet 4.6, GPT-4.1 | GPT APPROVED; Sonnet NEEDS REVIEW — R2-F1 (LOW): R-6 normative unknown-state req had no covering AC; AC-13 added; v0.3 fix applied |
| R3 | 2026-04-11 | Sonnet 4.6 | **APPROVED** — R2-F1 fix verified (AC-13 covers all three R-6 obligations); 0 regressions; unanimous |
