# PCS Remote — Smoke Test Checklist

Execute this checklist after every deployment on the garage PC. Each item has a clear pass condition and a failure action. All 13 items must pass before the deployment is considered complete.

**Executor**: the developer performing the deployment.  
**Sign-off**: mark each item pass or fail. Record the outcome (including any steps that required clarification) in the delivery record in `IS-007-Deployment.md` before marking the step Delivered.

> **Blocking unknown D-U-8**: Items 6–9, 12, and 13 require the PCS Pro install path to be known and `PcsPro:ExecutablePath` correctly configured. Items 1–5, 10, and 11 can be verified independently without PCS Pro.
>
> **Match data prerequisite**: Items 6–9 and 13 require PCS Pro to contain at least one match dated today (or a suitable test fixture). On non-match days, defer these items or create a test match in PCS Pro before execution.

---

## Checklist

### Item 1 — Deployment artefact set complete

| Field | Value |
|---|---|
| **Area** | Deployment |
| **Pass condition** | All expected files present in the deployment directory (`$DeployDir`, default `C:\PcsRemote\`): `PcsRemote.TrayHost.exe`, `coreclr.dll`, `appsettings.json`, `appsettings.Development.json` |
| **Failure action** | Re-publish on the developer machine (`scripts\publish.ps1`) and redeploy (`scripts\Deploy-PcsRemote.ps1`) |

---

### Item 2 — Self-contained artefact (D-SC-1)

| Field | Value |
|---|---|
| **Area** | D-SC-1 |
| **Pass condition** | Confirm `coreclr.dll` and `PcsRemote.TrayHost.exe` are both present in `$DeployDir` — proves the CLR is bundled. One-time initial rollout only: optionally verify on a clean Windows 10/11 VM with no .NET runtime installed — not required for subsequent deployments. |
| **Failure action** | Re-run `scripts\publish.ps1` on the developer machine and redeploy |

---

### Item 3 — Task Scheduler auto-start (D-SC-2)

| Field | Value |
|---|---|
| **Area** | D-SC-2 |
| **Pass condition** | Application tray icon appears within 30 seconds of logon |
| **Failure action** | Check Task Scheduler task status and "Start in" setting: open Task Scheduler → Task Scheduler Library → verify task `PcsRemote` exists, is enabled, has the correct ONLOGON trigger, and `Start in` is set to `$DeployDir` |

---

### Item 4 — LAN browser access (D-SC-8 / HLPS-003)

| Field | Value |
|---|---|
| **Area** | D-SC-8 / HLPS-003 |
| **Pass condition** | Control panel loads at `http://<garage-pc-ip>:<configured-port>` (default port 5000) from a separate device on the LAN |
| **Failure action** | Check the Windows Firewall inbound rule `PcsRemote-HTTP` exists and targets the correct port; verify the correct IP address and port are being used |

---

### Item 5 — SignalR connection (HLPS-003)

| Field | Value |
|---|---|
| **Area** | HLPS-003 |
| **Pass condition** | With the control panel open in a browser, observe that the status indicator updates automatically (without a manual page refresh) as PCS Pro progresses through startup states during item 6 startup — confirms the WebSocket/SignalR hub is live |
| **Failure action** | Open the browser developer console and check for WebSocket connection errors or failed hub negotiation |

> **Note**: Observe this item during item 6 execution, not as a separate step.

---

### Item 6 — PCS Pro launch in real mode (HLPS-006)

| Field | Value |
|---|---|
| **Area** | HLPS-006 |
| **Requires** | D-U-8 resolved; match data prerequisite |
| **Pass condition** | PCS Pro (`cricket.exe`) launches and the service reaches `MatchSelectionReady` state (or `MatchLoaded` directly if only one match exists today — auto-select fires automatically) |
| **Failure action** | Check `PcsPro:UseMock=false` in `appsettings.json`; verify `PcsPro:ExecutablePath` points to the correct `cricket.exe`; confirm `PcsPro__Password` is set (`[System.Environment]::GetEnvironmentVariable("PcsPro__Password","Machine") -ne $null`) |

---

### Item 7 — Match list (HLPS-004)

| Field | Value |
|---|---|
| **Area** | HLPS-004 |
| **Requires** | D-U-8 resolved; match data prerequisite |
| **Pass condition** | Today's matches appear as match cards in the control panel (or auto-load if exactly one match exists) |
| **Failure action** | Verify the date on the garage PC is correct; check PCS Pro contains match data for today |

---

### Item 8 — Match load (HLPS-006)

| Field | Value |
|---|---|
| **Area** | HLPS-006 |
| **Requires** | D-U-8 resolved; match data prerequisite |
| **Pass condition** | With multiple matches: click a match card and confirm the service reaches `MatchLoaded` state. With a single match: confirm auto-load fires without user action and the service reaches `MatchLoaded`. |
| **Failure action** | Check PCS Pro UI for unexpected dialogs; check the log file |

---

### Item 9 — Scoreboard preview (HLPS-004)

| Field | Value |
|---|---|
| **Area** | HLPS-004 |
| **Requires** | D-U-8 resolved; match data prerequisite |
| **Pass condition** | Scoreboard image appears in the control panel and refreshes on demand |
| **Failure action** | Check `Scoreboard:JpegQuality` in `appsettings.json`; check the log file for `CaptureScoreboardImageAsync` errors |

---

### Item 10 — Manual mode toggle (HLPS-005)

| Field | Value |
|---|---|
| **Area** | HLPS-005 |
| **Pass condition** | (1) Right-click the tray icon on the garage PC → **"Switch to Manual Mode"** — confirm the banner *"Manual mode — automation paused by local operator"* appears in the web control panel. (2) While manual mode is active, click **"Refresh Scoreboard"** — confirm a warning notification appears and no scoreboard refresh occurs (command guard verified). (3) Right-click → **"Resume Automation"** — confirm the banner disappears. |
| **Failure action** | Check `ManualModeService` DI registration; confirm the tray icon renders the context menu correctly |

---

### Item 11 — Task Scheduler restart mechanism (D-SC-3)

| Field | Value |
|---|---|
| **Area** | D-SC-3 |
| **Pass condition** | Kill `PcsRemote.TrayHost.exe` via Task Manager **Details tab → End process** (not "End task" — that may send WM_CLOSE first and produce exit code 0). Application restarts within 60 seconds. Repeat three times in succession; each restart must occur within 60 seconds of the kill (~40 seconds expected). |
| **Failure action** | Check Task Scheduler restart settings: delay ≤ 30s, count ≥ 999 |

> **Note**: This item tests the Task Scheduler restart settings, not the S-001 exit code fix. For the exit code fix: inspect `src/PcsRemote.TrayHost/Program.cs` catch block and confirm `return 1;` is present (code review).

---

### Item 12 — Password change (D-SC-6)

| Field | Value |
|---|---|
| **Area** | D-SC-6 |
| **Requires** | D-U-8 resolved |
| **Pass condition** | Update `PcsPro__Password` in the System environment (follow the manual snippet in the [Configuration Guide](Configuration-Guide.md#setting-the-pcs-pro-password)); restart the application; confirm PCS Pro logs in successfully (status reaches `MatchSelectionReady` or `MatchLoaded`) |
| **Failure action** | Follow [Configuration Guide — Setting the PCS Pro password](Configuration-Guide.md#setting-the-pcs-pro-password) |

---

### Item 13 — Scoreboard refresh (HLPS-004)

| Field | Value |
|---|---|
| **Area** | HLPS-004 |
| **Requires** | D-U-8 resolved; match data prerequisite |
| **Pass condition** | With a match loaded, click **"Refresh Scoreboard"** — a new scoreboard image loads without errors |
| **Failure action** | Check the log file for `CaptureScoreboardImageAsync` errors |

---

## Sign-off record

| Item | Result | Notes |
|---|---|---|
| 1 — Artefact complete | | |
| 2 — Self-contained (D-SC-1) | | |
| 3 — Auto-start (D-SC-2) | | |
| 4 — LAN access (D-SC-8) | | |
| 5 — SignalR (HLPS-003) | | |
| 6 — PCS Pro launch (HLPS-006) | | |
| 7 — Match list (HLPS-004) | | |
| 8 — Match load (HLPS-006) | | |
| 9 — Scoreboard preview (HLPS-004) | | |
| 10 — Manual mode (HLPS-005) | | |
| 11 — Restart mechanism (D-SC-3) | | |
| 12 — Password change (D-SC-6) | | |
| 13 — Scoreboard refresh (HLPS-004) | | |

> **D-SC-7 note**: The checklist as a whole satisfies D-SC-7 (smoke test covers all Phase 1 functional areas — HLPS-003 through HLPS-006). No individual item covers D-SC-7 alone.
