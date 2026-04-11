# SPEC-S-004: Error Injection

| Field | Value |
|---|---|
| **Document** | SPEC-S-004-Error-Injection.md |
| **Status** | APPROVED |
| **Version** | 0.5 |
| **Date** | 2026-04-11 |
| **Step** | S-004 — Error Injection |
| **IS** | IS-002-Mock-Service.md (APPROVED v0.2) |
| **HLPS** | HLPS-002-Mock-Service.md (APPROVED v0.3) |
| **Branch** | `feature/hlps002-S004-error-injection` |

---

## 1. Overview

This step extends `MockPcsProAutomationService` with configurable error injection. After each simulated delay in `LaunchAndLoginAsync` and `LoadMatchAsync`, the service evaluates `ErrorProbability` using a seedable RNG. When an error is triggered, the service transitions to `PcsProState.Error`, fires `StateChanged`, logs a warning with a descriptive reason, and returns from the method normally (not via exception). A `LastErrorReason` property on the mock class exposes the most recent error reason for test assertions.

`StopAsync` is intentionally excluded from error evaluation — stopping is always reliable in the mock.

> **Caller contract:** `LaunchAndLoginAsync` and `LoadMatchAsync` return normally (no exception) when an error is injected. Callers **must** check `CurrentState` or subscribe to `StateChanged` to detect failure; a completed `Task` does not imply success. This contract applies to both the mock and the real implementation.

---

## 2. Scope

### In Scope

- RNG initialisation at construction time: seeded `Random` if `RngSeed` is set, otherwise `Random.Shared`
- Error evaluation after every `Task.Delay` in `LaunchAndLoginAsync` and `LoadMatchAsync`
- `TransitionToError` private helper: sets state to `Error`, fires `StateChanged(Error)`, logs warning, stores reason in `LastErrorReason`
- `LastErrorReason` public property (`string?`) on `MockPcsProAutomationService`; cleared on `StopAsync` completion
- Unit tests covering HLPS M-SC-6

### Out of Scope

- `StopAsync` error injection (always succeeds per design)
- `GetTodaysMatchesAsync` and `CaptureScoreboardImageAsync` (not yet implemented)
- Serilog log assertions (S-007 — in-memory sink not yet wired)
- Any change to `MockPcsProOptions` — the `ErrorProbability` and `RngSeed` knobs established in S-002 are consumed here unchanged

---

## 3. Requirements

### R-1: RNG initialisation

A single `Random` instance is created once at construction time and stored as a private field. If `_options.RngSeed` is non-null, construct `new Random(_options.RngSeed.Value)` for deterministic behaviour. If null, use `Random.Shared`. Using a single instance ensures that a seeded RNG produces the same error sequence across multiple consecutive method calls, enabling deterministic test scenarios.

### R-2: Error evaluation

Immediately after each `Task.Delay` in `LaunchAndLoginAsync` and `LoadMatchAsync` — but before calling `Transition` — the service **must** call `_rng.NextDouble()` unconditionally and compare the result to `_options.ErrorProbability`. The draw **must** occur regardless of the current value of `ErrorProbability` (including when `ErrorProbability == 0.0`); implementors **must not** short-circuit or skip the draw. If the result is less than `ErrorProbability`, call `TransitionToError` with the canonical reason string for that delay point (see table below) and return from the method immediately. If the result is greater than or equal to `ErrorProbability`, proceed with the normal `Transition` call.

The evaluation must occur for every delay point, including the `ChangeMatchDelay` in the `LoadMatchAsync` ChangeMatch path.

No `ct.ThrowIfCancellationRequested()` is inserted between delay completion and the error check. Cancellation is only observed via `Task.Delay` itself. An error check that fires after a successful (non-cancelled) delay takes precedence over any subsequent cancellation.

**Canonical reason strings** — used verbatim in `TransitionToError` calls:

| Method | Delay property | Reason string |
|---|---|---|
| `LaunchAndLoginAsync` | `LaunchDelay` | `"Error before Launching transition"` |
| `LaunchAndLoginAsync` | `LoginDetectedDelay` | `"Error before LoginScreen transition"` |
| `LaunchAndLoginAsync` | `CredentialsEnteredDelay` | `"Error before MatchSelection transition"` |
| `LoadMatchAsync` | `ChangeMatchDelay` | `"Error before ChangeMatch MatchSelection transition"` |
| `LoadMatchAsync` | `SearchTriggeredDelay` | `"Error before MatchSelectionSearching transition"` |
| `LoadMatchAsync` | `SpinnerGoneDelay` | `"Error before MatchSelectionReady transition"` |
| `LoadMatchAsync` | `MatchOpenedDelay` | `"Error before MatchLoaded transition"` |

Error injection applies exclusively to the delays listed in this table. `StopAsync` is explicitly exempt from error injection and must always complete its reset to `NotRunning` regardless of `ErrorProbability`. Implementors must not add an error check to `StopAsync`.

When `TransitionToError` returns (error-injection return path), the method must still release the `SemaphoreSlim` in all code paths. The existing `try/finally` pattern used by `StopAsync`'s early-return guard must be applied consistently — the `TransitionToError` call and subsequent `return` occur inside the `try` block, not after it.

### R-3: TransitionToError helper

A private method that first sets `LastErrorReason` to the reason string (so it is populated before any event subscriber can read it), then delegates to `Transition(PcsProState.Error)` (reusing the existing helper to avoid duplicating `_currentState` mutation and `OnStateChanged` invocation), then calls `_logger.LogWarning` with the reason string. The ordering — `LastErrorReason` before `Transition` — is required so that `StateChanged` subscribers observe a consistent state where both `CurrentState == Error` and `LastErrorReason` are set simultaneously.

### R-4: LastErrorReason property

A public `string?` property on `MockPcsProAutomationService`. Initial value: `null`. Set by `TransitionToError`. Cleared (set back to `null`) when `StopAsync` successfully completes its reset to `NotRunning`. This allows tests to assert reason content after an error without requiring a Serilog sink.

> **Test note:** `LastErrorReason` exists on `MockPcsProAutomationService` only — not on `IPcsProAutomationService`. Test code must hold a reference of type `MockPcsProAutomationService` (not `IPcsProAutomationService`) to access this property. The `CreateSut` helper already satisfies this.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `dotnet build` from solution root exits 0 with zero errors and zero warnings |
| AC-2 | `dotnet test` from solution root exits 0; all pre-existing tests pass; new error injection tests also pass |
| AC-3 | `LaunchAndLoginAsync` with `ErrorProbability=1.0`: `CurrentState == Error`; `LastErrorReason == "Error before Launching transition"` |
| AC-4 | `LoadMatchAsync` from `MatchSelection` with `ErrorProbability=1.0` (opts-mutation setup — see §7): `CurrentState == Error`; `LastErrorReason == "Error before MatchSelectionSearching transition"` |
| AC-5 | `LoadMatchAsync` from `MatchLoaded` with `ErrorProbability=1.0` (opts-mutation setup — see §7): `CurrentState == Error` (not `MatchSelection`); `LastErrorReason == "Error before ChangeMatch MatchSelection transition"` |
| AC-6 | `LaunchAndLoginAsync` with `ErrorProbability=0.0` and any seed: completes normally to `MatchSelection`; `LastErrorReason` is null |
| AC-7 | `LoadMatchAsync` with `ErrorProbability=0.0` and any seed: completes normally to `MatchLoaded`; `LastErrorReason` is null |
| AC-8 | Determinism: two service instances constructed with the same non-null `RngSeed` and `ErrorProbability=0.5` produce identical `CurrentState` and `LastErrorReason` outcomes after the same sequence of calls; the sequence **must** use a seed where `LaunchAndLoginAsync` completes to `MatchSelection` (no error fires during launch), so that `LoadMatchAsync` is exercised as the second call and cross-method RNG state maintenance is proven; the chosen seed and expected per-call outcomes must be documented in the test |
| AC-9 | `StateChanged` fires with `PcsProState.Error` when an error is injected; inside the handler assert `LastErrorReason` is non-null (verifies R-3 ordering); after the await, assert the handler was actually invoked (using a `bool handlerFired` flag or equivalent) to eliminate vacuous pass |
| AC-10 | `StopAsync` called after a mid-`LaunchAndLoginAsync` error (entry state: `Error`): `CurrentState == NotRunning`; `LastErrorReason == null` after stop completes |
| AC-11 | opts-mutation: Separate test (own method): drive to `Error` state via EP=1.0 `LaunchAndLoginAsync` on same instance; call `StopAsync` to reset; set `opts.ErrorProbability=0.0`; call `LaunchAndLoginAsync`; assert `CurrentState == MatchSelection` without exception |
| AC-12 | `LaunchAndLoginAsync` called when `CurrentState == Error` throws `InvalidOperationException` (existing precondition guard, no regression) |
| AC-13 | `LoadMatchAsync` called when `CurrentState == Error` throws `InvalidOperationException` (existing precondition guard, no regression) |
| AC-14 | Reason overwrite: using opts-mutation setup (see §7), inject error on `LoadMatchAsync` from `MatchLoaded` (reason = `"Error before ChangeMatch MatchSelection transition"`); `StopAsync`; inject error on `LaunchAndLoginAsync` (reason = `"Error before Launching transition"`); assert `LastErrorReason == "Error before Launching transition"` (new reason overwrote previous) |

---

## 5. Verification Table

| AC | Verification Method |
|---|---|
| AC-1 | `dotnet build` — exit 0, zero warnings |
| AC-2 | `dotnet test` — exit 0 |
| AC-3 | Unit test: service with `ErrorProbability=1.0`; call `LaunchAndLoginAsync`; assert `CurrentState == Error` and `LastErrorReason == "Error before Launching transition"` |
| AC-4 | Unit test: `opts.ErrorProbability=0.0`; call `LaunchAndLoginAsync` to reach `MatchSelection`; set `opts.ErrorProbability=1.0`; call `LoadMatchAsync`; assert `CurrentState == Error` and `LastErrorReason == "Error before MatchSelectionSearching transition"` |
| AC-5 | Unit test: `opts.ErrorProbability=0.0`; `LaunchAndLoginAsync` + `LoadMatchAsync` to reach `MatchLoaded`; set `opts.ErrorProbability=1.0`; call `LoadMatchAsync`; assert `CurrentState == Error` (not `MatchSelection`) and `LastErrorReason == "Error before ChangeMatch MatchSelection transition"` |
| AC-6 | Unit test: service with `ErrorProbability=0.0`; call `LaunchAndLoginAsync`; assert `CurrentState == MatchSelection` and `LastErrorReason == null` |
| AC-7 | Unit test: service with `ErrorProbability=0.0`; drive to `MatchSelection`; call `LoadMatchAsync`; assert `CurrentState == MatchLoaded` and `LastErrorReason == null` |
| AC-8 | Unit test: two service instances with same `RngSeed` and `ErrorProbability=0.5`; seed **must** be chosen so `LaunchAndLoginAsync` completes to `MatchSelection` (no error during launch); run `LaunchAndLoginAsync` then `LoadMatchAsync` on both; assert `CurrentState` and `LastErrorReason` match at each step; document seed and expected outcomes in test |
| AC-9 | Unit test: subscribe to `StateChanged`; set `bool handlerFired = false` and assert inside handler: `state == Error`, `LastErrorReason != null`; set `handlerFired = true`; call `LaunchAndLoginAsync` with `ErrorProbability=1.0`; assert `handlerFired == true` after await |
| AC-10 | Unit test: service with `ErrorProbability=1.0`; call `LaunchAndLoginAsync`; verify `CurrentState == Error`; call `StopAsync`; assert `CurrentState == NotRunning` and `LastErrorReason == null` |
| AC-11 | Separate unit test (own method, opts-mutation): EP=1.0; `LaunchAndLoginAsync` → Error; `StopAsync`; set `opts.ErrorProbability=0.0`; `LaunchAndLoginAsync` → assert `CurrentState == MatchSelection` without exception |
| AC-12 | Unit test: drive to `Error` state via AC-3 path; call `LaunchAndLoginAsync` again; assert `InvalidOperationException` thrown |
| AC-13 | Unit test: drive to `Error` state; call `LoadMatchAsync`; assert `InvalidOperationException` thrown |
| AC-14 | Unit test: `opts.ErrorProbability=0.0`; `LaunchAndLoginAsync`+`LoadMatchAsync` to `MatchLoaded`; set `opts.ErrorProbability=1.0`; `LoadMatchAsync` → error (reason = `"Error before ChangeMatch MatchSelection transition"`); `StopAsync`; `LaunchAndLoginAsync` with EP still 1.0 → error (reason = `"Error before Launching transition"`); assert `LastErrorReason == "Error before Launching transition"` |

---

## 6. Code Review Checklist

The following items are verified by human inspection during the delivery phase code review:

- [ ] `_rng` is a single `Random` instance initialised once at construction; it is not re-created per-method-call
- [ ] `_rng.NextDouble()` is called unconditionally at every delay point in `LaunchAndLoginAsync` and `LoadMatchAsync` (7 total) — no short-circuit on `ErrorProbability == 0.0`
- [ ] `StopAsync` contains no error injection check — it always resets to `NotRunning`
- [ ] In `TransitionToError`: `LastErrorReason` is set **before** `Transition(PcsProState.Error)` is called (so `StateChanged` fires after `LastErrorReason` is populated)
- [ ] `TransitionToError` call and `return` reside inside the `try` block so `SemaphoreSlim` is always released via `finally`
- [ ] No numeric literals for probability thresholds (always compared against `_options.ErrorProbability`)
- [ ] Reason strings match the canonical table in R-2 exactly (no free-hand string variations)
- [ ] `LastErrorReason` is cleared in `StopAsync` reset path (not just in `TransitionToError`)

---

## 7. Risk Notes

### Opts-mutation setup pattern (AC-4, AC-5, AC-11, AC-14)

Several ACs require reaching `MatchLoaded` state before activating error injection. With `ErrorProbability=1.0`, `LaunchAndLoginAsync` cannot complete, so the service can never advance past `NotRunning`. The solution is to construct the service with `ErrorProbability=0.0` and mutate the `opts` reference after reaching the desired state.

This works because `Options.Create(opts)` creates an `OptionsWrapper<T>` that stores the exact same object reference; the service's `_options = options.Value` field therefore points to the same `MockPcsProOptions` instance. Mutating `opts.ErrorProbability` in the test immediately affects `_options.ErrorProbability` in the service.

Since `CreateSut` does not expose the `opts` reference, tests using this pattern must bypass `CreateSut` and construct the service directly:

```csharp
var opts = new MockPcsProOptions(); // ErrorProbability = 0.0
var sut = new MockPcsProAutomationService(
    Options.Create(opts),
    NullLogger<MockPcsProAutomationService>.Instance);
// drive to desired state with opts.ErrorProbability = 0.0 ...
opts.ErrorProbability = 1.0;
// now error injection is active
```

Note: `_rng.NextDouble()` is still called for every delay point even when `ErrorProbability=0.0` (the result is always `>= 0.0`, so no error fires). Mutation changes when the already-advanced RNG value is compared, not whether draws occur. For seeded determinism tests the RNG draw count must account for all draws made during the setup phase.

> **Unconditional-draw invariant:** This property is mandated by R-2 but cannot be verified by any runtime unit test — no AC exercise sequence makes draws during an EP=0.0 phase observable to a seeded assertion. The §6 code-review checklist gate is the only enforcement mechanism. Reviewers must manually inspect all 7 delay sites to confirm the short-circuit pattern `if (EP > 0.0 && ...)` is absent.

### Determinism test seed selection (AC-8)

With `ErrorProbability=0.5`, the probability of an error firing on any single delay point is 50%. The implementation phase must choose a seed and document which calls error and which succeed. At minimum, the test sequence spans `LaunchAndLoginAsync` plus one additional call to prove cross-method sequence maintenance. The chosen seed and expected outcomes must be stated in the test method.

### RNG thread safety (R-1)
`Random.Shared` is thread-safe. `new Random(seed)` is NOT thread-safe when shared across threads. Since `MockPcsProAutomationService` uses a `SemaphoreSlim(1,1)` (established in S-003), only one lifecycle method runs at a time, making the seeded instance safe. The delivery phase must not introduce concurrent RNG access.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | Claude Sonnet 4.6 | NEEDS REVIEW — 3 HIGH (context artefacts + AC-7 + missing ACs), 6 MEDIUM, 3 LOW |
| R2 | 2026-04-11 | Claude Sonnet 4.6 | NEEDS REVIEW — 2 HIGH (AC-5 precondition contradiction + AC-14 vacuous), 2 MEDIUM (R-3 DRY + AC-8 underspecified), 3 LOW |
| R3 | 2026-04-11 | Claude Sonnet 4.6 | NEEDS REVIEW — 0 HIGH, 2 MEDIUM (R-3 ordering + unconditional draw not enforced), 3 LOW |
| R4 | 2026-04-11 | Claude Sonnet 4.6 | NEEDS REVIEW — 0 HIGH, 1 MEDIUM (StopAsync exemption implicit), 2 LOW |
| R5 | 2026-04-11 | Claude Sonnet 4.6 | **APPROVED** — 0 blocking; 2 LOW (AC-11 opts-mutation annotation + unconditional-draw note) applied as polish |
