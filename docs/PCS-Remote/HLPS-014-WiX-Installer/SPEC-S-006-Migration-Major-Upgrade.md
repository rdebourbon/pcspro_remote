# SPEC-S-006 — Migration & Major Upgrade

| Field | Value |
|---|---|
| **Document** | SPEC-S-006-Migration-Major-Upgrade.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-22 |
| **IS Step** | S-006 |
| **HLPS** | HLPS-014 (APPROVED) |
| **Dependencies** | SPEC-S-005 (APPROVED — system artifact CAs exist) |

---

## 1. Objective

Enable two upgrade paths for PCS Remote:

1. **Migration from PowerShell deployment:** The first MSI install on a machine with an existing PowerShell-deployed instance (Task Scheduler task, Firewall rule, env var, config) adopts the artifacts cleanly.
2. **Major upgrade (MSI-to-MSI):** Installing a newer MSI version over an older MSI version replaces application files while preserving all user configuration and system artifacts — no wizard re-entry.

After this step, the MSI supports the full install → upgrade → uninstall lifecycle described in HLPS-014 scope items 7 and 11.

---

## 2. Problem Analysis

### 2.1 Install CA Conditions

All install CAs (config write, Task Scheduler, Firewall, env var) are conditioned on `NOT Installed`. During a major upgrade, the *new* product code has never been installed, so `NOT Installed` evaluates to true. This causes install CAs to fire with default/empty MSI property values (the wizard does not run during upgrade), which would:

- Overwrite `appsettings.json` with empty configuration values
- Recreate the Task Scheduler task with potentially stale registry transport values
- Recreate the Firewall rule with the default port (potentially wrong)
- Trigger the env var CA (though the S-005 null guard prevents damage)

**Fix:** Guard all install CAs with `NOT WIX_UPGRADE_DETECTED` in addition to `NOT Installed`. The WiX `MajorUpgrade` element sets `WIX_UPGRADE_DETECTED` (to the old product's ProductCode) whenever upgrading from an older version. This suppresses all install CAs during upgrade, preserving existing artifacts.

### 2.2 Uninstall CA Conditions

All uninstall CAs are conditioned on `REMOVE="ALL"`. During a major upgrade, the old product is fully removed — `REMOVE="ALL"` is set and uninstall CAs fire, removing the Task Scheduler task and Firewall rule.

**Fix:** Guard all uninstall CAs with `NOT UPGRADINGPRODUCTCODE`. The MSI engine sets `UPGRADINGPRODUCTCODE` (to the new product's ProductCode) on the old product during the removal phase of a major upgrade. This suppresses uninstall CAs during upgrade, preserving artifacts.

**Note:** Since no MSI has been deployed yet (v1.0 is still in development), this fix is applied to all CA conditions before the first release. There is no "first upgrade problem" — the corrected conditions will be in the very first deployed MSI.

### 2.3 Install Directory Preservation

During upgrade, the wizard does not run and the `INSTALLFOLDER` property defaults to `C:\PcsRemote\`. If the user chose a different directory during the original install, the upgrade would install files to the wrong location.

**Fix:** Add a registry search that detects the previous install directory from the registry transport (`HKLM\SOFTWARE\PcsRemote\Install`, `InstallFolder` value). If the key exists, it overrides `INSTALLFOLDER` with the previous value. On fresh install, the key does not exist, so the wizard default applies.

This uses the 32-bit registry view (WOW6432Node), consistent with the 32-bit MSI's WriteRegistryValues and WixQuietExec (established in S-004/S-005). The `INSTALLFOLDER` property must remain Public and Secure so that the AppSearch-resolved value flows through to deferred custom actions.

### 2.4 Migration from PowerShell Deployment

The first MSI install on a machine with an existing PowerShell-deployed instance is a **fresh install** (`NOT Installed` is true, `WIX_UPGRADE_DETECTED` is not set). All install CAs fire and handle each artifact type:

- **Task Scheduler:** `InstallTaskScheduler.ps1` uses `Register-ScheduledTask -Force`, which replaces any existing `PcsRemote` task — PowerShell-deployed or otherwise.
- **Firewall:** `InstallFirewall.ps1` uses `Remove-NetFirewallRule -ErrorAction SilentlyContinue` then `New-NetFirewallRule` — replaces any existing `PcsRemote-HTTP` rule.
- **Environment variable:** `InstallEnvVar.ps1` overwrites the existing `PcsPro__Password` value with the wizard-entered password (or skips if empty, per S-005 null guard). If the env var was previously set by `Deploy-PcsRemote.ps1`, the wizard value takes precedence.
- **Configuration:** `WriteConfig.ps1` writes a fresh `appsettings.json` with wizard-collected values. Any pre-existing `appsettings.json` from a PowerShell deployment is overwritten (the `NeverOverwrite` component attribute only prevents overwrite when the component is already *installed* by MSI — it does not detect pre-existing non-MSI files).
- **Log files and YouTube OAuth tokens:** Left in place (no CA touches them).

No additional migration logic is needed beyond what S-004/S-005 already provide.

### 2.5 MajorUpgrade Element

The `MajorUpgrade` element already exists with `Schedule="afterInstallExecute"`. With the corrected CA conditions (§2.1, §2.2), this schedule works correctly:

1. New product installs: files replaced, WriteRegistryValues runs, install CAs suppressed
2. Old product removed: RemoveRegistryValues, RemoveFiles run; uninstall CAs suppressed; component reference counting preserves files owned by the new product

No schedule change is required.

### 2.6 Registry Transport During Upgrade

WriteRegistryValues is a standard MSI action and runs unconditionally during upgrade. It writes default MSI property values to the registry transport (wizard did not run, so values like Port default to `5000`). These stale values are harmless because:

- Install CAs are suppressed — no script reads the transport during upgrade
- Uninstall CAs use hardcoded names, not registry values
- The registry transport is cleaned up on standalone uninstall (RemoveRegistryValues)

The only observable effect: after an upgrade, the registry transport briefly contains default values instead of the original wizard values. This is cosmetic and does not affect application behaviour. Note that AppSearch runs before WriteRegistryValues in the standard MSI sequence, so the §2.3 registry search reads the *previous* install's transport values before they are overwritten by the current install's defaults.

### 2.7 NeverOverwrite + Config CA Suppression

Configuration preservation across upgrades relies on two independent mechanisms:

1. **File level:** The `AppSettingsComponent` has `NeverOverwrite="yes"` — MSI does not replace the existing file
2. **CA level:** The config write CA is suppressed by `NOT WIX_UPGRADE_DETECTED` — no CA overwrites the file

Both mechanisms must be in place. `NeverOverwrite` alone is insufficient (the CA writes directly to the file path, bypassing the file component).

---

## 3. Changes Required

### 3.1 Package.wxs — Install CA Conditions

Update all install CA conditions in `InstallExecuteSequence` from:

```
NOT Installed
```

to:

```
NOT Installed AND NOT WIX_UPGRADE_DETECTED
```

**Affected CAs (setter + deferred pairs):**
- CA_SetWriteConfigData / CA_WriteConfigExec
- CA_SetInstallTaskSchedulerData / CA_InstallTaskSchedulerExec
- CA_SetInstallFirewallData / CA_InstallFirewallExec
- CA_SetInstallEnvVarData / CA_InstallEnvVarExec

Total: 8 condition updates.

### 3.2 Package.wxs — Uninstall CA Conditions

Update all uninstall CA conditions in `InstallExecuteSequence` from:

```
REMOVE="ALL"
```

to:

```
REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE
```

**Affected CAs (setter + deferred pairs):**
- CA_SetUninstallFirewallData / CA_UninstallFirewallExec
- CA_SetUninstallTaskSchedulerData / CA_UninstallTaskSchedulerExec

Total: 4 condition updates.

### 3.3 Package.wxs — Install Directory Detection

Add a registry search on the `INSTALLFOLDER` property so that upgrades detect the previous install directory. The search reads from the same registry transport key used by the install CAs. On fresh install, the key does not exist and the existing default value (`C:\PcsRemote\`) applies.

### 3.4 IS-014 Update

Mark S-006 as delivered in the IS coverage tracking.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | All install CA conditions include `NOT WIX_UPGRADE_DETECTED` guard |
| AC-2 | All uninstall CA conditions include `NOT UPGRADINGPRODUCTCODE` guard |
| AC-3 | `INSTALLFOLDER` property has a registry search that detects the previous install directory from the 32-bit registry view (`HKLM\SOFTWARE\PcsRemote\Install`), consistent with the S-004/S-005 transport |
| AC-4 | On fresh install: all install CAs fire, all artifacts created (no regression from S-004/S-005) |
| AC-5 | On standalone uninstall: all uninstall CAs fire, Task Scheduler and Firewall artifacts removed (no regression) |
| AC-6 | Build: 0 errors, 0 warnings; existing test suite passes (no new tests — upgrade behaviour validated in S-008) |
| AC-7 | Migration from PowerShell deployment: each artifact type's handling is confirmed by design review of S-004/S-005 install scripts (config via WriteConfig, system artifacts via S-005 CAs, logs/tokens untouched by design); runtime verification deferred to S-008 smoke test |

**Outcome verification note:** The core upgrade outcomes — silent upgrade preserves config, Task Scheduler, Firewall, and env var; install directory is reused; upgraded product version is correct in ARP — require building two MSIs with different version numbers and executing the full install → upgrade → verify cycle. This end-to-end verification is the responsibility of S-008 (Smoke Test). S-006 delivers the correct CA conditions and registry search; S-008 validates the runtime behaviour.

---

## 5. Branching & Commits

- **Branch:** `feature/S-006-migration-major-upgrade`
- **Commit strategy:** Small, focused commits scoped to each logical change. Squash-merged to master after review approval.

---

## 6. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | `WIX_UPGRADE_DETECTED` or `UPGRADINGPRODUCTCODE` property name misspelled — undefined MSI properties evaluate to empty string (not a build error), so the guard condition would silently evaluate as if the property were unset | Low risk. Property names are well-documented in WiX MajorUpgrade documentation and used across the WiX ecosystem. A misspelling would not fail the build but would cause install CAs to fire during upgrade (or uninstall CAs to fire during upgrade removal). Runtime verification in S-008 smoke test (install v1 → upgrade v2 → verify artifacts preserved) catches this class of error. |
| R-2 | Registry search for INSTALLFOLDER reads from 64-bit registry view instead of WOW6432Node | Low risk. The 32-bit MSI's RegistrySearch defaults to the 32-bit registry view, consistent with WriteRegistryValues. Same WOW6432Node alignment established in S-004/S-005. |
| R-3 | Stale registry transport values after upgrade cause confusion during debugging | Low risk. Documented in §2.6. Values are cosmetic and cleaned up on next uninstall. |

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-22 | Opus 4.7, GPT 5.4 | REQUEST CHANGES — Opus: 1 HIGH (R-1 fail-fast claim incorrect), 2 MEDIUM (AC-7 non-falsifiable, AC-3 missing registry view), 3 LOW (AC-6 no new tests, INSTALLFOLDER Secure note, AppSearch ordering). GPT: 1 HIGH (ACs incomplete for upgrade outcomes), 1 MEDIUM (migration scope unclear for config/env var). All 8 findings accepted and applied in v0.2. |
| R2 | 2026-04-22 | Opus 4.7, GPT 5.4 | Opus: APPROVE — all R1 fixes verified, 0 findings. GPT: REQUEST CHANGES — 1 HIGH (outcome deferral contradicts IS verification intent — REJECTED: IS verification intent ≠ runtime test mandate, S-008 is designed for lifecycle testing), 1 MEDIUM (AC-7 "S-005 scripts" too narrow — ACCEPTED: reworded to S-004/S-005). Effective unanimous approval (1× APPROVE, 1× sole finding rejected per IS scoping). |
