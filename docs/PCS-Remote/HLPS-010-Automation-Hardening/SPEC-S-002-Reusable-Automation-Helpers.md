# SPEC-S-002: Reusable Automation Helpers

| Field | Value |
|---|---|
| **Document** | SPEC-S-002-Reusable-Automation-Helpers.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-22 |
| **Step ID** | S-002 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-002-automation-helpers` |

---

## 1. Purpose

Extract reusable FlaUI interaction patterns from `tools/AutomationDiagnostic/DiagnosticRunner.cs` into a shared helper class in `PcsRemote.Automation`. These helpers eliminate code duplication across the five FlaUi\* implementations delivered in S-003 through S-007 and make core interaction logic independently testable (HLPS-010 §2.5, H-SC-6).

Refer to the diagnostic tool (`tools/AutomationDiagnostic/DiagnosticRunner.cs`) for the proven interaction patterns that these helpers are derived from.

---

## 2. Scope

### 2.1 New File: `UIAutomationHelpers.cs`

**Location:** `src/PcsRemote.Automation/UIAutomationHelpers.cs`

**Class:** `internal static class UIAutomationHelpers`

**Requirements:**

- R1: **Safe element search wrappers** — Two `static` methods named `FindDescendant` (wraps `FindFirstDescendant`) and `FindAllDescendants` that wrap FlaUI's search methods with `try/catch` to return `null` or empty array on COMException/element-stale errors. These are the highest-reuse patterns in the diagnostic tool (~65 and ~39 call sites respectively for the wrapper methods).

- R2: **FindButtonByChildText** — Locate a `Button` element by matching search text against either the button's `Name` or the `Name` of child `Text` elements (WPF buttons often have content via child TextBlock rather than the Button.Name property). Case-insensitive, partial matching via `Contains`. Returns the first matching button or `null`.

- R3: **InvokeButtonSafely** — Safely invoke a button with the multi-strategy fallback pattern proven in the diagnostic tool: (1) check `IsEnabled` → return error if disabled, (2) try `InvokePattern` → (3) fallback to `.Click()` on failure. Returns `null` on success, or a descriptive error message string on failure.

- R4: **WaitForElement** — Poll for an element matching a condition within a timeout. 200ms poll interval, configurable timeout (default 2000ms). Returns the element or `null` on timeout. Must accept a `CancellationToken` parameter (default `CancellationToken.None`). Implementation must use synchronous polling with `Thread.Sleep(200)` and check `cancellationToken.IsCancellationRequested` before each poll iteration. When cancellation is requested, the method returns `null` immediately (does not throw). To enable unit testing of the polling/timeout logic, the search operation must be accepted as a `Func<AutomationElement?>` delegate parameter rather than raw `AutomationElement` + `ConditionBase`.

- R5: **WalkUpToClassName** — Walk the automation tree upward from a given element using `RawViewWalker`, returning the first ancestor whose `ClassName` matches. Cap at 10 steps to prevent infinite traversal. Return `null` if not found.

- R6: **ActivateToolWindow** — Activate a ToolWindow pane so its title bar buttons become interactive. Multi-strategy: (1) find parent `ToolWindowContainer` via `WalkUpToClassName`, search for TabItem/Header matching the window name, click it; (2) try `SelectionItemPattern.Select()`; (3) try `Focus()`; (4) try `Click()`. Returns `void` — FlaUI cannot reliably detect activation state, and all 6 diagnostic tool call sites use fire-and-forget semantics.

- R7: All methods must be `static` where they do not require instance state. Each method accepts the FlaUI types it needs as parameters — no stored fields or constructor dependencies. The helper class holds no state.

- R8: All public-facing methods (internal to the assembly) must have XML doc comments. Inline `//` comments from the diagnostic tool that explain non-obvious behaviour (e.g., WPF button child text pattern, spinner tree lifecycle) must be preserved.

- R9: Diagnostic `Console.WriteLine` calls must be replaced with structured logging via `ILogger` (from `Microsoft.Extensions.Logging.Abstractions`, already referenced in the project). Methods that need logging accept an optional `ILogger?` parameter. Low-level wrappers (R1) that are called at very high frequency do not need logging. No string interpolation in log calls.

- R10: The class must be declared in the `PcsRemote.Automation` namespace. All nullable reference return types must use nullable annotations (`AutomationElement?`, `string?`). All parameters and returns must have correct nullable annotations per method semantics.

### 2.2 Out of Scope

- **Popup helpers** (`TryOpenPopupButton`, `IsPopupVisible`, `FindPopupMenuItem`) — specialised to scoreboard automation, extracted in S-006 if reuse is confirmed.
- **Spinner waiting** (`WaitForSpinnerIdle`) — tightly coupled to match selection dialog structure, extracted in S-004.
- **Grid stabilisation** (`WaitForGridStable`) — unused in diagnostic tool, deferred.
- **Modifying any FlaUi\* class** to call these helpers — each class adopts helpers in its own step (S-003 through S-007).
- **Async wrappers** — helpers are synchronous (FlaUI is synchronous). `WaitForElement` uses synchronous `Thread.Sleep` polling with `CancellationToken.IsCancellationRequested` checks. The async boundary remains in `PcsProAutomationService`.

---

## 3. Requirements Traceability

| Req | Source | HLPS Reference |
|---|---|---|
| R1 | DiagnosticRunner lines 2807–2817 (~65 / ~39 call sites) | §2.5 reusable helpers |
| R2 | DiagnosticRunner lines 2642–2669 | §2.5, §2.1 (element lookup) |
| R3 | DiagnosticRunner lines 2675–2699 | §2.5 |
| R4 | DiagnosticRunner lines 2546–2558 | §2.5, §2.4 (polling) |
| R5 | DiagnosticRunner lines 1611–1631 | §2.5 |
| R6 | DiagnosticRunner lines 1642–1712 | §2.5, §2.3 (ToolWindow activation) |
| R7 | Architecture rules | Stateless helpers, DI parameter passing |
| R8 | Coding standards | Inline comment preservation |
| R9 | Coding standards | ILogger structured logging |
| R10 | Coding standards + namespace convention | PcsRemote.Automation namespace, nullable annotations |

---

## 4. Test Strategy

FlaUI's `AutomationElement` and related types are sealed COM interop wrappers that cannot be constructed or mocked in a test harness. Most helpers are directly intertwined with FlaUI calls and are verified via the diagnostic tool's proven patterns and garage PC integration in S-003+.

However, `WaitForElement` (R4) accepts a `Func<AutomationElement?>` delegate, making its polling/timeout/cancellation logic unit-testable without FlaUI.

### 4.1 Unit Tests — `UIAutomationHelpersTests.cs`

Test file: `tests/PcsRemote.Automation.Tests/UIAutomationHelpersTests.cs`

- **T1: WaitForElement returns immediately when delegate returns non-null** — Verify the method returns the element without delay when the search function succeeds on the first call.
- **T2: WaitForElement returns null on timeout** — Provide a delegate that always returns `null` and verify that the method returns `null` after approximately the timeout duration.
- **T3: WaitForElement respects cancellation** — Cancel the token before timeout expires and verify `null` is returned promptly (not after the full timeout).
- **T4: WaitForElement returns element found on Nth poll** — Provide a delegate that returns `null` for the first N calls then returns an element. Verify the element is returned.

### 4.2 Integration Verification (No Unit Tests)

The following helpers cannot be unit-tested without live UI elements. They are verified indirectly:

- **T5: FindButtonByChildText** — Verified via diagnostic tool (3 proven call sites) and S-003+ garage PC sessions.
- **T6: InvokeButtonSafely** — Verified via diagnostic tool (7 proven call sites) and S-003+ garage PC sessions.
- **T7: WalkUpToClassName** — Verified via diagnostic tool (2 proven call sites) and S-006 garage PC session.
- **T8: ActivateToolWindow** — Verified via diagnostic tool (6 proven call sites) and S-006+ garage PC sessions.
- **T9: FindDescendant / FindAllDescendants** — Safety wrappers with trivial logic (try/catch → null/empty). Verified by ubiquitous usage in diagnostic tool.

### 4.3 Build and Regression

- **T10: Build verification** — The Automation project must build with 0 warnings, 0 errors after adding the helper class.
- **T11: Existing tests remain green** — All existing tests must continue to pass. No behavioural changes are introduced since no FlaUi\* class is modified.

### 4.4 Formal Deviation from IS-010

**DEV-001:** IS-010 verification intent states *"Helper methods have unit tests covering core logic (e.g., timeout behaviour, child-text matching)."* This spec partially satisfies the intent:

- **Timeout behaviour** — fully covered by T1–T4 via the `Func<AutomationElement?>` delegate pattern.
- **Child-text matching** — the matching logic in `FindButtonByChildText` is intertwined with FlaUI `FindAllDescendants` calls and cannot be separated without introducing an artificial abstraction. Verified via diagnostic tool proven patterns.

This deviation is accepted because the FlaUI COM interop boundary makes full isolation impractical, and the diagnostic tool provides comprehensive integration verification.

---

## 5. Acceptance Criteria

- AC-1: `UIAutomationHelpers.cs` exists in `src/PcsRemote.Automation/` containing all seven helper methods (R1: `FindDescendant` + `FindAllDescendants`, R2: `FindButtonByChildText`, R3: `InvokeButtonSafely`, R4: `WaitForElement`, R5: `WalkUpToClassName`, R6: `ActivateToolWindow`).
- AC-2: The class is `internal static` with no instance state, in the `PcsRemote.Automation` namespace.
- AC-3: Each method accepts the FlaUI types it needs as parameters — no stored fields or constructor dependencies.
- AC-4: `FindButtonByChildText` uses case-insensitive `Contains` matching on both `Button.Name` and child `Text` element names.
- AC-5: `InvokeButtonSafely` returns `null` on success, descriptive `string?` on failure. Strategy order: IsEnabled check → InvokePattern → Click fallback.
- AC-6: `WaitForElement` accepts a `Func<AutomationElement?>` search delegate, a timeout parameter with 2000ms default, and a `CancellationToken` (default `CancellationToken.None`). Poll interval is 200ms. Returns `null` on timeout or cancellation. Synchronous (non-async).
- AC-7: `WalkUpToClassName` caps traversal at 10 parent steps.
- AC-8: `ActivateToolWindow` attempts four strategies in order: tab header click → SelectionItemPattern → Focus → Click. Returns `void`.
- AC-9: All diagnostic `Console.WriteLine` replaced with `ILogger` structured logging on methods that need it. Zero string interpolation in log calls.
- AC-10: All methods have XML doc comments. Non-obvious behaviour comments from diagnostic tool preserved as inline `//` comments.
- AC-11: All return types and parameters use correct nullable annotations (`AutomationElement?`, `string?`) per method semantics. Build produces zero nullable warnings.
- AC-12: Build: 0 warnings, 0 errors in the Automation project.
- AC-13: All existing tests pass (no regressions).
- AC-14: `UIAutomationHelpersTests.cs` exists in `tests/PcsRemote.Automation.Tests/` with unit tests for `WaitForElement` covering: immediate success, timeout, cancellation, and Nth-poll success (T1–T4). All new tests pass.

---

## 6. Risks

| ID | Risk | Mitigation |
|---|---|---|
| H-R-1 | Helpers are untestable in isolation (sealed FlaUI types) | Diagnostic tool serves as proven integration test; garage PC sessions in S-003+ verify end-to-end |
| H-R-2 | Production logging differs from diagnostic Console.WriteLine | Serilog Debug/Verbose level ensures logs are available in diagnostics but don't pollute production output |
| H-R-3 | Helper signatures may need adjustment when FlaUi* classes adopt them | Signatures follow diagnostic tool patterns which are proven; minor adjustments in S-003+ are acceptable |
