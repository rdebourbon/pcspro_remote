# PCS Remote

A Blazor Server web application and ASP.NET Core backend that remotely controls PlayCricket Scorer Pro (cricket.exe) for High Halstow Cricket Club's garage PC — enabling match selection and scoreboard broadcast from any device on the local network.

---

## Architecture Overview

The solution is composed of five runtime components:

| Project | Role |
|---|---|
| `PcsRemote.Web` | Blazor Server web application — hosts the remote control UI and SignalR hub |
| `PcsRemote.Core` | Domain model — state machine, service contracts, shared types; zero external dependencies |
| `PcsRemote.Automation` | FlaUI-based automation service — drives cricket.exe on the Windows host |
| `PcsRemote.Automation.Mock` | In-process mock of the automation service — enables UI development without cricket.exe |
| `PcsRemote.TrayHost` | Windows Forms system tray host — provides manual override toggle and session management |

Test projects mirror the production structure under `tests/`.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows OS (the automation and tray host components require Win32 session access)
- Visual Studio 2022, JetBrains Rider, or VS Code with C# Dev Kit

---

## How to Build

```bash
dotnet build PCS_Remote.sln
```

---

## How to Run

```bash
dotnet run --project src/PcsRemote.Web
```

The web application will be available at `https://localhost:5001` (or the port shown in the console output).

---

## Local Development Testing (Mock Mode)

`appsettings.Development.json` enables mock mode when running via `dotnet run` — no cricket.exe required. The mock automation service simulates the full PCS Pro state machine with configurable delays and fake match data.

### Start the full application (recommended)

```powershell
dotnet run --project src/PcsRemote.TrayHost
```

This starts the Blazor web server **and** the WinForms tray icon (manual mode toggle, Open Browser, Exit). Browse to `http://localhost:5000`.

### Web server only (faster for Blazor component dev)

```powershell
dotnet run --project src/PcsRemote.Web
```

No tray icon. Manual mode can still be toggled via the browser UI.

### What mock mode exercises

| Scenario | How to trigger |
|---|---|
| Full launch flow (NotRunning → MatchLoaded) | Auto-starts on app launch |
| Match list (3 fake clubs) | Appears automatically at MatchSelectionReady |
| Auto-select (single match) | Set `FakeMatchCount: 1` in `appsettings.Development.json` |
| Manual match selection | Click a match card |
| Scoreboard preview + Refresh + Change Match | Available once a match is loaded |
| Manual mode toggle | Right-click tray icon → "Switch to Manual Mode" |
| Error injection + Retry | Set `ErrorProbability: 0.3` or `ForcedErrorMode` in config |

### Configuring mock delays

Edit `PcsPro:Mock:*` values in `appsettings.Development.json` to speed up or slow down each transition. Current defaults give roughly 1–1.5 s per state step so UI changes are clearly observable.

### Post-deployment smoke test

See [`docs/guides/Smoke-Test-Checklist.md`](docs/guides/Smoke-Test-Checklist.md) for the full acceptance checklist. Items 1–5, 10, and 11 are exercisable in mock mode. Items 6–9, 12–13 require cricket.exe on the garage PC.

---

## How to Test

```bash
dotnet test PCS_Remote.sln
```

---

## Project Structure

```
PCS_Remote/
├── src/
│   ├── PcsRemote.Web/            # Blazor Server web application
│   ├── PcsRemote.Core/           # Domain model and service contracts
│   ├── PcsRemote.Automation/     # FlaUI Windows automation service
│   ├── PcsRemote.Automation.Mock/# Mock automation service for development
│   └── PcsRemote.TrayHost/       # Windows Forms system tray host
├── tests/
│   ├── PcsRemote.Core.Tests/     # Unit tests for domain model
│   ├── PcsRemote.Web.Tests/      # bUnit component tests for the web app
│   └── PcsRemote.E2E.Tests/      # Playwright end-to-end tests
├── docs/
│   └── PCS-Remote/               # Architecture and planning documents
│       ├── PROJECT-CONTEXT.md    # Tech stack, constraints, assumptions
│       └── HLPS-001-Foundation/  # Foundation HLPS, IS, and JIT specs
├── .editorconfig                 # Code style enforcement
├── Directory.Build.props         # Shared MSBuild properties
└── PCS_Remote.sln
```

---

## Further Reading

- [PROJECT-CONTEXT.md](docs/PCS-Remote/PROJECT-CONTEXT.md) — tech stack decisions, architectural constraints, and project assumptions
