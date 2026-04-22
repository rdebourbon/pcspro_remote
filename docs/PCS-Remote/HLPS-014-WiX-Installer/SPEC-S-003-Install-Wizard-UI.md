# SPEC-S-003 — Install Wizard UI

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **IS Step** | S-003 |
| **HLPS** | HLPS-014 (APPROVED) |
| **Author** | Copilot |
| **Created** | 2026-04-22 |
| **Version** | 0.2 |

---

## 1. Objective

Extend the installer wizard to collect all configuration inputs required by downstream custom actions (S-004, S-005). The default `WixUI_InstallDir` dialog set currently provides only an installation directory picker. S-003 adds custom dialog panels for PCS Pro settings, HTTP port, and YouTube API credentials. Credential fields must be masked and their MSI properties must be hidden from verbose logs.

---

## 2. Scope

### 2.1 MSI Properties

Define the following public MSI properties with defaults:

| Property | Default | Purpose |
|---|---|---|
| `INSTALLFOLDER` | `C:\PcsRemote\` | Installation directory (already exists from S-002) |
| `PCSPRO_EXEPATH` | `C:\Program Files (x86)\PCS Pro\cricket.exe` | PCS Pro executable path |
| `PCSPRO_PASSWORD` | *(empty)* | PCS Pro password — credential |
| `HTTP_PORT` | `5000` | HTTP listening port |
| `YOUTUBE_CLIENTID` | *(empty)* | YouTube API Client ID |
| `YOUTUBE_CLIENTSECRET` | *(empty)* | YouTube API Client Secret — credential |
| `YOUTUBE_LIVESTREAMID` | *(empty)* | YouTube LiveStream ID |

All property names are uppercase (MSI convention for public properties that survive across process boundaries between the client and server MSI processes).

### 2.2 Credential Hiding

The two credential properties (`PCSPRO_PASSWORD` and `YOUTUBE_CLIENTSECRET`) must be added to `MsiHiddenProperties` (per HLPS I-C-6). This prevents their values from appearing in MSI verbose logs (`msiexec /l*v`). Downstream steps (S-004, S-005) that consume these properties are separately responsible for ensuring they do not re-expose credential values in their own logging or error output.

### 2.3 Custom Wizard Dialogs

The current `WixUI_InstallDir` dialog set provides: Welcome → License → InstallDir → Verify Ready → Install. S-003 extends this sequence by inserting two custom dialog panels between the InstallDir dialog and the Verify Ready dialog:

**Dialog 1 — PCS Pro Settings:**
- PCS Pro executable path (text input, pre-populated with default)
- PCS Pro password (password/masked input)
- HTTP port (text input, pre-populated with default)

**Dialog 2 — YouTube Settings:**
- YouTube Client ID (text input)
- YouTube Client Secret (password/masked input)
- YouTube LiveStream ID (text input)

Navigation flow: Welcome → License → InstallDir → PCS Pro Settings → YouTube Settings → Verify Ready → Install.

Each custom dialog must include standard Back, Next, and Cancel buttons with correct navigation wiring.

### 2.4 Masked Input Fields

Password and secret fields must use a masked edit control (WiX `<Control Type="Edit" Password="yes">`) so that the entered text is displayed as dots/asterisks in the wizard.

### 2.5 Fresh Install Only

S-003 defines the wizard UI for **fresh install only**. Per HLPS §2 item 7, upgrades are silent — the wizard is not displayed and no MSI properties are re-collected. Upgrade behaviour is owned by the `MajorUpgrade` element (S-002) and S-006 (Migration & Major Upgrade). S-003 must not introduce any UI logic that blocks or interferes with the silent upgrade path.

### 2.6 WixUI Customisation Approach

WiX v5's `WixUI_InstallDir` is provided by the `WixToolset.UI.wixext` extension. To insert custom dialogs into the standard sequence, the approach is:

1. Define custom `<Dialog>` elements in a new `.wxs` fragment file.
2. Override the standard dialog navigation using `<Publish>` elements that redirect the InstallDirDlg "Next" button to the first custom dialog, and the last custom dialog's "Next" button to VerifyReadyDlg.
3. The UI element reference (`<ui:WixUI>`) remains, with navigation overrides injected via a `<UI>` element in the package.

This is the standard WiX pattern for extending built-in dialog sets and avoids recreating the entire UI from scratch.

---

## 3. Out of Scope

- Input validation (e.g., checking that the PCS Pro path exists, port is numeric). Validation logic is deferred — the wizard collects raw values; downstream custom actions are responsible for handling invalid inputs per I-C-3 (fail-forward).
- Custom action implementation — S-004 and S-005 consume these properties.
- The actual writing of values to `appsettings.json`, Task Scheduler, Firewall, or environment variables — all S-004/S-005 scope.
- Wizard UI for upgrades — upgrades are silent per HLPS §2 item 7.

---

## 4. Branch Strategy

- Branch: `feature/S-003-install-wizard-ui`
- Target: `master`
- Merge: squash-merge after adversarial code review

---

## 5. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | The installer builds with `dotnet build src/PcsRemote.Installer` producing a `.msi` with 0 errors and 0 warnings. |
| AC-2 | Running the MSI presents the wizard sequence: Welcome → License → InstallDir → PCS Pro Settings → YouTube Settings → Verify Ready → Install. |
| AC-3 | The PCS Pro Settings dialog displays: executable path (text, pre-populated with default), password (masked), and HTTP port (text, pre-populated with `5000`). |
| AC-4 | The YouTube Settings dialog displays: Client ID (text), Client Secret (masked), and LiveStream ID (text). |
| AC-5 | Back/Next/Cancel navigation works correctly across all dialogs in the sequence. |
| AC-6 | Each dialog field is bound to its corresponding MSI property — values entered in the wizard persist through Back/Next navigation and are available to the install session. Verified by entering distinctive sentinel values, navigating forward and backward, and confirming the values are retained. |
| AC-7 | `PCSPRO_PASSWORD` and `YOUTUBE_CLIENTSECRET` are included in `MsiHiddenProperties` — verified by inspecting the WiX source. |
| AC-8 | Running `msiexec /i ... /l*v install.log` and inspecting the log file confirms that `PCSPRO_PASSWORD` and `YOUTUBE_CLIENTSECRET` values do not appear in the log. |
| AC-9 | The existing solution continues to build and test cleanly: `dotnet build PCS_Remote.slnx` produces 0 errors/0 warnings and `dotnet test PCS_Remote.slnx` passes all non-skipped tests. |
| AC-10 | I-U-4 in the HLPS unknowns register is updated to "Resolved" with a note confirming custom wizard panels work. |

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| WiX v5 custom dialog authoring may differ from WiX v3/v4 patterns documented online | Investigate WiX v5 schema for `<Dialog>` and `<Control>` elements. Fall back to WiX v5 source code for the standard dialog sets if documentation is sparse. |
| Dialog navigation overrides may conflict with the standard WixUI_InstallDir sequence | Test navigation thoroughly — verify all Back/Next/Cancel paths work without orphaned dialogs. |
| Masked edit controls may not be available in all WiX v5 UI control types | The `Password="yes"` attribute on `<Control Type="Edit">` is a standard MSI feature inherited by WiX. Low risk. |

---

## 7. Decision Log

*Populated during delivery.*

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-22 | Opus 4.7, GPT 5.4 | Opus: APPROVE (4 LOW observations). GPT: REQUEST CHANGES — HIGH: no AC for property-binding verification; MEDIUM: credential protection wording too narrow; MEDIUM: §2.5 upgrade wording blurs step boundaries. HIGH accepted (AC-6 added). MEDIUM credential note accepted (§2.2 amended). MEDIUM upgrade wording accepted (§2.5 rewritten as fresh-install-only). |
| R2 | 2026-04-22 | GPT 5.4 | APPROVE. All R1 fixes verified — property-binding AC, credential wording, step-boundary clarification. No regressions. |
