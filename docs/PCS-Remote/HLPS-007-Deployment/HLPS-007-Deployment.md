# HLPS-007: Deployment & Operations

| Field | Value |
|---|---|
| **Document** | HLPS-007-Deployment.md |
| **Status** | APPROVED |
| **Version** | 0.5 |
| **Date** | 2026-04-14 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Dependencies** | HLPS-003 (control panel), HLPS-004 (scoreboard), HLPS-005 (system tray integrated), HLPS-006 (real automation working) |

---

## 1. Problem Statement

The application is built and tested, but it needs to run reliably on the garage PC with minimal operator involvement. The target operators are cricket club volunteers — not IT professionals. The deployment must be simple, the application must auto-start at logon, and it must recover from crashes without human intervention.

> **Auto-start after reboot**: The application runs in an interactive Windows session (not a Session 0 service), so it starts when the configured user logs on — not on bare boot. Whether this is fully unattended depends on whether Windows auto-logon is configured on the garage PC (see D-U-5). The operational guide must document what action is needed after a reboot if auto-logon is not in use.

The previous Python/AHK system required manual setup knowledge that was never documented and was lost when the original developer left. This must not happen again.

This HLPS delivers production-ready deployment: a self-contained publish, Task Scheduler auto-start with crash recovery, Windows Firewall configuration, configuration documentation, and an operational guide that any committee member can follow.

---

## 2. Scope

### In Scope

- **Self-contained portable publish**: `dotnet publish src/PcsRemote.TrayHost/PcsRemote.TrayHost.csproj -c Release -r win-x64 --self-contained` producing a portable deployment artefact set (executable + required companion files including `appsettings.json`) with no .NET runtime dependency on the garage PC.
- **Task Scheduler configuration**: PowerShell script or documented steps to create a scheduled task that:
  - Triggers at logon of the designated deployment user account (not "any user" — prevents multiple instances on shared or auto-logon PCs)
  - Sets "Start in" (working directory) to the deployment directory — required for correct resolution of all relative paths (log files, configuration, static content)
  - Starts the application minimised (tray icon visible, no console window)
  - Restarts on failure with a short delay (≤ 30 seconds) and a high retry count (≥ 999) to approximate indefinite recovery
  - Runs in the interactive session (not Session 0)
- **Windows Firewall rule**: Deployment script or documented steps to create or replace (idempotent) an inbound TCP rule allowing connections on the configured HTTP port (default 5000), enabling LAN access from tablets, laptops, and other devices
- **Configuration documentation**: Clear guide covering all `appsettings.json` settings:
  - `PcsPro:AutoLaunch` — whether PCS Pro is launched automatically on service start
  - `PcsPro:ExecutablePath` and `PcsPro:WorkingDirectory` — path to cricket.exe installation (required for real mode)
  - `PcsPro:Password` — how to set via the System-scoped environment variable `PcsPro__Password` (double-underscore hierarchy separator — no application prefix); User Secrets is a development-only mechanism and must not be used in production
  - `PcsPro:UseMock` — top-level boolean to switch between mock and real mode (note: distinct from the `PcsPro:Mock` subsection below — `PcsPro:UseMock` is a production-relevant setting; `PcsPro:Mock` contains dev/test-only parameters)
  - `Kestrel:Endpoints:Http:Url` — network port and bind address (default `http://0.0.0.0:5000`)
  - `Scoreboard:JpegQuality` — image compression setting
  - Log file location (`logs/` subdirectory of deployment directory), log level, and 7-day retention policy
  - `AllowedHosts` — host filtering (default `"*"` is appropriate for LAN deployment; note if restricting to specific hostnames)
  - Note on `PcsPro:Mock` subsection — development/test parameters (delay timings, probability settings); not relevant for production deployment
  - Note on `appsettings.Development.json` — this file is included in the publish output but is not loaded unless `DOTNET_ENVIRONMENT=Development` is set; do not set that variable on the garage PC
- **Operational guide**: Step-by-step document for club volunteers covering:
  - How to access the control panel from a browser (including the application's IP address or hostname)
  - What each status indicator means
  - How to use manual mode
  - How to update the PCS Pro password if it changes
  - How to check log files for troubleshooting
  - How to restart the application if needed
  - What to expect after a reboot (auto-logon or manual login requirement — resolution depends on D-U-5)
  - How to update the PCS Pro executable path in `appsettings.json` if PCS Pro is reinstalled or upgraded to a different directory
  - What to do if a Windows SmartScreen prompt appears on first run
- **Deployment script**: PowerShell script (required, not optional) that **must be run with Administrator elevation** (Run as Administrator — required for System-scoped environment variables and Windows Firewall rule creation). Automates: copy publish artefacts to deployment directory, create/update Task Scheduler task, create or replace Windows Firewall rule, set System-scoped environment variables (at minimum: `PcsPro__Password` for PCS Pro credentials — password must be entered interactively at script runtime via `Read-Host -AsSecureString` and must never be hardcoded in the script file itself)
- **Smoke test checklist**: Manual verification steps to run after deployment covering: application starts at logon, control panel reachable from a separate LAN device, PCS Pro launches in real mode, match selection works, scoreboard visible, manual mode toggle works, crash recovery restarts the process

### Out of Scope

- CI/CD pipeline (deferred)
- Automatic updates
- Remote deployment tooling
- Windows installer / MSI package (overkill for single-machine deployment)
- Code signing / EV certificate (deferred; SmartScreen dismissal documented in operational guide)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| D-SC-1 | Application publishes as a self-contained, portable artefact set for `win-x64` | `dotnet publish src/PcsRemote.TrayHost/PcsRemote.TrayHost.csproj -c Release -r win-x64 --self-contained` succeeds; copy output to a clean directory on a machine without .NET installed; application starts correctly |
| D-SC-2 | Task Scheduler starts application at logon of the designated user in the interactive session | Logoff/logon cycle on garage PC; application appears in system tray without manual intervention |
| D-SC-3 | Application restarts automatically after crash within 60 seconds; recovery continues after multiple sequential crashes | Kill process; verify restart within ≤ 60 seconds (Task Scheduler restart delay ≤ 30 s per §2 + application startup time); kill three times in succession; verify it continues to restart |
| D-SC-4 | Configuration guide is complete and accurate | A person who has not previously configured the system can complete a fresh configuration from scratch using only the guide, without agent assistance, within 30 minutes |
| D-SC-5 | Operational guide is understandable by a club volunteer | A person unfamiliar with the system can: access the control panel, identify system status, enable manual mode, and locate today's log file — using only the guide, without agent assistance |
| D-SC-6 | PCS Pro password can be changed without code or rebuild | Follow config guide to update the environment variable; restart application; verify successful login to PCS Pro |
| D-SC-7 | Smoke test checklist covers all Phase 1 functional areas: HLPS-003 control panel access, HLPS-004 scoreboard preview and refresh, HLPS-005 manual mode toggle, HLPS-006 PCS Pro launch and login in real mode | Execute checklist on garage PC; all items pass |
| D-SC-8 | Control panel is accessible from a browser on a separate LAN device | Navigate to `http://<garage-pc-ip>:5000` from a phone or laptop on the same network; control panel loads |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| D-U-1 | Garage PC OS version (Windows 10 or 11) — affects Task Scheduler PowerShell syntax slightly | Agent | No — both supported; minor syntax differences handled in script |
| D-U-2 | Garage PC user account name — needed to configure the Task Scheduler task trigger for the specific user (avoids multiple-instance risk from "any user" trigger) | User | No — deployment script will prompt for username if not pre-configured |
| D-U-3 | Target deployment directory on garage PC | User | No — default to `C:\PcsRemote\`; configurable |
| D-U-4 | Windows Firewall policy — is it managed by group policy, or local? Can the deployment script create inbound rules without admin escalation? | User | No — script will attempt rule creation; if blocked, manual steps are documented |
| D-U-5 | Auto-logon configuration — is the garage PC configured for auto-logon? If not, the application will not start after a reboot until a user logs in. The club must decide whether to enable auto-logon or document the manual logon requirement in the operational guide. | User | No — both paths are handled; blocking only for the "fully unattended after reboot" use case |
| D-U-6 | Antivirus / Windows Defender SmartScreen — will a locally-built self-contained executable be flagged on first run? | User | No — dismissal steps documented in operational guide; if AV quarantines the file, an exclusion path must be added |
| D-U-7 | Garage PC network addressing — does the garage PC have a static IP, DHCP reservation, or dynamic DHCP? A dynamic address can change after a router reboot, rendering any hardcoded IP in the operational guide stale. Recommendation: configure a DHCP reservation or static IP before deployment. | User | No — operational guide will include a "how to find the current IP" step (e.g., `ipconfig`); static IP or DHCP reservation is strongly recommended |
| D-U-8 | PCS Pro installation directory on garage PC — needed to configure `PcsPro:ExecutablePath` and `PcsPro:WorkingDirectory`; typical default is `C:\Program Files (x86)\PCS Pro\` but must be confirmed. | User | Yes — required for real-mode smoke test (D-SC-7); must be resolved before deployment |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-14 | Claude Opus 4.6, Claude Sonnet 4.6 | NEEDS REVISION — 3 HIGH, 8 MEDIUM, 4 LOW |
| R1 fixes | 2026-04-14 | Orchestrator | All 15 findings triaged and applied — see triage table below |
| R2 | 2026-04-14 | Claude Opus 4.6, Claude Sonnet 4.6 | NEEDS REVISION — 2 HIGH, 6 MEDIUM, 3 LOW; all R1 findings RESOLVED |
| R2 fixes | 2026-04-14 | Orchestrator | All 11 findings triaged and applied — see triage table below |
| R3 | 2026-04-14 | Claude Opus 4.6, Claude Sonnet 4.6 | Opus: APPROVED (2 LOW); Sonnet: NEEDS REVISION (2 MEDIUM, 2 LOW); all R2 findings RESOLVED |
| R3 fixes | 2026-04-14 | Orchestrator | All 6 findings triaged and applied — see triage table below |

### R1 Triage Summary

| Finding | Decision |
|---|---|
| Windows Firewall missing from scope (both HIGH) | Accepted — Firewall rule added to scope, deployment script, smoke test, and unknowns (D-U-4) |
| Boot-vs-logon / auto-logon gap (both HIGH) | Accepted — Problem statement qualified; auto-logon addressed in scope and unknowns (D-U-5); operational guide scope updated |
| Relative log path fails without Task Scheduler "Start in" (Opus HIGH) | Accepted — "Start in" requirement added to Task Scheduler scope item |
| Missing config settings: AutoLaunch, ExecutablePath, WorkingDirectory (Opus MEDIUM) | Accepted — All appsettings.json keys now enumerated in configuration documentation scope |
| User Secrets in production contradicts PROJECT-CONTEXT.md (both MEDIUM) | Accepted — User Secrets removed; environment variable (System-scoped) specified as production mechanism |
| No LAN connectivity success criterion (Opus MEDIUM) | Accepted — D-SC-8 added |
| D-U-2 "any user" trigger risks multiple instances (Opus MEDIUM) | Accepted — Task Scheduler scope now specifies named-user trigger; D-U-2 reworded |
| Publish RID not specified / single-file claim unqualified (both MEDIUM) | Accepted — Scope now says `win-x64 --self-contained`; "single-file" replaced with "portable artefact set" |
| Task Scheduler retry exhaustion (Sonnet MEDIUM) | Accepted — Retry count ≥ 999 added to Task Scheduler scope; D-SC-3 updated |
| D-SC-3 no recovery time SLA (Sonnet MEDIUM) | Accepted — "within 60 seconds" added to D-SC-3 |
| D-SC-4, D-SC-5 unmeasurable (both MEDIUM/LOW) | Accepted — Task-based rubric added to both criteria |
| D-SC-7 "Phase 1" undefined (both MEDIUM/LOW) | Accepted — HLPS-003 through HLPS-006 functional areas enumerated in D-SC-7 |
| Deployment script marked "Optional" (Sonnet LOW) | Accepted — "Optional" removed; script is a required deliverable |
| SmartScreen risk unmentioned (Opus LOW) | Accepted — SmartScreen note added to operational guide scope and D-U-6 added |
| Context version stale (Sonnet LOW) | Accepted — Updated to v1.1 |

### R2 Triage Summary

| Finding | Decision |
|---|---|
| Env var prefix `PCSREMOTE_` incorrect — no prefix in app host builder (both) | Accepted — corrected to `PcsPro__Password` (no prefix); explanation of double-underscore separator added |
| Publish command missing target project and `-c Release` (Opus HIGH) | Accepted — full command `dotnet publish src/PcsRemote.TrayHost/... -c Release -r win-x64 --self-contained` applied in scope and D-SC-1 |
| Deployment script env vars not enumerated (Opus MEDIUM) | Accepted — `PcsPro__Password` explicitly named in deployment script scope item |
| Dependencies missing HLPS-003, HLPS-004 (Opus MEDIUM) | Accepted — added to Dependencies header |
| Logging/AllowedHosts/PcsPro:Mock undocumented (Opus LOW) | Accepted — log level and AllowedHosts added; PcsPro:Mock noted as dev-only |
| Firewall rule not idempotent on re-run (Sonnet MEDIUM) | Accepted — changed to "create or replace (idempotent)" in scope and deployment script |
| Garage PC IP discoverability unregistered (Sonnet MEDIUM) | Accepted — D-U-7 added; operational guide scope to include IP discovery step |
| PCS Pro installation path unregistered (Sonnet MEDIUM) | Accepted — D-U-8 added; marked Blocking: Yes for real-mode smoke test |
| "Start in" rationale too narrow (Sonnet LOW) | Accepted — broadened to "all relative paths (log files, configuration, static content)" |

### R3 Triage Summary

| Finding | Decision |
|---|---|
| Password secure handling not specified in deployment script scope (Sonnet MEDIUM) | Accepted — `Read-Host -AsSecureString` requirement and no-plaintext-in-script rule added to deployment script scope |
| `PcsPro:UseMock` vs `PcsPro:Mock` ambiguity (Sonnet MEDIUM; confirmed real against codebase) | Accepted — clarifying parenthetical added: "top-level boolean, distinct from the `PcsPro:Mock` subsection" |
| Deployment script requires elevation — not stated (Opus LOW) | Accepted — "must be run with Administrator elevation" added to deployment script scope; rationale given |
| `appsettings.Development.json` in publish output unmentioned (Opus LOW) | Accepted — note added to config documentation scope; warns against setting `DOTNET_ENVIRONMENT=Development` |
| D-SC-3 60s / §2 30s relationship implicit (Sonnet LOW) | Accepted — D-SC-3 verification now cross-references "restart delay ≤ 30 s per §2 + application startup" |
| Operational guide missing PCS Pro reinstall path scenario (Sonnet LOW) | Accepted — bullet added: "How to update PCS Pro executable path if PCS Pro is reinstalled or upgraded" |

