# SPEC-IS-021-S-003 — Mock Play-Cricket API Client

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **Step** | IS-021 S-003 |
| **Branch** | `feature/IS-021-S-003-mock-client` |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md |

---

## Context

S-001 created the `IPlayCricketApiClient` contract and the `PcsRemote.PlayCricket.Mock` project scaffold. S-003 adds the in-memory mock implementation to that project.

S-002 (real HTTP client) is parallel and independent. The mock is the reference implementation that all development and local testing uses. Downstream steps S-005, S-006, and S-007 all depend on a working `IPlayCricketApiClient` — the mock satisfies that dependency without network access.

---

## Requirements

### R-1 — Mock options

A `MockPlayCricketOptions` class is added to `PcsRemote.PlayCricket.Mock`. It holds a configurable dictionary of fixture lists keyed by site ID. This allows tests and development configuration to declare exactly which fixtures are returned for each site. Default: empty (no fixtures for any site).

The options class follows the pattern of `MockPcsProOptions` and `MockYouTubeOptions` in the existing codebase.

### R-2 — `MockPlayCricketApiClient` implementation

`MockPlayCricketApiClient` in `PcsRemote.PlayCricket.Mock` implements `IPlayCricketApiClient.GetFixturesAsync(int siteId, CancellationToken ct)`:
- If the configured fixture map contains the given site ID, return the fixture list for that site as `IReadOnlyList<PlayCricketFixture>`. Since `PlayCricketFixture` is an immutable record, callers cannot mutate individual fixture objects; returning `.AsReadOnly()` over the configured backing list is sufficient isolation.
- If the given site ID is not in the map, return an empty list.
- Never throw on non-cancellation inputs (including unknown site IDs, zero-length lists, and extreme values).
- The implementation is synchronous internally (no network, no I/O); it completes immediately. It must still honour the `CancellationToken` — check for cancellation at entry and throw `OperationCanceledException` if already cancelled before proceeding.
- No logging on normal operation; the mock is intentionally silent.

### R-3 — DI registration

`PcsRemote.PlayCricket.Mock.csproj` must add the following package references (all already versioned in `Directory.Packages.props`, mirroring the `PcsRemote.Automation.Mock.csproj` pattern): `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, and `Microsoft.Extensions.Options.ConfigurationExtensions`.

A `MockPlayCricketServiceCollectionExtensions` class in `PcsRemote.PlayCricket.Mock` exposes a registration extension method, analogous to `MockServiceCollectionExtensions` in `PcsRemote.Automation.Mock`. The method:
- Binds `MockPlayCricketOptions` from the `PlayCricket:Mock` configuration sub-section.
- Registers `MockPlayCricketApiClient` as the singleton implementation of `IPlayCricketApiClient`.

`WebApplicationBuilderExtensions.AddPcsRemoteServices` in `PcsRemote.Web` is updated to:
1. Add a `ProjectReference` to `PcsRemote.PlayCricket.Mock` in `PcsRemote.Web.csproj` (this is required so that the `using PcsRemote.PlayCricket.Mock;` directive resolves at compile time, consistent with how `PcsRemote.YouTube.Mock` is referenced from `PcsRemote.Web`).
2. Call the mock registration extension in the `PlayCricket:UseMock = true` branch (complementing the real-path branch added in S-002).

---

## Test Cases

All tests in `PcsRemote.PlayCricket.Mock.Tests` (the stub project created in S-001, which already has the required project references from S-001 — no new references needed).

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-1 | `GetFixturesAsync_KnownSiteId_ReturnsConfiguredFixtures` | Options configured with two fixtures for site ID 42; call with site ID 42 | Returns both configured fixtures with correct field values |
| TC-2 | `GetFixturesAsync_UnknownSiteId_ReturnsEmptyList` | Options configured for site ID 42 only; call with site ID 99 | Returns empty list; no exception |
| TC-3 | `GetFixturesAsync_NoFixturesConfigured_ReturnsEmptyList` | Options with empty map; call with any site ID | Returns empty list; no exception |
| TC-4 | `GetFixturesAsync_AlreadyCancelledToken_ThrowsOperationCancelled` | `CancellationToken` already cancelled at time of call | `OperationCanceledException` thrown |

---

## Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `MockPlayCricketApiClient` exists in `PcsRemote.PlayCricket.Mock` and implements `IPlayCricketApiClient` |
| AC-2 | `MockPlayCricketOptions` provides a configurable fixture map keyed by site ID |
| AC-3 | `MockPlayCricketServiceCollectionExtensions` registers options and singleton mock |
| AC-4 | `PcsRemote.PlayCricket.Mock.csproj` references `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, and `Microsoft.Extensions.Options.ConfigurationExtensions` |
| AC-5 | `PcsRemote.Web.csproj` gains a `ProjectReference` to `PcsRemote.PlayCricket.Mock` |
| AC-6 | `WebApplicationBuilderExtensions.AddPcsRemoteServices` calls the mock registration when `PlayCricket:UseMock` is `true` |
| AC-7 | Returns empty list for unknown site IDs without throwing |
| AC-8 | Already-cancelled token throws `OperationCanceledException` |
| AC-9 | TC-1 through TC-4 are runnable and pass |
| AC-10 | Solution builds with zero warnings |
| AC-11 | All existing tests continue to pass |

---

## Documentation Updates

None beyond this spec.

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 2 | GPT-5.4 | HIGH: 2 / MEDIUM: 2 / LOW: 2 | F-1/F-2/F-4/F-5/F-6 accepted; F-3 N/A (factual error was in review prompt context, not spec text) | REVISION | F-1 HIGH: missing DI packages in Mock.csproj — added explicit package list to R-3. F-2 HIGH: Web.csproj needs ProjectReference to PlayCricket.Mock — added to R-3. F-4 MEDIUM: "never throw" contradicts cancellation requirement — reworded to "non-cancellation inputs". F-5 MEDIUM: defensive copy vs AsReadOnly — since PlayCricketFixture is immutable record, AsReadOnly() is sufficient; R-2 updated accordingly and no test case needed. F-6 LOW: test project reference description stale — test cases section updated to note references already present from S-001. |
| R1-SC | Tier 0 | Self-Cert | — | — | APPROVED | All accepted R1 findings addressed. No scope creep. |
