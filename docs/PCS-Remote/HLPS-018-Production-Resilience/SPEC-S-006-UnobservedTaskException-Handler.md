# SPEC-S-006 — TaskScheduler.UnobservedTaskException Handler

| Field        | Value |
|--------------|-------|
| **Status**   | APPROVED |
| **Step**     | S-006 |
| **Author**   | Agent |
| **Created**  | 2026-05-03 |
| **Governs**  | IS-018 S-006; HLPS-018 SC9 |
| **Depends**  | S-005 (Serilog sink — DONE) |

---

## Context

All 23 existing `async void` event handlers in the codebase already carry boundary try-catch blocks that log via Serilog and prevent exceptions from propagating to the synchronisation context. The IS-018 pre-implementation audit confirmed this.

However, if any future `async void` handler or fire-and-forget Task omits this pattern, an unobserved `Task` exception would silently terminate the process. S-006 adds a supplementary safety net by registering a `TaskScheduler.UnobservedTaskException` handler that logs the exception and marks it as observed.

Because S-005 has wired a Serilog sink that routes Warning+ messages to the debug panel, any exception caught by this handler will be visible to the operator in real time.

---

## Requirements

### R1 — Register handler before host starts

The `TaskScheduler.UnobservedTaskException` event handler must be registered in `PcsRemote.TrayHost/Program.cs` **before** `app.Run()` is called. This ensures all unobserved exceptions during host lifetime are caught.

**Acceptance Criteria:**

- **AC-1:** The handler is registered inside the existing `try` block in Program.cs, after the builder is configured but before `app.Run()`.
- **AC-2:** The handler logs the exception at `Error` level via Serilog with a structured message including the exception detail.
- **AC-3:** The handler calls `e.SetObserved()` to prevent process termination.

### R2 — Log format

The logged message must follow Serilog structured logging conventions per project standards.

**Acceptance Criteria:**

- **AC-4:** The log message uses a structured message template (no string interpolation) at `Error` level, with the exception object passed as the first argument so Serilog renders it with full stack trace.

### R3 — AppDomain.UnhandledException handler

As an additional defence layer, register `AppDomain.CurrentDomain.UnhandledException` to log fatal unhandled exceptions before the process terminates. This catches synchronous exceptions that escape the top-level try-catch (e.g., from native interop or finalizer threads).

> **Governance Override:** HLPS-018 originally excluded `AppDomain.UnhandledException` as out of scope. The user explicitly requested its inclusion during planning. This requirement supersedes that exclusion.

**Acceptance Criteria:**

- **AC-5:** The handler is registered alongside the TaskScheduler handler, before `app.Run()`.
- **AC-6:** When `ExceptionObject` is an `Exception`, the handler logs at `Fatal` level via a structured message template, with the exception passed for full stack trace rendering. When `ExceptionObject` is not an `Exception`, see AC-7.
- **AC-7:** The handler must defensively handle the case where `UnhandledExceptionEventArgs.ExceptionObject` is not an `Exception` (it is typed as `object`). Non-Exception payloads must be logged with a descriptive message at `Fatal` level. The handler must never throw.
- **AC-8:** The handler calls `Log.CloseAndFlush()` to ensure the fatal message is written before process termination.

---

## Test Cases

### TC-1 — UnobservedTaskException is logged and observed

**Setup:** Create a faulted Task that goes out of scope without observation.  
**Action:** Force GC collection (`GC.Collect()` + `GC.WaitForPendingFinalizers()` + `GC.Collect()`) to trigger the UnobservedTaskException event.  
**Assert:** The handler was invoked, the exception was logged, and `e.Observed` is `true` after handler execution.

**Note:** This test registers and unregisters the handler directly on `TaskScheduler.UnobservedTaskException` to avoid test pollution. The test does NOT verify Serilog output — it verifies the handler's observable side effects (setting Observed = true). Serilog logging is verified by the sink tests in S-005.

### TC-2 — Handler registration placement (structural)

**Verify:** By code inspection or a targeted test that both the `TaskScheduler.UnobservedTaskException` handler (R1) and the `AppDomain.CurrentDomain.UnhandledException` handler (R3) are registered in Program.cs before `app.Run()`. This is a structural/review verification rather than a runtime test.

### TC-3 — AppDomain.UnhandledException handler logs at Fatal and flushes

**Setup:** Invoke the AppDomain handler delegate directly with a test exception.  
**Assert:** The handler logs at `Fatal` level with the exception. Verify `Log.CloseAndFlush()` was called by substituting `Log.Logger` with a mock or wrapper that records the call — a test Serilog sink cannot observe flush completion.

### TC-4 — Async void boundary try-catch audit (structural)

**Verify:** By code inspection that each new `async void` event handler introduced by S-002 and S-004 carries a boundary try-catch that logs via Serilog, consistent with the pattern confirmed by the IS-018 pre-implementation audit. Verification of S-007 handlers is deferred to the S-007 delivery gate. This is a code-review gate.

---

## Out of Scope

- Modifying existing `async void` handlers (already correct per IS-018 audit).
- Adding try-catch to any specific handler (they already have it).
- Testing that the process does NOT terminate (that's a process-level integration concern, not unit-testable).

---

## Review History

| Round | Reviewers | Outcome | Notes |
|-------|-----------|---------|-------|
| R1 | GPT 5.4, Sonnet 4.6 | REVISE | GPT: 3 findings (1H, 1M, 1L). Sonnet: 5 findings (2H, 2M, 1L). Accepted: defensive non-Exception handling in AppDomain handler (GPT-F1), governance override note for R3 (Sonnet-F1), TC-3 must verify CloseAndFlush (Sonnet-F2), TC-2 must name both handlers (Sonnet-F3), add TC-4 for async void audit (Sonnet-F4), fold AC-4/AC-5 overlap (Sonnet-F5), reword ACs to behavioral (GPT-F3). Deferred to Delivery: TC-1 determinism split (GPT-F2). |
| R2 | GPT 5.4, Sonnet 4.6 | — | GPT: 1 finding (1L — rejected, AC-4 is already behavioral). Sonnet: 3 findings (2M, 1L). Accepted: AC-6 conditional scope qualifier (Sonnet R2-F1), TC-3 CloseAndFlush verification mechanism (Sonnet R2-F2), TC-4 S-007 deferral note (Sonnet R2-F3). |
