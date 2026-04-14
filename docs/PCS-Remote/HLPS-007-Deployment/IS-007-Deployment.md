# IS-007: Deployment & Operations — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-007-Deployment.md |
| **Status** | DRAFT |
| **Version** | 0.8 |
| **Date** | 2026-04-14 |
| **Governing HLPS** | HLPS-007-Deployment.md v0.5 (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Prerequisites** | IS-006 delivered (real FlaUI automation working, all 443 tests passing). |

---

## Overview

This sequence delivers production-ready deployment: one targeted production code fix, a verified self-contained publish artefact, a PowerShell deployment script, a configuration guide, an operational guide for club volunteers, and a smoke test checklist covering all Phase 1 functional areas.

> **Production code change required**: Although this is primarily a deployment and documentation IS, one production code change is mandatory to satisfy D-SC-3 (crash recovery). The existing `Program.cs` top-level catch block exits with code 0, which causes Task Scheduler to treat a crash as a clean exit and never trigger a restart. S-001 delivers this one-line fix before the publish is verified.

Steps are ordered so that the production code fix is made and the publish artefact is verified first (S-001), the deployment automation is scripted and tested second (S-002), and the human-facing documentation is written last (S-003 through S-005) so it accurately describes the final configuration.

Blocking unknown D-U-8 (PCS Pro installation directory) must be resolved before S-005 can be executed on the garage PC. All other unknowns are non-blocking and are addressed within their respective steps.

Steps are identified with stable IDs S-001 through S-005. IDs are never renumbered.

---

## Steps

### S-001 — Exit code fix, self-contained publish verification, and helper script

**What changes:** Three deliverables:

1. **Exit code fix**: In `src/PcsRemote.TrayHost/Program.cs`, add `return 1;` as the last statement inside the top-level `catch` block, immediately after the `Log.Fatal(...)` call. Use `return 1;` — **not `Environment.Exit(1)`**. `Environment.Exit(1)` terminates the CLR immediately and bypasses the `finally` block, preventing `Log.CloseAndFlush()` from running and destroying the crash log entry at exactly the moment it is most needed. `return 1;` stores the exit code and allows the `finally` block to execute normally. The exact code change:

   ```csharp
   catch (Exception ex)
   {
       Log.Fatal(ex, "PCS Remote (TrayHost) terminated unexpectedly");
       return 1; // ← add this line; do NOT use Environment.Exit(1)
   }
   finally
   {
       Log.CloseAndFlush(); // ← this must run; return 1 above allows it
   }
   ```

   This is the only production code change in IS-007.

2. **Publish verification**: Run `dotnet publish src/PcsRemote.TrayHost/PcsRemote.TrayHost.csproj -c Release -r win-x64 --self-contained -o publish/` and capture the output artefact set. Confirm: (a) the command succeeds with zero errors; (b) `PcsRemote.TrayHost.exe` is present in the output directory; (c) `appsettings.json` and `appsettings.Development.json` are present as companion files; (d) no `.NET` runtime folder is required alongside the executable on a clean machine. If any `NU*` or `NETSDK*` warnings are emitted, they must be resolved before proceeding. Record the verified artefact list (file names and approximate sizes) in a comment block at the top of `scripts/publish.ps1` so it is available when `Deploy-PcsRemote.ps1` is written in S-002.

3. **Publish helper script**: Create `scripts/publish.ps1` at the repository root. The script runs the verified publish command, sets the output directory to `publish/` under the repo root (git-ignored), and prints the artefact list on completion. This makes it trivial for developers to rebuild the deployment artefact without memorising the full command.

**Why:** D-SC-3 (crash recovery) cannot be satisfied without the exit code fix — the Task Scheduler restart setting is silently inoperative with exit code 0. The publish verification step catches any configuration issues (missing RID, incompatible TFM, broken static assets) before the deployment script is written around them. The helper script eliminates the risk of a future developer using the wrong publish command. Addresses D-SC-1 and D-SC-3 (exit code prerequisite).

**Dependencies:** None within IS-007.

**Verification intent:** `src/PcsRemote.TrayHost/Program.cs` catch block now returns non-zero exit code. All 443 existing tests pass. `dotnet publish` succeeds with zero errors. `publish/PcsRemote.TrayHost.exe` exists. `scripts/publish.ps1` runs cleanly and prints the artefact list.

---

### S-002 — PowerShell deployment script

**What changes:** Create `scripts/Deploy-PcsRemote.ps1`. The script:

1. **Elevation guard**: Checks that it is running as Administrator; exits with a clear error message if not (`#Requires -RunAsAdministrator`).
2. **Parameters**: Accepts `-DeployDir` (default `C:\PcsRemote\`), `-AppUser` (the Windows account name for the Task Scheduler trigger — prompted if not supplied), `-Port` (TCP port for the **Windows Firewall rule only** — default `5000`; note: changing the Kestrel bind port also requires editing `Kestrel:Endpoints:Http:Url` in `appsettings.json` before running this script — see S-003), and `-SkipPasswordUpdate` (switch; if specified, the password prompt in step 7 is skipped — use for code-only re-deployments where the PCS Pro password has not changed).
3. **Stop running instance**: Before copying any files, the script stops the Task Scheduler task (`Stop-ScheduledTask -TaskName PcsRemote -ErrorAction SilentlyContinue`) and then waits up to 10 seconds for `PcsRemote.TrayHost.exe` to exit (polling `Get-Process` every 500 ms). This prevents `Access Denied` errors caused by file locks on the executable and DLLs during re-deployment. If the process has not exited after 10 seconds, the script issues a forced kill using `Stop-Process -Id <pid> -Force` and waits an additional 5 seconds. If the process is still alive after the forced kill, the script aborts with a clear error message: "Cannot stop PcsRemote.TrayHost.exe (PID `<pid>`). Close it manually and re-run the script." The script must not proceed to file copy if the process is still running.
4. **Artefact copy**: Copies all files from the `publish/` artefact set to `$DeployDir`. Configuration files are treated as follows: `appsettings.Development.json` is always silently overwritten (it is never loaded in production — see S-003; preserving it adds no operator value and would silently block restart on code-only redeployments). All other `appsettings*.json` files (`appsettings.json` and any environment-specific variants) are treated as production configuration: if any of these files already exist in `$DeployDir` (re-deployment), the script compares the SHA256 hash of the incoming file against the existing file using `Get-FileHash`. If the hashes differ, the existing file is preserved and the new version is placed alongside it with a `.new` suffix (e.g., `appsettings.json.new`) so the operator can diff them for new keys. If the hashes are identical, the existing file is silently overwritten (no `.new` file created, auto-start not suppressed). The script prints a warning message listing any `.new` files created, instructing the operator to review them and merge any new configuration keys manually before restarting.
5. **Task Scheduler (idempotent)**: Register the task using `Register-ScheduledTask -Force`, which atomically replaces any existing task named `PcsRemote` in a single operation. Do **not** unregister first then re-register: if re-registration fails after deletion, the system is left with no auto-start mechanism. Task definition:
   - Trigger: `ONLOGON` for the specified `-AppUser` account
   - Action: `$DeployDir\PcsRemote.TrayHost.exe`
   - Working directory (`Start in`): `$DeployDir`
   - Run only when user is logged on (interactive session)
   - On failure: restart after 30 seconds, up to 999 attempts
   - Settings: `ExecutionTimeLimit = PT0S` (no timeout), `MultipleInstances = IgnoreNew`
6. **Windows Firewall rule (idempotent)**: Removes any existing rule named `PcsRemote-HTTP` then creates a new inbound TCP rule for port `$Port` (`New-NetFirewallRule`).
7. **PCS Pro password**: If `-SkipPasswordUpdate` is not specified, checks whether `PcsPro__Password` is already set in the System environment. If already set, prints a prompt: "Password is already configured. Update it? [y/N]" and only proceeds if the operator answers `y`. If not yet set, prompts unconditionally via `Read-Host -AsSecureString`. **Guard**: if `-SkipPasswordUpdate` is specified but `PcsPro__Password` is not set in the System environment, the script must print a warning before proceeding: "⚠ -SkipPasswordUpdate specified but PcsPro__Password is not set. If real mode is enabled (PcsPro:UseMock=false), PCS Pro login will fail. Set the password manually or re-run without -SkipPasswordUpdate." Converts to plain text using `[System.Runtime.InteropServices.Marshal]::PtrToStringBSTR` and `SecureStringToBSTR` only for the duration of the `SetEnvironmentVariable` call; immediately after the `SetEnvironmentVariable` call, frees the BSTR via `[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)` in a `try/finally` block; sets the System-scoped environment variable `PcsPro__Password` via `[System.Environment]::SetEnvironmentVariable`. Note: .NET managed strings are immutable and cannot be zeroed in memory; the `SecureString` itself provides a degree of protection in memory, but once converted to a plain string for the registry call, normal GC rules apply. Freeing the BSTR promptly with `ZeroFreeBSTR` minimises the window during which the plaintext is held in unmanaged memory. The script must not write any credential to disk or to the console output.
8. **Restart after deployment**: If no `.new` configuration files were created in step 4 (first-time deployment or re-deployment with no new keys), starts the Task Scheduler task immediately (`Start-ScheduledTask -TaskName PcsRemote`). If `.new` files exist, does **not** start the task — instead prints a prominent warning: "⚠ Configuration merge required. Review the following .new files, merge any new keys into the existing configuration, then run: `Start-ScheduledTask -TaskName PcsRemote`." This prevents the application starting with incomplete configuration after a release that introduces required new keys.
9. **Summary**: Prints a deployment summary listing all actions taken and their outcomes, including any `.new` configuration files that require review.

**Why:** A single idempotent script that handles first-time deployment and re-deployment identically is the core mechanism for ensuring the system can be maintained by non-developers. Stopping the running instance before copying eliminates `Access Denied` file lock failures; the force-kill fallback handles hung processes. The `-Port` parameter opens the correct firewall port for non-standard configurations. The `-SkipPasswordUpdate` switch avoids unnecessary credential exposure during code-only re-deployments. The `appsettings*.json` merge pattern ensures re-deployments do not silently overwrite configuration, while also not silently discarding new configuration keys added by future releases. Conditional auto-start prevents the application launching with incomplete configuration after a key-adding release. Addresses D-SC-2, D-SC-3 (Task Scheduler), D-SC-6, D-SC-8 (Firewall).

**Dependencies:** S-001 (publish artefact set must exist in `publish/`).

**Verification intent:** Running the script from an elevated PowerShell prompt on the development machine (or a test VM) completes without errors. Re-running it a second time completes without errors (idempotency). Running it without elevation prints a clear error and exits. The Task Scheduler task appears in Task Scheduler with correct settings: correct user, `Start in`, restart settings. The Windows Firewall rule `PcsRemote-HTTP` appears inbound TCP port `$Port` (default 5000; confirm against the value passed to the script). `PcsPro__Password` is set in the System environment — verify with `[System.Environment]::GetEnvironmentVariable("PcsPro__Password","Machine") -ne $null -and [System.Environment]::GetEnvironmentVariable("PcsPro__Password","Machine") -ne ""` (do **not** print the value itself). No password text is visible in the script output or in any log file. Running with `-SkipPasswordUpdate` completes without prompting for a password. Stop-before-copy behaviour: (a) launch the exe manually, wait for the 10-second graceful timeout to elapse — the script must force-kill the process and continue to file copy; (b) if you can arrange for the process to survive the `Stop-Process -Force` call (e.g., by protecting it with a debugger), the script must abort with a clear message.

---

### S-003 — Configuration guide

**What changes:** Create `docs/guides/Configuration-Guide.md`. The guide covers:

- **Quick Start — First-Time Deployment Procedure**: A numbered end-to-end sequence so a newcomer does not need to synthesise the order from four separate documents:
  1. Run `scripts/publish.ps1` to produce the deployment artefact in `publish/`.
  2. Edit `publish/appsettings.json`: set `PcsPro:ExecutablePath` to the full path of `cricket.exe`; set `PcsPro:WorkingDirectory` to the folder containing it; set `PcsPro:AutoLaunch` to `true` (the shipped file defaults to `false`); leave `PcsPro:Password` empty. **For this initial deployment only**: do not run `git clean` or re-run `publish.ps1` until `Deploy-PcsRemote.ps1` has completed — either action will overwrite the edits you just made.
  3. Run `scripts/Deploy-PcsRemote.ps1` from an elevated PowerShell prompt (provide `-AppUser`, enter the PCS Pro password when prompted). If the script reports `.new` files: merge the new configuration keys into the existing files, then run `Start-ScheduledTask -TaskName PcsRemote` from an elevated PowerShell prompt on the garage PC, and verify the tray icon appears before proceeding to step 4.
  4. Execute the Smoke Test Checklist (`docs/guides/Smoke-Test-Checklist.md`).

- **Update Deployments** (new software release): re-run `scripts/publish.ps1` to rebuild the artefact from the latest code, then re-run `scripts/Deploy-PcsRemote.ps1 -SkipPasswordUpdate`. The script detects configuration file changes by comparing SHA256 hashes and produces `.new` files for any `appsettings.json` (or variant) whose content has changed; compare each `.new` file against the existing one and merge any new or changed keys into the live file, then run `Start-ScheduledTask -TaskName PcsRemote` to restart the application.

- **Prerequisites**: .NET 8 runtime not required (self-contained); Windows 10/11; Administrator account for deployment script.
- **`appsettings.json` settings** (all production-relevant keys):
  - `PcsPro:AutoLaunch` — boolean; if `true`, PCS Pro is launched automatically when the application starts; set to `false` for manual-launch mode. **Default when key is absent**: `true` (the code uses `GetValue("PcsPro:AutoLaunch", defaultValue: true)`). Explicitly set this key to `false` if you want to disable auto-launch; omitting it is equivalent to setting it to `true`. **Important**: the `appsettings.json` shipped with the deployment artefact sets `"AutoLaunch": false` explicitly — set this to `true` for production use unless you intend to use manual-launch mode.
  - `PcsPro:ExecutablePath` — full path to `cricket.exe` (e.g., `C:\Program Files (x86)\PCS Pro\cricket.exe`); required for real mode.
  - `PcsPro:WorkingDirectory` — working directory for cricket.exe process (typically the same folder as the executable).
  - `PcsPro:UseMock` — boolean; `false` for production (real PCS Pro automation); `true` for testing with the mock service. Note: this is a top-level key distinct from the `PcsPro:Mock` subsection (development/test parameters not relevant to production).
  - `Kestrel:Endpoints:Http:Url` — bind address and port (default `http://0.0.0.0:5000`); change the port number here if 5000 conflicts with another application.
  - `Scoreboard:JpegQuality` — JPEG compression quality for scoreboard images (1–100; default `85`, suitable for LAN use; lower values reduce image size at the cost of quality).
  - `Logging:LogLevel:Default` — log verbosity (default `Information`; use `Debug` only for troubleshooting as it increases file size significantly).
  - **Log files**: written to `<DeployDir>\logs\pcs-remote-YYYYMMDD.log` (e.g., `C:\PcsRemote\logs\pcs-remote-20260601.log` with the default deploy directory); one file per day, 7-day rolling retention. Note: retention limit (`retainedFileCountLimit: 7`) is hardcoded in `Program.cs` and is not configurable via `appsettings.json`.
  - `AllowedHosts` — host header filtering (default `"*"` is correct for LAN deployment; do not restrict without understanding the implications).
  - Note on `PcsPro:Mock` subsection — development/test delay and probability settings; leave unchanged in production.
  - Note on `appsettings.Development.json` — this companion file is present in the deployment directory but is never loaded in production; do not set `DOTNET_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT` to `Development` on the garage PC — either variable causes the Development config to load, silently enabling mock mode and disabling real PCS Pro automation.
- **Setting the PCS Pro password** (via `PcsPro__Password` System-scoped environment variable):
  - **Important**: Leave the `PcsPro:Password` key in `appsettings.json` empty (`""`). Do not enter the password there — it is stored in plain text and is a security risk. When `PcsPro__Password` is set as a System environment variable, it overrides the `appsettings.json` value; if the environment variable is absent, the JSON value would be used as a fallback — which is why the JSON value must always be left empty.
  - The deployment script sets this interactively. Manual steps if needed: open an elevated PowerShell prompt; run `[System.Environment]::SetEnvironmentVariable("PcsPro__Password","<password>","Machine")`; restart the application or the Task Scheduler task for the change to take effect.
  - Why not `appsettings.json`: storing credentials in a plain-text config file is insecure. The environment variable is stored in the Windows registry (HKLM) and is not human-readable without Administrator access.
- **Updating the PCS Pro executable path** after reinstall or upgrade: edit `appsettings.json` — update `PcsPro:ExecutablePath` and `PcsPro:WorkingDirectory` — then restart the application.
- **Switching between mock and real mode**: change `PcsPro:UseMock` in `appsettings.json`; restart the application.
- **Changing the HTTP port**: change `Kestrel:Endpoints:Http:Url` in `appsettings.json`; re-run `scripts/Deploy-PcsRemote.ps1 -SkipPasswordUpdate -Port <new-port>` to update the Windows Firewall rule to the new port; restart the application.

**Why:** Volunteers and future developers need a single authoritative reference for all configuration decisions. Accurately documenting all keys (including the `UseMock`/`Mock` distinction and the `Development.json` note) prevents misconfiguration. Addresses D-SC-4, D-SC-6.

**Dependencies:** S-001 (publish artefact must be verified so companion file list is accurate), S-002 (deployment script must exist so the password-setting cross-reference is accurate).

**Verification intent:** D-SC-4 — a person who has not previously configured the system can complete a fresh configuration from scratch using only the guide, without agent assistance, within 30 minutes. **Executor**: a developer on the team (not the author of this document). **Sign-off**: the executor records the outcome (pass/fail, any steps that needed clarification) and the result is noted in the delivery record above before the step is marked Delivered.

---

### S-004 — Operational guide

**What changes:** Create `docs/guides/Operational-Guide.md`. The guide is written for club volunteers with no IT background. Sections:

1. **Before the match** — How to confirm the application is running (look for the tray icon; if absent, log on to the garage PC — the application starts automatically within a few seconds of logon via the ONLOGON trigger). If the tray icon still has not appeared, contact your IT contact — they can restart the application by running `Start-ScheduledTask -TaskName PcsRemote` from an elevated PowerShell prompt on the garage PC.
2. **Accessing the control panel** — From the garage PC: right-click the tray icon and click **"Open Browser"** — the control panel opens automatically. From any other device on the club Wi-Fi: open a browser and navigate to `http://<garage-pc-ip>:<port>` where `<port>` is the configured Kestrel port (default `5000`). To find the garage PC's IP address: on the garage PC, open Command Prompt and run `ipconfig`; look for the IPv4 address of the active network adapter. Ask your IT contact to configure a static IP or DHCP reservation so the address never changes.
3. **Status indicators** — Description of each status shown in the header bar of the control panel and what action, if any, the volunteer should take:
   - **PCS Pro not running**: PCS Pro has not started yet. With auto-launch configured, PCS Pro will launch automatically — wait up to 30 seconds and the status will progress to "PCS Pro starting…" automatically. If the status does not change after 30 seconds, check that the garage PC is logged on and contact your IT contact.
   - **PCS Pro starting…**: PCS Pro is starting up. Wait; no action needed.
   - **Logging in…**: PCS Pro is open and the application is logging in automatically. Wait.
   - **Loading matches…**: The application is loading the match list from PCS Pro. Wait a few seconds.
   - **Searching…**: The application is fetching today's matches. Wait.
   - **Select a match**: The match list is loaded. If only one match is available, it is selected automatically. If multiple matches appear, click the card for today's match.
   - **Match loaded**: A match is loaded and live. The scoreboard is available.
   - **Error**: Something went wrong. Check the log file (see §7) and report to your IT contact.
4. **Loading a match** — With AutoLaunch=true (recommended for production), PCS Pro starts automatically on logon and the control panel will already show "Loading matches…", "Searching…", or "Select a match" — skip to selecting a match. Full sequence from a cold start: wait for the status to show **Select a match**; if only one match is available it is selected automatically and the status advances to "Match loaded"; if multiple matches appear, click the card for today's match. **To switch to a different match after loading**: click the **"Change Match"** button on the control panel, then click the correct card.
5. **Manual mode** — Manual mode pauses all automation (PCS Pro is no longer controlled automatically). Use it when you need to operate PCS Pro directly without interference (e.g., correcting an error in the scorer). To enable: right-click the tray icon on the garage PC and click "Switch to Manual Mode" — the web control panel will display a "Manual mode — automation paused by local operator" banner. To disable: right-click the tray icon and click "Resume Automation" — the banner disappears. Note: manual mode can only be toggled from the garage PC (tray icon), not from the web control panel.
6. **Updating the PCS Pro password** — "If the PCS Pro password changes, you will need to update it. Follow these steps: [cross-reference Configuration Guide §password-setting]." Note: this requires Administrator access on the garage PC.
7. **Checking for errors** — How to locate today's log file (default: `C:\PcsRemote\logs\pcs-remote-YYYYMMDD.log` where YYYYMMDD is today's date, e.g., `pcs-remote-20260601.log` for 1 June 2026; if your IT contact used a different deployment folder, substitute it for `C:\PcsRemote\`); what level of detail to share when reporting a problem.
8. **Restarting the application** — Right-click the tray icon → Exit; then log off and log on again (the application restarts automatically within a few seconds of logon via the ONLOGON Task Scheduler trigger). Note: the 30-second restart delay configured for crash recovery does not apply here; a clean exit and re-logon restarts the application immediately.
9. **After a reboot** — If the garage PC has been restarted (e.g., after a power cut or Windows Update): log on to the garage PC if auto-logon is not configured; the application will start automatically within a few seconds of logon (ONLOGON trigger).
10. **SmartScreen prompt** — If Windows shows a "Windows protected your PC" prompt when first running the application: click "More info" then "Run anyway." This is a one-time prompt for unsigned executables.
11. **Updating PCS Pro path** — If PCS Pro is reinstalled or upgraded, the executable path in `appsettings.json` must be updated (cross-reference Configuration Guide §updating-pcs-pro-path).

**Why:** Non-technical operators need a guide that gives them confidence to manage the system on match days without developer assistance. The language must avoid jargon and every step must have a clear outcome. Addresses D-SC-5.

**Dependencies:** S-002 (deployment script and task scheduler must be finalised so guide describes actual deployment directory and process), S-003 (password update cross-reference).

**Verification intent:** D-SC-5 — a person unfamiliar with the system can: access the control panel, identify system status, enable manual mode, and locate today's log file — using only the guide, without agent assistance. **Executor**: a club volunteer or non-technical team member. **Sign-off**: the executor records the outcome (pass/fail, any steps that needed clarification) and the result is noted in the delivery record above before the step is marked Delivered.

---

### S-005 — Smoke test checklist

**What changes:** Create `docs/guides/Smoke-Test-Checklist.md`. The checklist is executed after every deployment on the garage PC. It covers all Phase 1 functional areas (HLPS-003 through HLPS-006). Each item has a clear pass condition and a failure action.

**Checklist items:**

| # | Area | Test | Pass condition | Failure action |
|---|---|---|---|---|
| 1 | Deployment | Application artefact set complete | All expected files present in deploy directory | Re-publish from developer machine (`scripts/publish.ps1`) and redeploy (`scripts/Deploy-PcsRemote.ps1`) |
| 2 | D-SC-1 | Self-contained artefact verification | Confirm `coreclr.dll` and `PcsRemote.TrayHost.exe` are both present in the deployment directory (`$DeployDir`, default `C:\PcsRemote\`) — proves the CLR is bundled. (One-time initial rollout only: optionally verify on a clean Windows 10/11 VM with no .NET runtime installed — not required for subsequent deployments.) | Re-run `scripts/publish.ps1` on a developer machine and redeploy |
| 3 | D-SC-2 | Task Scheduler auto-start | Application tray icon appears within 30s of logon | Check Task Scheduler task status and "Start in" setting |
| 4 | D-SC-8 / HLPS-003 | LAN browser access | Control panel loads at `http://<garage-pc-ip>:<configured-port>` (default port 5000) from a separate device | Check firewall rule; verify correct IP address and port |
| 5 | HLPS-003 | SignalR connection | Status indicator updates in real time (no spinner stuck) | Check browser console for WebSocket errors |
| 6 | HLPS-006 | PCS Pro launch in real mode | PCS Pro (cricket.exe) launches and service reaches `MatchSelectionReady` state (or `MatchLoaded` directly if only one match exists — auto-select fires automatically) | Check `PcsPro:UseMock=false`, `ExecutablePath`, and `PcsPro__Password` |
| 7 | HLPS-004 | Match list | Today's matches appear as match cards (or auto-load if exactly one match exists) | Verify date on garage PC; check PCS Pro data |
| 8 | HLPS-006 | Match load | With multiple matches, click a match card; service reaches MatchLoaded state. With a single match, confirm auto-load fires without user action. | Check PCS Pro UI for dialogs; check log file |
| 9 | HLPS-004 | Scoreboard preview | Scoreboard image appears and refreshes on demand | Check `Scoreboard:JpegQuality`; check PrintWindow availability |
| 10 | HLPS-005 | Manual mode toggle | Right-click the tray icon on the garage PC → "Switch to Manual Mode"; confirm "Manual mode — automation paused by local operator" banner appears in the web control panel. Right-click → "Resume Automation"; confirm banner disappears. | Check ManualModeService wiring in DI; confirm tray icon renders context menu |
| 11 | D-SC-3 | Task Scheduler restart mechanism | Kill `PcsRemote.TrayHost.exe` via Task Manager **Details tab → End process** (not "End task" — that may send WM_CLOSE first and produce exit code 0); application restarts within 60 seconds. Repeat three times in succession; each restart must occur within 60 seconds of the kill (~40 seconds expected). Note: this tests the Task Scheduler restart settings, not the S-001 exit code fix. For S-001 exit code fix: inspect `src/PcsRemote.TrayHost/Program.cs` catch block and confirm `return 1;` is present (code review). | Check Task Scheduler restart settings (delay ≤ 30s, count ≥ 999) |
| 12 | D-SC-6 | Password change | Update `PcsPro__Password` env var; restart application; PCS Pro logs in successfully | Follow Configuration Guide §password-setting |
| 13 | HLPS-004 | Scoreboard refresh | With a match loaded, click "Refresh Scoreboard"; a new image loads without errors | Check `CaptureScoreboardImageAsync` in FlaUI service; check log file |

**Why:** A structured checklist that any developer or club IT contact can follow ensures that every deployment is validated against all Phase 1 capabilities. It also provides a regression baseline for future deployments. The checklist as a whole satisfies D-SC-7 (smoke test covers all Phase 1 functional areas); no individual item covers D-SC-7 alone.

**Dependencies:** S-001–S-004 (all prior deliverables must exist before the checklist can be executed meaningfully). Blocking unknown D-U-8 (PCS Pro install path) must be resolved before items 6–9, 12, and 13 can pass. **Match data prerequisite**: items 6–9 and 13 require PCS Pro to contain at least one match dated today (or a suitable test match); on non-match days, defer these items or create a test fixture in PCS Pro before execution.

**Verification intent:** Execute checklist on garage PC after first deployment; all 13 items pass. **Executor**: the developer performing the first deployment. **Sign-off**: each checklist item is marked pass/fail and the result is recorded in the delivery record above before the step is marked Delivered. Items 6–9, 12, and 13 require D-U-8 (PCS Pro install path) to be resolved first.

---

## Delivery Record

| Step | Status | Commit |
|---|---|---|
| S-001 — Exit code fix, publish verification, helper script | Pending | — |
| S-002 — PowerShell deployment script | Pending | — |
| S-003 — Configuration guide | Pending | — |
| S-004 — Operational guide | Pending | — |
| S-005 — Smoke test checklist | Pending | — |

---

## Review History

### R1 Review (v0.1 → v0.2)

| # | Finding | Severity | Source | Disposition |
|---|---|---|---|---|
| R1-H1 | `Program.cs` exits with code 0 on crash — Task Scheduler restart silently inoperative | HIGH | Sonnet | Accepted — exit code fix added to S-001 |
| R1-H2 | S-002 no stop-before-copy — file locks on re-deployment | HIGH | Opus | Accepted — stop task + process kill added to S-002 |
| R1-H3 | S-004 wrong state names (At Login, Manual don't exist; missing MatchSelectionSearching/Ready) | HIGH | Opus | Accepted — replaced with actual `PcsProState` enum values |
| R1-M1 | SecureString memory clearing overpromised | MEDIUM | Opus | Accepted — claim softened with accurate .NET GC language |
| R1-M2 | `appsettings.json` Password key needs "leave empty" warning | MEDIUM | Opus | Accepted — explicit warning added to S-003 |
| R1-M3 | S-005 #6 pass condition references transient `MatchSelection` state | MEDIUM | Opus | Accepted — corrected to `MatchSelectionReady` |
| R1-M4 | Firewall port hardcoded — no `-Port` parameter | MEDIUM/LOW | Opus+Sonnet | Accepted — `-Port` param added to S-002 |
| R1-M5 | Scoreboard refresh missing from smoke test (D-SC-7 gap) | MEDIUM | Sonnet | Accepted — item #13 added to S-005 |
| R1-M6 | Re-deployment password change needs explicit restart step | MEDIUM | Sonnet | Accepted — restart step added to S-002 |
| R1-M7 | S-003/S-004 UAT verification has no executor or sign-off mechanism | MEDIUM | Sonnet | Accepted — executor and sign-off language added to both |
| R1-M8 | Re-deployment silently discards new `appsettings.json` keys | MEDIUM | Sonnet | Accepted — `.new` file merge pattern added to S-002 |
| R1-L1 | `appsettings.Development.json` preservation inconsistency | LOW | Opus | Accepted — generalised to `appsettings*.json` in S-002 |
| R1-L2 | `AutoLaunch` default-when-absent undocumented | LOW | Opus | Accepted — `GetValue` default documented in S-003 |
| R1-L3 | Log file path pattern slightly misleading | LOW | Opus | Accepted — concrete dated example added to S-004 |
| R1-L4 | S-001 test-suite requirement made accurate | LOW | Sonnet | Accepted — tests now relevant (code change added); requirement retained |
| R1-L5 | S-001 forward reference to S-002 script before it exists | LOW | Sonnet | Accepted — artefact list recorded in `scripts/publish.ps1` instead |

### R2 Review (v0.2 → v0.3)

| # | Finding | Severity | Source | Disposition |
|---|---|---|---|---|
| R2-H1 | `Environment.Exit(1)` presented as equivalent to `return 1`; it skips `finally` and loses crash log | HIGH | Opus+Sonnet | Accepted — `Environment.Exit(1)` removed; `return 1` mandated with code diff and rationale |
| R2-H2 | `-Port` parameter claims to configure Kestrel endpoint but only opens firewall rule | HIGH | Opus+Sonnet | Accepted — parameter scope narrowed to firewall-only; S-003 already documents manual appsettings port change |
| R2-H3 | No force-kill fallback after 10s stop timeout — hung process leaves partial artefact set | HIGH | Opus+Sonnet | Accepted — `Stop-Process -Id -Force` fallback added; abort-with-error if still running |
| R2-M1 | Auto-start before `.new` merge contradicts operator instructions — application starts with incomplete config | MEDIUM | Opus | Accepted — conditional start: skip if `.new` files present; defer to operator |
| R2-M2 | Password unconditionally prompted on every re-deployment — unnecessary credential exposure | MEDIUM | Opus+Sonnet | Accepted — `-SkipPasswordUpdate` switch added; conditional prompt if password already set |
| R2-M3 | No end-to-end procedure in S-003 — D-SC-4 (30-min setup) at risk | MEDIUM | Opus | Accepted — Quick Start numbered procedure added at top of S-003 |
| R2-M4 | Ambiguous wording on `return 1` placement (covered by H1 fix) | MEDIUM | Opus | Accepted — resolved by R2-H1 code diff |
| R2-M5 | S-005 item 11 (Task Manager kill) tests restart mechanism, not S-001 exit code fix | MEDIUM | Sonnet | Accepted — clarifying note added to item 11; code-review verification step specified |
| R2-M6 | S-002 verification intent hardcodes port 5000, contradicting parameterized script | MEDIUM | Sonnet | Accepted — port reference parameterised to `$Port` (default 5000) |
| R2-L1 | S-004 log path hardcoded to default deploy directory | LOW | Opus | Accepted — placeholder with note added |
| R2-L2 | Double-click launch may place logs in wrong directory | LOW | Opus | Accepted — guidance updated to use `Start-ScheduledTask` instead |
| R2-L3 | `JpegQuality` default value not stated | LOW | Opus | Accepted — actual default (85) added |
| R2-L4 | S-003 describes code default for `AutoLaunch` but shipped file sets `false` | LOW | Sonnet | Accepted — explicit note added: set to `true` for production |
| R2-L5 | S-005 item 2 D-SC-1 test unexecutable on development machine | LOW | Sonnet | Accepted — replaced with `runtimeconfig.json` inspection guidance |

### R3 Review (v0.3 → v0.4)

**Models**: Claude Opus 4.6 · Claude Sonnet 4.6 · **Result**: NEEDS REVISION (7 findings, all accepted)

| ID | Finding | Severity | Source | Disposition |
|---|---|---|---|---|
| R3-H1 | S-004 §8 "double-click the executable to restart immediately" contradicts §1 "Do not double-click" | HIGH | Both | Accepted — parenthetical removed; replaced with Task Scheduler auto-restart language |
| R3-M1 | S-005 item 2 `"type": "selfContained"` is not a valid .NET 8 `runtimeconfig.json` field | MEDIUM | Opus | Accepted — clause removed; pass condition now tests only `coreclr.dll` presence |
| R3-M2 | S-004 §1 elevated PS recovery instruction inaccessible to club volunteers | MEDIUM | Sonnet | Accepted — replaced with "contact your IT contact to restart the application" escalation |
| R3-M3 | `-SkipPasswordUpdate` on first deploy leaves `PcsPro__Password` unset silently | LOW/MEDIUM | Both | Accepted — warning added to S-002 step 7 |
| R3-L1 | `publish/` edits lost on `git clean -fdx` — no warning in Quick Start | LOW | Both | Accepted — one-line callout added after Quick Start step 2 |
| R3-L2 | Smoke item 11 single kill insufficient to verify D-SC-3 reliably | LOW | Sonnet | Accepted — "Repeat three times in rapid succession" added |
| R3-L3 | S-004 §4 assumes PCS Pro not yet running (contradicts AutoLaunch: true in production) | INFO | Opus | Accepted — leading sentence added directing operator to skip to match selection if already running |

### R4 Review (v0.4 → v0.5)

**Models**: Claude Opus 4.6 · GPT-5.4 · **Result**: NEEDS REVISION (13 findings, all accepted)

| ID | Finding | Severity | Source | Disposition |
|---|---|---|---|---|
| R4-H1 | S-004 §3/§4: "Launch PCS Pro" button and "Load" button do not exist in the web UI | HIGH | GPT | Accepted — §3 "Not Running" rewritten (auto-launch waits, no button); §4 rewritten (click match card, auto-select with one match) |
| R4-H2 | S-004 §5 / S-005 item 10: manual mode toggle is tray-only, not web control panel | HIGH | GPT | Accepted — §5 rewritten to describe tray toggle; item 10 updated to test tray icon + web banner |
| R4-M1 | .new config files created on every redeploy → auto-start always suppressed even when config unchanged | MEDIUM | Both | Accepted — content-hash (Get-FileHash SHA256) comparison added; .new files and auto-start suppression only when file differs |
| R4-M2 | S-002 verification conflates force-kill-succeeds path and force-kill-fails-abort path | MEDIUM | GPT | Accepted — verification split into two explicit cases |
| R4-M3 | S-004 §2 and S-005 item 4 hardcode port 5000 contrary to configurable-port design | MEDIUM | GPT | Accepted — replaced with `<configured-port>` (default 5000) with cross-reference |
| R4-M4 | S-005 items 6-8: auto-select with single match means MatchSelectionReady may be skipped | MEDIUM | GPT | Accepted — items 6-8 updated to note auto-select path as valid outcome |
| R4-M5 | S-005 item 2 pass condition checks `publish/` folder; on garage PC should check deploy dir | MEDIUM | Opus | Accepted — changed to `$DeployDir` (default `C:\PcsRemote\`) |
| R4-M6 | S-003 development config warning omits `ASPNETCORE_ENVIRONMENT` | MEDIUM | Opus | Accepted — both environment variable names now listed with consequences |
| R4-H3 | Test count states 438; actual runtime count (verified) is 443 | MEDIUM | Opus | Accepted — corrected to 443 in Prerequisites and S-001 verification intent |
| R4-L1 | S-005 items 1-2 failure actions assume repo and SDK present on garage PC | LOW | GPT | Accepted — rewritten to "re-publish on developer machine and redeploy" |
| R4-L2 | S-005 item 13 D-SC-7 misattribution — checklist as whole satisfies D-SC-7, not one item | LOW | Opus | Accepted — area corrected to HLPS-004; D-SC-7 note added to S-005 "Why" |
| R4-L3 | BSTR not freed with ZeroFreeBSTR after SecureString conversion | LOW | Opus | Accepted — ZeroFreeBSTR in try/finally added to S-002 step 7 |
| R4-I1 | VM test described as per-deployment; impractical | INFO | Opus | Accepted — moved to one-time initial rollout note in item 2 |

### R5 Review (v0.5 → v0.6)

**Models**: Claude Opus 4.6 · Claude Sonnet 4.6 · **Result**: NEEDS REVISION (7 findings, all accepted)

| ID | Finding | Severity | Source | Disposition |
|---|---|---|---|---|
| R5-M1 | S-004 §3 status indicator headings use enum names not matching actual `PcsProStatusIndicator.razor` UI labels | MEDIUM | Opus | Accepted — all 8 status headings replaced with exact UI label strings from source |
| R5-M2 | S-003 Quick Start step 3 omits `Start-ScheduledTask` instruction after `.new` file merge — app stays stopped | MEDIUM | Sonnet | Accepted — explicit start instruction and tray icon verification added to step 3 |
| R5-L1 | S-005 item 10 ManualModeBanner text truncated (missing "by local operator") | LOW | Opus | Accepted — full banner text restored |
| R5-L2 | S-005 item 11 "within 2 minutes" contradicts "60s per cycle × 3" (180s > 120s) | LOW | Sonnet | Accepted — 2-minute window removed; per-cycle 60s criterion clarified with ~40s expected time |
| R5-L3 | S-004 §1/§8/§9 "30 seconds via Task Scheduler" attributes ONLOGON trigger startup to crash-restart delay | LOW | Sonnet | Accepted — corrected to "within a few seconds of logon (ONLOGON trigger)" throughout |
| R5-L4 | S-004 §7 log filename example hardcoded to document date (20260414); will not exist on any other deployment day | LOW | Sonnet | Accepted — replaced with `pcs-remote-YYYYMMDD.log` format pattern with illustrative example |
| R5-L5 | S-003 Quick Start note prohibiting `publish.ps1` re-runs has no update-deployment counterpart | LOW | Sonnet | Accepted — note scoped to initial deployment; Update Deployments procedure added |

### R6 Review (v0.6 → v0.7)

**Models**: GPT-5.4 · Claude Opus 4.6 · **Result**: NEEDS REVISION (6 findings accepted, 2 dismissed)

| ID | Finding | Severity | Source | Disposition |
|---|---|---|---|---|
| R6-M1 | S-002 verification intent checks `GetEnvironmentVariable("PcsPro__Password")` — prints the password while also saying "No credential in output" | MEDIUM | GPT | Accepted — replaced with non-printing existence check (`-ne $null -and -ne ""`) |
| R6-M2 | S-003 port-change procedure says "update the Windows Firewall rule" without specifying the mechanism | MEDIUM | GPT | Accepted — replaced with explicit "re-run `Deploy-PcsRemote.ps1 -SkipPasswordUpdate -Port <new-port>`" instruction |
| R6-M3 | S-002 step 4 includes `appsettings.Development.json` in merge-blocking glob — never loaded in production; can block auto-restart on code-only redeployments | MEDIUM | GPT | Accepted — `appsettings.Development.json` excluded from merge-blocking; always silently overwritten |
| R6-M4 | S-005 D-U-8 dependency incorrect: item 10 (manual mode, no PCS Pro needed) included; item 12 (password change, requires PCS Pro login) excluded | MEDIUM | Opus | Accepted — corrected to "items 6–9, 12, and 13" in both Dependencies and Verification intent |
| R6-M5 | S-003 missing log file location and 7-day retention required by governing HLPS-007 §2 | MEDIUM | Opus | Accepted — log path pattern and retention note added to S-003 settings list |
| R6-I1 | S-003 Update Deployments says "detects new keys" — script detects whole-file hash change, not individual keys | INFO | Opus | Accepted — reworded to "detects configuration file changes by comparing SHA256 hashes" |
| — | R6-GPT-L1: S-004 §7 log path assumes default deploy dir | LOW | GPT | Dismissed — line 149 already says "if your IT contact used a different deployment folder, substitute it for `C:\PcsRemote\`" |
| — | R6-Opus-L1: S-001 code snippet uses K&R brace style | LOW | Opus | Dismissed — lines 38–46 already use Allman style; reviewer saw condensed summary, not source |

### R7 Review (v0.7 → v0.8)

**Models**: GPT-5.4 · Claude Opus 4.6 · **Result**: NEEDS REVISION (GPT) / APPROVED with caveats (Opus) — 6 findings accepted

| ID | Finding | Severity | Source | Disposition |
|---|---|---|---|---|
| R7-M1 | S-002 step 5: unregister-then-register is non-atomic — if re-registration fails, auto-start task is deleted with no replacement | HIGH→MEDIUM | GPT | Accepted — replaced with `Register-ScheduledTask -Force` (atomic replacement; no prior unregister) |
| R7-M2 | S-004 §2 omits "Open Browser" tray menu item — the easiest way for a garage PC volunteer to open the control panel | MEDIUM | Opus | Accepted — "Open Browser" tray item added as primary access method; LAN URL retained for remote devices |
| R7-L1 | S-003 password section says value "will be ignored in favour of the env var" — only true when env var is present | MEDIUM→LOW | GPT | Accepted — reworded: env var overrides JSON; JSON must always be left empty to prevent plaintext fallback |
| R7-L2 | S-005 items 6–9 and 13 require live match data — no prerequisite note; smoke test on non-match days would fail spuriously | MEDIUM→LOW | GPT | Accepted — match data prerequisite added to S-005 Dependencies |
| R7-L3 | S-004 step 4 has no recovery path for loading the wrong match | LOW | Opus | Accepted — "Change Match" button guidance added to step 4 |
| R7-I1 | S-005 item 11 "Kill via Task Manager" ambiguous — "End task" may send WM_CLOSE (exit code 0); "End process" reliably uses TerminateProcess (exit code 1) | INFO | Opus | Accepted — clarified to "Details tab → End process" |
