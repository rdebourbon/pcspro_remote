# SPEC-S-005 — Serilog Sink to Debug Panel

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **Author**  | Agent |
| **Created** | 2026-05-03 |
| **Governs** | IS-018 S-005 |
| **Depends** | None |

---

## Objective

Route Warning-level-and-above Serilog log messages into the existing `IAutomationLogService` so they appear in the web UI debug panel. This gives operators immediate visibility into application errors without server access.

---

## Requirements

### R1 — Add `Warning` to `AutomationLogOutcome`

Add a `Warning` member to the `AutomationLogOutcome` enum, positioned between `Success` and `Failure` to maintain semantic ordering: `Info`, `Success`, `Warning`, `Failure`.

### R2 — Create `AutomationLogSerilogSink`

Create a new class in `PcsRemote.Web` that implements `Serilog.Core.ILogEventSink`. The sink:

- Receives a reference to `IAutomationLogService` via constructor injection.
- In its `Emit(LogEvent)` method, if the event level is below `Warning`, return immediately (internal guard — the pipeline-level filter is a secondary defence).
- Renders the log event's message template to a string. If the `LogEvent.Exception` is non-null, appends the exception type and message (e.g., `" [ExceptionType: message]"`) to provide diagnostic context.
- Calls `AddEntry(renderedMessage, outcome)` with the appropriate outcome mapping.
- Maps Serilog levels: `Warning` → `AutomationLogOutcome.Warning`; `Error` and `Fatal` → `AutomationLogOutcome.Failure`.
- Must not throw exceptions — any failure in `AddEntry` is swallowed to avoid disrupting the logging pipeline.

### R3 — Wire sink into Serilog pipeline

Update `Program.cs` to use the three-argument `UseSerilog((context, services, config) => ...)` overload so that `IAutomationLogService` can be resolved from DI. Add `.WriteTo.Sink(new AutomationLogSerilogSink(...), restrictedToMinimumLevel: Warning)` using the resolved service. Existing Console and File sinks must be preserved.

### R4 — Add CSS rule for Warning outcome

Add `.automation-log-outcome-warning` CSS class in `app.css` with amber/yellow styling, consistent with the existing outcome badge pattern.

### R5 — Update existing tests

- `AutomationLogOutcomeTests`: Update to assert 4 members: `Info`, `Success`, `Warning`, `Failure`.
- The `AutomationLogDisplay.razor` already uses `@entry.Outcome.ToString().ToLowerInvariant()` for CSS class generation, so `Warning` entries will automatically get the `automation-log-outcome-warning` class — no Razor changes needed.

---

## Acceptance Criteria

| AC | Description |
|----|-------------|
| AC-1 | `AutomationLogOutcome` has exactly 4 members: Info, Success, Warning, Failure |
| AC-2 | `AutomationLogSerilogSink` implements `ILogEventSink` and is in `PcsRemote.Web` namespace |
| AC-3 | Serilog `Warning` log events appear in the debug panel with `Warning` outcome |
| AC-4 | Serilog `Error` and `Fatal` log events appear with `Failure` outcome |
| AC-5 | Serilog `Information` and `Debug` log events do NOT appear in the debug panel |
| AC-6 | The sink does not throw exceptions when `AddEntry` fails |
| AC-7 | The 200-entry ring buffer naturally bounds entries under rapid error conditions |
| AC-8 | `.automation-log-outcome-warning` CSS rule exists with amber/yellow styling |
| AC-9 | Build succeeds with 0 errors, 0 warnings |
| AC-10 | All existing tests pass, updated enum test asserts 4 members |

---

## Test Cases

| TC | Test | Verifies |
|----|------|----------|
| TC-1 | Emit Warning-level event → `AddEntry` called with `AutomationLogOutcome.Warning` | AC-3 |
| TC-2 | Emit Error-level event → `AddEntry` called with `AutomationLogOutcome.Failure` | AC-4 |
| TC-3 | Emit Fatal-level event → `AddEntry` called with `AutomationLogOutcome.Failure` | AC-4 |
| TC-4 | Emit Information-level event → `AddEntry` NOT called | AC-5 |
| TC-5 | `AddEntry` throws → sink does not propagate exception | AC-6 |
| TC-6 | Emit Warning event with structured message template → rendered string contains resolved property values | AC-3 |
| TC-7 | Enum has 4 members in correct order | AC-1, AC-10 |
| TC-8 | Emit Error event where LogEvent.Exception is non-null → AddEntry called with message containing exception suffix; outcome is Failure | AC-4 |

---

## Review History

| Round | Panel | Outcome | Notes |
|-------|-------|---------|-------|
| R1 | GPT 5.4, Sonnet 4.6 | REVISE | GPT: 2 findings (1M, 1L). Sonnet: 5 findings (2H, 1M, 2L). Accepted 4: internal level guard in sink (F-001), exception detail rendering (F-002/GPT-F-001), preserve existing sinks (F-004). Deferred 2: order assertion detail (F-003), doc annotation (GPT-F-002). Rejected 1: AC-9 scoping (F-005). |
| R2 | GPT 5.4, Sonnet 4.6 | REVISE (1 APPROVE, 1 REVISE) | GPT: APPROVE. Sonnet: 2 findings (1M, 1L). Accepted 1: split TC-6 into TC-6 (structured rendering) and TC-8 (exception detail) (F-001). Deferred 1: exception suffix GetType().Name vs FullName (F-002, delivery detail). |
