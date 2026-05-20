# IS-021: Play-Cricket Auto-Watch — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-021-PlayCricket-Auto-Watch.md |
| **Status** | APPROVED |
| **Version** | 0.9 |
| **Date** | 2026-05-20 |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` |
| **Prerequisites** | HLPS-001 (foundation), HLPS-006 (FlaUI), HLPS-010 (automation hardening), HLPS-016 (operator mode) — all delivered. |

---

## Overview

This sequence implements the HLPS-021 scope in ten atomic steps.

The feature has two independent runtime concerns: **auto-load** (FlaUI-based detection of scorer-started matches) and **auto-close** (Play-Cricket API-based detection of match completion). These share a single hosted service and a single polling interval.

Steps proceed in dependency order: Core contracts first, then implementations, then the two hosted-service polling loops, then peripheral concerns (midnight reset, UI, push notifications).

> **Note on auto-load method:** The automation service already exposes a method for retrieving today's converted match list. The JIT Spec for S-005 will confirm whether this existing method satisfies SC-1 (the match list read must be preceded by a UI refresh) or whether a new dedicated method is required. Either outcome satisfies SC-1 — the requirement is on the behaviour, not the method.

Steps use stable IDs S-001 through S-010. IDs are never renumbered; deferred steps leave gaps.

---

## Accepted Risks from Approved HLPS-021

| Risk Summary | Citation |
|---|---|
| Fixture ID resolution may fail (no unique match); auto-close is skipped and operator manages end manually | HLPS-021 SC-6, C-9 |
| Play-Cricket API outage suppresses auto-close but does not affect auto-load | HLPS-021 SC-9 |
| Multiple simultaneous scorer-started matches suppress auto-load silently (no operator alert) | HLPS-021 SC-10 — explicit user requirement |
| Auto-watch always starts disabled on restart; no unattended startup-to-end operation | HLPS-021 SC-7, A-7 |
| Browser push notifications are opt-in; graceful degradation on denial | HLPS-021 C-7 |
| OQ-1 (exact Play-Cricket status strings) and OQ-3 (team name normalisation algorithm) are deferred to JIT Spec | HLPS-021 §9 Open Questions |

---

## Steps

### S-001 — Play-Cricket project scaffold, Core contracts, and configuration

**What:** Two new class library projects for Play-Cricket integration (real and mock), both platform-neutral. An API client contract interface and a fixture record type added to `PcsRemote.Core`. A configuration options class added to the real project (following the existing options pattern), covering API credentials, site IDs, polling interval, countdown duration, and a mock flag. The Play-Cricket configuration section scaffolded in `appsettings.json`. Both projects added to the solution.

**Why:** Foundation for all downstream steps. Placing the API client contract and fixture record in `PcsRemote.Core` maintains the zero-external-dependency architectural rule (C-1). Configuration must be scaffolded before any service can consume it.

**Dependencies:** None.

**Verification:** Solution builds with zero warnings. Both new projects reference only `PcsRemote.Core` with no Windows-specific dependencies. Configuration binds with expected defaults. All existing tests pass.

---

### S-002 — Real Play-Cricket API client

**What:** A real HTTP implementation of the API client contract, added to the Play-Cricket project. Uses the factory-based HTTP client pattern consistent with the existing codebase. Maps the JSON response to fixture records; returns an empty list on HTTP error or deserialisation failure (warning logged). Registered in DI via the mock flag.

**Why:** The auto-close polling loop (S-007) and fixture ID resolution (S-006) both depend on a working API client. Building and testing the HTTP layer in isolation ensures mapping and error-handling concerns are verified before being composed with polling logic.

**Dependencies:** S-001.

**Verification:** Unit tests cover: successful response maps to correct fixture records; HTTP error returns empty list with warning; malformed JSON returns empty list with warning; cancellation propagates to the HTTP call. All existing tests pass.

---

### S-003 — Mock Play-Cricket API client

**What:** An in-memory mock implementation of the API client contract, added to the mock project. Returns configurable fixture lists per configured site; makes no network calls. Registered in DI when mock mode is enabled.

**Why:** All development and local testing uses mock mode. The mock must be complete before fixture ID resolution (S-006) and auto-close polling (S-007) can be developed without network access. Serves as the reference contract implementation.

**Dependencies:** S-001.

**Verification:** Unit tests cover: returns configured fixtures for a known site; returns empty list for an unknown site; never throws on any input. All existing tests pass.

---

### S-004 — Auto-watch watcher service — interface and state

**What:** A watcher service interface added to `PcsRemote.Core`, exposing: observable state (enabled flag, resolved fixture ID, active countdown remaining time), lifecycle methods (enable/disable, start/cancel countdown, per-tick countdown advance), auto-load duplicate-suppression methods, auto-close dismiss methods, and events for all significant state transitions with immutable snapshot payloads. The implementation added to `PcsRemote.Web` owns all domain state for this feature. The hosted service (S-005/S-007) is fully stateless — it reads and writes exclusively through this interface. The implementation subscribes to the existing automation service state change event to reset the fixture ID when the system is no longer in a loaded state. Enabling auto-watch clears any previously dismissed fixture (operator intent to resume). Disabling auto-watch delegates to countdown cancellation as a single code path. Thread safety is an implementation concern addressed in the JIT Spec. Registered as a singleton.

**Why:** Separating state management from polling keeps the hosted service focused on scheduling and makes the watcher service independently testable. All UI components and the hosted service depend on the interface; no component touches domain state directly. Addresses G-4, G-5, SC-2, SC-4, SC-6, SC-7.

**Dependencies:** S-001.

**Verification:** Unit tests cover the full state surface: enable/disable toggle; enable clears dismissed fixture; disable delegates to countdown cancellation without duplicate events; countdown start, tick progression, and expiry; 60-second warning emission (suppressed when countdown starts at or below 60 seconds); countdown cancellation; auto-load suppression record and lookup; auto-close dismiss record and lookup; fixture ID reset on state exit; concurrent access does not corrupt state or lose events. All existing tests pass.

---

### S-005 — Auto-load polling loop

**What:** The auto-watch hosted service added to `PcsRemote.Web` (auto-load half only). Polls at the configured interval; each iteration completes before the next begins (non-reentrant). When auto-watch is enabled, manual mode is inactive, and the state machine is in a match-selection state: calls the automation service to retrieve the current converted match list — the call must include a UI refresh per SC-1 (the JIT Spec confirms whether the existing method satisfies this or a new dedicated method is required). If exactly one match is returned and it is not already suppressed, a final pre-load re-check is performed before triggering match load; suppression is recorded on success. Zero results are a no-op; multiple results are a no-op logged per SC-10. Automation exceptions are caught and logged.

**Why:** Primary user-facing value — the scorer starts their match and the scoreboard follows automatically. Implementing and verifying the auto-load loop before the API integration allows it to be independently validated on match day. Addresses G-1, SC-1, SC-10, SC-11.

**Dependencies:** S-001, S-004.

**Verification:** Unit tests cover: auto-load fires on a single unsuppressed match; pre-load re-check aborts when conditions change mid-tick; suppressed match is not re-loaded; multiple matches logs ambiguity and takes no action; automation exception does not crash the service. All existing tests pass.

---

### S-006 — Post-load fixture ID resolution

**What:** The hosted service extended to subscribe to state machine transitions. On entry to the loaded state, a cancellable async resolution task queries all configured Play-Cricket sites in parallel, filtering to the loaded match's date (not today — ensures manually loaded non-today matches resolve correctly). A guard skips resolution if the loaded match date is unset. Team names are normalised and compared across all site results; a unique match sets the fixture ID in the watcher service. Zero or multiple matches leave it unresolved with a warning (auto-close polling skipped per SC-6). If a fixture ID is already set when a new unique match is found, the active countdown is cancelled before the ID is updated. In-flight tasks are cancelled on re-entry to the loaded state or on state exit. If any configured site query fails during a resolution cycle, the resolution outcome for that cycle is indeterminate — no fixture ID is set or updated, a warning is logged, and resolution retries on the next poll tick. The normalisation algorithm is resolved in the JIT Spec (OQ-3).

**Why:** The resolved fixture ID is the bridge between the PCS Pro domain and the Play-Cricket API. Without it auto-close cannot function. Implementing resolution separately from polling keeps each concern independently testable. Addresses G-4, SC-6.

**Dependencies:** S-002 (or S-003), S-004, S-005.

**Verification:** Unit tests cover: unique match found sets fixture ID; zero or multiple matches leaves it unresolved with warning; resolution uses loaded match date not today; unset match date skips resolution with warning; existing fixture ID is cancelled-then-replaced when a new unique match is found; task cancelled on re-entry; task cancelled on state exit. All existing tests pass.

---

### S-007 — Auto-close polling and countdown

**What:** The hosted service extended with the auto-close polling half. On each poll tick, when a fixture ID is resolved and all pre-conditions are met (auto-watch enabled, manual mode off, state is loaded, fixture not dismissed), queries the fixture status. On confirmed completion (OQ-1, resolved in JIT Spec), performs a final re-check then starts the countdown via the watcher service. A second-resolution inner loop advances the countdown; on expiry, the fixture is dismissed immediately (preventing any retry regardless of subsequent outcome), a final pre-execution re-check is performed — confirming all pre-conditions still hold and that the resolved fixture identity has not changed since countdown started — then stop-streaming is called followed by return-to-match-selection, with the auto-close event fired on success. Cancellation is triggered by any of: user action via UI; auto-watch being disabled; manual mode becoming active (hosted service subscribes to the manual mode change event and cancels immediately, not deferred to next poll); state exit from loaded; or fixture ID change. All cancellation paths also dismiss the fixture. Expiry sequence exceptions are caught and logged; the fixture remains dismissed so the operator must intervene manually (accepted risk per SC-9).

**Why:** The mechanism that makes the scoreboard self-managing at match end. Implementing after S-006 ensures fixture resolution is solid before close logic depends on it. Addresses G-2, G-3, SC-2, SC-3, SC-4, SC-9, SC-11.

**Dependencies:** S-002 (or S-003), S-004, S-005, S-006.

**Verification:** Unit tests cover: completed status triggers countdown when all pre-conditions met; dismissed fixture prevents countdown restart; countdown advances to expiry; dismiss called immediately on expiry before re-check; auto-close event fires only on successful sequence completion; final re-check aborts sequence on failed conditions or fixture identity mismatch; all five cancellation triggers also dismiss; manual-mode cancellation fires immediately not deferred; expiry exceptions caught without crashing the service. All existing tests pass.

---

### S-008 — Midnight reset

**What:** A separate midnight reset hosted service added to `PcsRemote.Web`. Calculates and delays to local midnight; on wake, calls the automation service to return to match selection if the system is in a loaded state and manual mode is off, then unconditionally clears the auto-load suppression record in the watcher service. Re-arms for the following midnight. Exceptions from the match-return call are caught and logged; suppression is always cleared regardless.

**Why:** An independent safety net decoupled from auto-watch toggle state. Ensures suppression records do not persist across match days and stale loaded matches are resolved at day boundary. Carries no Play-Cricket API dependency. Addresses SC-1, SC-8.

**Dependencies:** None beyond the existing automation and manual mode services.

**Verification:** Unit tests cover: match-return fires at midnight when in loaded state and manual mode off; match-return not called in other states or when manual mode active; suppression cleared in all wake paths; suppression cleared even when match-return throws; service re-arms for the next midnight. All existing tests pass.

---

### S-009 — Web UI — auto-watch toggle and countdown banner

**What:** An auto-watch enabled/disabled toggle added to the debug panel, following the existing debug toggle pattern (HLPS-019). Wired to the watcher service enable/disable; reflects live enabled state. A countdown banner added to the main UI, visible when a countdown is active, showing remaining time updated in real time via a component-level periodic timer. Includes a cancel button. Both controls start in the disabled/hidden state on mount per SC-7.

**Why:** Operator visibility and control over the auto-watch feature. UI is isolated from service logic and implemented late to avoid churn while service contracts settle. Addresses G-3, G-5, SC-2, SC-4, SC-7.

**Dependencies:** S-004.

**Verification:** bUnit tests cover: toggle renders disabled on mount; toggle wires to enable/disable; countdown banner hidden when no countdown active; banner appears with correct time and updates; banner dismisses on countdown end (cancelled or fired); cancel button triggers cancellation. All existing tests pass.

---

### S-010 — Browser push notifications

**What:** A lazy-loaded JavaScript interop module added to `PcsRemote.Web` managing the browser notification permission and notification dispatch. Permission is requested on first auto-watch enable. Notifications are fired when the auto-close countdown starts and at the 60-second remaining warning (suppressed if the total countdown is 60 seconds or less per C-3a — the service handles this by not raising the warning event in that case). Degrades gracefully on permission denial with no crash or error state. Does not affect page load time.

**Why:** Optional enhancement layered on after all other functionality is operational. Highest-risk step due to JS interop and browser permission model — delivering last minimises blast radius. Addresses G-3, SC-5, C-3a, C-7.

**Dependencies:** S-004, S-009.

**Verification:** Tests cover: permission requested on first enable; countdown-start notification fires; 60-second warning notification fires; warning suppressed when countdown started at 60 seconds or less; no exception when permission denied. All existing tests pass.

---

## Step Dependency Summary

```
S-001 (scaffold + contracts)
  ├── S-002 (real API client)
  ├── S-003 (mock API client)
  └── S-004 (watcher service state)
        └── S-005 (auto-load polling)
              └── S-006 (fixture ID resolution) ← also needs S-002/S-003
                    └── S-007 (auto-close + countdown)
S-008 (midnight reset) — independent, no Play-Cricket dependency
S-009 (UI toggle + banner) ← depends on S-004
  └── S-010 (push notifications) ← also depends on S-004
```

| Step | Depends on |
|---|---|
| S-001 | — |
| S-002 | S-001 |
| S-003 | S-001 |
| S-004 | S-001 |
| S-005 | S-001, S-004 |
| S-006 | S-002 (or S-003), S-004, S-005 |
| S-007 | S-002 (or S-003), S-004, S-005, S-006 |
| S-008 | — (no Play-Cricket dependency) |
| S-009 | S-004 |
| S-010 | S-004, S-009 |

---

## SC Coverage Matrix

| SC | Addressed by |
|---|---|
| SC-1 | S-005, S-008 |
| SC-2 | S-004, S-007, S-009 |
| SC-3 | S-007 |
| SC-4 | S-004, S-007, S-009 |
| SC-5 | S-010 |
| SC-6 | S-006 |
| SC-7 | S-004, S-009 |
| SC-8 | S-008 |
| SC-9 | S-002, S-005, S-006, S-007 |
| SC-10 | S-005 |
| SC-11 | S-005, S-007, S-008 |

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| — | — | — | — | — | DRAFT | Awaiting review |
| R1 | 3 | claude-opus-4.5, gpt-5.4 | 12 findings (4×HIGH, 3×MEDIUM, 5×LOW) | All 12 accepted | REVISION REQUIRED | Countdown timer decoupled; suppression lifecycle defined; stale ID cancellation added; auto-cancel triggers added; non-reentrancy specified; refresh constraint firmed up; toggle binding corrected; parallel queries committed; service location committed |
| R2 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 3 findings (1×MEDIUM, 2×LOW, 1×INFO). GPT: 2×HIGH | All accepted except GPT-F2(b) rejected (midnight reset is intentionally independent of IsAutoWatchEnabled per SC-8) | REVISION REQUIRED (GPT) / APPROVED (Opus) | StartCountdown() added to interface; responsibility boundary documented; suppression set ownership specified; resolver cancellation on state exit; DisableAutoWatch() hard-off cancels countdown |
| R3 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 2×LOW. GPT: 1×HIGH, 1×MEDIUM | All 4 accepted | REVISION REQUIRED (GPT) / APPROVED (Opus) | TickCountdown() + AutoCloseT60Warning added to interface; start-countdown gated on IsAutoWatchEnabled + !manual + MatchLoaded; T-60 suppression in S-010 clarified |
| R4 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 2×MEDIUM, 2×LOW. GPT: 2×HIGH, 1×MEDIUM | All 6 accepted (Opus F4 + GPT F3 same finding) | REVISION REQUIRED | TickCountdown() returns false when inactive; countdown loop exit on null; expiry exception handling; thread safety lock spec; per-fixture dismissed flag prevents restart after cancel; final re-check before expiry actions |
| R5 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 1×MEDIUM, 2×LOW. GPT: 2×HIGH, 1×MEDIUM | All 6 accepted | REVISION REQUIRED | bool? TickCountdown() (null=inactive, false=running, true=expired); StartCountdown is fixture-scoped (fixtureId param); expiry re-check validates fixtureId; CurrentFixtureId change as explicit cancel trigger (e); pre-LoadMatchAsync re-check; CancellationToken for S-006; dismiss-on-retoggle noted intentional |
| R6 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 2×LOW, 1×INFO. GPT: 2×HIGH, 1×MEDIUM | All 6 accepted | REVISION REQUIRED (GPT) / APPROVED (Opus) | ClearAutoLoadSuppression() added to interface; suppression cleared only by S-008 (not StateChanged); DisableAutoWatch() delegates to CancelCountdown(); TickCountdown() does NOT fire AutoCloseFired; AutoCloseFired fired in S-007 after successful expiry; _dismissedFixtureId set immediately on TickCountdown()=true; S-006 filters by loaded match MatchDate (not today); S-006 cancel-before-assign on stale CurrentFixtureId |
| R7 | 3 | claude-opus-4.5, gpt-5.4 | Opus: 1×MEDIUM, 2×LOW. GPT: 2×HIGH, 2×MEDIUM (1 rejected) | Accepted: Opus-F1+GPT-F1(HIGH, same finding), GPT-F2(MEDIUM→partial failure policy), GPT-F3(LOW→MatchDate guard), GPT-F4(MEDIUM→banner update mechanism), Opus-F2(LOW→ManualModeChanged sub). Rejected: GPT-F5 (SC-5 not in HLPS; single-user system) | REVISION REQUIRED | Suppression+dismissed ownership moved to PlayCricketWatcherService; 4 interface methods added (RecordAutoLoadSuppressed/IsAutoLoadSuppressed/DismissAutoClose/IsAutoCloseDismissed); hosted service is fully stateless; MatchDate==MinValue guard in S-006; partial failure policy documented; ManualModeChanged subscription explicit in S-007; banner uses PeriodicTimer for real-time updates |
| R8 | 0 | Self-Cert | — | — | SELF-CERTIFIED | Abstraction-level editorial pass per IS guidance. All steps rewritten to comply with IS Abstraction Level rules (no method signatures, property names, config keys, or compile-correctness-sensitive identifiers). SC Coverage Matrix and canonical dependency table added. No functional requirement added, removed, or changed. All architectural decisions from R1–R7 preserved in behavioural language. |
| R10 | 3 | claude-opus-4.5, gpt-5.4 | 0 findings | All fixes verified adequate | APPROVED | F1 fix (S-007 fixture-identity re-check), F2 fix (S-006 partial-site-failure policy), F3 fix (SC-9 matrix) all confirmed correct and internally consistent. No regressions introduced. Unanimous approval. |