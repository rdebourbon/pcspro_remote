# IS-001: Foundation Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-001-Foundation.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-10 |
| **HLPS** | HLPS-001-Foundation.md (APPROVED v0.4) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |

---

## Overview

This IS breaks HLPS-001 into 5 atomic, ordered steps executed on separate feature branches and squash-merged to master on approval. Each step is independently verifiable and builds directly on the previous.

---

## Steps

### S-001: Repository Scaffold and Tooling

**What changes:** The empty repository gains its structural skeleton — the solution file, all project stubs (src and test, as defined in PROJECT-CONTEXT §6), and all tooling configuration files. All NuGet dependencies required by the HLPS are installed at this stage.

**Why:** Nothing else can be built or tested until a compiling solution structure exists. Addresses the HLPS problem "no code can be written or tested." Delivers F-SC-1, F-SC-8, F-SC-9, F-SC-10.

**Dependencies:** None.

**Verification intent:** Solution builds cleanly with zero warnings. Formatting check passes. All projects are present and correctly wired into the solution. README and copilot-instructions content is meaningful.

---

### S-002: Core Domain Model Types

**What changes:** The foundational domain types are defined in `PcsRemote.Core` — the state enumeration, the trigger enumeration, and the match data records. These form the shared vocabulary for all subsequent layers.

**Why:** No service interface or state machine can be defined without shared types. Addresses the HLPS problem "no shared types exist." Prerequisite for S-003 and S-004.

**Dependencies:** S-001.

**Verification intent:** Core project compiles cleanly. All types match the canonical definitions in PRD §5a and §6.

---

### S-003: Automation Service Contract

**What changes:** `IPcsProAutomationService` is defined in `PcsRemote.Core` — the sole architectural seam between the web layer and the automation layer. Project references connecting the automation projects to Core are established.

**Why:** The interface is the dependency inversion boundary that allows Mock (HLPS-002) and Real (HLPS-006) implementations to be substituted transparently. Delivers F-SC-7.

**Dependencies:** S-002 (interface signature references domain types).

**Verification intent:** Interface compiles. Contract verified by code review against PRD §5a. Automation project references to Core are in place.

---

### S-004: State Machine and Unit Tests

**What changes:** `PcsProStateMachine` is implemented in `PcsRemote.Core`, wrapping the Stateless library to model the full PCS Pro application lifecycle. Compile-time timeout constants are defined. A unit test suite is written in `PcsRemote.Core.Tests` covering the complete state machine behaviour.

**Why:** The state machine is the domain core — every automation sequence in every subsequent HLPS flows through it. Delivers F-SC-2, F-SC-3, F-SC-4, F-SC-5.

**Dependencies:** S-002 (requires state and trigger types). S-001 (test project must exist).

**Verification intent:** All unit tests pass. Tests cover: every valid transition per PRD §6; invalid transition rejection; timeout constant values; and that firing the `Timeout` trigger from each timed state produces the correct `Error` transition.

---

### S-005: Minimal Web Host and Initial Commit

**What changes:** `Program.cs` in `PcsRemote.Web` is implemented as a minimal ASP.NET Core host with structured logging configured. No Blazor middleware is included at this stage. The complete foundation is committed to master.

**Why:** Validates that the web project is correctly scaffolded and logging is wired end-to-end from the very first commit (per constraint C-10). Delivers F-SC-6.

**Dependencies:** S-001 through S-004 (all foundation pieces must be complete before the initial master commit).

**Verification intent:** Application starts without error; console output contains at least one structured log entry; `logs/` directory contains a rolling log file with at least one entry. No Blazor middleware present. All F-SC-1 through F-SC-10 pass on master.

---

## Dependency Map

```
S-001 (Scaffold)
  └── S-002 (Domain Types)
        ├── S-003 (Interface) ──────┐
        └── S-004 (State Machine)   │
                                    ▼
                              S-005 (Host + Commit)
```

---

## Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| — | No IS-specific unknowns. All shared unknowns resolved in PROJECT-CONTEXT. | — | — |

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | GPT 5.4, Claude Sonnet 4.6, Claude Opus 4.6 | REVISE / APPROVE / APPROVE — 1 MEDIUM + 1 LOW accepted; 2 MEDIUM rejected |

| R2 | 2026-04-10 | GPT 5.4 | APPROVE — F1 resolved, F2 rejection accepted, F3 deferral accepted |

### R1 Findings Applied (v0.1 → v0.2)

| Ref | Finding | Disposition | Resolution |
|---|---|---|---|
| F1 | S-005 "produces log output" doesn't cover both sinks | Accept (MEDIUM) | S-005 verification now explicitly requires console output AND rolling log file |
| F2 | S-001 not truly atomic — bundles too much | Reject — 2/3 reviewers approved atomicity; partial scaffold delivers zero independent value | No change |
| F3 | Documentation verification under-specified vs F-SC-8/9/10 | Defer to JIT Spec — IS abstraction level is correct; JIT Spec will specify concretely | No change |
| F4 | Dependency map omits S-003 → S-005 edge | Accept (LOW) | Map updated to show S-003 as direct parent of S-005 |
| F5 | S-004 S-001 dependency is transitive (redundant) | Reject — explicit is clearer for the implementer | No change |
