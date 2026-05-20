# SPEC-IS-020-S-004 — Background Refresh Scheduler and Tray Advisory Balloon

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **Step** | IS-020 S-004 |
| **Branch** | `feature/IS-020-S-004-background-scheduler` |
| **Governing IS** | IS-020-YouTube-Token-Maintenance.md |
| **Governing HLPS** | HLPS-020-YouTube-Token-Maintenance.md |

---

## Context

S-003 added `RunProactiveRefreshAsync` to `IYouTubeLiveStreamService` and wired the staleness check and token-expiry-approaching event to the real service. This step adds the two remaining pieces that complete P1:

1. **Background hosted service** — calls `RunProactiveRefreshAsync` on the configured interval while the service is in the ready state.
2. **Tray advisory balloon** — subscribes to `TokenExpiryApproaching` and shows a balloon advising the operator to run YouTube Setup before the token expires.

Both pieces live in `PcsRemote.TrayHost` because: the background scheduler is not required when running the Web standalone entry point (no match-day operator is present to act on the balloon), and the tray balloon surface is tray-host-specific.

---

## Requirements

### R-1 — Background hosted service: `YouTubeTokenRefreshService`

A new `BackgroundService` named `YouTubeTokenRefreshService` is added to `PcsRemote.TrayHost`. Its `ExecuteAsync` loop:

1. Creates a `PeriodicTimer` whose interval is read from `YouTubeOptions.ProactiveRefreshIntervalHours` (already added in S-003, default 6 hours).
2. On each tick, checks whether `IYouTubeLiveStreamService.Availability == YouTubeAvailability.Ready`. If not ready, skips the tick without logging (the service is not in a state where proactive refresh is useful — `RunProactiveRefreshAsync` itself also guards, but the outer check avoids a pointless async call).
3. If ready, calls `IYouTubeLiveStreamService.RunProactiveRefreshAsync(stoppingToken)`. If `RunProactiveRefreshAsync` throws an exception other than `OperationCanceledException`, catches it, logs a warning, and continues the loop — the scheduler must be resilient. `OperationCanceledException` propagates (signals host shutdown and exits the loop cleanly).
4. Stops when `stoppingToken` is cancelled (normal host shutdown).

No logging on skip (availability != Ready) — this is the common idle path during non-match periods and should not pollute the log.

### R-2 — Timer abstraction (`IPeriodicTimer`)

`PcsRemote.TrayHost` defines its own `IPeriodicTimer` (internal) and `RealPeriodicTimer` (internal sealed) following the identical pattern used in `PcsRemote.Web`. The interface is not shared across projects — it is a per-project internal testing seam. `YouTubeTokenRefreshService` accepts a `Func<IPeriodicTimer>` factory as a second constructor parameter (test-only constructor), with the production constructor building the factory from `YouTubeOptions`.

### R-3 — DI registration

`YouTubeTokenRefreshService` is registered as a hosted service in `Program.cs` (TrayHost entry point), inside the `else` branch that handles the real YouTube service (not when `YouTube:UseMock` is set). This mirrors the placement of `YouTubeInitializerHostedService` in `WebApplicationBuilderExtensions`, but in the tray host's own setup code — the scheduler is tray-specific and must not be registered in the shared `WebApplicationBuilderExtensions`.

When `YouTube:UseMock` is true, `MockYouTubeLiveStreamService.RunProactiveRefreshAsync` is a no-op and `TokenExpiryApproaching` never fires — there is no functional benefit to running the scheduler in mock mode. Excluding the registration keeps mock mode behaviour clean and avoids unnecessary timer overhead during development.

### R-4 — Tray advisory balloon

`TrayApplicationContext` subscribes to `IYouTubeLiveStreamService.TokenExpiryApproaching` in its constructor, following the same subscribe-before-read pattern used for `ManualModeChanged` and `StatusChanged`. The handler marshals to the STA thread using `_invoker.BeginInvoke` and shows a balloon with:

- **Title:** `"YouTube Token"`
- **Text:** `"YouTube token is approaching expiry. Run YouTube Setup to re-authorise before the token expires."`
- **Icon:** `ToolTipIcon.Warning`

The handler is unsubscribed in `Dispose`, alongside the existing unsubscriptions.

The balloon is advisory only — no availability state transition occurs and no streaming operations are gated or suppressed (HLPS-020 C6). Streaming remains enabled throughout. The event fires at most once per staleness check cycle; the rate is bounded by the proactive refresh interval (default 6 hours) and the staleness threshold (default 5 days).

The tray balloon handler requires a live Win32 message queue and cannot be exercised in a headless test environment. This is consistent with the project convention for all `TrayApplicationContext` balloon tests (S-001 TC-1–TC-4, S-005 TC-4–TC-8, S-007 TC-3 are all `[Ignore]` stubs; IS-020 and prior ISes were approved under this pattern). Advisory-only correctness (C6) is enforced at the service layer in S-003 and is not dependent on the balloon handler.

---

## Test Cases

All tests follow project conventions: MSTest 3.x + FluentAssertions, naming `MethodName_Scenario_ExpectedResult`. New hosted service tests are added to a new file `YouTubeTokenRefreshServiceTests.cs` in `PcsRemote.TrayHost.Tests`. Tray context balloon tests are added as stubs (headless environment cannot create `NotifyIcon`) in `TrayApplicationContextTests.cs`.

### `YouTubeTokenRefreshService` tests (runnable, not `[Ignore]`)

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-1 | `ExecuteAsync_WhenReady_CallsRefreshOnEachTick` | `Availability == Ready`; timer ticks twice | `RunProactiveRefreshAsync` called twice |
| TC-2 | `ExecuteAsync_WhenNotReady_SkipsRefresh` | `Availability == NotConfigured`; timer ticks | `RunProactiveRefreshAsync` not called |
| TC-3 | `ExecuteAsync_WhenCancelled_StopsGracefully` | `stoppingToken` cancelled after first tick | Loop exits without throwing; no unhandled exception |

### `TrayApplicationContext` balloon tests (stubs, `[Ignore]` — require STA message queue)

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-4 | `OnTokenExpiryApproaching_ShowsAdvisoryBalloon` | `TokenExpiryApproaching` fires | Advisory balloon shown with correct title and warning icon |

---

## Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `YouTubeTokenRefreshService` exists in `PcsRemote.TrayHost` and implements `BackgroundService` |
| AC-2 | `YouTubeTokenRefreshService` is registered as a hosted service in `Program.cs` (TrayHost), inside the real-YouTube branch only |
| AC-3 | `TrayApplicationContext` subscribes to `TokenExpiryApproaching` and unsubscribes in `Dispose` |
| AC-4 | Advisory balloon uses the specified title, text, and `ToolTipIcon.Warning` |
| AC-5 | TC-1 through TC-3 are runnable (not `[Ignore]`) and pass |
| AC-6 | TC-4 is present as a stub with `[Ignore]` |
| AC-7 | All existing tests continue to pass (no regressions) |
| AC-8 | Solution builds with zero warnings |

---

## Documentation Updates

None required beyond the spec itself.

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 2 | GPT-5.4 | HIGH: 1 / MEDIUM: 3 / LOW: 1 | F-1 Defer, F-2 Accept, F-3 Accept, F-4 Accept, F-5 Accept | REVISION | F-1 HIGH: TC-4 only an ignored stub — defer: established project convention for all `TrayApplicationContext` balloon tests; advisory-only semantics enforced at service layer (S-003), not balloon layer. F-2 MEDIUM: `OperationCanceledException` swallowed in refresh catch — fixed with `when (ex is not OperationCanceledException)`. F-3 MEDIUM: C6 missing "streaming stays enabled" half — R-4 now explicitly states no streaming operations are gated or suppressed. F-4 MEDIUM: mock-branch exclusion rationale missing — R-3 now explicitly documents why scheduler is excluded in mock mode. F-5 LOW: balloon text not expiry-focused — updated to "Run YouTube Setup to re-authorise before the token expires." |
| R1-SC | Tier 0 | Self-Cert | — | — | APPROVED | All five R1 findings addressed. Tier 0 self-cert confirms no scope creep or regressions introduced. |
