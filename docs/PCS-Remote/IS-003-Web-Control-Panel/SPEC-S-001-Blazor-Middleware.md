# SPEC-IS003-S-001: Blazor Server + Radzen Middleware Pipeline

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-Blazor-Middleware.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-11 |
| **Step** | S-001 — Blazor Server + Radzen middleware pipeline |
| **IS** | IS-003-Web-Control-Panel.md (APPROVED v0.3) |
| **HLPS** | HLPS-003-Web-Control-Panel.md (APPROVED v0.2) |
| **Branch** | `feature/hlps003-S001-blazor-middleware` |

---

## 1. Overview

`PcsRemote.Web` is currently a bare ASP.NET Core host: it starts Kestrel, bootstraps Serilog, and registers the automation service — nothing more. No Blazor services, no SignalR hub mapping, no Razor Pages, and no Radzen components are wired in. The application binds only on the loopback interface, making it unreachable from any other machine on the local network.

This step upgrades `PcsRemote.Web` to a functional Blazor Server application by:

- Adding the Radzen Blazor NuGet package and pinning it in Central Package Management.
- Registering all Blazor Server, Radzen, and supporting services in the DI container.
- Wiring the middleware pipeline: static files, routing, the Blazor hub endpoint, and fallback page routing.
- Configuring Kestrel to accept connections on all network interfaces so the garage PC is reachable from any browser on the LAN.
- Adding a minimal host Razor Page so that an HTTP GET to the root URL returns a valid 200 response.

No Razor components, layouts, or application router are created here — those are S-002's responsibility. This step delivers only the pipeline and service infrastructure that all subsequent steps depend on.

---

## 2. Scope

### In Scope

- Add `Radzen.Blazor` to `PcsRemote.Web.csproj` (no inline version — managed via CPM).
- Add `Radzen.Blazor` version entry to `Directory.Packages.props` pinned at `10.2.2`.
- Register Razor Pages, Blazor Server, and Radzen component services in `Program.cs`.
- Wire the middleware pipeline in `Program.cs` to serve static files, enable routing, map the Blazor hub endpoint, and map the Razor Pages fallback to the host page.
- Configure Kestrel via `appsettings.json` to listen on `http://0.0.0.0:5000`, making the app reachable on all network interfaces.
- Create a minimal `Pages/_Host.cshtml` Razor Page — the fallback target for all non-API routes. This page needs only to return HTTP 200 with valid HTML. It does **not** render an App component, router, or Radzen layout (those belong to S-002).
- Create `Pages/_ViewImports.cshtml` to declare the `PcsRemote.Web.Pages` namespace and register the `Microsoft.AspNetCore.Mvc.TagHelpers` assembly — required infrastructure for the Razor Pages host model.
- Update `Properties/launchSettings.json` to align the development `http` profile's `applicationUrl` with the Kestrel binding (`http://0.0.0.0:5000`), ensuring `dotnet run` honours the same port and interface in development as in production.

### Out of Scope

- `App.razor`, `MainLayout.razor`, `_Imports.razor`, Home page, or any other Razor component (S-002).
- `<RadzenComponents/>` HTML tag in any layout (S-002 — no layout exists yet).
- SignalR hub class (`PcsProHub`) or connection counter service (S-003).
- Any bUnit test cases for Blazor components (S-004 and later).
- HTTPS configuration or TLS certificates (local LAN only; not required in Phase 1).
- Changes to `PcsRemote.Web.Tests.csproj`.

---

## 3. Requirements

**R-1:** `Radzen.Blazor` must be listed in `Directory.Packages.props` at version `10.2.2`. The `<PackageReference>` in `PcsRemote.Web.csproj` must carry no inline `Version` attribute.

**R-2:** `Program.cs` must register Razor Pages services, Blazor Server services, and Radzen component services in the DI container, added after the existing automation service registration and before `builder.Build()`.

**R-3:** The middleware pipeline in `Program.cs` must wire the four required components in the correct order: static file serving first, then routing, then the Blazor SignalR hub endpoint, then Razor Pages fallback routing targeting the host page. No endpoint mapping may precede routing. The existing Serilog bootstrap and automation service registration must be preserved unchanged.

**R-4:** `appsettings.json` must include a Kestrel endpoint configuration that binds HTTP to `0.0.0.0:5000`. This must be expressed as configuration data (not code) so the port is overridable by environment-specific settings without recompilation.

**R-4a:** `Properties/launchSettings.json` must be updated so the development `http` profile's `applicationUrl` matches the Kestrel binding (`http://0.0.0.0:5000`). This ensures `dotnet run` (which applies the launch profile) probes the same address as the production configuration and eliminates the port-override discrepancy between development and production.

**R-5:** `Pages/_Host.cshtml` must exist and be reachable as the fallback target for all non-API routes. It must return HTTP 200 with a minimal, valid HTML page. It must **not** attempt to render any Blazor `App` component, router, or `<component>` tag helper (no `App.razor` exists yet). A placeholder comment noting that S-002 will replace this with the full Blazor shell is appropriate.

**R-6:** `Pages/_ViewImports.cshtml` must declare the `PcsRemote.Web.Pages` namespace and import the `Microsoft.AspNetCore.Mvc.TagHelpers` assembly, satisfying the Razor Pages host infrastructure requirements.

**R-7:** `dotnet build` from the solution root exits 0 with zero errors and zero warnings.

**R-8:** `dotnet test` from the solution root exits 0. All pre-existing tests pass. No new tests are added or expected in this step.

**R-9:** An HTTP GET to `http://localhost:5000/` (or the configured port) when the app is running returns HTTP 200. This is the acceptance signal that Kestrel is bound, the pipeline is wired, and the host page is reachable.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `Directory.Packages.props` contains `<PackageVersion Include="Radzen.Blazor" Version="10.2.2" />` |
| AC-2 | `PcsRemote.Web.csproj` contains `<PackageReference Include="Radzen.Blazor" />` with no `Version` attribute |
| AC-3 | `Program.cs` registers Razor Pages, Blazor Server, and Radzen component services in the DI container |
| AC-4 | `Program.cs` pipeline includes: `UseStaticFiles()` before `UseRouting()`, `UseRouting()` before any endpoint mappings, Blazor hub endpoint mapped, and Razor Pages fallback to `_Host` mapped |
| AC-5 | `appsettings.json` contains a Kestrel configuration entry binding HTTP to `0.0.0.0:5000` |
| AC-5a | `Properties/launchSettings.json` `http` profile `applicationUrl` is updated to `http://0.0.0.0:5000` |
| AC-6 | `Pages/_Host.cshtml` exists under `src/PcsRemote.Web/Pages/` and contains a minimal HTML response (no `<component>` tag or `App.razor` reference) |
| AC-7 | `Pages/_ViewImports.cshtml` exists under `src/PcsRemote.Web/Pages/` and declares the page namespace and `Microsoft.AspNetCore.Mvc.TagHelpers` import |
| AC-8 | `dotnet build` exits 0 — zero errors, zero warnings |
| AC-9 | `dotnet test` exits 0 — all 79 pre-existing tests pass (32 in `PcsRemote.Core.Tests`, 47 in `PcsRemote.Automation.Mock.Tests`); test count does not change; no new tests added |
| AC-10 | With the app running via `dotnet run` (development profile), `curl http://localhost:5000/` returns HTTP 200 |

---

## 5. Verification Table

| AC | Verification Method |
|---|---|
| AC-1 | Inspect `Directory.Packages.props` — `Radzen.Blazor` entry present at `10.2.2` |
| AC-2 | Inspect `PcsRemote.Web.csproj` — `Radzen.Blazor` reference present, no `Version` attribute |
| AC-3 | Inspect `Program.cs` — service registrations present |
| AC-4 | Inspect `Program.cs` — `UseStaticFiles()` call present before `UseRouting()`; `UseRouting()` call present before endpoint mappings; Blazor hub endpoint mapped; Razor Pages fallback to `_Host` mapped |
| AC-5 | Inspect `appsettings.json` — Kestrel HTTP binding present for `0.0.0.0:5000` |
| AC-5a | Inspect `Properties/launchSettings.json` — `http` profile `applicationUrl` is `http://0.0.0.0:5000` |
| AC-6 | File system check — `Pages/_Host.cshtml` exists; inspect content — no `<component>` tag present |
| AC-7 | File system check — `Pages/_ViewImports.cshtml` exists; inspect content — `PcsRemote.Web.Pages` namespace and `Microsoft.AspNetCore.Mvc.TagHelpers` import present |
| AC-8 | Run `dotnet build` — exit 0, zero warnings in output |
| AC-9 | Run `dotnet test` — exit 0; output shows exactly 79 tests (32 + 47); no test count change |
| AC-10 | Start app with `dotnet run`; issue HTTP GET to `http://localhost:5000/`; confirm HTTP 200 response |

---

## 6. Accepted Risks

| ID | Risk | Rationale | Mitigation |
|---|---|---|---|
| AR-1 | HTTP-only on all interfaces (`0.0.0.0:5000`) | Intentional design for LAN accessibility. The garage PC is on a trusted private home network; the app serves no authentication credentials or sensitive data at this step. HTTPS and access controls are explicitly deferred to a later HLPS. | Documented assumption A-3 ("Local network only"). Operator is responsible for ensuring the machine is not exposed to untrusted networks. |

---

## 7. Commit Strategy

Changes are small and cohesive — a single feature branch with one or two commits is appropriate:

- One commit for the package and DI/pipeline changes (csproj, CPM, Program.cs, appsettings.json, launchSettings.json).
- One commit for the Razor Pages host infrastructure (Pages/_Host.cshtml, Pages/_ViewImports.cshtml).

Commits should be squash-merged to `master` as a single summary commit once the adversarial review passes.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | Sonnet 4.6, GPT-4.1 | NEEDS REVIEW — 2 HIGH, 3 MEDIUM, 2 LOW accepted; 1 LOW deferred, 1 LOW rejected; v0.2 fixes applied |
| R2 | 2026-04-11 | Sonnet 4.6, GPT-4.1 | APPROVED — unanimous; 0 blocking; 1 LOW (AC-10 Windows curl address) applied inline |
