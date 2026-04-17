# SPEC-S-006: PcsRemote.YouTube Real Implementation

| Field | Value |
|---|---|
| **Document** | SPEC-S-006-YouTubeRealImpl.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-17 |
| **IS Step** | S-006 |
| **Governing HLPS** | HLPS-008-YouTube-LiveStream.md v0.4 (APPROVED) |
| **Governing IS** | IS-008-YouTube-LiveStream.md v0.3 (APPROVED) |
| **Branch** | `feature/IS-008-S-006-youtube-real-impl` |

---

## 1. Objective

Create the `PcsRemote.YouTube` project containing the real `YouTubeLiveStreamService` and `DpapiFileDataStore`, wire conditional DI (real vs. mock based on `YouTube:UseMock`), add `--setup-youtube` CLI support to TrayHost for OAuth consent, and provide comprehensive unit tests in `PcsRemote.YouTube.Tests`.

---

## 2. Scope

### In Scope

1. **New `PcsRemote.YouTube` project** (`net8.0-windows`):
   - `DpapiFileDataStore` — custom `Google.Apis.Util.Store.IDataStore` using Windows DPAPI (`ProtectedData.Protect`/`Unprotect`) to encrypt OAuth tokens at rest. File location configurable via `YouTube:TokenStorePath`.
   - `YouTubeLiveStreamService` — implements `IYouTubeLiveStreamService` with the full broadcast lifecycle per HLPS §5.7: create broadcast → bind to stream → poll OBS health → transition to live. Includes startup reconciliation (§5.6), setup guard (§5.4), cancel cleanup (§5.7 cancel path).
   - `YouTubeOptions` — strongly-typed options class bound to the `YouTube` config section.

2. **New `PcsRemote.YouTube.Tests` project** — unit tests for `DpapiFileDataStore` and `YouTubeLiveStreamService` using mocked Google API HTTP transport.

3. **Conditional DI wiring** — `WebApplicationBuilderExtensions.AddPcsRemoteServices` updated to register `MockYouTubeLiveStreamService` when `YouTube:UseMock == true`, or `YouTubeLiveStreamService` when `false`.

4. **`--setup-youtube` CLI flag** — `PcsRemote.TrayHost/Program.cs` detects `--setup-youtube` in `args` and runs the OAuth2 Installed App consent flow (opens browser, waits for code, stores token via `DpapiFileDataStore`, then exits). This requires adding a `ProjectReference` from `PcsRemote.TrayHost.csproj` to `PcsRemote.YouTube.csproj`.

5. **`PcsRemote.Web.csproj`** — add `ProjectReference` to `PcsRemote.YouTube.csproj` (for conditional DI).

### Out of Scope

- Mock implementation changes (S-003, delivered)
- StreamingControls component (S-004, delivered)
- ChangeMatchButton lifecycle coupling (S-005, delivered)
- Playwright E2E tests (S-007)

### IS Deviations

1. **DpapiFileDataStore per-key files:** HLPS §5.4 specifies a single fixed file `PcsRemote.YouTube.dat`. The `IDataStore` contract requires per-key storage (`StoreAsync<T>(string key, T value)`, `GetAsync<T>(string key)`). Per-key files at `{TokenStorePath}/{key}` are the only viable implementation. The HLPS single-file description is impractical for the interface contract.
2. **YouTubeStreamException sealed fix:** HLPS §4.6 specifies `sealed class`. The delivered S-001 type is `public class` (not sealed). This step corrects the omission as a pre-existing fix.

---

## 3. Design

### 3.1 `DpapiFileDataStore`

Implements `Google.Apis.Util.Store.IDataStore` (4 methods: `StoreAsync`, `GetAsync`, `DeleteAsync`, `ClearAsync`).

**Storage format (per-key files — see IS Deviation #1):**
- `key` is provided by Google's auth library (typically `"Google.Apis.Auth.OAuth2.Responses.TokenResponse-user"`)
- Serialize the value to JSON via `System.Text.Json`
- Encrypt bytes with `ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)`
- Write encrypted bytes to `{TokenStorePath}/{key}`
- Read is the inverse: read bytes → `Unprotect` → deserialize

**Configuration:** `YouTube:TokenStorePath` — defaults to `Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "PcsRemote", "GoogleTokens")` when empty/missing.

**Directory creation:** `StoreAsync` creates the directory if it doesn't exist.

**Error handling:** `CryptographicException` on `Unprotect` (wrong user, corrupted file) → delete the file and return `default(T)` with a Warning log, forcing re-consent.

### 3.2 `YouTubeOptions`

Strongly-typed POCO bound to `Configuration.GetSection("YouTube")`:

```
ClientId, ClientSecret, LiveStreamId, BroadcastTitleTemplate, 
BroadcastPrivacy, StreamReadyTimeoutSeconds, StreamPollIntervalSeconds, TokenStorePath, UseMock
```

- `StreamReadyTimeoutSeconds` defaults to 60
- `StreamPollIntervalSeconds` defaults to 3 (per HLPS §5.7 step 5)
- `BroadcastPrivacy` defaults to `"public"`

Validated at service startup — `LiveStreamId` must be non-empty; `ClientId`/`ClientSecret` must be non-empty. Config validation occurs BEFORE token check (see §3.3).

### 3.3 `YouTubeLiveStreamService`

**Constructor dependencies:**
- `IOptions<YouTubeOptions>` — configuration
- `IPcsProAutomationService` — for `LoadedMatch`
- `BroadcastTitleRenderer` — for title generation
- `ILogger<YouTubeLiveStreamService>` — structured logging
- `IDataStore` — token storage (DI wires `DpapiFileDataStore` as `IDataStore`; tests inject a stub)

**Key implementation patterns:**

- **Thread safety:** `SemaphoreSlim(1, 1)` guards state transitions. The semaphore is held ONLY during state mutation; it is released during long-running external API calls (polling, broadcast creation/binding). This mirrors the mock's interruption pattern where `StopStreamAsync` can enter and cancel an in-flight start.
- **StatusChanged event:** Fires inside the semaphore, carrying a `StreamStateSnapshot`.
- **CancellationTokenSource:** `_startCts` is created on `StartStreamAsync` entry and cancelled by `StopStreamAsync` when status is `Starting` (cancel-during-start path).

**`InitializeAsync`:**
1. Validate config: `LiveStreamId`, `ClientId`, `ClientSecret` must be non-empty. If any are missing, log `Fatal` and throw `InvalidOperationException` with a guidance message. Config validation runs FIRST — before any token check — so a missing `LiveStreamId` is always caught regardless of token state.
2. Attempt to load token from `IDataStore`. If no token exists: log `Information("YouTube not configured. Run --setup-youtube.")`, set `_tokenAvailable = false`, return. The service starts without error but `StartStreamAsync` will throw.
3. If token valid: construct `UserCredential` non-interactively from stored token (NO `GoogleWebAuthorizationBroker.AuthorizeAsync` — use `GoogleCredential.FromAccessToken` or construct `UserCredential` directly from stored `TokenResponse` and a `GoogleAuthorizationCodeFlow` with no `ICodeReceiver`). This ensures runtime NEVER triggers browser consent (S-YT-12).
4. Create `YouTubeService` instance with the credential.
5. Validate `LiveStreamId` via API: call `liveStreams.list(id=configured, part=id,status)`. If not found (empty items list) or not reusable, log `Fatal` with `{LiveStreamId}` and throw `YouTubeStreamException` with guidance message.
6. Startup reconciliation: query `liveBroadcasts.list(mine=true, broadcastStatus=active)`:
   - If one or more `live` broadcasts found: restore `CurrentStatus = Live`, populate `CurrentBroadcast` from most recently started. Log `Warning` if multiple active broadcasts exist including count.
   - Check for `ready` state broadcasts (orphaned starts): log `Warning` per orphan with `{BroadcastId}` and `{Title}`.
   - If no active broadcasts: `CurrentStatus = Idle`.

**`StartStreamAsync` (per HLPS §5.7):**
1. Guard: throw `YouTubeStreamException("YouTube streaming is not configured...")` if `_tokenAvailable == false`
2. Guard: throw `InvalidOperationException` if `LoadedMatch == null`
3. Render broadcast title: `_titleRenderer.Render(_options.BroadcastTitleTemplate, match)`
4. Set `CurrentBroadcast = new LiveBroadcastInfo("", renderedTitle, "")` and `CurrentStatus = Starting`. Fire `StatusChanged`. This populates the Starting snapshot with a title-carrying broadcast (matching mock behavior).
5. Create `_startCts` linked to caller's `ct`. Release semaphore.
6. Insert `liveBroadcast` (status=created, scheduledStartTime=UtcNow, privacy=`_options.BroadcastPrivacy`). Update `CurrentBroadcast.BroadcastId`.
7. Bind broadcast to `liveStream` using `_options.LiveStreamId`.
8. Poll `liveStream.status.streamStatus` at `_options.StreamPollIntervalSeconds` intervals:
   - `"active"` → proceed to step 9
   - Timeout (`StreamReadyTimeoutSeconds`) → throw `YouTubeStreamException("OBS is not streaming...")`
   - `ct` cancelled → clean up (see cancellation path below)
9. `liveBroadcasts.transition(broadcastStatus: "live")`. Reacquire semaphore.
10. Update `CurrentBroadcast` with `WatchUrl`. Set `CurrentStatus = Live`. Fire `StatusChanged`.

**Cancellation path (OperationCanceledException during steps 6-9):**
If broadcast ID was obtained (step 6), attempt `liveBroadcasts.delete(broadcastId)` (best-effort, log Warning on failure). Reacquire semaphore. Set `CurrentBroadcast = null`, `CurrentStatus = Idle`. Fire `StatusChanged` with null error. Cancellation NEVER routes through Error (S-YT-11).

**Error path (any other exception during steps 6-9):**
If broadcast ID was obtained, attempt `liveBroadcasts.delete(broadcastId)` (best-effort, log Warning on failure). Reacquire semaphore. Set `CurrentBroadcast = null`, `CurrentStatus = Error`. Fire `StatusChanged` with error message.

**`StopStreamAsync` (semaphore release pattern mirrors mock):**
- Acquire semaphore. If `Live`: set `Stopping`, fire `StatusChanged`. Release semaphore. Call `liveBroadcasts.transition("complete")` (not holding semaphore). Reacquire semaphore. Set `CurrentBroadcast = null`, `CurrentStatus = Idle`. Fire `StatusChanged`. Release.
- If `Starting`: cancel `_startCts`. The start method handles cleanup and transition to Idle.
- If `Idle`/`Error`: no-op with Warning log.

**`ResetAsync`:**
- If Error: clean up any partial broadcast, return to Idle
- If not Error: no-op

**Google API mocking strategy for tests:** The `YouTubeService` is constructed with a configurable `HttpMessageHandler`. Tests inject a mock handler that returns pre-configured JSON responses, avoiding any real network calls.

### 3.4 `--setup-youtube` CLI Flow

In `PcsRemote.TrayHost/Program.cs`, before `builder.Build()`:

```
if args contains "--setup-youtube":
  1. Read YouTubeOptions from configuration
  2. Validate ClientId/ClientSecret are present; if missing, log Error with guidance, exit 1
  3. Create DpapiFileDataStore  
  4. Run GoogleWebAuthorizationBroker.AuthorizeAsync with YouTube scope
  5. On success: log Information with token file path, exit 0
  6. On failure (user cancels, network error, write error): log Error with guidance, exit 1
```

This path does NOT call `builder.AddPcsRemoteServices()`, `builder.Build()`, or `app.Run()`. It is a standalone utility flow that exits before any web server, tray icon, or hosted services start.

### 3.5 Conditional DI Wiring

Replace the current mock-only registration in `WebApplicationBuilderExtensions`:

```
Current (unconditional):
  services.Configure<MockYouTubeOptions>(config.GetSection("YouTube:Mock"));
  services.AddSingleton<IYouTubeLiveStreamService, MockYouTubeLiveStreamService>();

New (conditional):
  if YouTube:UseMock:
    services.Configure<MockYouTubeOptions>(config.GetSection("YouTube:Mock"));
    services.AddSingleton<IYouTubeLiveStreamService, MockYouTubeLiveStreamService>();
  else:
    services.Configure<YouTubeOptions>(config.GetSection("YouTube"));
    services.AddSingleton<IDataStore>(sp => new DpapiFileDataStore(options.TokenStorePath));
    services.AddSingleton<IYouTubeLiveStreamService, YouTubeLiveStreamService>();
    services.AddHostedService<YouTubeInitializerHostedService>();
```

`YouTubeInitializerHostedService` is a simple `IHostedService` that calls `InitializeAsync` on the registered `IYouTubeLiveStreamService` during `StartAsync`.

`BroadcastTitleRenderer` registration remains unconditional (both paths need it).

`MockYouTubeOptions` registration moves inside the mock branch only.

---

## 4. File Inventory

| Action | Path |
|---|---|
| Create | `src/PcsRemote.YouTube/PcsRemote.YouTube.csproj` |
| Create | `src/PcsRemote.YouTube/YouTubeOptions.cs` |
| Create | `src/PcsRemote.YouTube/DpapiFileDataStore.cs` |
| Create | `src/PcsRemote.YouTube/YouTubeLiveStreamService.cs` |
| Create | `src/PcsRemote.YouTube/YouTubeInitializerHostedService.cs` |
| Create | `tests/PcsRemote.YouTube.Tests/PcsRemote.YouTube.Tests.csproj` |
| Create | `tests/PcsRemote.YouTube.Tests/DpapiFileDataStoreTests.cs` |
| Create | `tests/PcsRemote.YouTube.Tests/YouTubeLiveStreamServiceTests.cs` |
| Modify | `src/PcsRemote.Core/YouTubeStreamException.cs` — add `sealed` modifier (IS Deviation #2) |
| Modify | `src/PcsRemote.Web/PcsRemote.Web.csproj` — add YouTube project reference |
| Modify | `src/PcsRemote.Web/WebApplicationBuilderExtensions.cs` — conditional DI wiring |
| Modify | `src/PcsRemote.TrayHost/PcsRemote.TrayHost.csproj` — add YouTube project reference |
| Modify | `src/PcsRemote.TrayHost/Program.cs` — add `--setup-youtube` CLI handling |
| Modify | `PCS_Remote.slnx` — add new projects |

---

## 5. Acceptance Criteria

| AC | Description | Verification |
|---|---|---|
| AC-1 | `DpapiFileDataStore.StoreAsync` encrypts with DPAPI and writes to configured path | Unit test: round-trip store → get returns identical value |
| AC-2 | `DpapiFileDataStore.GetAsync` returns `default(T)` with Warning log when decryption fails | Unit test: corrupt file → returns null, logs Warning |
| AC-3 | `DpapiFileDataStore.DeleteAsync` removes the file | Unit test: store → delete → get returns null |
| AC-4 | `DpapiFileDataStore.ClearAsync` removes all files in token directory | Unit test: store multiple → clear → directory empty |
| AC-5 | `InitializeAsync` with valid token and active broadcast restores `Live` status | Unit test: mock API returns active broadcast → status = Live |
| AC-5b | `InitializeAsync` with valid token and orphan `ready` broadcast logs Warning with `{BroadcastId}` and `{Title}` | Unit test: mock API returns ready-state broadcast → verify Warning log |
| AC-5c | `InitializeAsync` with valid token validates `LiveStreamId` via `liveStreams.list` API; not found → throws | Unit test: mock API returns empty items → Fatal log + exception |
| AC-6 | `InitializeAsync` with no token logs Information and sets `_tokenAvailable = false` | Unit test: no token → service starts, StartStreamAsync throws |
| AC-6b | `InitializeAsync` config validation runs BEFORE token check: missing `LiveStreamId` → Fatal + throws regardless of token | Unit test: empty LiveStreamId → Fatal log + InvalidOperationException |
| AC-7 | `StartStreamAsync` with no token throws `YouTubeStreamException` with "not configured" message | Unit test: verify exception type and message |
| AC-8 | `StartStreamAsync` transitions Idle → Starting → Live on success | Unit test: mock successful API responses → verify status transitions via StatusChanged events |
| AC-8b | `StartStreamAsync` Starting snapshot carries title-populated `CurrentBroadcast` with empty BroadcastId/WatchUrl | Unit test: verify StatusChanged(Starting) snapshot has non-null broadcast with rendered title |
| AC-9 | `StartStreamAsync` generates broadcast title from `BroadcastTitleRenderer` using `LoadedMatch` and `BroadcastTitleTemplate` | Unit test: verify title in broadcast creation API call matches rendered template |
| AC-10 | `StartStreamAsync` binds broadcast to `YouTube:LiveStreamId` | Unit test: verify bind API call uses configured stream ID |
| AC-10b | `StartStreamAsync` passes `YouTubeOptions.BroadcastPrivacy` to broadcast insert API call | Unit test: verify mock HTTP request body includes configured privacy value |
| AC-11 | `StartStreamAsync` polls OBS health at `StreamPollIntervalSeconds` intervals and times out after `StreamReadyTimeoutSeconds` → Error | Unit test: mock returns non-active stream status → verify timeout → Error state; verify poll count ≈ timeout / interval |
| AC-12 | Cancel during Starting: cancels in-flight operation, deletes broadcast, returns to Idle (not Error) | Unit test: trigger cancel during simulated API delay → verify Idle, not Error |
| AC-12b | Error during StartStreamAsync after broadcast creation: best-effort delete, transitions to Error with descriptive message | Unit test: mock HTTP 500 on bind after successful create → verify delete attempt + Error state |
| AC-13 | `StopStreamAsync` when Live: transitions to Stopping (observable), then broadcasts.transition(complete), then Idle | Unit test: mock successful transition → verify Stopping and Idle StatusChanged events both fire |
| AC-14 | `StopStreamAsync` when Starting: cancels start operation, start cleans up to Idle | Unit test: start → immediate stop → verify cancellation and Idle |
| AC-15 | `StopStreamAsync` when Idle: no-op with Warning log | Unit test: verify no API calls, Warning logged |
| AC-16 | `ResetAsync` when Error: cleans up partial broadcast, returns to Idle | Unit test: error state → reset → Idle |
| AC-17 | `--setup-youtube` flag runs OAuth consent and exits without starting web server, tray, or hosted services | Manual verification on garage PC. On missing config: logs Error with guidance, exits code 1 |
| AC-18 | Conditional DI: `YouTube:UseMock = true` → `MockYouTubeLiveStreamService` registered, `MockYouTubeOptions` configured | Build-time verification |
| AC-19 | Conditional DI: `YouTube:UseMock = false` → `YouTubeLiveStreamService` registered, `YouTubeOptions` configured, `IDataStore` wired | Build-time verification |
| AC-20 | Build succeeds with 0 errors, 0 warnings | Build verification |
| AC-21 | All existing tests continue to pass | Test suite verification |
| AC-22 | `YouTubeStreamException` is `sealed` per HLPS §4.6 | Code inspection |
| AC-23 | Runtime service construction NEVER calls `GoogleWebAuthorizationBroker.AuthorizeAsync`; only `--setup-youtube` path does | Unit test: verify no broker invocation in InitializeAsync or StartStreamAsync |

---

## 6. Sub-Tasks and Commit Strategy

Given the size of this step, implementation proceeds in these internal sub-tasks:

### ST-1: Project scaffold
- Create `PcsRemote.YouTube.csproj` with Google API NuGet packages
- Create `PcsRemote.YouTube.Tests.csproj`
- Add project references to Web and TrayHost csproj files
- Add projects to solution
- Create `YouTubeOptions.cs`

### ST-2: DpapiFileDataStore
- Implement `DpapiFileDataStore`
- Write DpapiFileDataStore unit tests (AC-1 through AC-4)

### ST-3: YouTubeLiveStreamService core
- Implement `YouTubeLiveStreamService` with all lifecycle methods
- Write unit tests (AC-5 through AC-16)

### ST-4: CLI and DI wiring
- Add `--setup-youtube` to TrayHost Program.cs (AC-17)
- Update conditional DI wiring (AC-18, AC-19)
- Verify build and all tests pass (AC-20, AC-21)

---

## 7. Dependencies

| Dependency | Source | Status |
|---|---|---|
| `IYouTubeLiveStreamService` | S-001 | ✅ Delivered |
| `LiveStreamStatus`, `LiveBroadcastInfo`, `StreamStateSnapshot` | S-001 | ✅ Delivered |
| `BroadcastTitleRenderer` | S-002 | ✅ Delivered |
| `MockYouTubeLiveStreamService` | S-003 | ✅ Delivered |
| `StreamingControls.razor` | S-004 | ✅ Delivered |
| `ChangeMatchButton lifecycle coupling` | S-005 | ✅ Delivered |

---

## 8. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Google API NuGet package version conflicts | MEDIUM | Pin versions; verify restore succeeds |
| DPAPI tests require Windows | LOW | Project already targets net8.0-windows; CI must run on Windows |
| YouTube API rate limits during testing | LOW | All tests use mocked HTTP transport; no real API calls |
| `YouTubeLiveStreamService` cognitive complexity | HIGH | Use private helper methods for each lifecycle step; semaphore pattern keeps state transitions linear |

---

## 9. Traceability

| HLPS Requirement | Coverage |
|---|---|
| S-YT-2 (broadcast title from template) | AC-9 |
| S-YT-3 (Idle → Starting → Live transitions) | AC-8, AC-8b |
| S-YT-5 (OBS timeout → Error) | AC-11 |
| S-YT-6 (Stop → complete → Idle) | AC-13 |
| S-YT-8 (Mock/real conditional DI) | AC-18, AC-19 |
| S-YT-10 (Startup reconciliation) | AC-5, AC-5b, AC-5c |
| S-YT-11 (Cancel during Starting) | AC-12 |
| S-YT-12 (No token → error message) | AC-7, AC-23 |

---

## 10. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-17 | Claude Opus 4.6, GPT-5.4 | NEEDS REVIEW — 16 Opus findings + 8 GPT findings; 18 unique after dedup. All blocking fixes applied in v0.2. |

### R1 Findings Applied

| # | Severity | Source | Finding | Disposition |
|---|---|---|---|---|
| 1 | CRITICAL | Both | Missing LiveStreamId API validation at startup | Accept — added AC-5c, §3.3 step 5 |
| 2 | CRITICAL | Opus | YouTubeStreamException not sealed per HLPS §4.6 | Accept — IS Deviation #2, AC-22, file inventory updated |
| 3 | HIGH | Both | Orphan broadcast warning log missing | Accept — added AC-5b, §3.3 step 6 |
| 4 | HIGH | Both | DpapiFileDataStore per-key naming contradicts HLPS | Accept — IS Deviation #1 documents rationale |
| 5 | HIGH | Opus | Constructor takes DpapiFileDataStore vs IDataStore | Accept — IDataStore for testability, §3.3 + §3.5 updated |
| 6 | HIGH | Both | StopStreamAsync semaphore release pattern undefined | Accept — §3.3 StopStreamAsync explicitly specifies mock-matching pattern |
| 7 | HIGH | Opus | No AC for BroadcastPrivacy in broadcast creation | Accept — added AC-10b |
| 8 | HIGH | Opus | Missing poll interval specification | Accept — added StreamPollIntervalSeconds to YouTubeOptions, AC-11 updated |
| 9 | HIGH | GPT | Starting snapshot must carry title-populated CurrentBroadcast | Accept — §3.3 step 4 explicit, added AC-8b |
| 10 | HIGH | GPT | Runtime browser suppression mechanism not defined | Accept — §3.3 step 3 specifies non-interactive credential construction, added AC-23 |
| 11 | MEDIUM | Opus | InitializeAsync fail-fast order ambiguous | Accept — §3.3 step 1 clarifies config validation runs FIRST, added AC-6b |
| 12 | MEDIUM | Opus | MockYouTubeOptions registration unconditional | Accept — §3.5 moves MockYouTubeOptions inside mock branch |
| 13 | MEDIUM | Opus | No AC for error path broadcast cleanup | Accept — added AC-12b |
| 14 | MEDIUM | GPT | --setup-youtube testability | Dismiss — CLI utility path; manual verification adequate |
| 15 | MEDIUM | Opus | Traceability missing S-YT-8 | Accept — added to §9 |
| 16 | MEDIUM | Opus | File inventory missing seal fix + IHostedService adapter | Accept — both added to §4 |
| 17 | LOW | Opus/GPT | File inventory sln → slnx; BroadcastTitleTemplate caller; setup error handling; HLPS stale tree | Accept (where applicable) — corrected slnx, added §3.4 error handling, §3.3 clarifies template caller |
