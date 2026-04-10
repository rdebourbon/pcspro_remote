# HLPS-001: Project Foundation & Domain Model

| Field | Value |
|---|---|
| **Document** | HLPS-001-Foundation.md |
| **Status** | APPROVED |
| **Version** | 0.4 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | None — first deliverable; governed by PROJECT-CONTEXT v1.0 and PRD §5a/§6 |

---

## 1. Problem Statement

The repository is empty — no code, no solution, no project structure. Before any feature work can begin, we need a solid foundation: a compiling .NET solution with the correct project structure, tooling configuration, developer documentation, and — critically — the core domain model and state machine that every subsequent HLPS depends on.

Without this foundation:
- No code can be written or tested.
- No shared types exist for the automation interface, state machine, or data models.
- No logging infrastructure is in place.
- No developer onboarding documentation exists.

This HLPS establishes the project from zero to a compiling, testable, documented shell with the complete domain model and state machine at its heart.

---

## 2. Scope

### In Scope

- **Repository scaffolding**: `.gitignore`, `.editorconfig`, `README.md`, `.github/copilot-instructions.md`
- **Solution and project creation**: `PCS_Remote.sln` with all projects defined in PROJECT-CONTEXT §6 (src + tests)
- **Minimal host entry point**: `Program.cs` in `PcsRemote.Web` — ASP.NET Core host builder with Serilog configured via `UseSerilog()` and `app.Run()`. **No Blazor middleware, no `AddRazorComponents`, no `MapRazorComponents`, no fallback pages at this stage** — all Blazor pipeline is deferred to HLPS-003. This ensures `dotnet run` succeeds without Razor components.
- **Serilog configuration**: Console + rolling file sinks from the first commit (per C-10)
- **Domain model types**: `PcsProState` enum (8 values: NotRunning, Launching, LoginScreen, MatchSelection, MatchSelectionSearching, MatchSelectionReady, MatchLoaded, Error), `PcsProTrigger` enum (11 values: Launch, LoginDetected, CredentialsEntered, SearchTriggered, SpinnerGone, MatchOpened, ChangeMatch, Stop, Timeout, UnexpectedDialog, Retry), `MatchInfo` record, `MatchTeams` record — all in `PcsRemote.Core`
- **IPcsProAutomationService interface**: The sole contract between web layer and automation layer (per PRD §5a)
- **`PcsProStateMachine` class**: Named class in `PcsRemote.Core` wrapping `StateMachine<PcsProState, PcsProTrigger>`. Public surface: `PcsProState CurrentState` property; `void Fire(PcsProTrigger trigger)` (delegates to the inner Stateless machine; throws `InvalidOperationException` on invalid transitions, consistent with Stateless defaults — this is what F-SC-4 rejection tests assert against); transition notification via `OnTransitioned` callback (exposed as `Action<PcsProState>` or equivalent) so consuming implementations (HLPS-002 Mock, HLPS-006 Real) can raise `StateChanged` events. Timeout values defined as **compile-time `const` or `static readonly` values** in this class (not configuration), so they are directly assertable in unit tests.
- **State machine**: Full PCS Pro lifecycle — 8 states, 11 triggers, all transitions, timeout constant definitions (per PRD §6). Stateless is a passive trigger/transition engine; timeout *management* (firing `PcsProTrigger.Timeout` after a delay) is the responsibility of the automation service layer (HLPS-002+), not Core.
- **Unit tests**: State machine transition tests, transition constraint tests (per PRD §6), invalid transition rejection tests, and `Timeout` trigger tests (verifying that `Fire(PcsProTrigger.Timeout)` from each timed state transitions to `Error`) — in `PcsRemote.Core.Tests`
- **NuGet dependencies**: Stateless, Serilog.AspNetCore, Serilog.Sinks.Console, Serilog.Sinks.File, MSTest, FluentAssertions, bUnit (scaffolding only — first used in HLPS-003)
- **Initial git commit to master**

### Out of Scope

- Mock automation service implementation (HLPS-002)
- Any UI, SignalR, or web layer code (HLPS-003+)
- FlaUI or any PCS Pro interaction (HLPS-006)
- Radzen Blazor setup (HLPS-003)
- Playwright setup (HLPS-003)
- DI service registration beyond Serilog bootstrap (HLPS-002/HLPS-003)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| F-SC-1 | Solution compiles with zero errors and zero warnings | `dotnet build` succeeds cleanly |
| F-SC-2 | All unit tests pass | `dotnet test` — green |
| F-SC-3 | State machine enforces all valid transitions from PRD §6 | Unit tests verify each transition |
| F-SC-4 | State machine rejects all invalid transitions | Unit tests verify rejection |
| F-SC-5 | Timeout values for each timed transition are defined as compile-time constants; `Fire(PcsProTrigger.Timeout)` from each timed state transitions to `Error` | Unit tests assert constant values AND that firing `Timeout` from Launching, LoginScreen, MatchSelectionSearching, MatchSelectionReady transitions to `Error` |
| F-SC-6 | Serilog writes to console and rolling file on application start | `dotnet run` produces log file in `logs/` directory |
| F-SC-7 | `IPcsProAutomationService` interface matches PRD §5a contract | Code review |
| F-SC-8 | README.md documents project purpose, structure, and how to build/run | Manual review |
| F-SC-9 | copilot-instructions.md captures coding standards and project conventions | Manual review |
| F-SC-10 | `.editorconfig` enforces consistent formatting | `dotnet format --verify-no-changes` exits clean |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| — | No HLPS-specific unknowns. All shared unknowns resolved in PROJECT-CONTEXT. | — | — |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | GPT 5.4, Claude Sonnet 4.6, Claude Opus 4.6 | REVISE / REVISE / APPROVE — 3 HIGH + 2 MEDIUM accepted |
| R2 | 2026-04-10 | GPT 5.4, Claude Sonnet 4.6, Claude Opus 4.6 | REVISE / REVISE / APPROVE — 2 HIGH + 3 MEDIUM + 2 LOW accepted |
| R3 | 2026-04-10 | GPT 5.4, Claude Sonnet 4.6 | REVISE (1 HIGH) / APPROVE (1 LOW) — same finding: `Fire(PcsProTrigger)` absent from public surface. Applied as max-rounds author correction (v0.3 → v0.4) |

### R1 Findings Applied (v0.1 → v0.2)

| Ref | Finding | Resolution |
|---|---|---|
| F1 | F-SC-6 unverifiable without runnable host | Added minimal `Program.cs` Serilog bootstrap to In Scope; F-SC-6 verification updated to "`dotnet run` produces log file" |
| F2 | `PcsProTrigger` enum absent — Stateless requires `StateMachine<TState,TTrigger>` | Added `PcsProTrigger` enum to domain model types |
| F3 | Timeout *behavior* untested — F-SC-5 only verified constants, not timeout-triggered transitions | F-SC-5 expanded to require tests that verify timeout elapsed → `Error` state |
| F4 | State machine exposes no transition hook for `StateChanged` | Scope note added: state machine exposes `OnTransitioned` notifications |
| F5 | Problem statement claimed DI infrastructure resolved; scope did not deliver it | Removed DI claim; added explicit Out of Scope entry for DI service registration |
| F6 | F-SC-10 "build-time validation" misleading | Verification changed to `dotnet format --verify-no-changes` |
| F7 | "No dependencies" wording ambiguous | Tightened to "first deliverable; governed by PROJECT-CONTEXT v1.0 and PRD §5a/§6" |

### R2 Findings Applied (v0.2 → v0.3)

| Ref | Finding | Resolution |
|---|---|---|
| R2-F1 | `PcsProTrigger` members not enumerated — naming drift risk | All 11 trigger members named explicitly in scope: Launch, LoginDetected, CredentialsEntered, SearchTriggered, SpinnerGone, MatchOpened, ChangeMatch, Stop, Timeout, UnexpectedDialog, Retry |
| R2-F2 | F-SC-5 "elapsed timeouts" implies timer ownership Stateless doesn't have | F-SC-5 rewritten: tests verify `Fire(PcsProTrigger.Timeout)` from each timed state → `Error`; timer management explicitly deferred to HLPS-002 |
| R2-F3 | Program.cs scope left Blazor middleware ambiguous | Explicit "no Blazor middleware until HLPS-003" scope note added to host bullet |
| R2-F4 | State machine class unnamed — incompatible implementations risk | Named as `PcsProStateMachine`; minimal public surface stated: `CurrentState` property + `OnTransitioned` callback |
| R2-F5 | "Guard tests" referenced without defined guards | Changed to "transition constraint tests (per PRD §6)" |
| R2-F6 | bUnit entry lacked clarity about deferred usage | Added parenthetical: "scaffolding only — first used in HLPS-003" |
| R2-F7 | Timeout constants location unspecified — risk of appsettings.json anti-pattern | Added "compile-time `const` or `static readonly` values in Core, not configuration" |

### R3 Finding Applied (v0.3 → v0.4)

| Ref | Finding | Resolution |
|---|---|---|
| R3-F1 | `PcsProStateMachine` public surface omitted `Fire(PcsProTrigger)` — F-SC-3/4/5 tests and HLPS-002 need to know this entry point exists and its rejection semantics | Added `void Fire(PcsProTrigger trigger)` to stated public surface with explicit note that invalid transitions throw `InvalidOperationException` (Stateless default) |
