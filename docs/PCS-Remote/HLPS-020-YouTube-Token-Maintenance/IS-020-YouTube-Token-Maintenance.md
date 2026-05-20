# IS-020: YouTube Token Maintenance — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-020-YouTube-Token-Maintenance.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-05-20 |
| **Governing HLPS** | HLPS-020-YouTube-Token-Maintenance.md (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` |
| **Prerequisites** | HLPS-018 (Production Resilience) — delivered. YouTube OAuth flow and availability state machine in place. |

---

## Overview

This sequence fixes two operational defects in the YouTube integration in four atomic steps.

**P1 (token expiry)** is addressed in steps S-002 through S-004: the interface is extended first to preserve mock/real substitutability, the core proactive refresh and staleness tracking logic is added to the service, and a background scheduled trigger plus tray advisory balloon complete the feature.

**P2 (misleading tray balloon)** is a standalone fix with no dependencies — it is delivered first as S-001 for an immediate operator experience improvement.

Steps use stable IDs S-001 through S-004. IDs are never renumbered; deferred steps leave gaps.

---

## Accepted Risks from Approved HLPS-020

| Risk Summary | Citation |
|---|---|
| Proactive refresh does not guarantee token rotation; Google's rotation behaviour in testing mode is non-deterministic | HLPS-020 A1 |
| The staleness warning is advisory only; the operator may ignore it and still face token expiry on match day | HLPS-020 C6, SC3 |
| Publishing the OAuth consent screen to production is the definitive fix; this IS is a best-effort code-level workaround | HLPS-020 C7 |
| If the app is restarted only on match day after extended idle, background refresh has not run; staleness check fires at init as the safety net | HLPS-020 A5, SC3 |

---

## Steps

### S-001 — P2 fix: tray pre-check and corrected balloon message

**What:** The tray context checks the live stream status before invoking YouTube Setup. For active-stream states (live, starting, or stopping), the setup call is suppressed and an accurate balloon is shown describing why Setup is unavailable. For idle and error states, the call proceeds as before. The internal state guard in the YouTube service implementation is relaxed to also permit re-authorisation from an error state (the stream has already failed; re-auth may resolve the cause). This is an implementation change only — the service interface is unchanged. The existing credentials-missing balloon is retained for its original code path.

**Why:** SC4, SC5. Operators currently see a misleading configuration error message when attempting YouTube Setup during an active stream. The false message causes unnecessary alarm and diagnostic effort mid-match.

**Dependencies:** None.

**Verification:** The accurate "stream active" balloon appears when Setup is attempted during live/starting/stopping states; setup proceeds normally from idle and error states; the credentials-missing balloon is unchanged for its original trigger. All existing tests pass.

---

### S-002 — Interface extension and mock update

**What:** A token-expiry-approaching event added to the YouTube live stream service interface. The mock implementation adds the new event member; the mock never fires it by default and continues to report as ready. No other behaviour changes.

**Why:** SC6, C1. The interface must be extended before any consuming code (tray host, background service) can wire up event handlers across the DI boundary. The mock must implement the new member to preserve its substitutability for development and testing.

**Dependencies:** None.

**Verification:** Solution builds with zero warnings. The mock satisfies the updated interface. All existing tests pass.

---

### S-003 — Proactive refresh logic and staleness tracking

**What:** The YouTube live stream service extended with three related capabilities:

1. **Proactive refresh:** When called and the service is in the ready state, requests a Google credential refresh. Detects whether the response included a rotated refresh token by comparing the pre- and post-refresh token values. On confirmed rotation, updates the persisted staleness marker. On failure, delegates to the existing auth-failed handling path — token is deleted, availability transitions, and the auth-status-changed event fires. No new failure path is introduced (C3).

2. **Staleness check:** Reads the persisted marker — which is stored in a separate persistence location, outside the Google credential data store (SC3) — and compares its age against the configurable staleness threshold. If the threshold is exceeded, fires the token-expiry-approaching event. The check runs at both service initialisation and after each proactive refresh call. The init-time check runs whenever the separate staleness marker store contains a valid marker, regardless of the service's current availability state — this preserves the startup safety-net for the scenario where the app is restarted after an extended idle period (A5, SC3). The ongoing check (run after each proactive refresh) is suppressed when the service is not in the ready state, consistent with C2, which restricts proactive refresh to the ready/authenticated state only.

3. **Re-consent marker update:** The existing setup flow (RunOAuthSetupAsync) writes the staleness marker on successful completion, marking the point of confirmed re-consent.

Configuration is extended with proactive refresh interval (default: 6 hours) and staleness threshold (default: 5 days), both configurable (C4).

**Why:** SC1, SC2, SC3. Core P1 logic. Implementing and testing the service-layer behaviour independently of the background scheduler eliminates timing dependencies from unit tests and makes each concern independently verifiable.

**Dependencies:** S-002 (interface must carry the token-expiry-approaching event before the implementation can fire it).

**Verification:** Unit tests cover: proactive refresh when ready updates staleness marker on rotation; proactive refresh when ready does not update marker when no rotation detected; proactive refresh failure delegates to auth-failed path (no new path); staleness check fires event when marker age exceeds threshold; staleness check fires at initialisation regardless of availability state when marker is present; ongoing staleness check suppressed when not ready; RunOAuthSetupAsync writes marker on successful re-consent. All existing tests pass.

---

### S-004 — Background refresh scheduler and tray advisory balloon

**What:** A background hosted service added to the tray host that calls the service's proactive refresh on the configured interval while the service is in the ready state. The tray host subscribes to the token-expiry-approaching event and shows an advisory balloon prompting the operator to run YouTube Setup before the token expires. The balloon is advisory only — no availability state transition occurs and streaming operations remain enabled (C6).

**Why:** SC1 (scheduling), SC3 (tray surface). Completes the P1 feature: the background scheduler keeps the token alive between match days, and the tray balloon gives the operator actionable advance warning before token failure disrupts a match.

**Dependencies:** S-002, S-003.

**Verification:** The hosted service calls proactive refresh at the configured interval while ready; the tray balloon fires on token-expiry-approaching; the balloon is advisory only (no state change, streaming unaffected); the service does not call refresh when not ready. All existing tests pass.

---

## Step Dependency Summary

```
S-001 (P2 fix — tray pre-check) — independent
S-002 (interface extension + mock) — independent
  └── S-003 (proactive refresh logic + staleness tracking)
        └── S-004 (background scheduler + tray balloon)
```

| Step | Depends on |
|---|---|
| S-001 | — |
| S-002 | — |
| S-003 | S-002 |
| S-004 | S-002, S-003 |

---

## SC Coverage Matrix

| SC | Addressed by |
|---|---|
| SC1 | S-003, S-004 |
| SC2 | S-003 |
| SC3 | S-003, S-004 |
| SC4 | S-001 |
| SC5 | S-001 |
| SC6 | S-002 |

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | Tier 3 | Claude Opus 4.5, GPT-5.4 | HIGH: 1 / MEDIUM: 1 / LOW: 1 | Accept: 2, Defer: 1 | REVISION | Opus APPROVED with F-1 MEDIUM (staleness marker persistence location unspecified — separate store vs. Google data store ambiguous) and F-2 LOW (scheduler edge transition clarity — deferred). GPT REVISION REQUIRED on F-1 HIGH (init-time staleness check incorrectly gated on ready state — C2 restricts proactive refresh only; SC3/A5 require init check to run during startup regardless of availability). R1 fixes applied to S-003: (1) staleness marker now explicitly described as separate from Google credential store; (2) staleness check gating split — init-time check runs whenever marker is present (availability-agnostic), ongoing check suppressed when not ready. |
| R2 | Tier 3 | GPT-5.4 | LOW: 1 | Accept: 1 | REVISION | R1 HIGH fix verified adequate. R1 MEDIUM mostly addressed but one residual wording inconsistency — "credential store contains a valid staleness marker" contradicted the separate-store claim. Tier 0 self-cert applied: replaced contradictory phrase with "separate staleness marker store contains a valid marker". Verification line updated for consistency (init-time check explicitly availability-agnostic; ongoing check suppressed when not ready). No regressions identified. |
| R2-SC | Tier 0 | Self-Cert | — | — | APPROVED | Trivial one-sentence wording alignment; no logic or scope change. Constitutes R2 final close. |
