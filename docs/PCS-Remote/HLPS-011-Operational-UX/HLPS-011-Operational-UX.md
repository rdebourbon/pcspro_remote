# HLPS-011: Operational UX — Visibility, Feedback & Error Recovery

| Field | Value |
|---|---|
| **Document** | HLPS-011-Operational-UX.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.4 |
| **Date** | 2026-04-21 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Dependencies** | HLPS-005 (error display, manual mode, operation coordination), HLPS-008 (YouTube streaming UI), HLPS-010 (automation hardening, health-check poll) |

---

## 1. Problem Statement

PCS Remote is now fully functional — match selection, scoreboard, YouTube streaming, and automation hardening are all delivered (HLPS-001 through HLPS-010). However, hands-on operational testing has revealed four UX gaps that reduce operator confidence and complicate troubleshooting on match days:

1. **No way to dismiss an error without retrying.** When PCS Pro enters an error state, the operator's only option is the Retry button, which restarts the full launch-and-login sequence. If the operator wants to manually fix PCS Pro first (e.g., close a rogue dialog, restart the process), they have no way to clear the error banner and return to a clean starting state. There is no "I've dealt with this, clear the error" action.

2. **No visual feedback during long-running operations.** When an automation command is executing (e.g., loading a match, starting a stream), the buttons are disabled but there is no indication of *what is happening* or *how long it might take*. The operator sees a frozen UI and may assume the system is stuck.

3. **No live automation log.** When something goes wrong, the operator must SSH into the garage PC and read Serilog log files. There is no way to see what the automation service is doing from the browser in real time.

4. **No debug/advanced section.** There is no dedicated area in the Web UI for diagnostic or advanced features. The automation log and any future debug tools have no home.

These are operator-experience problems, not functional defects. The system works, but the operators — cricket club volunteers, not IT professionals — lack the visibility and control they need to confidently manage match-day operations without technical assistance.

---

## 2. Scope

### In Scope

- **Error dismissal (GAP-003):** A single-click action that clears the Error state and returns the system to a quiescent state (NotRunning) *without* restarting automation. Distinct from Retry (which clears *and* restarts). The operator uses this when they have manually resolved the issue and want to start fresh on their own terms. Dismiss must work independently of manual mode — the state transition is valid regardless of whether manual mode is active. Note: NotRunning means "automation is not managing a PCS Pro lifecycle" — it does not guarantee the PCS Pro process has been terminated. The IS must address the case where PCS Pro is still running after dismissal (existing process-detection logic in the launch path already handles this).

- **Operation-in-progress feedback (GAP-014):** A global status message and visual indicator shown while any user-facing or startup automation operation is executing, communicating *what* the system is doing (e.g., "Loading match…", "Starting live stream…"). Complements the existing button-disabling behaviour (delivered in IS-005 S-004) by adding *positive* feedback rather than only *negative* feedback (greyed-out buttons). The indicator must be visible to all connected browsers simultaneously — when one operator triggers an action, all sessions see the same status. Late-joining browsers must receive the current operation status immediately on connection. Background monitoring activities (health-check polls, scoreboard capture loops) are excluded — they run too frequently to produce meaningful user-visible status. The status message must clear automatically on operation completion, operation failure, or any state machine transition to Error or NotRunning.

- **Real-time automation log (GAP-017):** A live log panel in the Web UI that shows automation activity as it happens. The log is backed by a **server-side rolling buffer** (last 200 entries) so that all connected browsers see identical data and late joiners receive the full recent history. Entries consist of a timestamp, action description, and outcome. Must push updates with latency ≤ 3 seconds. Shows entries for operator-visible automation steps — e.g., "Launching PCS Pro…", "Entering credentials…", "Selecting match row 2…", "Capturing scoreboard image…". Background monitoring events (individual health-check polls) do not generate individual log entries — only alerts or state changes resulting from monitoring are logged. Provides the same visibility currently available only in the server-side Serilog rolling file — but accessible from any browser on the LAN without SSH or file access.

- **Debug/Advanced section (GAP-001):** A collapsible or toggleable area in the Web UI that houses the automation log and any future debug tools. Not visible by default — operators expand it when troubleshooting. Protected by a simple PIN code (configured in appsettings.json) as an accidental-access deterrent — this is not a security control and is consistent with A-4 (no authentication on a trusted LAN). If no PIN is configured (null or empty), the debug section is freely accessible without a prompt (development convenience). PIN unlock scope is per Blazor circuit — lost on page refresh or tab close. Prevents the main UI from becoming cluttered with diagnostic information during normal use.

### Out of Scope

- Switch User / Re-Login (GAP-002) — requires new automation flows for credential entry and session management.
- Date Filter Override (GAP-005) — requires automation service changes for match selection.
- Site/Competition Filter Awareness (GAP-006) — depends on PCS Pro UI element discovery.
- Team Name Formatting (GAP-009, DEF-001, DEF-002, DEF-004) — data enrichment cluster, separate concern.
- WiX MSI Installer (GAP-012) — deployment infrastructure, separate HLPS.
- YouTube Setup Tray Shortcut (GAP-013) — tray host feature.
- Club Logo / Tray Icon (GAP-015, GAP-016) — branding assets.
- Live stream status health signal (DEF-005) — extends health-check, separate concern.
- CI/CD pipeline (U-6) — infrastructure.

---

## 3. Success Criteria

| ID | Criterion | Addresses |
|---|---|---|
| SC-1 | When in Error state, the operator can dismiss the error and return to NotRunning without triggering automation restart. The error banner and error message text are cleared from the UI. The Retry button remains available as a separate action. Dismiss works regardless of whether manual mode is active. | GAP-003 |
| SC-2 | While any user-facing or startup automation operation is executing, the Web UI shows a global status message describing the operation in progress (e.g., "Loading match…"). The message is visible to all connected browsers, including late joiners who connect mid-operation. The message clears automatically on any state machine transition (including to Error or NotRunning). | GAP-014 |
| SC-3 | The Web UI contains a collapsible debug/advanced section that is collapsed by default. Access requires entering a PIN code configured in appsettings.json (accidental-access deterrent, not a security control). If no PIN is configured, the section is freely accessible. PIN unlock and expand/collapse state persist for the Blazor circuit lifetime (lost on page refresh). | GAP-001 |
| SC-4 | When the debug section is expanded, a live automation log displays timestamped entries for automation actions as they execute, with update latency ≤ 3 seconds. | GAP-017 |
| SC-5 | The automation log shows entries from all operator-visible automation paths: launch, login, match selection, match loading, scoreboard capture, streaming start/stop, change match, health-check alerts/state changes (not individual polls), and error recovery. | GAP-017 |
| SC-6 | The automation log is visible to all connected browsers simultaneously — not just the browser that initiated the action. | GAP-017 |
| SC-7 | All new features work correctly alongside existing behaviour: manual mode disabling, operation coordination, multi-browser state sync, and the existing error display + retry flow. | All |
| SC-8 | All new features are covered by automated tests (unit, bUnit component, or E2E as appropriate). | All |

---

## 4. Constraints

All constraints from PROJECT-CONTEXT.md §4 apply. Additionally:

| ID | Constraint | Rationale |
|---|---|---|
| OUX-C-1 | The debug/advanced section must not be visible by default. Normal operators should not be confronted with diagnostic information during routine match-day use. | Operator audience is non-technical volunteers. |
| OUX-C-2 | The debug/advanced section PIN is an accidental-access deterrent, not a security control. This is consistent with A-4 (no authentication on a trusted LAN). The PIN may be stored in appsettings.json — it is not a credential. | Proportionate to threat model; LAN-only deployment. |
| OUX-C-3 | Error dismissal must be implemented as a new named trigger in the state machine with a defined Error → NotRunning transition. It must not reuse the Retry trigger, bypass the state machine, or directly set state. | Architectural integrity — PcsProStateMachine is the authoritative state owner (PROJECT-CONTEXT §2, point 3). |
| OUX-C-4 | The automation log must not introduce unbounded memory growth. Log entries must be bounded on the server (rolling buffer, 200 entries) and in the browser (browser displays at most the server buffer size). | Browser clients are resource-constrained; sessions may last hours. |
| OUX-C-5 | All new Core contract changes (including interface changes and state machine trigger additions) must be implemented in both the real (`PcsRemote.Automation`) and mock (`PcsRemote.Automation.Mock`) services. | Architecture rule — mock and real are interchangeable at the DI boundary. |
| OUX-C-6 | The automation log must not include credentials, passwords, secret values, PINs, or sensitive exception details. Log entries must be operator-safe for display in a browser accessible by any LAN user. | Login flow involves credentials; the log must redact them. |

---

## 5. Unknowns Register

| ID | Description | Owner | Blocking? | Resolution | Status |
|---|---|---|---|---|---|
| OUX-U-1 | Should the automation log retain entries across page navigations within the same browser session, or is it acceptable to clear on navigation? | User | No | Server-side rolling buffer (last 200 entries). All browsers see the same data. Late joiners receive the full buffer. No browser-local storage needed. | ✅ Resolved |
| OUX-U-2 | Should the activity indicator be a global overlay/spinner, a per-button spinner, or a status bar message? | User | No | Global status message (e.g., "Loading match…" in header/banner area). Broadcasts to all connected browsers. No per-button mapping. | ✅ Resolved |
| OUX-U-3 | Should error dismissal require confirmation ("Are you sure you want to dismiss this error?") or be a single-click action? | User | No | Single click. Trusted operators on a local network — no confirmation needed. | ✅ Resolved |
| OUX-U-4 | Should the debug/advanced section require a PIN or password? | User | No | Simple PIN code configured in appsettings.json. Accidental-access deterrent, not a security control. | ✅ Resolved |
| OUX-U-5 | What is the PIN unlock scope and lifetime? | Agent | No | Per Blazor circuit. Lost on page refresh or tab close. Re-entry required after refresh. | ✅ Resolved |
| OUX-U-6 | What happens when the PIN is absent or empty in configuration? | Agent | No | Debug section is freely accessible without a prompt (development convenience). | ✅ Resolved |

---

## 6. Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| OUX-R-1 | Instrumenting automation services for log entries requires touching multiple classes across `PcsRemote.Automation` and `PcsRemote.Automation.Mock`. Risk of introducing regressions. | Medium | Medium | Test-first approach; existing test suites (500+ tests) catch regressions. |
| OUX-R-2 | The automation log may generate high message volume during rapid automation sequences (e.g., match selection with grid parsing). Risk of flooding the SignalR connection. | Low | Low | Bounded log window + message batching if needed. |
| OUX-R-3 | Adding a Dismiss trigger to the state machine changes a contract shared across Core, Automation, Automation.Mock, and Web. Risk of missed update. | Low | Medium | State machine is well-tested; compiler will catch missing switch arms. |
| OUX-R-4 | The operation-in-progress status message requires extending a Core interface to carry a description string. This touches all callers across all assemblies. Risk of missed callers or stale descriptions. | Low | Medium | Compiler-enforced interface contract + existing test suite (500+ tests) catches regressions. |

---

*HLPS-011 Operational UX — v0.4. 2026-04-21. APPROVED by adversarial panel (Opus 4.7, GPT 5.4, Sonnet 4.6) after R1+R2.*
