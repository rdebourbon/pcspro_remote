# HLPS-014: WiX MSI Installer

| Field | Value |
|---|---|
| **Document** | HLPS-014-WiX-Installer.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-22 |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Dependencies** | HLPS-007 (Deployment — scripts, config guide, operational guide) |

---

## 1. Problem Statement

PCS Remote is deployed to the HHCC garage PC using a manual PowerShell-script workflow: the developer runs `scripts/publish.ps1` to produce a self-contained publish artefact, then runs `scripts/Deploy-PcsRemote.ps1` from an elevated prompt to copy files, register a Task Scheduler task, create a Windows Firewall rule, and set the PCS Pro password as a System-scoped environment variable.

This approach has several shortcomings:

1. **Operator skill barrier.** The deployment script requires an elevated PowerShell session, understanding of command-line parameters (`-AppUser`, `-Port`, `-SkipPasswordUpdate`), and manual merging of `.new` configuration files after upgrades. Club volunteers — the target operators — are not IT professionals and find this intimidating.

2. **No uninstall path.** There is no documented or scripted mechanism to cleanly remove PCS Remote from the garage PC. The Task Scheduler task, Firewall rule, environment variable, log files, and application files would all need to be identified and removed manually.

3. **Upgrade friction.** Re-deployment requires stopping the running instance, copying files, comparing configuration hashes, merging `.new` files, and restarting. A missed merge step can leave the application running with stale configuration.

4. **No Add/Remove Programs visibility.** The application does not appear in Windows "Apps & features" / "Add or Remove Programs", making it invisible to anyone who inherits maintenance of the garage PC.

5. **No version tracking.** There is no installed-version metadata anywhere on the target machine. Diagnosing whether the garage PC is running the latest build requires inspecting file timestamps or log output.

This HLPS delivers a Windows Installer (MSI) package built with WiX v5 that replaces the manual deployment workflow with a standard double-click install experience: a wizard collects configuration inputs, the MSI handles file placement, Task Scheduler, Firewall, environment variables, upgrades, and uninstall — all through a familiar Windows installer UI.

---

## 2. Scope

### In Scope

1. **WiX v5 installer project.** A new `src/PcsRemote.Installer` project using the WiX v5 SDK (`WixToolset.Sdk`) that produces a single `.msi` file. The MSI bundles the self-contained publish output from `PcsRemote.TrayHost`.

2. **Install wizard UI.** A standard WixUI dialog sequence collecting (fresh install only — upgrade is silent, see item 7):
   - Installation directory (default `C:\PcsRemote\`)
   - PCS Pro executable path (default `C:\Program Files (x86)\PCS Pro\cricket.exe`). `PcsPro:WorkingDirectory` is derived as the parent directory of this path — no separate wizard field.
   - PCS Pro password (masked input; stored as System-scoped environment variable `PcsPro__Password`)
   - HTTP port (default `5000`; used for Firewall rule and written to `appsettings.json`)
   - YouTube API Client ID (written to `appsettings.json` under `YouTube:ClientId`)
   - YouTube API Client Secret (masked input; written to `appsettings.json` under `YouTube:ClientSecret`)
   - YouTube LiveStream ID (written to `appsettings.json` under `YouTube:LiveStreamId`)

3. **File installation.** All files from the self-contained publish output installed to the chosen directory. On fresh install, a custom action writes `appsettings.json` with user-provided values (PCS Pro path, working directory, port, YouTube API credentials) and production defaults (`PcsPro:AutoLaunch = true`, `PcsPro:UseMock = false`). On upgrade, this custom action is skipped — see item 7. `appsettings.Development.json` is included; per the Configuration Guide (HLPS-007 §S-003), it is documented as development-only and must not be used in production.

4. **Task Scheduler registration.** Custom action to create/replace the `PcsRemote` scheduled task at install time with the same settings as the current `Deploy-PcsRemote.ps1`:
   - ONLOGON trigger bound to the interactive logon user at install time (per PROJECT-CONTEXT C-13, the application requires an interactive Windows session)
   - Working directory = install directory
   - Restart on failure: 30 seconds, 999 attempts
   - Interactive session, no timeout, IgnoreNew for multiple instances
   - Removed on uninstall.

5. **Windows Firewall rule.** Custom action to create the `PcsRemote-HTTP` inbound TCP rule for the configured port. Removed on uninstall.

6. **Environment variable.** Custom action to set `PcsPro__Password` as a System-scoped environment variable from the wizard input. Left in place on uninstall (see item 8).

7. **Major upgrade support.** The MSI uses the WiX major upgrade pattern (`MajorUpgrade` element) so that installing a newer version automatically removes the previous version. Upgrades are **silent** — no wizard re-entry; existing configuration, Task Scheduler task, Firewall rule, and environment variable are preserved. `appsettings.json` is marked `NeverOverwrite` so user-modified values survive the upgrade. New configuration keys introduced by a release must have sensible defaults compiled into the application code (`.NET` configuration layering ensures missing keys fall back to code defaults without manual config merging).

8. **Uninstall.** Standard Add/Remove Programs uninstall that:
   - Removes application files
   - Removes the Task Scheduler task
   - Removes the Firewall rule
   - Leaves the `PcsPro__Password` environment variable in place (avoids accidental credential loss; documented in uninstall notes)
   - Leaves log files in place (operator may need them for diagnostics)
   - Leaves YouTube OAuth tokens in place (`%AppData%\PcsRemote\GoogleTokens` — DPAPI-encrypted, harmless if orphaned)

9. **Add/Remove Programs entry.** The MSI registers the application in Add/Remove Programs with:
   - Display name: "PCS Remote"
   - Publisher: "High Halstow Cricket Club"
   - Version: derived from the project's `<Version>` MSBuild property (must conform to MSI `a.b.c.d` format, each segment ≤ 65535)
   - Icon: the custom PCS Remote icon (from S-007)

10. **Build integration.** A `scripts/build-installer.ps1` script that:
    - Runs `dotnet publish` for the TrayHost
    - Builds the WiX project to produce the `.msi`
    - Outputs the MSI to a known location (e.g., `publish/PcsRemote-Setup.msi`)

11. **Migration from PowerShell deployment.** The MSI install custom actions detect and adopt artifacts from an existing PowerShell-deployed instance:
    - If a `PcsRemote` Task Scheduler task already exists, remove it before creating the MSI-managed replacement.
    - If a `PcsRemote-HTTP` Firewall rule already exists, remove it before creating the MSI-managed replacement.
    - Existing `PcsPro__Password` environment variable and log files are left in place.

### Out of Scope

- **Code signing / EV certificate.** The MSI will trigger a SmartScreen warning on first install. Code signing is deferred (same as HLPS-007). The operational guide already documents SmartScreen dismissal.
- **Bundle / bootstrapper (Burn).** A single MSI is sufficient. No prerequisite runtimes are needed (self-contained publish). A Burn bundle would add complexity with no value.
- **Silent install parameters.** The MSI supports the standard `msiexec /quiet` mechanism natively. Documenting enterprise silent-install property mappings is deferred.
- **YouTube OAuth token acquisition.** The OAuth consent flow is triggered post-install via the tray menu "YouTube Setup..." item. The MSI captures the static API credentials (`ClientId`, `ClientSecret`, `LiveStreamId`) at install time; the interactive browser-based OAuth flow is handled at runtime.
- **Auto-update mechanism.** No self-update, no update-check, no download mechanism. Updates are manual (download new MSI, run it — major upgrade handles the rest).
- **CI/CD pipeline.** Building the MSI in a CI pipeline is deferred (same as HLPS-007).

---

## 3. Assumptions

| ID | Assumption |
|---|---|
| A-1 | The target machine runs Windows 10 or 11 x64. |
| A-2 | The installer is run by a user with local Administrator rights. |
| A-3 | `WixToolset.Sdk/5.0.2` is used via NuGet SDK — no standalone WiX Toolset install required on the build machine. (Updated from "WiX v5 SDK" by S-001 PoC; v7.0.0 evaluated but rejected due to OSMF EULA build-time enforcement.) |
| A-4 | The MSI is built on the same development machine that builds the application. Cross-compilation is not required. |
| A-5 | The installing user account is the same account that will log on to the garage PC for daily operation (the Task Scheduler ONLOGON trigger uses this account). |
| A-6 | PCS Pro is already installed on the target machine before PCS Remote is installed. |
| A-7 | The current PowerShell deployment scripts (`Deploy-PcsRemote.ps1`, `publish.ps1`) remain in the repository as a fallback mechanism but are no longer the primary deployment path. |
| A-8 | PCS Remote requires an interactive Windows logon session to operate (PROJECT-CONTEXT C-13). The Task Scheduler ONLOGON trigger provides interactive-session auto-start, not boot-time service behaviour. |

---

## 4. Constraints

| ID | Constraint | Source |
|---|---|---|
| I-C-1 | MSI must produce a single `.msi` file — no multi-file installer. | Simplicity for volunteer operators |
| I-C-2 | Configuration files modified by the user must survive upgrades. | HLPS-007 config-merge principle |
| I-C-3 | Custom actions use a **fail-forward** strategy — a failed Firewall or Task Scheduler action logs a warning and allows file installation to complete. The installer does not roll back file installation on post-file CA failure. The operational guide documents manual remediation for missing artifacts. | Robustness; single-machine, operator-recoverable |
| I-C-4 | The MSI must not require .NET to be pre-installed on the target machine (the application is self-contained). | PROJECT-CONTEXT C-2, self-contained publish |
| I-C-5 | Custom actions run elevated (the MSI itself requests elevation via `InstallPrivileges="elevated"`). A single UAC prompt is presented at MSI launch — individual custom actions must not trigger additional elevation prompts. | Required for System env vars, Task Scheduler, Firewall |
| I-C-6 | MSI properties that carry credentials (the properties used for PCS Pro password and YouTube Client Secret) must be marked as hidden (`MsiHiddenProperties`) to prevent leakage in MSI verbose logs. The actual MSI property identifiers are defined in the IS. | Credential security |

---

## 5. Success Criteria

| ID | Criterion | Verification |
|---|---|---|
| I-SC-1 | A single `PcsRemote-Setup.msi` file is produced by the build. | `scripts/build-installer.ps1` completes; MSI file exists. |
| I-SC-2 | Fresh install on a clean Windows 10/11 machine succeeds via double-click and wizard. | Run the MSI on a test machine; wizard completes; application appears in system tray after logoff/logon. |
| I-SC-3 | Upgrade install preserves user-modified `appsettings.json`. | Modify `appsettings.json` on the target; install a new version; verify the modified file is preserved. |
| I-SC-4 | Uninstall removes application files, Task Scheduler task, and Firewall rule. Env var and logs are preserved. | Uninstall via Add/Remove Programs; verify application files, scheduled task, and firewall rule are removed; verify log directory and `PcsPro__Password` env var are preserved. |
| I-SC-5 | Application appears in Add/Remove Programs with correct name, version, and icon. | Check Apps & Features after install. |
| I-SC-6 | Task Scheduler task is correctly configured after install. | Open Task Scheduler; verify ONLOGON trigger, trigger user matches installing user, restart settings, working directory. |
| I-SC-7 | Windows Firewall rule is created for the configured port. | Check `Get-NetFirewallRule -Name PcsRemote-HTTP`; verify port matches wizard input. |
| I-SC-8 | `PcsPro__Password` is set as a System environment variable after install. | Verify with `[System.Environment]::GetEnvironmentVariable("PcsPro__Password", "Machine")`. |
| I-SC-9 | Build produces 0 errors, 0 warnings. | `dotnet build` of the installer project succeeds cleanly. |
| I-SC-10 | Application reaches a healthy state after upgrade with a previously-customised config. | Modify `appsettings.json`, install a new version, logoff/logon; verify tray icon appears and control panel loads. |
| I-SC-11 | MSI verbose logging captures installation progress without leaking credentials. | Run `msiexec /i PcsRemote-Setup.msi /l*v install.log`; verify log exists and does not contain password or secret values. |

---

## 6. Risks & Mitigations

| ID | Risk | Impact | Mitigation |
|---|---|---|---|
| I-R-1 | WiX v5 custom action complexity — Task Scheduler and Firewall APIs require custom actions, which are notoriously difficult to debug in MSI context. | High | Keep custom action logic minimal; test each action in isolation before integration. The hosting model (C# DTF vs PowerShell) is tracked by I-U-2. |
| I-R-2 | Configuration preservation on upgrade — WiX's default file-overwrite behaviour replaces all files including user-modified config. | High | Mark `appsettings.json` component as `NeverOverwrite`. The config-write custom action is conditioned to run on fresh install only (not upgrade). Application code provides defaults for any new keys. |
| I-R-3 | SmartScreen blocking — unsigned MSI may be blocked or quarantined by Windows Defender SmartScreen. | Medium | Document SmartScreen dismissal procedure in the Operational Guide (§"First-Time Install" section, per HLPS-007 S-004). Defer code signing. |
| I-R-4 | Installer size — self-contained .NET 8 publish is ~35 MB; MSI compression should reduce this but the final MSI will still be substantial. | Low | Accept. Cabinet compression in MSI typically achieves 40-60% compression. |
| I-R-5 | Partial failure leaves missing artifacts — if a post-file custom action fails (e.g., Firewall rule creation), the installer completes with a degraded configuration. | Medium | Per I-C-3 (fail-forward), the installer logs a warning message visible to the operator and allows file installation to succeed. The Operational Guide documents manual remediation steps for each custom action artifact (Task Scheduler, Firewall, env var). |

---

## 7. Unknowns Register

| ID | Description | Owner | Blocking? | Status | Resolution Plan |
|---|---|---|---|---|---|
| I-U-1 | WiX v5 SDK compatibility with the project's .NET 8 build toolchain — does `WixToolset.Sdk` work as a project SDK alongside the existing solution? | Agent | Yes | Resolved | `WixToolset.Sdk/5.0.2` builds cleanly with .NET 8 SDK. v7.0.0 rejected (OSMF EULA build-time enforcement, `WIX7015`). Installer project uses local `Directory.Build.props` and CPM opt-out to avoid conflicts with root build configuration. |
| I-U-2 | Custom action hosting model — should custom actions use C# DLL custom actions (WiX DTF), PowerShell via `WixQuietExec`, or batch scripts? | Agent | Yes | Resolved | PowerShell via `WixToolset.Util.wixext` / `WixQuietExec`. Both DTF and PowerShell prototyped successfully. PowerShell selected for: lower build complexity (no separate net472 project), native cmdlets for all required system operations, easier debugging, and equivalent I-C-4 compliance. See SPEC-S-001 §7 for full evaluation. |
| I-U-3 | Task Scheduler COM API vs `schtasks.exe` — which approach is more reliable for MSI custom actions? `schtasks.exe` is simpler but less capable; COM API offers full control but requires more code. | Agent | No | Open | Decide during IS custom-action step based on chosen hosting model (I-U-2). |
| I-U-4 | WiX v5 UI customisation — does WiX v5 support custom dialog panels for PCS Pro path and password input, or does this require WixUI extensions? | Agent | No | Partially Resolved | `WixToolset.UI.wixext` 5.0.2 builds and integrates successfully; `WixUI_InstallDir` standard dialog set works. Custom wizard panels with additional input fields deferred to S-003 for full validation. |
| I-U-5 | Upgrade behaviour for `appsettings.json` — can WiX's `NeverOverwrite` attribute reliably preserve user-modified config, or is a custom merge action needed? | Agent | Yes | Resolved | `NeverOverwrite` on the `appsettings.json` component. Config-write custom action conditioned on fresh install only (not upgrade). Application code provides defaults for new keys via .NET configuration layering — no custom merge needed. |
| I-U-6 | Environment variable removal on uninstall — should the password env var be unconditionally removed, or should the uninstaller leave it? | User | No | Resolved | Leave in place to avoid accidental credential loss. |

---

## 8. Relationship to HLPS-007

HLPS-007 delivered a working manual deployment model (`Deploy-PcsRemote.ps1`, `publish.ps1`, configuration guide, operational guide, smoke test checklist). HLPS-014 replaces the deployment mechanism but does **not** invalidate the guides:

- **Configuration Guide** (`docs/guides/Configuration-Guide.md`): Remains valid for post-install configuration changes (e.g., changing HTTP port, switching mock/real mode). The "Quick Start" section will need an addendum noting that the MSI handles first-time setup automatically. This addendum is a dedicated IS deliverable.
- **Operational Guide** (`docs/guides/Operational-Guide.md`): Remains valid — it describes how to use the running application, not how to install it. An addendum documenting MSI verbose logging (`msiexec /l*v`) and manual remediation for degraded installs (per I-C-3) will be added.
- **Smoke Test Checklist** (`docs/guides/Smoke-Test-Checklist.md`): Remains valid for post-install verification.
- **PowerShell scripts**: Retained in `scripts/` as a developer fallback and for CI/CD pipeline use. They are no longer the primary deployment path for operators.

---

## 9. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-22 | Opus 4.5, GPT 5.4, Sonnet 4.6 | REQUEST CHANGES — 1 CRITICAL (config strategy), 6 HIGH (WorkingDirectory, CAQuietExec API, build deliverable ambiguity, unknowns resolution, credential log redaction, wrong-user risk), 10 MEDIUM (I-C-3/I-R-5 contradiction, migration, defaults, post-upgrade SC, upgrade UX, interactive logon), 8 LOW. All accepted findings applied in v0.2. |
| R2 | 2026-04-22 | Opus 4.5, GPT 5.4, Sonnet 4.6 | 2× APPROVE (Opus, Sonnet). GPT: REQUEST CHANGES — 1 HIGH (MSI property naming in I-C-6), 1 LOW (§8 traceability). Both applied in v0.3. |
| R3 | 2026-04-22 | GPT 5.4, Sonnet 4.6 | Unanimous APPROVE. R2 fixes verified, no regressions. |

