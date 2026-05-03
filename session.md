# PCS Remote — Session Resume Info

## Session State
- **Session ID**: `368db05a-6325-499a-abe0-3ea5630a0516`
- **Session folder**: `C:\Users\rdebourb\.copilot\session-state\368db05a-6325-499a-abe0-3ea5630a0516\`
- **Plan file**: `C:\Users\rdebourb\.copilot\session-state\368db05a-6325-499a-abe0-3ea5630a0516\plan.md`

## Where We Are

| Phase | Status |
|---|---|
| Phase 1 (IS-001 – IS-007) | ✅ COMPLETE |
| Phase 1 bug fixes (CSS, theme, dialog) | ✅ COMPLETE |
| HLPS-008 (YouTube Live Streaming) | ✅ COMPLETE (all S-001 – S-007 delivered) |
| HLPS-013 (Team Names & Club Branding) | ✅ COMPLETE |
| HLPS-014 (WiX MSI Installer) | ✅ COMPLETE |
| HLPS-016 (Operator Mode & Resilience) | ✅ COMPLETE |

## HLPS-016 Delivery Summary

All 9 steps delivered and merged to `master`:

| Step | Description | Status |
|---|---|---|
| S-001 | ManualModeService extension + relocation | ✅ Done |
| S-002 | Demote UnexpectedDialog to warning | ✅ Done |
| S-003 | Dialog detection hardening (whitelist, popup filter, hysteresis) | ✅ Done |
| S-004 | Scoreboard interval 2s → 10s | ✅ Done |
| S-005 | BringToForeground preserve placement | ✅ Done |
| S-006 | Manual-mode backend wiring | ✅ Done |
| S-007 | Blazor UI manual-mode enhancements | ✅ Done |
| S-008 | Global Ctrl+Alt+P hotkey | ✅ Done |
| S-009 | Documentation updates | ✅ Done |

## Key Files
| File | Purpose |
|---|---|
| `docs/PCS-Remote/HLPS-016-Operator-Mode-And-Resilience/HLPS-016-Operator-Mode-And-Resilience.md` | HLPS document (APPROVED) |
| `docs/PCS-Remote/HLPS-016-Operator-Mode-And-Resilience/IS-016-Operator-Mode-And-Resilience.md` | Implementation Sequence (APPROVED) |
| `docs/PCS-Remote/HLPS-016-Operator-Mode-And-Resilience/SPEC-S-003-Dialog-Detection-Hardening.md` | S-003 SPEC (APPROVED v0.5) |
| `docs/PCS-Remote/DEFERRED-ITEMS.md` | Deferred items register |
| `docs/guides/Operational-Guide.md` | Operator guide (updated S-009) |
| `docs/guides/Configuration-Guide.md` | Configuration reference (updated S-009) |

## Brainstorming: HLPS-017 — Streaming Lifecycle & Live Dashboard

**Status:** Brainstorming (not yet formalized into HLPS)

### Core Idea
Decompose the current monolithic "Start Live Stream" into a proper broadcast lifecycle:

```
[Create Broadcast] → upcoming → [Start Stream] → live → [Stop Stream] → stopped → [Close Broadcast] → complete
```

### Proposed Features

| Feature | Priority | Notes |
|---------|----------|-------|
| **Create Broadcast** (upcoming state) | Core | YouTube API only, no PCS Pro automation |
| **Start Stream** (manual trigger) | Core | PCS Pro RTMP + YouTube transition to live |
| **Stop Stream** | Core | PCS Pro RTMP stop (broadcast stays open) |
| **Close Broadcast** | Core | YouTube transition to complete |
| **Live dashboard** (viewers, likes, status) | Core | Single `videos.list` call while live |
| **Match Centre embed** (GAP-019) | Optional | FlaUI menu automation, operator-triggered |
| **Autostart on match transition** | Deferred | Needs PlayCricket API or other signal |
| **VOD chapters** (GAP-018) | Deferred | Needs PCS Pro CSV format verification |

### Key Design Decisions (Pending)
- Decomposed lifecycle gives operator full control — works for both local/friendly and league matches
- `EnableAutoStart: false` on create → manual transition to live when operator clicks Start Stream
- Autostart-on-match-transition deferred (U1 dropped from Blocking to Deferred) — manual Start Stream covers all cases
- Live dashboard: `videos.list` with `part=liveStreamingDetails,statistics` returns concurrent viewers, likes, lifecycle status
- Match Centre embed: via PCS Pro menu option (not the startup dialog) — avoids sequencing issues
- **Create Broadcast = two API calls, not one.** `liveBroadcasts.insert` does not expose `embeddable` (it lives on the underlying `videos` resource). Follow up with `videos.update` `part=status` to set both:
  - `status.embeddable: true` — explicit, so GAP-019 Match Centre embed isn't at the mercy of channel default toggles
  - `status.selfDeclaredMadeForKids: false` — explicit declaration; avoids YouTube defaulting it and disabling features (chat, embeds, notifications) on us
  - The broadcast `id` returned by `liveBroadcasts.insert` *is* the video id — no `boundStreamId` lookup needed for this update.
  - Both calls use the same `youtube` OAuth scope already required for the lifecycle.

### Open Design Questions
| ID | Question | Notes |
|---|---|---|
| Q2 | How do we handle the YouTube stop-then-resume timeout window? | PCS Pro's Start/Stop is a single toggling button (already automated). RTMP can be stopped and restarted, but **YouTube imposes its own timeout** — if RTMP is absent for too long YouTube auto-closes the broadcast and the next ingest creates a new video. Implications: (a) "Stop Stream" in our UI is semantically close to "End broadcast" — can't model it as an indefinite pause; (b) need to either nail down the exact timeout from YouTube docs or empirical test, then surface it to the operator (countdown / "you have N seconds to resume before YouTube ends this broadcast"); (c) auto-recovery after a network blip must complete inside that window or we have to issue a fresh `liveBroadcasts.insert`. |
| Q1 |Commentary/event publishing strategy — chat-only, overlay-only, or both? And where does overlay rendering happen (PCS Pro built-in overlays vs. OBS as an intermediate layer)? | YouTube Live Chat **is** time-coded and replays correctly on the VOD (`liveChatMessages.insert` → server-side timestamp → "Live chat replay" track). Caveats: chat replay panel is collapsed by default on VODs, 2–5s latency, per-minute rate limits, messages post under the channel-owner identity (no "bot" label), and chat is disabled if `selfDeclaredMadeForKids: true` (another reason for our explicit `false`). For marquee events (wickets, 50s, partnerships) chat alone is too easy to miss — likely need both: chat post for searchability + VOD replay archive (every event), plus on-screen overlay for in-the-moment visibility (selective). Overlay options: (a) PCS Pro built-in overlays driven via FlaUI if it supports text injection, or (b) introduce OBS as an intermediate RTMP layer for full ticker/lower-thirds flexibility (much bigger change to the stack). |

### Unknowns Register (Draft)
| ID | Description | Owner | Blocking |
|---|---|---|---|
| U1 | Match "in progress" detection — PlayCricket API, scoreboard inference, or manual? | User | **No** (deferred — manual Start Stream covers all cases) |
| U2 | Does PCS Pro need YouTube broadcast to exist before RTMP starts? | User | **Resolved** — Yes. RTMP ingests into a YouTube *stream resource* (stream key), but a *broadcast* must exist and be **bound to that stream** for viewers to see anything. Lifecycle order: Create Broadcast + bind stream → Start RTMP from PCS Pro → Transition broadcast to live. |
| U3 | YouTube "upcoming" broadcast — does it show a waiting room for viewers? | Agent | No (research task) |
| U4 | Does PCS Pro detect when YouTube ends the broadcast underneath it? | User | **Resolved** — No. PCS Pro silently keeps pumping RTMP into a dead endpoint. **Implication:** PCS Remote needs a watchdog polling `liveBroadcasts.list` `status.lifeCycleStatus` while we believe we're streaming. If YouTube reports `complete`/`revoked`/`liveStreamingError`, surface a prominent error on the dashboard ("🔴 YouTube ended the broadcast at HH:MM:SS — click Stop in PCS Pro"). **Do not auto-stop** — operator-in-control principle from HLPS-016. Orphaned RTMP costs only bandwidth; surprising the operator with a self-pressing Stop button costs trust. |

### YouTube API Data Available During Livestream
| Data | Source | Usefulness |
|---|---|---|
| Concurrent viewers | `liveStreamingDetails.concurrentViewers` | High — live count while broadcasting |
| Total view count | `statistics.viewCount` | High — cumulative |
| Like count | `statistics.likeCount` | Medium — real-time |
| Lifecycle status | `liveBroadcasts.status.lifeCycleStatus` | High — independent health signal |
| Live chat ID | `liveStreamingDetails.activeLiveChatId` | Low (future) |
| Embeddable | `status.embeddable` | Low — guard for GAP-019 |

## Immediate Next Action
Resume brainstorming for HLPS-017, then formalize when user is ready.
