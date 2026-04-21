# IS-010: Automation Hardening — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-010-Automation-Hardening.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-21 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Prerequisites** | IS-006 (FlaUI Integration — delivered), diagnostic tool (12 steps passing) |

---

## Overview

This IS breaks HLPS-010 into 12 atomic steps that port proven diagnostic patterns into the production automation layer. Steps follow the HLPS delivery tiers:

- **Tier 1 (S-001 → S-007):** Core stub replacement — KnownElements, helpers, and all five FlaUi* classes
- **Tier 2 (S-008 → S-009):** Configurable timeout and streaming automation
- **Tier 3 (S-010 → S-012):** Team name structured type, Use Current Match, and health-check poll

Step IDs are stable. Deferred steps leave gaps; IDs are never renumbered.

All steps must comply with the Design Rules — Element Lifetime from HLPS-010 §2: reacquire controls per-operation, reacquire after dialog transitions, stale-element recovery, no UIA3Automation sharing across threads.

Refer to the diagnostic tool (`tools/AutomationDiagnostic/DiagnosticRunner.cs` and `KnownElements.cs`) for all element identifiers and interaction patterns.

---

## Tier 1 — Must-Ship ✅ COMPLETE

### S-001 — KnownElements Registry and DPI-Aware Startup ✅

**Status:** DELIVERED — commit `bfb7802`

**What changes:** Two foundational pieces that all subsequent steps depend on.

1. A centralized element identifier registry is created in the Automation project, ported from the diagnostic tool's version. All constants currently set to bare `"TODO"` (e.g., settings cog, scoreboard window identifiers) must be resolved or removed rather than imported as-is. Existing local constants scattered across the FlaUi* stub classes are not yet removed — each class handles its own migration in its own step.

2. DPI-aware process startup is added to the TrayHost entry point (before the web host builder) or via an application manifest. The approach is determined in the JIT spec.

Note: These are bundled because DPI correctness is only observable through capture (S-006), and both are prerequisites for all subsequent steps. If DPI startup encounters issues on the garage PC, the KnownElements registry work is still independently valid.

**Why:** HLPS-010 §2.1 (canonical element registry), §2.2 (DPI-aware startup). Addresses H-SC-1 (zero TODO), H-SC-4 (DPI-correct scoreboard capture), H-SC-9 (KnownElements verification).

**Dependencies:** None — this is the foundation step.

**Verification intent:** The new registry compiles and all constant values are verified against the diagnostic tool's version. DPI call is in place. Existing tests remain green (no behavioural changes yet).

---

### S-002 — Reusable Automation Helpers

**What changes:** Common FlaUI interaction patterns are extracted from the diagnostic runner into shared helper utilities in the Automation project. The exact helper set is determined during JIT spec creation by analysing which patterns are reused across multiple FlaUi* classes.

**Why:** HLPS-010 §2.5 (reusable helpers). Avoids code duplication across the five FlaUi* implementations in S-003 through S-007. Addresses H-SC-6 (testable helper logic).

**Dependencies:** S-001 (helpers may reference KnownElements constants).

**Verification intent:** Helper methods have unit tests covering core logic (e.g., timeout behaviour, child-text matching). Build stays green.

---

### S-003 — Login Automation

**What changes:** The login automation stub is replaced with a working implementation that enters the password, submits the login form, and detects login-failure dialogs. The implementation ports patterns from the diagnostic tool's login steps.

**Why:** HLPS-010 §2.3 (FlaUiLoginAutomation). Login is the first step in the automation flow — nothing else can proceed until the application is authenticated.

**Dependencies:** S-001 (KnownElements), S-002 (helpers for dialog detection).

**Verification intent:** Existing service-layer tests remain green (they use mocks). FlaUi* class no longer throws `NotImplementedException`. Manual verification deferred to garage PC end-to-end walkthrough (H-SC-3).

---

### S-004 — Match Selection and Row Parsing

**What changes:** Two tightly coupled components are implemented together:

1. The match selection automation stub is replaced with a working implementation ported from the diagnostic tool's match selection steps. A site-name filter configuration is added so searches are scoped to the correct club.

2. The match row parser stub is replaced with a working parser that converts DataGrid row text into match info records.

**Why:** HLPS-010 §2.3 (FlaUiMatchSelectionAutomation), §2.4 (MatchRowParser). These are coupled because the grid extraction output feeds directly into the parser. Addresses H-SC-1 (zero TODO_REPLACE in both files).

**Dependencies:** S-001 (KnownElements), S-002 (helpers for spinner polling, element search).

**Verification intent:** MatchRowParser gets unit tests with sample row text from diagnostic logs. Service-layer mock tests remain green. The new configuration property has a sensible default. H-SC-1 grep for the match selection and parser files returns zero.

---

### S-005 — Team Names Automation

**What changes:** The team names automation stub is replaced with a working implementation that navigates to the match details dialog via menu, reads ComboBox values for home and away teams, and closes the dialog. The implementation ports patterns from the diagnostic tool's team names step.

Note: This step uses the current `string` return type. The structured return type change (§2.10) is delivered separately in S-010 (Tier 3).

**Why:** HLPS-010 §2.3 (FlaUiTeamNamesAutomation). Team name extraction is required for the match-loaded state and downstream features (YouTube title).

**Dependencies:** S-001 (KnownElements), S-002 (helpers for menu navigation).

**Verification intent:** Stub no longer throws `NotImplementedException`. Service-layer mock tests remain green. Manual verification deferred to H-SC-3.

---

### S-006 — Scoreboard Automation

**What changes:** The scoreboard automation stub is replaced with a working implementation ported from the diagnostic tool's scoreboard capture step. Uses `Capture.Rectangle()` for DPI-aware screen capture (per H-U-1 resolution). The interface documentation is updated to reflect the sanctioned capture approach.

**Why:** HLPS-010 §2.3 (FlaUiScoreboardAutomation). Addresses H-SC-4 (valid JPEG at 150% DPI). Resolves H-U-1 documentation mismatch.

**Dependencies:** S-001 (KnownElements + DPI setup), S-002 (helpers for ToolWindow activation, popup menu search).

**Verification intent:** Stub no longer throws `NotImplementedException`. Interface XML doc reflects `Capture.Rectangle()`. `PROJECT-CONTEXT.md` §2 updated to replace the `PrintWindow` reference with `Capture.Rectangle()` (per H-U-1 resolution). Service-layer mock tests remain green. Actual capture quality verified on garage PC (H-SC-4). H-U-2 confirmed resolved: DPI-aware scoreboard capture validated on garage PC.

---

### S-007 — Change Match Automation

**What changes:** The change match automation stub is replaced with a working implementation ported from the diagnostic tool's change match step. The method **blocks until match load is confirmed** — it does not return after merely initiating the change.

**Why:** HLPS-010 §2.3 (FlaUiChangeMatchAutomation). Addresses the contract clarification from R1 review — the method must not return until the new match is fully loaded.

**Dependencies:** S-001 (KnownElements), S-002 (helpers), S-004 (match selection patterns are reused — dialog interaction, spinner wait, row selection).

**Verification intent:** Stub no longer throws `NotImplementedException`. Service-layer mock tests remain green.

---

## Tier 2 — Should-Ship ✅ COMPLETE

### S-008 — Login Timeout Configurable ✅

**Status:** DELIVERED — commit `0edb725`

**What changes:** The login screen timeout moves from a compile-time constant in the Core layer to a configurable value with a default of 30 seconds. Existing tests that depend on the old value are updated.

**Why:** HLPS-010 §2.6. The diagnostic tool demonstrated that 20 seconds is insufficient for slow PCS Pro startups.

**Dependencies:** None — this is an independent Core-layer change. Can be delivered in any order relative to S-003 through S-007, though delivering before or alongside S-003 avoids rework in the login automation implementation.

**Verification intent:** All existing timeout-related tests pass with the new default. Configuration can be overridden.

---

### S-009 — Streaming Automation ✅

**Status:** DELIVERED — commit `d73a001`

**What changes:** A new streaming automation interface is created in the Automation project with a corresponding FlaUI implementation. The existing inline stubs in the automation service are replaced with full service-level orchestration (state guard, idempotency, concurrency guard, error handling, logging). 101 automation tests pass.

**Why:** HLPS-010 §2.7. Unblocks IS-009 S-003 by resolving SA-U-2 and SA-U-3. Addresses H-SC-2 and H-SC-5.

**Dependencies:** S-001 (KnownElements), S-002 (helpers — especially button-by-child-text).

**Verification intent:** Zero `NotImplementedException` remains in the Automation project when combined with completed S-003–S-007 (H-SC-2 fully satisfied). New unit tests cover the delegation pattern and mock behaviour. Manual streaming cycle verified on garage PC (H-SC-5).

---

## Tier 3 — Can-Defer

### S-010 — Team Names Structured Return Type

**What changes:** The team names automation interface return type changes from a bare string to a structured type containing both club name and team name. This is a Core interface change that propagates to all implementations and consumers.

**Why:** HLPS-010 §2.10 (structured return type). Enables proper YouTube stream title formatting with both club and team identifiers.

**Dependencies:** S-005 (team names automation must exist before changing its return type).

**Verification intent:** All consumers of the old `string` return type are updated. Existing tests are updated or new tests are added for the structured type. Build and all tests pass.

---

### S-011 — Use Current Match (GAP-010)

**What changes:** The state machine gains a new trigger that permits transitioning directly to the match-loaded state when PCS Pro already has a match open. The automation service gains a new entry-point method that detects whether a match is loaded, reads team names, and fires the new trigger. Match identity verification is deferred — the operator's assertion is trusted.

**Why:** HLPS-010 §2.8 (GAP-010). Enables reconnection to a manually-loaded match without re-running the full match selection flow.

**Dependencies:** S-001 (KnownElements for detection signals), S-002 (helpers for ToolWindow detection and status bar reads), S-005 (team names automation for reading loaded match teams). S-010 (structured team name type) is recommended but not strictly required — the attach flow works with either return type.

**Verification intent:** New state machine path has unit tests. New service method has unit tests with mocks. Manual verification on garage PC (H-SC-7).

---

### S-012 — Health-Check Poll (GAP-011)

**What changes:** A periodic polling mechanism is added inside the automation service that reads PCS Pro state signals and emits state-change events to the web UI. The poll interval is configurable with bounded defaults (per HLPS §2.9). The poll uses try-acquire on the operation lock — if held, that cycle is skipped. The poll starts at match-loaded state and stops on change-match or shutdown.

**Why:** HLPS-010 §2.9 (GAP-011). Provides continuous monitoring of PCS Pro health without operator intervention.

**Dependencies:** S-001 (KnownElements for status bar identifiers), S-009 (streaming must be implemented for stream-status monitoring). Delivery ordering within Tier 1 is guaranteed by tier — S-003 through S-007 will always complete before S-012 begins.

**Verification intent:** Unit tests verify poll-skip behaviour and state-change emission. Manual test confirms detection within one poll interval (H-SC-8).

---

## Success Criteria Coverage

| H-SC | Covered By |
|------|-----------|
| H-SC-1 (zero TODO_REPLACE / "TODO") | S-001, S-003–S-007 |
| H-SC-2 (zero NotImplementedException) | S-003–S-007 (FlaUi*), S-009 (streaming) |
| H-SC-3 (end-to-end flow) | S-003 + S-004 + S-005 + S-006 + S-007 combined |
| H-SC-4 (scoreboard at 150% DPI) | S-001 (DPI) + S-006 (capture) |
| H-SC-5 (streaming cycle) | S-009 |
| H-SC-6 (zero warnings, tests) | Every step |
| H-SC-7 (Use Current Match) | S-011 |
| H-SC-8 (health-check poll) | S-012 |
| H-SC-9 (KnownElements verification) | S-001 |

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Opus / GPT-5.4 / Sonnet | NEEDS REVISION → 8 fixes applied (v0.2) |
| R2 | 2026-04-21 | Opus / GPT-5.4 / Sonnet | APPROVED (unanimous, 9/9 PASS, 0 new issues) |
