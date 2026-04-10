# SPEC-S-001: Repository Scaffold and Tooling

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-Scaffold.md |
| **Version** | 0.2 |
| **Status** | APPROVED |
| **Date** | 2026-04-10 |
| **Step** | S-001 |
| **IS** | IS-001-Foundation.md (APPROVED v0.2) |
| **HLPS** | HLPS-001-Foundation.md (APPROVED v0.4) |

---

## 1. Objective

Establish the complete repository skeleton: solution file, all project stubs, developer tooling files, and documentation scaffolding. By the end of this step the solution compiles cleanly, formatting is enforced, and a developer can clone the repo and be immediately productive.

---

## 2. Branch

`feature/S-001-scaffold`

Target: `master`

---

## 3. Solution Structure

### 3.1 Projects

All app-type projects (`PcsRemote.Web`, `PcsRemote.TrayHost`) are created using the minimal appropriate template — no demo pages, sample endpoints, or auto-generated scaffold content. Class library projects are created as empty stubs. This ensures `Program.cs` remains a minimal stub (per §9) and no out-of-scope content is generated.

**src/**

| Project | SDK Type | Notes |
|---|---|---|
| `PcsRemote.Web` | ASP.NET Core web application | .NET 8, Windows |
| `PcsRemote.Core` | Class library | .NET 8 |
| `PcsRemote.Automation` | Class library | .NET 8, Windows |
| `PcsRemote.Automation.Mock` | Class library | .NET 8 |
| `PcsRemote.TrayHost` | Windows Forms application | .NET 8, Windows |

**tests/**

| Project | SDK Type | Notes |
|---|---|---|
| `PcsRemote.Core.Tests` | MSTest test project | .NET 8 |
| `PcsRemote.Web.Tests` | MSTest test project | .NET 8 |
| `PcsRemote.E2E.Tests` | MSTest test project | .NET 8 |

### 3.2 Project References

Test projects reference their targets:
- `PcsRemote.Core.Tests` → `PcsRemote.Core`
- `PcsRemote.Web.Tests` → `PcsRemote.Web`

`PcsRemote.Web` references `PcsRemote.Core`.

`PcsRemote.Automation` and `PcsRemote.Automation.Mock` reference `PcsRemote.Core`.

---

## 4. NuGet Dependencies

Dependencies are installed to the appropriate projects. Exact version resolution follows standard NuGet floating rules for .NET 8 compatible releases.

| Package | Target Project(s) |
|---|---|
| `Stateless` | `PcsRemote.Core` |
| `Serilog.AspNetCore` | `PcsRemote.Web` |
| `Serilog.Sinks.Console` | `PcsRemote.Web` |
| `Serilog.Sinks.File` | `PcsRemote.Web` |
| `MSTest.TestFramework` + `MSTest.TestAdapter` | All three test projects |
| `FluentAssertions` | All three test projects |
| `bUnit` | `PcsRemote.Web.Tests` |

---

## 5. Tooling Files

### 5.1 `.gitignore`

Standard .NET `.gitignore` covering: build outputs (`bin/`, `obj/`), IDE artefacts (`.vs/`, `.idea/`, `*.user`), runtime outputs (`logs/`), NuGet package cache, and OS noise (`.DS_Store`, `Thumbs.db`).

### 5.2 `.editorconfig`

Enforce consistent C# style across all projects. At minimum:

- Indentation: 4 spaces (no tabs)
- Charset: `utf-8`
- Line endings: `crlf` (Windows target)
- Trim trailing whitespace
- Insert final newline
- C# `using` directives: system namespaces first, then alphabetical
- Prefer `var` where type is apparent
- Braces on new lines (Allman style)
- Naming conventions: PascalCase for types and public members, camelCase with `_` prefix for private fields

### 5.3 `Directory.Build.props` (required)

Must centralise shared MSBuild properties: `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. This makes AC-1's zero-warning requirement enforceable at the build level — without it, `dotnet build` exits 0 regardless of warnings present. All downstream steps assume these properties are active.

---

## 6. Documentation

### 6.1 `README.md` (repository root)

Must cover, in order:

1. **Project title and one-line description** — what PCS Remote is and who it is for
2. **Architecture overview** — brief description of the solution structure (web app, core domain, automation layer, mock, tray host) and how they relate
3. **Prerequisites** — .NET 8 SDK, Windows OS, Visual Studio / Rider / VS Code
4. **How to build** — `dotnet build` command
5. **How to run** — `dotnet run` from `PcsRemote.Web`
6. **How to test** — `dotnet test` command
7. **Project structure** — directory listing with one-line description per project
8. **Links** — to `docs/PCS-Remote/PROJECT-CONTEXT.md` for deeper architectural context

### 6.2 `.github/copilot-instructions.md`

Captures the project's coding standards and conventions so Copilot suggestions stay consistent. Must cover:

- **Namespace** — `PcsRemote.*` convention
- **Language version** — C# 12, .NET 8
- **Null safety** — nullable reference types enabled; no `!` suppression without comment
- **Error handling** — exceptions for unexpected states; `InvalidOperationException` for state machine violations
- **Logging** — Serilog structured logging; all log calls use message templates with named properties, never string interpolation
- **Testing** — MSTest 3.x + FluentAssertions >8.0; test method names follow `MethodName_Scenario_ExpectedResult` convention; no `Thread.Sleep` in tests
- **Style** — matches `.editorconfig`; `var` where type is apparent; Allman braces
- **Architecture rules** — `PcsRemote.Core` must have zero dependencies on Web, Automation, or TrayHost; interfaces over concrete types at layer boundaries

---

## 7. Verification

All of the following must pass before this step is considered complete:

| Check | Method |
|---|---|
| Solution compiles with zero errors and zero warnings | `dotnet build PCS_Remote.sln` |
| Formatting is consistent | `dotnet format PCS_Remote.sln --verify-no-changes` |
| Test runner discovers all test projects | `dotnet test` (zero tests, zero failures) |
| All 8 projects present in solution | `dotnet sln list` |
| README covers all required sections (§6.1) | Manual review |
| `.github/copilot-instructions.md` covers all required standards (§6.2) | Manual review |
| No `bin/` or `obj/` directories tracked in git | `git ls-files bin/ obj/` returns empty |

---

## 8. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` exits 0 with no errors or warnings on a clean checkout |
| AC-2 | `dotnet format --verify-no-changes` exits 0 |
| AC-3 | `dotnet test` exits 0 (no tests written yet — test discovery must succeed without errors) |
| AC-4 | `dotnet sln list` shows all 8 project paths |
| AC-5 | README.md is present at repository root and covers all sections in §6.1 |
| AC-6 | `.github/copilot-instructions.md` is present and covers all standards in §6.2 |
| AC-7 | No `bin/` or `obj/` output is tracked in git |

---

## 9. Out of Scope

- Any production or domain code (deferred to S-002+)
- `Program.cs` content beyond the default stub (deferred to S-005)
- Any test code beyond project stubs (deferred to S-004)

---

## 10. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | GPT 5.4, Sonnet 4.6, Opus 4.6 | REVISE (GPT: REVISE 1H+3M; Sonnet: REVISE 1M+1L; Opus: APPROVE 2L) |
| R2 | 2026-04-10 | GPT 5.4 | APPROVE — all R1 findings resolved or legitimately deferred/rejected |
