# SPEC-S-002 — Switch-User Login: Orchestration

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **IS Step** | S-002 |
| **HLPS**    | HLPS-013 (APPROVED) |
| **Author**  | Copilot |
| **Created** | 2026-04-21 |

---

## 1. Objective

Integrate username detection into the login orchestration flow. Before entering credentials, the automation reads the pre-populated username, compares it against the configured expected username, and invokes switch-user on mismatch — bounded to a single retry.

---

## 2. Branch

`feature/S-002-switch-user-orchestration`

---

## 3. Requirements

### R-1: Username Check Before Credential Submission

Modify the credential submission logic in the automation service so that, when the login dialog is visible and credentials have not yet been submitted:

1. Read the pre-populated username from the login dialog.
2. If `ExpectedUsername` is not configured (empty), skip the check — proceed to password entry as today (SC-2).
3. If the username field is blank or null, skip the check — proceed to password entry as today (SC-2).
4. If the username matches the expected value (case-insensitive), proceed to password entry as today (SC-2).
5. If the username does not match, invoke the switch-user flow (R-2).

### R-2: Switch-User Flow

When a username mismatch is detected:

1. Log a warning indicating a mismatch was detected. Do not log the actual username or expected username values — log only the fact that a mismatch occurred (C-6).
2. Click the "Switch User" element via the login automation contract.
3. The login dialog will reappear with both username and password fields editable (per A-2).
4. Wait for the login dialog to reappear (it may briefly disappear during the transition).
5. Enter the expected username into the username field.
6. Enter the password and click submit.
7. The normal post-submit polling loop then waits for match selection to appear (same as the existing flow).

The switch-user flow does **not** re-check the username after step 6. The retry bound (R-3) applies when a fresh login-phase entry (e.g. after an error/retry cycle that re-enters the login screen) encounters a mismatch again.

### R-3: Bounded Retry

The switch-user attempt count is tracked per login-phase entry and bounded to 1 (C-8):

- On the first mismatch within a login phase, the switch-user flow (R-2) executes.
- If the same login-phase entry encounters a second mismatch (e.g. the switch-user click failed silently and the dialog still shows the wrong user), the automation must log an error and fire an error trigger to surface the failure to the UI.
- The counter resets each time the state machine re-enters the login phase (e.g. after a retry from the error state). This is consistent with how other login-phase state (e.g. `submitted` flag) resets per entry.
- The automation must not enter an infinite loop.

### R-4: Credential Logging Prohibition

All logging in the switch-user flow must comply with C-6:
- Log the comparison outcome (match, mismatch, blank, unconfigured) at appropriate levels.
- Never log the actual username value, expected username value, or password value.

---

## 4. Test Cases

All tests in `PcsRemote.Automation.Tests` against `PcsProAutomationServiceTests`.

### Happy Path (No Change)

| Test | Scenario | Expected |
|------|----------|----------|
| TC-1 | ExpectedUsername is empty (unconfigured) | Password entered directly, no username check, no switch-user call |
| TC-2 | Username field returns null (field not found) | Password entered directly, no switch-user call |
| TC-3 | Username field is blank (empty string) | Password entered directly, no switch-user call |
| TC-4 | Username matches expected (case-insensitive) | Password entered directly, no switch-user call |

### Mismatch Path

| Test | Scenario | Expected |
|------|----------|----------|
| TC-5 | Username mismatches expected, switch-user not yet attempted | Switch-user clicked, expected username entered, password entered, submit clicked |
| TC-6 | Username mismatches, switch-user completes, credentials submitted | Match selection appears after switch-user + credential entry → login succeeds |

### Failure Path

| Test | Scenario | Expected |
|------|----------|----------|
| TC-7 | Username mismatches and switch-user was already attempted in this login phase | Error fired immediately, no second switch-user attempt (C-8 bound) |
| TC-8 | Switch-user click throws (element not found) | Error fired, login phase aborts |

### Regression

| Test | Scenario | Expected |
|------|----------|----------|
| TC-9 | All existing login tests | Continue to pass (C-1) |

---

## 5. Acceptance Criteria

- [ ] When `ExpectedUsername` is configured and the login dialog username mismatches, switch-user is invoked and correct credentials are re-entered (SC-1).
- [ ] When `ExpectedUsername` is empty, blank username, null username, or matching username, existing login flow is unchanged (SC-2, C-1).
- [ ] Mismatch persisting after 1 retry results in an error surfaced to the UI, not an infinite loop (SC-3, C-8).
- [ ] Switch-user interaction failure (element not found) results in an error surfaced to the UI (SC-3).
- [ ] No credential values appear in log output at any level (C-6).
- [ ] All new and existing tests pass.
- [ ] Build: 0 errors, 0 warnings.

---

## 6. Review History

### R1 — 2026-04-21

**Panel:** Opus 4.7, GPT 5.4
**Verdict:** Split — Opus APPROVED (with advisory), GPT REQUEST CHANGES (2 findings)

| Finding | Source | Severity | Disposition | Action |
|---------|--------|----------|-------------|--------|
| Retry sequencing unclear — when does re-check occur after switch-user? | GPT F-1 | Major | Accept | Fixed: R-2 clarified no re-check after submit; R-3 explains per-login-phase counter and reset. TC-6/TC-7 aligned to same model. |
| AC misses switch-user interaction failure path (TC-8) | GPT F-2 | Medium | Accept | Fixed: AC line added for interaction failure surfacing to UI. |

### R2 — 2026-04-21

**Panel:** GPT 5.4
**Verdict:** REQUEST CHANGES (1 finding)

| Finding | Source | Severity | Disposition | Action |
|---------|--------|----------|-------------|--------|
| TC-6/TC-7 still describe "re-check" inconsistent with R-2/R-3 clarification | GPT F-1 (R2) | Major | Accept | Fixed: TC-6 now describes switch-user + submit → success. TC-7 now describes switchUserAttempted flag preventing second attempt. No "re-check" language. |

### R3 — 2026-04-21

**Panel:** GPT 5.4
**Verdict:** APPROVED

R2 finding fully addressed. TC-6/TC-7 consistent with R-2/R-3. No regressions.

---

*End of SPEC-S-002.*
