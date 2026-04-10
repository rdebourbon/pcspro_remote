# Copilot Instructions — PCS Remote

This document defines the coding standards, architectural rules, and conventions for the PCS Remote project. All Copilot suggestions must conform to these rules.

---

## Namespace Convention

All types use the `PcsRemote.*` namespace hierarchy:

- `PcsRemote.Core` — domain model, state machine, service contracts
- `PcsRemote.Automation` — FlaUI automation implementation
- `PcsRemote.Automation.Mock` — mock automation service
- `PcsRemote.Web` — Blazor Server UI, SignalR hubs, DI wiring
- `PcsRemote.TrayHost` — Windows Forms tray host

---

## Language and Runtime

- **C# 12**, **.NET 8 LTS**
- Target framework: `net8.0` for platform-neutral projects; `net8.0-windows` for projects requiring Win32 APIs (Web, Automation, TrayHost)

---

## Null Safety

- Nullable reference types are **enabled** globally via `Directory.Build.props`.
- The `!` null-forgiving operator is **forbidden** without an inline comment explaining why the null-dereference is provably impossible.
- Prefer null checks or pattern matching over suppression.

---

## Error Handling

- Use exceptions for unexpected or unrecoverable states — do not swallow exceptions silently.
- State machine violations throw `InvalidOperationException` with a descriptive message.
- Do not catch `Exception` at call sites unless you are at a top-level boundary (e.g., ASP.NET middleware, tray host message loop).

---

## Logging

- All logging uses **Serilog** with structured message templates.
- **Never** use string interpolation in log calls: `Log.Information("User {UserId} logged in", userId)` ✅ — `Log.Information($"User {userId} logged in")` ❌
- Named properties must describe the data, not the variable: `{MatchId}` not `{id}`.
- Log levels: `Verbose` for trace/diagnostic, `Debug` for development detail, `Information` for key lifecycle events, `Warning` for recoverable issues, `Error` for failures, `Fatal` for unrecoverable crashes.

---

## Testing

- Test framework: **MSTest 3.x** with **FluentAssertions > 8.0**.
- Test method naming convention: `MethodName_Scenario_ExpectedResult`
  - Example: `Fire_InvalidTrigger_ThrowsInvalidOperationException`
- **No `Thread.Sleep`** in tests — use async/await with `CancellationToken` or test doubles.
- bUnit is used for Blazor component tests in `PcsRemote.Web.Tests`.
- Test projects are empty stubs until the relevant production step is implemented.

---

## Code Style

- Matches `.editorconfig` — refer to it for formatting rules.
- Prefer `var` when the type is apparent from the right-hand side.
- **Allman braces**: opening brace always on a new line.
- `using` directives: System namespaces first, then alphabetical; no `using` inside namespace declarations.
- Expression-bodied members are acceptable for simple single-expression properties and methods; avoid for multi-line logic.

---

## Architecture Rules

- **`PcsRemote.Core` has zero dependencies** on `PcsRemote.Web`, `PcsRemote.Automation`, `PcsRemote.Automation.Mock`, or `PcsRemote.TrayHost`. Any violation is a build error.
- **Depend on interfaces, not concrete types** at layer boundaries. Concrete types are wired in DI registration only.
- `PcsRemote.Automation.Mock` must implement the same interfaces as `PcsRemote.Automation` — they are interchangeable at the DI boundary.
- The state machine (`PcsProStateMachine`) is a **passive engine** — it has no internal timers. All timeout triggers are fired by the automation service layer.

---

## Commit Strategy

- Small, frequent commits scoped to a single logical change.
- Commit messages use the imperative mood: `Add PcsProStateMachine`, `Fix nullable warning in Core`, `Wire Serilog in Program.cs`.
- Each feature branch corresponds to one IS step (e.g., `feature/S-001-scaffold`).
- Branches are squash-merged to `master` after adversarial review approval.
