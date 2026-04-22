# HLPS-014 — MSI Installer Smoke Test Checklist

This checklist covers the full install → verify → upgrade → verify → uninstall → verify lifecycle for the PCS Remote MSI installer. Each item maps to an I-SC success criterion from IS-014.

---

## Scenario 1: Fresh Install (Clean Machine)

**Prerequisites:** Windows 10/11 machine with no prior PCS Remote installation — no MSI, no PowerShell deployment, no leftover environment variables, Task Scheduler tasks, or firewall rules.

| Execution | Value |
|---|---|
| Date | |
| Executor | |
| Machine | |
| OS Build | |
| MSI Version | |

| # | Check | I-SC | Pass/Fail | Notes |
|---|---|---|---|---|
| 1.1 | `scripts/build-installer.ps1` runs to completion and produces `artifacts/PcsRemote-Setup.msi` | I-SC-1 | | |
| 1.2 | MSI build produces 0 errors, 0 warnings | I-SC-9 | | |
| 1.3 | Double-click MSI on test machine; wizard launches after UAC prompt | I-SC-2 | | |
| 1.4 | Wizard pages display correctly: Welcome → Install Location → PCS Pro Settings (exe path, password, port) → YouTube Settings (client ID, client secret, live stream ID) → Verify Ready → Install | I-SC-2 | | |
| 1.5 | Installation completes without errors | I-SC-2 | | |
| 1.6 | Application files present in chosen install directory | I-SC-2 | | |
| 1.7 | `appsettings.json` exists with correct port, PCS Pro path, YouTube credentials, and live stream ID | I-SC-2 | | |
| 1.8 | Task Scheduler: `PcsRemote` task exists with ONLOGON trigger, correct user, restart settings, and working directory | I-SC-6 | | |
| 1.9 | Firewall: `Get-NetFirewallRule -Name PcsRemote-HTTP` returns rule with correct port | I-SC-7 | | |
| 1.10 | Environment variable: `[System.Environment]::GetEnvironmentVariable("PcsPro__Password", "Machine")` returns the entered password | I-SC-8 | | |
| 1.11 | Apps & Features: "PCS Remote" appears with correct version, publisher ("High Halstow Cricket Club"), and custom icon | I-SC-5 | | |
| 1.12 | Log off and log on; tray icon appears within 30 seconds | I-SC-2 | | |
| 1.13 | Right-click tray icon → "Open Browser" opens control panel successfully | I-SC-2 | | |

---

## Scenario 2: Migration from PowerShell Deployment

**Prerequisites:** Windows 10/11 machine with an existing PCS Remote installation via `Deploy-PcsRemote.ps1`. The `PcsRemote` Task Scheduler task, `PcsRemote-HTTP` firewall rule, `PcsPro__Password` environment variable, and customised `appsettings.json` are all present.

| Execution | Value |
|---|---|
| Date | |
| Executor | |
| Machine | |
| OS Build | |
| MSI Version | |

| # | Check | I-SC | Pass/Fail | Notes |
|---|---|---|---|---|
| 2.1 | Record pre-existing `appsettings.json` content (port, PCS Pro path, YouTube credentials) | — | | |
| 2.2 | Install MSI over existing PowerShell deployment; installation completes | I-SC-2 | | |
| 2.3 | No duplicate Task Scheduler tasks: only one `PcsRemote` task exists | I-SC-5 | | |
| 2.4 | No duplicate firewall rules: only one `PcsRemote-HTTP` rule exists | I-SC-6 | | |
| 2.5 | `appsettings.json` is preserved (content matches pre-install recording from 2.1) | I-SC-3 | | |
| 2.6 | Environment variable `PcsPro__Password` is preserved | I-SC-8 | | |
| 2.7 | Log off and log on; tray icon appears; control panel loads | I-SC-10 | | |

---

## Scenario 3: Major Upgrade

**Prerequisites:** Machine with PCS Remote v1.0.0.0 installed via MSI (Scenario 1 complete). Customise `appsettings.json` before upgrading (e.g., change port to 5001) to verify config preservation.

| Execution | Value |
|---|---|
| Date | |
| Executor | |
| Machine | |
| OS Build | |
| MSI Version (old) | |
| MSI Version (new) | |

| # | Check | I-SC | Pass/Fail | Notes |
|---|---|---|---|---|
| 3.1 | Record pre-upgrade `appsettings.json` content | — | | |
| 3.2 | Build v1.1.0.0 MSI: `scripts/build-installer.ps1 -Version 1.1.0.0` | I-SC-1 | | |
| 3.3 | Install v1.1.0.0 MSI over v1.0.0.0; installation completes | I-SC-10 | | |
| 3.4 | `appsettings.json` is preserved (content matches pre-upgrade recording from 3.1) | I-SC-3 | | |
| 3.5 | Apps & Features shows version 1.1.0.0 (not 1.0.0.0) | I-SC-5 | | |
| 3.6 | Task Scheduler task still exists and is correctly configured | I-SC-6 | | |
| 3.7 | Firewall rule still exists with correct port | I-SC-7 | | |
| 3.8 | Environment variable `PcsPro__Password` is preserved | I-SC-8 | | |
| 3.9 | Log off and log on; tray icon appears; control panel loads | I-SC-10 | | |

---

## Scenario 4: Uninstall

**Prerequisites:** Machine with PCS Remote installed via MSI (Scenario 1 or 3 complete).

| Execution | Value |
|---|---|
| Date | |
| Executor | |
| Machine | |
| OS Build | |
| MSI Version | |

| # | Check | I-SC | Pass/Fail | Notes |
|---|---|---|---|---|
| 4.1 | Uninstall via Apps & Features; uninstall completes | I-SC-4 | | |
| 4.2 | Application files removed from install directory | I-SC-4 | | |
| 4.3 | Task Scheduler: `PcsRemote` task is removed | I-SC-4 | | |
| 4.4 | Firewall: `PcsRemote-HTTP` rule is removed | I-SC-4 | | |
| 4.5 | Environment variable `PcsPro__Password` is **preserved** (intentionally not removed) | I-SC-4 | | |
| 4.6 | Log directory (`logs\`) is **preserved** (intentionally not removed) | I-SC-4 | | |
| 4.7 | Apps & Features: "PCS Remote" no longer appears | I-SC-4 | | |

---

## Scenario 5: Credential Log Redaction

**Prerequisites:** MSI built from Scenario 1 or 3.

| Execution | Value |
|---|---|
| Date | |
| Executor | |
| Machine | |
| OS Build | |
| MSI Version | |

| # | Check | I-SC | Pass/Fail | Notes |
|---|---|---|---|---|
| 5.1 | Install with verbose logging: `msiexec /i PcsRemote-Setup.msi /l*v install.log` | I-SC-11 | | |
| 5.2 | Enter a recognisable sentinel password (e.g., `SENTINEL_PASSWORD_12345`) during wizard | I-SC-11 | | |
| 5.3 | Enter a recognisable sentinel client secret (e.g., `SENTINEL_SECRET_67890`) during wizard | I-SC-11 | | |
| 5.4 | Search `install.log` for `SENTINEL_PASSWORD_12345` — must **not** appear | I-SC-11 | | |
| 5.5 | Search `install.log` for `SENTINEL_SECRET_67890` — must **not** appear | I-SC-11 | | |
| 5.6 | Confirm log contains `*****` redaction markers for hidden properties | I-SC-11 | | |

---

## Summary

| Scenario | I-SC Coverage | Result |
|---|---|---|
| 1. Fresh Install | I-SC-1, 2, 5, 6, 7, 8, 9 | |
| 2. Migration | I-SC-3, 10, scope 11 | |
| 3. Upgrade | I-SC-1, 3, 5, 6, 7, 8, 10 | |
| 4. Uninstall | I-SC-4 | |
| 5. Credential Redaction | I-SC-11 | |

**All I-SC criteria covered:** I-SC-1 ✓, I-SC-2 ✓, I-SC-3 ✓, I-SC-4 ✓, I-SC-5 ✓, I-SC-6 ✓, I-SC-7 ✓, I-SC-8 ✓, I-SC-9 ✓, I-SC-10 ✓, I-SC-11 ✓
