# SPEC-S-002: Options Model and Service Skeleton

| Field | Value |
|---|---|
| **Document** | SPEC-S-002-Options-Model-Skeleton.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-10 |
| **Step** | S-002 — Options Model and Service Skeleton |
| **IS** | IS-002-Mock-Service.md (APPROVED v0.2) |
| **HLPS** | HLPS-002-Mock-Service.md (APPROVED v0.3) |
| **Branch** | `feature/hlps002-S002-options-skeleton` |

---

## 1. Overview

This step introduces two new types in `PcsRemote.Automation.Mock`:

1. **`MockPcsProOptions`** — a plain options class capturing all configurable behaviour surfaces. All values must have sensible defaults; all delay properties must be zero-configurable (the property type must accept zero without error or special handling).
2. **`MockPcsProAutomationService`** — a class implementing `IPcsProAutomationService` with stub method bodies. Its constructor accepts `IOptions<MockPcsProOptions>` and `ILogger<MockPcsProAutomationService>`. No logic is introduced in this step; all interface methods throw `NotImplementedException`.

After this step the project must compile and the test project must pass (zero tests).

---

## 2. Scope

### In Scope

- `MockPcsProOptions` class in `PcsRemote.Automation.Mock`
- `MockPcsProAutomationService` class in `PcsRemote.Automation.Mock`, implementing `IPcsProAutomationService`
- `CurrentState` property backed by a private field initialised to `PcsProState.NotRunning`
- `StateChanged` event declaration (raised in subsequent steps)
- Stub `NotImplementedException` bodies for all interface methods
- Constructor accepting `IOptions<MockPcsProOptions>` and `ILogger<MockPcsProAutomationService>`

### Out of Scope

- Any state machine logic or delay simulation (S-003)
- Error injection (S-004)
- Match data generation (S-005)
- Image generation (S-006)
- DI registration or Serilog wiring (S-007)
- Tests beyond zero-test build pass (S-003 onwards)

---

## 3. Requirements

### R-1: MockPcsProOptions shape

`MockPcsProOptions` must expose the following properties, all settable (to support configuration binding):

**Per-transition delays** — one `TimeSpan` per state transition in the happy-path lifecycle. Default value for each is `TimeSpan.Zero` (zero-configurable per M-SC-8):

| Property | Transition it simulates |
|---|---|
| `LaunchDelay` | Before `NotRunning → Launching` |
| `LoginDetectedDelay` | Before `Launching → LoginScreen` |
| `CredentialsEnteredDelay` | Before `LoginScreen → MatchSelection` |
| `SearchTriggeredDelay` | Before `MatchSelection → MatchSelectionSearching` |
| `SpinnerGoneDelay` | Before `MatchSelectionSearching → MatchSelectionReady` |
| `MatchOpenedDelay` | Before `MatchSelectionReady → MatchLoaded` |
| `ChangeMatchDelay` | Before `MatchLoaded → MatchSelection` |
| `StopDelay` | Before mock resets internal state to `NotRunning` (see R-2 StopAsync note) |

> **Note on `GetTeamNamesAsync`, `RefreshScoreboardAsync`, and `CaptureScoreboardImageAsync`:** These methods produce no state transitions. Per HLPS-002 §2, configurable delays simulate dwell time before state-machine triggers. Since no trigger is involved, these methods are intentionally zero-latency in the mock — no delay options are required for them.

**Behavioural knobs:**

| Property | Type | Default | Purpose |
|---|---|---|---|
| `ErrorProbability` | `double` | `0.0` | Probability (0.0–1.0) that any given transition produces an Error state instead. Used by S-004. |
| `RngSeed` | `int?` | `null` | If set, RNG is seeded for deterministic error injection. `null` = system random. Used by S-004. |
| `ImageVariationProbability` | `double` | `0.2` | Probability (0.0–1.0) that consecutive scoreboard images differ. Used by S-006. |
| `FakeMatchCount` | `int` | `3` | Number of fake matches returned by `GetTodaysMatchesAsync`. Used by S-005. |

### R-2: MockPcsProAutomationService skeleton

- Must implement `IPcsProAutomationService` from `PcsRemote.Core`
- Constructor must accept `IOptions<MockPcsProOptions>` and `ILogger<MockPcsProAutomationService>` — both required (no overloads). Constructor must guard against null arguments.
- `CurrentState` must be a get-only property (single `get` accessor) reading from a `private PcsProState _currentState` field initialised to `PcsProState.NotRunning`
- `StateChanged` must be a public event of type `EventHandler<PcsProState>`
- All interface methods must have stub bodies (`throw new NotImplementedException()`)
- The options and logger instances must be stored as private fields for use in later steps
- **StopAsync note (forward reference for S-003+):** The state machine's `Stop` trigger is only valid from `MatchLoaded`. The mock's `StopAsync` is required to succeed from any state (M-SC-12). When implemented in S-003, `StopAsync` must therefore directly reset `_currentState` to `NotRunning` and fire `StateChanged(NotRunning)` after applying `StopDelay`, bypassing the Stateless machine's trigger validation. This is an intentional deviation from trigger-based transitions, documented here so S-003 does not attempt to fire `Stop` from all states. In this step the method body remains `throw new NotImplementedException()`.

### R-3: No hardcoded magic numbers

No numeric literal representing a delay or probability may appear outside `MockPcsProOptions`. All values flow from options.

### R-4: Package dependencies

`PcsRemote.Automation.Mock.csproj` must reference `Microsoft.Extensions.Options` and `Microsoft.Extensions.Logging.Abstractions`. Both must be added to `Directory.Packages.props` (Central Package Management). Use the latest stable versions compatible with `net8.0`.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` from solution root exits 0 with zero errors and zero warnings |
| AC-2 | `dotnet test` from solution root exits 0 — all pre-existing tests pass; no new tests introduced in this step |
| AC-3 | `MockPcsProOptions` is in namespace `PcsRemote.Automation.Mock` and declares all 8 delay properties and all 4 behavioural knob properties with names and defaults as specified in R-1 |
| AC-4 | `MockPcsProAutomationService` is in namespace `PcsRemote.Automation.Mock`, implements `IPcsProAutomationService`, and has a constructor accepting `IOptions<MockPcsProOptions>` and `ILogger<MockPcsProAutomationService>` with null guards |
| AC-5 | `MockPcsProAutomationService._currentState` field is initialised to `PcsProState.NotRunning`; `CurrentState` property has only a `get` accessor reading that field |
| AC-6 | All interface method bodies in `MockPcsProAutomationService` consist solely of `throw new NotImplementedException()` |
| AC-7 | No numeric literal representing a delay, probability, or count appears in `MockPcsProAutomationService` |
| AC-8 | `Directory.Packages.props` includes `Microsoft.Extensions.Options` and `Microsoft.Extensions.Logging.Abstractions`; `PcsRemote.Automation.Mock.csproj` references both |

---

## 5. Verification Table

| AC | Verification Method |
|---|---|
| AC-1 | `dotnet build` — exit 0, zero warnings |
| AC-2 | `dotnet test` — exit 0; no new tests in this step |
| AC-3 | Inspect `MockPcsProOptions.cs` — all 8 delay + 4 knob properties present, correct types and defaults |
| AC-4 | Inspect `MockPcsProAutomationService.cs` — class declaration, constructor signature, null guards |
| AC-5 | Code review: `_currentState` field present, initialised to `PcsProState.NotRunning`; `CurrentState` property is get-only (single accessor, no setter) |
| AC-6 | Code review: every interface method body is `throw new NotImplementedException()` and nothing else |
| AC-7 | Code review: no bare numeric literals in `MockPcsProAutomationService.cs` |
| AC-8 | Inspect `Directory.Packages.props` and `PcsRemote.Automation.Mock.csproj` — both packages present |

---

## 6. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-10 | Sonnet 4.6, GPT-4.1 | NEEDS REVIEW — 3 HIGH, 2 MEDIUM, 2 LOW (Sonnet); 2 HIGH, 2 MEDIUM, 2 LOW (GPT) |
| R2 | 2026-04-10 | Sonnet 4.6 | APPROVED — 0 blocking; 1 LOW (StopAsync note wording) applied as v0.3 polish |
