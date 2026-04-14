# IS-007: Deployment & Operations — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-007-Deployment.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-14 |
| **Governing HLPS** | HLPS-007-Deployment.md v0.5 (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Prerequisites** | IS-006 delivered (real FlaUI automation working, all 438 tests passing). |

---

## Overview

This sequence delivers production-ready deployment: a verified self-contained publish artefact, a PowerShell deployment script, a configuration guide, an operational guide for club volunteers, and a smoke test checklist covering all Phase 1 functional areas.

All deliverables are documentation and scripting — no production C# code changes. Steps are ordered so that the publish artefact is verified first (S-001), the deployment automation is scripted and tested second (S-002), and the human-facing documentation is written last (S-003 through S-005) so it accurately describes the final configuration.

Blocking unknown D-U-8 (PCS Pro installation directory) must be resolved before S-005 can be executed on the garage PC. All other unknowns are non-blocking and are addressed within their respective steps.

Steps are identified with stable IDs S-001 through S-005. IDs are never renumbered.

---

## Steps

### S-001 — Self-contained publish verification and helper script

**What changes:** Two deliverables:

1. **Publish verification**: Run `dotnet publish src/PcsRemote.TrayHost/PcsRemote.TrayHost.csproj -c Release -r win-x64 --self-contained` and capture the output artefact set. Confirm: (a) the command succeeds with zero errors; (b) `PcsRemote.TrayHost.exe` is present in the output directory; (c) `appsettings.json` and `appsettings.Development.json` are present as companion files; (d) no `.NET` runtime folder is required alongside the executable on a clean machine. If any `NU*` or `NETSDK*` warnings are emitted, they must be resolved before proceeding. Document the exact output artefact list (file names and approximate sizes) in a comment block at the top of the deployment script (S-002) so volunteers know what a complete artefact set looks like.

2. **Publish helper script**: Create `scripts/publish.ps1` at the repository root. The script runs the verified publish command, sets the output directory to `publish/` under the repo root (git-ignored), and prints the artefact list on completion. This makes it trivial for developers to rebuild the deployment artefact without memorising the full command.

**Why:** The deployment script (S-002) assumes a valid artefact set exists. Verifying the publish command first catches any configuration issues (missing RID, incompatible TFM, broken static assets) before documentation is written around them. The helper script eliminates the risk of a future developer using the wrong publish command. Addresses D-SC-1.

**Dependencies:** None within IS-007.

**Verification intent:** `dotnet publish src/PcsRemote.TrayHost/PcsRemote.TrayHost.csproj -c Release -r win-x64 --self-contained -o publish/` succeeds with zero errors. `publish/PcsRemote.TrayHost.exe` exists. `scripts/publish.ps1` runs cleanly and prints the artefact list. All 438 existing tests continue to pass (no production code changes were made).

---

### S-002 — PowerShell deployment script

**What changes:** Create `scripts/Deploy-PcsRemote.ps1`. The script:

1. **Elevation guard**: Checks that it is running as Administrator; exits with a clear error message if not (`#Requires -RunAsAdministrator`).
2. **Parameters**: Accepts `-DeployDir` (default `C:\PcsRemote\`) and `-AppUser` (the Windows account name to use for the Task Scheduler trigger — prompted if not supplied).
3. **Artefact copy**: Copies all files from the `publish/` artefact set to `$DeployDir`, preserving `appsettings.json` if it already exists (an existing file means a re-deployment — do not overwrite configuration). New files are copied in all cases.
4. **Task Scheduler (idempotent)**: Unregisters any existing task named `PcsRemote` before registering a new one, ensuring re-deployments are clean. Task definition:
   - Trigger: `ONLOGON` for the specified `-AppUser` account
   - Action: `$DeployDir\PcsRemote.TrayHost.exe`
   - Working directory (`Start in`): `$DeployDir`
   - Run only when user is logged on (interactive session)
   - On failure: restart after 30 seconds, up to 999 attempts
   - Settings: `ExecutionTimeLimit = PT0S` (no timeout), `MultipleInstances = IgnoreNew`
5. **Windows Firewall rule (idempotent)**: Removes any existing rule named `PcsRemote-HTTP` then creates a new inbound TCP rule for port 5000 (`New-NetFirewallRule`).
6. **PCS Pro password**: Prompts the operator interactively via `Read-Host -AsSecureString`; converts to plain text in memory only; sets the System-scoped environment variable `PcsPro__Password` via `[System.Environment]::SetEnvironmentVariable`. The password string is cleared from memory after the call. The script must not write any credential to disk.
7. **Summary**: Prints a deployment summary listing all actions taken and their outcomes.

**Why:** A single idempotent script that handles first-time deployment and re-deployment identically is the core mechanism for ensuring the system can be maintained by non-developers. Elevation, idempotency, and interactive password entry are all required by HLPS-007. Addresses D-SC-2, D-SC-3 (Task Scheduler), D-SC-6, D-SC-8 (Firewall).

**Dependencies:** S-001 (publish artefact set must exist in `publish/`).

**Verification intent:** Running the script from an elevated PowerShell prompt on the development machine (or a test VM) completes without errors. Re-running it a second time completes without errors (idempotency). Running it without elevation prints a clear error and exits. The Task Scheduler task appears in Task Scheduler with correct settings: correct user, `Start in`, restart settings. The Windows Firewall rule `PcsRemote-HTTP` appears inbound TCP port 5000. `[System.Environment]::GetEnvironmentVariable("PcsPro__Password", "Machine")` returns the entered password. No password text is visible in the script output or in any log file.

---

### S-003 — Configuration guide

**What changes:** Create `docs/guides/Configuration-Guide.md`. The guide covers:

- **Prerequisites**: .NET 8 runtime not required (self-contained); Windows 10/11; Administrator account for deployment script.
- **`appsettings.json` settings** (all production-relevant keys):
  - `PcsPro:AutoLaunch` — boolean; if `true`, PCS Pro is launched automatically when the application starts; set to `false` for manual-launch mode.
  - `PcsPro:ExecutablePath` — full path to `cricket.exe` (e.g., `C:\Program Files (x86)\PCS Pro\cricket.exe`); required for real mode.
  - `PcsPro:WorkingDirectory` — working directory for cricket.exe process (typically the same folder as the executable).
  - `PcsPro:UseMock` — boolean; `false` for production (real PCS Pro automation); `true` for testing with the mock service. Note: this is a top-level key distinct from the `PcsPro:Mock` subsection (development/test parameters not relevant to production).
  - `Kestrel:Endpoints:Http:Url` — bind address and port (default `http://0.0.0.0:5000`); change the port number here if 5000 conflicts with another application.
  - `Scoreboard:JpegQuality` — JPEG compression quality for scoreboard images (1–100; default suitable for LAN use).
  - `Logging:LogLevel:Default` — log verbosity (default `Information`; use `Debug` only for troubleshooting as it increases file size significantly).
  - `AllowedHosts` — host header filtering (default `"*"` is correct for LAN deployment; do not restrict without understanding the implications).
  - Note on `PcsPro:Mock` subsection — development/test delay and probability settings; leave unchanged in production.
  - Note on `appsettings.Development.json` — this companion file is present in the deployment directory but is never loaded in production; do not set `DOTNET_ENVIRONMENT=Development` on the garage PC.
- **Setting the PCS Pro password** (via `PcsPro__Password` System-scoped environment variable):
  - The deployment script sets this interactively. Manual steps if needed: open an elevated PowerShell prompt; run `[System.Environment]::SetEnvironmentVariable("PcsPro__Password","<password>","Machine")`; restart the application or the Task Scheduler task for the change to take effect.
  - Why not `appsettings.json`: storing credentials in a plain-text config file is insecure. The environment variable is stored in the Windows registry (HKLM) and is not human-readable without Administrator access.
- **Updating the PCS Pro executable path** after reinstall or upgrade: edit `appsettings.json` — update `PcsPro:ExecutablePath` and `PcsPro:WorkingDirectory` — then restart the application.
- **Switching between mock and real mode**: change `PcsPro:UseMock` in `appsettings.json`; restart the application.
- **Changing the HTTP port**: change `Kestrel:Endpoints:Http:Url` in `appsettings.json`; update the Windows Firewall rule to use the new port; restart the application.

**Why:** Volunteers and future developers need a single authoritative reference for all configuration decisions. Accurately documenting all keys (including the `UseMock`/`Mock` distinction and the `Development.json` note) prevents misconfiguration. Addresses D-SC-4, D-SC-6.

**Dependencies:** S-001 (publish artefact must be verified so companion file list is accurate), S-002 (deployment script must exist so the password-setting cross-reference is accurate).

**Verification intent:** D-SC-4 — a person who has not previously configured the system can complete a fresh configuration from scratch using only the guide, without agent assistance, within 30 minutes.

---

### S-004 — Operational guide

**What changes:** Create `docs/guides/Operational-Guide.md`. The guide is written for club volunteers with no IT background. Sections:

1. **Before the match** — How to confirm the application is running (look for the tray icon; if absent, log on to the garage PC and wait 30 seconds for auto-start, or double-click the executable).
2. **Accessing the control panel** — Open a browser on any device on the club Wi-Fi and navigate to `http://<garage-pc-ip>:5000`. Instructions for finding the garage PC's IP address: on the garage PC, open Command Prompt and run `ipconfig`; look for the IPv4 address of the active network adapter. Recommendation: ask your IT contact to configure a static IP or DHCP reservation so the address never changes.
3. **Status indicators** — Description of each state shown on the control panel (Not Running, Launching, At Login, Match Selection, Match Loaded, Manual, Error) and what action, if any, the volunteer should take.
4. **Loading a match** — Step-by-step: click "Launch PCS Pro", wait for match list, select today's match, click "Load".
5. **Manual mode** — When to use manual mode; how to toggle it; what it changes.
6. **Updating the PCS Pro password** — "If the PCS Pro password changes, you will need to update it. Follow these steps: [cross-reference Configuration Guide §password-setting]." Note: this requires Administrator access on the garage PC.
7. **Checking for errors** — How to locate today's log file (`C:\PcsRemote\logs\pcs-remote-YYYYMMDD.log`); what level of detail to share when reporting a problem.
8. **Restarting the application** — Right-click the tray icon → Exit; then log off and log on again (or double-click the executable to restart immediately).
9. **After a reboot** — If the garage PC has been restarted (e.g., after a power cut or Windows Update): log on to the garage PC if auto-logon is not configured; the application will start automatically within 30 seconds of logon.
10. **SmartScreen prompt** — If Windows shows a "Windows protected your PC" prompt when first running the application: click "More info" then "Run anyway." This is a one-time prompt for unsigned executables.
11. **Updating PCS Pro path** — If PCS Pro is reinstalled or upgraded, the executable path in `appsettings.json` must be updated (cross-reference Configuration Guide §updating-pcs-pro-path).

**Why:** Non-technical operators need a guide that gives them confidence to manage the system on match days without developer assistance. The language must avoid jargon and every step must have a clear outcome. Addresses D-SC-5.

**Dependencies:** S-002 (deployment script and task scheduler must be finalised so guide describes actual deployment directory and process), S-003 (password update cross-reference).

**Verification intent:** D-SC-5 — a person unfamiliar with the system can: access the control panel, identify system status, enable manual mode, and locate today's log file — using only the guide, without agent assistance.

---

### S-005 — Smoke test checklist

**What changes:** Create `docs/guides/Smoke-Test-Checklist.md`. The checklist is executed after every deployment on the garage PC. It covers all Phase 1 functional areas (HLPS-003 through HLPS-006). Each item has a clear pass condition and a failure action.

**Checklist items:**

| # | Area | Test | Pass condition | Failure action |
|---|---|---|---|---|
| 1 | Deployment | Application artefact set complete | All expected files present in deploy directory | Re-run `scripts/publish.ps1` and `scripts/Deploy-PcsRemote.ps1` |
| 2 | D-SC-1 | Publish artefact runs on clean machine | Application starts without .NET pre-installed | Verify `--self-contained` flag was used |
| 3 | D-SC-2 | Task Scheduler auto-start | Application tray icon appears within 30s of logon | Check Task Scheduler task status and "Start in" setting |
| 4 | D-SC-8 / HLPS-003 | LAN browser access | Control panel loads at `http://<garage-pc-ip>:5000` from a separate device | Check firewall rule; verify correct IP address |
| 5 | HLPS-003 | SignalR connection | Status indicator updates in real time (no spinner stuck) | Check browser console for WebSocket errors |
| 6 | HLPS-006 | PCS Pro launch in real mode | PCS Pro (cricket.exe) launches and service reaches MatchSelection state | Check `PcsPro:UseMock=false`, `ExecutablePath`, and `PcsPro__Password` |
| 7 | HLPS-004 | Match list | Today's matches appear in match cards | Verify date on garage PC; check PCS Pro data |
| 8 | HLPS-006 | Match load | Select a match and click Load; service reaches MatchLoaded state | Check PCS Pro UI for dialogs; check log file |
| 9 | HLPS-004 | Scoreboard preview | Scoreboard image appears and refreshes on demand | Check `Scoreboard:JpegQuality`; check PrintWindow availability |
| 10 | HLPS-005 | Manual mode toggle | Manual mode can be enabled and disabled from the control panel | Check ManualModeService wiring in DI |
| 11 | D-SC-3 | Crash recovery | Kill `PcsRemote.TrayHost.exe` via Task Manager; application restarts within 60 seconds | Check Task Scheduler restart settings (delay ≤ 30s, count ≥ 999) |
| 12 | D-SC-6 | Password change | Update `PcsPro__Password` env var; restart application; PCS Pro logs in successfully | Follow Configuration Guide §password-setting |

**Why:** A structured checklist that any developer or club IT contact can follow ensures that every deployment is validated against all Phase 1 capabilities. It also provides a regression baseline for future deployments. Addresses D-SC-7.

**Dependencies:** S-001–S-004 (all prior deliverables must exist before the checklist can be executed meaningfully). Blocking unknown D-U-8 (PCS Pro install path) must be resolved before items 6–10 can pass.

**Verification intent:** Execute checklist on garage PC after first deployment; all 12 items pass.

---

## Delivery Record

| Step | Status | Commit |
|---|---|---|
| S-001 — Publish verification and helper script | Pending | — |
| S-002 — PowerShell deployment script | Pending | — |
| S-003 — Configuration guide | Pending | — |
| S-004 — Operational guide | Pending | — |
| S-005 — Smoke test checklist | Pending | — |
