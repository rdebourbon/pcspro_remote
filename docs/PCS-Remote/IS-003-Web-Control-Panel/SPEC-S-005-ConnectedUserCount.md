# SPEC-S-005: ConnectedUserCount — Real-Time Connected User Count Display

| Field | Value |
|---|---|
| **Document** | SPEC-S-005-ConnectedUserCount.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-11 |
| **Step ID** | S-005 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Success Criteria** | W-SC-3 |
| **Dependencies** | S-002 (layout shell), S-003 (connection tracker service) |

---

## 1. Objective

Create a `ConnectedUserCount` Razor component that displays the current number of connected browsers in the application header. The component subscribes to count-change notifications from the connection tracking service and re-renders in real-time without a page refresh. It is wired into `MainLayout` so it is visible on every page.

---

## 2. Background

`IConnectionTracker` exposes `ConnectionCount` (integer snapshot) and `ConnectionCountChanged` (event, `Action<int>?`, fired after every increment or decrement). Both are available to Blazor Server components via DI; the service is a singleton registered in `Program.cs` as part of S-003.

The PRD §12 wireframe shows "● 1 user online" in the header — the label is singular when the count is exactly 1 and plural otherwise. The dot/bullet is a visual affordance and is treated as implementation detail (e.g., CSS pseudo-element or literal character); it is not part of the testable text content. Note: the backing metric is the number of active SignalR connections (browser tabs/windows), not a distinct user identity count; the "user(s) online" copy is intentional per PRD §12 and does not require identity de-duplication.

The `ConnectionCountChanged` event has type `Action<int>?` (not `EventHandler`). Subscription and unsubscription use the standard `+=` / `-=` delegate syntax. The event argument is the new count value.

---

## 3. Label Format (normative)

The displayed text label is:

| Count | Label text |
|---|---|
| 0 | "0 users online" |
| 1 | "1 user online" |
| N (≥ 2) | "N users online" |

The word "user" is singular only when the count is exactly 1; "users" is used for all other counts including zero.

---

## 4. Requirements

### R-1 — Component location and namespace
The component is created in `src/PcsRemote.Web/Shared/` so that it is accessible from `MainLayout` under the existing `@using PcsRemote.Web.Shared` import. The `IConnectionTracker` interface lives in `PcsRemote.Web.Hubs`; the component's code section must include the appropriate `@using` directive.

### R-2 — Dependency injection
The component receives `IConnectionTracker` via Blazor's standard DI injection mechanism. It does not accept the tracker as a Razor parameter.

### R-3 — Initialisation
On `OnInitializedAsync`, the component registers a handler on `IConnectionTracker.ConnectionCountChanged` **before** reading `IConnectionTracker.ConnectionCount` for the initial snapshot. This ordering eliminates the window in which a count change could fire after the snapshot is taken but before the handler is attached. The component must not display a stale default (e.g., 0) when the application has active connections at the time of first render. Note: this ordering is a code-discipline requirement enforced during delivery; it cannot be verified deterministically in a synchronous mock environment (AC-6 verifies that the initial read is performed, not that subscription precedes it).

### R-4 — Real-time update
When the `ConnectionCountChanged` event fires, the component updates its internal count and marshals a re-render back to the Blazor renderer. After re-render, the displayed label reflects the new count with the correct singular/plural form. Every event triggers a re-render; no debouncing is applied.

### R-5 — Label rendering
The component renders a single element that contains the count text. The element carries the CSS class `connected-user-count` so tests can locate it without relying on element type or position. The label text matches the format in §3 exactly — including singular/plural agreement.

### R-6 — Disposal
The component implements `IDisposable`. The `Dispose` method unsubscribes the handler from `IConnectionTracker.ConnectionCountChanged` to prevent memory leaks and zombie callbacks after the component is torn down.

### R-7 — Layout integration
The `MainLayout.razor` comment placeholder `@* S-005: Connected user count placeholder *@` is replaced with a `<ConnectedUserCount />` element. No other changes to `MainLayout.razor` are required in this step.

---

## 5. Acceptance Criteria

### AC-1 — Zero users
When the component is rendered with the tracker reporting zero connections, the rendered HTML contains an element with the CSS class `connected-user-count` whose text content is "0 users online".

### AC-2 — Singular user
When the component is rendered with the tracker reporting exactly one connection, the displayed text is "1 user online".

### AC-3 — Plural users
When the component is rendered with the tracker reporting a count greater than one (e.g., 3), the displayed text is "3 users online".

### AC-4 — Real-time update: count increase
After initial render, when a `ConnectionCountChanged` event is raised increasing the count (e.g., from 0 to 1), the component re-renders and the displayed text reflects the new count with correct singular/plural form.

### AC-5a — Real-time update: plural-to-singular decrease
After initial render with a count of 2, when a `ConnectionCountChanged` event is raised with value 1, the component re-renders and the displayed text is "1 user online".

### AC-5b — Real-time update: singular-to-zero decrease
After initial render with a count of 1, when a `ConnectionCountChanged` event is raised with value 0, the component re-renders and the displayed text is "0 users online".

### AC-6 — Initialisation from current count
When the component is mounted with the tracker already reporting a non-zero count (e.g., 2), the rendered label immediately shows "2 users online" — not "0 users online". This verifies that the component reads `ConnectionCount` on mount, not just subscribes passively.

### AC-7 — Disposal unsubscribes
After the component is disposed, raising a `ConnectionCountChanged` event on the mock tracker does not invoke the component's handler. Tests verify that the handler delegate is cleanly detached.

### AC-8 — Layout wire-up
The `MainLayout.razor` placeholder comment is absent from the final build; `<ConnectedUserCount />` is rendered in its place. A bUnit test rendering `MainLayout` verifies the component is present in the layout output.

---

## 6. Out of Scope

- Visual styling of the "●" bullet prefix — this is CSS implementation detail and is not testable via bUnit text-content assertions.
- Negative count handling — `IConnectionTracker.Decrement` is a no-op when the count is already zero; the component does not need to guard against negative values.
- Any Playwright multi-context verification — W-SC-3 Playwright coverage is deferred to S-009 per the IS.
- Any changes to `_Imports.razor` — all required using directives are already present or will be added inline in the component.

---

## 7. Branch Convention

`feature/hlps003-S005-connected-user-count`

---

## 8. Accepted Risks

| ID | Description | Rationale |
|---|---|---|
| AR-1 | `ConnectionCountChanged` fires from the `Increment`/`Decrement` callers (SignalR hub threads); `InvokeAsync(StateHasChanged)` handles the synchronisation-context marshal, but unobserved exceptions from the handler are not caught. Hardening is HLPS-005 scope. | Acceptable for Phase 1, consistent with AR-1 in SPEC-S-004 and AR-2 in SPEC-S-003. |

---

## 9. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | GPT-5.4, Sonnet 4.6 | GPT NEEDS REVIEW (2 MEDIUM: CSS class not normative, subscribe-before-read AC gap; 1 LOW: terminology); Sonnet APPROVED (2 LOW: AC-5 ambiguity, CSS class "e.g."); v0.2 fixes: CSS class made normative in R-5, AC-1 extended to assert selector, AC-5 split into AC-5a/AC-5b, §2 terminology note added, AC-6 clarified as R-3 testable proxy |
| R3 | 2026-04-11 | GPT-5.4 | **APPROVED** — R2 regression verified resolved; R-3 honesty note and AC-6 revert accepted; 0 new findings |
