# HLPS-020 — YouTube Token Maintenance

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED — Pending user approval |
| **Author**  | Agent |
| **Created** | 2026-05-18 |
| **Autopilot** | ENABLED |

---

## 1. Problem Statement

Two related operational defects were discovered in production after HLPS-018 was delivered.

### P1 — OAuth refresh token expires after ~7 days with no warning

The PCS Remote YouTube integration uses Google's OAuth 2.0 desktop flow. The Google Cloud project is configured with an OAuth consent screen in **Testing** status. Google explicitly limits refresh token lifetime to **7 days** for apps in testing mode. Once a refresh token expires:

- The next `InitializeAsync` call (on app startup) detects the `TokenResponseException`, deletes the stored token, and sets `YouTubeAvailability.AuthFailed` — correctly, per HLPS-018 SC4/SC5.
- The operator must run YouTube Setup via the tray icon to re-authorise.

The problem is the **absence of any proactive mitigation**. The app has no mechanism to either:
- Proactively refresh the token before it expires (exploiting Google's refresh token rotation, where a new refresh token is returned on each successful access-token refresh, sliding the 7-day window).
- Warn the operator before the token expires so re-auth can happen before the next match day.

The operator discovers the token is invalid only when the app starts on match day — when disruption is most damaging.

> **Refresh token rotation context:** When `UserCredential.RefreshTokenAsync()` succeeds and Google returns a new `refresh_token` in the response, the Google .NET client library updates the `TokenResponse` in the `IDataStore`. This slides the 7-day window. Google's rotation behaviour in testing mode is not guaranteed on every refresh, but frequent proactive refreshes maximise the probability of rotation and provide early detection of token failure before a match starts.

### P2 — Misleading tray balloon when YouTube Setup blocked during active stream

`RunOAuthSetupAsync` correctly blocks the OAuth consent flow when the service is not in `Idle` state — re-authorising while a stream is in progress would interrupt the live broadcast. However, when `RunOAuthSetupAsync` returns `false` due to the non-Idle guard, `TrayApplicationContext.OnYouTubeSetupClicked` shows this balloon:

> *"YouTube:ClientId and YouTube:ClientSecret must be set in appsettings.json."*

This message is wrong. It is the credentials-missing message, repurposed for a completely different failure mode. An operator mid-game who sees this message will incorrectly believe their `appsettings.json` has a configuration problem, not that YouTube Setup is simply unavailable while a stream is active.

---

## 2. Constraints

| ID | Constraint |
|----|-----------|
| C1 | The real-service (`YouTubeLiveStreamService`) and mock service (`MockYouTubeLiveStreamService`) must implement the same `IYouTubeLiveStreamService` interface. Any new members added to support proactive refresh or expiry signalling must be implemented by both. |
| C2 | The proactive refresh must not run when the service is not in a ready/authenticated state (`Availability != Ready`). Attempting to refresh an already-deleted or unconfigured token is a no-op. |
| C3 | A failed proactive refresh (`TokenResponseException`) must follow the same token-deletion and availability-transition path as the existing startup failure handling (HLPS-018 SPEC-S-003). No new deletion or availability logic is introduced. |
| C4 | The proactive refresh interval and the staleness warning threshold must be configurable via `YouTubeOptions` with sensible defaults. They must not be hardcoded. |
| C5 | The fix to P2 must not change the `IYouTubeLiveStreamService` interface signature. The implementation of `RunOAuthSetupAsync` is extended to allow both `Idle` and `Error` states (the existing guard is relaxed); this is an implementation change only, not a contract change. |
| C6 | The tray balloon for an expiry warning must be advisory only. Streaming operations must remain enabled; the warning does not transition the service to a degraded availability state. |
| C7 | Publishing the Google Cloud project's OAuth consent screen to "In production" is the definitive fix for the 7-day expiry. This HLPS does not implement that change. The code-level mitigations in this HLPS are a best-effort workaround for an operator who cannot or does not wish to publish the OAuth app. |

---

## 3. Success Criteria

| ID | Criterion | Addresses |
|----|-----------|-----------|
| SC1 | A background hosted service proactively calls `UserCredential.RefreshTokenAsync()` on a configurable interval (default: every 6 hours) while `Availability == Ready`. If the call succeeds and Google returns a new `refresh_token`, the `DpapiFileDataStore` persists it automatically via the Google client library, sliding the 7-day window. | P1 |
| SC2 | If a proactive refresh fails with a `TokenResponseException`, the service follows the same handling path as HLPS-018 SC5: token is deleted, availability transitions to `AuthFailed`, and `AuthStatusChanged` fires. No separate error path is introduced. | P1 |
| SC3 | The service tracks the age of the **last confirmed re-consent or refresh-token rotation**. A separate persisted marker (stored outside the `TokenResponse`) records the date of the most recent event where either (a) `RunOAuthSetupAsync` completed successfully, or (b) a proactive refresh returned a new `refresh_token` value (detected by comparing the pre- and post-refresh `refresh_token` field). When this marker is older than a configurable staleness threshold (default: 5 days), the service raises a `TokenExpiryApproaching` event. This check runs at both initialisation time and after each proactive refresh, so the warning fires even if the app was restarted after an idle period. The web UI is unaffected; the tray host subscribes and shows an advisory balloon prompting the operator to run YouTube Setup. | P1 |
| SC4 | `TrayApplicationContext.OnYouTubeSetupClicked` checks the live stream status before calling `RunOAuthSetupAsync`. If the status is `Live`, `Starting`, or `Stopping`, it shows an accurate balloon: *"YouTube Setup cannot run while streaming is active. Stop the stream first, then retry."* `RunOAuthSetupAsync` is not called in this path. For `Idle` and `Error` states, the call proceeds normally. | P2 |
| SC5 | The existing `RunOAuthSetupAsync` false-return path (credentials not configured) retains its existing balloon message. The internal guard in `RunOAuthSetupAsync` is extended to allow both `Idle` and `Error` states — in `Error` state, the stream has already failed and re-authorisation may resolve the underlying cause. Only the active-stream states (`Live`, `Starting`, `Stopping`) trigger SC4's pre-emptive message. | P2 |
| SC6 | `MockYouTubeLiveStreamService` implements any new interface members added for SC1–SC3, including the `TokenExpiryApproaching` event. The mock's default behaviour is to report as `Ready` and never fire `TokenExpiryApproaching`. | C1 |

---

## 4. Assumptions

| ID | Assumption |
|----|-----------|
| A1 | Google's refresh token rotation behaviour in testing mode means `UserCredential.RefreshTokenAsync()` calls that succeed will sometimes return a new `refresh_token`. Proactive refreshes maximise the chance of rotation but cannot guarantee it. The staleness-check warning (SC3) acts as the safety net when rotation does not occur. |
| A2 | `LiveStreamStatus.Error` is a valid state from which YouTube Setup can run — the stream has already failed and re-authorisation may resolve the underlying cause. The SC4 guard allows Setup from `Idle` and `Error` states; `RunOAuthSetupAsync`'s internal guard is extended to match. |
| A3 | `TrayApplicationContext` can safely check `_youTubeService.CurrentStatus` on the WinForms STA thread — `CurrentStatus` is lock-protected and thread-safe, consistent with the existing codebase. |
| A4 | The `TokenResponse.RefreshToken` field in the stored `TokenResponse` is updated by the Google .NET client library only when the token endpoint returns a new `refresh_token` value. Comparing this field before and after a refresh is a reliable signal for rotation detection. |
| A5 | The proactive refresh background service is most effective when the TrayHost process runs continuously between match days (the normal configuration via Task Scheduler auto-start at logon). If the app is restarted only on match day after an extended idle period, the background refresh cannot execute in advance — but the staleness check in SC3 runs at initialisation time and can still fire a warning on that startup before any match begins. |

---

## 5. Out of Scope

| Item | Rationale |
|------|-----------|
| Publishing the Google Cloud OAuth consent screen | A Google Console administrative change, outside the codebase. Documented as the definitive fix (C7). |
| Web UI indicator for token staleness | The `TokenExpiryApproaching` event is advisory; the tray is the appropriate surface for this notification. Adding a web UI indicator is a separate UX decision. |
| Allowing `RunOAuthSetupAsync` to run during `Live` / `Starting` / `Stopping` states | Deliberately out of scope. Re-authorising while a broadcast is in progress could invalidate the credential used by the active stream. The guard remains. |
| Tracking the exact Google-side refresh token issue time | Not directly available. SC3 uses a persisted re-consent/rotation marker updated when `RunOAuthSetupAsync` succeeds or when a proactive refresh returns a new `refresh_token` value. This is the basis for the staleness heuristic. |

---

## 6. Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|-------|------|-------|----------|--------------|---------|-------|
| R1 | Tier 3 | Claude Opus 4.5, GPT-5.4, GPT-5.2-Codex | CRITICAL: 2 / HIGH: 3 / MEDIUM: 1 / LOW: 3 | Accept: 4, Downgrade: 2, Reject: 0 | REVISION | Opus approved; both GPT models flagged two shared defects (severity downgraded from CRITICAL to HIGH): (1) `IssuedUtc` staleness heuristic doesn't measure rotation age — SC3 redesigned to track last confirmed re-consent/rotation via a persisted marker and compare `refresh_token` value before/after refresh; (2) SC4/A2 allowed `Error` state but C5 forbade changing `RunOAuthSetupAsync` guard — C5 updated to allow implementation guard relaxation (interface unchanged), A2 and SC5 aligned. Both GPT models also flagged missing uptime assumption (MEDIUM/HIGH) — A5 added and SC3 extended to also fire at initialisation. Opus LOW findings accepted: SC4/Error inconsistency addressed (covered above); SC6 updated to name `TokenExpiryApproaching` explicitly; Opus F-03 (implementation detail) rejected as delivery concern. |
| R2 | Tier 3 | GPT-5.4, GPT-5.2-Codex | CRITICAL: 0 / HIGH: 0 / MEDIUM: 0 / LOW: 1 | Accept: 1 | REVISION → APPROVED | GPT-5.4: clean APPROVE, all R1 fixes verified adequate, no regressions. GPT-5.2-Codex: REVISE on one LOW regression — OOS section still referenced `IssuedUtc` as "sufficient for staleness heuristic," contradicting the redesigned SC3. |
| R2-SC | Tier 0 | Self-Cert | — | — | SELF-CERTIFIED | Surgical fix to OOS item: updated single sentence to describe the persisted marker approach consistent with SC3. Tier 0 justified: trivial one-sentence alignment with already-approved SC3 text; no new logic or scope introduced. Constitutes R2 final close. |
