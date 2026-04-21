# SPEC-S-009 — Integration and E2E Tests (IS-011)

**Version:** 0.1 · **Status:** APPROVED · **Step:** S-009

## Scope

Add Playwright E2E tests to verify cross-feature, multi-browser behaviour for HLPS-011 operational UX features that cannot be covered by bUnit alone.

## Test Cases

### TC-1 — Dismiss multi-browser
One browser dismisses an error → all connected browsers see error banner clear and state return to NotRunning.

### TC-2 — Operation status multi-browser
One browser triggers an operation → all browsers see `operation-status-banner` with the description. Late-joining browser sees current status.

### TC-3 — Automation log multi-browser
Log entries emitted during an automation operation are visible in the `automation-log-display` of all connected browsers. Late-joining browser receives buffer history.

### TC-4 — Debug section toggle and PIN gate
Debug section toggle opens/closes the collapsible; PIN entry grants access; incorrect PIN shows error.

### TC-5 — Coexistence regression
All 15 existing E2E tests continue to pass alongside the 4 new tests. Validated by the full `dotnet test` run, not a dedicated test method.

## Out of Scope

- Latency measurement (SC-4) — network conditions vary; covered by existing bUnit snapshot-before-subscribe and broadcaster tests.
- Late-joiner combined payload — covered by hub unit tests (PcsProHubTests.cs).

## Verification

All E2E tests pass. All existing 696+ tests continue to pass.
