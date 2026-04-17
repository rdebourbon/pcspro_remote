# SPEC-S-003: PcsRemote.YouTube.Mock and MockYouTubeLiveStreamService

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-YouTube-Mock.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.2 |
| **Date** | 2026-04-17 |
| **IS Step** | S-003 |
| **Governing HLPS** | HLPS-008-YouTube-LiveStream.md v0.4 (APPROVED) |
| **Governing IS** | IS-008-YouTube-LiveStream.md v0.3 (APPROVED) |
| **Branch** | `feature/IS-008-S-003-youtube-mock` |

---

## 1. Objective

Create the `PcsRemote.YouTube.Mock` project containing `MockYouTubeLiveStreamService` — a mock implementation of `IYouTubeLiveStreamService` that simulates YouTube live stream lifecycle transitions with configurable delays and failure modes. This enables full UI development and testing (S-004) without YouTube credentials.

---

## 2. Scope

### In Scope

1. **`PcsRemote.YouTube.Mock` project** — target `net8.0`, references `PcsRemote.Core` only. Namespace: `PcsRemote.YouTube.Mock`.

2. **`MockYouTubeOptions` class** — configuration POCO for mock behaviour:
   - `StartDelayMs` (int, default 1500) — simulated delay between `Starting` and `Live`
   - `StopDelayMs` (int, default 500) — simulated delay between `Stopping` and `Idle`
   - `SimulateStartFailure` (bool, default false) — if true, `StartStreamAsync` transitions to `Error` instead of `Live`
   - `SimulateActiveOnStartup` (bool, default false) — if true, `InitializeAsync` reconciles to `Live` state (S-YT-10)

3. **`MockYouTubeLiveStreamService`** implementing `IYouTubeLiveStreamService`:

   **Constructor:** Accepts `IOptions<MockYouTubeOptions>`, `ILogger<MockYouTubeLiveStreamService>`, `BroadcastTitleRenderer`, `IPcsProAutomationService`.

   **Properties:**
   - `CurrentStatus` — returns the current `LiveStreamStatus`
   - `CurrentBroadcast` — returns the current `LiveBroadcastInfo?`
   - `StatusChanged` — event fired at each transition with `StreamStateSnapshot`

   **Methods:**
   - `InitializeAsync(ct)` — if `SimulateActiveOnStartup`, transitions to `Live` with a synthetic broadcast. Otherwise logs information and returns.
   - `StartStreamAsync(ct)` — precondition: must be `Idle` and `LoadedMatch` must be non-null (throws `InvalidOperationException` otherwise). Transitions `Idle → Starting` (fires event), waits `StartDelayMs`, then either transitions `Starting → Live` (fires event) or `Starting → Error` if `SimulateStartFailure`. Uses `BroadcastTitleRenderer.Render` to generate the title. Respects cancellation (resets to `Idle` on cancel).
   - `StopStreamAsync(ct)` — if `Live` or `Starting`: transitions to `Stopping` (fires event), waits `StopDelayMs`, transitions to `Idle` (fires event), clears `CurrentBroadcast`. If not `Live`/`Starting`: logs warning, no-op.
   - `ResetAsync(ct)` — if `Error`: transitions to `Idle` (fires event). If not `Error`: logs warning, no-op.

4. **`PcsRemote.YouTube.Mock.Tests` project** — target `net8.0`, references `PcsRemote.YouTube.Mock` and `PcsRemote.Core`. Contains contract tests.

5. **Solution integration** — both projects added to `PCS_Remote.slnx`.

### Out of Scope

- DI wiring in `PcsRemote.Web` — handled by S-004.
- Configuration binding from `appsettings.json` — handled by S-004.
- Real YouTube API interaction — handled by S-006.
- The `BroadcastTitleTemplate` config key — the mock receives `BroadcastTitleRenderer` via DI which handles template rendering. The template string itself is wired in S-004.

### IS Deviation

IS-008 S-003 states `StopStreamAsync` is a "No-op with Warning log if not `Live`", implying `Starting` is also a no-op case. This SPEC expands `StopStreamAsync` to also work from `Starting` state, per HLPS-008 v0.4 `IYouTubeLiveStreamService.StopStreamAsync` XML doc: *"Works in both Live and Starting states."* The HLPS is the governing document; the IS text is narrower than the interface contract it implements.

---

## 3. Implementation Detail

### 3.1 State Machine

The mock uses a simple `_status` field (no Stateless library needed — transitions are linear and small):

```
Idle → Starting → Live → Stopping → Idle
         ↓ (failure)     ↘ (stop also works from Starting)
         Error
         ↓ (cancel)
         Idle
Error → Idle (via ResetAsync)
```

**Cancellation from `Starting` always returns to `Idle`** — never to `Error`. Cancellation is a deliberate user action (S-YT-11). Only `SimulateStartFailure` causes `Starting → Error`.

### 3.2 Event Firing and State Cleanup

Every transition calls a private `SetStatus` method that:
1. Updates `_status`
2. Creates a `StreamStateSnapshot(newStatus, CurrentBroadcast, errorMessage)`
3. Invokes `StatusChanged?.Invoke(this, snapshot)`

**Cleanup rules:**
- Transitioning to `Idle` (from any state): `CurrentBroadcast` is set to `null`, `ErrorMessage` is `null`.
- Transitioning to `Error`: `CurrentBroadcast` is set to `null`, `ErrorMessage` is set to the failure reason.
- Simulated failure error message: `"Simulated start failure"` (constant).

### 3.3 Cancellation and Concurrency

The service maintains an internal `CancellationTokenSource` (`_operationCts`) for in-flight operations:
- `StartStreamAsync` creates a linked token from `ct` and `_operationCts`.
- During the `Starting` delay, if the linked token is cancelled: set status to `Idle`, clear broadcast, fire event, return (no throw).
- `StopStreamAsync` cancels `_operationCts` before transitioning, which interrupts any in-flight `StartStreamAsync` delay.
- After each operation completes, `_operationCts` is disposed and replaced.

**Concurrency guard:** A `SemaphoreSlim(1, 1)` serialises all public method calls. This prevents races between concurrent Start/Stop/Reset calls from multiple Blazor circuits (the service is registered as a singleton per HLPS-008 §4.4).

### 3.4 Title Rendering

`StartStreamAsync` calls `_titleRenderer.Render(null, match)` where:
- `null` template causes the renderer to use its default (`"{HomeTeam} vs {AwayTeam}"`)
- `match` comes from `_automationService.LoadedMatch`

Template configuration binding is S-004's responsibility — the mock always uses the renderer's default.

### 3.5 Broadcast ID Generation

The mock generates a synthetic broadcast ID: `"mock-broadcast-{Guid.NewGuid():N}"` and a synthetic watch URL: `"https://youtube.com/watch?v=mock123"`.

---

## 4. Acceptance Criteria

| AC | Description | Test Method |
|---|---|---|
| AC-1 | `StartStreamAsync` transitions `Idle → Starting → Live` | `StartStreamAsync_FromIdle_TransitionsToLive` |
| AC-2 | `StopStreamAsync` transitions `Live → Stopping → Idle` | `StopStreamAsync_FromLive_TransitionsToIdle` |
| AC-3 | Cancel during `Starting` resets to `Idle` | `StartStreamAsync_CancelledDuringStarting_ResetsToIdle` |
| AC-4 | `ResetAsync` from `Error` returns to `Idle` | `ResetAsync_FromError_TransitionsToIdle` |
| AC-5 | `ResetAsync` when not `Error` is a no-op | `ResetAsync_WhenNotError_NoOp` |
| AC-6 | `StopStreamAsync` when not `Live` or `Starting` is a no-op | `StopStreamAsync_WhenIdle_NoOp` |
| AC-7 | `StatusChanged` fires correct `StreamStateSnapshot` at each transition (start, stop, reset, cancel, failure) | `StartStreamAsync_FiresStatusChangedAtEachTransition` |
| AC-8 | `SimulateStartFailure` causes `Starting → Error` with `ErrorMessage = "Simulated start failure"` and `CurrentBroadcast = null` | `StartStreamAsync_SimulateFailure_TransitionsToError` |
| AC-9 | `SimulateActiveOnStartup` causes `InitializeAsync` to reconcile to `Live` | `InitializeAsync_SimulateActiveOnStartup_TransitionsToLive` |
| AC-10 | `StartStreamAsync` when no `LoadedMatch` throws `InvalidOperationException` | `StartStreamAsync_NoLoadedMatch_ThrowsInvalidOperationException` |
| AC-11 | `StartStreamAsync` generates title via `BroadcastTitleRenderer` | `StartStreamAsync_UsesRendererForTitle` |
| AC-12 | `CurrentBroadcast` is non-null when `Live`, null when `Idle` | `CurrentBroadcast_NullWhenIdle_PopulatedWhenLive` |
| AC-13 | `StartStreamAsync` when not `Idle` throws `InvalidOperationException` | `StartStreamAsync_WhenNotIdle_ThrowsInvalidOperationException` |
| AC-14 | Build succeeds with 0 errors, 0 warnings | (build verification) |
| AC-15 | All existing tests continue to pass | (test suite verification) |
| AC-16 | `InitializeAsync` with default options leaves status `Idle` and does not fire `StatusChanged` | `InitializeAsync_DefaultOptions_RemainsIdle` |

---

## 5. File Inventory

| File | Action | Project |
|---|---|---|
| `src/PcsRemote.YouTube.Mock/PcsRemote.YouTube.Mock.csproj` | Create | PcsRemote.YouTube.Mock |
| `src/PcsRemote.YouTube.Mock/MockYouTubeOptions.cs` | Create | PcsRemote.YouTube.Mock |
| `src/PcsRemote.YouTube.Mock/MockYouTubeLiveStreamService.cs` | Create | PcsRemote.YouTube.Mock |
| `tests/PcsRemote.YouTube.Mock.Tests/PcsRemote.YouTube.Mock.Tests.csproj` | Create | PcsRemote.YouTube.Mock.Tests |
| `tests/PcsRemote.YouTube.Mock.Tests/MockYouTubeLiveStreamServiceTests.cs` | Create | PcsRemote.YouTube.Mock.Tests |
| `PCS_Remote.slnx` | Modify (add both projects) | Solution |

---

## 6. Dependencies

- **S-001** (DELIVERED) — `IYouTubeLiveStreamService`, `LiveStreamStatus`, `LiveBroadcastInfo`, `StreamStateSnapshot`, `MatchInfo`.
- **S-002** (DELIVERED) — `BroadcastTitleRenderer` in `PcsRemote.Core`.
- **NuGet:** `Microsoft.Extensions.Options` and `Microsoft.Extensions.Logging.Abstractions` (both already in `Directory.Packages.props`).

---

## 7. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Mock diverges from real service contract | MEDIUM | Contract tests verify all `IYouTubeLiveStreamService` semantics; same tests can be reused for real impl |
| Delay-based tests are flaky | LOW | Use short delays (50ms) in tests, not production defaults |
| Thread safety of `_status` field | LOW | `SemaphoreSlim(1,1)` serialises all public methods; safe for multi-circuit singleton access |

---

## 8. Traceability

| HLPS Requirement | Coverage |
|---|---|
| S-YT-2 (broadcast title from template) | AC-11 (partial — mock uses renderer default) |
| S-YT-3 (start stream lifecycle) | AC-1 |
| S-YT-6 (stop stream lifecycle) | AC-2 |
| S-YT-7 (StatusChanged events) | AC-7 |
| S-YT-8 (mock implementation) | AC-1 through AC-13 (partial — DI wiring deferred to S-004) |
| S-YT-10 (startup reconciliation) | AC-9 |
| S-YT-11 (cancel during startup) | AC-3 |

---

## 9. Review History

### R1 — Opus 4.6 + GPT-5.4 (2026-04-17)

| # | Source | Severity | Title | Disposition |
|---|---|---|---|---|
| 1 | Both | CRITICAL | State diagram shows cancel → Error; should be cancel → Idle | Accept — fixed §3.1 diagram |
| 2 | Opus | HIGH | S-YT-12 incorrectly traced to AC-10 | Accept — removed from traceability |
| 3 | Opus | HIGH | StopStreamAsync scope expanded without IS deviation | Accept — added IS Deviation note |
| 4 | Opus | MEDIUM | No internal CTS for Stop interrupting Start | Accept — added §3.3 internal CTS spec |
| 5 | Both | MEDIUM | "set via options" ghost requirement in §3.4 | Accept — removed wording |
| 6 | Opus | LOW | No AC for InitializeAsync default path | Accept — added AC-16 |
| 7 | GPT | MEDIUM | StatusChanged contract under-specified | Accept — expanded AC-7 description |
| 8 | GPT | MEDIUM | Cleanup semantics incomplete | Accept — added cleanup rules to §3.2 |
| 9 | Opus | LOW | SimulateStartFailure error message unspecified | Accept — specified constant in §3.2 and AC-8 |
| 10 | Opus | LOW | CurrentBroadcast during Error unspecified | Accept — specified null in §3.2 cleanup rules |
| 11 | GPT | MEDIUM | Traceability S-YT-2 missing, S-YT-8 overstated | Accept — fixed §8 |
| 12 | GPT | HIGH | Concurrency model wrong | Accept — added SemaphoreSlim to §3.3, updated risk table |
