# SPEC-IS003-S-002: Application Shell — Blazor Router, Radzen Layout, and Home Page

| Field | Value |
|---|---|
| **Document** | SPEC-S-002-App-Shell.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-11 |
| **Step ID** | S-002 |
| **Governing IS** | IS-003-Web-Control-Panel.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-003-Web-Control-Panel.md v0.2 (APPROVED) |
| **Branch** | `feature/hlps003-S002-app-shell` |
| **Depends on** | SPEC-S-001-Blazor-Middleware.md (APPROVED, delivered) |

---

## 1. Purpose

S-001 produced a working Blazor Server pipeline that returns HTTP 200 from a static placeholder page. S-002 replaces that placeholder with the live Blazor application shell: a `<component>` tag targeting `App.razor`, the Blazor client-side router, global Razor component imports, a `MainLayout` built with Radzen layout components, and a minimal Home page.

When this step is complete, navigating to `http://localhost:5000/` renders a fully styled Radzen layout with a Home page heading — the visual scaffold that S-004 through S-007 will populate with interactive components.

---

## 2. Requirements

### R-1 — Global Razor component imports

A `_Imports.razor` file must be created at the root of `PcsRemote.Web` (not inside `Pages/` or `Shared/`). Placing it at the project root ensures the imports apply to all Razor components in the project. It must declare at minimum:

- `PcsRemote.Web`, `PcsRemote.Web.Shared`, and `PcsRemote.Web.Pages` namespaces
- `Radzen` and `Radzen.Blazor` namespaces
- Any additional namespaces needed to avoid inline `@using` declarations in individual Razor components

### R-2 — Root application component (`App.razor`)

A root Razor component must be created at the root of `PcsRemote.Web`. It must:

- Wire the Blazor client-side router against the application's own assembly so all `@page`-attributed components are discoverable
- Define a `Found` handler that renders matched route components using `MainLayout` as the default layout
- Define a `NotFound` handler that renders a minimal "page not found" message, also within `MainLayout`

### R-3 — Main layout (`MainLayout.razor`)

A main layout component must be created in `PcsRemote.Web/Shared/`. It must:

- Inherit from `LayoutComponentBase`
- Compose the page using Radzen layout components providing at minimum a header region and a main content region
- Render the active page content (`@Body`) in the content region
- Include `<RadzenComponents />` exactly once — this fulfils the Radzen service requirement registered in S-001. `<RadzenComponents />` must be placed after `@Body` in the layout markup, following Radzen's documented layout conventions, so that overlay components (dialogs, tooltips, notifications) layer correctly above page content
- Display the application name ("PCS Remote") in the header region
- Include clearly labelled placeholder markup in the header region for the status indicator (to be wired in S-004) and connected-user count (to be wired in S-005). Placeholders should be HTML comments or minimal non-interactive elements that communicate their future purpose to the next developer
- Contain no reference to Bootstrap CSS classes or Bootstrap JavaScript

### R-4 — `_Host.cshtml` update

The existing `_Host.cshtml` (currently a static placeholder) must be updated to activate the Blazor application:

- Replace the static placeholder body content with a `<component>` tag targeting `App` with server-side prerendering
- Add a `<link>` element in `<head>` referencing the Radzen **Default** theme stylesheet served from the `Radzen.Blazor` static web asset path (this resolves HLPS-003 W-U-1: the Default theme is adopted for Phase 1; a theme swap requires only a one-line CSS link change and is deferred to HLPS-007 if needed) — no Bootstrap stylesheet link must be present in this file or anywhere in the project
- Add the Blazor Server JavaScript bundle `<script>` element at the end of `<body>`, immediately before `</body>`
- Retain `<base href="/" />` (added during S-001 code review)
- Retain `Layout = null` (required to prevent the Razor Pages layout engine from wrapping the already-complete HTML document)

### R-5 — Home page (`Index.razor`)

A minimal Home page component must be created in `PcsRemote.Web/Pages/` routed to `/`. It must:

- Display a simple heading that confirms the application loaded (for example, "PCS Remote" or "Welcome to PCS Remote")
- Carry no business logic — it exists solely to give the router a default destination

### R-6 — No Bootstrap constraint

The built project must contain no reference to Bootstrap CSS or Bootstrap JavaScript in any Razor, CSHTML, or static file. The Radzen stylesheet is the sole styling source for Phase 1.

### R-7 — bUnit smoke test

At least one bUnit test must be added to `PcsRemote.Web.Tests` that:

- Renders the Home page (`Index`) component in a bUnit `TestContext` with Radzen services registered
- Asserts that the rendered output contains the expected heading text
- Passes when run via `dotnet test`

---

## 3. Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | `_Imports.razor` exists at `src/PcsRemote.Web/_Imports.razor` and declares the required namespaces |
| AC-2 | `App.razor` exists at `src/PcsRemote.Web/App.razor` with a Blazor Router, Found handler using `MainLayout`, and NotFound handler |
| AC-3 | `MainLayout.razor` exists at `src/PcsRemote.Web/Shared/MainLayout.razor` with Radzen layout structure, `@Body`, `<RadzenComponents />`, application name in header, and placeholder markup for status indicator and connected-user count |
| AC-4 | `Index.razor` exists at `src/PcsRemote.Web/Pages/Index.razor` with `@page "/"` directive and a visible heading |
| AC-5 | `_Host.cshtml` contains a `<component>` tag for `App` using server-side prerendering |
| AC-5a | `_Host.cshtml` contains `<base href="/" />` within the `<head>` element |
| AC-6 | `_Host.cshtml` contains a Radzen Default theme CSS link; no Bootstrap CSS or JavaScript reference is present in any Razor component (`.razor`), CSHTML file (`.cshtml`), or static file in the project |
| AC-7 | `_Host.cshtml` contains the Blazor Server JavaScript bundle `<script>` tag |
| AC-8 | Build completes with 0 errors and 0 warnings |
| AC-9 | All 79 existing tests continue to pass |
| AC-10 | At least one new bUnit test in `PcsRemote.Web.Tests` passes, raising the total test count to ≥ 80 |
| AC-11 | **(Manual — browser navigation)** Navigating to `http://localhost:5000/` in a browser displays the Radzen-themed layout with the Home page heading visible — no blank page, no browser error, no server 500 |

---

## 4. Accepted Risks

| ID | Risk | Justification |
|----|------|---------------|
| AR-1 | HTTP-only, 0.0.0.0 binding | Inherited from S-001 (SPEC-S-001 §6 AR-1); no change in this step |
| AR-2 | No CSS/JS bundling or minification | Not required for Phase 1 LAN-only deployment on a single-page app with one theme stylesheet; deferred to HLPS-007 |
| AR-3 | `MainLayout` header placeholders are non-functional | By design — S-004 and S-005 are responsible for wiring real components into these regions. Placeholder elements carry no interactive behaviour and impose no runtime cost |

---

## 5. Out of Scope

- Status indicator component and state-to-colour logic (S-004)
- Connected-user count component (S-005)
- SignalR hub registration in `Program.cs` (S-003)
- Navigation sidebar or additional pages beyond Home (no requirement exists in HLPS-003 for additional pages in this step)
- CSS custom properties, theming overrides, or app-specific stylesheets (HLPS-007)

---

## 6. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | Sonnet 4.6, GPT-4.1 | NEEDS REVIEW — 3 MEDIUM accepted (theme W-U-1 unclosed, AC-6 missing JS, `<base href>` AC absent), 3 LOW (RadzenComponents placement, AC-11 unlabelled, verification table); v0.2 fixes applied |
| R2 | 2026-04-11 | Sonnet 4.6, GPT-4.1 | APPROVED — unanimous, 0 blocking, 0 non-blocking; all 6 R1 fixes confirmed, 0 regressions |
