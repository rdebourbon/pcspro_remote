# SPEC-S-005 — System Artifact Custom Actions

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **IS Step** | S-005 |
| **HLPS** | HLPS-014 (APPROVED) |
| **Author** | Copilot |
| **Created** | 2026-04-23 |
| **Version** | 0.3 |

---

## 1. Objective

Implement three deferred PowerShell custom actions that create the system artifacts required for PCS Remote to operate as an auto-starting service-like application on the garage PC:

1. **Task Scheduler task** — auto-starts PCS Remote on user logon.
2. **Windows Firewall rule** — allows inbound HTTP traffic on the configured port.
3. **Environment variable** — sets the PCS Pro password for the application to read at runtime.

Each CA has install and uninstall behaviour. All three follow the fail-forward strategy (HLPS I-C-3).

---

## 2. Scope

### 2.1 Task Scheduler CA

**Install behaviour:** Create or replace the `PcsRemote` scheduled task using PowerShell `ScheduledTask` cmdlets (`Register-ScheduledTask -Force`). The task settings must match the existing `Deploy-PcsRemote.ps1` behaviour (HLPS scope item 4):

- **Action:** Execute `PcsRemote.Web.exe` (or the main executable) from `INSTALLFOLDER`, with working directory set to `INSTALLFOLDER`.
- **Trigger:** `AtLogOn -User <LogonUser>` (user-bound, not any-user) — bound to the interactive logon user at install time (see §2.4 for `LogonUser` source and format).
- **Principal:** `LogonType=Interactive`, `RunLevel=Limited` — no elevation at logon, no password stored. The task runs under the interactive token of the logon user.
- **Settings:** `ExecutionTimeLimit=0` (no timeout), `MultipleInstances=IgnoreNew`, `RestartCount=999`, `RestartInterval=30s`. These values are prescribed by HLPS scope item 4 and match the existing `Deploy-PcsRemote.ps1`.

**Uninstall behaviour:** Stop any running instance of the task (`Stop-ScheduledTask`, suppress errors if not running), then remove the `PcsRemote` task via `Unregister-ScheduledTask`. Suppress errors if the task does not exist.

**I-U-3 resolution:** PowerShell `ScheduledTask` cmdlets (not `schtasks.exe`, not COM API). Rationale: the existing deployment script already uses these cmdlets successfully; they provide full control over all required settings; they are consistent with the PowerShell hosting model chosen in I-U-2; and they are available on all supported Windows versions (10+).

### 2.2 Windows Firewall CA

**Install behaviour:** Create the `PcsRemote-HTTP` inbound TCP firewall rule for the wizard-configured port (`HTTP_PORT`). Use `Remove-NetFirewallRule` then `New-NetFirewallRule` (atomic replace is not available via `New-NetFirewallRule`). The rule settings must match `Deploy-PcsRemote.ps1`:

- **Name:** `PcsRemote-HTTP`
- **Display name:** `PCS Remote HTTP`
- **Direction:** Inbound
- **Protocol:** TCP
- **Local port:** Value from `HTTP_PORT` registry transport (S-004 pattern)
- **Profile:** Any (default — matches `Deploy-PcsRemote.ps1` which does not restrict profiles; the garage PC network type may be Private or Public depending on configuration)
- **Action:** Allow

**Uninstall behaviour:** Remove the rule via `Remove-NetFirewallRule`. Suppress errors if the rule does not exist.

### 2.3 Environment Variable CA

**Install behaviour:** Set `PcsPro__Password` as a System-scoped environment variable from the wizard `PCSPRO_PASSWORD` value, read from the registry transport (§2.4). Use `[System.Environment]::SetEnvironmentVariable()` with `Machine` target. The variable is only visible to processes started after the install completes — already-running processes do not receive the update (no `WM_SETTINGCHANGE` broadcast is needed because PCS Remote is started by the Task Scheduler at next logon, which inherits the updated system environment).

**Uninstall behaviour:** Leave the environment variable in place (per HLPS scope item 8 and I-U-6 resolution — avoids accidental credential loss).

### 2.4 Registry Transport Extension

S-005 CAs need two additional values that are not in S-004's registry transport:

- `PCSPRO_PASSWORD` — for the environment variable CA. This is a credential and must be transported via registry (never on the CA command line) to prevent process-tree exposure. The same accepted-risk posture applies as S-004 D-3 (plaintext in HKLM until uninstall).
- `LogonUser` — composed from the Windows environment variable `[%USERDOMAIN]` and the MSI built-in property `[LogonUser]` to produce a fully-qualified SAM-format user name (e.g., `DOMAIN\user` on domain-joined machines, or `MACHINENAME\user` on standalone machines). The MSI `LogonUser` property contains only the bare username; `USERDOMAIN` provides the domain or machine qualifier. Even when UAC elevation occurs, `LogonUser` returns the original interactive user, not the elevated admin identity. This is correct per HLPS A-5 ("the installing user account is the same account that will log on to the garage PC for daily operation"). The composed value is written to the registry transport via `[%USERDOMAIN]\[LogonUser]` Formatted field resolution in a single `RegistryValue`.

The `ConfigTransportComponent` from S-004 must be extended with `RegistryValue` entries for both values.

### 2.5 Script Organisation

Each CA should be a separate PowerShell script for maintainability and single-responsibility:

- `InstallTaskScheduler.ps1` — install-side Task Scheduler CA
- `UninstallTaskScheduler.ps1` — uninstall-side Task Scheduler removal
- `InstallFirewall.ps1` — install-side Firewall CA
- `UninstallFirewall.ps1` — uninstall-side Firewall removal
- `InstallEnvVar.ps1` — install-side environment variable CA

Five scripts, each invoked by a dedicated deferred CA pair (immediate setter + deferred executor). All follow the same `WixQuietExec` pattern established in S-004.

### 2.6 CA Scheduling and Conditions

- **Install CAs** (Task Scheduler, Firewall, Env Var): Conditioned on `NOT Installed` (fresh install only). Scheduled after `WriteRegistryValues` (same anchor as S-004).
- **Uninstall CAs** (Task Scheduler, Firewall): Conditioned on `REMOVE="ALL"` (full uninstall only — not triggered during upgrade). Scheduled before `RemoveFiles` so they run while the scripts are still on disk.
- The env var CA has no uninstall counterpart (by design — HLPS item 8).

**Upgrade note:** The `NOT Installed` condition is true during a major upgrade's new-product install phase. S-006 will revisit these conditions to ensure system artifacts are not redundantly recreated during upgrades. For S-005, the conditions are correct for the fresh-install and full-uninstall scenarios that are in scope.

### 2.7 Fail-Forward

All CAs follow the same fail-forward pattern as S-004: `try/catch` wrapper, exit 0 on failure, log to `[INSTALLFOLDER]\install-{artifact}-ca.log` with `%TEMP%` fallback. Each script has its own log file for independent diagnosis.

Deferred CAs that exit 0 cannot display UI warnings — the log file is the primary visibility mechanism. The log file locations will be documented in the Operational Guide addendum (S-008) so operators know where to check after a degraded install.

**Rollback note:** If the MSI transaction rolls back for reasons outside the CAs (e.g., user cancellation, disk space failure), system artifacts (Task Scheduler task, Firewall rule, env var) created by already-executed CAs will persist. This is consistent with the fail-forward strategy (HLPS I-C-3) — the Operational Guide documents manual remediation for orphaned artifacts.

### 2.8 Credential Safety

`PCSPRO_PASSWORD` is the second credential handled by the installer (after `YOUTUBE_CLIENTSECRET` in S-004). Protection layers:

1. **MSI property level:** `PCSPRO_PASSWORD` is marked `Hidden="yes"` (S-003).
2. **Registry transport:** The password is transported via the `HKLM\SOFTWARE\PcsRemote\Install` registry key (same as S-004). The accepted-risk posture from S-004 D-3 applies (plaintext in HKLM until uninstall, same exposure as appsettings.json).
3. **Command-line safety:** No CA command line contains the password value. All secret values are read by scripts from the registry, never passed as arguments. This prevents process-tree exposure via `Get-CimInstance Win32_Process`.
4. **CustomAction level:** All CAs that reference password data must carry `HideTarget="yes"`.
5. **Script level:** The script must not echo, log, or expose `PCSPRO_PASSWORD` to stdout, stderr, or any log file.

### 2.9 WiX Authoring

Additions to `Package.wxs`:

1. **File components** for the 5 new PowerShell scripts.
2. **Registry transport extension** — additional `RegistryValue` entries for `Password` and `LogonUser`.
3. **10 CustomAction elements** — 5 setter + 5 deferred CAs (3 install + 2 uninstall).
4. **InstallExecuteSequence** entries for all 10 CAs with appropriate conditions.
5. **Feature** — component refs for all new script files.

---

## 3. Out of Scope

- Migration logic (detecting/removing pre-existing PowerShell-deployed artifacts) — S-006.
- Major upgrade behaviour — S-006.
- Configuration write (`appsettings.json`) — S-004 (delivered).
- Task Scheduler task name or settings changes after install — post-install operational concern.

**Verification mode:** All acceptance criteria are verified via manual operator checklist / smoke test on a test machine. The MSI CA execution sequence cannot be exercised in CI without a real install. S-008 (Documentation & Smoke Test) will formalize the checklist.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | Five PowerShell scripts are created and installed to `INSTALLFOLDER`. |
| AC-2 | `Package.wxs` contains 10 CAs (5 immediate setters + 5 deferred) following the `WixQuietExec` pattern with `HideTarget="yes"`. |
| AC-3 | Install CAs are conditioned on `NOT Installed`; uninstall CAs are conditioned on `REMOVE="ALL"`. S-006 may refine conditions for upgrade scenarios. |
| AC-4 | After fresh install, the `PcsRemote` scheduled task exists with: ONLOGON trigger bound to the installing user, `LogonType=Interactive`, `RunLevel=Limited`, `RestartCount=999`, `RestartInterval=30s`, `ExecutionTimeLimit=0`, `MultipleInstances=IgnoreNew`. |
| AC-5 | After fresh install, the `PcsRemote-HTTP` firewall rule exists: inbound TCP, configured port, `Profile=Any`, `Action=Allow`. |
| AC-6 | After fresh install, `[System.Environment]::GetEnvironmentVariable('PcsPro__Password', 'Machine')` returns the wizard-entered password. |
| AC-7 | After uninstall, the `PcsRemote` scheduled task no longer exists (verified via `Get-ScheduledTask` or Task Scheduler UI). |
| AC-8 | After uninstall, the `PcsRemote-HTTP` firewall rule no longer exists (verified via `Get-NetFirewallRule` or Firewall UI). |
| AC-9 | After uninstall, `PcsPro__Password` environment variable is preserved (not removed). |
| AC-10 | All scripts follow fail-forward: `try/catch`, exit 0, log to individual `install-{artifact}-ca.log` files. |
| AC-11 | `PCSPRO_PASSWORD` is not echoed, logged, or exposed by any script. No CA command line contains the password value. All CAs referencing it carry `HideTarget="yes"`. |
| AC-12 | I-U-3 in the HLPS unknowns register is updated to "Resolved" with the chosen approach. |
| AC-13 | The installer project builds with 0 errors, 0 warnings. |
| AC-14 | The full solution builds and all existing tests pass (no regressions). |

---

## 5. Decision Log

| ID | Decision | Rationale |
|---|---|---|
| D-1 | PowerShell `ScheduledTask` cmdlets for Task Scheduler (resolves I-U-3) | Consistent with existing `Deploy-PcsRemote.ps1`; full control over all required settings; available on Windows 10+; consistent with PowerShell hosting model (I-U-2). |
| D-2 | Separate script per CA (5 scripts) rather than a single multi-function script | Single-responsibility; independent fail-forward logging; easier to debug individual artifacts; each script can be tested in isolation. |
| D-3 | Logon user from MSI `[LogonUser]` property, not from wizard input | The installing user IS the daily-use account (HLPS A-5). Eliminates one more wizard field and reduces operator error risk. |

---

## 6. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-23 | Opus 4.7, GPT 5.4 | REQUEST CHANGES — 4 HIGH (principal params, LogonUser semantics, password process-tree exposure, upgrade condition ambiguity), 5 MEDIUM (restart settings citation, firewall profile, env var visibility, rollback, uninstall stop-before-remove), 2 LOW (verification mode, D-2 rationale). All accepted findings applied in v0.2; 3 findings rejected (ACs already enumerated, 10-CA count already reconciled, D-2 rationale already present). |
| R2 | 2026-04-23 | Opus 4.7, GPT 5.4 | NEEDS REVIEW / REQUEST CHANGES — Opus: 1 MEDIUM (LogonUser bare-username, ACCEPTED — composed `[USERDOMAIN]\[LogonUser]`), 2 LOW (Stop-ScheduledTask race REJECTED — standard MSI behavior; trigger binding implicit ACCEPTED — clarified). GPT: 1 HIGH (fail-forward visibility REJECTED — I-C-3 says "logs a warning", log file satisfies; deferred CAs cannot display UI by design; Operational Guide is the documented channel per I-R-5). All accepted findings applied in v0.3. |
| R3 | 2026-04-23 | Opus 4.7, GPT 5.4 | **UNANIMOUS APPROVAL** — All R2 fixes verified. No regressions. 1 non-blocking cosmetic observation (D-3 text says `[LogonUser]` — §2.4 governs actual format). |
