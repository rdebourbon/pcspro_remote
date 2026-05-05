# HLPS-019 — Debug Date Selection Override

| Field        | Value                          |
|--------------|--------------------------------|
| **Status**   | APPROVED                         |
| **Author**   | Copilot                        |
| **Created**  | 2026-05-05                     |

---

## 1. Problem Statement

The PCS Remote match selection process uses `TimeProvider` to determine "today's" date, which is then typed into PCS Pro's search fields and used to filter returned matches. For testing purposes, a `TestDateOverride` setting in `appsettings.json` can lock this date to an arbitrary value via `FixedDateTimeProvider`.

This design has a critical operational flaw: the override is invisible at runtime, persists across deployments, and has no safeguard against accidental use in production. On 2026-05-05, the application was deployed to production with `TestDateOverride` still set to `2025-06-01` (a date from the previous season), preventing operators from selecting any of today's matches.

The root cause is that a testing concern (date override) is embedded in infrastructure configuration rather than being an explicit, operator-visible, transient action.

## 2. Proposed Solution

Replace the config-based date override with a **debug-panel-gated, single-use date selection** feature:

1. **Debug panel toggle** — A toggle labelled "Allow Date Selection" in the debug section. Defaults to **Off**.

2. **Conditional UI in match selection** — When the toggle is Off, the match selection prompt is unchanged: `Load Today's Matches` and `Use Current Match`. When the toggle is On, a **third option** appears: a date picker control and a `Load Matches for Date` button.

3. **Single-use auto-reset** — The toggle automatically resets to Off on **any** transition into the `MatchSelection` state, regardless of origin (post-ChangeMatch, post-login, post-error-recovery). This ensures the date selection is a one-shot override for a single match selection cycle; the operator must explicitly re-enable it each time they want to select matches from a non-today date. Collapsing or locking the debug panel does **not** reset the toggle — only a state-machine transition to `MatchSelection` does.

4. **Removal of config-based override** — The `TestDateOverride` setting in `appsettings.json` (both the key and the code that reads it) and `FixedDateTimeProvider` are removed entirely. Production DI always registers `TimeProvider.System`; test projects continue to inject `FakeTimeProvider` directly, bypassing DI registration.

## 3. Constraints

| ID   | Constraint |
|------|------------|
| C-1  | Must not change the behaviour of `Load Today's Matches` or `Use Current Match` — these always use today's real date. |
| C-2  | The date selection toggle can only be **enabled** when the debug panel is unlocked. Once enabled, the date picker is visible in the main match selection UI regardless of whether the debug panel is subsequently collapsed or locked. |
| C-3  | The toggle must default to Off on application startup (in-memory state, not persisted). |
| C-4  | The toggle must auto-reset to Off on any transition into `MatchSelection` state (post-ChangeMatch, post-login, or any other path). |
| C-5  | The `PcsRemote.Core` zero-dependency rule must be maintained — the date selection service interface lives in Core, implementation can live anywhere appropriate. |
| C-6  | Existing tests that use `FakeTimeProvider` for time-dependent behaviour (timeouts, health checks) must continue to work — `TimeProvider` stays in the DI container for those purposes. |

## 4. Success Criteria

| ID   | Criterion |
|------|-----------|
| SC-1 | Deploying the application with default configuration shows no date selection UI; match selection behaves identically to pre-change behaviour (Load Today's Matches uses today's date). |
| SC-2 | With the debug toggle On, a date picker and "Load Matches for Date" button appear alongside the existing buttons in the match selection prompt. |
| SC-3 | Clicking "Load Matches for Date" with a selected date types that date into PCS Pro's search fields and filters returned matches by that date. |
| SC-4 | After loading a match via the date override flow and subsequently clicking ChangeMatch, the toggle is Off and the date picker is no longer visible. |
| SC-5 | The toggle also resets to Off after other paths into `MatchSelection` (e.g. post-login), not just post-ChangeMatch. |
| SC-6 | `TestDateOverride` config key and `FixedDateTimeProvider` are removed from the codebase. |
| SC-7 | All existing tests pass. New tests cover the date selection service toggle behaviour, auto-reset, and auto-select when a single match is returned via the date override flow. |
| SC-8 | When `Load Matches for Date` returns exactly one match, that match is auto-selected, consistent with `Load Today's Matches` behaviour. |

## 5. Architectural Approach

### 5.1 Service Layer

A new service interface in `PcsRemote.Core` tracks the toggle state (enabled/disabled) and notifies subscribers when the state changes. The service tracks only the toggle state — the selected date is managed locally by the UI and passed explicitly to the automation layer. The implementation is an in-memory singleton with no external dependencies, consistent with the Core zero-dependency rule (C-5).

### 5.2 Automation Layer Changes

The automation layer gains the ability to search for and filter matches by an arbitrary date, not just today's date. The existing "today's matches" flow is preserved as a delegation to the new date-parameterised flow. The date is passed explicitly as a method parameter — the automation layer does **not** consult the date selection service. This keeps the service as a UI-only concern and avoids ambient mutable state in the automation layer.

### 5.3 UI Layer Changes

The debug section gains a toggle control for enabling/disabling date selection. The main page subscribes to the service's state-changed notification and conditionally renders a date picker and "Load Matches for Date" button when the toggle is on and the state machine is in the match selection state. The date picker defaults to today's date (U-1). When only one match is returned, it is auto-selected (U-2), consistent with the existing "Load Today's Matches" behaviour.

### 5.4 Auto-Reset Mechanism

The toggle is automatically disabled on any transition into the match selection state, regardless of the transition's origin. This provides a universal reset that covers post-ChangeMatch, post-login, and post-error-recovery paths without requiring path-specific logic.

## 6. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|----------|------------|
| U-1 | Should the date picker default to today's date or be empty when first shown? | User | No | **Resolved:** Default to today's date — consistent with what "Load Today's Matches" would use, saves the operator a click on the most common use case. |
| U-2 | Should the "Load Matches for Date" flow also support auto-select when only one match is returned? | User | No | **Resolved:** Yes — reuse the same auto-select logic for consistency. |

## 7. Out of Scope

- Persisting the date selection state across application restarts (explicitly ruled out — in-memory only).
- Any changes to the `Use Current Match` flow (attaching to an already-loaded match is date-independent).
- Accessibility hardening of the date picker control (keyboard navigation, ARIA labels) — track separately if required.

## 8. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Automation types wrong date into PCS Pro | Low | Medium | The date flows explicitly through method parameters — no ambient state mutation. Tests verify the date parameter is forwarded correctly. |
| All connected operators share toggle state (server-side singleton) | Certain (by design) | Low | Intentional singleton behaviour consistent with the single-operator deployment model. PCS Remote is designed for one operator per PCS Pro instance. The debug panel is PIN-protected, limiting exposure. |
| Operator selects a date with no matches (empty result) | Medium | Low | The existing "No matches available today" empty-state UI handles this case. The error flow (`FilterToday` returning zero matches → error state) applies equally to date-override searches. |
| Date-picker value in unexpected timezone or format | Low | Low | PCS Pro's date pickers use the local machine's date format. The automation already formats dates using `dd/MM/yyyy` from `DateOnly.ToString()`. The override date follows the same formatting path. |
| Removing `FixedDateTimeProvider` breaks `FakeTimeProvider` usage in tests | None | N/A | `FakeTimeProvider` is a test-only type (Microsoft.Extensions.TimeProvider.Testing) unrelated to `FixedDateTimeProvider`. Production DI always registers `TimeProvider.System`; tests inject `FakeTimeProvider` directly. |

## 9. Mock Service Obligation

Adding a date-parameterised match retrieval method to the public automation service interface requires the mock automation service to implement it. The mock delegates to existing synthetic match generation logic, ignoring the date parameter — consistent with the mock's philosophy of returning synthetic data without real automation.
