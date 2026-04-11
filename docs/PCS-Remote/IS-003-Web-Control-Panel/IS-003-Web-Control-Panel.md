# IS-003: Real-Time Web Control Panel — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-003-Web-Control-Panel.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-11 |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Prerequisites** | HLPS-001 delivered (domain model, state machine); HLPS-002 delivered (mock service + DI registration) |

---

## Overview

This sequence implements the HLPS-003 scope in nine atomic steps: from completing the ASP.NET Core/Blazor middleware pipeline, through building the UI shell, SignalR hub, individual components, the auto-launch service, and finally Playwright E2E infrastructure. Each step is independently verifiable and builds on the last.

Steps are identified with stable IDs (S-001 through S-009). IDs are never renumbered; deferred steps leave gaps.

---

## Steps

### S-001 — Blazor Server + Radzen middleware pipeline ✅ DELIVERED (commit `3e390b9`)

**What changes:** `PcsRemote.Web` is upgraded from a bare ASP.NET Core host to a functional Blazor Server application. This means adding the Radzen Blazor and SignalR packages, registering all required Blazor, Radzen, and SignalR services in `Program.cs`, wiring the middleware pipeline (Blazor hub endpoint, fallback page routing), and configuring Kestrel to bind on all interfaces so the app is reachable from any machine on the local network.

**Why:** The DI and middleware pipeline scope item in HLPS-003 was explicitly deferred from HLPS-001. Without it, no Blazor components can render and W-SC-1 (network-accessible web app) cannot be satisfied. This step is the foundation all subsequent UI work depends on.

**Dependencies:** HLPS-002 delivery complete (DI automation service registration already in `Program.cs`).

**Verification intent:** The application starts without errors. A browser on the same network can reach the root URL and receive a valid HTTP response (not a 404 or connection-refused). Build passes with 0 errors and 0 warnings. All existing tests remain green.

---

### S-002 — Application shell: Blazor host page, routing, and Radzen layout ✅ DELIVERED (commit `ede407c`)

**What changes:** The Blazor host page (entry point for the browser), application router, global Razor imports, `MainLayout`, and navigation structure are created. The Radzen theme is applied consistently throughout the shell. The layout includes a header area (placeholder for connected-user count and status indicator, to be wired in later steps) and a content body for page components. A minimal `Home` page component is added as the default route.

**Why:** Provides the visual and structural scaffold that all subsequent component steps (S-004 through S-007) will be placed within. Satisfies the Radzen theming and layout scope item in HLPS-003 and ensures W-SC-1 (a browser can load the page and see meaningful content, not a blank page) is fully met.

**Dependencies:** S-001 (Blazor middleware pipeline must exist before Razor components can render).

**Verification intent:** The application loads in a browser and displays the Radzen layout with navigation and a home page. No Bootstrap styles are present. Build and all existing tests pass.

---

### S-003 — PcsProHub: SignalR hub with connection tracking and state broadcast ✅ DELIVERED (commit `299b087`)

**What changes:** A dedicated SignalR hub (`PcsProHub`) is introduced in `PcsRemote.Web`. The hub tracks active connections using a thread-safe counter. When a client connects, the hub immediately pushes the current PCS Pro state to that client (late-joiner support). When a client disconnects, the counter is decremented. A singleton service manages the connection count and exposes a notification mechanism so Blazor components can react to count changes. The hub subscribes to the `IPcsProAutomationService` state-change event and broadcasts each transition to all connected clients. The hub endpoint is registered in the middleware pipeline.

**Why:** Directly addresses W-SC-3 (live connected-user count), W-SC-9 (state pushed to all browsers simultaneously), and the state-on-reconnect requirement from HLPS-003. All multi-browser scenarios require this hub to be in place before component integration steps.

**Dependencies:** S-001 (hub registration belongs in the middleware pipeline established there). Note: S-003 is strictly backend — the hub and connection tracker have no dependency on any Razor components or application shell; S-005 is the downstream consumer that wires the count into the UI.

**Verification intent:** Unit/integration tests verify: new client immediately receives the current state on connect; state-change events from the service are broadcast to all connected clients; connection count increments and decrements correctly. Build and all existing tests pass.

---

### S-004 — PCS Pro status indicator component ✅ DELIVERED (commit `06942d4`)

**What changes:** A `PcsProStatusIndicator` Blazor component is created. It maps the eight `PcsProState` values to the correct colour indicator (⚪🟡🟢🔴 per PRD §12). The component subscribes to state-change notifications and triggers a UI re-render when state changes, so the indicator updates in real-time without a page refresh. It is wired into the application layout so it is visible on every page. bUnit tests cover every state-to-colour mapping and verify that a state-change event causes the expected visual update.

**Why:** Satisfies W-SC-2 (status indicator updates in real-time across all states, including `Error → 🔴`). This component is the most visible piece of feedback the operator relies on and is a prerequisite for the auto-launch and match-selection flows to be visually verifiable.

**Dependencies:** S-003 (hub and state-change notification mechanism exist); S-002 (layout shell is available to host the indicator).

**Verification intent:** bUnit tests for all 8 states pass. Manual inspection confirms indicator is visible and correct in the browser. Build and all tests pass.

---

### S-005 — Connected user count: real-time header display ✅ DELIVERED (commit `1c5ecc7`)

**What changes:** The connected-user count is surfaced in the application header using the count tracking service introduced in S-003. The header component subscribes to count-change notifications and re-renders without a page refresh. bUnit tests verify correct rendering for 0, 1, and N connected users.

**Why:** Satisfies W-SC-3 (connected-user count updates when browsers connect/disconnect). The count data already exists after S-003; this step wires it into the visible UI and adds test coverage.

**Dependencies:** S-003 (connection tracker service established); S-002 (header structure available).

**Verification intent:** bUnit tests for user count display pass (0, 1, and N users rendered correctly). Playwright multi-context verification (two browser contexts, W-SC-3) is deferred to S-009, where the E2E infrastructure exists. Build and all tests pass.

---

### S-006 — Match selection flow: cards, loading state, auto-select, interactivity gating ✅ DELIVERED (commit `d55cc39`)

**What changes:** A match selection page and `MatchCard` component are created. On the service reaching `MatchSelection` state (entered after login), the page calls `GetTodaysMatchesAsync` (which transitions the service to `MatchSelectionSearching`) — during which a loading indicator is shown and card interaction is disabled. On reaching `MatchSelectionReady`, results are rendered as selectable cards showing home team, away team, match type, and start time. If exactly one match is returned, it is auto-selected without user interaction. If zero matches are returned, an appropriate empty state is shown. If multiple matches are returned, only cards in `MatchSelectionReady` state are interactive; all other states render cards as non-interactive. Selecting a card calls `LoadMatchAsync` and the resulting state transitions are propagated in real-time. bUnit tests cover: card rendering with correct data, loading-state interaction lock, auto-select with one match, empty state with zero matches, non-interactive rendering in non-`MatchSelectionReady` states, and the state-gating logic for each relevant state.

**Why:** Satisfies W-SC-4 (match cards with correct data), W-SC-5 (single-match auto-select), W-SC-6 (selection triggers observable state transitions), and W-SC-8 (interactivity gated on state). This is the primary interactive workflow delivered in HLPS-003.

**Dependencies:** S-003 (state-change notification mechanism and broadcast established); S-002 (application shell and routing available to host the match selection page).

**Verification intent:** All bUnit tests pass, including zero-match empty state and each state-gating scenario. End-to-end test via mock confirms state transitions are visible after card selection. Build and all tests pass.

---

### S-007 — Team name display after match load ✅ DELIVERED (commit `a2eac71`)

**What changes:** After `LoadMatchAsync` completes and the service reaches a post-load state, the home and away team names from the loaded `MatchInfo` are displayed in the UI (per PRD §9). This may be part of the match page or a dedicated component. bUnit tests verify that when the service has a loaded match, the team names render correctly.

**Why:** Satisfies W-SC-7 (team names displayed after match load). This is a small, focused addition building on the match-selection flow established in S-006.

**Dependencies:** S-006 (match-selection flow and state transitions exist; loaded match data is available).

**Verification intent:** bUnit tests confirm team names render correctly from mock data. Build and all tests pass.

---

### S-008 — Auto-launch IHostedService

**What changes:** An `AutoLaunchService` implementing `IHostedService` is added to `PcsRemote.Web`. On `StartAsync`, if the `PcsPro:AutoLaunch` configuration flag is enabled (default: `true`), the service calls the automation service to initiate the launch-and-login flow. If the flow reaches `Error` state, the existing status indicator (S-004) already shows 🔴 — no additional UI work is required here. The service is registered in the DI pipeline. An integration test using the mock service verifies that the hosted service triggers the launch flow on startup and that state transitions are observable.

**Why:** Satisfies W-SC-11 (auto-launch initiates on application start; state transitions visible to any connecting browser). Also satisfies the design decision from HLPS-003 (W-U-2 resolved): auto-launch is the default, disableable via configuration.

**Dependencies:** S-003 (hub broadcasts all launch-flow state transitions to connected clients); S-004 (error state is visually handled by the status indicator — no additional UI work required in this step).

**Verification intent:** Integration test confirms the service triggers launch when `AutoLaunch=true` and does not trigger when `AutoLaunch=false`. Playwright smoke test verifying state visibility after startup is deferred to S-009. Build and all tests pass.

---

### S-009 — Playwright E2E project and smoke test

**What changes:** The existing `PcsRemote.E2E.Tests` project (scaffolded during HLPS-001 with MSTest and FluentAssertions but no Playwright packages) is upgraded with Playwright packages and test host infrastructure. At least one smoke test is implemented: the application starts, the root URL returns HTTP 200, and the status indicator element is present in the DOM. The Playwright multi-context tests for W-SC-3 (connected user count — two browser contexts) and W-SC-9 (simultaneous state broadcast — two browser contexts assert same state) are also added in this step. The project is verified to run via `dotnet test`.

**Why:** Satisfies W-SC-10 (Playwright E2E project with at least one passing smoke test). The multi-context tests added here deliver final verified coverage for W-SC-3 (live connected-user count across browsers) and W-SC-9 (simultaneous state broadcast to all browsers). Earlier steps (S-003, S-005) established the mechanisms and bUnit coverage; this step provides the end-to-end browser-level proof.

**Note:** W-SC-9 unit/integration test coverage (state broadcast to all connected clients) is established in S-003. W-SC-3 unit/integration test coverage (counter mechanism and UI display) is established in S-003 and S-005 respectively. The Playwright multi-context tests in this step are the definitive browser-level verification for both.

**Dependencies:** S-001 (app runs and serves HTTP); S-002 (application shell present — Playwright needs the layout to render); S-003 (hub and connection tracking complete); S-004 (status indicator element exists in DOM); S-005 (connected user count component complete — needed for W-SC-3 multi-context test).

**Verification intent:** `dotnet test` on the E2E project produces at least one passing test. Multi-context Playwright tests for W-SC-3 and W-SC-9 pass. Build and all solution tests pass.

---

## Coverage Map

| HLPS-003 Success Criterion | Delivering Step(s) |
|---|---|
| W-SC-1: App accessible from local network browser | S-001, S-002 |
| W-SC-2: Status indicator updates in real-time | S-004 |
| W-SC-3: Connected user count updates | S-005 (mechanism + bUnit), S-009 (Playwright verification) |
| W-SC-4: Match cards display correct data | S-006 |
| W-SC-5: Single match auto-selected | S-006 |
| W-SC-6: Match selection triggers visible state transitions | S-006 |
| W-SC-7: Team names displayed after match load | S-007 |
| W-SC-8: Cards non-interactive in non-MatchSelectionReady states | S-006 |
| W-SC-9: State pushed to all browsers simultaneously | S-003 (mechanism), S-009 (Playwright verification) |
| W-SC-10: Playwright E2E project with passing smoke test | S-009 |
| W-SC-11: Auto-launch initiates on start, visible to any browser | S-008 |

---

## Dependency Graph

```
S-001 (middleware pipeline)
  ├─ S-002 (app shell + layout)
  └─ S-003 (SignalR hub + connection tracking)

S-002 + S-003  →  S-004 (status indicator component)
S-002 + S-003  →  S-005 (connected user count display)
S-002 + S-003  →  S-006 (match selection flow)

S-003 + S-004  →  S-008 (auto-launch service)

S-006          →  S-007 (team name display)

S-001 + S-002 + S-003 + S-004 + S-005  →  S-009 (Playwright E2E + smoke test)
```

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | Sonnet 4.6, Opus 4.6, GPT-4.1 | NEEDS REVIEW — 2 HIGH, 6 MEDIUM accepted; 4 deferred/rejected; v0.2 fixes applied |
| R2 | 2026-04-11 | Sonnet 4.6, Opus 4.6, GPT-4.1 | NEEDS REVIEW — Sonnet+Opus APPROVED; GPT 2 MEDIUM (S-009 missing S-002 dep, "respectively" inversion) + 2 LOW accepted; v0.3 fixes applied |
| R3 | 2026-04-11 | Sonnet 4.6, GPT-4.1 | APPROVED — unanimous, 0 blocking, 0 non-blocking; all R2 fixes verified, 0 regressions |
