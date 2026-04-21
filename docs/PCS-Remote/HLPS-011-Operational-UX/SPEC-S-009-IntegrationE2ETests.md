# SPEC-S-009 — Integration and E2E Tests (IS-011)

**Version:** 0.4 · **Status:** APPROVED · **Step:** S-009

## Scope

Add Playwright E2E tests to verify cross-feature, multi-browser behaviour for HLPS-011 operational UX features that cannot be covered by bUnit alone. These tests use the established Playwright infrastructure (WebApplicationFactory + Chromium headless) to exercise the full Blazor Server → SignalR → browser pipeline.

## Requirements

### R-1 — Dismiss multi-browser (SC-1, SC-7)
A test must verify that when one browser dismisses an error, all connected browsers see the error banner clear and state return to NotRunning. The Retry button must remain visible and enabled after dismiss.

### R-2 — Operation status banner multi-browser with late-joiner (SC-2, SC-7)
A test must verify that when an operation is in progress with a description, the status banner is visible in an already-connected browser AND in a browser that connects after the operation starts (late-joiner). The banner must clear when the operation completes.

### R-3 — Automation log multi-browser with late-joiner (SC-5, SC-6)
A test must verify that log entries emitted during automation are visible in the debug section log panel. A browser connecting after entries are emitted must receive the buffer history via the hub snapshot. SC-4 latency assertion (≤ 3s) is deferred — see Out of Scope.

### R-4 — Debug section toggle (SC-3)
A test must verify that the debug section toggle opens and closes the collapsible content area. When no PIN is configured (test default), clicking the toggle must expand immediately.

### R-5 — Coexistence regression (SC-7)
All existing E2E tests (15 at time of writing) must continue to pass alongside the new tests. This is validated by the full test run, not a dedicated test method.

## Acceptance Criteria

| ID | Criterion |
|----|-----------|
| AC-1 | TC-1 (dismiss multi-browser) passes; Retry button remains available post-dismiss |
| AC-2 | TC-2 (operation status banner with late-joiner) passes |
| AC-3 | TC-3 (automation log with late-joiner snapshot) passes |
| AC-4 | TC-4 (debug section toggle) passes |
| AC-5 | All pre-existing E2E tests continue to pass (no regressions) |
| AC-6 | All tests across all projects pass |
| AC-7 | Build produces 0 errors, 0 warnings |

## Out of Scope

- Latency measurement (SC-4 ≤ 3s) — network conditions vary; covered by existing bUnit snapshot-before-subscribe and broadcaster tests.
- Late-joiner combined payload as a dedicated test — covered by hub unit tests (`PcsProHubTests.cs`). Coverage verified transitively via AC-6 (full test run).
- PIN gate E2E test — requires per-test factory reconfiguration; covered by bUnit `DebugSectionTests`. Coverage verified transitively via AC-6 (full test run).

## Branch

`feature/IS-011-S-009-integration-e2e-tests`
