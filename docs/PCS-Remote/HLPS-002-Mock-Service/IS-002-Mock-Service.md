# IS-002: Mock Automation Service — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-002-Mock-Service.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-10 |
| **HLPS** | HLPS-002-Mock-Service.md (APPROVED v0.3) |
| **Branch target** | `master` |

---

## Overview

This IS breaks HLPS-002 into seven atomic steps. Each step is independently buildable, testable, and mergeable. Steps are ordered to maintain a compilable state at all times and to introduce complexity incrementally.

---

## Steps

### S-001 — Project Scaffold

**What:** Create `PcsRemote.Automation.Mock` (class library) and `PcsRemote.Automation.Mock.Tests` (MSTest test project). Add both to the solution. Configure project references: Mock references `PcsRemote.Core`; Tests references Mock.

**Why:** Satisfies the structural prerequisite for all downstream steps. Without the projects, nothing can be built or tested. Addresses M-SC-1 prerequisite.

**Dependencies:** Requires HLPS-001 (S-001) — solution file and `PcsRemote.Core` must exist on master.

**Verification intent:** `dotnet build` exits 0 with zero warnings. `dotnet test` exits 0 (no tests yet, empty pass). Both projects appear in `dotnet sln list`.

---

### S-002 — Options Model and Service Skeleton

**What:** Create a `MockPcsProOptions` class capturing all configurable behaviour: per-transition simulated delays, error injection probability, RNG seed (nullable — absent means system random), image variation probability, and fake match count. Create `MockPcsProAutomationService` implementing `IPcsProAutomationService` with stub method bodies that throw `NotImplementedException`. Wire constructor injection of `IOptions<MockPcsProOptions>` and `ILogger`.

**Why:** Establishes the shape of the configurable behaviour surface (M-SC-8) and forces all options to be explicit before any logic is written. The skeleton makes the project compile and lets tests be written against the interface. Satisfies M-SC-8 prerequisite.

**Dependencies:** S-001.

**Verification intent:** `dotnet build` exits 0. The service resolves correctly when constructed with default options. No tests required at this step beyond build success.

---

### S-003 — Core State Machine Lifecycle

**What:** Implement all lifecycle methods (`LaunchAndLoginAsync`, `SelectMatchAsync`, `LoadMatchAsync`, `RetryAsync`, `StopAsync`) with: configurable `Task.Delay` waits before each trigger, `CancellationToken` propagation into delays, `StateChanged` event firing after each transition, single-caller enforcement via a `SemaphoreSlim` guard (concurrent second call throws `InvalidOperationException`), and `StopAsync` resetting state to `NotRunning`. Methods must complete their trigger-to-event path within 500ms (exclusive of the configurable dwell delay). Include basic structured `ILogger` calls at `Debug`/`Information` level throughout lifecycle methods to aid debuggability during this and subsequent steps; these will be finalised in S-007.

**Why:** Delivers the heart of the mock: realistic state progression that downstream web and SignalR components can consume. Addresses M-SC-1, M-SC-2, M-SC-8, M-SC-9, M-SC-10, M-SC-11, M-SC-12.

**Dependencies:** S-002.

**Verification intent:** Unit tests verify: happy-path state sequence from `NotRunning` through `MatchLoaded`; `StateChanged` events fire in exact order; cancellation mid-delay throws `OperationCanceledException` and leaves state unchanged; concurrent call throws `InvalidOperationException`; `StopAsync` returns state to `NotRunning`; trigger-to-event elapsed time is <500ms at zero delays.

> **Risk note:** S-003 is the highest-complexity step and is the single verification gate for 7 of 12 success criteria. If a timing or concurrency test fails, address it before proceeding to S-004 — downstream steps modify the same class.

---

### S-004 — Error Injection

**What:** Extend `MockPcsProAutomationService` to support error injection: after each simulated delay, evaluate a configurable probability using an RNG that is seeded from options if a seed is provided (deterministic) or uses system random if not. On a triggered error, transition to the `Error` state with a descriptive reason string and fire `StateChanged`.

**Why:** Enables downstream components (HLPS-003+) to exercise error recovery flows without a real PCS Pro. Addresses M-SC-6.

**Dependencies:** S-003.

**Verification intent:** Unit tests verify: with seed and probability=1.0, every lifecycle call transitions to `Error` with a non-empty reason; with probability=0.0, no error transitions occur; same seed produces identical error sequence across test runs.

---

### S-005 — Match Data

**What:** Implement `GetTodaysMatchesAsync` to return a list of realistic `MatchInfo` records (configurable count from options) with team names, match type, and match date stamped with `DateTime.Today` at call-time — not a fixed literal.

**Why:** Provides the data layer the web control panel (HLPS-003) will consume. Addresses M-SC-3.

**Dependencies:** S-004 (full chain S-001 → S-004; options model prerequisite is S-002).

**Verification intent:** Unit test asserts all returned records carry today's date (i.e., `MatchDate == DateOnly.FromDateTime(DateTime.Today)` at the moment of the call); calling with count=0 returns empty list.

---

### S-006 — Scoreboard Image Generation

**What:** Implement `CaptureScoreboardImageAsync` to return a valid JPEG byte array generated using `System.Drawing.Common`: a solid-colour rectangle with overlay text (score placeholder). On each call, evaluate the image variation probability from options — if triggered, vary the colour or text so the returned bytes differ from the previous call. Maintain a simple last-image cache to support the probability=0.0 (identical) and probability=1.0 (always different) test assertions.

**Why:** Provides the scoreboard image stream the hub (HLPS-004) will consume. Addresses M-SC-4.

**Dependencies:** S-004 (full chain S-001 → S-004; options model prerequisite is S-002).

**Verification intent:** Unit tests verify: returned bytes begin with JPEG magic bytes (`FF D8 FF`); with probability=1.0 no two consecutive calls return identical bytes; with probability=0.0 all calls return identical bytes.

> **Risk note:** `System.Drawing.Common` requires GDI+ (Windows-only, per C-1). If CI runners become Linux-hosted, this step must be revisited (per HLPS-002 §2).

---

### S-007 — Serilog Integration and DI Registration

**What:** Finalise structured `ILogger` calls throughout `MockPcsProAutomationService` (the basic calls introduced in S-003 are confirmed and completed at appropriate levels: `Debug` for routine state steps, `Warning` for error injection, `Information` for major lifecycle events). Add a DI registration extension method to `PcsRemote.Automation.Mock` that reads `PcsPro:UseMock` and registers either `MockPcsProAutomationService` or a compile-only placeholder for the real type. Wire this registration into `PcsRemote.Web`'s `Program.cs`.

**Why:** Makes logging observable and testable (M-SC-7). Enables the web host to select the mock at startup via configuration (M-SC-5). Completes the HLPS-002 deliverable.

**Dependencies:** S-003–S-006 (service must be fully implemented before logging and DI are finalised).

**Verification intent:** Unit tests inject an in-memory Serilog sink and assert at least one `Information`-or-above event is emitted per state transition. Integration test builds a `ServiceProvider` with `PcsPro:UseMock=true`, resolves `IPcsProAutomationService`, and asserts the instance is `MockPcsProAutomationService`. `dotnet build` exits 0 for the full solution. `dotnet test` exits 0 with all tests passing.

---

## Success Criteria Coverage

| HLPS SC | Covered By |
|---|---|
| M-SC-1 | S-003 |
| M-SC-2 | S-003 |
| M-SC-3 | S-005 |
| M-SC-4 | S-006 |
| M-SC-5 | S-007 |
| M-SC-6 | S-004 |
| M-SC-7 | S-007 |
| M-SC-8 | S-002, S-003 |
| M-SC-9 | S-003 |
| M-SC-10 | S-003 |
| M-SC-11 | S-003 |
| M-SC-12 | S-003 |

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | Claude Sonnet 4.6, GPT-4.1 | NEEDS REVIEW / APPROVE — 1 HIGH + 2 MEDIUM + 2 LOW (Sonnet); 2 LOW (GPT) |
| R2 | 2026-04-10 | Claude Sonnet 4.6 | **APPROVED** — all R1 findings verified resolved; 0 regressions |

### R1 Findings Applied (v0.1 → v0.2)

| Ref | Finding | Disposition | Resolution |
|---|---|---|---|
| Sonnet HIGH-1 | S-005/S-006 declare S-002-only dependency while modifying S-003/S-004 class | Accept | S-005 and S-006 dependencies updated to S-004 (full chain) |
| Sonnet MED-2 | S-005 verification references undeclared "mocked date provider" abstraction | Accept | Verification simplified to same-call today's-date assertion; consecutive-days scenario removed |
| Sonnet MED-3 | S-007 "replace placeholder logging" — no prior step establishes them | Accept | S-003 What updated to include basic ILogger calls; S-007 changed to "finalise" |
| Sonnet LOW-4 | System.Drawing.Common Linux CI risk not echoed in S-006 | Accept | Risk note added to S-006 |
| Sonnet LOW-5 | S-003 7-criteria concentration unacknowledged | Accept | Risk note added to S-003 |
| GPT LOW-1 | S-005/S-006 cache dependency ambiguity | Accept | Resolved by S-004 dependency fix (same root cause as Sonnet HIGH-1) |
| GPT LOW-2 | S-007 verification ambiguity | Accept | Addressed by S-007 "finalise" rewrite and S-003 logging mention |
