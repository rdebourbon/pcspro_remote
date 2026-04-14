# IS-007: Deployment & Operations — Implementation Sequence

| Field | Value |
|---|---|
| **Document** | IS-007-Deployment.md |
| **Status** | DRAFT |
| **Version** | 0.3 |
| **Date** | 2026-04-14 |
| **Governing HLPS** | HLPS-007-Deployment.md v0.5 (APPROVED) |
| **Context** | `docs/PCS-Remote/PROJECT-CONTEXT.md` v1.1 |
| **Prerequisites** | IS-006 delivered (real FlaUI automation working, all 438 tests passing). |

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

**Verification intent:** `src/PcsRemote.TrayHost/Program.cs` catch block now returns non-zero exit code. All 438 existing tests pass. `dotnet publish` succeeds with zero errors. `publish/PcsRemote.TrayHost.exe` exists. `scripts/publish.ps1` runs cleanly and prints the artefact list.

---

### S-002 — PowerShell deployment script

**What changes:** Create `scripts/Deploy-PcsRemote.ps1`. The script:

1. **Elevation guard**: Checks that it is running as Administrator; exits with a clear error message if not (`#Requires -RunAsAdministrator`).
2. **Parameters**: Accepts `-DeployDir` (default `C:\PcsRemote\`), `-AppUser` (the Windows account name for the Task Scheduler trigger — prompted if not supplied), `-Port` (TCP port for the **Windows Firewall rule only** — default `5000`; note: changing the Kestrel bind port also requires editing `Kestrel:Endpoints:Http:Url` in `appsettings.json` before running this script — see S-003), and `-SkipPasswordUpdate` (switch; if specified, the password prompt in step 7 is skipped — use for code-only re-deployments where the PCS Pro password has not changed).
3. **Stop running instance**: Before copying any files, the script stops the Task Scheduler task (`Stop-ScheduledTask -TaskName PcsRemote -ErrorAction SilentlyContinue`) and then waits up to 10 seconds for `PcsRemote.TrayHost.exe` to exit (polling `Get-Process` every 500 ms). This prevents `Access Denied` errors caused by file locks on the executable and DLLs during re-deployment. If the process has not exited after 10 seconds, the script issues a forced kill using `Stop-Process -Id <pid> -Force` and waits an additional 5 seconds. If the process is still alive after the forced kill, the script aborts with a clear error message: "Cannot stop PcsRemote.TrayHost.exe (PID `<pid>`). Close it manually and re-run the script." The script must not proceed to file copy if the process is still running.
4. **Artefact copy**: Copies all files from the `publish/` artefact set to `$DeployDir`. The `appsettings*.json` family of files (`appsettings.json`, `appsettings.Development.json`, any environment-specific variants) is treated as configuration: if any of these files already exist in `$DeployDir` (re-deployment), the existing files are preserved and the new versions are placed alongside them with a `.new` suffix (e.g., `appsettings.json.new`) so the operator can diff them for new keys. The script prints a warning message listing any `.new` files created, instructing the operator to review them and merge any new configuration keys manually before restarting.
5. **Task Scheduler (idempotent)**: Unregisters any existing task named `PcsRemote` before registering a new one, ensuring re-deployments are clean. Task definition:
   - Trigger: `ONLOGON` for the specified `-AppUser` account
   - Action: `$DeployDir\PcsRemote.TrayHost.exe`
   - Working directory (`Start in`): `$DeployDir`
   - Run only when user is logged on (interactive session)
   - On failure: restart after 30 seconds, up to 999 attempts
   - Settings: `ExecutionTimeLimit = PT0S` (no timeout), `MultipleInstances = IgnoreNew`
6. **Windows Firewall rule (idempotent)**: Removes any existing rule named `PcsRemote-HTTP` then creates a new inbound TCP rule for port `$Port` (`New-NetFirewallRule`).
7. **PCS Pro password**: If `-SkipPasswordUpdate` is not specified, checks whether `PcsPro__Password` is already set in the System environment. If already set, prints a prompt: "Password is already configured. Update it? [y/N]" and only proceeds if the operator answers `y`. If not yet set, prompts unconditionally via `Read-Host -AsSecureString`. Converts to plain text using `[System.Runtime.InteropServices.Marshal]::PtrToStringBSTR` and `SecureStringToBSTR` only for the duration of the `SetEnvironmentVariable` call; sets the System-scoped environment variable `PcsPro__Password` via `[System.Environment]::SetEnvironmentVariable`. Note: .NET managed strings are immutable and cannot be zeroed in memory; the `SecureString` itself provides a degree of protection in memory, but once converted to a plain string for the registry call, normal GC rules apply. The script must not write any credential to disk or to the console output.
8. **Restart after deployment**: If no `.new` configuration files were created in step 4 (first-time deployment or re-deployment with no new keys), starts the Task Scheduler task immediately (`Start-ScheduledTask -TaskName PcsRemote`). If `.new` files exist, does **not** start the task — instead prints a prominent warning: "⚠ Configuration merge required. Review the following .new files, merge any new keys into the existing configuration, then run: `Start-ScheduledTask -TaskName PcsRemote`." This prevents the application starting with incomplete configuration after a release that introduces required new keys.
9. **Summary**: Prints a deployment summary listing all actions taken and their outcomes, including any `.new` configuration files that require review.

**Why:** A single idempotent script that handles first-time deployment and re-deployment identically is the core mechanism for ensuring the system can be maintained by non-developers. Stopping the running instance before copying eliminates `Access Denied` file lock failures; the force-kill fallback handles hung processes. The `-Port` parameter opens the correct firewall port for non-standard configurations. The `-SkipPasswordUpdate` switch avoids unnecessary credential exposure during code-only re-deployments. The `appsettings*.json` merge pattern ensures re-deployments do not silently overwrite configuration, while also not silently discarding new configuration keys added by future releases. Conditional auto-start prevents the application launching with incomplete configuration after a key-adding release. Addresses D-SC-2, D-SC-3 (Task Scheduler), D-SC-6, D-SC-8 (Firewall).

**Dependencies:** S-001 (publish artefact set must exist in `publish/`).

**Verification intent:** Running the script from an elevated PowerShell prompt on the development machine (or a test VM) completes without errors. Re-running it a second time completes without errors (idempotency). Running it without elevation prints a clear error and exits. The Task Scheduler task appears in Task Scheduler with correct settings: correct user, `Start in`, restart settings. The Windows Firewall rule `PcsRemote-HTTP` appears inbound TCP port `$Port` (default 5000; confirm against the value passed to the script). `[System.Environment]::GetEnvironmentVariable("PcsPro__Password", "Machine")` returns the entered password. No password text is visible in the script output or in any log file. Running with `-SkipPasswordUpdate` completes without prompting for a password. Killing `PcsRemote.TrayHost.exe` with `Stop-Process -Force` during the stop-before-copy step causes the script to abort with a clear message (test by launching the exe manually and refusing to close it within 10 seconds).

---

### S-003 — Configuration guide

**What changes:** Create `docs/guides/Configuration-Guide.md`. The guide covers:

- **Quick Start — First-Time Deployment Procedure**: A numbered end-to-end sequence so a newcomer does not need to synthesise the order from four separate documents:
  1. Run `scripts/publish.ps1` to produce the deployment artefact in `publish/`.
  2. Edit `publish/appsettings.json`: set `PcsPro:ExecutablePath` to the full path of `cricket.exe`; set `PcsPro:WorkingDirectory` to the folder containing it; set `PcsPro:AutoLaunch` to `true` (the shipped file defaults to `false`); leave `PcsPro:Password` empty.
  3. Run `scripts/Deploy-PcsRemote.ps1` from an elevated PowerShell prompt (provide `-AppUser`, enter the PCS Pro password when prompted). If the script reports `.new` files, merge them before proceeding.
  4. Execute the Smoke Test Checklist (`docs/guides/Smoke-Test-Checklist.md`).

- **Prerequisites**: .NET 8 runtime not required (self-contained); Windows 10/11; Administrator account for deployment script.
- **`appsettings.json` settings** (all production-relevant keys):
  - `PcsPro:AutoLaunch` — boolean; if `true`, PCS Pro is launched automatically when the application starts; set to `false` for manual-launch mode. **Default when key is absent**: `true` (the code uses `GetValue("PcsPro:AutoLaunch", defaultValue: true)`). Explicitly set this key to `false` if you want to disable auto-launch; omitting it is equivalent to setting it to `true`. **Important**: the `appsettings.json` shipped with the deployment artefact sets `"AutoLaunch": false` explicitly — set this to `true` for production use unless you intend to use manual-launch mode.
  - `PcsPro:ExecutablePath` — full path to `cricket.exe` (e.g., `C:\Program Files (x86)\PCS Pro\cricket.exe`); required for real mode.
  - `PcsPro:WorkingDirectory` — working directory for cricket.exe process (typically the same folder as the executable).
  - `PcsPro:UseMock` — boolean; `false` for production (real PCS Pro automation); `true` for testing with the mock service. Note: this is a top-level key distinct from the `PcsPro:Mock` subsection (development/test parameters not relevant to production).
  - `Kestrel:Endpoints:Http:Url` — bind address and port (default `http://0.0.0.0:5000`); change the port number here if 5000 conflicts with another application.
  - `Scoreboard:JpegQuality` — JPEG compression quality for scoreboard images (1–100; default `85`, suitable for LAN use; lower values reduce image size at the cost of quality).
  - `Logging:LogLevel:Default` — log verbosity (default `Information`; use `Debug` only for troubleshooting as it increases file size significantly).
  - `AllowedHosts` — host header filtering (default `"*"` is correct for LAN deployment; do not restrict without understanding the implications).
  - Note on `PcsPro:Mock` subsection — development/test delay and probability settings; leave unchanged in production.
  - Note on `appsettings.Development.json` — this companion file is present in the deployment directory but is never loaded in production; do not set `DOTNET_ENVIRONMENT=Development` on the garage PC.
- **Setting the PCS Pro password** (via `PcsPro__Password` System-scoped environment variable):
  - **Important**: The `PcsPro:Password` key in `appsettings.json` must be left empty (`""`). Do not enter the password there — it is stored in plain text and will be ignored in favour of the environment variable. Entering it in `appsettings.json` is a security risk.
  - The deployment script sets this interactively. Manual steps if needed: open an elevated PowerShell prompt; run `[System.Environment]::SetEnvironmentVariable("PcsPro__Password","<password>","Machine")`; restart the application or the Task Scheduler task for the change to take effect.
  - Why not `appsettings.json`: storing credentials in a plain-text config file is insecure. The environment variable is stored in the Windows registry (HKLM) and is not human-readable without Administrator access.
- **Updating the PCS Pro executable path** after reinstall or upgrade: edit `appsettings.json` — update `PcsPro:ExecutablePath` and `PcsPro:WorkingDirectory` — then restart the application.
- **Switching between mock and real mode**: change `PcsPro:UseMock` in `appsettings.json`; restart the application.
- **Changing the HTTP port**: change `Kestrel:Endpoints:Http:Url` in `appsettings.json`; update the Windows Firewall rule to use the new port; restart the application.

**Why:** Volunteers and future developers need a single authoritative reference for all configuration decisions. Accurately documenting all keys (including the `UseMock`/`Mock` distinction and the `Development.json` note) prevents misconfiguration. Addresses D-SC-4, D-SC-6.

**Dependencies:** S-001 (publish artefact must be verified so companion file list is accurate), S-002 (deployment script must exist so the password-setting cross-reference is accurate).

**Verification intent:** D-SC-4 — a person who has not previously configured the system can complete a fresh configuration from scratch using only the guide, without agent assistance, within 30 minutes. **Executor**: a developer on the team (not the author of this document). **Sign-off**: the executor records the outcome (pass/fail, any steps that needed clarification) and the result is noted in the delivery record above before the step is marked Delivered.

---

### S-004 — Operational guide

**What changes:** Create `docs/guides/Operational-Guide.md`. The guide is written for club volunteers with no IT background. Sections:

1. **Before the match** — How to confirm the application is running (look for the tray icon; if absent, log on to the garage PC and wait 30 seconds for auto-start). If it still has not appeared, open an elevated PowerShell prompt and run `Start-ScheduledTask -TaskName PcsRemote`. Do not double-click the executable directly — starting from outside the deployment folder can place log files in an unexpected location.
2. **Accessing the control panel** — Open a browser on any device on the club Wi-Fi and navigate to `http://<garage-pc-ip>:5000`. Instructions for finding the garage PC's IP address: on the garage PC, open Command Prompt and run `ipconfig`; look for the IPv4 address of the active network adapter. Recommendation: ask your IT contact to configure a static IP or DHCP reservation so the address never changes.
3. **Status indicators** — Description of each state shown on the control panel and what action, if any, the volunteer should take:
   - **Not Running**: PCS Pro is not yet started. Click "Launch PCS Pro" to start it.
   - **Launching**: PCS Pro is starting up. Wait; no action needed.
   - **Login Screen**: PCS Pro is open and waiting for login. The application will log in automatically.
   - **Match Selection**: The match selection screen is open. Use the control panel to search for and load today's match.
   - **Match Selection — Searching**: The application is fetching the match list from PCS Pro. Wait a few seconds.
   - **Match Selection — Ready**: The match list is loaded and ready to select from.
   - **Match Loaded**: A match is loaded and live. The scoreboard is available.
   - **Error**: Something went wrong. Check the log file (see §7) and report to your IT contact.
4. **Loading a match** — Step-by-step: click "Launch PCS Pro", wait for match list, select today's match, click "Load".
5. **Manual mode** — When to use manual mode; how to toggle it; what it changes.
6. **Updating the PCS Pro password** — "If the PCS Pro password changes, you will need to update it. Follow these steps: [cross-reference Configuration Guide §password-setting]." Note: this requires Administrator access on the garage PC.
7. **Checking for errors** — How to locate today's log file (default: `C:\PcsRemote\logs\pcs-remote-20260414.log` — where the date changes daily; if your IT contact used a different deployment folder, substitute it for `C:\PcsRemote\`); what level of detail to share when reporting a problem.
8. **Restarting the application** — Right-click the tray icon → Exit; then log off and log on again (or double-click the executable to restart immediately).
9. **After a reboot** — If the garage PC has been restarted (e.g., after a power cut or Windows Update): log on to the garage PC if auto-logon is not configured; the application will start automatically within 30 seconds of logon.
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
| 1 | Deployment | Application artefact set complete | All expected files present in deploy directory | Re-run `scripts/publish.ps1` and `scripts/Deploy-PcsRemote.ps1` |
| 2 | D-SC-1 | Self-contained artefact verification | Inspect `publish\PcsRemote.TrayHost.runtimeconfig.json` — confirm `"type": "selfContained"` is `true`; confirm `coreclr.dll` is present in the `publish/` folder. Optionally test on a clean Windows 10/11 VM with no .NET runtime installed. | Re-run `scripts/publish.ps1` and confirm the `--self-contained` flag is present |
| 3 | D-SC-2 | Task Scheduler auto-start | Application tray icon appears within 30s of logon | Check Task Scheduler task status and "Start in" setting |
| 4 | D-SC-8 / HLPS-003 | LAN browser access | Control panel loads at `http://<garage-pc-ip>:5000` from a separate device | Check firewall rule; verify correct IP address |
| 5 | HLPS-003 | SignalR connection | Status indicator updates in real time (no spinner stuck) | Check browser console for WebSocket errors |
| 6 | HLPS-006 | PCS Pro launch in real mode | PCS Pro (cricket.exe) launches and service reaches `MatchSelectionReady` state | Check `PcsPro:UseMock=false`, `ExecutablePath`, and `PcsPro__Password` |
| 7 | HLPS-004 | Match list | Today's matches appear in match cards | Verify date on garage PC; check PCS Pro data |
| 8 | HLPS-006 | Match load | Select a match and click Load; service reaches MatchLoaded state | Check PCS Pro UI for dialogs; check log file |
| 9 | HLPS-004 | Scoreboard preview | Scoreboard image appears and refreshes on demand | Check `Scoreboard:JpegQuality`; check PrintWindow availability |
| 10 | HLPS-005 | Manual mode toggle | Manual mode can be enabled and disabled from the control panel | Check ManualModeService wiring in DI |
| 11 | D-SC-3 | Task Scheduler restart mechanism | Kill `PcsRemote.TrayHost.exe` via Task Manager; application restarts within 60 seconds. Note: Task Manager always exits with code 1 — this tests the Task Scheduler restart settings, not the S-001 exit code fix. For exit code fix verification: inspect `src/PcsRemote.TrayHost/Program.cs` catch block and confirm `return 1;` is present (code review). | Check Task Scheduler restart settings (delay ≤ 30s, count ≥ 999) |
| 12 | D-SC-6 | Password change | Update `PcsPro__Password` env var; restart application; PCS Pro logs in successfully | Follow Configuration Guide §password-setting |
| 13 | D-SC-7 / HLPS-004 | Scoreboard refresh | With a match loaded, click "Refresh Scoreboard"; a new image loads without errors | Check `CaptureScoreboardImageAsync` in FlaUI service; check log file |

**Why:** A structured checklist that any developer or club IT contact can follow ensures that every deployment is validated against all Phase 1 capabilities. It also provides a regression baseline for future deployments. Addresses D-SC-7.

**Dependencies:** S-001–S-004 (all prior deliverables must exist before the checklist can be executed meaningfully). Blocking unknown D-U-8 (PCS Pro install path) must be resolved before items 6–10 can pass.

**Verification intent:** Execute checklist on garage PC after first deployment; all 13 items pass. **Executor**: the developer performing the first deployment. **Sign-off**: each checklist item is marked pass/fail and the result is recorded in the delivery record above before the step is marked Delivered. Items 6–10 and 13 require D-U-8 (PCS Pro install path) to be resolved first.

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
