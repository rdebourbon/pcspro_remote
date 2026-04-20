# YouTube Live Streaming — Setup & Configuration Guide

> **Audience:** Operator deploying PCS Remote on the garage PC.
> **Prerequisite knowledge:** Basic Google account management, appsettings.json editing.
> **Development note:** If you are developing locally with `YouTube:UseMock: true` in `appsettings.Development.json`, **none of these steps are required**. The mock service simulates the full broadcast lifecycle without any Google or YouTube configuration.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Pre-Requisites Checklist](#2-pre-requisites-checklist)
3. [Step 1 — Enable Live Streaming on the YouTube Channel](#3-step-1--enable-live-streaming-on-the-youtube-channel)
4. [Step 2 — Create a Google Cloud Project](#4-step-2--create-a-google-cloud-project)
5. [Step 3 — Enable the YouTube Data API v3](#5-step-3--enable-the-youtube-data-api-v3)
6. [Step 4 — Configure the OAuth Consent Screen](#6-step-4--configure-the-oauth-consent-screen)
7. [Step 5 — Create OAuth 2.0 Desktop Credentials](#7-step-5--create-oauth-20-desktop-credentials)
8. [Step 6 — Configure PCS Remote (appsettings.json)](#8-step-6--configure-pcs-remote-appsettingsjson)
9. [Step 7 — Configure the PCS Pro Stream Key](#9-step-7--configure-the-pcs-pro-stream-key)
10. [Step 8 — First-Run OAuth Consent (Garage PC Only)](#10-step-8--first-run-oauth-consent-garage-pc-only)
11. [Step 9 — Obtain the LiveStreamId](#11-step-9--obtain-the-livestreamid)
12. [Step 10 — End-to-End Verification](#12-step-10--end-to-end-verification)
13. [Brand Account Considerations](#13-brand-account-considerations)
14. [Token Storage & DPAPI](#14-token-storage--dpapi)
15. [API Quota & Monitoring](#15-api-quota--monitoring)
16. [Match-Day Quick Reference](#16-match-day-quick-reference)
17. [Troubleshooting](#17-troubleshooting)
18. [Configuration Reference](#18-configuration-reference)

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                  Garage PC (match day)                       │
│                                                             │
│  ┌──────────────┐     FlaUI automation      ┌────────────┐  │
│  │ PCS Remote   │ ──────────────────────── → │ PCS Pro    │  │
│  │ (Blazor      │  Start/Stop streaming      │ (cricket   │  │
│  │  Server)     │  click buttons via UI      │  scorer)   │  │
│  └──────┬───────┘                            └─────┬──────┘  │
│         │                                          │         │
│         │ YouTube Data API v3                      │ RTMP    │
│         │ (create/bind/transition broadcast)       │ push    │
│         │                                          │         │
│         ▼                                          ▼         │
│  ┌─────────────────────────────────────────────────────┐     │
│  │              YouTube (cloud)                         │     │
│  │  liveBroadcast ←──── bound to ────→ liveStream      │     │
│  │  (match title,       (RTMP ingest endpoint,          │     │
│  │   watch URL)          receives PCS Pro video)        │     │
│  └─────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────┘
```

**Key point:** PCS Pro is the sole RTMP video source. It has built-in streaming capability with a pre-configured YouTube stream key. There is no OBS involved. PCS Remote automates PCS Pro's "Start Live Stream" / "Stop" buttons via FlaUI and manages the YouTube broadcast lifecycle via the YouTube Data API.

**Streaming sequence:**

1. **Start:** PCS Remote creates a YouTube broadcast → binds it to the stream → clicks PCS Pro's "Start Live Stream" button → polls YouTube until the stream is active → transitions the broadcast to "live"
2. **Stop:** PCS Remote clicks PCS Pro's "Stop" button → transitions the YouTube broadcast to "complete"

---

## 2. Pre-Requisites Checklist

Complete these items **once** before the first match day. Tick each off as you go.

| # | Task | Where | Done? |
|---|------|-------|-------|
| 1 | YouTube channel has live streaming enabled | YouTube Studio | ☐ |
| 2 | Google Cloud project created | Google Cloud Console | ☐ |
| 3 | YouTube Data API v3 enabled | Google Cloud Console | ☐ |
| 4 | OAuth consent screen configured | Google Cloud Console | ☐ |
| 5 | OAuth 2.0 Desktop credentials created | Google Cloud Console | ☐ |
| 6 | `ClientId` + `ClientSecret` added to `appsettings.json` | Garage PC | ☐ |
| 7 | PCS Pro stream key configured to match YouTube channel | PCS Pro settings | ☐ |
| 8 | `--setup-youtube` run successfully (OAuth consent) | Garage PC | ☐ |
| 9 | `LiveStreamId` obtained and added to `appsettings.json` | Garage PC | ☐ |
| 10 | End-to-end test: Start Stream → Live → Stop Stream | Garage PC | ☐ |

---

## 3. Step 1 — Enable Live Streaming on the YouTube Channel

1. Open [YouTube Studio](https://studio.youtube.com).
2. Sign in with the Google account that manages the club's YouTube channel.
   - **Brand Account?** If the club channel is a Brand Account, select the **Brand Account identity directly** in the Google account chooser — do NOT sign in with your personal account. The YouTube Studio "default channel" setting is a UI preference only and does not affect which channel the API (or Studio features like live streaming) operates on. See [Brand Account Considerations](#13-brand-account-considerations) for details.
3. Click **Create** (the camera/+ icon) → **Go Live**.
4. If live streaming is not already enabled:
   - Click **Enable**.
   - Complete **phone verification** when prompted.
   - ⚠️ **New channels may wait up to 24 hours** for live streaming to activate.
5. Verify: you should see the Live Control Room with stream setup options.

---

## 4. Step 2 — Create a Google Cloud Project

1. Open the [Google Cloud Console](https://console.cloud.google.com).
2. Sign in with the **same Google account** used in Step 1 (the one that manages the YouTube channel).
3. Click the project dropdown (top-left, next to "Google Cloud") → **New Project**.
4. **Project name:** `PCS Remote` (or any name you prefer).
5. **Organisation:** leave as "No organisation" unless your club uses Google Workspace.
6. Click **Create**.
7. Wait for the notification confirming the project is created, then **select** it as the active project.

---

## 5. Step 3 — Enable the YouTube Data API v3

1. In the Google Cloud Console, navigate to **APIs & Services → Library** (left sidebar).
2. Search for **YouTube Data API v3**.
3. Click on the result, then click **Enable**.
4. Wait for the API to be enabled (a few seconds).

> **Note:** There is no separate "YouTube Live Streaming API" — live streaming operations (broadcasts, streams) are part of the YouTube Data API v3.

---

## 6. Step 4 — Configure the OAuth Consent Screen

This tells Google what your app is and who can use it.

1. Navigate to **APIs & Services → OAuth consent screen** (left sidebar).
2. If prompted to choose user type:
   - Select **External** (standard for club/personal use outside Google Workspace).
   - Click **Create**.
3. Fill in the required fields:
   - **App name:** `PCS Remote`
   - **User support email:** your club email address
   - **Developer contact information:** same email
4. Click **Save and Continue**.
5. **Scopes** screen: click **Add or Remove Scopes**:
   - Search for `youtube` or scroll to find `https://www.googleapis.com/auth/youtube`
   - Check the box next to it
   - Click **Update** → **Save and Continue**
6. **Test users** screen: click **Add Users**:
   - Add the email address of the Google account that will run `--setup-youtube` on the garage PC.
   - **Brand Account?** Add **both** the personal Google account email **and** the Brand Account email. During `--setup-youtube`, the operator selects the Brand Account identity in the account chooser — Google needs the Brand Account listed as a test user for consent to succeed.
   - Click **Save and Continue**.
7. Review the summary and click **Back to Dashboard**.

### Why "External" and "Testing" mode is fine

PCS Remote stays in **Testing** mode (the default). This means:

- Only accounts listed as **test users** can authorise the app (max 100 test users — more than enough for a single club).
- **No Google app verification review is needed** (verification is only required for "Production" mode apps with sensitive scopes accessed by the general public).
- The consent screen will show a "This app isn't verified" warning — this is normal and expected. Click **Continue** when prompted during first-run setup.

---

## 7. Step 5 — Create OAuth 2.0 Desktop Credentials

1. Navigate to **APIs & Services → Credentials** (left sidebar).
2. Click **Create Credentials → OAuth client ID**.
3. **Application type:** select **Desktop app**.
4. **Name:** `PCS Remote Desktop`.
5. Click **Create**.
6. A dialog shows your **Client ID** and **Client secret**. Click **Download JSON** to save a backup, then copy both values.

> ⚠️ **Security:** Never commit `ClientId` or `ClientSecret` to source control. Store them in `appsettings.json` on the garage PC only (this file is `.gitignore`-excluded), or use environment variables.

---

## 8. Step 6 — Configure PCS Remote (appsettings.json)

On the garage PC, edit `appsettings.json` (located next to the PCS Remote executable):

```json
{
  "YouTube": {
    "UseMock": false,
    "ClientId": "123456789-xxxxxxxxx.apps.googleusercontent.com",
    "ClientSecret": "GOCSPX-xxxxxxxxxxxxxxxxxx",
    "BroadcastTitleTemplate": "{HomeTeam} vs {AwayTeam}",
    "BroadcastPrivacy": "public",
    "StreamReadyTimeoutSeconds": 60,
    "StreamPollIntervalSeconds": 3
  }
}
```

**Minimum required keys for production:**

| Key | Value | Required? |
|-----|-------|-----------|
| `YouTube:UseMock` | `false` | Yes (defaults to `false`) |
| `YouTube:ClientId` | Your OAuth client ID from Step 5 | **Yes** |
| `YouTube:ClientSecret` | Your OAuth client secret from Step 5 | **Yes** |
| `YouTube:LiveStreamId` | Obtained in Step 9 below | **Yes** (added after Step 9) |

See [Configuration Reference](#18-configuration-reference) for all available keys.

**Alternative: environment variables**

Instead of `appsettings.json`, you can set:
```
YouTube__ClientId=123456789-xxxxxxxxx.apps.googleusercontent.com
YouTube__ClientSecret=GOCSPX-xxxxxxxxxxxxxxxxxx
YouTube__LiveStreamId=xxxxxxxxxxx
```

Or use `dotnet user-secrets` during development:
```bash
dotnet user-secrets set "YouTube:ClientId" "your-client-id" --project src/PcsRemote.Web
dotnet user-secrets set "YouTube:ClientSecret" "your-secret" --project src/PcsRemote.Web
```

---

## 9. Step 7 — Configure the PCS Pro Stream Key

PCS Pro must be configured to stream to the same YouTube RTMP endpoint that PCS Remote manages broadcasts for.

1. Open **PCS Pro** on the garage PC.
2. Navigate to streaming settings (the exact path depends on your PCS Pro version — typically under the "Video Display" configuration).
3. Set the RTMP endpoint to YouTube's ingest URL: `rtmp://a.rtmp.youtube.com/live2/`
4. Set the **Stream key** to your YouTube channel's stream key:
   - Find it in [YouTube Studio](https://studio.youtube.com) → **Go Live** → **Stream** tab → **Stream key** (click "Show" to reveal).
   - Copy and paste it into PCS Pro's stream key field.
5. Save the configuration.

> ⚠️ **Critical (Constraint C-8):** The stream key configured in PCS Pro **must** correspond to the same `liveStream` resource that PCS Remote will use as `YouTube:LiveStreamId`. If they differ, PCS Pro will push video to the wrong ingest point and PCS Remote's stream readiness poll will time out.

---

## 10. Step 8 — First-Run OAuth Consent (Garage PC Only)

This step authorises PCS Remote to manage YouTube broadcasts on your channel. It opens a browser window for Google sign-in and must be performed interactively on the garage PC (or via RDP into the desktop session).

1. Open a terminal on the garage PC.
2. Run:

   **From source:**
   ```bash
   dotnet run --project src/PcsRemote.TrayHost -- --setup-youtube
   ```

   **From published executable:**
   ```bash
   PcsRemote.TrayHost.exe --setup-youtube
   ```

3. A browser window opens showing the Google sign-in page.
4. Sign in with the Google account that manages the club's YouTube channel.
   - **Brand Account?** When the account chooser appears, select the **Brand Account identity directly** — do NOT select your personal Google account. Signing in as the personal account will bind the token to your personal channel (which may not have live streaming enabled), even if the Brand Account is your "default channel" in YouTube Studio. See [Brand Account Considerations](#13-brand-account-considerations).
5. You may see **"This app isn't verified"** — this is expected in Testing mode:
   - Click **Advanced** → **Go to PCS Remote (unsafe)** → **Continue**.
6. Grant the requested permission: **"Manage your YouTube account"**.
7. The browser shows a success message. Return to the terminal.
8. The terminal should display:
   ```
   YouTube OAuth2 setup complete. Token stored at C:\Users\{user}\AppData\Roaming\PcsRemote\GoogleTokens
   ```

> **Important:**
> - This command must be run as the **same Windows user** that will run PCS Remote on match days (DPAPI encryption is tied to the Windows user account — see [Token Storage & DPAPI](#14-token-storage--dpapi)).
> - If no token exists when a user clicks "Start Stream" in the web UI, they will see: *"YouTube streaming is not configured. Run the setup command first."*
> - You only need to run this once unless the token is revoked or the Windows user account changes.

---

## 11. Step 9 — Obtain the LiveStreamId

The `YouTube:LiveStreamId` is a YouTube resource identifier for the RTMP ingest endpoint that PCS Pro streams to. It is **not** the same as the stream key.

### Option A — Using the YouTube Data API Explorer (recommended)

1. **First:** ensure PCS Pro has connected to YouTube at least once (so YouTube has created the `liveStream` resource).
2. Open the [YouTube Live Streaming API Explorer — liveStreams.list](https://developers.google.com/youtube/v3/live/docs/liveStreams/list).
3. Set parameters:
   - **part:** `id,snippet`
   - **mine:** `true`
4. Click **Execute**.
5. Sign in with the Google account that manages the club's YouTube channel.
   - **Brand Account?** Make sure you're signed in with the same account used in Step 8.
6. In the response, find the `liveStream` resource whose `snippet.title` matches your stream configuration (usually `"Default stream key"` or similar).
7. Copy the `id` field value (e.g., `"AbCdEfGhIjKl"`).

### Option B — Using YouTube Studio

1. Open [YouTube Studio](https://studio.youtube.com) → **Go Live** → **Stream** tab.
2. The stream key details page may show the stream ID in the URL or page metadata. If not visible, use Option A.

### Add to configuration

Add the `LiveStreamId` to `appsettings.json`:

```json
{
  "YouTube": {
    "LiveStreamId": "AbCdEfGhIjKl"
  }
}
```

> **Why is this needed?** When PCS Remote creates a new broadcast for a match, it must bind that broadcast to the correct RTMP ingest endpoint. The `LiveStreamId` tells the YouTube API which stream to bind to. Without it, PCS Remote fails fast at startup: *"YouTube:LiveStreamId is required. See the setup guide."*

---

## 12. Step 10 — End-to-End Verification

1. Start PCS Remote normally (not with `--setup-youtube`).
2. Verify the application starts without errors — check the console or log file for:
   - `YouTube token validated successfully` (or similar)
   - No `CRITICAL` or `ERROR` log entries about YouTube configuration.
3. Select a match in the PCS Remote web UI.
4. Click **▶ Start Stream**.
5. Observe the status badge progression: `Idle` → `Starting` → `Live`.
6. Verify in [YouTube Studio](https://studio.youtube.com) that a live broadcast appears with the match title.
7. Click **⏹ Stop Stream** in PCS Remote.
8. Verify the broadcast ends in YouTube Studio.

**If something goes wrong**, see [Troubleshooting](#17-troubleshooting).

---

## 13. Brand Account Considerations

If your club's YouTube channel is a **Brand Account** (rather than a personal channel), there are important nuances:

### What is a Brand Account?

A Brand Account is a YouTube channel identity that can be managed by multiple Google accounts. It is separate from any personal Google account. You can check if your channel is a Brand Account in [YouTube Studio → Settings → Account](https://studio.youtube.com/channel/UC/editing/account) — if you see "Your channel has a Brand Account," it's a Brand Account.

### How OAuth works with Brand Accounts

- During OAuth consent (whether via `--setup-youtube` or the API Explorer), Google's account chooser presents **both** your personal Google account **and** any Brand Account identities you manage.
- **You must select the Brand Account identity directly.** Selecting your personal account binds the token to your personal channel — which likely does not have live streaming enabled and does not own the club's `liveStream` resource.
- The YouTube Studio "default channel" setting is a **UI preference only** — it does NOT affect which channel API calls (including `mine=true`) operate on. The API always operates on the channel of the authenticated identity.
- The OAuth token is bound to whichever identity you selected in the account chooser.

### Key gotchas

| Issue | Impact | Mitigation |
|-------|--------|------------|
| Selecting the personal account instead of the Brand Account during OAuth | Token bound to personal channel — API calls fail (e.g., "live streaming not enabled") or target the wrong channel. The "default channel" setting in YouTube Studio does **not** redirect API calls. | **Always select the Brand Account identity** in the account chooser during `--setup-youtube`. This is confirmed by real-world testing. |
| The `LiveStreamId` is channel-specific | If the token is bound to a different channel than the one that owns the `LiveStreamId`, the service will fail at startup with "LiveStreamId not found" | Obtain the `LiveStreamId` while signed in as the Brand Account (Step 9), and ensure `--setup-youtube` was also run with the Brand Account selected |
| Token is tied to the selected identity | If the person who ran `--setup-youtube` loses access to the Brand Account, the token becomes invalid | Ensure the person running `--setup-youtube` has permanent Owner access to the Brand Account |
| Multiple Brand Accounts in the account chooser | The operator might accidentally select the wrong Brand Account | Verify during end-to-end testing (Step 10) that broadcasts appear on the correct channel |

### Recommendation

When running `--setup-youtube` or the API Explorer, **always select the Brand Account identity directly** in the account chooser. Do not select your personal Google account — even if the Brand Account is your "default channel" in YouTube Studio, that setting has no effect on API authentication. This has been confirmed through real-world testing: signing in with the personal account produces errors about live streaming not being enabled, while signing in directly as the Brand Account works correctly.

---

## 14. Token Storage & DPAPI

PCS Remote uses a custom `DpapiFileDataStore` to securely store the OAuth refresh token.

### How it works

1. After OAuth consent, Google returns an access token + refresh token.
2. `DpapiFileDataStore` serialises the token response to JSON.
3. The JSON bytes are encrypted with **Windows DPAPI** (`ProtectedData.Protect`) using `DataProtectionScope.CurrentUser`.
4. The encrypted bytes are written to disk.

### Token file location

| Configuration | Path |
|---------------|------|
| Default (no `TokenStorePath` set) | `%AppData%\PcsRemote\GoogleTokens\` |
| Custom (`YouTube:TokenStorePath`) | Whatever path you specify |

The actual file is: `{TokenStorePath}\Google.Apis.Auth.OAuth2.Responses.TokenResponse-user`

### Important constraints

- **Same Windows user:** The token file can only be decrypted by the **same Windows user account** that created it. If you run `--setup-youtube` as user `Admin` but run PCS Remote as user `CricketPC`, the token cannot be decrypted.
- **Same machine:** DPAPI keys are tied to the local machine. Moving the token file to a different PC will not work — you must run `--setup-youtube` again on the new machine.
- **Token lifetime:** The refresh token does not expire unless explicitly revoked. The access token expires every ~1 hour but is automatically refreshed by the Google client library using the stored refresh token.

### Revoking access

If you need to revoke PCS Remote's access to your YouTube account:

1. Go to [myaccount.google.com/permissions](https://myaccount.google.com/permissions).
2. Find **PCS Remote** in the list of third-party apps.
3. Click **Remove Access**.
4. Delete the token file from `%AppData%\PcsRemote\GoogleTokens\`.
5. Re-run `--setup-youtube` if you want to re-authorise.

---

## 15. API Quota & Monitoring

YouTube Data API v3 has a daily quota of **10,000 units per Google Cloud project**. PCS Remote's usage is well within this limit for a single club.

### Per-stream cost breakdown

| Operation | API Call | Units |
|-----------|----------|-------|
| Validate stream config (startup) | `liveStreams.list` | 1 |
| Reconcile active broadcasts (startup) | `liveBroadcasts.list` | 1 |
| Create broadcast | `liveBroadcasts.insert` | 50 |
| Bind broadcast to stream | `liveBroadcasts.bind` | 50 |
| Poll stream readiness (~20 polls) | `liveStreams.list` × 20 | 20 |
| Transition to live | `liveBroadcasts.transition` | 50 |
| **Start subtotal** | | **~172** |
| Transition to complete (stop) | `liveBroadcasts.transition` | 50 |
| **Full cycle total** | | **~222** |

With 10,000 units/day, you could run **~45 complete stream cycles per day** — far more than any cricket club needs.

### Monitoring quota usage

1. Open [Google Cloud Console](https://console.cloud.google.com).
2. Navigate to **APIs & Services → Dashboard**.
3. Click on **YouTube Data API v3**.
4. View the **Quotas** tab to see daily usage.
5. Optionally set up quota alerts: **IAM & Admin → Quotas → Create Alert**.

### Quota resets

The daily quota resets at **midnight Pacific Time (PT)**.

### If you hit quota limits

- This should never happen with normal club use (1-3 streams per day).
- If it does: check for runaway polling, repeated failed starts (each failed start may consume ~120 units before cleanup), or other API consumers sharing the same Google Cloud project.
- You can request a quota increase via the Google Cloud Console (free, but requires justification and may take several days for approval).

---

## 16. Match-Day Quick Reference

Once all setup is complete, match-day operation is simple:

1. ✅ Ensure PCS Pro is running and a match is loaded.
2. ✅ Ensure PCS Remote is running (it starts automatically via Task Scheduler).
3. 📱 Open PCS Remote in a browser on your phone/tablet: `http://{garage-pc-ip}:5000`
4. ▶️ Click **Start Stream** — PCS Remote handles everything:
   - Creates a YouTube broadcast with the match title
   - Clicks PCS Pro's "Start Live Stream" button
   - Waits for the stream to become active
   - Transitions the broadcast to "live"
   - Shows the watch URL
5. ⏹️ When the match ends, click **Stop Stream**.

No need to touch the garage PC, YouTube Studio, or PCS Pro directly.

---

## 17. Troubleshooting

### "YouTube streaming is not configured. Run the setup command first."

**Cause:** No valid OAuth token found on disk.
**Fix:** Run `--setup-youtube` on the garage PC (Step 8).

### "YouTube:LiveStreamId is required. See the setup guide."

**Cause:** `LiveStreamId` is not set in `appsettings.json`.
**Fix:** Complete Step 9 to obtain and configure the `LiveStreamId`.

### "YouTube:ClientId is required" or "YouTube:ClientSecret is required"

**Cause:** OAuth credentials not configured.
**Fix:** Add `ClientId` and `ClientSecret` to `appsettings.json` (Step 6).

### "YouTube LiveStreamId '{id}' not found"

**Cause:** The configured `LiveStreamId` does not match any stream on the authenticated YouTube channel.
**Possible reasons:**
- The stream was deleted in YouTube Studio.
- You are authenticated as a different channel than the one that owns the stream.
- **Brand Account:** The token may be querying the wrong channel (see [Brand Account Considerations](#13-brand-account-considerations)).
**Fix:** Re-run Step 9 to obtain the correct `LiveStreamId`.

### "PCS Pro is not streaming. Waited Xs for stream to become active."

**Cause:** PCS Remote clicked "Start Live Stream" in PCS Pro, but YouTube did not detect an active RTMP stream within the timeout period.
**Possible reasons:**
- PCS Pro's stream key does not match the YouTube channel's stream key.
- PCS Pro's RTMP endpoint is misconfigured.
- Network issues preventing the RTMP stream from reaching YouTube.
- The `LiveStreamId` in `appsettings.json` does not correspond to PCS Pro's configured stream key.
**Fix:**
1. Verify PCS Pro's stream key matches the one in YouTube Studio (Step 7).
2. Verify `YouTube:LiveStreamId` matches the stream resource (Step 9).
3. Check network connectivity from the garage PC to `rtmp://a.rtmp.youtube.com`.
4. Increase `YouTube:StreamReadyTimeoutSeconds` if on a slow connection.

### "This app isn't verified" warning during OAuth consent

**Expected behaviour** in Testing mode. Click **Advanced** → **Go to PCS Remote (unsafe)** → **Continue**.

### Token expired or revoked (403 Forbidden errors in logs)

**Cause:** The refresh token was revoked (e.g., via [myaccount.google.com/permissions](https://myaccount.google.com/permissions)) or the account's access to the Brand Account was removed.
**Fix:**
1. Delete the token file: `%AppData%\PcsRemote\GoogleTokens\`
2. Re-run `--setup-youtube` (Step 8).

### "Cannot start stream: current status is Starting/Live/Error"

**Cause:** A stream operation is already in progress or the service is in an error state.
**Fix:** Wait for the current operation to complete, or click **Dismiss** (if in Error state) to reset to Idle, then try again.

### Broadcasts appearing on the wrong YouTube channel / "Live streaming not enabled" errors

**Cause:** During `--setup-youtube` (or API Explorer), the operator selected their **personal Google account** instead of the **Brand Account identity** in the account chooser. The YouTube Studio "default channel" setting does NOT affect which channel the API operates on.
**Fix:** 
1. Delete the token file: `%AppData%\PcsRemote\GoogleTokens\`
2. Re-run `--setup-youtube` (Step 8).
3. In the account chooser, select the **Brand Account** identity — not your personal account.

---

## 18. Configuration Reference

All configuration keys live under the `YouTube` section in `appsettings.json`.

| Key | Type | Default | Required | Description |
|-----|------|---------|----------|-------------|
| `UseMock` | `bool` | `false` | No | When `true`, uses `MockYouTubeLiveStreamService` (no real YouTube calls). Set `true` in `appsettings.Development.json`. |
| `ClientId` | `string` | `""` | **Yes** (prod) | OAuth 2.0 Desktop App client ID from Google Cloud Console. |
| `ClientSecret` | `string` | `""` | **Yes** (prod) | OAuth 2.0 Desktop App client secret from Google Cloud Console. |
| `LiveStreamId` | `string` | `""` | **Yes** (prod) | YouTube `liveStream` resource ID. Identifies the RTMP ingest endpoint PCS Pro streams to. Obtained via Step 9. |
| `BroadcastTitleTemplate` | `string` | `"{HomeTeam} vs {AwayTeam}"` | No | Title template for new broadcasts. Supported tokens: `{HomeTeam}`, `{AwayTeam}`, `{Date}`, `{MatchId}`. |
| `BroadcastPrivacy` | `string` | `"public"` | No | Broadcast privacy setting. Only `"public"` is supported and tested. |
| `StreamReadyTimeoutSeconds` | `int` | `60` | No | Maximum seconds to wait for PCS Pro's RTMP stream to become active after clicking "Start Live Stream". |
| `StreamPollIntervalSeconds` | `int` | `3` | No | Interval in seconds between stream readiness polls during the Starting phase. |
| `TokenStorePath` | `string` | *(see below)* | No | Directory for DPAPI-encrypted token storage. Defaults to `%AppData%\PcsRemote\GoogleTokens`. |

### Mock-specific configuration (development only)

Under `YouTube:Mock` in `appsettings.Development.json`:

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `StartDelayMs` | `int` | `1500` | Simulated delay for `StartStreamAsync` in milliseconds. |
| `StopDelayMs` | `int` | `500` | Simulated delay for `StopStreamAsync` in milliseconds. |
| `SimulateStartFailure` | `bool` | `false` | When `true`, `StartStreamAsync` throws a simulated error. |
| `SimulateActiveOnStartup` | `bool` | `false` | When `true`, mock starts in `Live` state (simulates an active broadcast found at startup). |
