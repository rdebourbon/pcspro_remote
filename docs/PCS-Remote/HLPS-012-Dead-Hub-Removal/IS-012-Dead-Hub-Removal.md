# IS-012 — Remove Dead SignalR Hub Infrastructure

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **HLPS**    | HLPS-012-Dead-Hub-Removal (APPROVED) |
| **Author**  | Copilot |
| **Created** | 2026-04-21 |

---

## Overview

This IS delivers HLPS-012 in two atomic steps. The ordering is strict: the safety-net behaviour must be relocated and verified **before** the broadcaster that currently hosts it is deleted.

---

## S-001 — Relocate Stale-Description Safety-Net

**What:** Extract the stale-description clearing logic from `OperationInProgressBroadcaster` into a new, lightweight `IHostedService` in the `PcsRemote.Web` layer. This new service subscribes to the automation service's state-changed event and, when the coordinator is idle, calls `ClearStaleDescription()`. It has no dependency on any SignalR hub or `IHubContext`.

**Why:** Satisfies HLPS-012 C-1 (preserve the only behavioural side-effect) and SC-4 (preserved and tested). Must be completed before S-002 deletes the broadcaster.

**Dependencies:** None.

**Verification intent:** A unit test asserts that when the state-changed event fires and the coordinator reports idle, `ClearStaleDescription()` is invoked. A second assertion confirms it is NOT called when the coordinator reports busy. The existing test suite continues to pass (no regressions from adding the new service).

---

## S-002 — Delete Dead Hub Infrastructure

**What:** Remove the hub (`PcsProHub`), all four broadcasters, `PcsProHubConstants`, hub-specific DI registrations (`AddHostedService`, `MapHub`), mirrored registrations in the test factory, all hub/broadcaster unit test files, and any now-unused package references. The `AddSignalR()` call is removed only if build and E2E verification confirms Blazor Server functions without it; otherwise it is retained standalone as the documented fallback (HLPS-012 R-1). This step resolves HLPS-012 U-1.

**Why:** Satisfies HLPS-012 SC-1 (dead files deleted), SC-2 (DI registrations removed), SC-3 (endpoint unmapped), SC-5 (dead tests deleted), SC-6 (clean build), SC-7 (remaining tests pass with documented delta), SC-8 (cross-circuit sync verified by E2E tests).

**Dependencies:** S-001 must be complete. The safety-net logic must be independently hosted before its current container is deleted.

**Verification intent:** The solution builds with 0 errors and 0 warnings. All remaining tests pass. The before/after test count delta is documented. E2E tests (`OperationalUxE2ETests` TC-1 through TC-4) confirm cross-circuit state synchronisation is unaffected.

---

## Review History

### R1 — 2026-04-21
**Panel:** Claude Opus 4.7, GPT 5.4

| Reviewer | Decision | Key Findings |
|----------|----------|--------------|
| Opus 4.7 | APPROVE | INFOs only: ordering correct, coverage complete, atomicity sound. LOWs: S-002 deletion order and test delta recording deferred to JIT Spec. |
| GPT 5.4 | REQUEST CHANGES | HIGH F-01: S-002 treats AddSignalR removal as unconditional — must be conditional per HLPS R-1 fallback. |

**Resolution:** Accept F-01 — amended S-002 to separate hub-specific registrations (unconditional) from AddSignalR removal (conditional on build/E2E verification).

### R2 — 2026-04-21
**Panel:** GPT 5.4 (Opus 4.7 approved in R1)

| Reviewer | Decision | Notes |
|----------|----------|-------|
| GPT 5.4 | APPROVE | R1 fix verified — AddSignalR conditionality correctly expressed. No regressions, no new findings. |

**Result:** Unanimous approval (2/2). Document status → APPROVED.

---

*End of IS-012.*
