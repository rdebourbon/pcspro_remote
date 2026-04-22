# SPEC-S-008 — Documentation & Smoke Test

| Field | Value |
|---|---|
| **Document** | SPEC-S-008-Documentation-Smoke-Test.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-22 |
| **IS Step** | S-008 |
| **HLPS** | HLPS-014 (APPROVED) |
| **Dependencies** | SPEC-S-007 (APPROVED — complete, buildable MSI) |

---

## 1. Objective

Complete the HLPS-014 deliverables by updating project documentation and defining the end-to-end smoke test procedure. After this step:
- The Configuration Guide reflects MSI-based first-time setup
- The Operational Guide covers MSI logging, manual remediation, and SmartScreen
- A smoke test checklist covers every I-SC criterion
- DEFERRED-ITEMS.md marks GAP-012 as delivered

---

## 2. Requirements

### 2.1 Configuration Guide Update

Restructure `docs/guides/Configuration-Guide.md` so the MSI-based deployment is the **primary Quick Start path** a new reader encounters first. The existing PowerShell-based deployment procedure is moved into a clearly labelled "Fallback — PowerShell Deployment" subsection. Specific changes:

1. Replace the current "Quick Start — First-Time Deployment Procedure" with an MSI-based flow: build the MSI via `scripts/build-installer.ps1`, copy to the garage PC, double-click, follow the wizard.
2. Describe the MSI wizard flow: install location → port → PCS Pro password → YouTube credentials → live stream ID → install.
3. Note that `appsettings.json` is written automatically by the MSI on fresh install and preserved on upgrade (no manual editing required for first-time setup).
4. Reference `scripts/build-installer.ps1` for building the MSI and the `-Version` parameter for versioned builds.
5. Move the existing `publish.ps1` + `Deploy-PcsRemote.ps1` procedure into a "Fallback — PowerShell Deployment" subsection, clearly labelled as the legacy path.
6. Note that post-install configuration changes (port, mock mode, etc.) continue to use the existing manual `appsettings.json` editing process regardless of deployment method.

### 2.2 Operational Guide Addendum

Add the following sections to `docs/guides/Operational-Guide.md`:

1. **MSI verbose logging:** How to run `msiexec /l*v install.log /i PcsRemote-Setup.msi` for troubleshooting. Note that credential values are redacted in the log.
2. **Manual remediation for degraded installs (I-C-3):** Step-by-step instructions for manually creating each system artifact if the MSI custom action failed:
   - Task Scheduler task: `schtasks` or Task Scheduler GUI
   - Firewall rule: `New-NetFirewallRule`
   - Environment variable: `[System.Environment]::SetEnvironmentVariable`
   - Configuration file: manually creating `appsettings.json` from the template with the values entered during the wizard (port, PCS Pro path, YouTube credentials, live stream ID)
3. **SmartScreen dismissal (I-R-3):** Instructions for dismissing the Windows SmartScreen warning when running an unsigned MSI.
4. **Uninstalling PCS Remote:** How to uninstall via Apps & Features, and what artifacts are intentionally preserved (environment variable, log directory).

### 2.3 Smoke Test Checklist

Create `docs/PCS-Remote/HLPS-014-WiX-Installer/SMOKE-TEST-CHECKLIST.md` with a structured checklist covering the full install → verify → upgrade → verify → uninstall → verify lifecycle. Each item maps to an I-SC criterion. The checklist is designed for manual execution on a test machine.

The checklist covers:
- **Fresh install (clean machine):** MSI wizard completes on a Windows 10/11 machine with no prior PCS Remote installation (no MSI, no PowerShell deployment, no leftover env var/task/firewall). Files installed, Task Scheduler task created, Firewall rule created, environment variable set, ARP entry visible (I-SC-1 through I-SC-9).
- **Post-install health:** Application starts after logoff/logon, control panel loads (I-SC-2).
- **Migration from PowerShell deployment:** On a separate machine with an existing `Deploy-PcsRemote.ps1` installation, install the MSI. Verify pre-existing task/firewall rule are adopted/replaced cleanly with no duplicates, config is preserved, application is healthy (scope item 11, I-SC-3, I-SC-10).
- **Upgrade:** Build a v1.1.0.0 MSI, install over v1.0.0.0, verify config preserved, app healthy (I-SC-3, I-SC-10).
- **Uninstall:** Remove via Apps & Features, verify files/task/firewall removed, env var and logs preserved (I-SC-4).
- **Credential log redaction:** Run with `/l*v`, verify no password or secret values in log (I-SC-11).

**Execution requirement:** The smoke test checklist is not just a document — it must be executed end-to-end on test machines. Each scenario (fresh install, migration, upgrade, uninstall, credential redaction) records its own execution metadata: date, executor, machine info (OS build), and MSI version(s) tested. Each checklist item has a pass/fail column. HLPS-014 is not considered complete until executed results are committed to the repository.

### 2.4 DEFERRED-ITEMS.md Update

Update GAP-012 in `docs/PCS-Remote/DEFERRED-ITEMS.md`:
- **Target HLPS** column: change to `✅ Delivered (HLPS-014)`
- **Status** column: change to `✅ Complete`

---

## 3. Changes Required

### 3.1 Modified: `docs/guides/Configuration-Guide.md`

Add MSI installation section per §2.1.

### 3.2 Modified: `docs/guides/Operational-Guide.md`

Add MSI logging, remediation, SmartScreen, and uninstall sections per §2.2.

### 3.3 New File: `docs/PCS-Remote/HLPS-014-WiX-Installer/SMOKE-TEST-CHECKLIST.md`

Structured smoke test checklist per §2.3.

### 3.4 Modified: `docs/PCS-Remote/DEFERRED-ITEMS.md`

GAP-012 status update per §2.4.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | Configuration Guide Quick Start is restructured: MSI-based deployment is the primary path, PowerShell deployment is in a clearly labelled fallback subsection |
| AC-2 | Operational Guide contains sections for MSI logging, manual remediation (Task Scheduler, Firewall, env var, appsettings.json), SmartScreen dismissal, and uninstall behavior |
| AC-3 | Smoke test checklist maps every I-SC criterion (I-SC-1 through I-SC-11) to a concrete verification step |
| AC-4 | Smoke test checklist covers fresh install (clean machine), migration from PowerShell deployment, upgrade, and uninstall lifecycle |
| AC-5 | Smoke test checklist includes per-scenario pass/fail columns and execution metadata fields (date, executor, OS build, MSI version) for each scenario (fresh install, migration, upgrade, uninstall, credential redaction) |
| AC-6 | DEFERRED-ITEMS.md shows GAP-012 with Target HLPS `✅ Delivered (HLPS-014)` and Status `✅ Complete` |
| AC-7 | Build: 0 errors, 0 warnings; existing test suite passes |
| AC-8 | HLPS-014 is not considered complete until the smoke test checklist is executed end-to-end and the completed results (all pass/fail columns filled, per-scenario execution metadata populated) are committed to the repository |

---

## 5. Branching & Commits

- **Branch:** `feature/S-008-docs-smoke-test`
- **Commit strategy:** Small, focused commits per logical change.

---

## 6. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | Documentation may become stale as the MSI evolves | Low risk. The documentation describes stable wizard flows and system artifacts that are unlikely to change. Future HLPS iterations update docs as needed. |

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-22 | Opus 4.7, GPT 5.4 | REQUEST CHANGES — 2 HIGH (smoke test doc-only without execution, migration scenario missing), 2 MEDIUM (Config Guide structure conflict, config-write CA remediation missing), 3 LOW (clean machine prerequisite, evidence format, DEFERRED-ITEMS schema). All accepted except L-3 (I-R-3 reference verified correct). Applied in v0.2. |
| R2 | 2026-04-22 | Opus 4.7, GPT 5.4 | REQUEST CHANGES — 1 MEDIUM/HIGH (AC-5 "ready for manual execution" doesn't gate on executed evidence), 1 MEDIUM (per-scenario metadata). Both ACCEPTED: added AC-8 requiring committed execution results, per-scenario metadata fields. Applied in v0.3. |
