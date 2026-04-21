# SPEC-S-003: Automation Log Service Contract

| Field | Value |
|---|---|
| **Document** | SPEC-S-003-AutomationLogContract.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-21 |
| **Governing IS** | IS-011-Operational-UX.md v0.3 (APPROVED) |
| **Governing HLPS** | HLPS-011-Operational-UX.md v0.4 (APPROVED) |
| **Step** | S-003 |
| **Branch** | `feature/IS-011-S-003-automation-log-contract` |

---

## 1. Objective

Introduce the automation log service contract and entry type in `PcsRemote.Core`. This provides the shared boundary consumed by log producers (Mock and Real automation services in S-008), the log service implementation (S-006), and the log UI (S-007). Placing the contract in Core ensures both automation services can emit entries without depending on Web.

No implementation is delivered in this step — only the interface, record type, and enum.

---

## 2. Scope

### In Scope

- `AutomationLogOutcome` enum in `PcsRemote.Core`
- `AutomationLogEntry` record type in `PcsRemote.Core`
- `IAutomationLogService` interface in `PcsRemote.Core`
- Unit tests verifying type existence and member shapes via reflection

### Out of Scope

- `AutomationLogService` implementation (S-006)
- Rolling buffer logic (S-006)
- SignalR broadcasting (S-006)
- Automation instrumentation emitting entries (S-008)
- Log UI display (S-007)

---

## 3. Requirements

### R-1: AutomationLogOutcome Enum

An enum with exactly three members representing the outcome category of a log entry: one for informational/in-progress steps (e.g., "Launching PCS Pro…"), one for successful completion, and one for failures. The canonical member names are `Info`, `Success`, and `Failure`.

### R-2: AutomationLogEntry Record

An immutable record type representing a single log entry. Each entry carries:
- A timestamp of type `DateTimeOffset` (representing when the action occurred)
- An action description (human-readable string describing what the automation is doing)
- An outcome indicator (using the R-1 enum)

The record should be a simple data carrier with no behaviour beyond what the compiler generates for records.

### R-3: IAutomationLogService Interface

A service interface with the following members:
- **`AddEntry`** — a void method to add a new log entry. Accepts the action description (string) and outcome (AutomationLogOutcome). The timestamp is generated internally by the implementation — callers do not provide it.
- **`GetRecentEntries`** — a method to retrieve a point-in-time snapshot of recent entries as an `IReadOnlyList<AutomationLogEntry>`. Entries are ordered chronologically, oldest to newest. The returned list is an immutable copy — subsequent additions do not mutate it.
- **`EntryAdded`** — an event of type `EventHandler<AutomationLogEntry>` that fires when a new entry is added, providing the new entry to subscribers.

### R-4: Core Zero-Dependency Rule

All types are declared in `PcsRemote.Core`. No project references or new NuGet packages added.

---

## 4. Design Notes

### Timestamp Source

The interface contract specifies that the `AddEntry` method accepts action and outcome but not a timestamp — the implementation provides the timestamp. This keeps the caller API simple and ensures consistent time sourcing. For testability, the implementation (S-006) may accept a time provider via DI, but that is not a Core contract concern.

### Thread Safety

Implementations must be thread-safe — `AddEntry` may be called from background threads while subscribers handle `EntryAdded` events and UI components call `GetRecentEntries` concurrently. The `EntryAdded` event may fire on any thread; subscribers are responsible for marshalling to their own synchronisation context if needed. When `EntryAdded` fires, the new entry is already present in the result of any subsequent `GetRecentEntries` call on any thread (publish-after-commit). This is a contractual requirement on implementations (S-006), documented here to prevent incompatible assumptions between producers and consumers.

### Buffer Size

The 200-entry buffer limit (OUX-C-4) is an implementation constraint on the concrete service in S-006, not a contract-level concern. The interface's snapshot method returns "recent entries" without prescribing the buffer size.

---

## 5. Test Cases

All tests live in `PcsRemote.Core.Tests`.

### TC-1: `AutomationLogOutcome_IsEnumWithExactMembers`
**Assert:** The type is an enum in the `PcsRemote.Core` namespace with exactly three members named `Info`, `Success`, and `Failure`.

### TC-2: `AutomationLogEntry_IsRecordWithExpectedProperties`
**Assert:** The type has properties for timestamp (DateTimeOffset), action (string), and outcome (AutomationLogOutcome). The type is a record (has compiler-generated Equals/GetHashCode).

### TC-3: `IAutomationLogService_HasAddEntryMethod`
**Assert:** The interface has a void method named `AddEntry` that accepts a string and an `AutomationLogOutcome` parameter.

### TC-4: `IAutomationLogService_HasGetRecentEntriesMethod`
**Assert:** The interface has a method named `GetRecentEntries` that returns `IReadOnlyList<AutomationLogEntry>`.

### TC-5: `IAutomationLogService_HasEntryAddedEvent`
**Assert:** The interface has an event named `EntryAdded` of type `EventHandler<AutomationLogEntry>`.

---

## 6. Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `AutomationLogOutcome` enum exists in `PcsRemote.Core` with exactly three members: `Info`, `Success`, `Failure` |
| AC-2 | `AutomationLogEntry` record exists in `PcsRemote.Core` with timestamp (`DateTimeOffset`), action (`string`), and outcome (`AutomationLogOutcome`) properties |
| AC-3 | `IAutomationLogService` interface exists in `PcsRemote.Core` with `AddEntry` method, `GetRecentEntries` method (returns `IReadOnlyList<AutomationLogEntry>`, oldest→newest), and `EntryAdded` event |
| AC-4 | `PcsRemote.Core.csproj` has zero project references and no new `PackageReference` entries (unchanged) |
| AC-5 | TC-1 through TC-5 pass |
| AC-6 | `PcsRemote.Core` and `PcsRemote.Core.Tests` build with 0 errors, 0 warnings |
| AC-7 | All pre-existing tests in `PcsRemote.Core.Tests` (86 at baseline) continue to pass |

---

## 7. Commit Strategy

Delivered on `feature/IS-011-S-003-automation-log-contract`; squash-merged to `master` after adversarial code review approval.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Opus 4.7, GPT 5.4 | REQUEST CHANGES (threading, naming, ordering) |
| R2 | 2026-04-21 | Opus 4.7, GPT 5.4 | REQUEST CHANGES (TC consistency, type pinning) |
| R3 | 2026-04-21 | Opus 4.7, GPT 5.4 | APPROVED (2/2 Opus + 1 accepted, 1 rejected GPT) |

