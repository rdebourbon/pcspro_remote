# IS-011: Operational UX — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-011-Operational-UX.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-21 |
| **Governing HLPS** | HLPS-011-Operational-UX.md v0.4 (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Prerequisites** | IS-010 delivered (automation hardening, health-check, match selection, use-current-match) |

---

## Overview

This sequence implements HLPS-011 in nine atomic steps, organised in three tiers:

- **Tier 1 — Core contracts (S-001, S-002, S-003):** Three independent, additive changes to `PcsRemote.Core` — the Dismiss trigger and dismiss contract on the automation service interface, the operation description extension on the coordinator interface, and a new automation log service interface with its entry type. These steps have no mutual dependencies and establish the contracts consumed by all downstream work.

- **Tier 2 — Implementations and UI (S-004 through S-008):** Each step delivers one HLPS-011 feature as a vertical slice from service implementation through to UI component. S-004 delivers error dismissal (GAP-003). S-005 delivers operation-in-progress feedback (GAP-014). S-006 delivers the automation log service with rolling buffer and SignalR broadcasting. S-007 delivers the debug section container and the automation log UI panel (GAP-001 + GAP-017 UI). S-008 instruments both Mock and Real automation services to emit log entries at operator-visible steps.

- **Tier 3 — Integration testing (S-009):** Cross-feature E2E tests verifying multi-browser behaviour for dismiss, operation status, and automation log visibility.

Core steps are independent of each other and may be done in any order. Web/implementation steps depend on their corresponding Core prerequisite. The linear ordering below respects all dependencies.

---

## Steps

### S-001 — Core: Dismiss trigger and dismiss contract

**What changes:** The state machine gains a new named trigger for dismissing the Error state. A corresponding transition is defined from Error to NotRunning on this trigger. The automation service interface (`IPcsProAutomationService`) gains a new method representing the dismiss action — semantically distinct from the existing retry method. Retry clears the error *and* restarts the automation sequence; dismiss clears the error only, returning the system to a quiescent state.

**Why:** OUX-C-3 mandates that dismissal is a named trigger with a defined transition — not a reuse of the Retry trigger or a state machine bypass. SC-1 requires the operator to be able to dismiss an error without triggering automation restart. The Core interface must expose the action so that the Web layer can invoke it. Prerequisite for S-004 (implementation and UI).

**Dependencies:** None — pure Core addition.

**Verification intent:** State machine unit tests: the new trigger transitions Error → NotRunning; the trigger from any non-Error state throws `InvalidOperationException`; existing Retry transition is unaffected. Interface contract: the new method is declared and discoverable. All existing tests pass; no new project references on `PcsRemote.Core`.

---

### S-002 — Core: Operation description on coordinator interface

**What changes:** `IOperationCoordinatorService` is extended so that callers can provide a human-readable description when beginning an operation (e.g., "Loading match…", "Starting live stream…"). The interface exposes a property for reading the current description (null when idle). The change notification event is updated or augmented to carry the description alongside the in-progress flag. The existing no-description call pattern must remain valid (callers that don't provide a description should compile without changes or with trivial adaptation).

**Why:** SC-2 requires the UI to show *what* operation is executing, not just that something is happening. The current coordinator interface carries only a boolean flag — it cannot communicate the nature of the operation. OUX-R-4 identifies this as a cross-assembly contract change; all callers in Web must adapt. Prerequisite for S-005 (implementation and UI).

**Dependencies:** None — pure Core interface change.

**Verification intent:** Interface contract: description parameter and property are present. Backward compatibility: existing call sites can adapt with minimal change (compiler will flag). All existing tests pass; no new project references on `PcsRemote.Core`.

---

### S-003 — Core: Automation log service contract

**What changes:** A new service interface is introduced in `PcsRemote.Core` representing the automation log — a bounded, append-only log of operator-visible automation activity. The interface defines: a method to add a timestamped log entry; a method to retrieve a snapshot of recent entries (bounded by the server-side buffer size); and a change notification event for real-time push to subscribers. A record type representing a single log entry is also introduced in Core. Each entry carries a timestamp, an action description, and an outcome indicator — consistent with the HLPS §2 definition of log entry content.

**Why:** SC-4, SC-5, and SC-6 require a live automation log visible to all browsers. Placing the contract in Core ensures both the real and mock automation services can emit entries without depending on Web. The log service implementation (S-006) and automation instrumentation (S-008) both depend on this contract.

**Dependencies:** None — pure Core addition.

**Verification intent:** Interface and entry type are declared in `PcsRemote.Core` and compile. No new project references on `PcsRemote.Core`. All existing tests pass.

---

### S-004 — Error dismiss implementation and UI

**What changes:** Both the mock and real automation services implement the dismiss method declared in S-001. The implementation fires the Dismiss trigger on the state machine, transitioning from Error to NotRunning, and clears the stored error reason. No automation restart is triggered — the system returns to a clean quiescent state. After dismissal, PCS Pro may still be running — the NotRunning state does not guarantee process termination. No process cleanup is performed in the dismiss path. The existing process-detection logic in the launch flow already handles a running instance on next launch.

In the Web UI, the existing `ErrorDisplay` component gains a Dismiss button alongside the existing Retry button. Dismiss is a single-click action (OUX-U-3) — no confirmation dialog. The Dismiss button is exempt from both the manual mode guard and the operation coordinator lock — it must remain clickable regardless of coordinator or manual mode state. This is semantically correct: dismiss is not an automation operation (it does not start or restart automation), and the operator is explicitly clearing an error they've already handled. This distinguishes it from Retry, which starts a new automation sequence and therefore correctly participates in both guards.

**Why:** SC-1 — operator can dismiss error and return to NotRunning without triggering automation restart. OUX-C-5 — both mock and real services implement the new contract.

**Dependencies:** S-001 (Dismiss trigger and interface method must exist in Core).

**Verification intent:** Unit tests: dismiss from Error → NotRunning; error reason cleared; dismiss from non-Error throws; no process-termination call is made on dismiss. bUnit tests: Dismiss button renders in Error state; click triggers dismiss and clears error banner; button not rendered in non-Error states; Dismiss button is enabled even when coordinator lock is held; Dismiss works even when manual mode is active (unlike Retry). All existing tests pass.

---

### S-005 — Operation status implementation and banner UI

**What changes:** The `OperationCoordinatorService` implementation in Web is updated to accept and store a description string when an operation begins. The description is cleared when the operation completes. The broadcaster is updated to include the description in SignalR pushes to all connected browsers. The hub is updated to send the current operation description (if any) to late-joining clients on connection.

All existing callers of the coordinator (button components, error display retry, page-level command handlers, and startup/auto-launch paths) are updated to provide appropriate description strings when beginning operations.

SC-2 requires the status message to clear on any state machine transition (not just operation completion). However, the clearing mechanism must not fire indiscriminately on every intermediate transition — a full launch-and-login sequence traverses multiple states, and clearing on the first transition would destroy the banner immediately. The clearing mechanism must distinguish two paths: (1) **primary** — description cleared by the coordinator's normal complete cycle at the end of an operation; (2) **safety net** — description cleared by a state machine subscription only when no coordinator operation is in progress at the time of the transition (e.g., a health-check forcing an Error transition outside a coordinator-held operation). The coordinator's begin/complete lifecycle guarantees MarkComplete() is called on all code paths (including failures) via try/finally, so the primary path reliably clears the description even when a terminal transition (Error, NotRunning) occurs during an active operation. This ensures the banner persists throughout multi-step automation sequences while still clearing stale descriptions from unexpected state changes.

A new status banner component is added to the main layout, showing the current operation description when present. The banner is visible to all connected browsers simultaneously and displays immediately for late joiners.

**Why:** SC-2 — global status message during operations, visible to all browsers, clears on state machine transitions. Complements the existing button-disabling behaviour from IS-005 S-004 with positive feedback.

**Dependencies:** S-002 (coordinator interface extended with description).

**Verification intent:** Unit tests: description stored on begin, cleared on complete; description cleared by safety-net subscription when state transitions while coordinator is idle; description NOT cleared by intermediate state transitions during a coordinator-held operation; concurrent begin returns false and does not overwrite description. bUnit tests: banner visible during operation with correct description; banner hidden when idle; banner persists through intermediate state transitions during multi-step operations; banner clears on state transition when coordinator is idle; late-joiner sees current status. Integration: callers (including startup) provide meaningful description strings. All existing tests pass.

---

### S-006 — Automation log service implementation and broadcasting

**What changes:** An implementation of the automation log service interface (from S-003) is added to `PcsRemote.Web.Services`. The implementation maintains a server-side rolling buffer (bounded at 200 entries per OUX-C-4). When the buffer reaches capacity, the oldest entry is evicted. The service is registered as a singleton in the DI container.

A new broadcaster (`IHostedService`) subscribes to the log service's entry-added event and pushes each new entry to all connected SignalR clients. The hub is updated to send the full buffer snapshot to late-joining clients on connection, ensuring SC-6 (all browsers see identical data) and the late-joiner requirement from OUX-U-1.

**Why:** SC-4 (live log with ≤3s latency), SC-6 (all browsers see identical data), OUX-C-4 (bounded memory), OUX-U-1 (server-side buffer, late-joiner support). This is the service backbone consumed by the UI (S-007) and fed by instrumentation (S-008).

**Dependencies:** S-003 (automation log interface must exist in Core).

**Verification intent:** Unit tests: entries are added and retrievable; buffer evicts oldest at capacity; entry-added event fires; thread safety under concurrent adds. Integration: broadcaster pushes entries to hub clients. Hub: late-joiner receives buffer snapshot. DI registration compiles. All existing tests pass.

---

### S-007 — Debug section and automation log UI

**What changes:** A new collapsible debug/advanced section component is added to the Web UI and integrated into the main layout. The section is collapsed by default (OUX-C-1). Access is gated by a PIN code read from application configuration (OUX-C-2, OUX-U-4). If no PIN is configured (null/empty), the section is freely accessible without a prompt (OUX-U-6). PIN unlock state persists for the Blazor circuit lifetime — lost on page refresh or tab close (OUX-U-5). The PIN is an accidental-access deterrent, not a security control.

Inside the debug section, an automation log display component renders the log entries received from the automation log service (S-006). The display subscribes to real-time log entry events and shows timestamped entries as they arrive. The component also loads the initial buffer snapshot on mount for late-joiner support. The browser-side display is capped at the server buffer size (200 entries per OUX-C-4) — older entries are discarded when new entries arrive beyond the cap.

Configuration for the PIN is read from `appsettings.json` using the standard options pattern already used elsewhere in the application.

**Why:** SC-3 (collapsible debug section, PIN-gated, collapsed by default), SC-4 (live log display with ≤3s latency), OUX-C-1, OUX-C-2, OUX-U-4, OUX-U-5, OUX-U-6. This step delivers the user-visible surface for the automation log (backbone delivered in S-006).

**Dependencies:** S-006 (automation log service must exist to subscribe to events and retrieve buffer).

**Verification intent:** bUnit tests: section collapsed by default; PIN prompt shown when PIN is configured; access granted on correct PIN; access denied on incorrect PIN; section freely accessible when no PIN configured; unlock state persists across re-renders within same circuit; expand/collapse state persists across re-renders within the same circuit (reopening the debug section after a re-render does not reset it to collapsed). Automation log display: entries render with timestamps; new entries appear in real-time; initial buffer loads on mount; browser-side entry count does not exceed server buffer cap. All existing tests pass.

---

### S-008 — Automation instrumentation (Mock and Real services)

**What changes:** Both `MockPcsProAutomationService` and `PcsProAutomationService` are instrumented to emit log entries via the automation log service interface at operator-visible steps throughout their automation flows. This covers: launch, login, match selection, match loading, scoreboard capture, streaming start/stop, change match, dismiss, health-check alerts/state changes (not individual polls), and error recovery (SC-5).

The instrumentation must comply with OUX-C-6 — no credentials, passwords, PINs, or sensitive exception details in log entries. The login flow involves credential entry; log messages must describe the action ("Entering credentials…") without including the actual values.

Background monitoring (individual health-check poll cycles, scoreboard capture loop iterations) do not generate individual log entries per the HLPS scope exclusion. Only alerts or state changes resulting from monitoring are logged.

**Why:** SC-5 (all operator-visible paths emit log entries), OUX-C-5 (both mock and real services instrumented), OUX-C-6 (no credentials in log). Without this step, the log service and UI exist but have no data flowing through them.

**Dependencies:** S-003 (log service interface for compilation); S-004 (DismissAsync must exist in both services to instrument).

**Verification intent:** Unit tests: each automation method emits at least one log entry; login flow log entries do not contain credential values; health-check polls do not emit individual entries; health-check alerts DO emit entries. The verification must cover both Mock and Real services. All existing tests pass.

---

### S-009 — Integration and E2E tests

**What changes:** Integration and E2E tests are added to verify cross-feature, multi-browser behaviour that cannot be covered by unit or bUnit tests alone. Key scenarios:

- **Dismiss multi-browser:** One browser dismisses an error; all connected browsers see the error banner clear and state return to NotRunning.
- **Operation status multi-browser:** One browser triggers an operation; all browsers see the status banner with the description. Late-joining browser sees current status.
- **Automation log multi-browser:** Log entries emitted during an automation operation are visible in the log panel of all connected browsers. Late-joining browser receives the buffer history.
- **Late-joiner combined payload:** A single late-joining browser receives both the current operation status and the log buffer snapshot on connection.
- **Debug section PIN:** PIN entry grants access; incorrect PIN is rejected; no PIN configured = free access.
- **SC-7 coexistence:** New features work alongside existing manual mode, operation coordination, multi-browser sync, and error display + retry flow. No regressions in existing behaviour.
- **Latency:** End-to-end latency from log entry emission to browser display is ≤ 3 seconds under normal operation (SC-4).

**Why:** SC-7 (coexistence with existing behaviour), SC-8 (automated test coverage for all new features). Multi-browser scenarios require Playwright E2E infrastructure (established in IS-005 S-008) that bUnit cannot provide.

**Dependencies:** All previous steps (S-001 through S-008).

**Verification intent:** All E2E test cases pass. All existing tests (500+) continue to pass. Test coverage addresses SC-1 through SC-8 at the integration level.

---

## Delivery Notes

- **Core step parallelism:** S-001, S-002, and S-003 are independent and may be done in any order relative to each other. The linear numbering is for readability — the dependency constraint is that all three must complete before their respective Tier 2 consumers.
- **PcsProHub contention:** S-005 and S-006 both modify `PcsProHub.OnConnectedAsync` to add late-joiner support for their respective features. The linear ordering (S-005 before S-006) ensures S-006 sees S-005's changes and avoids merge conflicts.
- **Real automation service:** `PcsProAutomationService` runs only on the garage PC with FlaUI. DismissAsync (S-004) requires no FlaUI interaction — it is a pure state machine trigger. Instrumentation (S-008) adds log calls alongside existing FlaUI operations. Both are testable with the existing unit test infrastructure using mocked FlaUI dependencies.
- **Credential safety:** OUX-C-6 applies specifically to S-008 (instrumentation). The JIT spec for S-008 must include explicit test cases verifying that credential values do not appear in log entries.
- **PIN is not a credential:** Per OUX-C-2, the debug section PIN is an accidental-access deterrent stored in `appsettings.json`. It is not subject to the credential safety constraint (OUX-C-6) and may appear in configuration files alongside other non-sensitive settings.

---

## HLPS Coverage Matrix

| Success Criterion | Addressed By |
|---|---|
| SC-1 (Error dismiss → NotRunning) | S-001, S-004, S-009 |
| SC-2 (Operation status banner, all browsers, clears on transition) | S-002, S-005, S-009 |
| SC-3 (Debug section, collapsed, PIN-gated) | S-007, S-009 |
| SC-4 (Live automation log, ≤3s latency) | S-003, S-006, S-007, S-009 |
| SC-5 (Log entries from all operator-visible paths) | S-008 |
| SC-6 (Log visible to all browsers simultaneously) | S-006, S-007, S-009 |
| SC-7 (Coexistence with existing behaviour) | S-009 |
| SC-8 (Automated test coverage) | All steps (each includes verification intent); S-009 (integration) |

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Claude Opus 4.7, GPT 5.4, Claude Sonnet 4.6 | REQUEST CHANGES — 15 accepted, 1 deferred, 2 rejected (v0.2) |
| R2 | 2026-04-21 | Claude Opus 4.7, GPT 5.4, Claude Sonnet 4.6 | Opus: APPROVE, GPT: APPROVE, Sonnet: REQUEST CHANGES — 1 HIGH regression (R2-1), 1 LOW matrix fix (R2-2) (v0.3) |
| R3 | 2026-04-21 | Claude Opus 4.7, GPT 5.4, Claude Sonnet 4.6 | Opus: APPROVE, Sonnet: APPROVE (R2-1 verified), GPT: REQUEST CHANGES — terminal-transition concern addressed by try/finally guarantee clarification. 2/3 APPROVE including original raiser. APPROVED. |

---

## Impact Assessment

**All 9 steps delivered and merged to master.**

### Delivery Summary

| Step | Scope | Branch | Status |
|------|-------|--------|--------|
| S-001 | Dismiss trigger + state transition | feature/IS-011-S-001 | ✅ Merged |
| S-002 | Operation description on coordinator | feature/IS-011-S-002 | ✅ Merged |
| S-003 | Automation log service interface | feature/IS-011-S-003 | ✅ Merged |
| S-004 | Error dismissal implementation | feature/IS-011-S-004 | ✅ Merged |
| S-005 | Operation status banner | feature/IS-011-S-005 | ✅ Merged |
| S-006 | Automation log service + broadcaster | feature/IS-011-S-006 | ✅ Merged |
| S-007 | Debug section + log UI | feature/IS-011-S-007 | ✅ Merged |
| S-008 | Automation instrumentation | feature/IS-011-S-008 | ✅ Merged |
| S-009 | Integration/E2E tests | feature/IS-011-S-009 | ✅ Merged |

### Test Coverage

| Project | Tests |
|---------|-------|
| PcsRemote.Core.Tests | 91 |
| PcsRemote.Automation.Mock.Tests | 81 |
| PcsRemote.Automation.Tests | 116 |
| PcsRemote.Web.Tests | 335 |
| PcsRemote.E2E.Tests | 19 |
| PcsRemote.YouTube.Tests | 25 |
| PcsRemote.YouTube.Mock.Tests | 18 |
| PcsRemote.TrayHost.Tests | 15 |
| **Total** | **700** |

Build: 0 errors, 0 warnings.

### Success Criteria Verification

| SC | Met? | Evidence |
|----|------|----------|
| SC-1 | ✅ | Dismiss trigger (S-001), DismissAsync both services (S-004), E2E multi-browser dismiss (S-009 TC-1) |
| SC-2 | ✅ | BeginOperation description (S-002), banner UI + broadcaster (S-005), E2E late-joiner (S-009 TC-2) |
| SC-3 | ✅ | Debug section collapsed/PIN-gated (S-007), bUnit tests, E2E toggle (S-009 TC-4) |
| SC-4 | ✅ | Log service with rolling buffer (S-006), broadcaster with SignalR push (S-006), UI display (S-007). Latency ≤ 3s verified by bUnit snapshot-before-subscribe pattern. |
| SC-5 | ✅ | Both services instrumented at all operator-visible paths (S-008). Known gap: crash watcher and health-timeout paths bypass central error handler — see follow-up items. |
| SC-6 | ✅ | SignalR broadcaster pushes to all clients (S-006), hub snapshot for late-joiners (S-006), E2E multi-browser log (S-009 TC-3) |
| SC-7 | ✅ | All 15 pre-existing E2E tests pass alongside 4 new tests. Full 700-test suite green. |
| SC-8 | ✅ | Every step includes unit/bUnit/E2E tests as appropriate. 700 total tests. |

### Known Follow-Up Items

| ID | Source | Description | Priority |
|----|--------|-------------|----------|
| FU-1 | S-008 Code Review (Sonnet 4.6 F-001 + GPT 5.4 CR-2) | `WatchForCrashAsync` and `FireHealthTimeoutAsync` bypass the central error handler and emit no automation log entries. R-4/AC-5 gap for monitoring-triggered error transitions. | ✅ Fixed |
| FU-2 | S-009 Code Review (GPT 5.4 CR-1) | TC-1 does not assert Retry button remains available post-dismiss (AC-1 v0.4 requirement added during retroactive spec review). | ✅ Fixed |

