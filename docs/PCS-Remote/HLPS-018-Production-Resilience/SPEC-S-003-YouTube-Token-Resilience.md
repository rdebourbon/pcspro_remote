# SPEC-S-003 — YouTube Token Resilience on Startup

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **Author**  | Agent |
| **Created** | 2026-05-03 |
| **Governs** | IS-018 S-003 |
| **Depends** | None |

---

## Problem

`YouTubeLiveStreamService.InitializeAsync` calls `ValidateLiveStreamIdAsync` without any exception handling. If the YouTube API call fails — because the OAuth token expired, was revoked, the stream ID is wrong, or the API is transiently unavailable — the entire application crashes during hosted-service startup. The operator must then access the server to restart the app, which is unacceptable in production (the server is physically inaccessible on match days).

Additionally, there is no way for the UI to know *why* YouTube streaming is unavailable, and no mechanism for the UI to receive live updates when auth is restored via the tray icon's "YouTube Setup" flow.

---

## Requirements

### R1 — Wrap `InitializeAsync` in error classification

The entire body of `InitializeAsync` — including `ValidateConfiguration()`, token retrieval, `ValidateLiveStreamIdAsync`, and `ReconcileActiveBroadcastsAsync` — must be wrapped in error-classification logic so that no exception propagates to the hosted-service runner and crashes the app. The existing partial catch around `ReconcileActiveBroadcastsAsync` must be replaced by the same classification logic (not supplemented), since a `TokenResponseException` surfacing from that method's API calls would otherwise be silently swallowed as a generic warning without triggering token deletion or setting the appropriate flag.

Failures are classified into three categories:

1. **Auth failure** (any `TokenResponseException`, regardless of the specific error code — including `invalid_grant`, `invalid_client`, etc.) → delete the stored token, set an auth-failed flag, log as Error, transition to degraded mode where YouTube features are disabled but the rest of the app runs normally.

2. **Configuration error** (e.g., stream ID not found via `YouTubeStreamException`, or missing configuration values from `ValidateConfiguration` which throws `InvalidOperationException`) → preserve the token, set a config-error flag, log as Error.

3. **Transient API error** (network timeout, temporary 5xx, `Google.GoogleApiException` with HTTP status codes, generic `HttpRequestException`, etc.) → preserve the token, log as Warning, enter degraded mode.

In all three cases, `InitializeAsync` must return normally (not throw). The app must start successfully.

### R2 — Auth status properties on the interface

Add to `IYouTubeLiveStreamService`:

- A property indicating whether the service is authenticated and ready (i.e., token is valid, configuration is valid, API is reachable). The exact name and type are deferred to delivery.
- A property indicating the specific failure category (auth failed, config error, transient error, or none). The exact representation (enum, nullable string, etc.) is deferred to delivery.

These properties must be readable from any thread (they are used by UI components via `InvokeAsync`).

### R3 — Dedicated auth-status-changed event

Add an event to `IYouTubeLiveStreamService` that fires whenever the auth/availability status changes. This is a **separate event** from the existing `StatusChanged` event (which tracks broadcast lifecycle). The event must:

- Fire when `InitializeAsync` transitions to a degraded/error state.
- Fire when `RunOAuthSetupAsync` restores auth (after successful re-initialisation).
- Follow the codebase pattern: plain `event EventHandler<TArgs>` on the interface, with an immutable snapshot argument.

This enables S-004's streaming controls to subscribe and update in real time when the operator re-authenticates via the tray icon, without requiring a page refresh.

### R4 — Token deletion criteria

Token deletion must occur **only** when the error conclusively proves the token is permanently invalid:

- Any `TokenResponseException` (including `invalid_grant`, `invalid_client`, and other OAuth error codes) → DELETE token. The `TokenResponseException` type itself is the deletion signal, not a specific error-code string.
- Transient 401/403 HTTP errors (from `Google.GoogleApiException` with HTTP status codes) → DO NOT delete. Google's OAuth library handles automatic token refresh; a 401 may simply mean the access token expired but the refresh token is still valid.
- `HttpRequestException`, timeout, 5xx → DO NOT delete.
- `YouTubeStreamException` (stream ID not found) → DO NOT delete (it's a config problem, not a token problem).

### R5 — Mock service must implement new interface members

`MockYouTubeLiveStreamService` must implement the new properties and event from R2/R3. The mock should always report as authenticated/ready (its default state). The mock's `RunOAuthSetupAsync` should fire the auth-status-changed event.

### R6 — Non-YouTube flows unaffected in degraded mode

When YouTube initialisation fails (any category), all non-YouTube automation flows — launch PCS Pro, login, load match, scoreboard refresh, change match — must remain fully functional. The YouTube failure must be contained to the YouTube service.

---

## Acceptance Criteria

### AC-1 — Expired token does not crash the app

Given a stored token that produces `TokenResponseException` with `invalid_grant` when used, when the app starts, then `InitializeAsync` completes normally, the token is deleted, the auth-failed flag is set, and YouTube features are disabled.

### AC-2 — Wrong stream ID does not crash the app

Given a valid token but a stream ID that returns zero results from the API, when the app starts, then `InitializeAsync` completes normally, the token is preserved, the config-error flag is set, and YouTube features are disabled.

### AC-3 — Transient API error does not crash the app

Given a valid token but the YouTube API returns a transient 5xx or network error, when the app starts, then `InitializeAsync` completes normally, the token is preserved, and YouTube enters degraded mode.

### AC-4 — Transient 401 does not delete token

Given a `Google.GoogleApiException` with HTTP 401 status, when thrown during `ValidateLiveStreamIdAsync`, then the token is NOT deleted (Google's library will refresh it on next use).

### AC-5 — Transient 403 does not delete token

Given a `Google.GoogleApiException` with HTTP 403 status, then the token is NOT deleted.

### AC-6 — Auth status is queryable

After initialisation, the service's auth/availability properties correctly reflect the current state (authenticated, auth-failed, config-error, or transient-error).

### AC-7 — Auth-status-changed event fires on degradation

When `InitializeAsync` transitions to any degraded/error state, the auth-status-changed event fires with a snapshot reflecting the new state.

### AC-8 — Auth-status-changed event fires on re-auth

When `RunOAuthSetupAsync` completes successfully and re-initialisation succeeds, the auth-status-changed event fires indicating auth is restored. (For the case where `RunOAuthSetupAsync` completes but `InitializeAsync` subsequently enters degraded mode, see AC-7.)

### AC-9 — Mock implements new members

`MockYouTubeLiveStreamService` compiles and implements all new interface members. Default state is authenticated/ready. `RunOAuthSetupAsync` fires the auth-status-changed event.

### AC-10 — Non-YouTube flows work in degraded mode

After any YouTube initialisation failure, automation operations (launch PCS Pro, login, load match, scoreboard refresh, change match) succeed without interference from the YouTube service's degraded state.

### AC-11 — `StartStreamAsync` guard message is clear

When `StartStreamAsync` is called while in any degraded state (auth-failed, config-error, or transient-error), the exception message clearly indicates the specific failure category and what action to take.

---

## Test Strategy

Tests are written against `YouTubeLiveStreamService` using the existing Moq-based test infrastructure (`_dataStoreMock`, `_options`, `CreateService()`). Key test scenarios:

- **TC-1:** `InitializeAsync` with `TokenResponseException(invalid_grant)` → returns normally, token deleted, auth-failed flag set. The test intent is exception-type-based classification — any `TokenResponseException` variant (not just `invalid_grant`) must trigger token deletion. A second scenario with a different error code (e.g., `invalid_client`) must verify this.
- **TC-2:** `InitializeAsync` with `YouTubeStreamException` (stream not found) → returns normally, token preserved, config-error flag set.
- **TC-3:** `InitializeAsync` with `HttpRequestException` → returns normally, token preserved, transient-error flag set.
- **TC-4:** `InitializeAsync` with `GoogleApiException(401)` → returns normally, token NOT deleted.
- **TC-5:** `InitializeAsync` with `GoogleApiException(403)` → returns normally, token NOT deleted.
- **TC-6:** Successful `InitializeAsync` → authenticated flag true, no error flags.
- **TC-7:** Auth-status-changed event fires on auth failure.
- **TC-8:** `RunOAuthSetupAsync` → auth-status-changed event fires on success.
- **TC-9:** Mock service implements new members, default state is ready.
- **TC-10:** `StartStreamAsync` after auth failure throws with descriptive message.
- **TC-11:** `ReconcileActiveBroadcastsAsync` throws `TokenResponseException(invalid_grant)` → token deleted, auth-failed flag set (verifies the existing partial catch is replaced, not supplemented).
- **TC-12:** `InitializeAsync` where `ValidateConfiguration` throws `InvalidOperationException` due to missing config value → returns normally, token preserved, config-error flag set.

**AC-10 (non-YouTube flows unaffected)** is validated at integration level. The YouTube service is a self-contained singleton with no shared state that could block automation flows. No unit TC is defined for AC-10 because the isolation is architectural (separate service, separate DI registration) rather than behavioural.

Testing of `ValidateLiveStreamIdAsync` failure requires the mock `IDataStore` to return a valid `TokenResponse` (so the service creates a `YouTubeService`) and then the YouTube API call to throw. Since the `YouTubeService` is constructed internally, the test approach will depend on the delivery phase's strategy for making the API call mockable or interceptable (e.g., custom `HttpMessageHandler`, or extracting the validation into a virtual/overridable method).

---

## Review History

| Round | Panel | Outcome | Notes |
|-------|-------|---------|-------|
| R1 | GPT 5.4, Sonnet 4.6 | REVISE (0/2) | 7 accepted: expand catch to entire InitializeAsync body incl. ValidateConfiguration (GPT F1); clarify all TokenResponseException → DELETE (GPT F2); expand AC-10 to all R6 flows (GPT F3 + Sonnet F-02); replace ReconcileActiveBroadcastsAsync partial catch with same classification (Sonnet F-01); expand AC-11 to all degraded states (Sonnet F-03); annotate TC-1 for type-based classification (Sonnet F-04); add AC-8 cross-reference (Sonnet F-05). 1 rejected: R3 event delegate type too prescriptive (GPT F4 — IS-018 explicitly mandates this pattern). |
| R2 | GPT 5.4, Sonnet 4.6 | APPROVE (1/2) | GPT 5.4: clean APPROVE, no findings. Sonnet: 2 accepted — R2-F01: specify `InvalidOperationException` for `ValidateConfiguration` in R1 category 2 + add TC-12; R2-F02: change TC-1 "should" to "must" for second TokenResponseException variant. |
