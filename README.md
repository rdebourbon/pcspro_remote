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
