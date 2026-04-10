# SPEC-S-001: Mock Service Project Scaffold

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-Mock-Scaffold.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-10 |
| **Step** | S-001 — Project Scaffold |
| **IS** | IS-002-Mock-Service.md (APPROVED v0.2) |
| **HLPS** | HLPS-002-Mock-Service.md (APPROVED v0.3) |
| **Branch** | `feature/hlps002-S001-mock-scaffold` |

---

## 1. Overview

IS-002 S-001 calls for creating `PcsRemote.Automation.Mock` and `PcsRemote.Automation.Mock.Tests` and adding both to the solution. **`PcsRemote.Automation.Mock` was already created during HLPS-001 S-001** — it exists in `src/PcsRemote.Automation.Mock/` with the correct TFM (`net8.0`) and a reference to `PcsRemote.Core`. No changes to the source project are needed.

This step therefore delivers only:
1. The `PcsRemote.Automation.Mock.Tests` MSTest test project
2. Its addition to the solution

---

## 2. Scope

### In Scope

- Create `tests/PcsRemote.Automation.Mock.Tests/` as an MSTest test project targeting `net8.0`
- Install the **single `MSTest` meta-package** and **`FluentAssertions`** at the exact versions used in `PcsRemote.Core.Tests` (`MSTest 4.0.1`, `FluentAssertions 8.9.0`)
- Declare `<LangVersion>latest</LangVersion>`, `<Nullable>enable</Nullable>`, and `<ImplicitUsings>enable</ImplicitUsings>` — matching every other project in the solution
- Declare `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />` — matching the global-using convention of all other test projects
- Add a project reference from `PcsRemote.Automation.Mock.Tests` to `PcsRemote.Automation.Mock`
- Add `PcsRemote.Automation.Mock.Tests` to the solution file

### Out of Scope

- Any production code in `PcsRemote.Automation.Mock` (S-002 onwards)
- Any test code beyond the empty project scaffold
- Changes to `PcsRemote.Automation.Mock.csproj`

---

## 3. Requirements

**R-1:** Test project must target `net8.0` — matching the source project and the rest of the test suite.

**R-2:** Test project packages must use the exact package names and versions from `PcsRemote.Core.Tests`: the **single `MSTest` meta-package** at `4.0.1` (not individual `MSTest.*` packages) and `FluentAssertions` at `8.9.0`.

**R-3:** The solution file must list both `PcsRemote.Automation.Mock` (already present) and `PcsRemote.Automation.Mock.Tests` (newly added). `dotnet sln list` must show both.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` from solution root exits 0 with zero errors and zero warnings |
| AC-2 | `dotnet test` from solution root exits 0 — all pre-existing tests pass; `PcsRemote.Automation.Mock.Tests` contributes 0 tests (no new passing or failing tests) |
| AC-3 | `dotnet sln list` shows **both** `src\PcsRemote.Automation.Mock\PcsRemote.Automation.Mock.csproj` and `tests\PcsRemote.Automation.Mock.Tests\PcsRemote.Automation.Mock.Tests.csproj` |
| AC-4 | `PcsRemote.Automation.Mock.Tests.csproj` contains exactly one `<ProjectReference>` pointing to `PcsRemote.Automation.Mock` |
| AC-5 | `PcsRemote.Automation.Mock.Tests.csproj` contains `<PackageReference Include="MSTest" Version="4.0.1" />` and `<PackageReference Include="FluentAssertions" Version="8.9.0" />` — no other `MSTest.*` package references |
| AC-6 | `PcsRemote.Automation.Mock.Tests.csproj` is located at `tests\PcsRemote.Automation.Mock.Tests\` |
| AC-7 | `PcsRemote.Automation.Mock.Tests.csproj` declares `<LangVersion>latest</LangVersion>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, and `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />` |

---

## 5. Verification Table

| AC | Verification Method |
|---|---|
| AC-1 | `dotnet build` — exit 0, zero warnings |
| AC-2 | `dotnet test` — exit 0; `dotnet test --list-tests` produces no test entries for `PcsRemote.Automation.Mock.Tests` (project is discovered but lists no tests) |
| AC-3 | `dotnet sln list` — both paths visible; confirm neither has been removed or renamed |
| AC-4 | Inspect `PcsRemote.Automation.Mock.Tests.csproj` — exactly one `<ProjectReference>` present |
| AC-5 | Inspect `PcsRemote.Automation.Mock.Tests.csproj` — exact `Include` names and versions match; no individual `MSTest.*` packages |
| AC-6 | File system check — csproj exists at `tests\PcsRemote.Automation.Mock.Tests\` |
| AC-7 | Inspect `PcsRemote.Automation.Mock.Tests.csproj` — `<LangVersion>latest</LangVersion>`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`, and `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />` all present |

---

## 6. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | Sonnet 4.6, GPT-4.1 | NEEDS REVIEW — 2 HIGH, 4 MEDIUM, 5 LOW |
| R2 | 2026-04-10 | Sonnet 4.6 | APPROVED — all R1 findings resolved; 2 LOW non-blockers applied as v0.3 polish |
