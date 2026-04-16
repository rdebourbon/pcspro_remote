# HLPS-008: YouTube Live Stream Management

| Field | Value |
|---|---|
| **Document** | HLPS-008-YouTube-LiveStream.md |
| **Status** | DRAFT |
| **Version** | 0.2 |
| **Date** | 2026-06-15 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Dependencies** | HLPS-003 (Blazor Server web infrastructure, DI wiring), HLPS-004 (match-loaded state and scoreboard component context) |

---

## 1. Problem Statement

After a match is selected and loaded (PCS Pro state = `MatchLoaded`), the club currently requires a technically proficient operator to manually log into YouTube Studio, create a live broadcast event with the correct title, and coordinate the go-live transition with OBS. Errors at this step include wrong titles, accidentally public/private broadcasts, forgotten stop transitions, and races between the broadcast going live before OBS is ready.

This HLPS adds a **Start Stream / Stop Stream** button pair to the PCS Remote web UI, visible only when a match is loaded. Clicking Start creates a YouTube live broadcast with a configurable auto-generated title (derived from the loaded match teams), binds it to the club's pre-existing reusable OBS stream key, verifies that OBS is actively sending video, and transitions the broadcast directly to live. Clicking Stop transitions the broadcast to complete. The operator never needs to touch YouTube Studio or OBS Studio for these lifecycle actions.

PCS Remote manages only the **YouTube broadcast lifecycle** — it does not capture, encode, or transmit video. OBS is already configured on the garage PC with the club's YouTube stream key and must be running and actively streaming before a broadcast can go live.

---

## 2. Scope

### In Scope

- **New `PcsRemote.YouTube` project**: real implementation of `IYouTubeLiveStreamService` using `Google.Apis.YouTube.v3` and `Google.Apis.Auth` (target `net8.0-windows` — required by `DpapiFileDataStore`)
- **New `PcsRemote.YouTube.Mock` project**: mock implementation for development/testing without YouTube credentials (target `net8.0`)
- **`IYouTubeLiveStreamService` interface** and supporting types added to `PcsRemote.Core` (zero external dependencies maintained)
- **New domain types** in `PcsRemote.Core`: `LiveStreamStatus` enum, `LiveBroadcastInfo` record, `YouTubeStreamException`, `StreamStateSnapshot`
- **`LoadedMatch` property on `IPcsProAutomationService`**: `MatchInfo? LoadedMatch { get; }` — exposes the currently loaded match; null when state ≠ `MatchLoaded`. Must be implemented in both `PcsRemote.Automation` and `PcsRemote.Automation.Mock`.
- **OAuth 2.0 Installed App flow with DPAPI token storage**: first-run browser-based consent; refresh token persisted to disk via `DpapiFileDataStore` — a custom `IDataStore` implementation in `PcsRemote.YouTube` that encrypts content using `System.Security.Cryptography.ProtectedData` (Windows DPAPI) before writing to disk. Token file location configurable via `YouTube:TokenStorePath`.
- **Setup guard before consent**: if no valid token exists when Start Stream is clicked, show a user-friendly in-app message: *"YouTube streaming is not configured. Run the setup command first: `pcs-remote setup youtube`."* Consent is never triggered from a remote browser. A dedicated setup path (`dotnet run --setup-youtube` flag or a separate setup page) handles first-run consent on the garage PC.
- **Reusable stream binding**: the club's YouTube stream key is identified via `YouTube:LiveStreamId` config key. This ID uniquely identifies the club's reusable `liveStream` resource (the RTMP ingestion endpoint OBS connects to). The service fails fast with a clear error at startup if this key is absent or the stream ID is invalid. See Section 7 (Setup Guide) for how to obtain it.
- **Startup reconciliation**: on service startup, query `liveBroadcasts.list(mine=true, broadcastStatus=active)` to check for any broadcasts already in `live` state. If found, restore `CurrentStatus = Live` and `CurrentBroadcast` from the live broadcast. This handles app restarts during an active stream.
- **Lifecycle coupling with PCS Pro state**: `ChangeMatchButton.razor` is updated to check `IYouTubeLiveStreamService.CurrentStatus` before executing. If status ≠ `Idle`, the confirmation dialog gains an additional warning: *"⚠️ A live stream is active — it will be automatically stopped."* On confirm, `StopStreamAsync` is called before the match-change transition. If PCS Pro enters `Error` state while streaming, `StopStreamAsync` is called automatically.
- **Broadcast title template**: configurable via `YouTube:BroadcastTitleTemplate` in `appsettings.json`. Template tokens: `{HomeTeam}`, `{AwayTeam}`, `{MatchType}`, `{Date:format}`. Default: `"{HomeTeam} vs {AwayTeam}"`. See §4.5 for grammar specification.
- **Broadcast privacy**: `YouTube:BroadcastPrivacy` config key (default: `"public"`); only `"public"` is supported and tested. The config key exists for operational flexibility but no other value is validated.
- **OBS stream health check before go-live**: before calling `liveBroadcasts.transition("live")`, poll `liveStreams.list` until `streamStatus == "active"` (configurable timeout: `YouTube:StreamReadyTimeoutSeconds`, default 60). Poll interval: 3s.
- **Cancel during Starting**: a "Cancel" button is visible when `CurrentStatus == Starting`. Clicking it cancels the in-flight `StartStreamAsync` via `CancellationTokenSource`, deletes the partially-created broadcast, and resets status to `Idle`.
- **Error dismissal**: when `CurrentStatus == Error`, the error area shows a "Dismiss" button. Clicking it calls `ResetAsync()` on the service, which cleans up any partially-created broadcast and returns status to `Idle`. Start button is disabled while status is `Error`.
- **`IOperationCoordinatorService` integration**: `StreamingControls` calls `BeginOperation()` before `StartStreamAsync`/`StopStreamAsync` and `MarkComplete()` on completion or error — consistent with `ChangeMatchButton.razor`.
- **`StreamingControls.razor` Blazor component**: maroon/gold themed; rendered conditionally by `Index.razor` when `PcsProState == MatchLoaded` (consistent with `ScoreboardPreview` visibility pattern); shows current status via `StatusChanged` event; implements `IDisposable` with event unsubscription.
- **`StatusChanged` event carries full state snapshot** (`StreamStateSnapshot`) to avoid stale reads between event and subsequent property access.
- **Stream status propagated to all connected browsers** via the existing Blazor circuit + service-event pattern (consistent with scoreboard updates — no new hub needed)
- **Unit tests**: title template rendering (all tokens, DateOnly format constraints, invalid template), `LiveStreamStatus` transitions, OBS-not-ready timeout, cancel path, startup reconciliation, error dismissal
- **bUnit tests**: `StreamingControls` conditional rendering by state (parent-controlled), button enabled/disabled states for each `LiveStreamStatus`, error area and dismiss button, cancel button during Starting
- **Playwright E2E**: two browser tabs both show stream status update when Start is clicked (cross-circuit verification — bUnit cannot cover this)
- **One-time setup documentation**: inline instructions for Google Cloud project, YouTube API enablement, OAuth2 credentials, first-run consent, and obtaining the `LiveStreamId`

### Out of Scope

- OBS control via OBS WebSocket (OBS is pre-configured; operator starts OBS independently)
- Starting/stopping OBS streaming from PCS Remote
- Stream scheduling (future match pre-scheduling via the YouTube `scheduledStartTime` field)
- YouTube Live chat moderation
- Stream analytics or viewer count display
- Multiple simultaneous streams or multi-account support
- Any video capture, encoding, or RTMP ingestion from within PCS Remote

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| S-YT-1 | `StreamingControls` is rendered by `Index.razor` **only** when `PcsProState == MatchLoaded`; absent in all other states | bUnit test for `NotRunning`, `Launching`, `MatchSelection`, `Error` states |
| S-YT-2 | Clicking Start creates a YouTube broadcast with title generated from the configured template and correct `{HomeTeam}` / `{AwayTeam}` substitution | Unit test on `BroadcastTitleRenderer`; integration test against mock service verifying title passed to `StartStreamAsync` |
| S-YT-3 | `CurrentStatus` transitions from `Idle` → `Starting` (immediately, before any API call) → `Live` on a successful Start | Unit test via mock — verify `Starting` fires before any simulated API delay |
| S-YT-4 | Start button disabled unless status = `Idle`; Stop button disabled unless status = `Live`; Cancel button visible only during `Starting`; Dismiss button visible only during `Error` | bUnit test for each `LiveStreamStatus` value |
| S-YT-5 | OBS not streaming: operation times out after `YouTube:StreamReadyTimeoutSeconds` (default 60s); status → `Error`; error message shown; Dismiss resets to `Idle` | Unit test — mock returns `streamStatus != "active"` for full timeout |
| S-YT-6 | Clicking Stop transitions broadcast to `complete`; `CurrentStatus` transitions `Live` → `Stopping` → `Idle` | Unit test via mock |
| S-YT-7 | `StatusChanged` event (carrying `StreamStateSnapshot`) is reflected in all connected browser tabs simultaneously | Playwright E2E — two browser tabs both update on Start click |
| S-YT-8 | Mock implementation honours `YouTube:UseMock: true` config flag and is wired identically to real implementation at the DI boundary | Integration test with `UseMock: true` — all buttons functional without credentials |
| S-YT-9 | Template tokens `{HomeTeam}`, `{AwayTeam}`, `{MatchType}`, `{Date:d}` substituted correctly; invalid template falls back to default | Unit test for each token; unit test for malformed template fallback |
| S-YT-10 | App restarted while stream is `Live`: on next startup `CurrentStatus` is restored to `Live` and `CurrentBroadcast` is populated from the active YouTube broadcast | Unit test against mock that simulates a live broadcast present at startup |
| S-YT-11 | Clicking Cancel during `Starting` cancels the operation, deletes any partially-created broadcast, and resets status to `Idle` | Unit test — verify cancellation cleans up and status returns to Idle |
| S-YT-12 | If no YouTube token exists when Start is clicked, a clear in-app error message is shown; no browser consent is triggered from the web UI | bUnit test — mock service with no token returns a `YouTubeStreamException("not configured")`; verify message rendered |
| S-YT-13 | `ChangeMatchButton` confirmation dialog shows stream warning when `CurrentStatus != Idle`; `StopStreamAsync` is called before match-change transition | bUnit test — mock service in `Live` state; verify warning text and stop called on confirm |
| S-YT-14 | `StreamingControls` unsubscribes from all service events on `Dispose` | bUnit test — verify no `StateHasChanged` call after component is disposed |
| S-YT-15 | `IPcsProAutomationService.LoadedMatch` returns the loaded `MatchInfo` when state is `MatchLoaded`; null otherwise | Unit test against mock for both states |
| S-YT-16 | Streaming controls are themed consistently with the maroon/gold palette | Playwright screenshot smoke test |

---

## 4. Domain Types

### 4.1 `LiveStreamStatus` (in `PcsRemote.Core`)

```csharp
public enum LiveStreamStatus
{
    Idle,      // No active broadcast; Start button enabled
    Starting,  // Status set immediately on Start click; broadcast being created/bound/health-checked
    Live,      // Broadcast transitioned to live; Stop button enabled
    Stopping,  // Transition to complete in progress
    Error,     // Last operation failed; Dismiss button shown; Start button disabled until dismissed
}
```

### 4.2 `LiveBroadcastInfo` (in `PcsRemote.Core`)

```csharp
/// <summary>
/// Represents an active or recently-created YouTube live broadcast.
/// Does not carry status (use <see cref="IYouTubeLiveStreamService.CurrentStatus"/> — the single source of truth).
/// </summary>
/// <param name="BroadcastId">YouTube broadcast resource ID. Empty string while creation is in progress.</param>
/// <param name="Title">The broadcast title as set on YouTube.</param>
/// <param name="WatchUrl">Full YouTube watch URL. Empty string before broadcast is live.</param>
public record LiveBroadcastInfo(
    string BroadcastId,
    string Title,
    string WatchUrl);
```

### 4.3 `StreamStateSnapshot` (in `PcsRemote.Core`)

Carried by `StatusChanged` to give subscribers an atomic, consistent view without requiring separate property reads.

```csharp
/// <summary>
/// Immutable snapshot of streaming state, carried by <see cref="IYouTubeLiveStreamService.StatusChanged"/>.
/// </summary>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="Broadcast">Active broadcast, or null when <paramref name="Status"/> is <see cref="LiveStreamStatus.Idle"/>.</param>
/// <param name="ErrorMessage">User-facing error description, or null when <paramref name="Status"/> is not <see cref="LiveStreamStatus.Error"/>.</param>
public record StreamStateSnapshot(
    LiveStreamStatus Status,
    LiveBroadcastInfo? Broadcast,
    string? ErrorMessage);
```

### 4.4 `IYouTubeLiveStreamService` (in `PcsRemote.Core`)

```csharp
/// <summary>
/// Manages the lifecycle of a YouTube live broadcast for the currently loaded match.
/// Implementations must be registered as singletons.
/// </summary>
public interface IYouTubeLiveStreamService
{
    /// <summary>Current streaming status. The single source of truth — prefer over <see cref="LiveBroadcastInfo"/> fields for status checks.</summary>
    LiveStreamStatus CurrentStatus { get; }

    /// <summary>The active broadcast, or null when <see cref="CurrentStatus"/> is <see cref="LiveStreamStatus.Idle"/>.</summary>
    LiveBroadcastInfo? CurrentBroadcast { get; }

    /// <summary>
    /// Raised when any streaming state changes.
    /// Carries a <see cref="StreamStateSnapshot"/> for atomic consumption — do not read <see cref="CurrentStatus"/>
    /// or <see cref="CurrentBroadcast"/> inside the handler; use the snapshot.
    /// </summary>
    event EventHandler<StreamStateSnapshot> StatusChanged;

    /// <summary>
    /// Creates a YouTube broadcast with title generated from the configured template
    /// using teams from the currently loaded match (via <c>IPcsProAutomationService.LoadedMatch</c>),
    /// binds it to the configured reusable liveStream (<c>YouTube:LiveStreamId</c>),
    /// waits for OBS stream health, then transitions to live.
    /// Sets <see cref="CurrentStatus"/> to <see cref="LiveStreamStatus.Starting"/> immediately (before any API call).
    /// </summary>
    /// <exception cref="YouTubeStreamException">Thrown if the broadcast cannot be started.</exception>
    Task StartStreamAsync(CancellationToken ct = default);

    /// <summary>
    /// Transitions the active broadcast to complete.
    /// No-op (with Warning log) if <see cref="CurrentStatus"/> is not <see cref="LiveStreamStatus.Live"/>.
    /// </summary>
    Task StopStreamAsync(CancellationToken ct = default);

    /// <summary>
    /// Clears the Error state, cleaning up any partially-created broadcast, and returns to Idle.
    /// No-op if <see cref="CurrentStatus"/> is not <see cref="LiveStreamStatus.Error"/>.
    /// </summary>
    Task ResetAsync(CancellationToken ct = default);
}
```

> **Note:** `StartStreamAsync` no longer takes a `MatchInfo` parameter. It reads `IPcsProAutomationService.LoadedMatch` internally. This removes the awkward parameter threading through the UI layer and ensures the service always uses the authoritative loaded match.

### 4.5 Title Template Grammar

The `YouTube:BroadcastTitleTemplate` value is a string with named replacement tokens in curly braces:

- `{HomeTeam}` → `MatchInfo.HomeTeam`
- `{AwayTeam}` → `MatchInfo.AwayTeam`
- `{MatchType}` → `MatchInfo.MatchType`
- `{Date}` → `MatchInfo.MatchDate.ToString("d")` (short date, current culture)
- `{Date:format}` → `MatchInfo.MatchDate.ToString("format")` where `format` is a valid `DateOnly` format string. Time-related format specifiers (`H`, `h`, `m`, `s`, `t`, `z`) are not applicable to `DateOnly` and produce undefined output — the renderer must validate and reject them at startup.

**Error handling:**
- Unknown token (e.g. `{Venue}`) → retained literally in the title
- Missing `MatchInfo` field (null/empty `HomeTeam`) → substituted with empty string
- Invalid `{Date:format}` → fall back to `{Date}` (short date) and log a `Warning`
- Resulting title longer than 100 characters (YouTube API limit) → truncated to 97 chars + `"..."`
- Empty or missing `YouTube:BroadcastTitleTemplate` config → use default `"{HomeTeam} vs {AwayTeam}"`

### 4.6 `YouTubeStreamException` (in `PcsRemote.Core`)

```csharp
/// <summary>Thrown when a YouTube live stream operation fails unrecoverably.</summary>
public sealed class YouTubeStreamException : Exception
{
    public YouTubeStreamException(string message) : base(message) { }
    public YouTubeStreamException(string message, Exception inner) : base(message, inner) { }
}
```

---

## 5. Architecture

### 5.1 Project Structure Additions

```
src/
├── PcsRemote.YouTube/              # Real implementation (net8.0-windows — required by DpapiFileDataStore)
│   ├── PcsRemote.YouTube.csproj
│   ├── YouTubeLiveStreamService.cs
│   ├── DpapiFileDataStore.cs       # Custom IDataStore: encrypts with ProtectedData.Protect before file write
│   └── BroadcastTitleRenderer.cs   # Template token substitution (see §4.5)
└── PcsRemote.YouTube.Mock/         # Mock implementation (net8.0)
    ├── PcsRemote.YouTube.Mock.csproj
    └── MockYouTubeLiveStreamService.cs

tests/
├── PcsRemote.YouTube.Tests/        # Unit tests for BroadcastTitleRenderer, YouTubeLiveStreamService
│   └── PcsRemote.YouTube.Tests.csproj
└── PcsRemote.YouTube.Mock.Tests/   # Contract tests: MockYouTubeLiveStreamService honours IYouTubeLiveStreamService semantics
    └── PcsRemote.YouTube.Mock.Tests.csproj
```

`PcsRemote.YouTube` references:
- `PcsRemote.Core`
- `Google.Apis.YouTube.v3` (NuGet)
- `Google.Apis.Auth` (NuGet)

`PcsRemote.YouTube.Mock` references:
- `PcsRemote.Core` only

`PcsRemote.Web.csproj` gains two new `ProjectReference` entries:
```xml
<ProjectReference Include="..\PcsRemote.YouTube\PcsRemote.YouTube.csproj" />
<ProjectReference Include="..\PcsRemote.YouTube.Mock\PcsRemote.YouTube.Mock.csproj" />
```

### 5.2 DI Registration

`PcsRemote.Web/WebApplicationBuilderExtensions.cs` — add alongside existing `IPcsProAutomationService` registration:

```csharp
if (builder.Configuration.GetValue<bool>("YouTube:UseMock"))
    services.AddSingleton<IYouTubeLiveStreamService, MockYouTubeLiveStreamService>();
else
    services.AddSingleton<IYouTubeLiveStreamService, YouTubeLiveStreamService>();
```

### 5.3 Configuration Schema

```json
// appsettings.json
{
  "YouTube": {
    "UseMock": false,
    "ClientId": "",
    "ClientSecret": "",
    "LiveStreamId": "",
    "BroadcastTitleTemplate": "{HomeTeam} vs {AwayTeam}",
    "BroadcastPrivacy": "public",
    "StreamReadyTimeoutSeconds": 60,
    "TokenStorePath": ""
  }
}

// appsettings.Development.json
{
  "YouTube": {
    "UseMock": true
  }
}
```

| Key | Purpose | Required for prod? |
|---|---|---|
| `YouTube:UseMock` | Swap to mock implementation | No (defaults false) |
| `YouTube:ClientId` | OAuth2 desktop app client ID | Yes |
| `YouTube:ClientSecret` | OAuth2 desktop app client secret | Yes |
| `YouTube:LiveStreamId` | YouTube `liveStream` resource ID bound to the club's OBS stream key. Service fails fast at startup if absent. | Yes |
| `YouTube:BroadcastTitleTemplate` | Title template; see §4.5 | No (has default) |
| `YouTube:BroadcastPrivacy` | Broadcast privacy (`"public"` only supported/tested) | No (defaults `"public"`) |
| `YouTube:StreamReadyTimeoutSeconds` | OBS health check timeout | No (defaults 60) |
| `YouTube:TokenStorePath` | DPAPI-encrypted token file location | No (defaults `%AppData%\PcsRemote\GoogleTokens\`) |

> **Security**: `YouTube:ClientId` and `YouTube:ClientSecret` must never be committed to source control. Use `dotnet user-secrets` in development; environment variables or a secrets file excluded from `.gitignore` in production.

### 5.4 OAuth2 Token Storage (`DpapiFileDataStore`)

`DpapiFileDataStore` is a custom `IDataStore` implementation in `PcsRemote.YouTube` that:
1. Serialises the token response to JSON (same format as Google's `FileDataStore`)
2. Encrypts the bytes with `ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser)` — Windows DPAPI, current-user scope
3. Writes the encrypted bytes to `{TokenStorePath}\PcsRemote.YouTube.dat`

On read, the inverse: read bytes → `ProtectedData.Unprotect` → deserialise.

The encrypted file is only decryptable by the same Windows user account that wrote it. NTFS ACLs are a secondary defence.

**First-run consent guard**: `YouTubeLiveStreamService` checks for a valid token at startup. If no token exists or the token is revoked, it does NOT open a browser. Instead, any call to `StartStreamAsync` throws `YouTubeStreamException("YouTube not configured. Run setup first.")` which the component renders as the S-YT-12 error message. Consent is performed via a separate one-time setup path (see §7, Step 5).

### 5.5 Reusable Stream and `YouTube:LiveStreamId`

The club's YouTube channel has one reusable `liveStream` resource — the RTMP ingest endpoint OBS connects to. It has a stable resource ID (e.g. `"xxxxxxxxxxx"`). This ID is distinct from the stream key (which OBS uses).

> **What is a LiveStreamId?** When you connect OBS to YouTube, YouTube internally creates a `liveStream` resource representing the RTMP ingestion endpoint. PCS Remote needs to know this resource's ID so it can bind each new broadcast to the correct stream. The setup guide (§7) shows how to retrieve it via the YouTube Data API Explorer after OBS has connected at least once.

`YouTubeLiveStreamService` reads `YouTube:LiveStreamId` at startup. If it is empty or the API returns a 404, the service logs `Fatal` and the app fails to start (for the real implementation — mock ignores this). This prevents silent binding failures mid-match.

### 5.6 Startup Reconciliation

On service startup, `YouTubeLiveStreamService.InitializeAsync()` calls `liveBroadcasts.list(mine=true, broadcastStatus=active)`:
- If one or more `live` broadcasts are found: restore `CurrentStatus = Live`; populate `CurrentBroadcast` from the first active broadcast. Log `Warning` if multiple active broadcasts exist (ambiguous — use the most recently started).
- If no active broadcasts found: `CurrentStatus = Idle`. Also checks for `ready` state broadcasts (orphaned starts); logs each as `Warning` including the broadcast title and ID.

### 5.7 Broadcast Lifecycle Within `StartStreamAsync`

```
0.  Check IPcsProAutomationService.LoadedMatch is not null; throw if null
    Check YouTube token exists; throw YouTubeStreamException("not configured") if missing
1.  CurrentStatus = Starting; CurrentBroadcast = new LiveBroadcastInfo("", renderedTitle, ""); fire StatusChanged
2.  Render broadcast title from BroadcastTitleTemplate + LoadedMatch
3.  Insert liveBroadcast (status=created, scheduledStartTime=UtcNow, privacy=BroadcastPrivacy)
    → Update CurrentBroadcast.BroadcastId with returned ID (re-assign record)
    ct.ThrowIfCancellationRequested() ← cancel checkpoints throughout
4.  Bind broadcast to liveStream (YouTube:LiveStreamId)
5.  Poll liveStream.status.streamStatus at 3s intervals:
    a. "active" → proceed to step 6
    b. Timeout (StreamReadyTimeoutSeconds) → throw YouTubeStreamException("OBS is not streaming...")
    c. ct cancelled → delete broadcast; throw OperationCanceledException
6.  liveBroadcasts.transition(broadcastStatus: "live", id: broadcastId)
7.  Update CurrentBroadcast.WatchUrl; CurrentStatus = Live; fire StatusChanged
```

**On any exception** between steps 3-6: if a broadcast ID was obtained (step 3), attempt `liveBroadcasts.delete(broadcastId)` (best-effort, log Warning on failure); set `CurrentStatus = Error`; fire `StatusChanged` with error message.

### 5.8 `StreamingControls.razor` Component

Located at `PcsRemote.Web/Components/Streaming/StreamingControls.razor`.

**Rendering:** Conditionally rendered by `Index.razor` — `@if (pcsProService.CurrentState == PcsProState.MatchLoaded)` — consistent with `ScoreboardPreview`. The component itself does not subscribe to `IPcsProAutomationService.StateChanged`.

**Injected dependencies:** `IYouTubeLiveStreamService`, `IOperationCoordinatorService`

**Lifecycle:** `@implements IDisposable`. Subscribes to `IYouTubeLiveStreamService.StatusChanged` in `OnInitialized` (synchronous — not `OnInitializedAsync`). Unsubscribes in `Dispose()` with a `_disposed` guard before `InvokeAsync(StateHasChanged)`.

**Renders a `RadzenCard` containing:**

| Element | Condition |
|---|---|
| Status badge (colour by `LiveStreamStatus`) | Always |
| "▶ Start Stream" button | Always; disabled unless `CurrentStatus == Idle` |
| "Cancel" button | `CurrentStatus == Starting` |
| "⏹ Stop Stream" button | Always; disabled unless `CurrentStatus == Live` |
| Watch URL link (opens new tab) | `CurrentStatus == Live` |
| Error message + "Dismiss" button | `CurrentStatus == Error` |

**Operation flow:**
- Start: `IOperationCoordinatorService.BeginOperation()` → `StartStreamAsync(ct)` → `MarkComplete()` in finally
- Stop: `IOperationCoordinatorService.BeginOperation()` → `StopStreamAsync(ct)` → `MarkComplete()` in finally
- Cancel (during Starting): cancel the `CancellationTokenSource` for the in-flight `StartStreamAsync`
- Dismiss: `ResetAsync()` — no coordinator involvement (idempotent cleanup)

---

## 6. Unknowns Register

| ID | Description | Owner | Blocking? | Resolution | Status |
|---|---|---|---|---|---|
| Y-U-1 | Does the club YouTube channel have live streaming enabled? YouTube requires phone verification and may impose a 24-hour delay on new channels. | User | **Yes — blocks production use** | Operator must verify at [studio.youtube.com](https://studio.youtube.com) before first run. Development unaffected (`UseMock: true`). | ⚠️ Unresolved |
| Y-U-2 | Does a Google Cloud project exist with YouTube Data API v3 enabled and OAuth2 desktop credentials created? | User | **Yes — blocks production use** | See §7 Step 2–3. Development unaffected. | ⚠️ Unresolved |
| Y-U-3 | What is the club's YouTube `liveStream` resource ID (`YouTube:LiveStreamId`)? | User | **Yes — blocks production use** | See §7 Step 6 for how to retrieve it. Once retrieved, set in config. Development unaffected. | ⚠️ Unresolved |
| Y-U-4 | Should orphaned broadcasts (created but never live) be auto-deleted at startup? | User | No | Log Warning at startup for each orphan; operator deletes via YouTube Studio. Active cleanup deferred. | ✅ Resolved by deferral |
| Y-U-5 | Should the watch URL be copied to clipboard or shown as a link? | Agent | No | Shown as a clickable link opening in a new tab. | ✅ Resolved |

---

## 7. Setup Guide (One-Time Prerequisites)

> Development uses `YouTube:UseMock: true` and requires **none** of these steps.

### Step 1 — Enable Live Streaming on the YouTube Channel
1. Open [YouTube Studio](https://studio.youtube.com) and sign in as the club account.
2. Navigate to **Create → Go Live**.
3. If live streaming is not enabled, click **Enable** and complete phone verification. This may take up to 24 hours for new accounts.

### Step 2 — Create a Google Cloud Project
1. Open the [Google Cloud Console](https://console.cloud.google.com).
2. **Select a project → New Project**. Name it `PCS Remote`.
3. **APIs & Services → Library** → search **YouTube Data API v3** → **Enable**.

### Step 3 — Create OAuth2 Desktop Credentials
1. **APIs & Services → Credentials → Create Credentials → OAuth client ID**.
2. If prompted, configure the **OAuth consent screen**:
   - User type: **External** (or Internal if using Google Workspace)
   - App name: `PCS Remote`; add the club email as a test user
3. Application type: **Desktop app**. Name: `PCS Remote Desktop`.
4. Download the JSON. Copy `client_id` and `client_secret`.

### Step 4 — Configure PCS Remote
On the garage PC, add to `appsettings.json` (do not commit to source control):
```json
{
  "YouTube": {
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret"
  }
}
```
Or use environment variables: `YouTube__ClientId` / `YouTube__ClientSecret`.

### Step 5 — First-Run OAuth Consent (garage PC only)
Run the setup command on the garage PC (operator must be physically present, or RDP into the desktop session):
```bash
dotnet run --project src/PcsRemote.TrayHost -- --setup-youtube
```
This opens the system browser, prompts Google consent, and stores the DPAPI-encrypted token. **This command must be run at the garage PC.** Remote operators cannot initiate it from the web UI — if no token exists, clicking Start Stream shows: *"YouTube not configured. Run setup first on the garage PC."*

### Step 6 — Obtain the LiveStreamId
The `YouTube:LiveStreamId` uniquely identifies the RTMP ingest endpoint OBS connects to. To find it:
1. Ensure OBS is configured and has connected to YouTube at least once (**Settings → Stream → Service: YouTube - RTMP**).
2. Open the [YouTube Live Streaming API Explorer](https://developers.google.com/youtube/v3/live/docs/liveStreams/list) and call `liveStreams.list(part=id,snippet, mine=true)`.
3. Sign in as the club account. The response will list `liveStream` resources. Find the one whose `snippet.title` matches OBS's stream configuration (usually named `"Default stream key"`).
4. Copy the `id` field (e.g. `"xxxxxxxxxxx"`).
5. Add to `appsettings.json`:
   ```json
   {
     "YouTube": {
       "LiveStreamId": "xxxxxxxxxxx"
     }
   }
   ```

> **Why is this needed?** YouTube uses `liveStream` IDs to link broadcasts to ingest endpoints. Without this ID, PCS Remote cannot bind a new match broadcast to the OBS stream key. This is a one-time configuration — OBS's stream key does not change unless you reset it in YouTube Studio.

### Step 7 — Verify End-to-End
1. Start OBS and begin streaming (Settings → Stream → Start Streaming).
2. Start PCS Remote and select a match.
3. Click **Start Stream** in the web UI. Verify the broadcast appears in YouTube Studio as live.
4. Click **Stop Stream**. Verify the broadcast ends in YouTube Studio.
5. Future match days: start OBS streaming, select match in PCS Remote, click Start Stream.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-06-15 | Claude Opus 4.6, GPT-5.4 | NEEDS REVIEW — 3 CRITICAL, 9 HIGH, 8 MEDIUM/LOW; all 20 accepted fixes applied in v0.2 |

### R1 Findings Applied

| # | Severity | Finding | Disposition | Fix Applied |
|---|---|---|---|---|
| 1 | CRITICAL | DPAPI claim on FileDataStore is factually wrong — tokens stored plaintext | Accept | Specified `DpapiFileDataStore` using `ProtectedData.Protect`; removed false claim |
| 2 | CRITICAL | `StreamingControls` cannot access `MatchInfo` — not on any service interface | Accept | Added `MatchInfo? LoadedMatch { get; }` to `IPcsProAutomationService`; `StartStreamAsync` reads it internally |
| 3 | CRITICAL | Live broadcast orphaned when PCS Pro leaves `MatchLoaded` | Accept | `ChangeMatchButton` warns and auto-stops; `Error` state entry auto-stops |
| 4 | CRITICAL | No authorization model | Dismiss | Project decision A-4: no auth on local trusted network |
| 5 | CRITICAL | Concurrency unspecified | Merged into #7 | Covered by `IOperationCoordinatorService` integration |
| 6 | HIGH | Error state has no recovery path — UI deadlock | Accept | Added `Dismiss` button and `ResetAsync()` on interface |
| 7 | HIGH | No `IOperationCoordinatorService` integration | Accept | Specified `BeginOperation`/`MarkComplete` around Start/Stop |
| 8 | HIGH | `CurrentBroadcast` null during Starting — crash loses broadcast ID | Accept | `CurrentStatus = Starting` + placeholder `CurrentBroadcast` set before any API call (step 0→1) |
| 9 | HIGH | `CurrentStatus` set too late — double-click race | Accept | Same fix as #8 |
| 10 | HIGH | No cancel path during 60s Starting | Accept | Added Cancel button + `CancellationTokenSource`; best-effort broadcast delete on cancel |
| 11 | HIGH | No restart recovery — in-memory state lost | Accept | Startup reconciliation via `liveBroadcasts.list(active)` in `InitializeAsync` |
| 12 | HIGH | Reusable stream selection unsafe — create-if-absent, no disambiguation | Accept | `YouTube:LiveStreamId` required config key; fail-fast at startup if absent |
| 13 | HIGH | First-run OAuth triggered remotely | Accept | Setup guard: consent only via `--setup-youtube` CLI flag at garage PC |
| 14 | HIGH | Target channel unspecified | Accept | `YouTube:LiveStreamId` implicitly identifies the correct channel (stream belongs to exactly one channel) |
| 15 | HIGH | DI snippet uses undefined variable; missing project references | Accept | Fixed to `builder.Configuration`; added two `ProjectReference` entries to `PcsRemote.Web.csproj` |
| 16 | HIGH | No test projects specified | Accept | Added `PcsRemote.YouTube.Tests` and `PcsRemote.YouTube.Mock.Tests` to §5.1 |
| 17 | MEDIUM | `StatusChanged` carries only enum — stale read risk | Accept | Changed to `EventHandler<StreamStateSnapshot>` with atomic snapshot |
| 18 | MEDIUM | `IDisposable` + unsubscription not specified | Accept | Specified `@implements IDisposable`, synchronous `OnInitialized` subscribe, `Dispose` unsubscribe with `_disposed` guard |
| 19 | MEDIUM | `net8.0-windows` unjustified | Accept (adjusted) | `net8.0-windows` retained and justified — required by `DpapiFileDataStore` using `ProtectedData` |
| 20 | MEDIUM | Privacy contradiction — fixed but configurable | Accept (documented) | Config key retained; documented as only `"public"` supported/tested |
| 21 | MEDIUM | Title template grammar incomplete | Accept | Added §4.5 with full token grammar, error handling, DateOnly constraint, 100-char limit |
| 22 | MEDIUM | API quota/retry unspecified | Dismiss | Operational concern; deferred |
| 23 | MEDIUM | Visibility source unspecified | Accept | Specified parent-controlled rendering in `Index.razor` (consistent with `ScoreboardPreview`) |
| 24 | LOW | `LiveBroadcastInfo.Status` redundant | Accept | Removed `Status` from record; service is single source of truth |
