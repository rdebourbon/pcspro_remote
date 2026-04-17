# SPEC-S-001: Core Domain Types, IYouTubeLiveStreamService Interface, and LoadedMatch Property

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-Core-Domain-Types.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-17 |
| **IS Step** | S-001 |
| **Governing HLPS** | HLPS-008-YouTube-LiveStream.md v0.4 (APPROVED) |
| **Governing IS** | IS-008-YouTube-LiveStream.md v0.3 (APPROVED) |
| **Branch** | `feature/IS-008-S-001-core-domain-types` |

---

## 1. Objective

Add all YouTube-related domain types and the service interface to `PcsRemote.Core`, and extend `IPcsProAutomationService` with a `LoadedMatch` property. This step creates the foundational types that every subsequent IS-008 step depends on.

---

## 2. Scope

### In Scope

1. **`LiveStreamStatus` enum** in `PcsRemote.Core` — values: `Idle`, `Starting`, `Live`, `Stopping`, `Error`.

2. **`LiveBroadcastInfo` record** in `PcsRemote.Core` (per HLPS-008 §4.2) — immutable snapshot of a YouTube broadcast. Three properties: the broadcast identifier, the rendered title, and the watch URL. All are strings. The record does not carry status (the service's `CurrentStatus` property is the single source of truth).

3. **`StreamStateSnapshot` record** in `PcsRemote.Core` (per HLPS-008 §4.3) — immutable snapshot carried by `StatusChanged` events. Three properties: the current status (a `LiveStreamStatus`), the current broadcast (nullable `LiveBroadcastInfo`), and an optional error message (nullable string).

4. **`YouTubeStreamException` class** in `PcsRemote.Core` — a custom exception for YouTube streaming errors. Must support construction with a message, and with a message plus inner exception. Derives from `Exception`.

5. **`IYouTubeLiveStreamService` interface** in `PcsRemote.Core` — per HLPS-008 §4.4 v0.4. Defines:
   - `CurrentStatus` property (read-only `LiveStreamStatus`)
   - `CurrentBroadcast` property (read-only nullable `LiveBroadcastInfo`)
   - `StatusChanged` event (`EventHandler<StreamStateSnapshot>`)
   - `StartStreamAsync` method (accepts `CancellationToken`, returns `Task`)
   - `StopStreamAsync` method (accepts `CancellationToken`, returns `Task`)
   - `ResetAsync` method (accepts `CancellationToken`, returns `Task`)
   - `InitializeAsync` method (accepts `CancellationToken`, returns `Task`)

   > **IS reconciliation note:** IS-008 S-001 (v0.3) lists six members, omitting `InitializeAsync`. HLPS-008 v0.4 §4.4 defines seven members including `InitializeAsync` (added during the v0.4 external review patch). The HLPS is the governing spec; this JIT Spec follows it.

6. **`LoadedMatch` property** on `IPcsProAutomationService` — `MatchInfo? LoadedMatch { get; }`. Returns the currently loaded match when state is `MatchLoaded`, null otherwise.

7. **Both concrete implementations** of `IPcsProAutomationService` updated:
   - `MockPcsProAutomationService` in `PcsRemote.Automation.Mock` — tracks the match passed to `LoadMatchAsync`; clears on state transitions away from `MatchLoaded`.
   - `PcsProAutomationService` in `PcsRemote.Automation` — tracks the match passed to `LoadMatchAsync`; clears on state transitions away from `MatchLoaded`.

### Out of Scope

- No implementations of `IYouTubeLiveStreamService` (S-003 and S-006)
- No `BroadcastTitleRenderer` (S-002)
- No UI components (S-004)
- No configuration schema changes
- No NuGet package additions to `PcsRemote.Core`

---

## 3. Deferred Concerns from HLPS-008 §9

The following concern from HLPS-008 §9 is assigned to this JIT Spec:

| Concern | Resolution |
|---|---|
| Test-double compatibility for `LoadedMatch` addition to `IPcsProAutomationService` | All existing tests use Moq's `Mock<IPcsProAutomationService>()` which auto-generates properties — adding `LoadedMatch` will not break them (Moq returns `default` for unsetup members). Both concrete implementations (`MockPcsProAutomationService`, `PcsProAutomationService`) are updated in scope. No additional test-double audit work is needed beyond confirming the build passes. |

---

## 4. Acceptance Criteria

| ID | Criterion | Verification |
|---|---|---|
| AC-1 | `LiveStreamStatus` enum exists with exactly five members: `Idle`, `Starting`, `Live`, `Stopping`, `Error`; default value is `Idle` (ordinal 0) | Unit test asserts all five values exist, are distinct, and default is `Idle` |
| AC-2 | `LiveBroadcastInfo` record is constructable with broadcast ID, title, and watch URL; properties are accessible; value equality holds for identical property values | Unit test constructs records and verifies properties and equality |
| AC-3 | `StreamStateSnapshot` record holds a status, nullable broadcast, and nullable error message | Unit test constructs snapshots with and without broadcast/error, verifies properties |
| AC-4 | `YouTubeStreamException` preserves message string | Unit test constructs with message, asserts `Message` matches |
| AC-5 | `YouTubeStreamException` preserves message and inner exception | Unit test constructs with message + inner, asserts both |
| AC-6 | `IYouTubeLiveStreamService` compiles with all seven members defined (4 methods, 2 properties, 1 event) | Compilation (implicitly tested by any test referencing the interface) |
| AC-7 | `IPcsProAutomationService.LoadedMatch` returns null when state ≠ `MatchLoaded` | Unit test: mock service in `NotRunning` state, assert `LoadedMatch` is null |
| AC-8 | `IPcsProAutomationService.LoadedMatch` returns the loaded match after `LoadMatchAsync` | Unit test: mock service, call `LoadMatchAsync`, assert `LoadedMatch` returns the same `MatchInfo` |
| AC-9 | `IPcsProAutomationService.LoadedMatch` returns null after `ChangeMatchAsync` (state leaves `MatchLoaded`) | Unit test: mock service in `MatchLoaded`, call `ChangeMatchAsync`, assert `LoadedMatch` is null |
| AC-9a | `IPcsProAutomationService.LoadedMatch` returns null after `StopAsync` (state leaves `MatchLoaded`) | Unit test: mock service in `MatchLoaded`, call `StopAsync`, assert `LoadedMatch` is null |
| AC-10 | `PcsRemote.Core.csproj` has no new package references (zero-dependency rule maintained) | Inspection of csproj diff |
| AC-11 | All existing tests pass | Full test run succeeds |
| AC-12 | `PcsProAutomationService` (real FlaUI impl) implements `LoadedMatch` — tracks the match passed to `LoadMatchAsync`, clears on non-`MatchLoaded` transitions | Code inspection (garage-PC-only testing deferred per I-U unknowns) |

---

## 5. Test-First Approach

Tests are written before production code. The test project is `PcsRemote.Core.Tests` for types in Core, and `PcsRemote.Automation.Mock.Tests` for mock service `LoadedMatch` behaviour.

### Test Classes and Methods

**`PcsRemote.Core.Tests/LiveStreamStatusTests.cs`**
- Verify enum has exactly 5 members
- Verify default value is `Idle` (ordinal 0)

**`PcsRemote.Core.Tests/LiveBroadcastInfoTests.cs`**
- Construct with valid arguments, verify all three properties
- Verify record equality (two records with same values are equal)

**`PcsRemote.Core.Tests/StreamStateSnapshotTests.cs`**
- Construct with status, broadcast, and error — verify all properties
- Construct with null broadcast and null error — verify nulls preserved

**`PcsRemote.Core.Tests/YouTubeStreamExceptionTests.cs`**
- Construct with message only — verify `Message`
- Construct with message and inner exception — verify both

**`PcsRemote.Automation.Mock.Tests/LoadedMatchTests.cs`**
- `LoadedMatch_BeforeLoadMatchAsync_ReturnsNull` — service starts, assert null
- `LoadedMatch_AfterLoadMatchAsync_ReturnsMatch` — load a match, assert returned
- `LoadedMatch_AfterChangeMatchAsync_ReturnsNull` — load then change, assert null
- `LoadedMatch_AfterStopAsync_ReturnsNull` — load then stop, assert null

---

## 6. Commit Strategy

Small, frequent commits in imperative mood. Approximate sequence (delivery phase determines exact groupings):

1. Add `LiveStreamStatus` enum + tests
2. Add `LiveBroadcastInfo` record + tests
3. Add `StreamStateSnapshot` record + tests
4. Add `YouTubeStreamException` class + tests
5. Add `IYouTubeLiveStreamService` interface
6. Add `LoadedMatch` to `IPcsProAutomationService` + both implementations + tests
7. Verify all existing tests still pass

---

## 7. Documentation Updates

- None required. Types are self-documented via XML comments. README and PROJECT-CONTEXT do not need changes for internal type additions.

---

## 8. Risk Assessment

| Risk | Mitigation |
|---|---|
| Adding `LoadedMatch` to interface breaks existing Moq-based tests | Moq auto-generates unsetup properties returning `default`. Verified: all 30+ test files use `Mock<IPcsProAutomationService>()`. No manual implementations exist outside the two concrete services. |
| `PcsRemote.Core.csproj` accidentally gains dependencies | AC-10 explicitly checks; `Stateless` is the only existing dependency and remains unchanged. |
| `PcsProAutomationService` (FlaUI) compilation on non-Windows | Project already targets `net8.0-windows`; this risk is pre-existing and unchanged. |

---

## 9. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-17 | Claude Opus 4.6, GPT-5.4 | Opus: APPROVED (0 blocking, 2 MED, 3 LOW). GPT: NEEDS REVIEW (2 HIGH, 1 MED, 1 LOW). Author triage: both HIGHs resolved (1 downgraded, 1 accepted). 5 fixes applied in v0.2. |

### R1 Findings

| # | Source | Severity | Finding | Disposition | Fix |
|---|---|---|---|---|---|
| GPT-1 | GPT | HIGH → LOW | Interface AC validates signatures not contract | Downgrade / Defer to Delivery | Spec already references HLPS §4.4; requiring re-transcription of XML docs violates JIT Spec abstraction level |
| GPT-2 | GPT | HIGH → MEDIUM | `InitializeAsync` not in IS-008 S-001 member list | Accept | Added IS reconciliation note to §2 item 5 |
| GPT-3 | GPT | MEDIUM | `LoadedMatch` clearing only covers `ChangeMatchAsync` in ACs | Accept | Added AC-9a for `StopAsync` path |
| GPT-4 | GPT | LOW | Test count baseline stale | Accept | Replaced "438+" with no count |
| Opus-1 | Opus | MEDIUM | Test plan includes tests without matching ACs | Accept | Aligned: AC-1 broadened (default value), AC-2 broadened (equality), AC-9a added |
| Opus-2 | Opus | MEDIUM | Record descriptions lack HLPS cross-references | Accept | Added "per §4.2" and "per §4.3" |
| Opus-3 | Opus | LOW | `InitializeAsync` discrepancy with IS | Same as GPT-2 | Addressed by reconciliation note |
| Opus-4 | Opus | LOW | Parameterless constructor omitted | Reject | Intentional: domain exception always carries a message |
| Opus-5 | Opus | LOW | Only `NotRunning` tested for null | Accept (informational) | No change — uniform null logic makes single-state sampling adequate |
