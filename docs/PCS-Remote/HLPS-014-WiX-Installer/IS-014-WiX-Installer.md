# IS-014 — WiX MSI Installer

| Field       | Value |
|-------------|-------|
| **Status**  | APPROVED |
| **HLPS**    | HLPS-014 (APPROVED) |
| **Author**  | Copilot |
| **Created** | 2026-04-22 |
| **Version** | 0.2 |

---

## Overview

This Implementation Sequence breaks HLPS-014 into eight ordered steps across three phases:

1. **Foundation (S-001 → S-002):** Scaffold the WiX v5 project and resolve blocking unknowns (I-U-1, I-U-2), then harvest the TrayHost publish output into the MSI. Highest technical risk — tackled first to fail fast.
2. **Installer behaviour (S-003 → S-006):** Build the wizard UI, configuration write, system artifact custom actions (Task Scheduler, Firewall, env var), and upgrade/migration logic. Each step adds an independently testable capability.
3. **Integration (S-007 → S-008):** Build script, ARP metadata, credential hiding, documentation updates, and end-to-end smoke test.

**Dependency graph:**

```
S-001 → S-002 → S-003 → S-004 → S-005 → S-006 → S-007 → S-008
```

All steps are strictly sequential — each builds on the MSI produced by the previous step. Each step results in a squash-merge to master with 0 errors and 0 warnings.

---

## S-001 — WiX v5 Project Scaffold & PoC

**What:** Create a new `src/PcsRemote.Installer` WiX v5 project using the `WixToolset.Sdk`. Add it to the solution. Configure a minimal product definition (ProductCode, UpgradeCode, version, manufacturer). Include a trivial payload (e.g., a single text file) to validate the full build → MSI → install → uninstall cycle. Evaluate the custom action hosting model by implementing a minimal "hello world" custom action using both `WixToolset.Dnc.wixext` (managed C# hosting) and `WixToolset.Util.wixext` (`WixQuietExec` PowerShell), then select the approach with better debuggability and fewer dependencies.

**Why:** Resolves I-U-1 (SDK compatibility), I-U-2 (CA hosting model), and partially validates I-U-4 (UI extensibility — full resolution in S-003). These blocking unknowns must be settled before any real installer work can proceed. The PoC validates that the entire WiX v5 toolchain works with the project's .NET 8 build environment.

**Dependencies:** None.

**Verification intent:**
- WiX project builds without errors alongside the existing solution.
- A minimal MSI is produced and can be installed/uninstalled on Windows 10/11.
- The chosen CA hosting model is documented with rationale.
- I-U-1 and I-U-2 are updated to "Resolved" in the HLPS unknowns register. I-U-4 is updated to "Partially Resolved" (full validation deferred to S-003 custom wizard dialogs).

**HLPS traceability:** Scope item 1, I-U-1, I-U-2, I-U-4, I-SC-9.

---

## S-002 — Publish Output Harvesting & File Installation

**What:** Configure the WiX project to harvest the TrayHost self-contained publish output. Define a component group that includes all files from the publish directory. Set the default installation directory. Mark `appsettings.json` with `NeverOverwrite` to preserve user modifications across upgrades. Include `appsettings.Development.json` as a normal (overwritable) component.

**Why:** Delivers HLPS scope item 3 (file installation) and the file-level foundation for scope item 7 (upgrade preservation via `NeverOverwrite`). The MSI must correctly install the ~409-file publish payload to function.

**Dependencies:** S-001.

**Verification intent:**
- MSI installs all publish output files to the chosen directory.
- `appsettings.json` component is marked `NeverOverwrite`.
- Clean uninstall removes all application files from the install directory.
- MSI file count matches the publish output file count.

**HLPS traceability:** Scope items 1, 3, I-C-1, I-C-4.

---

## S-003 — Install Wizard UI

**What:** Replace the default WixUI dialog set with a custom sequence that collects all installer inputs: installation directory, PCS Pro executable path, PCS Pro password (masked), HTTP port, YouTube Client ID, YouTube Client Secret (masked), and YouTube LiveStream ID. Define MSI properties for each input with appropriate defaults. The credential MSI properties are named `PCSPRO_PASSWORD` (PCS Pro password) and `YOUTUBE_CLIENTSECRET` (YouTube Client Secret); both must be included in `MsiHiddenProperties` to prevent log leakage (per I-C-6).

**Why:** Delivers HLPS scope item 2 (wizard UI), I-C-6 (credential hiding), and fully resolves I-U-4 (UI customisation validated with production wizard dialogs). The wizard is the operator's primary interaction point — it must collect all inputs needed by downstream custom actions (S-004, S-005).

**Dependencies:** S-002.

**Verification intent:**
- Wizard presents all input fields with correct defaults.
- Password and secret fields are masked.
- `PCSPRO_PASSWORD` and `YOUTUBE_CLIENTSECRET` properties do not appear in MSI verbose logs.
- Wizard can be completed and installation proceeds to file placement.
- I-U-4 is updated to "Resolved" in the HLPS unknowns register.

**HLPS traceability:** Scope item 2, I-C-5, I-C-6, I-U-4, I-SC-2, I-SC-11.

---

## S-004 — Configuration Write Custom Action

**What:** Implement a deferred custom action that writes `appsettings.json` with wizard-collected values on fresh install. The CA derives `WorkingDirectory` from the PCS Pro executable path (parent directory). It writes production defaults for auto-launch and mock-mode settings. The CA must be conditioned to execute only on first install — not on upgrade or repair — so that `NeverOverwrite` on the file component and the skipped CA together guarantee config preservation on upgrade.

**Why:** Delivers the configuration portion of HLPS scope item 3. The config write CA is the bridge between wizard inputs (S-003) and a working application configuration.

**Dependencies:** S-003 (wizard properties must exist).

**Verification intent:**
- Fresh install produces an `appsettings.json` containing all wizard values and production defaults.
- Upgrade install does not execute the config-write CA and preserves the existing `appsettings.json`.
- The derived working directory is the parent of the PCS Pro executable path.

**HLPS traceability:** Scope items 3, 7, I-C-2, I-C-3, I-SC-3, I-U-5.

---

## S-005 — System Artifact Custom Actions

**What:** Implement deferred custom actions for the three system artifacts:

1. **Task Scheduler:** Create/replace the `PcsRemote` scheduled task with ONLOGON trigger bound to the interactive logon user, working directory set to the install directory, and failure-restart settings matching the existing deployment script. On uninstall, remove the task.
2. **Windows Firewall:** Create the `PcsRemote-HTTP` inbound TCP rule for the wizard-configured port. On uninstall, remove the rule.
3. **Environment variable:** Set `PcsPro__Password` as a System-scoped environment variable from the wizard input. Left in place on uninstall.

All three CAs follow the fail-forward strategy (I-C-3): on failure, log a warning visible to the operator and allow the install to complete. Each CA has a corresponding uninstall action where applicable.

**Why:** Delivers HLPS scope items 4, 5, 6, and 8 (system artifact lifecycle). These are the core custom actions that replace the manual `Deploy-PcsRemote.ps1` workflow.

**Dependencies:** S-003 (wizard properties for port, password, user identity).

**Verification intent:**
- After install: Task Scheduler task exists with correct trigger, user, and restart settings. Firewall rule exists for the configured port. Environment variable is set.
- After uninstall: Task Scheduler task and Firewall rule are removed. Environment variable is preserved. Log directory is present and intact. YouTube OAuth tokens at `%AppData%\PcsRemote\GoogleTokens` are untouched.
- If any CA fails, install completes with a warning — files are installed successfully.
- The Task Scheduler implementation approach (COM API vs `schtasks.exe`) is documented and I-U-3 is updated to "Resolved" in the HLPS unknowns register.

**HLPS traceability:** Scope items 4, 5, 6, 8, I-C-3, I-C-5, I-SC-4, I-SC-6, I-SC-7, I-SC-8, A-5, A-8.

---

## S-006 — Migration & Major Upgrade

**What:** Add migration logic and major upgrade support:

1. **Migration:** Before creating MSI-managed artifacts, detect and remove any pre-existing `PcsRemote` Task Scheduler task and `PcsRemote-HTTP` Firewall rule left by the PowerShell deployment script. This runs as part of the install CA sequence (S-005) but is logically a pre-step.
2. **Major upgrade:** Configure the `MajorUpgrade` element so that installing a newer version removes the previous MSI-installed version. The upgrade sequence must ensure that S-005's uninstall custom actions do not fire during the old-product removal phase of an upgrade — only during a standalone uninstall. Validate that the upgrade sequence preserves `appsettings.json` (via `NeverOverwrite`), skips the config-write CA, and leaves existing Task Scheduler, Firewall, and env var artifacts untouched.

**Why:** Delivers HLPS scope items 7 and 11. The migration path is essential for the first MSI install on a machine with an existing PowerShell deployment. The major upgrade pattern is essential for all subsequent version updates.

**Dependencies:** S-005 (system artifact CAs must exist to test upgrade behaviour).

**Verification intent:**
- First MSI install on a machine with existing PowerShell-deployed artifacts adopts them cleanly.
- Upgrade from one MSI version to a newer version (built with a different `<Version>` value) preserves config and system artifacts.
- Upgrade is silent — no wizard re-entry.
- Version in ARP updates to the new version after upgrade.

**HLPS traceability:** Scope items 7, 11, I-C-2, I-SC-3, I-SC-5, I-SC-10.

---

## S-007 — Build Script & ARP Metadata

**What:** Create `scripts/build-installer.ps1` that orchestrates the full build pipeline: publish TrayHost as self-contained win-x64, build the WiX project, and output the MSI to `publish/PcsRemote-Setup.msi`. Configure Add/Remove Programs metadata (display name, publisher, version from MSBuild `<Version>` property, icon). Validate that MSI verbose logging (`msiexec /l*v`) does not leak credential values.

**Why:** Delivers HLPS scope items 9, 10 and I-SC-11. The build script is the single entry point for producing a release MSI. ARP metadata makes the installation visible and professional.

**Dependencies:** S-006 (complete MSI must exist to validate build output).

**Verification intent:**
- `scripts/build-installer.ps1` runs to completion and produces a single MSI file.
- ARP entry shows correct display name, publisher, version, and icon.
- MSI verbose log does not contain credential values.
- Build produces 0 errors, 0 warnings.

**HLPS traceability:** Scope items 9, 10, I-C-1, I-C-6, I-SC-1, I-SC-5, I-SC-9, I-SC-11.

---

## S-008 — Documentation & Smoke Test

**What:** Update project documentation to reflect the MSI-based deployment:

1. **Configuration Guide addendum:** Add a section noting that the MSI handles first-time setup automatically. Post-install config changes (port, mock mode, etc.) continue to use the existing guide.
2. **Operational Guide addendum:** Add sections for MSI verbose logging (`msiexec /l*v`), manual remediation steps for degraded installs (per I-C-3 fail-forward), and SmartScreen dismissal for the MSI.
3. **End-to-end smoke test:** Execute the full install → verify → upgrade → verify → uninstall → verify cycle. The fresh-install path (I-SC-2) must be validated on a clean machine with no prior PCS Remote installation. The migration path (scope item 11) is validated separately on a machine with an existing PowerShell-deployed instance. All HLPS success criteria (I-SC-1 through I-SC-11) are verified.

**Why:** Completes the HLPS deliverables. Documentation addenda ensure operators can troubleshoot the MSI-based deployment. The smoke test provides end-to-end confidence.

**Dependencies:** S-007 (complete, buildable MSI with all features).

**Verification intent:**
- Configuration Guide has the MSI addendum.
- Operational Guide has logging, remediation, and SmartScreen sections.
- Full install/upgrade/uninstall cycle passes all I-SC criteria.
- DEFERRED-ITEMS.md updated: GAP-012 marked as delivered.

**HLPS traceability:** §8 (relationship to HLPS-007), I-SC-2, I-SC-10, all I-SC criteria (smoke test).

---

## HLPS Coverage Matrix

| HLPS Scope Item | IS Step(s) |
|-----------------|------------|
| 1. WiX v5 project | S-001, S-002 |
| 2. Wizard UI | S-003 |
| 3. File installation (S-002) + config write (S-004) | S-002, S-004 |
| 4. Task Scheduler | S-005 |
| 5. Firewall | S-005 |
| 6. Environment variable | S-005 |
| 7. Major upgrade | S-004, S-006 |
| 8. Uninstall | S-005 |
| 9. ARP entry | S-007 |
| 10. Build integration | S-007 |
| 11. Migration | S-006 |

| HLPS Success Criterion | IS Step(s) |
|------------------------|------------|
| I-SC-1 (MSI produced) | S-007 |
| I-SC-2 (fresh install) | S-008 |
| I-SC-3 (config preserved) | S-004, S-006 |
| I-SC-4 (uninstall) | S-002, S-005 |
| I-SC-5 (ARP entry) | S-006, S-007 |
| I-SC-6 (Task Scheduler) | S-005 |
| I-SC-7 (Firewall) | S-005 |
| I-SC-8 (env var) | S-005 |
| I-SC-9 (0 errors/warnings) | S-007 |
| I-SC-10 (post-upgrade health) | S-008 |
| I-SC-11 (log redaction) | S-007 |

---

## Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-22 | Opus 4.5, GPT 5.4, Sonnet 4.6 | 1× APPROVE (Opus). GPT + Sonnet: REQUEST CHANGES — 1 HIGH (I-C-6 property names), 1 HIGH downgraded to MEDIUM (upgrade CA sequencing), 5 MEDIUM (I-U-3/I-U-4 resolution, log preservation, smoke test env), 6 LOW. 1 MEDIUM set aside (S-005 atomicity). All accepted findings applied in v0.2. |
| R2 | 2026-04-22 | GPT 5.4, Sonnet 4.6 | Unanimous APPROVE. All R1 fixes verified, no regressions. |
