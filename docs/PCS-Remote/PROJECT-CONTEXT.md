# Project Context: PCS Remote — Phase 1

| Field | Value |
|---|---|
| **Document** | PROJECT-CONTEXT.md |
| **Type** | Shared reference — not an HLPS |
| **Version** | 1.1 |
| **Date** | 2026-04-10 |
| **Source PRD** | `docs/Requirements/PRD - Match Selection and Scoreboard.md` v1.0 |

> **Purpose**: This document captures shared context — technology stack, constraints, assumptions, and project structure — referenced by all HLPS documents. It is not a deliverable itself; it is a living reference maintained throughout Phase 1.

---

## 1. Background & Purpose

High Halstow Cricket Club (HHCC) operates a garage PC that automates setup of live cricket scoring and streaming using PlayCricket Scorer Pro (PCS Pro / NV Play ECB) and YouTube Live. The existing automation was built by a former volunteer using Python, AutoHotkey, and Selenium. It is fragile, unmaintainable, and poses a single-point-of-failure risk to the club's match-day operations.

**Phase 1** delivers a robust, reliable web-based control panel for match selection and scoreboard management — rebuilt from scratch in C#/.NET. Phase 2 (YouTube live streaming) is out of scope but must be accommodatable without architectural changes.

---

## 2. Architecture

```
Operator Browser (Chrome/Edge, local network)
        │ HTTP + WebSocket (Blazor Server circuit)
        ▼
ASP.NET Core + Blazor Server
  ├── Blazor UI Components (Radzen, real-time, interactive)
  ├── Singleton Services (state sync via events + InvokeAsync)
  ├── PCS Pro Automation Service
  │     ├── State Machine (Stateless NuGet)
  │     ├── Scoreboard Capture (FlaUI Capture.Rectangle, DPI-aware)
  │     └── FlaUI (UIA3 automation)
  ├── YouTube Live Stream Service (Data API v3, OAuth2)
  ├── System Tray Host (NotifyIcon, manual mode toggle)
  └── Configuration & Logging (Serilog)
        │ UIAutomation3 COM API
        ▼
PCS Pro (cricket.exe) — WPF Application
        │ COM5 (serial)
        ▼
LED Scoreboard
```

### Key Architectural Decisions

1. **Blazor Server (Interactive Server)**: All logic server-side; natively uses SignalR; ideal for local network real-time updates. Avoids CORS/API complexity.

2. **IPcsProAutomationService interface**: All PCS Pro interaction behind a clean interface. Mock implementation enables full development without PCS Pro. DI registration controlled by `PcsPro:UseMock` config flag.

3. **Stateless NuGet state machine**: Formal state machine prevents invalid transitions. States and transitions defined declaratively.

4. **FlaUI (UIA3)**: Modern UIAutomation library. Uses accessible properties (AutomationId, Name, ClassName) — never coordinates. WPF-native.

5. **FlaUI `Capture.Rectangle()`**: DPI-aware screen capture using FlaUI's built-in `Capture.Rectangle(bounds)` method. The process calls `SetProcessDPIAware()` at startup so UIA `BoundingRectangle` coordinates and screen capture use physical pixels consistently. Captures the `ReplayScreenPreview` element content (not the ToolWindow chrome) and encodes as JPEG.

6. **Blazor circuit-based state delivery**: All cross-circuit state synchronisation uses the standard Blazor Server pattern — singleton service events with `InvokeAsync(StateHasChanged)`. Scoreboard images, automation state, operation status, and manual mode are all distributed this way. The framework's built-in `/_blazor` SignalR connection handles UI diff pushes. No custom SignalR hub is needed (HLPS-012 removed the dead hub infrastructure).

7. **System tray hosting with manual mode**: Console app with NotifyIcon. Manual mode toggle pauses remote automation for local PCS Pro use. Auto-started via Task Scheduler.

---

## 3. Technology Stack

| Component | Technology | Version | Rationale |
|---|---|---|---|
| Runtime | .NET | 8 LTS | PRD specification; long-term support |
| Web Framework | ASP.NET Core + Blazor Server | 8.0 | Real-time by default; server-side automation access |
| Real-time | SignalR | (built into ASP.NET Core 8) | Native Blazor Server transport; no custom hub |
| UI Components | Radzen Blazor | Latest stable | Full component library with theming and layout; replaces Bootstrap |
| UI Automation | FlaUI | Latest stable | UIA3 — modern, robust, WPF-native |
| State Machine | Stateless | Latest stable | Lightweight, declarative state machine |
| Logging | Serilog | Latest stable | Structured logging; console + rolling file sinks |
| Testing (unit) | MSTest 3.x+ + FluentAssertions >8.0 | Latest stable | Modern MSTest with fluent assertions |
| Testing (component) | bUnit | Latest stable | Blazor component unit testing in isolation |
| Testing (E2E) | Playwright | Latest stable | Real-browser end-to-end testing |
| Configuration | .NET User Secrets (dev) / Env vars (prod) | — | Secure credential management |

---

## 4. Constraints

| ID | Constraint | Source |
|---|---|---|
| C-1 | Windows 10/11 only — PCS Pro is Windows-only WPF | PRD §14 |
| C-2 | .NET 8 LTS runtime | PRD §5 |
| C-3 | Desktop browser only (Chrome/Edge) — no mobile for v1 | PRD §2, §14 |
| C-4 | Maximum 2 concurrent browser sessions | PRD §14 |
| C-5 | PCS Pro credentials never in source control | PRD §13 |
| C-6 | PCS Pro read-only mode — no scoring data entry | PRD §2 |
| C-7 | Phase 2 (YouTube streaming) accommodatable without architectural changes | PRD §17 |
| C-8 | Zero coordinate-based clicking | PRD §14 |
| C-9 | All errors surface to UI with actionable messages | PRD §14 |
| C-10 | Serilog structured logging from first commit | PRD §5b |
| C-11 | State transition latency < 500ms | PRD §14 |
| C-12 | Scoreboard image latency ≤ 3 seconds | PRD §14 |
| C-13 | Must run in interactive desktop session (not Session 0) | UIAutomation limitation |
| C-14 | Single machine — web app and PCS Pro co-located on garage PC | Operational |

---

## 5. Assumptions Register

| ID | Assumption | Status |
|---|---|---|
| A-1 | **Single machine**: Web app and PCS Pro on the same garage PC. | ✅ Confirmed |
| A-2 | **Interactive session**: Console app with tray icon via Task Scheduler at logon. Not a Windows Service. | ✅ Confirmed |
| A-3 | **Local network only**: No internet exposure, VPN, or NAT. | ✅ Confirmed |
| A-4 | **No authentication**: Local trusted network — no web UI login. | ✅ Confirmed |
| A-5 | **PCS Pro pre-installed**: cricket.exe already on the garage PC. | ✅ Confirmed |
| A-6 | **Single PCS Pro instance**: One instance of cricket.exe at a time. | ✅ Confirmed |

---

## 6. Project Structure

```
PCS_Remote/
├── src/
│   ├── PcsRemote.Web/              # Blazor Server web application (host)
│   ├── PcsRemote.Core/             # Domain models, interfaces, state machine
│   ├── PcsRemote.Automation/       # FlaUI real implementation (garage PC only)
│   ├── PcsRemote.Automation.Mock/  # Mock implementation (dev/test)
│   └── PcsRemote.TrayHost/         # System tray host (NotifyIcon, manual mode)
├── tests/
│   ├── PcsRemote.Core.Tests/       # Unit tests (MSTest + FluentAssertions)
│   ├── PcsRemote.Web.Tests/        # Component tests (bUnit) + integration tests
│   └── PcsRemote.E2E.Tests/        # End-to-end browser tests (Playwright)
├── docs/
│   ├── Requirements/               # PRD and source requirements
│   └── PCS-Remote/                 # Planning artifacts (Context, HLPS, IS, Specs)
├── .gitignore
├── .editorconfig
├── README.md
├── .github/
│   └── copilot-instructions.md
└── PCS_Remote.sln
```

---

## 7. HLPS Roadmap

| # | HLPS | Problem Area | Dependencies | Status |
|---|---|---|---|---|
| 1 | HLPS-001-Foundation | Project scaffolding, domain model, state machine | None | ✅ Complete |
| 2 | HLPS-002-Mock-Service | Development without PCS Pro | HLPS-001 | ✅ Complete |
| 3 | HLPS-003-Web-Control-Panel | Real-time browser-based control panel | HLPS-002 | ✅ Complete |
| 4 | HLPS-004-Scoreboard | Live scoreboard preview and operator controls | HLPS-003 | ✅ Complete |
| 5 | HLPS-005-Hardening | System tray, manual mode, multi-user, errors | HLPS-003 | ✅ Complete |
| 6 | HLPS-006-FlaUI-Integration | Real PCS Pro automation | HLPS-001 | ✅ Complete |
| 7 | HLPS-007-Deployment | Task Scheduler, production readiness | HLPS-005, HLPS-006 | ✅ Complete |
| 8 | HLPS-008-YouTube-LiveStream | YouTube Data API v3, broadcast lifecycle | HLPS-003 | ✅ Complete |
| 9 | HLPS-009-PCSPro-Streaming-Automation | PCS Pro Start/Stop Live Stream FlaUI | HLPS-006, HLPS-008 | ✅ Complete |
| 10 | HLPS-010-Automation-Hardening | Diagnostic porting, health-check, use-current-match | HLPS-006 | ✅ Complete |
| 11 | HLPS-011-Operational-UX | Error dismissal, status banner, automation log, debug section | HLPS-003 | ✅ Complete |
| 12 | HLPS-012-Dead-Hub-Removal | Remove dead SignalR hub infrastructure | HLPS-011 | ✅ Complete |

---

## 8. Unknowns Register (Shared)

| ID | Description | Owner | Blocking? | Resolution | Status |
|---|---|---|---|---|---|
| U-1 | Blazor rendering mode | User | Yes | Blazor Server (Interactive Server) | ✅ Resolved |
| U-2 | .NET version | User | Yes | .NET 8 LTS | ✅ Resolved |
| U-3 | Testing framework | User | Yes | MSTest 3.x+ / FluentAssertions >8.0 | ✅ Resolved |
| U-4 | CSS/UI framework | User | Yes | Radzen Blazor (all-in, no Bootstrap) | ✅ Resolved |
| U-5 | Namespace convention | User | Yes | PcsRemote | ✅ Resolved |
| U-6 | CI/CD pipeline | User | No | Deferred | Deferred |
| U-7 | PCS Pro password ownership | User | No | Operational — deferred | Deferred |
| U-8 | Remote scorer connection | User | No | Operational — deferred | Deferred |

---

*Shared reference document for PCS Remote Phase 1. Updated 2026-04-21.*
