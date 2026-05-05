# SPEC-S-002 — Date-Parameterised Match Retrieval (Automation Layer)

| Field           | Value |
|-----------------|-------|
| **Status**      | APPROVED |
| **Step ID**     | S-002 |
| **Governs**     | IS-019 S-002 |
| **Branch**      | `feature/S-002-date-param-match-retrieval` |
| **SC Coverage** | SC-3 (enables), SC-6 (enables) |

---

## Problem

The current automation layer only supports retrieving matches for "today" — the date is derived internally from `TimeProvider`. HLPS-019 requires the ability to retrieve matches for an arbitrary date, passed as an explicit parameter. The existing "today's matches" method must be preserved as a convenience that delegates to the new date-parameterised method.

---

## Requirements

### R1 — New method on the public automation service interface

Add a method to the public automation service interface that accepts a date-only value (no time component) as a parameter and returns the list of matches for that date. The method signature should follow the same conventions as the existing today's-matches method (async, returns a read-only list of match info, accepts an optional cancellation token).

### R2 — Existing today's-matches method delegates

The existing today's-matches method must be refactored to derive "today" from the time provider using local time (not UTC — the feature targets operator-facing match selection in a single-timezone deployment), then delegate to the new date-parameterised method. Both public methods must share a single private core method that contains the actual logic; guards (manual mode check, interlocked concurrency guard, try/finally) exist only in the public methods. This ensures zero behavioural change for all existing callers while avoiding non-reentrant guard deadlock.

### R3 — Internal match selection automation accepts date parameter

The internal match selection automation interface (used by the service to interact with PCS Pro's UI) must gain a new overload of its search method that accepts a date-only parameter. The existing parameterless overload must delegate to the new one, deriving the date from the time provider. The FlaUI implementation's private date-filter method must accept the date as a parameter instead of deriving it internally.

### R4 — Service implementation refactoring

The real automation service implementation must be refactored so the core match-retrieval logic accepts a date parameter. Both public methods (existing and new) delegate to a shared private core method — guards live in the public methods only. Key changes:
- The new public method has the same guards as the existing one (manual mode check, interlocked concurrency guard, try/finally).
- The shared private core method, polling method, and parse-and-filter method all accept and forward the date parameter.
- The date filter in the parse-and-filter method must use the passed date instead of deriving it from the time provider.
- Log messages should include the search date for diagnostics.
- The "no matches found" error message should reference the search date.

### R5 — Mock implementation

The mock automation service must implement the new interface method. Per the accepted risk from HLPS-019 §9, the mock ignores the date parameter for match generation but uses it for the `MatchDate` field on returned match info records. The existing today's-matches method in the mock must delegate to the new method.

### R6 — Test fake update

The test fake for the internal match selection automation interface must implement the new overload. The parameterless overload should delegate to it.

---

## Test Strategy

### Existing test preservation

All existing tests for the today's-matches flow must continue to pass unchanged. The delegation from the existing method to the new one is the critical behavioural-preservation mechanism.

### New test cases

Tests should verify:

- **TC-1**: The date-parameterised method forwards the search date to the internal search automation (the fake should record the date it received).
- **TC-2**: The date-parameterised method filters parsed matches by the specified date, not by today's date.
- **TC-3**: The today's-matches method produces identical results to calling the date-parameterised method with today's date.
- **TC-4**: The mock implementation returns match info records with the specified date in the MatchDate field.

---

## Acceptance Criteria

| AC   | Description |
|------|-------------|
| AC-1 | A date-parameterised method exists on the public automation service interface. |
| AC-2 | The existing today's-matches method delegates to the new method and all existing tests pass unchanged. |
| AC-3 | The internal match selection automation interface has a date-accepting overload. |
| AC-4 | The FlaUI implementation's date-filter method accepts the date as a parameter. |
| AC-5 | The mock implementation uses the date parameter for MatchDate on returned records. |
| AC-6 | The test fake implements the new overload. |
| AC-7 | New tests verify date forwarding, date-based filtering, and delegation equivalence. |
| AC-8 | All existing project tests continue to pass. |
