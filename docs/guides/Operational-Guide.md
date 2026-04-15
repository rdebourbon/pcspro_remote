# PCS Remote — Operational Guide

This guide is for **club volunteers** managing PCS Remote on match days. No IT background is required. Every step has a clear action and expected outcome.

---

## Before the match

Look for the PCS Remote tray icon (bottom-right corner of the garage PC taskbar). If the icon is visible, the application is running — skip to [Accessing the control panel](#accessing-the-control-panel).

If the icon is not visible, log on to the garage PC. The application starts automatically within about 30 seconds of logon. If the tray icon still has not appeared after 30 seconds, contact your IT contact — they can restart the application by running `Start-ScheduledTask -TaskName PcsRemote` from an elevated PowerShell prompt on the garage PC.

---

## Accessing the control panel

**From the garage PC:**  
Right-click the tray icon → click **"Open Browser"** — the control panel opens automatically in the default browser.

**From any other device on the club Wi-Fi:**  
Open a browser and navigate to `http://<garage-pc-ip>:<port>` where `<port>` is the configured port (default: `5000`). To find the garage PC's IP address: open Command Prompt on the garage PC and run `ipconfig` — look for the IPv4 address of the active network adapter. Ask your IT contact to configure a static IP or DHCP reservation so the address never changes.

---

## Status indicators

The status bar at the top of the control panel shows the current state of the system. Here is what each status means and what to do:

| Status | What it means | Action |
|---|---|---|
| **PCS Pro not running** | PCS Pro has not started yet. With auto-launch configured, it will start automatically. | Wait up to 30 seconds. If it does not change, contact your IT contact. |
| **PCS Pro starting…** | PCS Pro is starting up. | Wait — no action needed. |
| **Logging in…** | PCS Pro is open and logging in automatically. | Wait. |
| **Loading matches…** | Loading the match list from PCS Pro. | Wait a few seconds. |
| **Searching…** | Fetching today's matches. | Wait. |
| **Select a match** | The match list is ready. If only one match is available, it is selected automatically. | If multiple matches appear, click the card for today's match. |
| **Match loaded** | A match is loaded and live. The scoreboard is available. | No action needed unless you need to switch matches. |
| **Error** | Something went wrong. | Check the log file (see [Checking for errors](#checking-for-errors)) and report to your IT contact. |

---

## Loading a match

With `AutoLaunch=true` (recommended for production), PCS Pro starts automatically on logon. By the time you open the control panel the status will typically already show **Loading matches…**, **Searching…**, or **Select a match** — skip straight to selecting a match.

**Full sequence from a cold start:**

1. Wait for the status to show **Select a match**.
2. If only one match is available, it is selected automatically and the status advances to **Match loaded**.
3. If multiple matches appear, click the card for today's match.

**To switch to a different match after loading:**  
Click the **"Change Match"** button on the control panel, then click the correct match card.

---

## Manual mode

Manual mode blocks new remote commands and pauses automation so you can operate PCS Pro directly without interference (for example, to correct an error in the scorer).

> **Note on timing:** If an automation action is already running when you switch to manual mode, it will finish before remote commands are blocked — wait for the current status to settle before taking over PCS Pro directly.

**To enable manual mode:**  
Right-click the tray icon on the garage PC → click **"Switch to Manual Mode"**  
The web control panel displays: *"Manual mode — automation paused by local operator"*

**To disable manual mode:**  
Right-click the tray icon → click **"Resume Automation"**  
The banner disappears and automation resumes.

> **Note:** Manual mode can only be toggled from the garage PC (tray icon). It cannot be enabled from the web control panel.

---

## Updating the PCS Pro password

If the PCS Pro password changes, the System environment variable must be updated. This requires Administrator access on the garage PC. Follow the instructions in the [Configuration Guide — Setting the PCS Pro password](Configuration-Guide.md#setting-the-pcs-pro-password).

---

## Checking for errors

Log files are stored at:

```
C:\PcsRemote\logs\pcs-remote-YYYYMMDD.log
```

where `YYYYMMDD` is today's date — for example, `pcs-remote-20260601.log` for 1 June 2026. If your IT contact used a different deployment folder, substitute it for `C:\PcsRemote\`.

Logs are kept for 7 days and then automatically deleted.

When reporting a problem to your IT contact, share:
- The log file for today (and yesterday if the problem started overnight)
- The exact status shown in the control panel
- A description of what happened and when

---

## Restarting the application

Right-click the tray icon → **Exit**, then log off and log on again. The application restarts automatically within about 30 seconds of logon via the Task Scheduler ONLOGON trigger.

> **Note:** The 30-second restart delay configured for crash recovery does not apply here — a clean exit and re-logon restarts the application immediately.

---

## After a reboot

If the garage PC has been restarted (for example, after a power cut or Windows Update):

1. Log on to the garage PC if auto-logon is not configured.
2. The application starts automatically within about 30 seconds of logon.
3. Check the tray icon appears, then proceed to [Loading a match](#loading-a-match).

---

## SmartScreen prompt

If Windows shows a **"Windows protected your PC"** prompt when first running the application:

1. Click **"More info"**
2. Click **"Run anyway"**

This is a one-time prompt for unsigned executables and will not appear again.

---

## Updating the PCS Pro path

If PCS Pro is reinstalled or upgraded to a different location, the executable path must be updated. See [Configuration Guide — Updating the PCS Pro executable path](Configuration-Guide.md#updating-the-pcs-pro-executable-path). This requires IT contact assistance.
