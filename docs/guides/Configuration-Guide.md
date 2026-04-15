# PCS Remote — Configuration Guide

This guide is the authoritative reference for configuring PCS Remote on the garage PC. It covers first-time setup, ongoing configuration changes, and all production-relevant settings.

---

## Quick Start — First-Time Deployment Procedure

Follow these steps in order. A newcomer with Administrator access should be able to complete the setup in under 30 minutes using only this guide.

**Prerequisites**: Windows 10 or 11, Administrator account, PCS Pro (cricket.exe) already installed.

1. **Build the deployment artefact**  
   On the developer machine, open PowerShell and run:
   ```powershell
   scripts\publish.ps1
   ```
   This produces a self-contained artefact in `publish\` at the repository root. No .NET runtime needs to be installed separately on the garage PC.

2. **Edit `publish\appsettings.json`** before copying to the garage PC:
   - Set `PcsPro:ExecutablePath` to the full path of `cricket.exe` (e.g., `C:\Program Files (x86)\PCS Pro\cricket.exe`)
   - Set `PcsPro:WorkingDirectory` to the folder containing `cricket.exe`
   - Set `PcsPro:AutoLaunch` to `true` (the shipped file defaults to `false`)
   - Leave `PcsPro:Password` as `""` — the password is set via environment variable in step 3

   > **Important — first-time only**: do not run `git clean` or re-run `publish.ps1` until `Deploy-PcsRemote.ps1` has completed. Either action overwrites the edits you just made.

3. **Run the deployment script** from an elevated PowerShell prompt on the garage PC:
   ```powershell
   scripts\Deploy-PcsRemote.ps1 -AppUser "DOMAIN\username"
   ```
   When prompted, enter the PCS Pro password. The script handles everything: copying files, registering the Task Scheduler task, opening the firewall, and starting the application.

   If the script reports `.new` files, it means `appsettings.json` has changed and requires review:
   - Compare each `.new` file against the existing file and merge any new configuration keys
   - Then start the application manually:
     ```powershell
     Start-ScheduledTask -TaskName PcsRemote
     ```
   - Wait for the tray icon to appear before proceeding to step 4.

4. **Execute the Smoke Test Checklist**  
   Open `docs\guides\Smoke-Test-Checklist.md` and run through all items to verify the deployment.

---

## Update Deployments

For subsequent software releases:

1. Re-run `scripts\publish.ps1` on the developer machine to rebuild from the latest code.
2. Re-run the deployment script (password unchanged):
   ```powershell
   scripts\Deploy-PcsRemote.ps1 -AppUser "DOMAIN\username" -SkipPasswordUpdate
   ```
   The script detects configuration file changes by comparing SHA256 hashes and produces `.new` files for any `appsettings.json` (or variant) whose content has changed. Compare each `.new` file against the existing file and merge any new or changed keys into the live file, then start the application:
   ```powershell
   Start-ScheduledTask -TaskName PcsRemote
   ```

---

## Prerequisites

| Requirement | Notes |
|---|---|
| Windows 10/11 | 64-bit |
| Administrator account | Required for deployment script only |
| .NET runtime | **Not required** — the artefact is self-contained |
| PCS Pro (cricket.exe) | Must be installed before real-mode use |

---

## `appsettings.json` Settings Reference

All production-relevant configuration keys are described below.

### PCS Pro settings

| Key | Type | Default | Description |
|---|---|---|---|
| `PcsPro:AutoLaunch` | boolean | `true`* | If `true`, PCS Pro is launched automatically when the application starts. **The shipped `appsettings.json` sets this to `false` explicitly** — set it to `true` for production use. Omitting the key is equivalent to `true` (the code uses `GetValue("PcsPro:AutoLaunch", defaultValue: true)`). |
| `PcsPro:ExecutablePath` | string | _(empty)_ | Full path to `cricket.exe`, e.g. `C:\Program Files (x86)\PCS Pro\cricket.exe`. Required for real mode. |
| `PcsPro:WorkingDirectory` | string | _(empty)_ | Working directory for the `cricket.exe` process (typically the same folder as the executable). |
| `PcsPro:UseMock` | boolean | `false` | Set to `false` for production (real PCS Pro automation). Set to `true` to use the mock service for testing. Note: this is a top-level key, distinct from the `PcsPro:Mock` subsection (development/test parameters). |
| `PcsPro:Password` | string | `""` | **Leave empty.** The PCS Pro password is stored in the `PcsPro__Password` System environment variable, not here. See [Setting the PCS Pro password](#setting-the-pcs-pro-password) below. |

### Kestrel / network settings

| Key | Type | Default | Description |
|---|---|---|---|
| `Kestrel:Endpoints:Http:Url` | string | `http://0.0.0.0:5000` | Bind address and port. Change the port number here if 5000 conflicts with another application. After changing, also re-run `Deploy-PcsRemote.ps1 -SkipPasswordUpdate -Port <new-port>` to update the Windows Firewall rule. |
| `AllowedHosts` | string | `*` | Host header filtering. The default `"*"` is correct for LAN deployment. Do not restrict without understanding the implications. |

### Scoreboard settings

| Key | Type | Default | Description |
|---|---|---|---|
| `Scoreboard:JpegQuality` | integer | `85` | JPEG compression quality for scoreboard images (1–100). Default 85 is suitable for LAN use; lower values reduce image size at the cost of quality. |

### Logging settings

| Key | Type | Default | Description |
|---|---|---|---|
| `Logging:LogLevel:Default` | string | _(any value)_ | **This key has no effect.** Both `TrayHost/Program.cs` and `Web/Program.cs` configure Serilog with a hardcoded inline definition (no `ReadFrom.Configuration()` call). Serilog replaces the Microsoft.Extensions.Logging pipeline entirely and does not consult this key. Log verbosity is fixed at `Information` minimum level. To enable debug-level logging for troubleshooting, the Serilog configuration in `Program.cs` must be edited to add `.MinimumLevel.Debug()` and the application redeployed. |

### Log files

- **Location**: `<DeployDir>\logs\pcs-remote-YYYYMMDD.log`  
  Example: `C:\PcsRemote\logs\pcs-remote-20260601.log` for 1 June 2026 with the default deployment directory. If your IT contact used a different deployment folder, substitute it for `C:\PcsRemote\`.
- **Rotation**: one file per day; 7-day rolling retention.
- **Note**: the retention limit is hardcoded in `Program.cs` (`retainedFileCountLimit: 7`) and is not configurable via `appsettings.json`.

### Notes on other keys

- **`PcsPro:Mock` subsection** — development/test delay and probability settings used by the mock automation service. Leave these unchanged in production.
- **`appsettings.Development.json`** — present in the deployment directory but never loaded in production. Do not set `DOTNET_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT` to `Development` on the garage PC — either variable causes the Development configuration to load, silently enabling mock mode and disabling real PCS Pro automation.

---

## Setting the PCS Pro password

The PCS Pro password is stored as a System-scoped Windows environment variable named `PcsPro__Password` (note: double underscore). This keeps the plaintext password out of configuration files.

> **Important**: Leave `PcsPro:Password` in `appsettings.json` as `""` (empty). When `PcsPro__Password` is set as a System environment variable, it overrides the `appsettings.json` value. If the environment variable is absent, the JSON value would be used as a fallback — which is why the JSON value must always be left empty to prevent a plaintext credential in a file.

The deployment script (`Deploy-PcsRemote.ps1`) sets the password interactively using a secure prompt. If you need to set or update it manually without re-running the full deployment script:

```powershell
$pwd = Read-Host -AsSecureString "PCS Pro password"
$bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($pwd)
try {
    $plain = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    [System.Environment]::SetEnvironmentVariable("PcsPro__Password", $plain, "Machine")
    $plain = $null
}
finally {
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}
```

The BSTR pointer is captured before conversion so it can be freed via `ZeroFreeBSTR` in the `finally` block, minimising the window during which the plaintext is held in unmanaged memory.

Restart the application or the Task Scheduler task for the change to take effect.

---

## Updating the PCS Pro executable path

If PCS Pro is reinstalled or upgraded (e.g., to a different installation directory):

1. Edit `appsettings.json` in the deployment directory.
2. Update `PcsPro:ExecutablePath` and `PcsPro:WorkingDirectory` to the new paths.
3. Restart the application (right-click tray icon → Exit, then log off and log on).

---

## Switching between mock and real mode

Change `PcsPro:UseMock` in `appsettings.json`, then restart the application.

- `"UseMock": false` — production (real PCS Pro automation)
- `"UseMock": true` — testing with the mock service (no PCS Pro required)

---

## Changing the HTTP port

If port 5000 conflicts with another application:

1. Change `Kestrel:Endpoints:Http:Url` in `appsettings.json` to the new port (e.g., `"http://0.0.0.0:8080"`).
2. Re-run the deployment script to update the Windows Firewall rule:
   ```powershell
   scripts\Deploy-PcsRemote.ps1 -AppUser "DOMAIN\username" -SkipPasswordUpdate -Port 8080
   ```
3. Restart the application.
