# PCS Remote

A Blazor Server web application and ASP.NET Core backend that remotely controls PlayCricket Scorer Pro (PCS Pro / cricket.exe) for High Halstow Cricket Club's garage PC — enabling match selection, scoreboard broadcast, and YouTube live streaming from any device on the local network.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Prerequisites](#prerequisites)
- [How to Build](#how-to-build)
- [How to Run](#how-to-run)
- [How to Test](#how-to-test)
- [Local Development (Mock Mode)](#local-development-mock-mode)
- [Production Deployment](#production-deployment)
- [YouTube Live Streaming Setup](#youtube-live-streaming-setup)
- [Configuration Reference](#configuration-reference)
- [Match-Day Quick Reference](#match-day-quick-reference)
- [Troubleshooting](#troubleshooting)
- [Project Structure](#project-structure)
- [Further Reading](#further-reading)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                  Garage PC (match day)                       │
│                                                             │
│  ┌──────────────┐     FlaUI automation      ┌────────────┐  │
│  │ PCS Remote   │ ──────────────────────── → │ PCS Pro    │  │
│  │ (Blazor      │  Launch / login / match    │ (cricket   │  │
│  │  Server +    │  selection / scoreboard    │  scorer)   │  │
│  │  TrayHost)   │  capture / start-stop     │            │  │
│  └──────┬───────┘  streaming                └─────┬──────┘  │
│         │                                          │         │
│         │ YouTube Data API v3                      │ RTMP    │
│         │ (create/bind/transition broadcast)       │ push    │
│         ▼                                          ▼         │
│  ┌─────────────────────────────────────────────────────┐     │
│  │              YouTube (cloud)                         │     │
│  │  liveBroadcast ←──── bound to ────→ liveStream      │     │
│  └─────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────┘
```

The solution is composed of seven production projects:

| Project | Target | Role |
|---|---|---|
| `PcsRemote.Core` | `net8.0` | Domain model — state machine, service contracts, shared types; **zero external dependencies** |
| `PcsRemote.Web` | `net8.0-windows` | Blazor Server web application — hosts the remote control UI and SignalR hub |
| `PcsRemote.TrayHost` | `net8.0-windows` | Windows Forms system tray host — provides manual override toggle and session management |
| `PcsRemote.Automation` | `net8.0-windows` | FlaUI-based automation service — drives cricket.exe on the Windows host |
| `PcsRemote.Automation.Mock` | `net8.0-windows` | In-process mock of the automation service — enables development without cricket.exe |
| `PcsRemote.YouTube` | `net8.0-windows` | YouTube Data API v3 integration — manages broadcast lifecycle and OAuth tokens |
| `PcsRemote.YouTube.Mock` | `net8.0` | In-process mock of the YouTube service — simulates streaming without Google credentials |

Eight test projects mirror the production structure under `tests/`.

---

## Prerequisites

### Development

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (LTS)
- Windows OS (the automation and tray host components require Win32 session access)
- Visual Studio 2022, JetBrains Rider, or VS Code with C# Dev Kit

### Production (Garage PC)

- Windows 10 or 11 (64-bit)
- Administrator account (deployment script only)
- PCS Pro (cricket.exe) installed
- Network access for LAN clients and YouTube RTMP (`rtmp://a.rtmp.youtube.com`)
- .NET runtime is **not** required separately — the published artefact is self-contained

---

## How to Build

```powershell
dotnet build PCS_Remote.slnx
```

---

## How to Run

### Full application (recommended)

```powershell
dotnet run --project src/PcsRemote.TrayHost
```

This starts the Blazor web server **and** the WinForms tray icon (manual mode toggle, Open Browser, Exit). Browse to `http://localhost:5000`.

### Web server only (faster for Blazor component dev)

```powershell
dotnet run --project src/PcsRemote.Web
```

No tray icon. Manual mode can still be toggled via the browser UI.

---

## How to Test

```powershell
dotnet test PCS_Remote.slnx
```

- **Unit tests** — MSTest 3.x with FluentAssertions >8.0
- **Component tests** — bUnit for Blazor components
- **E2E tests** — Playwright browser tests against the full application stack (requires build first)
- Test method naming: `MethodName_Scenario_ExpectedResult`

---

## Local Development (Mock Mode)

`appsettings.Development.json` enables both PCS Pro mock and YouTube mock when running via `dotnet run` — no cricket.exe or Google credentials required.

### What mock mode exercises

| Scenario | How to trigger |
|---|---|
| Full launch flow (NotRunning → MatchLoaded) | Auto-starts on app launch |
| Match list (3 fake clubs) | Appears automatically at MatchSelectionReady |
| Auto-select (single match) | Set `PcsPro:Mock:FakeMatchCount: 1` in config |
| Manual match selection | Click a match card |
| Scoreboard preview + Refresh + Change Match | Available once a match is loaded |
| Manual mode toggle | Right-click tray icon → "Switch to Manual Mode" |
| Error injection + Retry | Set `PcsPro:Mock:ErrorProbability: 0.3` or `ForcedErrorMode` |
| YouTube streaming (start/stop/cancel) | Click Start/Stop Stream buttons (mock simulates lifecycle) |

### Configuring mock delays

Edit `PcsPro:Mock:*` and `YouTube:Mock:*` values in `appsettings.Development.json` to speed up or slow down each transition. Current defaults give roughly 1–1.5 s per state step so UI changes are clearly observable.

### Post-deployment smoke test

See [`docs/guides/Smoke-Test-Checklist.md`](docs/guides/Smoke-Test-Checklist.md) for the full acceptance checklist.

---

## Production Deployment

### First-time setup

1. **Build the deployment artefact** on the developer machine:
   ```powershell
   scripts\publish.ps1
   ```
   This produces a self-contained artefact in `publish\`.

2. **Edit `publish\appsettings.json`** before copying to the garage PC:
   - Set `PcsPro:ExecutablePath` to the full path of `cricket.exe`
   - Set `PcsPro:WorkingDirectory` to the folder containing `cricket.exe`
   - Set `PcsPro:AutoLaunch` to `true`
   - Leave `PcsPro:Password` as `""` (the password is set via environment variable)
   - Configure YouTube settings (see [YouTube Live Streaming Setup](#youtube-live-streaming-setup))

3. **Run the deployment script** from an elevated PowerShell prompt on the garage PC:
   ```powershell
   scripts\Deploy-PcsRemote.ps1 -AppUser "DOMAIN\username"
   ```
   The script handles: copying files, registering the Task Scheduler task, opening the firewall, storing the PCS Pro password, and starting the application.

4. **Complete YouTube setup** if using live streaming (see below).

5. **Execute the Smoke Test Checklist** — see `docs\guides\Smoke-Test-Checklist.md`.

### Update deployments

```powershell
scripts\publish.ps1                                           # rebuild
scripts\Deploy-PcsRemote.ps1 -AppUser "DOMAIN\username" -SkipPasswordUpdate  # redeploy
```

The script detects configuration changes via SHA256 hashes and produces `.new` files for any changed settings. Merge new keys, then restart.

See [`docs/guides/Configuration-Guide.md`](docs/guides/Configuration-Guide.md) for the complete deployment and configuration reference.

---

## YouTube Live Streaming Setup

YouTube live streaming allows the operator to start and stop public YouTube broadcasts of cricket matches directly from the PCS Remote web UI. PCS Pro is the sole RTMP video source — there is no OBS involved.

> **Development note:** If you are developing locally with `YouTube:UseMock: true` in `appsettings.Development.json`, **none of these steps are required**. The mock service simulates the full broadcast lifecycle.

### Pre-requisites checklist

| # | Task | Where |
|---|------|-------|
| 1 | YouTube channel has live streaming enabled (may take 24h for new channels) | [YouTube Studio](https://studio.youtube.com) → Create → Go Live |
| 2 | Google Cloud project created | [Google Cloud Console](https://console.cloud.google.com) |
| 3 | YouTube Data API v3 enabled | Google Cloud Console → APIs & Services → Library |
| 4 | OAuth consent screen configured (External, Testing mode) | Google Cloud Console → APIs & Services → OAuth consent screen |
| 5 | OAuth 2.0 Desktop credentials created | Google Cloud Console → APIs & Services → Credentials |
| 6 | `ClientId` + `ClientSecret` added to `appsettings.json` | Garage PC |
| 7 | PCS Pro stream key matches YouTube channel's stream key | PCS Pro streaming settings |
| 8 | `--setup-youtube` run successfully (one-time OAuth consent) | Garage PC terminal |
| 9 | `LiveStreamId` obtained and added to `appsettings.json` | API Explorer or YouTube Studio |
| 10 | End-to-end test: Start Stream → Live → Stop Stream | Garage PC + phone/tablet |

### Step-by-step setup

#### 1. Enable live streaming on YouTube

1. Open [YouTube Studio](https://studio.youtube.com).
2. Sign in with the Google account that manages the club's YouTube channel.
   - **Brand Account?** Select the Brand Account identity directly in the account chooser — do NOT sign in with your personal account. See [Brand Account Considerations](#brand-account-considerations).
3. Click **Create** (camera/+ icon) → **Go Live**.
4. Enable live streaming and complete phone verification if prompted.
5. ⚠️ New channels may wait up to 24 hours for activation.

#### 2. Create a Google Cloud project

1. Open the [Google Cloud Console](https://console.cloud.google.com).
2. Sign in with the **same Google account** that manages the YouTube channel.
3. Click the project dropdown → **New Project** → name it `PCS Remote` → **Create**.

#### 3. Enable the YouTube Data API v3

1. Navigate to **APIs & Services → Library**.
2. Search for **YouTube Data API v3** → click **Enable**.

> There is no separate "YouTube Live Streaming API" — live streaming operations are part of the YouTube Data API v3.

#### 4. Configure the OAuth consent screen

1. Navigate to **APIs & Services → OAuth consent screen**.
2. Select **External** → **Create**.
3. Fill in: App name (`PCS Remote`), support email, developer contact email.
4. Add scope: `https://www.googleapis.com/auth/youtube`
5. Add test users:
   - The email of the account that will run `--setup-youtube`.
   - **Brand Account?** Add **both** the personal account email and the Brand Account email.
6. Save — leave the app in **Testing** mode (no Google verification needed).

> The consent screen will show "This app isn't verified" — this is normal. Click **Advanced** → **Go to PCS Remote (unsafe)** → **Continue**.

#### 5. Create OAuth 2.0 Desktop credentials

1. Navigate to **APIs & Services → Credentials**.
2. Click **Create Credentials → OAuth client ID**.
3. Application type: **Desktop app** → name: `PCS Remote Desktop` → **Create**.
4. Copy the **Client ID** and **Client secret**.

> ⚠️ **Security:** Never commit `ClientId` or `ClientSecret` to source control. The `appsettings.json` on the garage PC is `.gitignore`-excluded.

#### 6. Configure PCS Remote

Add to `appsettings.json` on the garage PC:

```json
{
  "YouTube": {
    "UseMock": false,
    "ClientId": "123456789-xxxxxxxxx.apps.googleusercontent.com",
    "ClientSecret": "GOCSPX-xxxxxxxxxxxxxxxxxx"
  }
}
```

Alternative: use environment variables (`YouTube__ClientId`, `YouTube__ClientSecret`, `YouTube__LiveStreamId`).

#### 7. Configure PCS Pro's stream key

1. Open PCS Pro → streaming settings (typically under "Video Display" configuration).
2. Set RTMP endpoint: `rtmp://a.rtmp.youtube.com/live2/`
3. Set the stream key from [YouTube Studio](https://studio.youtube.com) → Go Live → Stream tab → Stream key.

> ⚠️ The stream key in PCS Pro must correspond to the same `liveStream` resource used as `YouTube:LiveStreamId`. Mismatches cause stream readiness timeouts.

#### 8. First-run OAuth consent (garage PC only)

This must be run interactively (or via RDP) **as the same Windows user** that will run PCS Remote on match days.

```powershell
# From source:
dotnet run --project src/PcsRemote.TrayHost -- --setup-youtube

# From published executable:
PcsRemote.TrayHost.exe --setup-youtube
```

A browser opens for Google sign-in. Grant the "Manage your YouTube account" permission. The token is stored encrypted with Windows DPAPI at `%AppData%\PcsRemote\GoogleTokens`.

- **Brand Account?** Select the Brand Account identity in the account chooser — not your personal account.
- You only need to run this once unless the token is revoked or the Windows user changes.

#### 9. Obtain the LiveStreamId

1. Ensure PCS Pro has connected to YouTube at least once.
2. Open the [YouTube liveStreams.list API Explorer](https://developers.google.com/youtube/v3/live/docs/liveStreams/list).
3. Set `part: id,snippet` and `mine: true` → **Execute**.
4. Sign in with the same account used in Step 8.
5. Copy the `id` from the matching `liveStream` resource.
6. Add to `appsettings.json`:
   ```json
   { "YouTube": { "LiveStreamId": "AbCdEfGhIjKl" } }
   ```

#### 10. End-to-end verification

1. Start PCS Remote normally → load a match → click **▶ Start Stream**.
2. A consent confirmation dialog appears — confirm you have obtained all necessary approvals.
3. Observe status: `Idle` → `Starting` → `Live`.
4. Verify a live broadcast appears in [YouTube Studio](https://studio.youtube.com).
5. Click **⏹ Stop Stream** → verify the broadcast ends.

### Brand Account considerations

If the club's YouTube channel is a **Brand Account**:

- During OAuth consent (`--setup-youtube` or API Explorer), Google's account chooser shows both your personal account and Brand Account identities.
- **Always select the Brand Account identity directly.** Selecting your personal account binds the token to your personal channel — which may not have live streaming enabled.
- The YouTube Studio "default channel" setting is a **UI preference only** — it does NOT affect which channel API calls operate on. This has been confirmed through real-world testing.
- The `LiveStreamId` is channel-specific: obtain it while signed in as the Brand Account.

### Token storage and DPAPI

- OAuth tokens are encrypted with **Windows DPAPI** (`DataProtectionScope.CurrentUser`).
- Default location: `%AppData%\PcsRemote\GoogleTokens\` (customisable via `YouTube:TokenStorePath`).
- Tokens can only be decrypted by the **same Windows user** on the **same machine** that created them.
- The refresh token does not expire unless explicitly revoked. Access tokens are auto-refreshed.
- To revoke: visit [myaccount.google.com/permissions](https://myaccount.google.com/permissions) → remove PCS Remote → delete token file → re-run `--setup-youtube`.

### API quota

YouTube Data API v3 has a daily quota of **10,000 units** per Google Cloud project. Each complete stream cycle (start + stop) consumes approximately **222 units** — allowing ~45 stream cycles per day.

Monitor usage: Google Cloud Console → APIs & Services → Dashboard → YouTube Data API v3 → Quotas tab. Quota resets at midnight Pacific Time.

---

## Configuration Reference

### PCS Pro settings (`PcsPro:*`)

| Key | Type | Default | Description |
|---|---|---|---|
| `AutoLaunch` | bool | `true` | Auto-launch PCS Pro on application start. Shipped default is `false` — set `true` for production. |
| `ExecutablePath` | string | _(empty)_ | Full path to `cricket.exe`. Required for real mode. |
| `WorkingDirectory` | string | _(empty)_ | Working directory for the cricket.exe process. |
| `UseMock` | bool | `false` | Use mock automation service (no cricket.exe needed). |
| `Password` | string | `""` | **Leave empty.** Use the `PcsPro__Password` system environment variable instead. |

### YouTube settings (`YouTube:*`)

| Key | Type | Default | Required (prod) | Description |
|---|---|---|---|---|
| `UseMock` | bool | `false` | No | Use mock YouTube service. Set `true` in development. |
| `ClientId` | string | `""` | **Yes** | OAuth 2.0 Desktop App client ID from Google Cloud Console. |
| `ClientSecret` | string | `""` | **Yes** | OAuth 2.0 Desktop App client secret. |
| `LiveStreamId` | string | `""` | **Yes** | YouTube `liveStream` resource ID (RTMP ingest endpoint identifier). |
| `BroadcastTitleTemplate` | string | `"{HomeTeam} vs {AwayTeam}"` | No | Title template. Tokens: `{HomeTeam}`, `{AwayTeam}`, `{Date}`, `{MatchId}`. |
| `BroadcastPrivacy` | string | `"public"` | No | Broadcast privacy. Only `"public"` is supported and tested. |
| `StreamReadyTimeoutSeconds` | int | `60` | No | Max seconds to wait for RTMP stream to become active. |
| `StreamPollIntervalSeconds` | int | `3` | No | Seconds between stream readiness polls. |
| `TokenStorePath` | string | `%AppData%\PcsRemote\GoogleTokens` | No | DPAPI-encrypted token storage directory. |

### Network settings (`Kestrel:*`)

| Key | Type | Default | Description |
|---|---|---|---|
| `Kestrel:Endpoints:Http:Url` | string | `http://0.0.0.0:5000` | Bind address and port. Change requires firewall rule update. |

### Scoreboard settings

| Key | Type | Default | Description |
|---|---|---|---|
| `Scoreboard:JpegQuality` | int | `85` | JPEG compression quality (1–100). |

### Logging

Serilog is configured in code (not via `appsettings.json`). Log files: `<DeployDir>\logs\pcs-remote-YYYYMMDD.log`, 7-day rolling retention.

---

## Match-Day Quick Reference

Once all setup is complete, match-day operation is simple:

1. ✅ Ensure the garage PC is logged on — PCS Remote starts automatically via Task Scheduler.
2. 📱 Open PCS Remote in a browser on your phone/tablet: `http://{garage-pc-ip}:5000`
3. ✅ Wait for a match to load (auto-selected if only one match today).
4. ▶️ Click **Start Stream** → confirm the consent dialog → PCS Remote handles everything:
   - Creates a YouTube broadcast with the match title
   - Clicks PCS Pro's "Start Live Stream" button
   - Waits for the stream to become active
   - Transitions the broadcast to "live"
   - Shows the watch URL
5. ⏹️ When the match ends, click **Stop Stream**.

No need to touch the garage PC, YouTube Studio, or PCS Pro directly.

See [`docs/guides/Operational-Guide.md`](docs/guides/Operational-Guide.md) for the complete operator's guide.

---

## Troubleshooting

### PCS Pro issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| Status stuck on "PCS Pro not running" | PCS Pro failed to launch or `ExecutablePath` is wrong | Check `PcsPro:ExecutablePath` in `appsettings.json`. Check logs. |
| "Error" status after launch | PCS Pro crashed or login failed | Check the log file, verify the PCS Pro password environment variable. |

### YouTube streaming issues

| Symptom | Cause | Fix |
|---------|-------|-----|
| "YouTube streaming is not configured. Run the setup command first." | No OAuth token on disk | Run `--setup-youtube` on the garage PC (Step 8). |
| "YouTube:LiveStreamId is required" | `LiveStreamId` not in config | Complete Step 9. |
| "YouTube:ClientId is required" / "YouTube:ClientSecret is required" | OAuth credentials missing | Add to `appsettings.json` (Step 6). |
| "YouTube LiveStreamId '{id}' not found" | Wrong LiveStreamId or wrong channel authenticated | Re-run Step 9, ensure Brand Account selected. |
| "PCS Pro is not streaming. Waited Xs..." | Stream key mismatch or network issue | Verify PCS Pro stream key matches YouTube (Step 7). Check network to `rtmp://a.rtmp.youtube.com`. |
| Broadcasts on wrong channel / "Live streaming not enabled" | Personal account selected instead of Brand Account during OAuth | Delete token file → re-run `--setup-youtube` → select Brand Account. |
| "This app isn't verified" during OAuth | Normal for Testing mode | Click Advanced → Go to PCS Remote (unsafe) → Continue. |
| 403 Forbidden errors in logs | Token revoked or Brand Account access removed | Delete `%AppData%\PcsRemote\GoogleTokens\` → re-run `--setup-youtube`. |
| "Cannot start stream: current status is Starting/Live/Error" | Operation already in progress or error state | Wait for operation to complete, or click Dismiss to reset. |

### General

- **Log files:** `<DeployDir>\logs\pcs-remote-YYYYMMDD.log` (7-day retention)
- **Consent dialog:** Starting a stream requires operator confirmation that all necessary consents (players, officials, parents/guardians of minors) have been obtained.

---

## Project Structure

```
PCS_Remote/
├── src/
│   ├── PcsRemote.Core/              # Domain model and service contracts (net8.0)
│   ├── PcsRemote.Web/               # Blazor Server web application (net8.0-windows)
│   ├── PcsRemote.TrayHost/          # Windows Forms system tray host (net8.0-windows)
│   ├── PcsRemote.Automation/        # FlaUI Windows automation service (net8.0-windows)
│   ├── PcsRemote.Automation.Mock/   # Mock automation service (net8.0-windows)
│   ├── PcsRemote.YouTube/           # YouTube Data API v3 integration (net8.0-windows)
│   └── PcsRemote.YouTube.Mock/      # Mock YouTube streaming service (net8.0)
├── tests/
│   ├── PcsRemote.Core.Tests/        # Unit tests for domain model
│   ├── PcsRemote.Web.Tests/         # bUnit component tests
│   ├── PcsRemote.Automation.Tests/  # Automation service tests
│   ├── PcsRemote.Automation.Mock.Tests/
│   ├── PcsRemote.YouTube.Tests/     # YouTube service tests
│   ├── PcsRemote.YouTube.Mock.Tests/
│   ├── PcsRemote.TrayHost.Tests/    # TrayHost tests
│   └── PcsRemote.E2E.Tests/        # Playwright end-to-end tests
├── docs/
│   ├── guides/                      # Operational and configuration guides
│   │   ├── Configuration-Guide.md   # Authoritative deployment & config reference
│   │   ├── Operational-Guide.md     # Match-day operator's guide
│   │   └── Smoke-Test-Checklist.md  # Post-deployment verification
│   └── PCS-Remote/                  # Architecture and planning documents
│       └── PROJECT-CONTEXT.md       # Tech stack, constraints, assumptions
├── scripts/
│   ├── publish.ps1                  # Build self-contained deployment artefact
│   └── Deploy-PcsRemote.ps1        # Production deployment script
├── .editorconfig                    # Code style enforcement
├── Directory.Build.props            # Shared MSBuild properties (nullable enabled)
├── Directory.Packages.props         # Central package version management
└── PCS_Remote.slnx                  # Solution file
```

---

## Further Reading

- [Configuration Guide](docs/guides/Configuration-Guide.md) — complete deployment, config, and password management reference
- [Operational Guide](docs/guides/Operational-Guide.md) — match-day operator's guide for club volunteers
- [Smoke Test Checklist](docs/guides/Smoke-Test-Checklist.md) — post-deployment verification steps
- [PROJECT-CONTEXT.md](docs/PCS-Remote/PROJECT-CONTEXT.md) — tech stack decisions, architectural constraints, and project assumptions
- [YouTube Setup Guide](docs/PCS-Remote/HLPS-008-YouTube-LiveStream/README-YouTube-Setup.md) — detailed 18-section YouTube configuration reference (for advanced troubleshooting)
