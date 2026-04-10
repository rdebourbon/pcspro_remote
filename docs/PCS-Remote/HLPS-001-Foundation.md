# HLPS-001: Project Foundation & Domain Model

| Field | Value |
|---|---|
| **Document** | HLPS-001-Foundation.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | None — this is the first deliverable |

---

## 1. Problem Statement

The repository is empty — no code, no solution, no project structure. Before any feature work can begin, we need a solid foundation: a compiling .NET solution with the correct project structure, tooling configuration, developer documentation, and — critically — the core domain model and state machine that every subsequent HLPS depends on.

Without this foundation:
- No code can be written or tested.
- No shared types exist for the automation interface, state machine, or data models.
- No logging, configuration, or DI infrastructure is in place.
- No developer onboarding documentation exists.

This HLPS establishes the project from zero to a compiling, testable, documented shell with the complete domain model and state machine at its heart.

---

## 2. Scope

### In Scope

- **Repository scaffolding**: `.gitignore`, `.editorconfig`, `README.md`, `.github/copilot-instructions.md`
- **Solution and project creation**: `PCS_Remote.sln` with all projects defined in PROJECT-CONTEXT §6 (src + tests)
- **Serilog configuration**: Console + rolling file sinks from the first commit (per C-10)
- **Domain model types**: `PcsProState` enum, `MatchInfo` record, `MatchTeams` record — in `PcsRemote.Core`
- **IPcsProAutomationService interface**: The sole contract between web layer and automation layer (per PRD §5a)
- **State machine**: Full PCS Pro lifecycle using Stateless NuGet — 8 states, all transitions, timeout definitions (per PRD §6)
- **Unit tests**: State machine transition tests, guard tests, invalid transition rejection tests — in `PcsRemote.Core.Tests`
- **NuGet dependencies**: Stateless, Serilog.AspNetCore, Serilog.Sinks.Console, Serilog.Sinks.File, MSTest, FluentAssertions, bUnit (test infra only)
- **Initial git commit to master**

### Out of Scope

- Mock automation service implementation (HLPS-002)
- Any UI, SignalR, or web layer code (HLPS-003+)
- FlaUI or any PCS Pro interaction (HLPS-006)
- Radzen Blazor setup (HLPS-003)
- Playwright setup (HLPS-003)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| F-SC-1 | Solution compiles with zero errors and zero warnings | `dotnet build` succeeds cleanly |
| F-SC-2 | All unit tests pass | `dotnet test` — green |
| F-SC-3 | State machine enforces all valid transitions from PRD §6 | Unit tests verify each transition |
| F-SC-4 | State machine rejects all invalid transitions | Unit tests verify rejection |
| F-SC-5 | Timeout values for each transition are defined and testable | Unit tests verify timeout constants |
| F-SC-6 | Serilog writes to console and rolling file on application start | Log file created in `logs/` directory |
| F-SC-7 | `IPcsProAutomationService` interface matches PRD §5a contract | Code review |
| F-SC-8 | README.md documents project purpose, structure, and how to build/run | Manual review |
| F-SC-9 | copilot-instructions.md captures coding standards and project conventions | Manual review |
| F-SC-10 | .editorconfig enforces consistent formatting | Build-time validation |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| — | No HLPS-specific unknowns. All shared unknowns resolved in PROJECT-CONTEXT. | — | — |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | Not yet reviewed |
