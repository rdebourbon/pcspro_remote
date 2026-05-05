# IS-019 — Date Selection Override — Implementation Sequence

| Field        | Value                          |
|--------------|--------------------------------|
| **Status**   | APPROVED                       |
| **Author**   | Copilot                        |
| **Created**  | 2026-05-05                     |
| **Governs**  | HLPS-019 (APPROVED)            |

---

## Overview

This document breaks HLPS-019 into an ordered sequence of atomic, independently valuable implementation steps. Each step produces a compilable, testable increment.

---

## S-001 — Date Selection Service (Core)

**What:** Add a date selection toggle service interface and implementation to `PcsRemote.Core`. The service tracks whether the date selection feature is enabled and raises a change notification event.

**Why:** Foundation for all subsequent steps. The toggle state drives both the UI (S-004, S-005) and is consumed by the auto-reset mechanism (S-005). Addresses SC-1 (defaults to off), SC-4/SC-5 (auto-reset), and C-3 (in-memory, not persisted).

**Dependencies:** None.

**Verification:** Unit tests confirm: defaults to disabled on construction, enable/disable toggles state, change notification fires on transitions, redundant enable/disable calls are idempotent and do not fire spurious events.

---

## S-002 — Date-Parameterised Match Retrieval (Automation Layer)

**What:** Add a date-parameterised match retrieval method to the public automation service interface and both implementations (real and mock). Refactor the existing "today's matches" method to delegate to the new method. Update the internal match selection automation interface to accept a date parameter for its search operation.

**Why:** The automation layer must support searching for matches on an arbitrary date. Delegation from the existing method ensures no behavioural change for existing callers. Addresses SC-3 (types the selected date into PCS Pro) and §9 (mock obligation). Enables S-003 to satisfy SC-6 by decoupling date derivation from the `TimeProvider`.

**Dependencies:** None (no dependency on S-001 — the date is an explicit parameter, not read from the service).

**Verification:** Existing tests for the "today's matches" flow continue to pass unchanged. New tests verify the date-parameterised method forwards the correct date to the internal search automation and filters results by that date. Mock implementation returns synthetic data regardless of date input.

---

## S-003 — Remove Config-Based Date Override

**What:** Remove the `TestDateOverride` configuration key from `appsettings.json`, delete the `FixedDateTimeProvider` class, and simplify the DI registration to always use `TimeProvider.System`. Clean up any now-unused `TimeProvider` constructor parameter from the internal match selection automation class.

**Why:** Eliminates the production footgun that caused the original incident. Addresses SC-6 (config and class removed) and C-6 (existing `FakeTimeProvider` tests unaffected).

**Dependencies:** S-002 (the date-parameterised method must exist before removing the `TimeProvider`-based date derivation from the internal search automation).

**Verification:** Build succeeds. All existing tests pass. No references to the removed config key or class remain in source code or configuration files.

---

## S-004 — Debug Panel Toggle UI

**What:** Add an "Allow Date Selection" toggle button to the debug section component. Wire it to the date selection service's enable/disable methods. Register the service in DI.

**Why:** Provides the operator-facing control for enabling date selection. Addresses SC-2 (toggle visible in debug panel), C-2 (only accessible when debug panel is unlocked), and C-3 (defaults to off).

**Dependencies:** S-001 (service must exist to inject and wire).

**Verification:** Component tests confirm: toggle button renders in the debug section, clicking it toggles the service state, button label/appearance reflects current state, toggle cannot be activated when debug panel is locked (C-2 clause a).

---

## S-005 — Date Picker UI and Auto-Reset

**What:** Add a date picker control (defaulting to today's date per U-1) and "Load Matches for Date" button to the main page, visible only when the date selection toggle is enabled and the state machine is in the match selection state. Subscribe to the service's change notification to reactively show/hide the controls. Wire the button to call the date-parameterised match retrieval method. Implement auto-reset: disable the toggle on any transition into the match selection state. The auto-reset subscribes to state machine transitions within the main page component — this follows the established pattern used by existing cross-component services in the codebase, and is safe because Blazor Server maintains the component instance for the lifetime of the SignalR circuit. Include single-match auto-select for consistency with the existing flow.

**Why:** Completes the user-facing feature. Addresses SC-2 (date picker appears), SC-3 (selected date is used), SC-4/SC-5 (auto-reset on any MatchSelection entry), SC-8 (auto-select when single match), C-1 (existing buttons unchanged), and C-4 (universal auto-reset).

**Dependencies:** S-001 (service for toggle state and event), S-002 (date-parameterised method to call), S-004 (toggle must be controllable).

**Verification:** Component tests confirm: date picker defaults to today's date, date picker hidden when toggle is off, visible when toggle is on and state is MatchSelection, date picker remains visible when debug panel is collapsed or locked after toggle was enabled (C-2), hidden again after state transitions to MatchSelection (auto-reset), button calls the date-parameterised method with the selected date, single-match auto-select works. CSS styles match existing debug section patterns.

---

## S-006 — Documentation and Cleanup

**What:** Update operational and configuration guides to reflect the removal of `TestDateOverride` and the new debug-panel date selection feature. Update `session.md` to reflect HLPS-019 completion.

**Why:** Ensures operators understand the new workflow and removes stale configuration references from documentation.

**Dependencies:** S-003 (config removal), S-005 (feature complete).

**Verification:** Documentation accurately describes the new feature. No references to the removed config key remain in documentation.

---

## Step Dependency Graph

```
         S-001 ──────→ S-004 ──┐
           │                   ├──→ S-005 ──┐
         S-002 ──┬─────────────┘            ├──→ S-006
                 └──→ S-003 ────────────────┘
```

**Edges (text-canonical — use this table if the diagram is ambiguous):**

| Step  | Depends on          |
|-------|---------------------|
| S-001 | —                   |
| S-002 | —                   |
| S-003 | S-002               |
| S-004 | S-001               |
| S-005 | S-001, S-002, S-004 |
| S-006 | S-003, S-005        |

## SC Coverage Matrix

| SC   | Step(s)       |
|------|---------------|
| SC-1 | S-001, S-005  |
| SC-2 | S-004, S-005  |
| SC-3 | S-002, S-005  |
| SC-4 | S-001, S-005  |
| SC-5 | S-001, S-005  |
| SC-6 | S-003         |
| SC-7 | S-001, S-002, S-004, S-005 |
| SC-8 | S-005         |
