# SPEC-S-002 — Delete Dead Hub Infrastructure

| Field       | Value |
|-------------|-------|
| **Status**  | R1-FIXES |
| **Step**    | S-002 |
| **IS**      | IS-012-Dead-Hub-Removal (APPROVED) |
| **HLPS**    | HLPS-012-Dead-Hub-Removal (APPROVED) |
| **Branch**  | `feature/S-001-stale-description-cleaner` (continues on same branch for squash-merge) |

---

## 1. Requirement

Delete all dead SignalR hub infrastructure now that the stale-description safety-net has been independently hosted (S-001). This covers: the hub class, all four broadcasters, the constants class, associated DI registrations, the hub endpoint mapping, all hub/broadcaster unit tests, and conditionally the `AddSignalR()` call.

This satisfies HLPS-012 SC-1, SC-2, SC-3, SC-5, SC-6, SC-7, SC-8.

## 2. Out of Scope (Preserved Artifacts)

The following artifacts reside in the same `Hubs/` directory but are **live circuit-tracking infrastructure**, not dead hub code. They must remain untouched:

- `PcsProCircuitHandler.cs` / `PcsProCircuitHandlerTests.cs`
- `IConnectionTracker.cs` / `ConnectionTracker.cs` / `ConnectionTrackerTests.cs`
- `StaleDescriptionCleaner.cs` / `StaleDescriptionCleanerTests.cs` (delivered in S-001)

## 3. Deletions

### Files to Delete
1. `src/PcsRemote.Web/Hubs/PcsProHub.cs`
2. `src/PcsRemote.Web/Hubs/PcsProStateBroadcaster.cs`
3. `src/PcsRemote.Web/Hubs/ManualModeBroadcaster.cs`
4. `src/PcsRemote.Web/Hubs/OperationInProgressBroadcaster.cs`
5. `src/PcsRemote.Web/Hubs/AutomationLogBroadcaster.cs`
6. `src/PcsRemote.Web/Hubs/PcsProHubConstants.cs`
7. `tests/PcsRemote.Web.Tests/Hubs/PcsProHubTests.cs`
8. `tests/PcsRemote.Web.Tests/Hubs/PcsProStateBroadcasterTests.cs`
9. `tests/PcsRemote.Web.Tests/Hubs/ManualModeBroadcasterTests.cs`
10. `tests/PcsRemote.Web.Tests/Hubs/OperationInProgressBroadcasterTests.cs`
11. `tests/PcsRemote.Web.Tests/Hubs/AutomationLogBroadcasterTests.cs`

### DI Registration Changes
- `WebApplicationBuilderExtensions.cs`: Remove `AddHostedService` for all 4 broadcasters, remove `MapHub<PcsProHub>` (wherever invoked), and conditionally remove `AddSignalR()`.
- `PcsProWebApplicationFactory.cs`: Remove mirrored `AddHostedService` for all 4 broadcasters, remove `MapHub<PcsProHub>`, and conditionally remove `AddSignalR()`.

### Implementation Order
Remove DI registrations and `using` directives before deleting source files to preserve incremental buildability.

### Conditional: AddSignalR()
Remove `AddSignalR()` and verify by build + E2E tests. If removal breaks the build or tests, retain it standalone per HLPS-012 R-1.

### Package References
Evaluate `.csproj` files for any now-unused explicit SignalR package references. Remove if found.

## 4. Acceptance Criteria

| AC | Criterion |
|----|-----------|
| AC-1 | All 11 files listed in §3 are deleted. |
| AC-2 | DI registrations for the 4 broadcasters and hub mapping are removed from both registration files. |
| AC-3 | Build produces 0 errors and 0 warnings. |
| AC-4 | All remaining tests pass. Before/after delta documented (baseline: 703 tests after S-001). The delta must reconcile exactly with the number of tests removed from deleted files. |
| AC-5 | `OperationalUxE2ETests` TC-1 through TC-3 pass (cross-circuit sync unaffected). TC-4 passes as broader regression coverage. |
| AC-6 | Out-of-scope artifacts (§2) remain present and their tests pass. |
| AC-7 | Disposition of `AddSignalR()` (removed or retained-standalone with rationale) is recorded in the Review History. |
| AC-8 | Package reference review is performed; any removals (or "none required") are documented. |

## 5. Review History

### R1 — 2 Reviewers

**Opus (claude-opus-4.7): APPROVE**
- F-01 (LOW): No AC captures AddSignalR disposition → Added AC-7.
- F-02 (LOW): No AC for package reference review → Added AC-8.
- F-03 (LOW): MapHub location may not be in WebApplicationBuilderExtensions → Reworded §3 to "wherever invoked".
- F-04 (INFO): Deletion ordering note → Added §3 Implementation Order.
- F-05 (INFO): Branch name confusion → Header already says "(continues on same branch for squash-merge)"; accepted as-is.

**GPT (gpt-5.4): REQUEST CHANGES**
- F-01 (MEDIUM): Protect circuit-tracking artifacts from over-deletion → Added §2 Out of Scope and AC-6.
- F-02 (LOW): AC-4 expected delta not defined → Added reconciliation requirement to AC-4.
- F-03 (LOW): TC-4 is not cross-circuit sync → Reworded AC-5 to separate TC-1–TC-3 (sync) from TC-4 (regression).

---

*End of SPEC-S-002.*
