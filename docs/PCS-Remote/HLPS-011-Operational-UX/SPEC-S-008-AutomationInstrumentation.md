# SPEC-S-008 — Automation Instrumentation (IS-011)

**Version:** 0.4 · **Status:** APPROVED · **Step:** S-008

## Scope

Instrument both `MockPcsProAutomationService` and `PcsProAutomationService` to emit log entries via `IAutomationLogService` at all operator-visible automation steps. This step makes the log service and UI (delivered in S-006/S-007) operational by providing the data source.

## Requirements

### R-1 — Constructor dependency
Both automation services must accept `IAutomationLogService` as a constructor dependency. The dependency is provided via DI; no service should create its own instance.

### R-2 — Operator-visible instrumentation
Each automation method that represents an operator-visible action must emit at least one `AddEntry` call with an appropriate action description and outcome in both `MockPcsProAutomationService` and `PcsProAutomationService`. The minimum set of instrumented actions:
- Launch, login/credential entry, match loading, match selection
- Scoreboard capture (on-demand), start/stop streaming
- Change match, dismiss, retry
- Error transitions (via the central error handler)

### R-3 — Credential safety (OUX-C-6)
Log entries for the login flow must describe the action (e.g., "Entering credentials…") without including actual credential values (username, password, PIN). Error reason strings emitted in failure entries must be operator-safe and must not contain sensitive exception details.

### R-4 — Background monitoring exclusion and alert inclusion
Individual health-check poll cycles and scoreboard capture loop iterations must NOT generate log entries. Health-check state-change alerts and monitoring-triggered error transitions MUST emit at least one log entry (via the central error handler or alert-handling code path).

### R-5 — Error transition instrumentation
The central error handler method must emit a failure log entry containing an operator-safe error reason string.

### R-6 — Test infrastructure
A no-op `NullAutomationLogService` implementation must be created in Core for test use across all test projects. All existing test constructor calls must be updated to include the new dependency.

### R-7 — E2E factory sync
The E2E test factory must register all services added in S-005 through S-008 to maintain the "keep in step with Program.cs" sync invariant documented in the factory.

## Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | Both services compile with `IAutomationLogService` as a constructor parameter |
| AC-2 | Each operator-visible automation method emits at least one log entry in both `MockPcsProAutomationService` and `PcsProAutomationService` |
| AC-3 | Login flow log entries do not contain credential values |
| AC-4 | Health-check poll cycles and scoreboard capture loop iterations do not emit individual entries |
| AC-5 | Health-check state-change alerts emit at least one log entry |
| AC-6 | Error transitions emit a failure entry with an operator-safe error reason |
| AC-7 | All existing tests pass with `NullAutomationLogService` injected |
| AC-8 | E2E tests pass (factory DI registration complete) |
| AC-9 | Build produces 0 errors, 0 warnings |

## Deviation from IS-011 Verification Intent

IS-011 §S-008 prescribes unit tests that mock `IAutomationLogService` and assert per-method `AddEntry` calls (including credential redaction and poll exclusion). This spec defers those unit tests. Rationale: (1) each instrumented method is a single `AddEntry` call verifiable by code inspection; (2) S-009 E2E tests validate the end-to-end pipeline from instrumentation through broadcaster to browser; (3) the instrumentation is additive (no branching logic) — the risk of a silent regression is low. This is an accepted deviation; if future changes introduce conditional instrumentation logic, targeted unit tests should be added at that time.

## Out of Scope

- Changes to log entry format or `AutomationLogEntry` record structure.

## Branch

`feature/IS-011-S-008-automation-instrumentation`
