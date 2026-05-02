# IS-018 — Production Resilience & Diagnostics

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **Author**  | Agent |
| **Created** | 2026-05-02 |
| **Governs** | HLPS-018 (APPROVED) |

---

## Pre-Implementation Findings

Code audit during IS preparation revealed:

1. **All 23 `async void` event handlers already have boundary try-catch with `InvokeAsync` marshalling.** SC9's primary defense requirement is already satisfied. Only the supplementary `TaskScheduler.UnobservedTaskException` handler needs to be added.

2. **`AutomationLogDisplay.OnEntryAdded` already uses `InvokeAsync`** — U1 resolved. The Serilog sink can safely call `AddEntry()` from any thread.

3. **`StopAsync()` includes force-kill fallback** with 10-second graceful timeout — U3 resolved. Reset sequence is reliable.

---

## Implementation Sequence

### S-001 — Populate LoadedMatch in all match-loading flows

**What:** The automation service's attach-to-running-match flow (UseCurrentMatch) intentionally leaves `LoadedMatch` null (the AC-8 design decision). This must be reversed so that `LoadedMatch` is populated after any successful match load, regardless of which flow was used. Specifically, `UseCurrentMatchCoreAsync` must read team names and set `_pendingLoadedMatch` **before** firing the `AttachToMatch` trigger, mirroring the `LoadMatchAsync` pattern — so that when `StateChanged(MatchLoaded)` reaches other circuits, `LoadedMatch` is already populated.

**Why:** SC2 — all match-loading flows must make match metadata available to any circuit. Without this, S-002 cannot hydrate the UI from service state. The ordering requirement ensures SC3 cross-tab consistency without a refresh.

**Dependencies:** None (first step).

**Verification:** After calling UseCurrentMatch, `AutomationService.LoadedMatch` is non-null and contains the correct team names. In a two-circuit test, assert that at the moment the second circuit's `StateChanged(MatchLoaded)` handler is invoked, `LoadedMatch` is already non-null — not merely eventually non-null after the call returns (verifies before-trigger ordering). After calling the normal match-load flow, behaviour is unchanged (LoadedMatch already populated). After calling StopAsync or ChangeMatch, LoadedMatch is appropriately cleared. Existing AC-8 tests that assert `LoadedMatch == null` after UseCurrentMatch must be updated to assert non-null. HLPS-011 spec documentation for AC-8 must be annotated as superseded by HLPS-018.

---

### S-002 — UI match hydration and ChangeMatch button resilience

**What:** The Index.razor component must hydrate its local `_loadedMatch` from the singleton service on initialisation and on state-change events, rather than relying solely on the field being set during the original match-load user interaction. The ChangeMatch button must render whenever the state is `MatchLoaded`, using a fallback label if team metadata is unavailable.

**Why:** SC1, SC3 — the ChangeMatch button and team names must survive circuit reconnects (page refresh, tab sleep, SignalR drop) and appear correctly in multiple browser tabs.

**Dependencies:** S-001 (service must populate LoadedMatch for all flows).

**Verification:** Open the app, load a match, refresh the page — ChangeMatch button and team names are visible. Open a second tab — same display. Use the attach-to-running-match flow, refresh — same display. With two tabs open, load a match in Tab A — Tab B updates to show team names and ChangeMatch button without refresh (live cross-tab update via service event). With two tabs open, use attach-to-running-match in Tab A — Tab B updates to show team names and ChangeMatch button without refresh (verifies S-001's before-trigger ordering). With two tabs in MatchLoaded, trigger ChangeMatch in Tab A — Tab B clears team metadata and returns to appropriate non-MatchLoaded UI without refresh. If LoadedMatch were hypothetically null (edge case), a fallback label shows and the button remains functional.

---

### S-003 — YouTube token resilience on startup

**What:** The YouTube service's initialisation must handle token-related failures gracefully instead of crashing. Failures must be classified into:

- **Auth errors:** Token is deleted **only** on `invalid_grant` or `TokenResponseException` — NOT on transient 401/403 or other HTTP errors (per C6). An auth-failed flag is set.
- **Configuration errors:** Token preserved, config-error flag set (e.g., stream ID not found).
- **Transient errors:** Token preserved, degraded mode (e.g., API timeout, temporary 5xx).

The service must expose its auth/availability status **and a dedicated auth-status-changed event** (separate from the existing stream-lifecycle `StatusChanged` event) so that subscribed UI components can update without a page refresh — specifically, when auth is restored via tray re-auth. The event follows the codebase pattern used by `StateChanged` and `EntryAdded` (plain `event` delegate on the service interface). `MockYouTubeLiveStreamService` must also implement the new interface members.

**Why:** SC4, SC5 — expired/revoked tokens must not crash the app; the app must start in degraded mode with appropriate error classification.

**Dependencies:** None (independent of P1 fix).

**Verification:** Start the app with an expired token — app starts, YouTube features disabled, no crash. Start with wrong stream ID — token preserved, config error flagged. Start with API timeout — token preserved, transient error logged. Start with valid token — normal operation. Auth status is queryable from the service interface. Simulate a transient 401 — token is NOT deleted. Simulate a transient 403 — token is NOT deleted. Simulate `invalid_grant` — token IS deleted, auth-failed flag set. Simulate `TokenResponseException` — token IS deleted, auth-failed flag set. After tray re-auth, the status-changed event fires and subscribed components receive it without page refresh. In all degraded/error startup scenarios, non-YouTube automation flows (launch PCS Pro, login, load match) remain fully functional.

---

### S-004 — YouTube auth status display in streaming controls

**What:** The streaming controls section must subscribe to the YouTube service's dedicated auth-status-changed event (from S-003) and display YouTube authentication and availability status in real time. When auth has failed, a clear message directs the operator to the tray icon → YouTube Setup. When auth is restored (via tray re-auth), the message clears and streaming controls become functional — no page refresh required. When a configuration error is active, a distinct message is shown. When a transient error is active, a distinct degraded/unavailable message is shown.

**Why:** SC6 — the operator must know why streaming is unavailable and what action to take, without accessing the server. Live update on re-auth is essential because the operator re-authenticates via the tray icon on the same machine.

**Dependencies:** S-003 (service must expose auth status and fire dedicated auth-status-changed events).

**Verification:** Start app with expired token — streaming controls show auth-failed message with tray icon instructions. Re-authenticate via tray — message clears and streaming controls become functional. Start with config error — distinct message shown. Start with API timeout/5xx — distinct degraded/unavailable message shown. Start with valid token — no error message, normal controls.

**Note:** `StreamingControls` is only rendered in the `MatchLoaded` state. If YouTube auth fails before a match is loaded, the operator won't see the auth error in streaming controls until reaching `MatchLoaded`. However, auth errors are logged at Warning+ level and will appear in the debug panel (via S-005's Serilog sink) regardless of automation state, providing early visibility. A global auth-status indicator could be added as a future enhancement but is not required by SC6.

---

### S-005 — Serilog sink to debug panel

**What:** Create a Serilog sink that routes Warning-level-and-above log messages to the existing automation log service, making them visible in the web UI debug panel. Specifically:

- Add a `Warning` value to `AutomationLogOutcome`.
- Map Serilog levels: `Warning` → `AutomationLogOutcome.Warning`, `Error`/`Fatal` → `AutomationLogOutcome.Failure`.
- Render the message template to a string for the entry description.
- Wire the sink into the Serilog pipeline with `minimumLevel: Warning`. The `Program.cs` Serilog configuration must be updated to the three-argument `UseSerilog((ctx, services, cfg) => ...)` overload so the sink can resolve the singleton `IAutomationLogService` from DI.
- The sink shares the existing 200-entry ring buffer — no separate buffer. Under rapid error conditions, the buffer naturally evicts the oldest entries (C7 compliance).

**Why:** SC8 — application-level errors and warnings must be visible in the debug panel without requiring server log file access.

**Dependencies:** None (independent). Enhances operator experience for all other fixes.

**Verification:** Log a warning via Serilog in any part of the app — it appears in the debug panel with Warning styling (distinct visual style, e.g., amber badge). A CSS rule for `.automation-log-outcome-warning` must be added to `app.css`. Log an error — appears with Failure styling. Info-level messages do NOT appear (they would flood the ring buffer). The debug panel remains bounded at 200 entries under rapid error conditions. Existing `AutomationLogOutcome` tests that assert exactly 3 enum members must be updated to include `Warning`. HLPS-011 spec documentation for the enum contract must be annotated as extended by HLPS-018.

---

### S-006 — TaskScheduler.UnobservedTaskException handler

**What:** Register a `TaskScheduler.UnobservedTaskException` handler early in the web host application startup (`PcsRemote.TrayHost/Program.cs`) that logs unobserved exceptions via Serilog and marks them as observed, preventing silent process termination.

**Why:** SC9 — supplementary safety net. All existing `async void` handlers already have boundary try-catch (confirmed by code audit), but this catches any future unobserved Task exceptions from code paths that don't use the established pattern.

**Dependencies:** S-005 (Serilog sink should be wired so that logged exceptions are visible in the debug panel, not just log files).

**Verification:** The handler is registered before the host starts. In a test scenario, an unobserved Task exception is logged and observed rather than terminating the process — assert `eventArgs.Observed == true` after handler execution. Test uses forced GC (`GC.Collect()` + `GC.WaitForPendingFinalizers()`) to deterministically trigger the `UnobservedTaskException` event. Confirm that each new `async void` handler introduced in S-002, S-004, and S-007 carries boundary try-catch consistent with the existing pattern.

---

### S-007 — Reset Automation button in debug panel

**What:** Add a "Reset Automation" button to the debug panel (visible when expanded and unlocked) that performs a full reset sequence: stop active stream (best-effort), terminate PCS Pro, return to NotRunning, and re-launch the automation sequence. If an automation operation is in-progress when reset is triggered, the reset does not wait for it to complete — it proceeds directly to the stop/kill sequence as a privileged operation that bypasses the normal operation coordinator lock (the in-flight operation will naturally fail when the process is killed). The button must have a confirmation dialog and show status feedback during the reset. If any step fails, the UI displays the failure details and current system state.

**Why:** SC7 — the operator needs an emergency escape hatch when Retry/Dismiss don't recover normal operation, without requiring server access.

**Dependencies:** S-005 (Serilog sink for operator visibility during reset).

**Verification:** With PCS Pro in MatchLoaded state, click Reset — confirmation dialog appears. Confirm — PCS Pro terminates, service state transitions through NotRunning, then relaunches through login to match selection. During reset, status feedback is visible and the NotRunning intermediate state is observable. If PCS Pro hangs, force-kill is used and reset completes. If reset fails partway, the failure step and current state are displayed. With an automation operation in-progress (e.g., LoadMatchAsync running), click Reset — reset proceeds without waiting; in-flight operation fails gracefully. With a YouTube stream active, click Reset — stream stop is attempted before PCS termination; if stream stop fails, failure is shown but reset continues to completion.

---

## SC Coverage Matrix

| SC | Step(s) | Notes |
|----|---------|-------|
| SC1 | S-002 | UI hydration from service state |
| SC2 | S-001 | AC-8 reversal, LoadedMatch populated in all flows |
| SC3 | S-002 | Multi-tab and reconnect resilience |
| SC4 | S-003 | Startup crash prevention, degraded mode |
| SC5 | S-003 | Error classification, selective token deletion |
| SC6 | S-004 | Auth status display with live updates |
| SC7 | S-007 | Reset button with full sequence |
| SC8 | S-005 | Serilog sink to debug panel |
| SC9 | S-006 | UnobservedTaskException handler (existing try-catch already covers boundary) |

---

## Dependency Graph

```
S-001 ──→ S-002
S-003 ──→ S-004
S-005 ──→ S-006
  │
  └──────→ S-007
```

Independent starting points: S-001, S-003, S-005 (can be developed in any order).

S-007 depends only on S-005 (Serilog sink for operator visibility during reset).

---

## Review History

| Round | Panel | Outcome | Notes |
|-------|-------|---------|-------|
| R1 | GPT 5.4, GPT 5.2-Codex, Sonnet 4.6 | REVISE (3/3) | 6 accepted: S-003 needs explicit deletion criteria + status-changed event; S-004 needs live-update emphasis; S-005 needs outcome mapping detail; S-007 remove S-001 dep. 2 rejected: S-007←S-002 dep (reset is in DebugSection not Index); SC9 handler logging assumption (pre-existing, confirmed by audit). |
| R2 | GPT 5.4, GPT 5.2-Codex, Sonnet 4.6 | REVISE (3/3) | 7 accepted: S-003 commit to event mechanism + mock update + TokenResponseException verification; S-002 cross-tab live verification; S-004 pre-MatchLoaded visibility note; S-005 CSS rule for Warning; S-007 clarify abort semantics + expand verification scenarios. 3 rejected: conflating deletion/auth-status on 401 (Google OAuth auto-refreshes), runtime token handling (out of P2 scope), wrong-channel token (already config-error). |
| R3 | GPT 5.4, GPT 5.2-Codex, Sonnet 4.6 | APPROVE (1/3) | 9 accepted: S-001 UseCurrentMatch must set LoadedMatch before firing trigger + update AC-8 tests/spec; S-003 clarify dedicated auth event separate from StatusChanged; S-004 add transient degraded verification + note S-005 provides early visibility; S-005 three-arg UseSerilog overload + update enum tests/spec; S-006 forced GC verification approach; S-007 reset is privileged operation bypassing coordinator. 1 rejected: pre-MatchLoaded auth visibility as blocking (already scoped, Serilog sink provides debug panel visibility). |
| R4 | GPT 5.4, GPT 5.2-Codex, Sonnet 4.6 | REVISE (3/3) | 6 accepted: S-002 cross-tab exit verification (ChangeMatch clears Tab B); S-003 add 403 token-preservation case + verify non-YouTube flows still work in degraded mode; S-004 remove unspecified transient recovery clause; S-006 scope to TrayHost Program.cs; S-007 verify NotRunning intermediate state. 1 rejected: SC9 boundary try-catch false negative (OnStateChanged is sync void, not async void; SC9 targets async void). |
| R5 | GPT 5.4, GPT 5.2-Codex, Sonnet 4.6 | APPROVE (1/3) | 4 accepted: S-001 before-trigger ordering assertion in two-circuit test; S-002 cross-tab attach-to-running-match live verification; S-006 assert eventArgs.Observed + verify new async void handlers carry boundary try-catch. 0 rejected. |
