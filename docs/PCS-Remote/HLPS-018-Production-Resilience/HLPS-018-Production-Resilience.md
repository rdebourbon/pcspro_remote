# HLPS-018 — Production Resilience & Diagnostics

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED — Pending user approval |
| **Author**  | Agent |
| **Created** | 2026-05-02 |

---

## 1. Problem Statement

The first full production deployment of PCS Remote (HLPS-016 delivery) surfaced two critical and three high-severity issues that prevented the operator from managing PCS Pro remotely on match day. The server hosting PCS Remote is not physically accessible to all operators, and RDP is not available — the web UI is the **primary control surface**. Any failure mode that requires server-side intervention (restarting the TrayHost, reading log files, clearing tokens) is a production blocker.

### P1 — ChangeMatch button vanishes (Critical)

After the Blazor Server circuit reconnects (page refresh, tab sleep, SignalR drop), the "Change Match" button and team-name display disappear from the dashboard. The operator can see the scoreboard and streaming controls but cannot switch to a different match without restarting the TrayHost process on the server.

**Root cause:** The `Index.razor` component stores the loaded match metadata (`_loadedMatch`) as a local field. On circuit reconnect, the component re-initialises and reads `CurrentState == MatchLoaded` from the singleton automation service, but `_loadedMatch` is null because nothing restores it. The `ChangeMatchButton` and team-name display are rendered inside `@if (_loadedMatch is not null)`, so they vanish.

Additionally, the `UseCurrentMatchAsync` code path intentionally leaves `LoadedMatch` null on the singleton automation service. This was an original design decision (referred to internally as "AC-8" from the HLPS-008 S-002 acceptance criteria) intended to distinguish "attached to an already-running match" from "loaded a match via the selection flow." In practice this distinction provides no operator value and is the secondary cause of P1 — even if the UI attempted to hydrate from the service, it would get null for attach-flow matches. **AC-8 is now considered a design mistake and must be reversed.**

### P2 — YouTube token expiry crashes the app on startup (Critical)

When the Google OAuth token expires or becomes invalid between matches (e.g., a week apart, or after switching YouTube channels), the app crashes on startup with an unhandled `GoogleApiException`. The operator cannot recover without server access to either delete the token file or run `--setup-youtube`.

**Root cause:** `YouTubeLiveStreamService.InitializeAsync()` calls `ValidateLiveStreamIdAsync()` without a try-catch. This method makes a YouTube API call that fails if the token is expired, revoked, or bound to a different channel. The exception propagates up through the hosted-service pipeline and terminates the process. The top-level `try/catch` in `Program.cs` catches it and returns exit code 1, but on restart the same stale token causes the same crash — an infinite crash loop.

Re-authentication is only available via the tray-icon context menu ("YouTube Setup..."), which requires server access. There is no web UI path to detect or recover from auth failures.

### P3 — No emergency reset from web UI (High)

When the automation enters a state where the existing Retry/Dismiss mechanisms do not recover normal operation (e.g., the state machine is in `MatchLoaded` but the ChangeMatch button is non-functional, or the automation is stuck in an intermediate state), the only option is to restart the TrayHost process on the server. The operator needs an emergency escape hatch accessible from the web UI that performs a full reset: abort any in-progress operation, terminate the PCS Pro process, return the state machine to `NotRunning`, and then re-launch the full automation sequence (launch → login → match selection).

### P4 — Errors not visible in web UI debug panel (High)

The debug panel shows only curated automation-action log entries (launch, login, match load, stream start/stop). Application-level errors, warnings, and diagnostic messages logged via Serilog are only visible in the server-side log files. When the scoreboard refresh fails silently or the YouTube token is invalid, the operator has no visibility into what went wrong without accessing the server.

### P5 — Unhandled exceptions in async event handlers can silently terminate the process (High)

Multiple Blazor components use `async void` event handlers (e.g., `OnStateChanged`, `OnOperationInProgressChanged`, `OnManualModeChanged`). If an exception escapes one of these handlers, it propagates to the captured synchronisation context (or the thread pool if none is captured), which terminates the process with no diagnostic output. There is no catch-all safety net registered for either `async void` escapes or unobserved `Task` exceptions (`TaskScheduler.UnobservedTaskException`).

---

## 2. Constraints

| ID | Constraint |
|----|-----------|
| C1 | The web UI is the **primary control surface** on match day. All failure recovery — except YouTube re-authentication (see C4) — must be achievable from the web UI without server access. |
| C2 | Changes must be backward-compatible with the existing state machine, DI architecture, and event-driven UI update pattern. |
| C3 | The existing debug section (a collapsible panel in the Blazor layout, optionally protected by a configurable PIN) is the appropriate location for advanced/destructive controls (reset button) and diagnostic log output. |
| C4 | YouTube re-authentication requires a browser-based OAuth consent flow that must run on the server (Google's `GoogleWebAuthorizationBroker` opens a localhost callback). For this HLPS, the web UI must clearly direct the operator to the tray icon "YouTube Setup..." option. A full web-triggered OAuth flow is out of scope. The assumption is that at least one person with physical or remote-desktop access to the server is available within a reasonable timeframe (not necessarily during the match itself). |
| C5 | FlaUI automation calls must not be triggered from component initialisation (too slow, contention risk). Service-side cached state is the only acceptable hydration source. |
| C6 | Token deletion must only occur on unambiguous authentication failures — specifically Google's `invalid_grant` error or explicit `TokenResponseException`. HTTP 401/403 responses alone are not sufficient grounds for deletion as they may indicate transient issues. |
| C7 | The debug panel log display must use a bounded ring buffer (matching the existing automation log service's capacity of 200 entries) to prevent unbounded memory growth and UI flooding from rapid error loops. |

---

## 3. Success Criteria

| ID | Criterion | Addresses |
|----|-----------|-----------|
| SC1 | After a Blazor circuit reconnect (page refresh, tab sleep, SignalR drop), the ChangeMatch button and team-name display are restored without requiring operator interaction or FlaUI calls. If team metadata is unavailable, a fallback label (e.g., "Match loaded") is shown and the button remains functional. | P1 |
| SC2 | All match-loading flows — including the attach-to-running-match flow — make match metadata available so that any current or future circuit can display it without re-querying the automation target. This explicitly reverses the prior AC-8 decision (HLPS-008 S-002), which is now considered a design mistake because it breaks UI resilience across circuit reconnects. | P1 |
| SC3 | When a second browser tab or a reconnected circuit receives a state transition to `MatchLoaded`, it displays the correct team names and ChangeMatch button without additional operator action. | P1 |
| SC4 | An expired, revoked, or wrong-channel YouTube token does not crash the app on startup. The app starts in a degraded "YouTube unavailable" mode with all non-YouTube functionality intact. | P2 |
| SC5 | When the YouTube API rejects a token with an unambiguous auth error (specifically `invalid_grant` or `TokenResponseException`), the stale token is deleted and an auth-failed flag is set. Configuration errors (wrong stream ID) and transient API errors preserve the token and set appropriate distinct flags. | P2 |
| SC6 | The streaming controls section of the web UI clearly displays YouTube auth status. On auth failure, it shows a message directing the operator to the tray icon YouTube Setup. The message updates live when auth is restored (via tray re-auth) without requiring a page refresh. | P2 |
| SC7 | The debug panel contains a "Reset Automation" button (visible when expanded and unlocked) that, after a confirmation dialog: (a) aborts any in-progress coordinated operation, (b) stops any active stream (best-effort), (c) terminates the PCS Pro process, (d) returns the state machine to `NotRunning`, and (e) re-launches the full automation sequence. The operator sees status feedback during the reset. If any step in the reset sequence fails, the UI displays the failure, the step at which it occurred, and the system's current state, so the operator can determine whether server-side intervention is required. | P3 |
| SC8 | All Serilog messages at Warning level and above automatically appear in the web UI debug panel, with appropriate outcome mapping (Warning → Warning, Error/Fatal → Failure). The display uses the same bounded ring buffer as the existing automation log (C7). No per-call-site instrumentation is required for new code. | P4 |
| SC9 | All `async void` event handlers catch exceptions at their boundary, log them via Serilog, and prevent them from propagating to the synchronisation context (which would terminate the process). As a supplementary safety net, `TaskScheduler.UnobservedTaskException` is registered to log and observe any unobserved `Task` exceptions that escape through other code paths. | P5 |

---

## 4. Assumptions

| ID | Assumption |
|----|-----------|
| A1 | The web UI will provide clear, actionable instructions directing the operator to the tray icon for YouTube re-authentication (see C4 for the underlying access assumption). |
| A2 | The existing `AutomationLogService` ring buffer (200 entries) is sufficient capacity for both curated automation log entries AND Serilog-sourced warning/error entries. |
| A3 | The `PcsProAutomationService` remains a singleton — there is only ever one PCS Pro instance being automated, and all circuits share the same state. |
| A4 | The state machine's existing `StopAsync()` method is sufficient to cleanly terminate PCS Pro and return to `NotRunning`. If PCS Pro hangs, process-kill is an acceptable last resort. |

---

## 5. Unknowns Register

| ID | Description | Owner | Blocking |
|----|-------------|-------|----------|
| U1 | **Serilog sink thread safety with Blazor circuits.** The Serilog sink will call `AutomationLogService.AddEntry()` from arbitrary threads (Serilog's pipeline is multi-threaded). `AddEntry()` currently uses `lock` internally and fires `EntryAdded` synchronously. Blazor circuits subscribing to `EntryAdded` must marshal back to their synchronous context via `InvokeAsync`. Need to verify the existing `OnEntryAdded` handler in components already does this correctly. | Agent | No |
| U2 | **Token deletion race with OAuth refresh.** When an `invalid_grant` error or `TokenResponseException` triggers token deletion (per SC5/C6), an in-progress OAuth refresh (from another thread or Google's token refresh middleware) could write a new token concurrently. Need to determine if `DpapiFileDataStore.DeleteAsync()` is atomic with respect to concurrent `StoreAsync()` calls, or if additional coordination is needed. | Agent | No |
| U3 | **Post-reset automation re-launch reliability.** The reset flow calls `StopAsync()` → `LaunchAndLoginAsync()` in sequence. If `StopAsync()` fails to cleanly terminate PCS Pro (e.g., process hangs, unresponsive WinForms app), the re-launch may fail or produce duplicate PCS Pro instances. Need to verify `StopAsync()` includes a process-kill fallback and properly awaits termination. | Agent | No |

---

## 6. Out of Scope

| Item | Rationale |
|------|-----------|
| Web-triggered YouTube OAuth consent flow | Requires browser redirect URI handler in Blazor Server; significant scope. Tray-icon path is sufficient for this HLPS (C4). |
| Persisting `LoadedMatch` across TrayHost process restarts | Process restart means PCS Pro state is unknown; match must be re-established. |
| `AppDomain.UnhandledException` handler | The top-level `try/catch` in `Program.cs` already catches synchronous startup exceptions. The gap is async task exceptions, addressed by SC9. |
| Refactoring `OperationCoordinatorService` to add timeout-based auto-release | Valid hardening but not part of the reported production issues. |

---

## 7. Review History

| Round | Panel | Outcome | Notes |
|-------|-------|---------|-------|
| R1 | Sonnet 4.6, GPT 5.4, GPT 5.2-Codex | REVISE | All three reviewers. Key findings: C1/C4 contradiction (3 reviewers), unknowns register too dismissive (3), AC-8 reversal unacknowledged (1), SC5 needs tighter token-delete criteria (1), SC8 needs volume bounds (2), P3/SC7 "reset" undefined (1), SC2/SC3 too implementation-prescriptive (1), missing assumptions section (2), issue count text wrong (1). All critical/high findings accepted and addressed. |
| R2 | Sonnet 4.6, GPT 5.4, GPT 5.2-Codex | REVISE | All R1 findings confirmed addressed. New findings: U2 references wrong exception type conflicting with C6/SC5 (all 3, High — fixed), SC2 still prescribes storage mechanism (2 of 3, Medium — rewritten as observable), P5/SC9 wrong mitigation mechanism for async void (1, High — reframed to boundary try-catch + supplemental UnobservedTaskException), SC7 missing reset failure mode (1, Medium — added failure clause), A1 duplicates C4 (1, Low — simplified). |
| R3 | Sonnet 4.6, GPT 5.4, GPT 5.2-Codex | APPROVED | Unanimous. All R2 findings verified as adequately addressed. No regressions detected. |
