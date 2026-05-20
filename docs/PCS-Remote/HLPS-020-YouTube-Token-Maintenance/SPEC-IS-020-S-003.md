# SPEC-IS-020-S-003 — Proactive Refresh Logic and Staleness Tracking

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **Step** | IS-020 S-003 |
| **Branch** | `feature/IS-020-S-003-proactive-refresh` |
| **Governing IS** | IS-020-YouTube-Token-Maintenance.md |
| **Governing HLPS** | HLPS-020-YouTube-Token-Maintenance.md |

---

## Context

S-002 added the `TokenExpiryApproaching` event to the interface. This step adds the logic that fires it: a proactive credential refresh capability and a staleness-tracking mechanism that detects when the refresh token has not been rotated recently enough to be trusted.

The two concerns are delivered together because the staleness check fires after each proactive refresh — the check requires the refresh to have run, and the refresh requires the staleness check to decide whether to fire the event. Testing them separately would require stubs for the other side; delivering them together allows each unit test to exercise both in isolation.

The background scheduled caller is not added in this step — that is S-004. The delivery method must be callable from S-004's background hosted service via the `IYouTubeLiveStreamService` interface, which means a new `RunProactiveRefreshAsync` method must be added to the interface in this step.

---

## Requirements

### R-1 — New interface method: proactive refresh entry point

`IYouTubeLiveStreamService` gains a new method for triggering a proactive credential refresh. The method takes a cancellation token and returns a Task. When the service is not in the ready state, the method is a no-op (returns without doing anything). When ready, it performs the credential refresh described in R-2. The mock must implement the method as a no-op that returns immediately without changing any state.

### R-2 — Proactive credential refresh (real service)

When the service is in the ready/authenticated state, the proactive refresh performs the following:

1. Records the current refresh token value before the refresh.
2. Calls the Google credential refresh API to request a new token. The credential object that was built during `InitializeAsync` and stored in the service must be accessible to this method — a private field holding the `UserCredential` reference must be retained from the initialisation path.
3. After the refresh, compares the refresh token value before and after. If the refresh token changed (rotation detected), writes the staleness marker (R-4) with the current UTC time.
4. Regardless of whether rotation was detected, runs the staleness check (R-3).
5. On `TokenResponseException` (Google auth failure): delegates to the existing auth-failed handling path used in `InitializeAsync` — token deleted, availability transitions to `AuthFailed`, `AuthStatusChanged` fires. No new failure path is introduced.
6. On other exceptions: logs a warning and returns without changing availability state or staleness marker.

### R-3 — Staleness check

The staleness check reads the persisted marker (R-4) and compares its age against the configurable staleness threshold from `YouTubeOptions`. If the marker is absent (no re-consent ever recorded), the check does not fire the event — there is nothing to compare against. If the marker age exceeds the threshold, fires `TokenExpiryApproaching` (added in S-002). The check runs at two points:

- **At service initialisation (`InitializeAsync`):** runs whenever the staleness marker store contains a valid marker, regardless of whether initialisation succeeded or what the current availability state is. This is the safety net for the scenario where the app is restarted on match day after an extended idle period (HLPS-020 A5, SC3).
- **After each proactive refresh call:** runs only when the service is in the ready state (ongoing check is suppressed if not ready, consistent with HLPS-020 C2).

### R-4 — Staleness persistence abstraction

The staleness marker is a single UTC timestamp representing the last time the refresh token was rotated (either via re-consent in `RunOAuthSetupAsync` or via proactive rotation in R-2). It is stored in a separate persistence location, independent of the Google credential data store (HLPS-020 SC3).

A new injectable abstraction for staleness persistence is introduced. It must support:
- Reading the last-consent timestamp (returning null when no marker is present).
- Writing a new timestamp.

The concrete implementation stores the marker on the local file system in a location consistent with the existing token store path convention (`YouTubeOptions.GetEffectiveTokenStorePath()`), but in a different file or subdirectory so it is clearly separated from the Google credential store.

### R-5 — Re-consent marker update in `RunOAuthSetupAsync`

After a successful `GoogleWebAuthorizationBroker.AuthorizeAsync` call (re-consent complete), `RunOAuthSetupAsync` writes the staleness marker (R-4) with the current UTC time before calling `InitializeAsync`. This marks the point of confirmed re-consent.

### R-6 — `YouTubeOptions` extension

`YouTubeOptions` gains two new configuration properties:
- A staleness threshold in whole days (default: 5). Used by the staleness check (R-3) to decide when to fire the event.
- A proactive refresh interval in whole hours (default: 6). Not directly used by the service logic in this step — its value is read by S-004's background hosted service. Adding it here ensures configuration is co-located with the existing YouTube options.

---

## Test Cases

All tests follow project conventions: MSTest 3.x + FluentAssertions, method naming `MethodName_Scenario_ExpectedResult`. New tests are added to `YouTubeLiveStreamServiceTests`. The mock requires no new tests beyond compile success.

### Service tests (runnable, not `[Ignore]`)

| TC | Method name | Scenario | Expected result |
|---|---|---|---|
| TC-1 | `RunProactiveRefreshAsync_WhenNotReady_IsNoOp` | Availability is not Ready (e.g., NotConfigured) | Returns without calling credential refresh; no staleness check; no event |
| TC-2 | `RunProactiveRefreshAsync_WhenReady_WithTokenRotation_UpdatesMarker` | Service is Ready; credential refresh returns a new refresh token | Staleness marker is written with the current time |
| TC-3 | `RunProactiveRefreshAsync_WhenReady_WithoutTokenRotation_DoesNotUpdateMarker` | Service is Ready; credential refresh returns the same refresh token | Staleness marker is not written |
| TC-4 | `RunProactiveRefreshAsync_WhenReady_TokenResponseException_DelegatesToAuthFailedPath` | Service is Ready; credential refresh throws `TokenResponseException` | Availability transitions to `AuthFailed`; `AuthStatusChanged` fires; no new exception path |
| TC-5 | `RunProactiveRefreshAsync_WhenReady_OtherException_LogsWarningNoStateChange` | Service is Ready; credential refresh throws a generic exception | Warning logged; availability unchanged; exception not propagated |
| TC-6 | `CheckStaleness_WhenMarkerExceedsThreshold_FiresTokenExpiryApproaching` | Staleness marker age exceeds configured threshold | `TokenExpiryApproaching` event fires |
| TC-7 | `CheckStaleness_WhenMarkerWithinThreshold_DoesNotFire` | Staleness marker age is within configured threshold | `TokenExpiryApproaching` event does not fire |
| TC-8 | `CheckStaleness_WhenNoMarker_DoesNotFire` | No staleness marker stored | `TokenExpiryApproaching` event does not fire |
| TC-9 | `InitializeAsync_WhenMarkerExceedsThreshold_FiresTokenExpiryApproaching` | Init completes (any outcome); marker is stale | `TokenExpiryApproaching` fires regardless of availability state |
| TC-10 | `InitializeAsync_WhenMarkerWithinThreshold_DoesNotFire` | Init completes; marker is fresh | `TokenExpiryApproaching` does not fire |
| TC-11 | `InitializeAsync_WhenNoMarker_DoesNotFire` | Init completes; no marker present | `TokenExpiryApproaching` does not fire |
| TC-12 | `RunOAuthSetupAsync_OnSuccess_WritesStalenessMaker` | Re-consent completes successfully | Staleness marker written with approximately-current UTC time |
| TC-13 | `RunProactiveRefreshAsync_AfterRefresh_RunsStalenessCheck` | Service is Ready; refresh completes; marker is stale | `TokenExpiryApproaching` fires (staleness check ran after refresh) |

Note: TC-1 through TC-13 require the staleness persistence abstraction to be injectable (mocked). The delivery phase determines the exact injection mechanism (constructor parameter, DI registration, etc.).

For TC-2, TC-3: the credential object must be accessible and replaceable in tests. The delivery phase determines the seam (e.g., an injectable credential factory, a testable credential wrapper, or a virtual method). The spec does not prescribe the mechanism.

---

## Acceptance Criteria

| AC | Criterion |
|---|---|
| AC-1 | `IYouTubeLiveStreamService` declares `RunProactiveRefreshAsync` |
| AC-2 | `MockYouTubeLiveStreamService` implements `RunProactiveRefreshAsync` as a no-op |
| AC-3 | `YouTubeLiveStreamService` implements all three capabilities: proactive refresh, staleness check, re-consent marker update |
| AC-4 | Staleness persistence abstraction is injectable (enables mocking in tests) |
| AC-5 | `YouTubeOptions` adds staleness threshold (default 5 days) and proactive refresh interval (default 6 hours) |
| AC-6 | TC-1 through TC-13 are runnable (not `[Ignore]`) and pass |
| AC-7 | All existing tests continue to pass (no regressions) |
| AC-8 | Solution builds with zero warnings |

---

## Documentation Updates

None required beyond the spec itself. `YouTubeOptions` property additions are self-documenting via XML doc comments.

---

## Review History

| Round | Tier | Panel | Findings | Dispositions | Outcome | Notes |
|---|---|---|---|---|---|---|
