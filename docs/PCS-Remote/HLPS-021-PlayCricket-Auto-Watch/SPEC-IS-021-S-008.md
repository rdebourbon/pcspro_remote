# SPEC-IS-021-S-008 — Midnight Reset

| Field | Value |
|---|---|
| **Document** | SPEC-IS-021-S-008.md |
| **Step** | IS-021 S-008 |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-05-20 |
| **Governing IS** | IS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Governing HLPS** | HLPS-021-PlayCricket-Auto-Watch.md (APPROVED) |
| **Branch** | `feature/IS-021-S-008-midnight-reset` |

---

## 1. Context and Purpose

Auto-load suppression prevents the same match from being loaded repeatedly within a session (SC-1). However, suppression records must not persist across match days — if they did, a match that was played on one day could be suppressed forever. Similarly, a match left in the `MatchLoaded` state overnight should be automatically cleared so the next day starts clean.

S-008 delivers a separate midnight reset hosted service. Its sole responsibilities are:
1. At local midnight, return to match selection if the system is in a loaded state and manual mode is inactive.
2. Unconditionally clear all auto-load suppression records after the midnight wake — regardless of whether match-return was performed or succeeded.
3. Re-arm and repeat for the following midnight.

This service carries no Play-Cricket API dependency and operates independently of the auto-watch enabled/disabled state (per SC-8).

---

## 2. Scope

**In scope:**
- New hosted service class in `PcsRemote.Web` implementing `IHostedService`/`BackgroundService`.
- Midnight delay and re-arm loop.
- Conditional `ChangeMatchAsync` call at wake.
- Unconditional `ClearAutoLoadSuppressions()` call at wake.
- Exception handling for the match-return call.
- Testable time and delay abstractions via injected functions (internal test constructor, following the existing pattern in `PlayCricketWatcherHostedService`).
- DI registration in `WebApplicationBuilderExtensions` (or `Program.cs`).
- Unit tests in `PcsRemote.Web.Tests`.
- IS-021 roadmap update: S-008 marked delivered.

**Out of scope:**
- Changes to `IPcsProAutomationService`, `IPlayCricketWatcherService`, or `IManualModeService` interfaces (all required methods already exist).
- UI changes.
- DST-aware re-arm beyond standard local-time midnight calculation.

---

## 3. Behavioural Requirements

### R-1 — Midnight delay
On service start, the service calculates how long until the next local midnight (i.e., the next occurrence of 00:00:00 local time), and delays for that duration. The calculation uses the injected "get current local time" function, not a static clock call. If the current time is exactly midnight (00:00:00.000), the delay is 24 hours (the following midnight).

### R-2 — Wake: match-return (conditional)
When the delay expires (midnight fires), the service reads the current automation state and manual mode flag. If and only if the automation state is `PcsProState.MatchLoaded` and manual mode is **not** active, the service calls `ChangeMatchAsync` on the automation service. In all other states, or when manual mode is active, no automation call is made.

### R-3 — Wake: suppression clear (unconditional)
After the match-return step (whether called or skipped), the service calls `ClearAutoLoadSuppressions()` on the watcher service. This call is unconditional — it must execute regardless of automation state, manual mode state, and whether match-return threw an exception.

### R-4 — Exception isolation
If `ChangeMatchAsync` throws, the exception is caught and logged at Error level. The suppression clear (R-3) must still execute.

### R-4a — Suppression-clear robustness
If `ClearAutoLoadSuppressions` throws, the exception is caught and logged at Error level. The re-arm loop (R-5) must still continue. This ensures a transient failure in suppression clearing cannot permanently disable the midnight reset.

### R-5 — Re-arm
After completing the wake sequence (R-2 through R-4), the service recalculates the time until the next local midnight using the injected clock and delays again. This loop repeats indefinitely until the service is stopped.

### R-6 — Graceful stop
When the service stopping token is cancelled (e.g., application shutdown), any in-progress delay is cancelled. The service exits cleanly without performing the wake sequence for an incomplete delay cycle.

### R-7 — No operational logging noise
Midnight wake and match-return calls are logged at `Information` level. Suppression clear is logged at `Debug` level. Exception from match-return is logged at `Error` level. No log entry is emitted on delay cancellation (i.e., normal stop).

### R-8 — Testability
The service exposes an internal constructor (visible to the test project via `InternalsVisibleTo`) accepting:
- An injected "get current local time" function (returns `DateTimeOffset`).
- An injected delay factory (accepts `TimeSpan` and `CancellationToken`, returns `Task`).

This mirrors the `Func<IPeriodicTimer>` pattern used by `PlayCricketWatcherHostedService`. The production constructor uses real `DateTimeOffset.Now` and `Task.Delay`.

---

## 4. Acceptance Criteria

| AC | Requirement |
|---|---|
| AC-1 | At wake, `ChangeMatchAsync` is called when state is `MatchLoaded` and manual mode is off. |
| AC-2 | At wake, `ChangeMatchAsync` is NOT called when state is `MatchSelection`. |
| AC-3 | At wake, `ChangeMatchAsync` is NOT called when state is `NotRunning`. |
| AC-4 | At wake, `ChangeMatchAsync` is NOT called when state is `Error`. |
| AC-4a | At wake, `ChangeMatchAsync` is NOT called in any state other than `MatchLoaded` (implicitly covers all intermediate states: `Launching`, `LoginScreen`, `MatchSelectionSearching`, `MatchSelectionReady`). |
| AC-5 | At wake, `ChangeMatchAsync` is NOT called when manual mode is active (even if state is `MatchLoaded`). |
| AC-6 | `ClearAutoLoadSuppressions()` is called on every wake, regardless of state or manual mode. |
| AC-7 | `ClearAutoLoadSuppressions()` is called even when `ChangeMatchAsync` throws. |
| AC-8 | When `ChangeMatchAsync` throws, the exception is logged at Error level and the service does not crash. |
| AC-8a | When `ClearAutoLoadSuppressions` throws, the exception is logged at Error level and the re-arm loop continues. |
| AC-9 | After the wake sequence, the service re-arms and waits for the next midnight. |
| AC-10 | When the service is stopped during the delay, it exits without calling `ChangeMatchAsync` or `ClearAutoLoadSuppressions()`. |
| AC-11 | The midnight reset fires and clears suppression records regardless of the auto-watch enabled/disabled state (SC-8). |
| AC-12 | The service is registered as a hosted service in the DI container. |

---

## 5. Test Cases

Tests are written before production code (test-first). All tests reside in the existing `PcsRemote.Web.Tests` project, in a new `PlayCricketMidnightResetHostedServiceTests` class.

Test infrastructure: Moq for `IPcsProAutomationService`, `IManualModeService`, `IPlayCricketWatcherService`, and `ILogger<T>` (via the existing `CapturingLogger<T>` helper). The injected delay factory uses a `TaskCompletionSource<bool>` per delay call, allowing tests to trigger wakes deterministically. The injected clock function returns a test-controlled `DateTimeOffset`.

| TC | Name | Description |
|---|---|---|
| TC-1 | `MidnightWake_MatchLoaded_ManualModeOff_CallsChangeMatch` | Wake fires with state=MatchLoaded and manual mode off → `ChangeMatchAsync` called once. |
| TC-2 | `MidnightWake_MatchSelection_DoesNotCallChangeMatch` | Wake fires with state=MatchSelection → `ChangeMatchAsync` not called. |
| TC-3 | `MidnightWake_NotRunning_DoesNotCallChangeMatch` | Wake fires with state=NotRunning → `ChangeMatchAsync` not called. |
| TC-4 | `MidnightWake_Error_DoesNotCallChangeMatch` | Wake fires with state=Error → `ChangeMatchAsync` not called. |
| TC-5 | `MidnightWake_MatchLoaded_ManualModeActive_DoesNotCallChangeMatch` | Wake fires with state=MatchLoaded but manual mode is active → `ChangeMatchAsync` not called. |
| TC-6 | `MidnightWake_AlwaysClearsSuppression_WhenNotLoaded` | Wake fires with state=MatchSelection → `ClearAutoLoadSuppressions()` called despite no match-return. |
| TC-7 | `MidnightWake_AlwaysClearsSuppression_WhenLoaded` | Wake fires with state=MatchLoaded → `ClearAutoLoadSuppressions()` called after `ChangeMatchAsync`. |
| TC-8 | `MidnightWake_ChangeMatchThrows_SuppressionStillCleared` | `ChangeMatchAsync` throws → exception caught and logged at Error; `ClearAutoLoadSuppressions()` still called. |
| TC-9 | `MidnightWake_ChangeMatchThrows_LogsError` | `ChangeMatchAsync` throws → log entry at Error level present. |
| TC-10 | `ServiceStop_DuringDelay_ExitsWithoutWakeActions` | Service stopping token cancelled while waiting for midnight → `ChangeMatchAsync` and `ClearAutoLoadSuppressions()` not called. |
| TC-11 | `MidnightWake_Fires_ReArmsForNextMidnight` | After first wake completes, service delays again (second delay factory call observed). |
| TC-12 | `MidnightWake_DelayDuration_IsCorrectForCurrentTime` | Given injected "current time" of 23:00:00 local, the first computed delay is within ±5 seconds of 1 hour (between 3595 and 3605 seconds). |
| TC-13 | `MidnightWake_AutoWatchDisabled_StillFiresAndClearsSuppression` | Auto-watch is disabled on the watcher service; wake fires → `ClearAutoLoadSuppressions()` still called (SC-8 independence). |
| TC-14 | `MidnightWake_ExactlyAtMidnight_DelaysFullDay` | Given injected "current time" of exactly 00:00:00.000, the computed delay is within ±5 seconds of 24 hours (86400 seconds). |
| TC-15 | `MidnightWake_SuppressionClearThrows_LogsErrorAndReArms` | `ClearAutoLoadSuppressions()` throws → exception logged at Error level; service re-arms for the next midnight (second delay factory call observed). |

---

## 6. Incremental Commit Strategy

Commits are small and logically grouped:
- One commit for the new hosted service class (production code only).
- One commit for the test class with all 12 test cases.
- One commit for DI registration.
- IS-021 roadmap update (S-008 marked delivered) in the squash-merge commit.

Exact commit count and ordering is a Delivery phase decision.

---

## 7. Documentation Updates

- IS-021 roadmap: mark S-008 as `✅ DELIVERED` after squash-merge.
- No HLPS changes required.

---

## 8. Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
| R1 | 2 | claude-opus-4.5, gpt-5.4 | Opus: 0×HIGH, 3×MEDIUM, 3×LOW. GPT: 3×HIGH, 2×MEDIUM, 1×LOW | Accept: GPT-H1 (new AC-11+TC-13), GPT-H2+Opus-M3 (R-4a+AC-8a), GPT-M4+Opus-M2 (R-1 midnight boundary, TC-12 ±5s, TC-14). Downgrade: GPT-H3→LOW (AC-12 added). Defer: GPT-M5, Opus-L1/L2/L3, GPT-L6 | REVISION | 7 fixes applied: R-4a (suppression-clear robustness), AC-4a (catch-all non-MatchLoaded), AC-8a (suppression-clear failure), AC-11 (SC-8 independence), AC-12 (DI registration), R-1 exact-midnight clarification, TC-12 ±5s tolerance, TC-13 (auto-watch disabled), TC-14 (exact midnight boundary) |
| R2 | 2 | claude-opus-4.5, gpt-5.4 (general-purpose; pr-review-agent substituted — format incompatibility) | Opus: 0×MEDIUM. GPT: 1×MEDIUM | Accept: GPT-R2-M1 (add TC-15: ClearAutoLoadSuppressions throws → error logged + re-arm continues) | REVISION | TC-15 added; all R1 fixes verified adequate by Opus |
| R3 | 2 | claude-opus-4.5 (R2 APPROVED, no new findings), gpt-5.4 (focused verify TC-15 only) | 0 findings | — | APPROVED | Unanimous approval. TC-15 verified adequate by GPT. |
