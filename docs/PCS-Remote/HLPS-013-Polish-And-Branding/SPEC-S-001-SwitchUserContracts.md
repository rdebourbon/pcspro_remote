# SPEC-S-001 — Switch-User Login: Contracts & Configuration

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **IS Step** | S-001 |
| **HLPS**    | HLPS-013 (APPROVED) |
| **Author**  | Copilot |
| **Created** | 2026-04-21 |

---

## 1. Objective

Add the configuration and automation contracts needed for switch-user login detection. This step provides the foundation that S-002 will use to orchestrate the switch-user flow.

---

## 2. Branch

`feature/S-001-switch-user-contracts`

---

## 3. Requirements

### R-1: Expected Username Configuration

Add a configuration property to `PcsProOptions` for the expected PCS Pro username. The property should:
- Bind to the `PcsPro:ExpectedUsername` configuration key.
- Default to empty string (unconfigured = skip username check, per SC-2).
- Follow the same pattern as existing properties (e.g., `Password`, `SiteName`).
- Include a doc comment noting that, like `Password`, this should not be committed — use environment variables or User Secrets.

### R-2: Login Automation Contract Extension

Extend `ILoginAutomation` with two new capabilities:

1. **Read username:** A method that reads and returns the current text content of the username field in the login dialog. Must follow the existing contract conventions:
   - Non-throwing detection pattern (returns a safe fallback on failure).
   - Returns `null` when the username field cannot be located.

2. **Click switch-user:** A method that clicks the "Switch User" hyperlink element. Must follow the existing interaction conventions:
   - Throws when the element cannot be located (same pattern as `EnterPassword`/`ClickSubmit`).
   - Logs the action at Debug level.

### R-3: FlaUI Implementation

Implement the two new contract members in `FlaUiLoginAutomation`:

1. **Read username:** Locate the username field via its AutomationId (already defined in `KnownElements` as `LoginUsernameFieldAutomationId`). Read the text value from the element. Return `null` if the element or main window cannot be found. Catch all exceptions and return `null` (matching the non-throwing detection pattern of `IsLoginDialogVisible`).

2. **Click switch-user:** Locate the "Switch User" hyperlink element by its Name (already defined in `KnownElements` as `LoginSwitchUserText`). The element is a hyperlink-class element inside the login dialog. Click/invoke it. Throw `InvalidOperationException` if the element cannot be found (matching the interaction pattern of `EnterPassword`/`ClickSubmit`).

### R-4: Test Double Extension

Extend `FakeLoginAutomation` with support for the two new contract members:

1. **Read username:** A configurable property controlling the return value. Default to `null` (simulates field not found / not yet visible).
2. **Click switch-user:** A property tracking whether the method was called, and an optional `ThrowOnInteraction` guard (same pattern as existing methods).

### R-5: Enter Username Capability

The switch-user flow requires re-entering both username and password after clicking "Switch User" (per A-2 — both fields become editable). Add:

1. **Contract member:** A method on `ILoginAutomation` that enters a username into the login dialog username field. Follows the same interaction conventions as `EnterPassword` — throws on element not found.
2. **FlaUI implementation:** Locate via `LoginUsernameFieldAutomationId`, focus, select-all, type. Same keyboard simulation pattern as `EnterPassword`.
3. **Test double:** Capture the entered username for assertion. Respect `ThrowOnInteraction`.

---

## 4. Test Cases

All tests go in `PcsRemote.Automation.Tests`.

### Configuration Tests

| Test | Scenario | Expected |
|------|----------|----------|
| TC-1 | `PcsPro:ExpectedUsername` is set in config | Options property returns the configured value |
| TC-2 | `PcsPro:ExpectedUsername` is absent from config | Options property returns empty string |

### FakeLoginAutomation Tests

| Test | Scenario | Expected |
|------|----------|----------|
| TC-3 | ReadUsername with default config | Returns `null` |
| TC-4 | ReadUsername with configured value | Returns configured string |
| TC-5 | ClickSwitchUser records the call | Tracking property is `true` |
| TC-6 | ClickSwitchUser with ThrowOnInteraction | Throws `InvalidOperationException` |
| TC-7 | EnterUsername records the value | Captured username matches input |
| TC-8 | EnterUsername with ThrowOnInteraction | Throws `InvalidOperationException` |

### Regression

| Test | Scenario | Expected |
|------|----------|----------|
| TC-9 | All existing login automation tests | Continue to pass (C-1) |

---

## 5. Acceptance Criteria

- [ ] `PcsPro:ExpectedUsername` configuration key binds correctly; defaults to empty when absent.
- [ ] `ILoginAutomation` has three new members (read username, click switch-user, enter username).
- [ ] `FlaUiLoginAutomation` implements all three with appropriate exception handling patterns.
- [ ] `FakeLoginAutomation` supports all three with configurable behavior and call tracking.
- [ ] `ReadUsername` and `EnterUsername` implementations do not emit credential values in log output — consistent with `EnterPassword` (C-6).
- [ ] Element identifiers for username field and switch-user link are already in `KnownElements` — no changes needed.
- [ ] All new and existing tests pass.
- [ ] Build: 0 errors, 0 warnings.

---

## 6. Review History

### R1 — 2026-04-21

**Panel:** Opus 4.7, Sonnet 4.6
**Verdict:** Split — Opus APPROVED (with advisory), Sonnet REQUEST CHANGES (1 finding)

| Finding | Source | Severity | Disposition | Action |
|---------|--------|----------|-------------|--------|
| C-6 credential logging discipline not in AC for EnterUsername/ReadUsername | Sonnet F-1, Opus F-1 (advisory) | MEDIUM | Accept | Fixed: AC line added requiring no credential values in log output for ReadUsername and EnterUsername (C-6). |

### R2 — 2026-04-21

**Panel:** Sonnet 4.6
**Verdict:** APPROVED

F-1 fully addressed. No regressions.

---

*End of SPEC-S-001.*
