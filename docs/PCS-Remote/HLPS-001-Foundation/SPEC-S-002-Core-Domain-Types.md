# SPEC-S-002: Core Domain Model Types

| Field | Value |
|---|---|
| **Document** | SPEC-S-002-Core-Domain-Types.md |
| **Version** | 0.2 |
| **Status** | APPROVED |
| **Date** | 2026-04-10 |
| **Step** | S-002 |
| **IS** | IS-001-Foundation.md (APPROVED v0.2) |
| **HLPS** | HLPS-001-Foundation.md (APPROVED v0.4) |

---

## 1. Objective

Define the shared domain vocabulary in `PcsRemote.Core`: the state enumeration, trigger enumeration, and value records used by the service contract and state machine. After this step all subsequent layers have a common type language and `PcsRemote.Core` compiles cleanly.

**Traceability:** S-002 directly delivers F-SC-1 (clean compilation). It is prerequisite infrastructure for F-SC-3, F-SC-4, F-SC-5 (state machine, which requires both enums), and F-SC-7 (automation service interface, which uses the record types).

---

## 2. Branch

`feature/S-002-domain-types`

Target: `master`

---

## 3. Types to Define

All types live in the `PcsRemote.Core` namespace. All files reside in `src/PcsRemote.Core/`.

### 3.1 `PcsProState` Enumeration

Models the lifecycle state of the PlayCricket Scorer Pro application. Exactly 8 members, in the order listed:

1. `NotRunning`
2. `Launching`
3. `LoginScreen`
4. `MatchSelection`
5. `MatchSelectionSearching`
6. `MatchSelectionReady`
7. `MatchLoaded`
8. `Error`

Source of truth: HLPS-001 §2, PRD §5a and §6.

### 3.2 `PcsProTrigger` Enumeration

Models the external events and internal signals that drive state transitions. Exactly 11 members, in the order listed:

1. `Launch`
2. `LoginDetected`
3. `CredentialsEntered`
4. `SearchTriggered`
5. `SpinnerGone`
6. `MatchOpened`
7. `ChangeMatch`
8. `Stop`
9. `Timeout`
10. `UnexpectedDialog`
11. `Retry`

Source of truth: HLPS-001 §2, PRD §5a and §6.

### 3.3 `MatchInfo` Record

An immutable reference type with structural equality that identifies a specific match fixture for use by `SelectMatchAsync` (defined in S-003). Carries the minimum data needed to locate and select a match in PCS Pro.

- `MatchId` — string, non-nullable — the PlayCricket fixture identifier used to search for and select the match

### 3.4 `MatchTeams` Record

An immutable reference type with structural equality returned by `GetTeamNamesAsync` (defined in S-003). Holds the names of the two competing teams as read from the loaded match in PCS Pro.

- `HomeTeam` — string, non-nullable
- `AwayTeam` — string, non-nullable

---

## 4. File Layout

One type per file is the conventional layout (not a hard requirement):

- `PcsProState.cs`
- `PcsProTrigger.cs`
- `MatchInfo.cs`
- `MatchTeams.cs`

---

## 5. Constraints

- All types must be in the `PcsRemote.Core` namespace.
- No dependencies on any other project or external NuGet package may be introduced. `PcsRemote.Core` remains dependency-minimal (`Stateless` is the only allowed NuGet reference — installed by S-001, first consumed in S-004).
- Enum member names must exactly match the values defined in §3.1 and §3.2 — they are referenced by name in HLPS documentation and test assertions.
- `MatchInfo` and `MatchTeams` are C# `record` types — immutable reference types with structural equality (positional constructor, value-based `Equals`/`GetHashCode`).

---

## 6. Verification

| Check | Method |
|---|---|
| Solution compiles with zero errors and zero warnings | `dotnet build` |
| `PcsProState` has exactly 8 members with correct names | Code review against §3.1 and PRD §6 |
| `PcsProTrigger` has exactly 11 members with correct names | Code review against §3.2 and PRD §6 |
| `MatchInfo` is a record with non-nullable `MatchId` string property | Code review |
| `MatchTeams` is a record with non-nullable `HomeTeam` and `AwayTeam` string properties | Code review |
| No new project or NuGet package references introduced in Core | `dotnet list src/PcsRemote.Core reference` and `dotnet list src/PcsRemote.Core package` — both unchanged from S-001 |

---

## 7. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` exits 0 with no errors or warnings |
| AC-2 | `PcsProState` enum exists in `PcsRemote.Core` with exactly 8 members matching §3.1 |
| AC-3 | `PcsProTrigger` enum exists in `PcsRemote.Core` with exactly 11 members matching §3.2 |
| AC-4 | `MatchInfo` record exists in `PcsRemote.Core` with a non-nullable `MatchId` string property |
| AC-5 | `MatchTeams` record exists in `PcsRemote.Core` with non-nullable `HomeTeam` and `AwayTeam` string properties |
| AC-6 | `PcsRemote.Core.csproj` project references and NuGet package references are unchanged from S-001 (zero project refs; only `Stateless` package) |

---

## 8. Out of Scope

- `IPcsProAutomationService` interface (deferred to S-003)
- `PcsProStateMachine` implementation and timeout constants (deferred to S-004)
- Unit tests (deferred to S-004)

---

## 9. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | GPT 5.4, Sonnet 4.6, Opus 4.6 | REVISE — unanimous (GPT: 2M+1L; Sonnet: 1C+1H+1M+2L; Opus: 2H+1M+2L) |
| R2 | 2026-04-10 | GPT 5.4, Sonnet 4.6 | APPROVE — all R1 findings resolved, no regressions |
