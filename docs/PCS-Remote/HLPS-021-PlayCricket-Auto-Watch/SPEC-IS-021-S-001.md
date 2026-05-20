# SPEC-IS-021-S-001 — Play-Cricket Project Scaffold, Core Contracts, and Configuration

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **Step** | IS-021 S-001 |
| **Branch** | `feature/IS-021-S-001-scaffold` |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md |

---

## Context

IS-021 delivers the Play-Cricket Auto-Watch feature in ten atomic steps. S-001 is the foundation — no business logic, no tests for new behaviour, no DI wiring. It creates the two new projects, the Core contracts that all downstream steps depend on, the configuration class, and scaffolds the configuration section.

DI registration of the API client is deferred to S-002 (real client) and S-003 (mock client). The watcher service interface is deferred to S-004. This step delivers only what is needed to unblock parallel S-002, S-003, and S-004 work.

---

## Requirements

### R-1 — New projects

Two new class library projects are added to the solution and source tree:

- **`PcsRemote.PlayCricket`** — the real implementation project. Targets `net8.0` (platform-neutral, no `net8.0-windows`). References `PcsRemote.Core`. No HTTP client framework imports in this step; those arrive with the client implementation in S-002.
- **`PcsRemote.PlayCricket.Mock`** — the mock implementation project. Targets `net8.0`. References `PcsRemote.Core`. No HTTP concerns.

Both projects follow the existing project layout conventions (one namespace, one top-level source file per type). Both are added to the solution under the `/src/` folder. `TreatWarningsAsErrors` is inherited from `Directory.Build.props` — no project-level override required.

### R-2 — Core contracts

Two new types are added to `PcsRemote.Core`:

**`IPlayCricketApiClient`** — the contract for querying Play-Cricket fixture data. Contains a single method: given a site identifier and a cancellation token, returns all fixtures for that site as an immutable list of `PlayCricketFixture` records. The return type is `Task<IReadOnlyList<PlayCricketFixture>>`. Empty list is a valid return — the caller distinguishes "no fixtures" from "error" via the logging contract established in S-002.

**`PlayCricketFixture`** — an immutable record representing a single Play-Cricket fixture. Uses a primary constructor with the following parameters, in order:
- Integer fixture identifier (Play-Cricket's own ID — not related to the PCS Pro `MatchInfo.MatchId` composite key). Required positional parameter — no default value.
- Home team display name (`string`, default `""`).
- Away team display name (`string`, default `""`).
- Status string (`string`, default `""`). The exact values are deferred to OQ-1, resolved in S-007 JIT Spec; the record stores whatever the API returns.
- Match date (`DateOnly`, default `DateOnly.MinValue` — sentinel meaning "not set", consistent with `MatchInfo.MatchDate`).

All string properties default to empty string (not null) for safe comparison. The fixture identifier is positional and required; construction without a `FixtureId` is a compile error.

### R-2 — Core contracts (dependency constraint)

`PcsRemote.Core` introduces no new `PackageReference` entries in this step. The two new types (`IPlayCricketApiClient` and `PlayCricketFixture`) use only BCL types (`Task`, `IReadOnlyList<T>`, `CancellationToken`, `DateOnly`) — all in-framework. This ensures HLPS-021 C-1 is a compile-time guarantee: `PcsRemote.PlayCricket` (and any HTTP client library it takes on) cannot become a transitive dependency of `PcsRemote.Core`.



A `PlayCricketOptions` class is added to `PcsRemote.PlayCricket`. It contains:

- **API key** — a string used for authenticating Play-Cricket API requests. Defaults to empty string (not null). Must not be embedded in source control (HLPS-021 C-2).
- **Site IDs** — a list of integer Play-Cricket site identifiers to query. Resolution queries all sites in parallel (S-006). Defaults to an empty list.
- **Polling interval** — the number of whole seconds between poll ticks, shared by both the auto-load (FlaUI) loop and the auto-close (API) loop. Default: 90. Minimum enforced: 60 (enforced at the service level in S-005/S-007, not in the options class itself — the options class stores the raw configured value).
- **Countdown duration** — the number of whole seconds for the auto-close countdown. Default: 300 (5 minutes).
- **Use-mock flag** — when true, the mock API client is registered instead of the real one (DI wiring in S-002/S-003).

### R-4 — Configuration scaffolding

The `PlayCricket` section is added to `appsettings.json` with comment-friendly defaults. The section mirrors the style of the existing `YouTube` and `PcsPro` sections:

```json
"PlayCricket": {
  "UseMock": false,
  "PollingIntervalSeconds": 90,
  "CountdownDurationSeconds": 300,
  "SiteIds": [],
  "ApiKey": ""
}
```

The API key is present but empty. `SiteIds` is an empty array — the operator populates both after installation.

---

## Test Cases

S-001 has no new business logic to test. Correctness is verified by compilation and configuration binding.

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-1 | `PlayCricketOptions_BoundFromConfig_DefaultsAreCorrect` | Bind `PlayCricketOptions` from an in-memory `IConfiguration` containing the scaffolded `PlayCricket` section values | `PollingIntervalSeconds == 90`, `CountdownDurationSeconds == 300`, `SiteIds` is empty, `ApiKey == ""`, `UseMock == false` |
| TC-2 | `IPlayCricketApiClient_IsInCore_HasNoExternalDependencies` | Reflection check on `IPlayCricketApiClient` | Type is declared in `PcsRemote.Core` assembly; all method parameter types and return type are BCL types only (no Play-Cricket, HTTP, or external assembly types) |
| TC-3 | `PlayCricketFixture_IsImmutableRecord_WithRequiredFixtureId` | Construct `PlayCricketFixture` with `FixtureId = 42` and check defaults | Type is a record; `FixtureId == 42`; `HomeTeam == ""`; `AwayTeam == ""`; `Status == ""`; `MatchDate == DateOnly.MinValue` |

TC-1 is added to `PcsRemote.PlayCricket.Tests` (new, empty test project created in this step). TC-2 and TC-3 are added to `PcsRemote.Core.Tests` (existing project, no new dependencies).

Two new test projects are created as minimal stubs in this step:
- `tests/PcsRemote.PlayCricket.Tests/PcsRemote.PlayCricket.Tests.csproj` — references `PcsRemote.PlayCricket` and `PcsRemote.Core`; contains only TC-1.
- `tests/PcsRemote.PlayCricket.Mock.Tests/PcsRemote.PlayCricket.Mock.Tests.csproj` — references `PcsRemote.PlayCricket.Mock` and `PcsRemote.Core`; no tests in this step (populated in S-003).

Both test projects are added to the solution under the `/tests/` folder.

---

## Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `PcsRemote.PlayCricket` project exists, targets `net8.0`, references only `PcsRemote.Core` |
| AC-2 | `PcsRemote.PlayCricket.Mock` project exists, targets `net8.0`, references only `PcsRemote.Core` |
| AC-3 | `IPlayCricketApiClient` is declared in `PcsRemote.Core` with the single `GetFixturesAsync` method; no new `PackageReference` added to `PcsRemote.Core` |
| AC-4 | `PlayCricketFixture` is an immutable record in `PcsRemote.Core` with a primary constructor requiring `FixtureId`; string properties default to `""` |
| AC-5 | `PlayCricketOptions` is in `PcsRemote.PlayCricket`; defaults are as specified |
| AC-6 | `appsettings.json` contains the `PlayCricket` section with the scaffolded defaults |
| AC-7 | TC-1 through TC-3 are runnable and pass |
| AC-8 | Both new test project stubs are added to the solution |
| AC-9 | Solution builds with zero warnings |
| AC-10 | All existing tests continue to pass |

---

## Documentation Updates

None required beyond the spec itself.

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 2 | GPT-5.4 | HIGH: 2 / MEDIUM: 1 | All 3 accepted | REVISION | F-1 HIGH: TC-1 tested default constructor only — rewritten to bind from in-memory IConfiguration. F-2 HIGH: C-1 enforcement only verified assembly location — added dependency constraint clause to R-2 and updated AC-3. F-3 MEDIUM: FixtureId "no default" ambiguous — PlayCricketFixture now specifies primary constructor with required positional FixtureId; TC-3 updated to verify construction with explicit FixtureId and string defaults. |
| R1-SC | Tier 0 | Self-Cert | — | — | APPROVED | All three R1 findings addressed. No scope creep or regressions introduced. |
