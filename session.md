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

### Unknowns Register (Draft)
| ID | Description | Owner | Blocking |
|---|---|---|---|
| U1 | Match "in progress" detection — PlayCricket API, scoreboard inference, or manual? | User | **No** (deferred — manual Start Stream covers all cases) |
| U2 | Does PCS Pro need YouTube broadcast to exist before RTMP starts? | User | Yes (verify on garage PC) |
| U3 | YouTube "upcoming" broadcast — does it show a waiting room for viewers? | Agent | No (research task) |

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
