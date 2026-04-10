# HLPS-007: Deployment & Operations

| Field | Value |
|---|---|
| **Document** | HLPS-007-Deployment.md |
| **Status** | DRAFT |
| **Version** | 0.1 |
| **Date** | 2026-04-10 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.0 |
| **Dependencies** | HLPS-005 (system tray integrated), HLPS-006 (real automation working) |

---

## 1. Problem Statement

The application is built and tested, but it needs to run reliably and unattended on the garage PC. The target operators are cricket club volunteers — not IT professionals. The deployment must be simple, the application must auto-start on boot/logon, and it must recover from crashes without human intervention.

The previous Python/AHK system required manual setup knowledge that was never documented and was lost when the original developer left. This must not happen again.

This HLPS delivers production-ready deployment: Task Scheduler auto-start, crash recovery, configuration documentation, and an operational guide that any committee member can follow.

---

## 2. Scope

### In Scope

- **Self-contained publish**: `dotnet publish` producing a self-contained single-file deployment (no .NET runtime dependency on garage PC)
- **Task Scheduler configuration**: PowerShell script or documented steps to create a scheduled task that:
  - Triggers at user logon
  - Starts the application minimised (tray icon visible)
  - Restarts on failure (with configurable delay)
  - Runs in the interactive session (not Session 0)
- **Configuration documentation**: Clear guide covering:
  - `appsettings.json` settings and their purpose
  - How to set PCS Pro password via User Secrets or environment variable
  - Network port configuration (`Web:Port`, default 5050)
  - How to switch between mock and real mode (`PcsPro:UseMock`)
  - Log file location and retention
- **Operational guide**: Step-by-step document for club volunteers covering:
  - How to access the control panel from a browser
  - What each status indicator means
  - How to use manual mode
  - How to update the PCS Pro password if it changes
  - How to check log files for troubleshooting
  - How to restart the application if needed
- **Deployment script**: Optional PowerShell script automating: copy files to target directory, create/update Task Scheduler task, set environment variables
- **Smoke test checklist**: Manual verification steps to run after deployment

### Out of Scope

- CI/CD pipeline (deferred — U-6)
- Automatic updates
- Remote deployment tooling
- Windows installer / MSI package (overkill for single-machine deployment)

---

## 3. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| D-SC-1 | Application publishes as self-contained single file | `dotnet publish` produces working executable |
| D-SC-2 | Task Scheduler starts application at logon in interactive session | Logoff/logon cycle on garage PC |
| D-SC-3 | Application restarts automatically after crash | Kill process → verify restart within configured delay |
| D-SC-4 | Configuration guide is complete and accurate | Walk-through by non-developer |
| D-SC-5 | Operational guide is understandable by a club volunteer | Review by non-technical person |
| D-SC-6 | PCS Pro password can be changed without code or rebuild | Follow config guide to update password → verify login works |
| D-SC-7 | Smoke test checklist verifies all Phase 1 functionality | Execute checklist on garage PC |

---

## 4. Unknowns Register

| ID | Description | Owner | Blocking? |
|---|---|---|---|
| D-U-1 | Garage PC OS version (Windows 10 or 11) — affects Task Scheduler PowerShell syntax slightly | Agent | No — both supported; minor syntax differences handled in script |
| D-U-2 | Garage PC user account — is it a dedicated account or shared? Affects Task Scheduler "run as" configuration. | User | No — Task Scheduler uses "any user" trigger; not blocking |
| D-U-3 | Target deployment directory on garage PC | User | No — default to `C:\PcsRemote\`; configurable |

---

## 5. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| — | — | — | Not yet reviewed |
