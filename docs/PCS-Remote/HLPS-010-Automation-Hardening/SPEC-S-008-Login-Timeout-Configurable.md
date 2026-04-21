# SPEC-S-008: Login Timeout Configurable

| Field | Value |
|---|---|
| **Document** | SPEC-S-008-Login-Timeout-Configurable.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-22 |
| **Step ID** | S-008 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-008-login-timeout` |

---

## 1. Purpose

Move `LoginScreenTimeoutSeconds` from a compile-time constant in `PcsProStateMachine` (Core) to a runtime-configurable property in `PcsProOptions` (Automation). The default changes from 20 seconds to 30 seconds, addressing diagnostic findings that 20 seconds is insufficient for slow PCS Pro startups.

---

## 2. Scope

### 2.1 Remove Constant from `PcsProStateMachine`

- R1: Remove the `public const int LoginScreenTimeoutSeconds = 20` field from `PcsProStateMachine`.
- R2: No other code in `PcsRemote.Core` references this constant — removal is clean. The state machine remains a passive engine with no timeout awareness.

### 2.2 Add Property to `PcsProOptions`

- R3: Add a `LoginScreenTimeoutSeconds` property to `PcsProOptions` with a default value of `30`.
- R4: The property type is `int`. No validation is added at this level — `PcsProOptions` is a simple POCO bound from configuration.

### 2.3 Update `PcsProAutomationService`

- R5: `CheckLoginTimeoutAsync` currently reads `PcsProStateMachine.LoginScreenTimeoutSeconds`. Update it to read from the injected `PcsProOptions` instance instead.
- R6: Both error message branches in `CheckLoginTimeoutAsync` must reference the configured timeout value rather than the removed constant. This includes the `submitted == true` branch (post-credentials timeout) and the `submitted == false` branch (login screen appearance timeout).

### 2.4 Update Tests

- R7: `PcsProStateMachineTests.TimeoutConstants_HaveCorrectValues` — remove the `LoginScreenTimeoutSeconds` assertion (the constant no longer exists on the state machine). The remaining timeout constants (`LaunchingTimeoutSeconds`, `MatchSelectionSearchingTimeoutSeconds`, `MatchSelectionReadyTimeoutSeconds`, `GracefulCloseTimeoutSeconds`) are unchanged.
- R8: `PcsProAutomationServiceTests.LaunchAndLogin_LoginTimeout_TransitionsToError` — this test uses `PcsProStateMachine.LoginScreenTimeoutSeconds`. Update it to use the default value from `PcsProOptions` (30 seconds) or read from the options instance used by the test service.
- R9: Add a new test verifying that a non-default `LoginScreenTimeoutSeconds` value in `PcsProOptions` is respected by the timeout check.

### 2.5 Out of Scope

- Validation or clamping of the timeout value (e.g., min/max bounds) — not required by HLPS-010.
- Moving other timeout constants from `PcsProStateMachine` to configuration — those remain compile-time constants.
- Changes to the web UI or configuration UI — timeout is set via `appsettings.json` / environment variables.

---

## 3. Test Strategy

- **T1: Build verification** — 0 warnings, 0 errors.
- **T2: Existing tests remain green** — after updating the two affected test methods.
- **T3: New test** — verify custom timeout value is respected by the login timeout check.

---

## 4. Acceptance Criteria

- AC-1: `LoginScreenTimeoutSeconds` no longer exists as a field on `PcsProStateMachine`.
- AC-2: `PcsProOptions.LoginScreenTimeoutSeconds` exists with a default of `30`.
- AC-3: `CheckLoginTimeoutAsync` reads the timeout from `PcsProOptions`, not a constant.
- AC-4: Both login timeout error messages include the configured timeout value (not a hardcoded literal).
- AC-5: `TimeoutConstants_HaveCorrectValues` test passes without a `LoginScreenTimeoutSeconds` assertion.
- AC-6: `LaunchAndLogin_LoginTimeout_TransitionsToError` test passes using the new default (30s).
- AC-7: A new test confirms a custom (non-default) timeout value is respected.
- AC-8: Sample/development configuration file (`appsettings.Development.json`) includes the new `LoginScreenTimeoutSeconds` key in the `PcsPro` section.
- AC-9: Build: 0 warnings, 0 errors. All existing tests pass.

---

## 5. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | Removing a public const from Core is a breaking change for any external consumer | `PcsProStateMachine` is consumed only within the solution — no external consumers exist. Grep confirms 4 reference sites: 2 in `PcsProAutomationService`, 1 in `PcsProAutomationServiceTests`, 1 in `PcsProStateMachineTests`. All are addressed by R5–R8. |
| R-2 | Default change from 20s → 30s may affect existing test timing assumptions | The only timing-sensitive test advances a `FakeTimeProvider` — it will be updated to use the new default. No real-time waits are involved. |
