# SPEC-IS-021-S-002 — Real Play-Cricket API Client

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **Step** | IS-021 S-002 |
| **Branch** | `feature/IS-021-S-002-api-client` |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md |

---

## Context

S-001 created the `IPlayCricketApiClient` contract and `PlayCricketFixture` record in `PcsRemote.Core`, and the `PcsRemote.PlayCricket` project scaffold. S-002 adds the real HTTP implementation of that contract to `PcsRemote.PlayCricket`.

S-003 (mock client) is parallel and independent. The wiring of mock vs real via the `UseMock` flag is split across both steps — S-002 registers the real path; S-003 registers the mock path.

The Play-Cricket API is a JSON REST API. OQ-2 confirms the endpoint is `matches.json` with `site_id`, `season`, and `api_token` query parameters. There is no server-side date filter — callers receive all fixtures for the season and filter client-side.

---

## Requirements

### R-1 — HTTP client registration

`PcsRemote.PlayCricket` gains a `Microsoft.Extensions.Http` dependency (the package that provides `IHttpClientFactory` and `AddHttpClient`). `Microsoft.Extensions.Http` must also be added to `Directory.Packages.props`.

A **named** HTTP client is registered for the Play-Cricket API using `AddHttpClient("PlayCricket", …)`. The base address and any required default headers (e.g., `Accept: application/json`) are configured at registration time. The named-client approach is required — not the typed-client approach — so that `PlayCricketApiClient` can be registered as a **singleton** that calls `IHttpClientFactory.CreateClient("PlayCricket")` on each request, correctly participating in the factory's handler pool and allowing DNS rotation in long-running processes.

A `PlayCricketServiceCollectionExtensions` class in `PcsRemote.PlayCricket` exposes a registration extension method analogous to `AddPcsProAutomation` and `MockServiceCollectionExtensions.AddPcsProAutomationService` in the rest of the codebase. This method:
- Registers `PlayCricketOptions` bound from the `PlayCricket` configuration section.
- Registers the named HTTP client.
- Registers `PlayCricketApiClient` as the singleton implementation of `IPlayCricketApiClient`, with `IHttpClientFactory` injected.

This method is called from `WebApplicationBuilderExtensions.AddPcsRemoteServices` in the real (non-mock) branch only.

### R-2 — `PlayCricketApiClient` implementation

`PlayCricketApiClient` implements `IPlayCricketApiClient.GetFixturesAsync(int siteId, CancellationToken ct)`.

**Request construction:**
- The endpoint is `matches.json` relative to the client base address.
- Query string includes `site_id`, `season` (current calendar year at call time), and `api_token` set to `PlayCricketOptions.ApiKey`.

**Success path:**
- Deserialises the JSON response using `System.Text.Json`.
- The JSON response root contains a `site_matches` array. Each element is a JSON object with the following fields used by this implementation (all Play-Cricket API field names are snake_case):
  - `id` — string representation of the integer fixture ID, mapped to `PlayCricketFixture.FixtureId` (parsed to `int`).
  - `home_team_name` — string, mapped to `HomeTeam`.
  - `away_team_name` — string, mapped to `AwayTeam`.
  - `match_status` — string, mapped to `Status` verbatim (exact status values are deferred to OQ-1 / S-007 JIT Spec).
  - `match_date` — date string in `DD/MM/YYYY` format, mapped to `MatchDate`.
- The DTOs used for deserialisation must use the above snake_case property names (via `[JsonPropertyName("…")]` attributes or `JsonNamingPolicy.SnakeCaseLower` — either approach is acceptable, but the mapping must be explicit so `System.Text.Json`'s default case-sensitive matching does not silently produce zero/empty values).
- Maps each element to a `PlayCricketFixture` record. Date parsing failure for any individual record produces `DateOnly.MinValue` for that record (no exception, warning logged per R-3). `FixtureId` parse failure for any individual record skips that record (warning logged).
- Returns the full list (excluding any records skipped due to unrecoverable parse failure).

**Error paths (all return empty list):**
- Non-success HTTP status code: log a warning including the status code and site ID; return empty list.
- `HttpRequestException` (network failure, timeout, etc.): log a warning including the exception and site ID; return empty list.
- JSON deserialisation failure (malformed or unexpected schema): log a warning including the exception and site ID; return empty list.
- All of the above must propagate `OperationCanceledException` / `TaskCanceledException` correctly — do not catch cancellation.

> **Note:** Log messages must not include the request URI or query string. The `api_token` (API key) must never appear in log output. Log only the site ID and, where appropriate, the HTTP status code.

### R-3 — Logging

All warning log calls use Serilog structured message templates (no string interpolation). Named properties must be descriptive. The following events are logged as `Warning`:
- Non-success HTTP response (include status code and site ID; do NOT include the request URI or API key).
- Network/transport exception (include exception and site ID; do NOT include the request URI or API key).
- JSON deserialisation failure (include exception and site ID).
- Individual record date parse failure (include the raw date string and fixture ID).
- Individual record fixture ID parse failure (include raw value).

No `Information`-level log on success (this is a polling call, not a lifecycle event).

### R-4 — DI wiring

`WebApplicationBuilderExtensions.AddPcsRemoteServices` is updated to branch on `PlayCricket:UseMock`:
- When `false` (default): calls the real registration extension.
- When `true`: no registration in this step (S-003 handles the mock path).

The `PlayCricket:UseMock` option is already scaffolded in `appsettings.json` from S-001.

---

## Test Cases

All tests in `PcsRemote.PlayCricket.Tests`. The test project requires the following additional package references (all already versioned in `Directory.Packages.props`): `Moq`, `Microsoft.Extensions.Logging.Abstractions`, and `Microsoft.Extensions.Options`. Mirror the pattern from `PcsRemote.YouTube.Tests.csproj` for logger mock setup and verification.

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-1 | `GetFixturesAsync_ValidResponse_ReturnsMappedFixtures` | `HttpClient` returns a well-formed JSON body with two fixtures using `site_matches`, `id`, `home_team_name`, `away_team_name`, `match_status`, `match_date` | Returns list of two `PlayCricketFixture` records with correct `FixtureId`, `HomeTeam`, `AwayTeam`, `Status`, and `MatchDate` |
| TC-2 | `GetFixturesAsync_NonSuccessStatus_ReturnsEmptyListAndLogsWarning` | `HttpClient` returns HTTP 500 | Returns empty list; a `Warning`-level log entry is captured |
| TC-3 | `GetFixturesAsync_MalformedJson_ReturnsEmptyListAndLogsWarning` | `HttpClient` returns 200 with invalid JSON | Returns empty list; a `Warning`-level log entry is captured |
| TC-4 | `GetFixturesAsync_NetworkException_ReturnsEmptyListAndLogsWarning` | `HttpClient` throws `HttpRequestException` | Returns empty list; a `Warning`-level log entry is captured |
| TC-5 | `GetFixturesAsync_CancellationRequested_PropagatesCancellation` | `CancellationToken` is cancelled before the HTTP call completes | `OperationCanceledException` (or `TaskCanceledException`) propagates to the caller; no empty-list swallow |
| TC-6 | `GetFixturesAsync_BadDateOnOneRecord_ReturnsMixedListAndLogsWarning` | Response contains two records, one with a malformed date string | Returns two entries; the bad-date entry has `DateOnly.MinValue`; a `Warning`-level log entry is captured containing the raw date string |

Tests use a fake/mock `HttpMessageHandler` to control HTTP responses without network access. No `Thread.Sleep` or real network calls. Tests are in the existing `PcsRemote.PlayCricket.Tests` project.

---

## Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `PlayCricketApiClient` class exists in `PcsRemote.PlayCricket` and implements `IPlayCricketApiClient` |
| AC-2 | `PcsRemote.PlayCricket` takes a dependency on `Microsoft.Extensions.Http`; `Microsoft.Extensions.Http` is added to `Directory.Packages.props` |
| AC-3 | `PlayCricketServiceCollectionExtensions` provides a registration extension method that registers options, named HTTP client, and the singleton implementation (using `IHttpClientFactory`) |
| AC-4 | `WebApplicationBuilderExtensions.AddPcsRemoteServices` calls the real registration in the non-mock branch |
| AC-5 | All error paths return empty list with a warning log (non-success HTTP, network exception, malformed JSON); no log includes the request URI or API key |
| AC-6 | Cancellation propagates without being swallowed |
| AC-7 | JSON field mapping uses explicit snake_case names; TC-1 verifies all five mapped fields have correct values |
| AC-8 | Date-parse fallback produces `DateOnly.MinValue` for affected record and logs a Warning (TC-6) |
| AC-9 | TC-1 through TC-6 are runnable and pass |
| AC-10 | Solution builds with zero warnings |
| AC-11 | All existing tests continue to pass |

---

## Documentation Updates

None beyond this spec.

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 2 | GPT-5.4 | HIGH: 2 / MEDIUM: 2 / LOW: 2 | All 6 accepted | REVISION | F-1 HIGH: typed-client+singleton anti-pattern — mandated named client + IHttpClientFactory.CreateClient per request. F-2 HIGH: STJ field names unspecified — added exact API snake_case field names to R-2, required explicit mapping. F-3 MEDIUM: date-parse path untested — added TC-6 + AC-8. F-4 MEDIUM: test project missing Moq/Logging/Options packages — added to test cases section. F-5 LOW: ApiKey vs api_token naming gap — R-2 now reads "api_token set to PlayCricketOptions.ApiKey". F-6 LOW: API key leak risk in logs — added note to R-2 and R-3 forbidding URI/key in log output. |
| R1-SC | Tier 0 | Self-Cert | — | — | APPROVED | All R1 findings addressed. No scope creep. |
