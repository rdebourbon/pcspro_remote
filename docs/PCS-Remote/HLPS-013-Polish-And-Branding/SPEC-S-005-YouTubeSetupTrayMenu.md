# SPEC-S-005 — YouTube Setup Tray Menu Item

**Status:** DRAFT  
**Parent:** IS-013 S-005  
**HLPS traceability:** SC-7, C-4, R-5

---

## §1 Objective

Add a "YouTube Setup..." item to the TrayHost context menu that launches the Google OAuth consent flow in the default browser, stores the token via the existing `DpapiFileDataStore`, and re-initialises the YouTube service. The menu item must be disabled while a setup is in progress or while a broadcast is live/starting/stopping. On completion or failure, re-enable the item so the operator can retry.

---

## §2 Requirements

| ID | Requirement |
|----|-------------|
| R-1 | Add `RunOAuthSetupAsync(CancellationToken)` to `IYouTubeLiveStreamService`. Returns `Task<bool>` — `true` on success, `false` on configuration error. |
| R-2 | Implement `RunOAuthSetupAsync` in `YouTubeLiveStreamService` using injected `_options` and `_dataStore`. Must acquire `_gate` and reject (return `false`) if `CurrentStatus` is not `Idle` — prevents concurrent setup + stream start race. After successful consent, call `InitializeAsync` to activate the token. If `InitializeAsync` throws (e.g., `LiveStreamId` not configured), catch the exception, log a warning ("Token stored but YouTube configuration incomplete"), and return `true` — the OAuth consent itself succeeded. |
| R-3 | Implement `RunOAuthSetupAsync` in `MockYouTubeLiveStreamService` — log and return `true`. |
| R-4 | Add "YouTube Setup..." `ToolStripMenuItem` to `TrayApplicationContext` context menu, positioned between "Open Browser" and the Exit separator. |
| R-5 | Disable the menu item when: (a) a setup is already in progress, or (b) `CurrentStatus` is `Starting`, `Live`, or `Stopping`. |
| R-6 | Subscribe to `StatusChanged` on `IYouTubeLiveStreamService` to update enabled state reactively. Marshal to STA thread via existing `_invoker.BeginInvoke` pattern. Unsubscribe on `Dispose` and guard `_disposed` before marshalling — mirrors the existing `ManualModeChanged` lifecycle pattern. |
| R-7 | On setup completion or failure, re-enable the menu item and log the outcome. |
| R-8 | Inject `IYouTubeLiveStreamService` into `TrayApplicationContext` constructor. Update the factory lambda in `Program.cs`. |
| R-9 | `--setup-youtube` CLI path remains unchanged (backward compatibility). |

---

## §3 Design Notes

**OAuth consent flow:** `GoogleWebAuthorizationBroker.AuthorizeAsync` opens the default browser for consent, blocks until the user completes or cancels, and stores the token via the injected `IDataStore`.

**Re-initialisation:** After successful consent, `RunOAuthSetupAsync` calls `InitializeAsync` internally so the service picks up the new token and validates the YouTube API configuration. If `InitializeAsync` fails (e.g., `LiveStreamId` not configured), the token is still stored — the operator can configure the remaining settings and restart.

**Click handler pattern:** `async void OnYouTubeSetupClicked` follows the same pattern as `OnExitClicked` — fire-and-forget from the STA thread with try/catch logging.

**CLI vs menu duplication (C-4):** The `--setup-youtube` CLI path bootstraps its own configuration before the DI container exists. The service method uses DI-injected dependencies. The actual OAuth call (`GoogleWebAuthorizationBroker.AuthorizeAsync`) is one line in each path. This is acceptable — extracting a shared helper for a single API call would over-engineer the boundary between standalone CLI and DI-hosted service.

---

## §4 Test Cases

| ID | Scope | Description |
|----|-------|-------------|
| TC-1 | Core | `IYouTubeLiveStreamService` has `RunOAuthSetupAsync` method (compilation test — interface exists) |
| TC-2 | Mock | `MockYouTubeLiveStreamService.RunOAuthSetupAsync` returns `true` |
| TC-3 | TrayHost | `TrayApplicationContext` constructor accepts `IYouTubeLiveStreamService` parameter |
| TC-4 | TrayHost | Context menu contains "YouTube Setup..." item at expected position (after "Open Browser", before Exit separator) |
| TC-5 | TrayHost | `_youTubeSetupItem.Enabled` is `false` when `CurrentStatus` is `Live` |
| TC-6 | TrayHost | `_youTubeSetupItem.Enabled` is re-enabled after status returns to `Idle` |
| TC-7 | TrayHost | `_youTubeSetupItem.Enabled` is `false` during setup when `CurrentStatus` is `Idle` (setup-in-progress flag) |
| TC-8 | TrayHost | `_youTubeSetupItem.Enabled` returns to `true` after setup fails |
| TC-9 | TrayHost | `Dispose` unsubscribes `StatusChanged` — post-dispose status changes do not throw |

> **Note:** Integration tests for the actual OAuth browser flow are not feasible in CI — the browser consent is interactive. The real `YouTubeLiveStreamService.RunOAuthSetupAsync` is verified manually on the garage PC.

---

## §5 Files Changed

| File | Change |
|------|--------|
| `src/PcsRemote.Core/IYouTubeLiveStreamService.cs` | Add `RunOAuthSetupAsync` method |
| `src/PcsRemote.YouTube/YouTubeLiveStreamService.cs` | Implement `RunOAuthSetupAsync` |
| `src/PcsRemote.YouTube.Mock/MockYouTubeLiveStreamService.cs` | Implement `RunOAuthSetupAsync` |
| `src/PcsRemote.TrayHost/TrayApplicationContext.cs` | Add menu item, inject service, wire events |
| `src/PcsRemote.TrayHost/Program.cs` | Update factory lambda to pass `IYouTubeLiveStreamService` |
| `tests/PcsRemote.YouTube.Mock.Tests/` | TC-2 |
| `tests/PcsRemote.TrayHost.Tests/` | TC-3 through TC-9 |

---

## §6 Review History

### R1 — Opus 4.5 + GPT 5.4

**Opus verdict:** REQUEST CHANGES (5 findings)  
**GPT verdict:** REQUEST CHANGES (5 findings)

| # | Source | Finding | Severity | Disposition |
|---|--------|---------|----------|-------------|
| F-1 | Both | Concurrent setup + stream start race — `InitializeAsync` doesn't use `_gate` | High | **ACCEPTED** — R-2 amended: acquire `_gate`, reject if not `Idle` |
| F-2 | Opus | Missing `_setupInProgress` test cases | Medium | **ACCEPTED** — added TC-7, TC-8 |
| F-3 | Both | Interface change breaks implementers | Medium | **SET ASIDE** — exactly 2 known implementers (`YouTubeLiveStreamService`, `MockYouTubeLiveStreamService`), both changed in this step |
| F-4 | Opus | `InitializeAsync` failure after successful OAuth confuses operator | Low | **ACCEPTED** — R-2 amended: catch, log warning, return `true` |
| F-5 | Opus | CancellationToken not propagated to OAuth broker | Low | **SET ASIDE** — broker supports it, will pass through |
| F-6 | GPT | Dispose must unsubscribe `StatusChanged` + guard `_disposed` | High | **ACCEPTED** — R-6 amended, added TC-9 |
| F-7 | GPT | `Task<bool>` too weak for multiple failure modes | Medium | **SET ASIDE** — sufficient for tray UX; failures logged with detail |
| F-8 | GPT | Test plan misses risky cases | Medium | **ACCEPTED** — covered by TC-7, TC-8, TC-9 |
