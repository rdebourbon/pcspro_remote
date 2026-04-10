# SPEC-S-005: Minimal Web Host with Serilog

| Field | Value |
|---|---|
| **Document** | SPEC-S-005-Minimal-Web-Host.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-10 |
| **Step** | S-005 — Minimal Web Host and Initial Commit |
| **IS** | IS-001-Foundation.md (APPROVED v0.2) |
| **HLPS** | HLPS-001-Foundation.md (APPROVED v0.4) |
| **Branch** | `feature/S-005-minimal-web-host` |

---

## 1. Overview

This step replaces the placeholder `Program.cs` in `PcsRemote.Web` with a minimal ASP.NET Core host that bootstraps Serilog with console and rolling-file sinks, then starts the application. No Blazor middleware is added — the host's only observable behaviour at this stage is structured logging on startup.

This is the final step of HLPS-001. Once merged, all F-SC-1 through F-SC-10 pass on master.

---

## 2. Scope

### In Scope

- Replace the placeholder `Program.cs` (currently just `Hello World!`) with a production-quality minimal host:
  - Bootstrap Serilog early (before `WebApplication.CreateBuilder`) using the two-stage initialisation pattern: a transient bootstrap logger active during host construction, replaced by the fully-configured logger once the host is built
  - Configure Serilog with two sinks:
    - **Console** — structured/readable output
    - **Rolling file** — writes to `logs/pcs-remote-.log` with daily roll and 7-day retention; the implementer must configure the retention limit explicitly via the sink's rolling/retention options
  - Call `UseSerilog()` on the host builder to replace ASP.NET Core's default logging
  - Emit a single structured startup log entry at `Information` level (e.g., "PCS Remote starting…") after the logger is fully configured
  - Call `app.Run()` to start the application
- Update `appsettings.json` if Serilog-specific configuration is more natural there; if all Serilog configuration is done in code, the default ASP.NET Core `Logging` section in `appsettings.json` may optionally be removed to avoid confusion (it has no effect when `UseSerilog()` is active, but leaving it is harmless)
- No `AddRazorComponents`, no `MapRazorComponents`, no `app.UseBlazorFrameworkFiles`, no static file middleware for Blazor — all Blazor pipeline is explicitly excluded per HLPS-001 §2

### Out of Scope

- Any Blazor, SignalR, or UI middleware
- DI service registration (beyond Serilog bootstrap)
- Health endpoints or routing beyond what the minimal host provides by default
- Changes to any project other than `PcsRemote.Web`

---

## 3. Requirements

### R-1: Serilog bootstrap (two-stage)

A transient `Log.Logger` must be configured before `WebApplication.CreateBuilder` is called, using a minimal console-only configuration. This ensures any exception thrown during host construction is captured in structured form rather than lost or printed as unformatted text. The transient logger is replaced by the fully-configured logger once the host is built.

### R-2: Console sink

The configured logger must write to the console. Output must be in a structured format that includes timestamp, level, and message at minimum.

### R-3: Rolling file sink

The configured logger must write to a rolling file in the `logs/` subdirectory relative to the working directory. The file name must include a date component (e.g., `logs/pcs-remote-20260410.log` or similar). The implementer must configure a 7-day retention limit via the sink's rolling/retention options. The file must be created on application start and contain at least one log entry.

### R-4: No Blazor middleware and no leftover routes

`Program.cs` must not contain any of: `AddRazorComponents`, `MapRazorComponents`, `UseBlazorFrameworkFiles`, any reference to Blazor/Razor component pipeline, or any `app.Map*` endpoint route (including any `Hello World` placeholder route from the scaffold). Verified by code review.

### R-5: Startup log entry

At least one `Information`-level structured log message must be emitted after the host is fully configured and before `app.Run()` is called.

### R-6: Clean shutdown logging

The `try/catch/finally` or `using` pattern must ensure that `Log.CloseAndFlush()` is called on both normal exit and unhandled exception, preventing log entry loss on shutdown.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` exits 0 with zero errors and zero warnings |
| AC-2 | `dotnet run` from the `PcsRemote.Web` directory starts without exception |
| AC-3 | After `dotnet run`, at least one structured log entry appears in the console output containing timestamp, log level, and message |
| AC-4 | After `dotnet run`, a file matching `logs/pcs-remote*.log` exists in `src/PcsRemote.Web/logs/` (the `logs/` subdirectory of the working directory used for AC-2) and contains at least one log entry |
| AC-5 | `Program.cs` contains no reference to Blazor/Razor component pipeline (`AddRazorComponents`, `MapRazorComponents`, `UseBlazorFrameworkFiles`) and no `app.Map*` endpoint routes |
| AC-6 | `Program.cs` calls `Log.CloseAndFlush()` on both normal and exceptional exit paths |
| AC-7 | `PcsRemote.Web.csproj` contains no new `<PackageReference>` elements — all required Serilog packages were installed in S-001 |
| AC-8 | `dotnet test` from the solution root exits 0 with all tests passing — confirming F-SC-2 through F-SC-5 remain green before the branch is merged |

---

## 5. Verification Table

| AC | Verification Method |
|---|---|
| AC-1 | `dotnet build` — exit 0, zero warnings |
| AC-2 | `dotnet run` — process starts; manually terminate after observing output |
| AC-3 | Visual inspection of console output after `dotnet run` |
| AC-4 | Inspect `logs/` directory relative to the working directory used for AC-2 (`src/PcsRemote.Web/logs/`) — confirm file matching `pcs-remote*.log` exists and contains at least one entry |
| AC-5 | Code review of `Program.cs` — confirm absence of Blazor pipeline keywords and any `app.Map*` routes |
| AC-6 | Code review of `Program.cs` — verify `Log.CloseAndFlush()` placement on normal and exceptional paths |
| AC-7 | Inspect `PcsRemote.Web.csproj` — no new `<PackageReference>` elements added |
| AC-8 | `dotnet test` from solution root — exit 0, all tests pass |

---

## 6. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | Claude Sonnet 4.6, GPT-4.1 | NEEDS REVIEW / APPROVE — 1 MEDIUM + 3 LOW (Sonnet); 1 MEDIUM + 3 LOW (GPT) |
| R2 | 2026-04-10 | Claude Sonnet 4.6 | **APPROVED** — all R1 findings verified resolved; 0 blocking issues; 1 non-blocking note (AC-4 path precision — corrected v0.2 → v0.3) |

### R1 Findings Applied (v0.1 → v0.2)

| Ref | Finding | Disposition | Resolution |
|---|---|---|---|
| Sonnet — MEDIUM | `dotnet test` absent — F-SC-2–F-SC-5 master gate unverified | Accept | Added AC-8: `dotnet test` from solution root, exit 0; added to Verification Table |
| Sonnet — LOW | `logs/` path ambiguous in AC-4 verification | Accept | AC-4 criterion and verification entry now specify `src/PcsRemote.Web/logs/` |
| Sonnet — LOW | `appsettings.json` cleanup untracked in scope | Accept | Scope note rewritten to make cleanup explicitly optional |
| Sonnet — LOW | Hello World route not prohibited | Accept | R-4 extended to cover `app.Map*` routes; AC-5 updated accordingly |
| GPT — MEDIUM | No error handling for logger init failures | Reject | R-1 (two-stage bootstrap) and R-6 (`CloseAndFlush`) already address this; exact try/catch is delivery detail not spec concern |
| GPT — LOW | "Structured" format ambiguity | Reject | "Timestamp, level, message" is sufficient observable specification; format choice belongs to delivery |
| GPT — LOW | `logs/` directory creation not required | Reject | Serilog.Sinks.File creates directories automatically — delivery concern |
| GPT — LOW | Retention enforcement no AC | Accept (partial) | R-3 now explicitly requires implementer to configure 7-day retention via sink options; no separate AC needed |
