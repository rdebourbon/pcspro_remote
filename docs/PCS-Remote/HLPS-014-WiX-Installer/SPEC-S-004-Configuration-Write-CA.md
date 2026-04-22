# SPEC-S-004 — Configuration Write Custom Action

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **IS Step** | S-004 |
| **HLPS** | HLPS-014 (APPROVED) |
| **Author** | Copilot |
| **Created** | 2026-04-22 |
| **Version** | 0.4 |

---

## 1. Objective

Implement a deferred PowerShell custom action that writes wizard-collected configuration values into `appsettings.json` on fresh install. This CA bridges the gap between S-003 (wizard UI that collects values as MSI properties) and a working application configuration. On upgrade, the CA is skipped entirely — `NeverOverwrite` on the file component (S-002) preserves user-modified config.

---

## 2. Scope

### 2.1 Custom Action Mechanism

The CA uses PowerShell via `WixQuietExec` (32-bit, provided by `WixToolset.Util.wixext` via `Wix4UtilCA_X86`, already referenced). The 32-bit CA variant is required because the MSI package is 32-bit — using `WixQuietExec64` would spawn 64-bit PowerShell, which reads the native 64-bit registry hive instead of the WOW6432Node hive where the 32-bit MSI writes the transport values. The pattern:

1. **Registry-based value transport** — Wizard values are written to `HKLM\SOFTWARE\PcsRemote\Install` as MSI `RegistryValue` components using `[PROPERTY]` Formatted field resolution. This is handled by MSI's standard `WriteRegistryValues` action during install — no shell quoting or command-line construction involved, eliminating injection risks entirely. The registry keys are removed on uninstall as standard component cleanup.
2. A **property-setter CA** runs in the immediate phase to set the deferred CA's command line. The only substituted value is `[INSTALLFOLDER]` (a directory path — safe from injection by definition on Windows).
3. A **deferred CA** (`Execute="deferred"`, `Impersonate="no"`) invokes `WixQuietExec` to run the script, which reads all wizard values from the registry path.

### 2.2 Execution Condition

The CA must be conditioned on **fresh install only**: `NOT Installed`. This ensures:

- Fresh install: CA executes, writes wizard values into `appsettings.json`.
- Upgrade: CA does not execute; `NeverOverwrite` preserves existing file.
- Repair: CA does not execute; existing config is preserved.

### 2.3 Configuration Values Written

The CA reads the installed `appsettings.json` (placed by InstallFiles), modifies or adds the following paths, and writes it back:

| JSON Path | Source | Notes |
|---|---|---|
| `Kestrel:Endpoints:Http:Url` | `http://0.0.0.0:{HTTP_PORT}` | Port from wizard; modifies existing key |
| `PcsPro:ExecutablePath` | `{PCSPRO_EXEPATH}` | Path from wizard; modifies existing key |
| `PcsPro:WorkingDirectory` | Parent directory of `{PCSPRO_EXEPATH}` | Derived at CA runtime; modifies existing key |
| `PcsPro:AutoLaunch` | `true` | Production default (template has `false`); modifies existing key |
| `PcsPro:UseMock` | `false` | Production default (confirms template value); modifies existing key |
| `YouTube:ClientId` | `{YOUTUBE_CLIENTID}` | From wizard (may be empty); **adds key** — not in template |
| `YouTube:ClientSecret` | `{YOUTUBE_CLIENTSECRET}` | From wizard (may be empty); **adds key** — not in template |
| `YouTube:LiveStreamId` | `{YOUTUBE_LIVESTREAMID}` | From wizard (may be empty); **adds key** — not in template |
| `YouTube:UseMock` | `false` | Production default (confirms template value); modifies existing key |

All other JSON paths (Logging, AllowedHosts, Scoreboard, DebugSection, PcsPro:LoginScreenTimeoutSeconds, PcsPro:Mock, PcsPro:Password, YouTube:BroadcastTitleTemplate, etc.) are preserved unchanged from the template.

### 2.4 Working Directory Derivation

`PcsPro:WorkingDirectory` is derived as the parent directory of the `PCSPRO_EXEPATH` property value. For example, if the user enters `C:\Program Files (x86)\PCS Pro\cricket.exe`, the working directory becomes `C:\Program Files (x86)\PCS Pro`. This derivation is performed within the PowerShell script at CA runtime. If `PCSPRO_EXEPATH` is empty, `WorkingDirectory` is written as an empty string (`""`), not `null`.

### 2.5 Credential Safety

S-004 handles exactly one credential: `YOUTUBE_CLIENTSECRET`. The other credential (`PCSPRO_PASSWORD`) is entirely handled by S-005 and is **not referenced** anywhere in S-004's property-setter, command line, or script.

Three layers of protection prevent credential leakage:

1. **MSI property level:** `YOUTUBE_CLIENTSECRET` is marked `Hidden="yes"` (S-003), which suppresses its value from verbose MSI property dumps.
2. **CustomAction level:** Both the property-setter CA and the deferred CA must carry `HideTarget="yes"`, which prevents the `CustomActionData` payload from appearing in verbose MSI logs.
3. **Script level:** The PowerShell script must not echo, log, or expose `YOUTUBE_CLIENTSECRET` to stdout, stderr, or any log file.

### 2.6 Scheduling

The CA must execute **after** `WriteRegistryValues` (so both the template `appsettings.json` and the registry transport values exist) and **before** `InstallFinalize`. The deferred CA is sequenced in the `InstallExecuteSequence`.

### 2.7 Fail-Forward

Per HLPS I-C-3, if the CA fails (e.g., PowerShell error, file access issue), the install should complete with a warning — files are installed successfully. The operator can manually edit `appsettings.json` post-install using the Configuration Guide.

**Implementation note:** WiX `WixQuietExec` does not natively support fail-forward — a non-zero exit code from the child process causes the CA to return `ERROR_INSTALL_FAILURE`, triggering rollback. To achieve fail-forward, the PowerShell command must use a `try/catch` wrapper that catches all exceptions, writes a warning message to the CA log file (see §2.9), and exits with code 0 regardless of outcome. This keeps the deferred CA non-blocking while still recording the failure for operator diagnosis.

### 2.8 WiX Authoring

The CA requires additions to `Package.wxs`:

1. **Registry transport component** — writes wizard property values to `HKLM\SOFTWARE\PcsRemote\Install` using MSI Formatted field resolution (`[PROPERTY]` syntax). Removed on uninstall as standard component cleanup.
2. **Property-setter CustomAction** — sets the deferred CA's command line to invoke the PowerShell script from `[INSTALLFOLDER]`. Must carry `HideTarget="yes"`.
3. **Deferred CustomAction** — references `WixQuietExec` (32-bit) DLL entry point from the Util extension (`Wix4UtilCA_X86`). Must carry `Execute="deferred"`, `Impersonate="no"`, `HideTarget="yes"`.
4. **InstallExecuteSequence** entries — schedules the property-setter before the deferred CA, and the deferred CA after `WriteRegistryValues` with condition `NOT Installed`.

No new `.wxs` files are needed — all authoring fits within `Package.wxs`.

### 2.9 File Encoding and Logging

- The PowerShell script must write `appsettings.json` as **UTF-8 without BOM**. This is required for `Microsoft.Extensions.Configuration.Json` compatibility on both Windows PowerShell 5.1 (which defaults to UTF-16) and PowerShell 7+.
- CA failure warnings are written to `[INSTALLFOLDER]\install-config-ca.log`. This deterministic path allows operators and the Operational Guide to reference a known location for CA diagnostic output.

### 2.10 Input Properties

S-004's property-setter CA references exactly these MSI properties:

| Property | Purpose in S-004 |
|---|---|
| `PCSPRO_EXEPATH` | Written to `PcsPro:ExecutablePath`; parent derived for `WorkingDirectory` |
| `HTTP_PORT` | Composed into `Kestrel:Endpoints:Http:Url` |
| `YOUTUBE_CLIENTID` | Written to `YouTube:ClientId` |
| `YOUTUBE_CLIENTSECRET` | Written to `YouTube:ClientSecret` |
| `YOUTUBE_LIVESTREAMID` | Written to `YouTube:LiveStreamId` |
| `INSTALLFOLDER` | Path to installed `appsettings.json` and CA log file |

`PCSPRO_PASSWORD` is **not used** by S-004.

---

## 3. Out of Scope

- `PCSPRO_PASSWORD` — handled entirely by S-005 (environment variable CA).
- Task Scheduler, Firewall, environment variable CAs — all S-005.
- Upgrade/repair config handling — handled by `NeverOverwrite` (S-002) and CA condition.
- Validation of wizard inputs (e.g., checking that PCSPRO_EXEPATH exists) — deferred per HLPS I-C-3 fail-forward.

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `Package.wxs` contains a `ConfigTransportComponent` with `RegistryValue` entries that write `[PCSPRO_EXEPATH]`, `[HTTP_PORT]`, `[YOUTUBE_CLIENTID]`, `[YOUTUBE_CLIENTSECRET]`, `[YOUTUBE_LIVESTREAMID]`, and `[INSTALLFOLDER]` to `HKLM\SOFTWARE\PcsRemote\Install` using MSI Formatted field resolution. |
| AC-2 | `Package.wxs` contains a deferred CA (`Execute="deferred"`, `Impersonate="no"`, `HideTarget="yes"`) referencing `WixQuietExec` (32-bit, `Wix4UtilCA_X86`). |
| AC-3 | The deferred CA is sequenced after `WriteRegistryValues` and conditioned on `NOT Installed`. |
| AC-4 | The PowerShell script reads the installed `appsettings.json`, modifies existing paths and adds the three YouTube keys listed in §2.3, and writes the file back. All other JSON paths are preserved. |
| AC-5 | `PcsPro:WorkingDirectory` is derived as the parent directory of `PCSPRO_EXEPATH` within the PowerShell script. If `PCSPRO_EXEPATH` is empty, `WorkingDirectory` is written as `""`. |
| AC-6 | `PcsPro:AutoLaunch` is set to `true` and `PcsPro:UseMock` is set to `false` (production defaults). |
| AC-7 | The PowerShell script does not echo, log, or expose `YOUTUBE_CLIENTSECRET` to stdout, stderr, or any log file. Both CAs carry `HideTarget="yes"`. |
| AC-8 | The PowerShell script uses a `try/catch` wrapper that exits with code 0 on failure (fail-forward per I-C-3), writing a warning to `[INSTALLFOLDER]\install-config-ca.log`. |
| AC-9 | The output `appsettings.json` is written as UTF-8 without BOM. |
| AC-10 | The installer project builds with 0 errors, 0 warnings. |
| AC-11 | The full solution builds and all existing tests pass (no regressions). |

---

## 5. Decision Log

| ID | Decision | Rationale |
|---|---|---|
| D-1 | `PcsPro:AutoLaunch` set to `true` on fresh install | The template ships with `false` (safe default for development). Production installs require the application to auto-launch with PCS Pro — this is the primary operational mode for the garage PC. The operator can change this post-install via the Configuration Guide. |
| D-2 | Registry-based value transport instead of Base64-encoded JSON | MSI `SetProperty` (Type 51 CA) only performs text substitution (`[PROPERTY]` → value) — it cannot compute Base64 encoding. Only C# DTF CAs can call `Session.SetProperty()` with computed values, but DTF was rejected (I-U-2). Registry transport via `RegistryValue` components with Formatted field resolution eliminates injection risks entirely and uses standard MSI infrastructure. |
| D-3 | Plaintext OAuth credentials in HKLM registry during installed lifetime — accepted risk | `YOUTUBE_CLIENTSECRET` is stored in `HKLM\SOFTWARE\PcsRemote\Install` as plaintext until uninstall. This is the same exposure level as `appsettings.json` itself (also plaintext on the same filesystem, readable by local administrators). The deployment target is a single-user garage PC. Registry keys are removed on uninstall via standard component cleanup. |

---

## 6. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-22 | Opus 4.7, GPT 5.4 | REQUEST CHANGES — 2 HIGH (unsafe command-line substitution, HideTarget missing), 4 MEDIUM (UTF-8 encoding, AutoLaunch citation, log filename, add-vs-modify YouTube keys), 1 LOW (empty WorkingDirectory edge case). All accepted; applied in v0.2. |
| R2 | 2026-04-22 | Opus 4.7, GPT 5.4 | APPROVED — all R1 fixes verified. |
| — | 2026-04-23 | Code Review R1 | REQUEST CHANGES — implementation used `-File` positional args instead of encoded payload (spec violation). Base64 found infeasible with MSI SetProperty; adopted registry transport (D-2). Spec bumped to v0.3. |
| — | 2026-04-23 | Code Review R2 | REQUEST CHANGES — WOW64 registry mismatch (WixQuietExec64 spawns 64-bit PS reading wrong hive), TrimEnd breaks root paths, ConvertTo-Json depth too shallow. Fixed: switched to WixQuietExec, removed TrimEnd, bumped depth to 32. Plaintext HKLM credentials accepted as D-3. |
| — | 2026-04-23 | Code Review R3 | APPROVED (unanimous: Opus 4.7, GPT 5.4, Sonnet 4.6) — all R2 fixes verified, no regressions. Spec updated to v0.4 to align WixQuietExec references. |
