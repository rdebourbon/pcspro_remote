# HLPS-002: Mock Automation Service

| Field | Value |
|---|---|
| **Document** | HLPS-002-Mock-Service.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | HLPS-001 (interface + domain types must exist) |

---

## 1. Problem Statement

PCS Pro (cricket.exe) only runs on the dedicated garage PC. Developers working on the web UI, SignalR integration, state machine behaviour, and end-to-end flows cannot install or run PCS Pro on their development machines.

Without a mock implementation of `IPcsProAutomationService`, all downstream development is blocked until the FlaUI real implementation (HLPS-006) is complete — making iterative, test-driven delivery impossible.

This HLPS delivers a fully functional mock that simulates PCS Pro's entire lifecycle with realistic timing, data, and state transitions, enabling the entire web application to be built, tested, and refined without PCS Pro.

---

## 2. Scope

### In Scope

- **`MockPcsProAutomationService`** implementing `IPcsProAutomationService` in `PcsRemote.Automation.Mock`
- **Realistic state transition simulation**: Short configurable delays (e.g., 2s login, 3s match search) to mimic real PCS Pro timing
- **Fake match data**: Hardcoded list of 1–2 today's matches with realistic team names, match types, and times (per PRD §5a)
- **Placeholder scoreboard images**: Generated JPEG images (solid colour with overlay text) that change occasionally to exercise delta detection downstream
- **StateChanged event firing**: Mock fires events on every transition, exactly as the real implementation would
- **DI registration**: Toggle between mock and real via `PcsPro:UseMock` config flag (per PRD §5a)
- **Configurable behaviour**: Simulated delays, error injection (configurable probability of transitioning to Error state), number of fake matches
- **Unit tests**: Verify state transitions, event firing, delay behaviour, error injection — in `PcsRemote.Automation.Mock.Tests`
- **Serilog integration**: Mock logs all simulated actions at appropriate levels

### Out of Scope

- Real FlaUI automation (HLPS-006)
- Web UI or SignalR (HLPS-003+)
- Scoreboard image delta detection logic (HLPS-004 — the mock just produces images)
- PrintWindow Win32 capture (HLPS-006)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| M-SC-1 | Mock implements all `IPcsProAutomationService` methods with simulated behaviour | Code review + unit tests |
| M-SC-2 | State transitions fire `StateChanged` events in correct order | Unit tests verify event sequence |
| M-SC-3 | `GetTodaysMatchesAsync` returns realistic match data for today's date | Unit test |
| M-SC-4 | `CaptureScoreboardImageAsync` returns valid JPEG bytes that occasionally change | Unit test — verify JPEG header + variation over multiple calls |
| M-SC-5 | DI toggle works: `PcsPro:UseMock=true` → mock, `false` → real (compile check only for real) | Integration test with DI container |
| M-SC-6 | Error injection: configurable probability triggers Error state with descriptive reason | Unit test with error config |
| M-SC-7 | All simulated actions logged via Serilog | Log output inspection |
| M-SC-8 | Simulated delays are configurable (not hardcoded magic numbers) | Code review — delays from config or constants |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| M-U-1 | Should the mock support simulating "PCS Pro not running" (NotRunning state) or always auto-launch? | Agent | No — default to supporting full lifecycle from NotRunning; configurable auto-start option is a nice-to-have |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | Not yet reviewed |
