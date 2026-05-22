# HLPS-017 — Streaming Lifecycle & Live Dashboard

| Field | Value |
|---|---|
| **Document** | HLPS-017-Streaming-Lifecycle-Live-Dashboard.md |
| **Type** | High-Level Problem Statement |
| **Status** | DRAFT — Pending user approval (v0.2) |
| **Autopilot** | DISABLED |
| **Date** | 2026-06-02 |
| **Author** | Copilot (GitHub Copilot CLI) |
| **Standards Loaded** | `sefe-dev/dev-standards/dotnet-standards.md`, `copilot-instructions.md` |
| **Dependencies** | HLPS-008 (YouTube broadcast lifecycle), HLPS-009 (PCS Pro streaming automation), HLPS-016 (operator-in-control principle) |

---

## 1. Background & Problem Statement

HLPS-008 and HLPS-009 jointly delivered a single "Start Stream / Stop Stream" button pair that hides the full YouTube broadcast lifecycle behind one action: Start creates a broadcast, starts PCS Pro RTMP, and transitions YouTube to live — all atomically. Stop transitions the broadcast to complete and stops PCS Pro RTMP.

This monolithic design has two practical problems for match-day operations:

**Problem 1 — No pre-match setup window.**  
The operator cannot create and configure the YouTube broadcast until they also want to push PCS Pro to start streaming. In practice, the operator wants to create the broadcast with the correct title and privacy settings *before* the match starts — so that the YouTube URL is shareable in advance and the broadcast appears in the club's channel schedule — then start the video feed separately when the first ball is actually bowled. Today this requires manual YouTube Studio work.

**Problem 2 — No live visibility.**  
Once streaming starts there is no feedback in the web UI about how many people are watching, whether the broadcast is healthy, or whether YouTube has unexpectedly terminated the broadcast underneath PCS Pro. PCS Pro silently continues pushing RTMP to a dead endpoint after YouTube ends the broadcast, and the operator has no way to detect this without switching screens to YouTube Studio.

This HLPS adds:

1. A decomposed broadcast lifecycle — five discrete, operator-controlled steps:

   ```
   Create Broadcast → upcoming → Start Streaming → live → Stop Streaming → StreamStopped
                                                                  ↕ (Resume Streaming while within YouTube timeout)
                                                          Close Broadcast → complete
   ```

2. A live dashboard displayed while streaming, showing concurrent viewers, stream health signals, and lifecycle status sourced from the YouTube Data API.
3. A background watchdog that detects when YouTube ends a broadcast unexpectedly and surfaces a prominent alert — without taking any automated recovery action (consistent with the operator-in-control principle from HLPS-016).

---

## 2. Goals

| ID | Goal |
|---|---|
| G-1 | Decompose the monolithic Start Stream action into five explicit, operator-controlled steps: **Create Broadcast**, **Start Streaming**, **Stop Streaming**, **Resume Streaming**, and **Close Broadcast**. Each step is a distinct UI action with clear preconditions and observable state. |
| G-2 | When creating a broadcast, explicitly set `embeddable: true` and `selfDeclaredMadeForKids: false` on the underlying video resource to prevent YouTube defaulting these to values that disable chat, embeds, and notifications. |
| G-3 | While streaming, display a live dashboard in the web UI showing concurrent viewer count, lifecycle status, and any health warnings sourced from the YouTube Data API. |
| G-4 | Detect when YouTube unexpectedly terminates a broadcast (timeout, upstream error, or operator action in YouTube Studio) while PCS Remote believes streaming is active, and surface a prominent alert. Do not take any automated recovery action. |

---

## 3. Non-Goals

| ID | Non-Goal |
|---|---|
| NG-1 | Auto-starting the broadcast or video feed on match transition. Manual operator action is always required to initiate streaming — consistent with the operator-in-control principle (HLPS-016) and explicitly deferred in HLPS-021 (NG-1). |
| NG-2 | Automatic recovery after YouTube terminates a broadcast. If YouTube ends the broadcast, the operator decides whether to create a new one. PCS Remote surfaces the problem; it does not solve it autonomously. |
| NG-3 | OBS WebSocket integration or any change to the video capture pipeline. PCS Pro remains the sole RTMP source. |
| NG-4 | Match Centre embed (GAP-019). That is a separate future capability and is not scoped here. |
| NG-5 | VOD chapters (GAP-018). Deferred. |
| NG-6 | Commentary or event publishing to YouTube Live Chat. Deferred to a separate HLPS. |
| NG-7 | Multiple simultaneous streams or multi-account YouTube support. Single-stream, single-account only. |
| NG-8 | Scheduled broadcasts with a future `scheduledStartTime`. Broadcasts are created for immediate use only. |

---

## 4. Success Criteria

| ID | Criterion |
|---|---|
| SC-1 | When the system is in `MatchLoaded` state, `LiveStreamStatus` is `Idle`, and `YouTube:LiveStreamId` is configured and valid, the web UI shows a **Create Broadcast** button. Clicking it creates a YouTube broadcast in the `upcoming` lifecycle state with `enableAutoStart: false` and `enableAutoStop: false`, using the configured title template and privacy setting, binds it to the reusable stream key (`YouTube:LiveStreamId`), and sets `embeddable: true` and `selfDeclaredMadeForKids: false` on the underlying video resource via a follow-up `videos.update` call. All three operations (insert, bind, update) are treated atomically: if any step fails, all prior steps are rolled back (broadcast deleted if inserted, bind released if applied) and status returns to `Idle` with an error annotation. After creation, the UI transitions to `BroadcastCreated` and the button is replaced by **Start Streaming**. If `YouTube:LiveStreamId` is absent or fails validation at startup, the Create Broadcast button is permanently disabled with a tooltip explaining the missing configuration. |
| SC-2 | The operator can cancel the Create Broadcast operation while it is in progress. Rollback semantics are defined per stage: if `liveBroadcasts.insert` succeeded before cancellation, the broadcast is deleted via `liveBroadcasts.delete` before returning to `Idle`; if deletion itself fails (quota exceeded, network error), the orphaned broadcast ID is logged and the operator is shown a warning to delete it manually in YouTube Studio. If creation fails unrecoverably, the same rollback applies. Status returns to `Idle` in all cases. |
| SC-3 | When status is `BroadcastCreated`, clicking **Start Streaming** calls `PcsProAutomationService.StartStreamingAsync()`, then polls for RTMP ingestion readiness, then transitions the YouTube broadcast to `live`. After a successful transition the status becomes `Live`. |
| SC-4 | *(Conditional on U1 resolution — see §6.)* When status is `Live`, clicking **Stop Streaming** calls `PcsProAutomationService.StopStreamingAsync()` and transitions the service status to `StreamStopped`. No YouTube lifecycle API call is made at this point — the broadcast lifecycle state on YouTube's side is expected to remain `live` while RTMP is absent (see U2). The operator is shown a prominent warning indicating that YouTube may auto-close the broadcast if RTMP is not resumed within its timeout window. If U1 resolves unfavourably (timeout too short to be operationally useful), SC-4 and SC-6 will be redesigned before the Stop Streaming step enters the delivery loop. |
| SC-5 | When status is `StreamStopped`, clicking **Close Broadcast** transitions the YouTube broadcast to `complete` and returns the service status to `Idle`. |
| SC-6 | *(Conditional on U1 and U2 resolution — see §6.)* When status is `StreamStopped`, clicking **Resume Streaming** calls `PcsProAutomationService.StartStreamingAsync()` and polls for RTMP ingestion readiness. No `liveBroadcasts.transition` call is expected to be needed because the broadcast lifecycle state is expected to remain `live` during RTMP absence (see U2). After RTMP readiness is confirmed, status returns to `Live`. When status is `StreamStopped`, the UI shows both **Resume Streaming** (primary action) and **Close Broadcast** (secondary/destructive action) simultaneously. |
| SC-7 | While status is `Live`, the web UI displays a live dashboard showing: concurrent viewer count, cumulative view count, local service lifecycle status, and stream health state. The dashboard updates on a configurable polling interval (default 30 seconds). Dashboard data is sourced from `videos.list` (`part=liveStreamingDetails,statistics`): concurrent viewers from `liveStreamingDetails.concurrentViewers`, cumulative views from `statistics.viewCount`, and stream health from `liveStreamingDetails` (e.g., "noData", "bad", "poor" health states are shown as health warnings). "Lifecycle status" on the dashboard refers to the local PCS Remote `LiveStreamStatus` value — not a separate YouTube API field. |
| SC-8 | A background watchdog runs whenever the service status is `Live` or `StreamStopped`. The watchdog polls `liveBroadcasts.list` on a configurable interval (default 30 seconds). To avoid false positives, the watchdog suppresses alerts during and for a configurable grace period (default 10 seconds) after any operator-initiated lifecycle transition (e.g., Close Broadcast calling `transition('complete')` — the watchdog must not re-alert on the same transition it just triggered). If the watchdog observes that the YouTube broadcast has transitioned to `complete`, `revoked`, or `liveStreamingError` outside of an operator-initiated transition window, it surfaces a prominent alert in the web UI, transitions the service to an `UnexpectedEnd` warning state, and does NOT call `StopStreamingAsync` or any other automated action. The operator dismisses the alert and decides next steps (see SC-13). |
| SC-9 | The existing HLPS-008 behaviour of auto-stopping when PCS Pro enters `Error` state is updated for the new lifecycle: `PcsProAutomationService.StopStreamingAsync()` is called, but the YouTube broadcast is **not** closed — no `liveBroadcasts.transition` call is made. Service transitions to `StreamStopped` with an error annotation so the operator can see why streaming was interrupted. The broadcast remains open for the operator to choose between Resume Streaming (if PCS Pro recovers) or Close Broadcast (to end the broadcast). If the operator subsequently confirms a Change Match action (SC-10), standard cleanup applies to close the broadcast. |
| SC-10 | `ChangeMatchButton.razor` retains its existing guard: if the service is not in `Idle`, the confirmation dialog includes an additional warning. The per-state cleanup on confirmation is: **`BroadcastCreated`** → delete the broadcast via `liveBroadcasts.delete` and return to `Idle`; **`Live`** → call `PcsProAutomationService.StopStreamingAsync()` then `liveBroadcasts.transition('complete')` then return to `Idle`; **`StreamStopped`** → call `liveBroadcasts.transition('complete')` then return to `Idle`; **`UnexpectedEnd`** → call `PcsProAutomationService.StopStreamingAsync()` (idempotent) then return to `Idle` (broadcast is already closed by YouTube). In all cases, `ChangeMatchAsync` is called only after the streaming service has returned to `Idle`. |
| SC-11 | All status changes are propagated to all connected browser circuits via the existing Blazor event pattern (no new SignalR hub required). |
| SC-12 | On service startup, the service queries `liveBroadcasts.list(mine=true)` to reconcile in-progress state. If a broadcast bound to `YouTube:LiveStreamId` is found in `live` or `liveStarting` state, status is restored to `Live` (existing HLPS-008 behaviour). If a broadcast is found in `upcoming` state, status is restored to `BroadcastCreated`. If multiple such broadcasts are found in either state, the service logs an error, surfaces a warning to the operator, and remains in `Idle` pending manual cleanup in YouTube Studio. |
| SC-13 | When status is `UnexpectedEnd`, the web UI shows a **Dismiss** button. Clicking it calls `PcsProAutomationService.StopStreamingAsync()` (idempotent — safe to call even if PCS Pro is already stopped) and returns service status to `Idle`. No YouTube API call is made (the broadcast is already closed). |
| SC-14 | Dashboard polling (SC-7) and watchdog polling (SC-8) are suspended while any lifecycle transition is in flight (i.e., while the service status is `Starting`, or during Create Broadcast, Stop Streaming, or Resume Streaming operations). Polling resumes only after status stabilises to a steady state. This ensures the single-writer invariant: only one execution path mutates YouTube broadcast state at a time. |

---

## 5. Constraints

| ID | Constraint | Rationale |
|---|---|---|
| C-1 | `PcsRemote.Core` must remain dependency-free. | Architecture rule: Core has zero external dependencies. |
| C-2 | No new OAuth scopes may be introduced. The existing `youtube` scope already covers all required YouTube Data API v3 operations. | Avoids re-consent for existing deployments. |
| C-3 | Automated recovery actions (re-creating a broadcast, restarting PCS Pro streaming) are prohibited. | Operator-in-control principle (HLPS-016). |
| C-4 | The live dashboard polling and watchdog polling must not overlap with or duplicate the broadcast creation / lifecycle transition calls. Polling is suspended during in-flight transitions (observable requirement: SC-14). | Single-writer principle — one execution path mutates YouTube broadcast state at a time. |
| C-5 | All YouTube API calls must go through `IYouTubeLiveStreamService`. No Blazor component or hosted service may call the YouTube API directly. | Architectural consistency. |
| C-6 | The new broadcast lifecycle states must be additive changes to the existing `LiveStreamStatus` enum. Existing `Idle`, `Starting`, `Live`, and `Error` state handling in `StreamingControls.razor` and other consumers must not regress. | Backward compatibility and delivery safety. |

---

## 6. Unknowns Register

| ID | Description | Owner | Blocking |
|---|---|---|---|
| U1 | **YouTube RTMP absence timeout.** After PCS Pro stops pushing RTMP (Stop Streaming), how long does YouTube allow the stream to be silent before it automatically transitions the broadcast to `complete` or `revoked`? This determines the viability of the Stop/Resume cycle and the wording of the operator warning shown after SC-4. If the timeout is very short (<2 minutes) the Stop Streaming action may not be useful in practice and the UI design should reflect that. | User / Agent (empirical test or official docs) | **Blocking** — for the Stop Streaming and Resume Streaming steps (SC-4, SC-6). Non-blocking for Create Broadcast and Start Streaming steps. |
| U2 | **YouTube broadcast lifecycle state while RTMP is absent.** Does the YouTube broadcast `lifeCycleStatus` remain `live` when PCS Pro stops pushing RTMP, or does YouTube immediately transition it to a different state (e.g., `testing`, `stopped`, or immediately begin the timeout countdown to `complete`)? SC-6 (Resume Streaming) assumes the broadcast stays in `live` and no `liveBroadcasts.transition` call is needed to resume — this assumption must be verified empirically or from YouTube API documentation. | Agent (empirical test or docs) | **Blocking** — for Resume Streaming (SC-6). If the broadcast does not stay `live`, the Resume Streaming API flow must be redesigned. |
| U3 | **YouTube "upcoming" waiting room.** Does a bound-but-not-yet-live broadcast in `upcoming` state show a viewer waiting room or a channel-default placeholder? | Agent (research) | **Non-Blocking** — informs the operator-facing description but does not affect implementation. |

---

## 7. Accepted Risks

| ID | Risk | Mitigation |
|---|---|---|
| AR-1 | YouTube Data API quota consumption will increase: live dashboard polling (SC-7) and watchdog polling (SC-8) both make API calls on a 30-second interval. Default quota is 10,000 units/day. `videos.list` costs 1 unit; `liveBroadcasts.list` costs 1 unit. At 30-second polling across both calls: ~5,760 units/day for a 24-hour stream (unlikely in practice). A 3-hour match uses ~720 units. Quota headroom is sufficient. | Configurable polling intervals. If quota pressure is observed in production, the operator can increase the interval. |
| AR-2 | The `Stop Streaming → Resume Streaming` cycle depends on U1 being resolved favourably. If YouTube's timeout is shorter than the typical interruption a club operator would need, the Stop/Resume path may provide false hope. | U1 is marked blocking. If the timeout makes resume impractical, SC-4 and SC-6 will be redesigned before the Stop Streaming step enters the delivery loop. |

---

## 8. Technical Direction Notes (Advisory — Not Requirements)

**TDN-1: New `LiveStreamStatus` values**
The existing enum likely needs: `BroadcastCreated`, `StreamStopped`, `UnexpectedEnd`. The `Starting` state from HLPS-008 covers the "starting" phase of Create Broadcast too. Downstream IS and JIT Spec authors should confirm the exact states and transitions during the IS design phase.

**TDN-2: Create Broadcast = two YouTube API calls**
`liveBroadcasts.insert` creates the broadcast resource. The broadcast `id` returned *is* the video id. A follow-up `videos.update` (`part=status`) on the same id sets `status.embeddable: true` and `status.selfDeclaredMadeForKids: false`. Both calls use the same OAuth scope. This is a known API gap in the YouTube Data API v3: `embeddable` and `selfDeclaredMadeForKids` are not settable via `liveBroadcasts.insert`.

**TDN-3: Dashboard data source**
`videos.list?part=liveStreamingDetails,statistics&id={broadcastId}` returns concurrent viewers (`liveStreamingDetails.concurrentViewers`), cumulative views (`statistics.viewCount`), and like count (`statistics.likeCount`) in one call. This is more efficient than separate `liveBroadcasts.list` and `videos.list` calls.

**TDN-4: Watchdog separation**
The watchdog (SC-8) and the live dashboard poller (SC-7) can share the same scheduled timer but should use separate API calls to keep their concerns independent. The watchdog calls `liveBroadcasts.list` (lifecycle state); the dashboard calls `videos.list` (viewer stats). Combining them into a single call is tempting but conflates availability signals with engagement signals.

**TDN-5: `EnableAutoStart: false`**
When creating the broadcast via `liveBroadcasts.insert`, set `enableAutoStart: false`. This ensures YouTube never auto-transitions to live — the operator's explicit Start Streaming action is the only trigger.

**TDN-6: Broadcast bind**
`liveBroadcasts.bind?id={broadcastId}&streamId={YouTube:LiveStreamId}` links the broadcast to the club's reusable stream key. This is already implemented in HLPS-008 and should be called during Create Broadcast (not Start Streaming), so that the stream binding is in place before PCS Pro is asked to push RTMP.

---

## 9. Review History

| Round | Reviewer | Result | Date |
|---|---|---|---|
| R1 | Claude Opus 4.5 (adversarial) | REVISION REQUIRED — 3×HIGH, 4×MEDIUM, 3×LOW | 2026-06-02 |
| R1 | GPT-5.2 (adversarial) | REVISION REQUIRED — 5×HIGH, 4×MEDIUM, 2×LOW | 2026-06-02 |
| R1 fixes | Copilot | Applied — v0.2 | 2026-06-02 |

**R1 key fixes applied (v0.1 → v0.2):**
- Background and G-1 updated: "four steps" → "five steps"; Resume Streaming added as first-class step with lifecycle diagram
- NG-1 citation corrected: HLPS-021 G-1 → HLPS-016 / HLPS-021 NG-1
- SC-1 hardened: `enableAutoStart: false`, `enableAutoStop: false`; `YouTube:LiveStreamId` precondition; three-call atomic semantics stated
- SC-2 rollback semantics specified: stage-by-stage rollback with `liveBroadcasts.delete`; orphaned broadcast handling documented
- SC-4 and SC-6 gated on U1/U2 resolution with explicit conditional language
- SC-6 corrected: Resume does not call `liveBroadcasts.transition`; button priority (primary/secondary) specified
- SC-7 clarified: "lifecycle status" = local service state; health warnings from `liveStreamingDetails` defined
- SC-8 hardened: watchdog suppression grace period during operator transitions defined
- SC-9 expanded: clarified no YouTube API call; SC-9/SC-10 interaction made explicit
- SC-10 expanded: per-state cleanup steps (delete vs complete vs idempotent stop) fully specified
- SC-12 added: startup reconciliation for `upcoming` broadcasts
- SC-13 added: `UnexpectedEnd` dismissal → `Idle`
- SC-14 added: polling suspension during in-flight transitions (makes C-4 testable)
- C-4 updated to reference SC-14
- Unknowns: Added U2 (YouTube broadcast lifecycle state while RTMP absent) as Blocking; old U2 → U3
