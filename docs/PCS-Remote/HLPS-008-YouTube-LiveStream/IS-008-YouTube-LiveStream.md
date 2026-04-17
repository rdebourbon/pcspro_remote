# IS-008: YouTube Live Stream Management — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-008-YouTube-LiveStream.md |
| **Status** | APPROVED — Pending user approval (v0.3 patch 2026-04-17) |
| **Version** | 0.3 |
| **Date** | 2026-04-17 |
| **Governing HLPS** | HLPS-008-YouTube-LiveStream.md v0.4 (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Prerequisites** | IS-007 delivered (deployment, Task Scheduler, tray host). Phase 1 complete. |

---

## Overview

This sequence implements the HLPS-008 scope in seven atomic steps: core domain types and interface additions, broadcast title template engine, mock YouTube service for development, streaming controls UI component with DI wiring, change match lifecycle coupling, real YouTube API implementation with DPAPI token storage and setup CLI, and Playwright E2E tests for cross-circuit stream status synchronisation.

Each step is independently verifiable and builds on the previous. Steps are identified with stable IDs (S-001 through S-007). IDs are never renumbered; deferred steps leave gaps.

---

## Accepted Risks from Approved HLPS-008

| Risk Summary | Citation |
|---|---|
| Y-U-1, Y-U-2, Y-U-3 block production use but NOT development — mock bypasses all three | HLPS-008 §6 Unknowns Register |
| No authorisation model on local trusted network | HLPS-008 §R1 finding #4 — dismissed per project decision A-4 |
| API quota/retry is deferred as an operational concern | HLPS-008 §R1 finding #22 — dismissed |
| Orphaned broadcast auto-cleanup deferred; logged as warnings | HLPS-008 §6, Y-U-4 — resolved by deferral |
| Only `"public"` privacy is supported and tested; config key exists for flexibility | HLPS-008 §2 In Scope, broadcast privacy |

---

## Steps

### S-001 — Core domain types, IYouTubeLiveStreamService interface, and LoadedMatch property

**What changes:** All YouTube-related domain types are added to `PcsRemote.Core`:
- `LiveStreamStatus` enum (Idle, Starting, Live, Stopping, Error)
- `LiveBroadcastInfo` record (BroadcastId, Title, WatchUrl)
- `StreamStateSnapshot` record (Status, Broadcast, ErrorMessage)
- `YouTubeStreamException` exception class
- `IYouTubeLiveStreamService` interface with `CurrentStatus`, `CurrentBroadcast`, `StatusChanged` event, `StartStreamAsync`, `StopStreamAsync`, and `ResetAsync`

Additionally, `IPcsProAutomationService` gains a `LoadedMatch` property that exposes the currently loaded `MatchInfo` (null when state ≠ `MatchLoaded`). Both `FlaUiPcsProAutomationService` and `MockPcsProAutomationService` are updated to implement this property.

**Why:** Every subsequent step depends on these types and interfaces. Placing them in `PcsRemote.Core` maintains the zero-external-dependency rule. The `LoadedMatch` property is required by `StartStreamAsync` to read match teams for title rendering (HLPS-008 §4.4 note).

**Dependencies:** None — this is the first step.

**Verification intent:** Unit tests for `LiveStreamStatus` enum values exist; `StreamStateSnapshot` and `LiveBroadcastInfo` records are constructable with expected properties; `YouTubeStreamException` preserves message and inner exception; `LoadedMatch` returns the loaded match when mock is in `MatchLoaded` state and null otherwise (S-YT-15). Build and all existing tests pass.

---

### S-002 — BroadcastTitleRenderer (title template engine)

**What changes:** A `BroadcastTitleRenderer` class is added to `PcsRemote.Core`. It accepts a template string and a `MatchInfo`, then substitutes all supported tokens: `{HomeTeam}`, `{AwayTeam}`, `{MatchType}`, `{Date}`, `{Date:format}`. The renderer enforces the grammar specified in HLPS-008 §4.5: unknown tokens are retained literally, null/empty match fields substitute to empty string, invalid `{Date:format}` falls back to short date with a warning log, time-related format specifiers are rejected at startup, and the resulting title is truncated to 97 characters + `"..."` if it exceeds 100 characters. Empty or missing template defaults to `"{HomeTeam} vs {AwayTeam}"`.

> **HLPS deviation:** HLPS-008 §5.1 places `BroadcastTitleRenderer.cs` in `PcsRemote.YouTube`. This IS relocates it to `PcsRemote.Core` because `PcsRemote.YouTube.Mock` (net8.0) depends on it but cannot reference `PcsRemote.YouTube` (net8.0-windows, with Google API dependencies). `BroadcastTitleRenderer` has no external dependencies and aligns with Core's zero-dependency rule.

**Why:** Title rendering is pure business logic with no external dependencies. Both the mock and real YouTube service implementations will consume it. Isolating it in Core allows thorough unit testing of all edge cases (S-YT-2, S-YT-9) without requiring any YouTube API interaction, and avoids a cross-TFM dependency from Mock to YouTube.

**Dependencies:** S-001 (`MatchInfo` type must exist; technically already exists from Phase 1 but confirming dependency).

**Verification intent:** Unit tests covering: each token substituted correctly with known `MatchInfo`; `{Date:d}` format specifier; invalid `{Date:HHmm}` falls back to short date; unknown token `{Venue}` retained literally; null `HomeTeam` substituted to empty string; title exceeding 100 chars truncated to 97 + `"..."`; empty template defaults to `"{HomeTeam} vs {AwayTeam}"` (S-YT-9). Build and all existing tests pass.

---

### S-003 — PcsRemote.YouTube.Mock project and MockYouTubeLiveStreamService

**What changes:** A new `PcsRemote.YouTube.Mock` project is created (target `net8.0`) referencing `PcsRemote.Core` only. `MockYouTubeLiveStreamService` implements `IYouTubeLiveStreamService` with simulated state transitions and configurable delays:
- `StartStreamAsync`: transitions `Idle → Starting → Live` with a configurable delay (simulating OBS health check wait). Uses `BroadcastTitleRenderer` to generate the title from `IPcsProAutomationService.LoadedMatch`. Fires `StatusChanged` at each transition. Respects cancellation token. On simulated failure (configurable), transitions to `Error`.
- `StopStreamAsync`: transitions `Live → Stopping → Idle` with a brief delay. No-op with Warning log if not `Live`.
- `ResetAsync`: clears `Error` state to `Idle`; no-op if not `Error`.
- Startup reconciliation: configurable flag to simulate an active broadcast found on startup.

A corresponding `PcsRemote.YouTube.Mock.Tests` test project is created with contract tests verifying the lifecycle transitions match `IYouTubeLiveStreamService` semantics.

**Why:** All development and testing uses mock mode (`YouTube:UseMock: true`). The mock must be complete before the UI component (S-004) can be developed and tested. The mock also serves as the contract reference for the real implementation (S-006). Satisfies S-YT-8.

**Dependencies:** S-001 (`IYouTubeLiveStreamService` interface), S-002 (`BroadcastTitleRenderer`).

**Verification intent:** Contract tests covering: `Idle → Starting → Live` transition (S-YT-3); `Live → Stopping → Idle` transition (S-YT-6); cancel during `Starting` resets to `Idle` (S-YT-11); `ResetAsync` from `Error` returns to `Idle`; `StopStreamAsync` when not `Live` is a no-op; `StatusChanged` fires `StreamStateSnapshot` at each transition (S-YT-7 partial); startup reconciliation restores `Live` when configured (S-YT-10); `StartStreamAsync` when no `LoadedMatch` throws (precondition guard — distinct from S-YT-12 which concerns missing tokens). Build and all existing tests pass.

---

### S-004 — StreamingControls.razor, DI wiring, and configuration schema

**What changes:** 
- `StreamingControls.razor` component is created at `PcsRemote.Web/Components/Streaming/`. It renders a `RadzenCard` themed consistently with the maroon/gold palette (inheriting CSS variables from `app.css`). Contains: status badge (colour varies by `LiveStreamStatus`), Start Stream button (enabled only when `Idle`), Stop Stream button (enabled only when `Live`), Cancel button (visible only during `Starting`), watch URL link (visible when `Live`), error message area with Dismiss button (visible when `Error`). Error messages include the "not configured" scenario — when the service throws `YouTubeStreamException` with a setup guidance message, the component renders it in the error area with a Dismiss button that calls `ResetAsync` to return to `Idle`. The component subscribes to `IYouTubeLiveStreamService.StatusChanged` in `OnInitialized`, calls `InvokeAsync(StateHasChanged)` on each event (with `_disposed` guard), and unsubscribes in `Dispose`. Start/Stop operations use `IOperationCoordinatorService.BeginOperation` / `MarkComplete` pattern.
- `Index.razor` is updated to conditionally render `StreamingControls` when `PcsProState == MatchLoaded` (same pattern as `ScoreboardPreview`).
- DI registration is added: `IYouTubeLiveStreamService` → `MockYouTubeLiveStreamService`. At this step, only the mock is wired — the real implementation does not exist yet. S-006 adds the conditional real/mock branching.
- Configuration schema is added to `appsettings.json` and `appsettings.Development.json` per HLPS-008 §5.3.

**Why:** This is the primary user-facing deliverable — the operator sees Start/Stop stream buttons when a match is loaded. With the mock service wired, the full UI flow is testable without YouTube credentials. Satisfies S-YT-1, S-YT-4, and partial coverage of S-YT-7 (within single circuit).

**Dependencies:** S-001 (domain types), S-003 (mock service for DI wiring and testing).

**Verification intent:** bUnit tests covering: component not rendered when state ≠ `MatchLoaded` (S-YT-1 — `NotRunning`, `Launching`, `MatchSelection`, `Error` states); Start button enabled only when `Idle`, disabled in all other states; Stop button enabled only when `Live`; Cancel button visible only during `Starting`; Dismiss button visible only during `Error`; error message rendered when `Error` (S-YT-4); "not configured" error message rendered when mock throws `YouTubeStreamException` with setup guidance (S-YT-12 partial); Dismiss button calls `ResetAsync` and returns to `Idle` (S-YT-5 partial — error recovery path); `StatusChanged` event updates component (S-YT-7 partial); component disposes cleanly with no post-dispose `StateHasChanged` (S-YT-14); watch URL link visible when `Live`. Build and all existing tests pass.

---

### S-005 — ChangeMatchButton lifecycle coupling

**What changes:** `ChangeMatchButton.razor` is updated to inject `IYouTubeLiveStreamService` and check `CurrentStatus` before executing the change match flow:
- If `CurrentStatus != Idle`, the confirmation dialog gains an additional warning message: *"⚠️ A live stream is active — it will be automatically stopped."*
- On confirm (when stream was active): `StopStreamAsync` is called before the match-change transition proceeds.
- PCS Pro `Error` state auto-stop: when PCS Pro enters `Error` state while `IYouTubeLiveStreamService.CurrentStatus` is `Live` or `Starting`, `StopStreamAsync` is called automatically. The exact wiring location (e.g., `Index.razor`'s existing state-change subscription or a dedicated observer) is a delivery-phase concern — the requirement is that the auto-stop happens reliably whenever PCS Pro enters `Error` with an active stream.

**Why:** Prevents orphaned YouTube broadcasts when the operator changes match or PCS Pro crashes mid-stream. This was identified as CRITICAL (#3) in HLPS-008 R1 review. Satisfies S-YT-13.

**Dependencies:** S-001 (interface), S-004 (DI wiring in place, `StreamingControls` exists for integration context).

**Verification intent:** bUnit tests covering: when `CurrentStatus == Live`, dialog shows warning text; on confirm, `StopStreamAsync` is called before `ChangeMatch` trigger fires; when `CurrentStatus == Idle`, no warning shown and no `StopStreamAsync` call; PCS Pro `Error` state entry triggers `StopStreamAsync` when stream is `Live` (S-YT-13). Build and all existing tests pass.

---

### S-006 — PcsRemote.YouTube project (real implementation, DpapiFileDataStore, setup CLI)

**What changes:**
- A new `PcsRemote.YouTube` project is created (target `net8.0-windows`) referencing `PcsRemote.Core`, `Google.Apis.YouTube.v3`, and `Google.Apis.Auth`.
- `DpapiFileDataStore` implements Google's `IDataStore` interface: serialises token to JSON, encrypts with `ProtectedData.Protect(DataProtectionScope.CurrentUser)`, writes to `{TokenStorePath}/PcsRemote.YouTube.dat`. Read is the inverse.
- `YouTubeLiveStreamService` implements the full broadcast lifecycle per HLPS-008 §5.7: create broadcast → bind to stream → poll OBS health → transition to live. Uses `BroadcastTitleRenderer` (from `PcsRemote.Core`) to generate the broadcast title from the loaded match. Includes startup reconciliation (§5.6), setup guard (§5.4), and cancel cleanup.
- `--setup-youtube` CLI flag is handled in `PcsRemote.TrayHost/Program.cs`: when present, runs the OAuth2 browser consent flow then exits. This requires adding a `ProjectReference` from `PcsRemote.TrayHost.csproj` to `PcsRemote.YouTube.csproj` (previously none existed).
- DI registration in `PcsRemote.Web` is updated to conditionally wire `MockYouTubeLiveStreamService` (when `YouTube:UseMock == true`) or `YouTubeLiveStreamService` (when `false`), replacing the mock-only registration from S-004.
- `PcsRemote.YouTube.Tests` test project is created.

> **Step size note:** This step is larger than others because its components are tightly coupled — `DpapiFileDataStore` is only meaningful with `YouTubeLiveStreamService`, the `--setup-youtube` CLI flag is only useful with the token store, and the DI update requires the real service type to exist. Splitting would create steps that cannot be independently verified. The JIT Spec for this step will define internal sub-tasks and commit boundaries to manage delivery complexity.

**Why:** Production implementation enabling real YouTube broadcast management. The DPAPI token storage justifies the `net8.0-windows` TFM. The setup CLI ensures OAuth consent happens only at the garage PC (S-YT-12). Startup reconciliation handles app restarts during live streams (S-YT-10).

**Dependencies:** S-001 (domain types and interface), S-002 (`BroadcastTitleRenderer`). Functionally independent of S-003/S-004/S-005 but all must be delivered first so the real service can be integration-tested through the existing UI.

**Verification intent:** Unit tests for `DpapiFileDataStore` round-trip (encrypt → write → read → decrypt); `YouTubeLiveStreamService` startup reconciliation restores `Live` when active broadcast found (S-YT-10); startup with missing `YouTube:LiveStreamId` fails fast; `StartStreamAsync` with no token throws `YouTubeStreamException` (S-YT-12); `StartStreamAsync` generates broadcast title from `BroadcastTitleRenderer` using `LoadedMatch` (S-YT-2); cancel during `Starting` deletes broadcast and resets to `Idle` (S-YT-11); OBS not streaming times out and transitions to `Error` (S-YT-5). Build and all existing tests pass.

---

### S-007 — Playwright E2E tests (cross-circuit stream status synchronisation)

**What changes:** New Playwright E2E tests are added to `PcsRemote.E2E.Tests`. The tests use mock mode and cover:
- Two independent browser contexts connect to the same server. When one clicks Start Stream, both contexts see the status update to `Live` (S-YT-7).
- Stream status badge and buttons are visible when match is loaded; absent when state ≠ `MatchLoaded`.
- Themed appearance smoke test: streaming controls use maroon/gold palette (S-YT-16).

The existing `PcsProWebApplicationFactory` two-host pattern (from IS-003 S-009) is reused. No new infrastructure is needed.

**Why:** S-YT-7 requires verifying that stream status updates propagate across Blazor circuits — bUnit tests operate within a single circuit and cannot validate this. This step is deliberately last, as all streaming components must exist before meaningful E2E tests can be written.

**Dependencies:** S-004 (UI component exists), S-003 (mock mode for E2E execution).

**Verification intent:** Playwright tests pass in headless mode. Two-context test verifies both browsers show `Live` status after Start Stream click. Build and all existing tests (including all prior E2E tests) pass.

---

## Success Criteria Coverage Matrix

| HLPS S-YT | Step(s) | Notes |
|---|---|---|
| S-YT-1 (StreamingControls rendered only in MatchLoaded) | S-004 | bUnit tests for conditional rendering |
| S-YT-2 (Start creates broadcast with correct title) | S-002, S-003, S-006 | Title rendering unit tests; mock verifies title pass-through; real service verifies title used in broadcast creation |
| S-YT-3 (Idle → Starting → Live transitions) | S-003, S-006 | Contract tests (mock); unit tests (real) |
| S-YT-4 (Button enabled/disabled states) | S-004 | bUnit tests for each LiveStreamStatus |
| S-YT-5 (OBS not streaming → timeout → Error) | S-004, S-006 | S-006: service times out and transitions to Error; S-004: error message rendered, Dismiss resets to Idle |
| S-YT-6 (Stop → Live → Stopping → Idle) | S-003, S-006 | Contract tests (mock); unit tests (real) |
| S-YT-7 (Cross-circuit status synchronisation) | S-004, S-007 | S-004 bUnit (single circuit); S-007 Playwright (cross-circuit) |
| S-YT-8 (Mock honours UseMock config) | S-003, S-004 | DI registration + contract tests |
| S-YT-9 (Template tokens + fallback) | S-002 | Comprehensive unit tests |
| S-YT-10 (Startup reconciliation) | S-003, S-006 | Mock configurable flag; real startup query |
| S-YT-11 (Cancel during Starting) | S-003, S-006 | Contract test (mock); unit test (real) |
| S-YT-12 (No token → error message, no browser consent) | S-004, S-006 | S-004 bUnit: "not configured" error rendered; S-006: service throws YouTubeStreamException |
| S-YT-13 (ChangeMatchButton lifecycle coupling) | S-005 | bUnit tests for warning + auto-stop |
| S-YT-14 (StreamingControls dispose unsubscription) | S-004 | bUnit test verifying no post-dispose StateHasChanged |
| S-YT-15 (LoadedMatch property) | S-001 | Unit test against mock |
| S-YT-16 (Themed appearance) | S-004, S-007 | S-004: component uses maroon/gold CSS variables; S-007: Playwright screenshot verification |

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-16 | Claude Opus 4.6, GPT-5.4 | NEEDS REVIEW — 0 CRITICAL, 0 HIGH (3 downgraded from HIGH), 7 MEDIUM, 2 LOW; all 9 findings accepted and applied in v0.2 |
| R2 | 2026-04-16 | Claude Opus 4.6, GPT-5.4 | Opus: **APPROVED** (9/9 verified, 0 new). GPT: NEEDS REVIEW (9/9 verified, 1 new MEDIUM — deferred to delivery). **Effective unanimity achieved.** |
| External | 2026-04-17 | Claude Opus 4.7, GPT-5.4 | NEEDS REVIEW → BLOCKED (GPT). Architectural defects found in paired HLPS-008; HLPS patched to v0.4. One inline IS fix applied (S-006 TrayHost ProjectReference). Remaining concerns deferred to JIT Specs per HLPS-008 §9. |

### R1 Findings Applied

| # | Source | Original → Final Severity | Finding | Disposition | Fix Applied |
|---|---|---|---|---|---|
| 1 | Both | HIGH/MEDIUM → MEDIUM | S-YT-16 theming not implementation-owned | Accept | Added theming mention to S-004 description; updated coverage matrix S-YT-16 → S-004 + S-007 |
| 2 | GPT | HIGH → MEDIUM | S-YT-5 recovery path not fully planned | Accept (downgrade) | Updated coverage matrix: S-YT-5 → S-004 + S-006; added Dismiss → Idle to S-004 verification |
| 3 | GPT | HIGH → MEDIUM | S-YT-12 lacks explicit web-UI handling | Accept (downgrade) | Added "not configured" error scenario to S-004 verification intent |
| 4 | Both | MEDIUM | S-006 oversized | Accept | Added cohesion justification + JIT Spec sub-task note to S-006 |
| 5 | GPT | MEDIUM | S-004 DI description inconsistent | Accept | Clarified S-004 wires mock only; S-006 adds conditional branching |
| 6 | GPT | MEDIUM | Real title verification missing in S-006 | Accept | Added title rendering verification to S-006 verification intent |
| 7 | Opus | MEDIUM | BroadcastTitleRenderer Core vs YouTube deviation | Accept | Added HLPS deviation note to S-002 with architectural justification |
| 8 | Opus | LOW | PCS Pro Error auto-stop wiring location unspecified | Accept | Clarified in S-005 that wiring location is a delivery concern |
| 9 | Opus | LOW | S-003 wrong S-YT-12 citation | Accept | Removed incorrect S-YT-12 annotation; clarified as precondition guard |

### R2 Findings

| # | Source | Severity | Finding | Disposition | Rationale |
|---|---|---|---|---|---|
| R2-1 | GPT | MEDIUM | S-YT-12 verification depends on unplanned mock capability | Defer to Delivery | S-003 already supports configurable failure; bUnit tests use test doubles configured to throw, not the runtime mock. Test helper pattern is a delivery concern per IS Abstraction Level rules. |

### v0.3 Patch — External Review (2026-04-17)

External adversarial review (Opus 4.7 + GPT-5.4) found **5 inline contradictions in HLPS-008** and one gap in this IS. HLPS-008 was patched to v0.4 (see its §8 review history). Only one fix was required in IS-008; all other concerns were deferred to JIT Specs per HLPS-008 §9.

| # | Issue | Fix |
|---|---|---|
| 1 | S-006 specified `--setup-youtube` in TrayHost but no `ProjectReference` from TrayHost → YouTube existed; build would fail | Added explicit `ProjectReference` requirement to S-006 "What changes" |

**Concerns deferred to JIT Specs** (do not block IS-008 approval — tracked in HLPS-008 §9):
- Service-owned `CancellationTokenSource` design for Starting-state cancel → JIT Spec S-006
- Thread-safety (`SemaphoreSlim`) for singleton service → JIT Spec S-006
- Browser-suppression mechanism in runtime path → JIT Spec S-006
- `TokenStorePath` default resolution → JIT Spec S-006
- `LiveStreamId` validity check at startup → JIT Spec S-006
- Auto-stop observer location (`YouTubeAutoStopObserver : IHostedService`) → JIT Spec S-005
- `MockYouTubeOptions` record naming → JIT Spec S-003
- `--setup-youtube` deployment-ready invocation (shipped exe, not `dotnet run`) → JIT Spec S-006
- `ready`-state orphan broadcast warning logging verification → JIT Spec S-006
- Test-double audit for `IPcsProAutomationService.LoadedMatch` addition → JIT Spec S-001
- Timing assertions for S-YT-3 / S-YT-7 operationalised → JIT Spec S-003 / S-007

Every JIT Spec authored for IS-008 must address the concerns assigned to its step.
