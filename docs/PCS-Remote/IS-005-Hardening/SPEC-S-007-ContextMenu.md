# SPEC-S-007 — TrayHost Context Menu

| Field | Value |
|---|---|
| **Document** | SPEC-S-007-ContextMenu.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-05-07 |
| **Step** | S-007 |
| **Governing IS** | IS-005-Hardening.md v0.3 (S-007) |
| **Branch** | `feature/IS-005-S-007-context-menu` |

---

## 1. Objective

Wire the `NotifyIcon`'s context menu in `PcsRemote.TrayHost` to live application services.
The completed context menu delivers H-SC-1 (tray visible; visual state; context menu),
H-SC-2 (tray side of manual mode toggle), H-SC-9 (Open Browser), and H-SC-10 (Exit/StopAsync)
from HLPS-005.

---

## 2. Requirements

### R-1 — Service injection into `TrayApplicationContext`

`TrayApplicationContext` must receive `IManualModeService`, `IPcsProAutomationService`, and
`IConfiguration` through its constructor. The DI factory lambda in `TrayHost/Program.cs` is
updated to resolve and capture these services from the `IServiceProvider` before constructing
the context.

The constructor signature change must remain `internal` — `TrayApplicationContext` is not
part of any public API.

### R-2 — Context menu structure

A `ContextMenuStrip` with the following items (in order) must be attached to the `NotifyIcon`:

| Index | Item | Notes |
|---|---|---|
| 0 | Manual mode toggle | Dynamic text; see R-3 |
| 1 | `ToolStripSeparator` | |
| 2 | "Open Browser" | See R-4 |
| 3 | `ToolStripSeparator` | |
| 4 | "Exit" | See R-5 |

The `ContextMenuStrip` is constructed and owned by `TrayApplicationContext`. Disposal must
follow this ordering to prevent `ObjectDisposedException` from pending WM_CONTEXTMENU messages:
(1) unsubscribe `ManualModeChanged`, (2) set `NotifyIcon.Visible = false`, (3) dispose
`NotifyIcon`, (4) dispose `ContextMenuStrip`.

### R-3 — Manual mode toggle: menu text, icon, and click handler

**Initial state:** When the context is constructed, the menu item text and the `NotifyIcon`
icon must reflect the *current* state of `IManualModeService.IsManualModeActive`:

| IsManualModeActive | Menu item text | NotifyIcon icon |
|---|---|---|
| `false` | "Switch to Manual Mode" | `SystemIcons.Application` |
| `true` | "Resume Automation" | `SystemIcons.Warning` |

**Ongoing updates:** `TrayApplicationContext` subscribes to the `IManualModeService.ManualModeChanged`
event. When the event fires, it updates the menu item text and icon to reflect the new state.
Because `ManualModeChanged` may fire on an ASP.NET Core thread-pool thread, the update must be
marshaled back to the STA thread using `SynchronizationContext.Post` (non-blocking — never
`Send`, which can deadlock if the STA pump is blocked). The `SynchronizationContext` must be
captured on the STA thread during construction (`SynchronizationContext.Current` at that point
is the WinForms-compatible context). If `SynchronizationContext.Current` is null at construction,
a fallback `Control`-based invoker is used.

**Initialisation ordering (subscribe-first):** To prevent a TOCTOU race between reading
`IsManualModeActive` and receiving the first `ManualModeChanged` event, the initialisation
sequence must be: (1) subscribe to `ManualModeChanged`, then (2) read `IsManualModeActive`
to set the initial text and icon. Reversing this order would create a window where a mode change
fires before the subscription, leaving the UI permanently out of sync.

**Click handler:** Clicking the toggle item calls `Enable()` if currently in automation mode,
or `Disable()` if currently in manual mode.

**Event unsubscription:** The `ManualModeChanged` subscription must be removed in
`Dispose(bool disposing)` to prevent callbacks after disposal.

### R-4 — "Open Browser" click handler

Clicking "Open Browser" launches the system default browser pointing to the application URL.
The URL is resolved from configuration using the following precedence:
1. `Kestrel:Endpoints:Http:Url` (the project's explicit Kestrel endpoint key)
2. `urls` (ASP.NET Core generic bind key, typically set via `--urls` or `ASPNETCORE_URLS`)
3. Hard-coded fallback: `http://localhost:5000`

If the resolved URL contains a wildcard bind address (`0.0.0.0` or `::`)
it is replaced with `localhost` before opening. If the URL is multi-valued (semicolon-separated),
the first value is used.

The launch uses `Process.Start` with `UseShellExecute = true` so the OS selects the default
browser. Any exception (e.g., no default browser configured, `Process.Start` fails) is caught,
logged at Warning level via Serilog structured template, and swallowed — a failed browser launch
must not crash the tray host.

### R-5 — "Exit" click handler

Clicking "Exit":
1. The click handler is `async void` (the standard WinForms async event handler pattern).
2. It attempts a graceful shutdown of the automation service via `IPcsProAutomationService.StopAsync`
   with a bounded timeout of **3 seconds** (a `CancellationTokenSource` with a 3-second timeout
   is created; `StopAsync` is awaited with that token). This is non-blocking — the WinForms STA
   message pump continues processing while the await is pending.
3. Any exception thrown by `StopAsync` (including `OperationCanceledException` on timeout) is
   caught inside the handler, logged at Warning level via Serilog, and does not prevent step 4.
4. `Application.Exit()` is called in a `finally` block — unconditionally, regardless of whether
   `StopAsync` completed, timed out, or threw.

`Application.Exit()` causes `WinFormsHostedService.RunMessageLoop` to return, which then calls
`IHostApplicationLifetime.StopApplication()` to shut down Kestrel gracefully.

### R-6 — Icon assets

Two visually distinct system icons are used as mode indicators:

| Mode | Icon | Rationale |
|---|---|---|
| Automation (normal) | `SystemIcons.Application` | Neutral — existing placeholder |
| Manual | `SystemIcons.Warning` | Yellow warning — conveys human intervention |

Production icon assets (custom `.ico` files) are explicitly deferred to a future asset creation
step. System icons are sufficient to satisfy the "visually distinct" requirement from IS S-007.

---

## 3. Acceptance Criteria

| ID | Criterion | Verification |
|---|---|---|
| AC-1 | `dotnet build` succeeds with zero new errors or warnings | Automated |
| AC-2 | Pre-existing tests (336 passing) continue to pass | Automated |
| AC-3 | Context menu has exactly 5 items in the order specified in R-2 (right-click tray icon; count items and verify labels) | Manual — Windows tray |
| AC-4 | In automation mode: toggle shows "Switch to Manual Mode" + Application icon. In manual mode: shows "Resume Automation" + Warning icon | Manual — Windows tray |
| AC-5 | Toggle click in automation mode → `Enable()` called → manual mode banner appears in connected browser (integration side-effect, not a S-007 deliverable per se) | Manual — Windows |
| AC-6 | Programmatically trigger `ManualModeChanged`; tray icon and menu item text update within one event cycle without requiring a tray interaction | Manual — Windows |
| AC-7 | Click "Open Browser" → default browser opens at `http://localhost:5000` (or configured URL); no crash or unhandled exception | Manual — Windows |
| AC-8 | Click "Exit" → process terminates within 5 seconds; Serilog log shows `StopAsync` attempt entry | Manual — Windows + log |
| AC-9 | If `StopAsync` hangs or throws, process still exits within 5 seconds (verify by blocking the mock service) | Manual — Windows |
| AC-10 | Test stubs exist for TC-1 through TC-5; TC-1 through TC-5 are `[Ignore]` in headless CI | Automated (compile) |

---

## 4. Test Cases

All test cases in this step target WinForms UI behaviour and require a live Windows desktop
context (tray notification area, window handles). They must be marked `[Ignore]` for headless
CI, consistent with TC-4 from S-006.

| ID | Name | Scenario | Expected Result | Status |
|---|---|---|---|---|
| TC-1 | ContextMenu_Structure_HasFiveItemsInCorrectOrder | Context constructed in automation mode | 5 items in R-2 order | `[Ignore]` |
| TC-2 | ManualModeChanged_WhenEventFires_UpdatesMenuTextAndIcon | Event fired with `isActive=true` | Item text → "Resume Automation"; icon → Warning | `[Ignore]` |
| TC-3 | ExitMenuItem_WhenClicked_AttemptsStopAsyncThenExits | Exit item click simulated | StopAsync called; Application.Exit called | `[Ignore]` |
| TC-4 | OpenBrowserMenuItem_WhenClicked_LaunchesBrowser | Open Browser item click simulated | `Process.Start` called with application URL | `[Ignore]` |
| TC-5 | ToggleMenuItem_WhenClickedInAutomationMode_CallsEnable | Toggle clicked with `IsManualModeActive=false` | `Enable()` called on `IManualModeService` | `[Ignore]` |

A new test class `TrayContextMenuTests` is added to `PcsRemote.TrayHost.Tests`. All five test
methods are stubs marked `[Ignore]`. They are added alongside (not replacing) the existing
`TrayApplicationContextTests` class from S-006, which retains its `Dispose_HidesAndDisposesNotifyIcon`
stub.

---

## 5. Branch and Commit Strategy

- **Branch:** `feature/IS-005-S-007-context-menu`
- **Commits:**
  - Commit 1 (R-1 + R-2): Enrich `TrayApplicationContext` constructor, add `ContextMenuStrip` skeleton, update factory in `Program.cs`.
  - Commit 2 (R-3): Wire manual mode toggle (event subscription, click handler, icon/text updates, thread marshaling).
  - Commit 3 (R-4 + R-5): Wire Open Browser and Exit handlers.
  - Commit 4 (Tests): Add `TrayContextMenuTests` with TC-1 through TC-5 stubs (`[Ignore]`).

---

## 6. Risk Register

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| `ManualModeChanged` fires on thread-pool thread; direct UI update causes cross-thread exception | Medium | High | Marshaling via invoker created on STA thread at construction time (R-3) |
| `StopAsync` never completes (hang in automation service) | Low | Medium | 3-second `CancellationTokenSource` timeout; `Application.Exit()` called unconditionally in `finally` block (R-5) |
| URL wildcard bind (`0.0.0.0`) not replaced → browser cannot connect | Medium | Low | Explicit wildcard replacement before `Process.Start` (R-4) |
| `Process.Start` fails (no default browser) | Low | Low | Exception caught; logged at Warning; swallowed (R-4) |
| TC-1 through TC-5 in `[Ignore]` — context menu never exercised automatically | Accepted | Medium | Manual acceptance test on Windows; consistent with S-006 TC-4 precedent and IS §Delivery Notes |

---

## 7. Review History

| Round | Reviewer | Verdict | Notes |
|---|---|---|---|
| R2 | Opus | APPROVED (1L) | Duplicate paragraph LOW regression — fixed in v0.3 |
| R2 | Sonnet | APPROVED (1L) | Control fallback identity LOW — accepted |
| R2 | GPT | Non-substantive | Applied code-review PR protocol to spec review; no findings on spec content |
| R1 | GPT | NEEDS REVIEW (2H, 3M) | AC specificity HIGH, exit async ambiguity HIGH, URL under-spec MEDIUM |
| R1 | Sonnet | NEEDS REVIEW (2H, 2M, 2L) | Method names HIGH, TOCTOU subscribe-first HIGH, STA Post/Send MEDIUM |

### R1 Findings Addressed
| Finding | Reviewer | Severity | Resolution |
|---|---|---|---|
| Wrong method names `EnableManualMode`/`DisableManualMode` | Sonnet/Opus | HIGH/MEDIUM | Fixed to `Enable()`/`Disable()` in R-3 |
| TOCTOU race: subscribe-after-read | Sonnet | HIGH | Added subscribe-first ordering requirement in R-3 |
| StopAsync exception path unclear | GPT/Opus | HIGH/MEDIUM | Added explicit `async void` + catch-log-Warning + unconditional finally in R-5 |
| STA marshaling Post vs Send | Sonnet | MEDIUM | Specified `SynchronizationContext.Post` in R-3 |
| URL config key precedence/fallback | GPT/Sonnet/Opus | MEDIUM/LOW | Added explicit precedence (Kestrel key > urls > fallback) + missing-key fallback in R-4 |
| AC-3–AC-9 observable outcomes sparse | GPT | HIGH | Enhanced each AC with explicit observable outcome |
| ContextMenuStrip disposal ordering | Sonnet | LOW | Added (1)–(4) ordering in R-2 |
| AC-5 cross-component banner | Opus | LOW | Clarified as integration side-effect, not S-007 deliverable |
| Test class placement | Opus | LOW | Added note that TrayContextMenuTests coexists with existing TrayApplicationContextTests |
| URL normalization unit test | Opus | LOW | Deferred to Delivery — implementer discretion |
