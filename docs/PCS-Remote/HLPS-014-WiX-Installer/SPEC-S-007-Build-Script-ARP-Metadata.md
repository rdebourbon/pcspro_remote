# SPEC-S-007 — Build Script & ARP Metadata

| Field | Value |
|---|---|
| **Document** | SPEC-S-007-Build-Script-ARP-Metadata.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Date** | 2026-04-22 |
| **IS Step** | S-007 |
| **HLPS** | HLPS-014 (APPROVED) |
| **Dependencies** | SPEC-S-006 (APPROVED — complete MSI with upgrade support) |

---

## 1. Objective

Create the single-command build pipeline for the MSI installer and complete the Add/Remove Programs (ARP) metadata so the installed application looks professional and is version-trackable.

After this step:
- A developer runs one script to produce a release-ready `PcsRemote-Setup.msi`
- The installed application appears in Apps & Features with correct name, publisher, version, and icon
- MSI verbose logs do not leak credential values

---

## 2. Requirements

### 2.1 Build Script (`scripts/build-installer.ps1`)

Create a PowerShell script that orchestrates the full build pipeline:

1. **Publish:** Invoke the existing `scripts/publish.ps1` to run `dotnet publish` for `PcsRemote.TrayHost` as self-contained win-x64 to the `publish/` directory. This avoids duplicating the publish logic and its artefact verification checks.
2. **Build MSI:** Run `dotnet build` on the WiX project (`src/PcsRemote.Installer`) in **Release** configuration to produce the MSI. The publish output must exist first (WiX `HarvestDirectory` depends on it).
3. **Copy MSI:** Copy the built MSI from the WiX project Release output directory to `artifacts/PcsRemote-Setup.msi` at the repository root. The `artifacts/` directory is separate from `publish/` to prevent the MSI from being harvested into subsequent WiX builds.
4. **Verification:** Confirm the MSI file exists in `artifacts/` and report its size.

The script must accept an optional `-Version` parameter. When supplied, it is forwarded to the WiX build as `/p:Version=...` (per §2.2). When omitted, the script does not pass `/p:Version`, allowing the `.wixproj` `<Version>` default to drive the MSI product version.

The script follows the same conventions as `scripts/publish.ps1`: `Set-StrictMode`, `$ErrorActionPreference = 'Stop'`, coloured status output, early exit on build failure.

### 2.2 Version Parameterisation

The `Package Version` attribute in Package.wxs is currently hardcoded to `1.0.0.0`. Make it parameterisable using the standard MSBuild `<Version>` property as the single source of truth, per HLPS-014 scope item 9:

1. Add a `<Version>` property in the `.wixproj` with a default value of `1.0.0.0`.
2. Wire `<Version>` into a WiX preprocessor variable via `<DefineConstants>$(DefineConstants);ProductVersion=$(Version)</DefineConstants>` in the `.wixproj`.
3. Reference the preprocessor variable in the `Package Version` attribute: `Version="$(var.ProductVersion)"`.
4. The build script passes the user-supplied version as `/p:Version=...`, which flows through `<Version>` → `DefineConstants` → WiX preprocessor → `Package Version`.

This allows the developer to produce versioned MSIs (e.g., `build-installer.ps1 -Version 1.2.0.0`) while maintaining a sensible default.

**MSI version constraint:** Windows Installer's `ProductVersion` is effectively 3 fields (`major.minor.build`); the 4th field (revision) is accepted but silently ignored by the MSI engine and not considered by `MajorUpgrade`. The script should warn if the revision field is non-zero.

### 2.3 ARP Metadata (Add/Remove Programs)

The MSI already sets display name (`Package Name`) and publisher (`Package Manufacturer`). Two additions are needed:

1. **Icon:** Add a WiX `Icon` element referencing the custom `pcs-remote-normal.ico` (already exists at `src/PcsRemote.TrayHost/Resources/`). The `Icon` element's `Id` attribute **must end with `.ico`** (e.g., `Id="PcsRemote.ico"`) — this is an MSI requirement for ARP icon rendering. Set the `ARPPRODUCTICON` property to this `Id` so the icon appears in Apps & Features.
2. **Help link:** Not required by HLPS. No `ARPHELPLINK` property will be set.

Version is already derived from the `Package Version` attribute (§2.2). No further ARP configuration needed.

### 2.4 Credential Log Redaction Verification

HLPS I-SC-11 requires that MSI verbose logging does not leak credentials. The `Hidden="yes"` attribute on `PCSPRO_PASSWORD` and `YOUTUBE_CLIENTSECRET` properties (set in S-003) automatically adds them to `MsiHiddenProperties`, which redacts their values in verbose logs.

No code change is needed — this is a static confirmation that the `Hidden="yes"` guards are in place. Runtime verification (installing with sentinel values under `msiexec /l*v` and confirming redaction) is deferred to S-008's end-to-end smoke test.

**Traceability note:** I-SC-11 is shared between S-007 (static confirmation) and S-008 (runtime verification).

---

## 3. Changes Required

### 3.1 New File: `scripts/build-installer.ps1`

Build pipeline script per §2.1. Accepts `-Version` parameter, calls `publish.ps1`, runs WiX build in Release, copies MSI to `artifacts/`.

### 3.2 Modified: `src/PcsRemote.Installer/PcsRemote.Installer.wixproj`

Add `<Version>` property and wire it into `DefineConstants` (§2.2).

### 3.3 Modified: `src/PcsRemote.Installer/Package.wxs`

- Replace hardcoded `Version="1.0.0.0"` with preprocessor variable reference (§2.2).
- Add `Icon` element and `ARPPRODUCTICON` property (§2.3).

---

## 4. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `scripts/build-installer.ps1` runs to completion and produces `artifacts/PcsRemote-Setup.msi` |
| AC-2 | The MSI file is a valid Windows Installer package (non-zero size) |
| AC-3 | Running the script with `-Version 1.2.3.0` produces an MSI with version `1.2.3.0` in its product metadata |
| AC-4 | Running the script with no `-Version` parameter produces an MSI with version `1.0.0.0` (the `.wixproj` `<Version>` default drives the value when no override is passed) |
| AC-5 | Package.wxs `Icon` element has an `Id` ending in `.ico`, references `pcs-remote-normal.ico`, and `ARPPRODUCTICON` matches the `Icon` `Id` |
| AC-6 | Build: 0 errors, 0 warnings; existing test suite passes |
| AC-7 | Credential log redaction: `Hidden="yes"` confirmed on `PCSPRO_PASSWORD` and `YOUTUBE_CLIENTSECRET` (runtime verification under `msiexec /l*v` deferred to S-008 smoke test; I-SC-11 traceability shared S-007 / S-008) |

---

## 5. Branching & Commits

- **Branch:** `feature/S-007-build-script-arp`
- **Commit strategy:** Small, focused commits per logical change.

---

## 6. Risks

| ID | Risk | Mitigation |
|---|---|---|
| R-1 | WiX preprocessor variable syntax incorrect — version parameterisation fails to build | Low risk. WiX v5 `DefineConstants` + `$(var.X)` is well-documented and widely used. Build verification catches this immediately. |
| R-2 | Icon file path reference breaks on different developer machine layouts | Low risk. Relative path from installer project to TrayHost resources is within the repository. |
| R-3 | Developer supplies version with non-zero revision (e.g., `1.0.0.1`) — MSI silently ignores revision, `MajorUpgrade` won't detect the change | Low risk. Script emits a warning when revision is non-zero. Documented in §2.2. |

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-22 | Opus 4.7, GPT 5.4 | REQUEST CHANGES — 2 HIGH (version source / HLPS misalignment, MSI in publish/ harvested), 4 MEDIUM (DRY publish, DefineConstants fragility, Release config, I-SC-11 traceability), 3 LOW (Icon Id suffix, verification method, magic bytes). All accepted except F-08 (verification method is impl detail). Applied in v0.2. |
| R2 | 2026-04-22 | Opus 4.7, GPT 5.4 | Both REQUEST CHANGES — 1 HIGH each (same finding: §2.1 vs §2.2 contradiction on `/p:ProductVersion` vs `/p:Version`). ACCEPTED and fixed in v0.3. Opus 2 LOW (AC-4 wording, AC-2 validity) — AC-4 ACCEPTED, AC-2 REJECTED (build validates MSI). GPT 1 MEDIUM (4-field AC examples) — REJECTED (WiX stores full 4-field version; 3-field constraint is upgrade-only). Effective unanimous approval. |
