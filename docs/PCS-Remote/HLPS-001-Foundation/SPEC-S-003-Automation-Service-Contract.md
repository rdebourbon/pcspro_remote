# SPEC-S-003: Automation Service Contract

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-Automation-Service-Contract.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-10 |
| **Step** | S-003 — Automation Service Contract |
| **IS** | IS-001-Foundation.md (APPROVED v0.2) |
| **HLPS** | HLPS-001-Foundation.md (APPROVED v0.4) |
| **Branch** | `feature/S-003-automation-service-contract` |

---

## 1. Overview

This step defines `IPcsProAutomationService` — the single architectural seam between the web layer and the automation layer. Once defined, all downstream layers (mock, real FlaUI, web hub) share a common contract and can evolve independently. This delivers HLPS-001 success criterion F-SC-7.

---

## 2. Scope

### In Scope

- Add a single new file `IPcsProAutomationService.cs` to `PcsRemote.Core`
- The interface must exactly match the contract specified in PRD §5a
- Full XML documentation on the interface declaration and every member
- Verify that project references from `PcsRemote.Automation` and `PcsRemote.Automation.Mock` to `PcsRemote.Core` are in place (established in S-001; confirmed not modified)

### Out of Scope

- Any concrete implementation of the interface (`PcsProAutomationService`, `MockPcsProAutomationService`) — these are HLPS-002 and HLPS-006
- DI registration / `appsettings.json` flag — deferred to HLPS-002
- State machine wiring within service implementations — deferred to HLPS-002/HLPS-006
- Unit tests — this step introduces no testable behaviour; interface conformance is verified by compilation and code review

---

## 3. Interface Contract

The interface must express the following contract (from PRD §5a). These are requirements — exact C# syntax is determined at delivery.

### 3.1 State Observation

| Member | Kind | Description |
|---|---|---|
| `CurrentState` | Read-only property → `PcsProState` | Returns the current state of the PCS Pro application lifecycle |
| `StateChanged` | Event → `EventHandler<PcsProState>` | Raised whenever the lifecycle state changes; subscribers receive the new state |

### 3.2 Lifecycle Operations

| Member | Kind | Parameters | Return | Description |
|---|---|---|---|---|
| `LaunchAndLoginAsync` | Async method | `CancellationToken ct = default` | `Task` | Launches PCS Pro and drives through login to the match selection screen |
| `GetTodaysMatchesAsync` | Async method | `CancellationToken ct = default` | `Task<IReadOnlyList<MatchInfo>>` | Reads and returns the list of today's available match fixtures |
| `LoadMatchAsync` | Async method | `MatchInfo match`, `CancellationToken ct = default` | `Task` | Selects and loads the specified match, advancing state to `MatchLoaded` |
| `GetTeamNamesAsync` | Async method | `CancellationToken ct = default` | `Task<MatchTeams>` | Reads the home and away team names from the loaded match |
| `RefreshScoreboardAsync` | Async method | `CancellationToken ct = default` | `Task` | Triggers a scoreboard data refresh within the loaded match |
| `CaptureScoreboardImageAsync` | Async method | `CancellationToken ct = default` | `Task<byte[]>` | Captures and returns a JPEG screenshot of the scoreboard |
| `StopAsync` | Async method | `CancellationToken ct = default` | `Task` | Stops and closes PCS Pro, returning state to `NotRunning` |

### 3.3 CancellationToken

All async methods must accept an optional `CancellationToken` parameter defaulting to `default`. This enables callers to cancel long-running operations without additional overloads.

---

## 4. XML Documentation Requirements

All documentation must be in plain, implementer-facing language:

- **Interface declaration**: One-sentence summary of the interface's architectural role as the boundary between web and automation layers
- **`CurrentState`**: Describe that it reflects the lifecycle state managed by the state machine
- **`StateChanged`**: Describe when it fires and what the event argument represents
- **Each async method**: One-sentence description of purpose; note the CancellationToken parameter

---

## 5. Project Reference Verification

`PcsRemote.Automation` and `PcsRemote.Automation.Mock` already reference `PcsRemote.Core` (established in S-001). This step must verify these references remain intact. If a reference is missing, it must be re-added as part of this step. No new references are to be added to `PcsRemote.Core`.

---

## 6. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` exits 0 with no errors or warnings |
| AC-2 | `IPcsProAutomationService` exists in `PcsRemote.Core` namespace |
| AC-3 | Interface has exactly 9 members with signatures matching §3: `CurrentState`, `StateChanged`, `LaunchAndLoginAsync`, `GetTodaysMatchesAsync`, `LoadMatchAsync` (with required `MatchInfo match` parameter), `GetTeamNamesAsync`, `RefreshScoreboardAsync`, `CaptureScoreboardImageAsync`, `StopAsync` |
| AC-4 | All 7 async methods accept an optional `CancellationToken ct = default`; `LoadMatchAsync` additionally accepts a required `MatchInfo match` as its first argument |
| AC-5 | `PcsRemote.Core` has no new `<ProjectReference>` elements and no new `<PackageReference>` elements introduced by this step (existing `Stateless` package retained) |
| AC-6 | The interface declaration and all 9 interface members carry XML `<summary>` documentation with content matching §4 descriptions |
| AC-7 | `PcsRemote.Automation` and `PcsRemote.Automation.Mock` both reference `PcsRemote.Core` |

---

## 7. Verification Table

| AC | Verification Method |
|---|---|
| AC-1 | Run `dotnet build` — assert exit code 0, zero warnings |
| AC-2–4 | Code review of `IPcsProAutomationService.cs` against PRD §5a and §3 above, including `LoadMatchAsync` parameter signature |
| AC-5 | Inspect `PcsRemote.Core.csproj` — assert no new `<ProjectReference>` or `<PackageReference>` elements vs S-002 baseline |
| AC-6 | Code review confirms `<summary>` on interface declaration and all 9 members, with content matching §4 descriptions |
| AC-7 | Inspect `.csproj` files for `PcsRemote.Automation` and `PcsRemote.Automation.Mock` |

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | GPT 5.4, Claude Sonnet 4.6 | REVISE / REVISE — 1 HIGH + 3 LOW accepted; 2 findings rejected |
| R2 | 2026-04-10 | Claude Sonnet 4.6, GPT-4.1* | APPROVE / APPROVE — all R1 fixes verified, no regressions |

*GPT 5.4 returned an invalid response (requested additional context) on R2; substituted with GPT-4.1 per fallback protocol. Panel remains valid: 2 reviewers, 2 distinct architectures (Claude + GPT).

### R1 Findings Applied (v0.1 → v0.2)

| Ref | Finding | Disposition | Resolution |
|---|---|---|---|
| GPT F1 / Sonnet Issue 1 | `LoadMatchAsync` missing `MatchInfo match` parameter from §3.2 and all ACs | Accept (HIGH) | Added Parameters column to §3.2; AC-3 now requires signatures match §3; AC-4 explicitly requires `MatchInfo match` as first argument of `LoadMatchAsync` |
| Sonnet Issue 2 | §3.2 table structure cannot represent non-CT parameters | Accept (merged with F1) | Resolved by Parameters column addition |
| GPT F2 / Sonnet Issue 3 | AC-6 omits interface declaration and doesn't enforce doc content | Accept (LOW) | AC-6 now requires `<summary>` on interface declaration AND all 9 members, with content matching §4 |
| Sonnet Issue 4 | AC-5 phrasing implies permanent NuGet constraint | Accept (LOW) | AC-5 reworded to "no new elements introduced by this step" |
| GPT F3 | Behavioral pre/post-state expectations required in spec | Reject | Interface-only spec; pre/post-condition contracts belong in implementing step specs (HLPS-002, HLPS-006) |
| GPT F4 | Spec over-relies on PRD §5a reference | Reject | PRD reference is intentional; §3 tables carry sufficient delivery context |
