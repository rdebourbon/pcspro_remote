# SPEC-S-002 — Publish Output Harvesting & File Installation

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **IS Step** | S-002 |
| **HLPS** | HLPS-014 (APPROVED) |
| **Author** | Copilot |
| **Created** | 2026-04-22 |
| **Version** | 0.2 |

---

## 1. Objective

Replace the PoC trivial payload (README.txt) with the real TrayHost self-contained publish output (~409 files). The MSI must install all published files to the target directory, mark `appsettings.json` as upgrade-safe (`NeverOverwrite`), and validate the full install/uninstall cycle with the production payload.

---

## 2. Scope

### 2.1 File Harvesting

The WiX project must automatically harvest all files from the TrayHost publish output directory at build time. Manual file enumeration is impractical for ~409 files and would create a maintenance burden as the file set evolves.

WiX provides a `HeatDirectory` mechanism for build-time directory harvesting. The harvesting must:

- Include all files and subdirectories from the publish output directory.
- Generate component groups automatically — one component per file (WiX best practice for clean uninstall and servicing).
- Exclude `appsettings.json` from the harvest — it is hand-authored as a separate component with `NeverOverwrite` per §2.3. All other files from the publish output are included in the harvest.

### 2.2 Install Directory

The default installation directory is `C:\PcsRemote\` (per HLPS-014 §2 scope item 2). This is a custom root-level directory, **not** under `Program Files`. The current PoC uses `ProgramFilesFolder` — this must be changed.

The install directory must remain user-configurable via the wizard's `INSTALLFOLDER` property (the WixUI_InstallDir dialog set already supports this from S-001).

### 2.3 Special File Handling

**`appsettings.json`**: Must be marked with `NeverOverwrite` so that user-modified configuration survives major upgrades (per HLPS-014 §2 scope item 7, I-C-2, I-R-2). This means the component containing `appsettings.json` must be defined separately from the harvested component group, with explicit `NeverOverwrite="yes"` on the file element.

**`appsettings.Development.json`**: Normal (overwritable) component. It can remain in the harvested set or be defined separately — no special attributes required.

### 2.4 PoC Cleanup

The trivial PoC payload (`Payload/README.txt` and its component/feature references) is replaced by the real publish output. The `Payload/` directory is removed from the installer project.

### 2.5 Build Dependency

The installer build depends on a prior `dotnet publish` of the TrayHost project. This step does NOT create a build script (that is S-007). For S-002, the developer must run `dotnet publish` manually before building the installer. The spec must document the exact publish command and expected output directory so the WiX project's harvest path is deterministic.

The publish command (from `scripts/publish.ps1`): `dotnet publish src\PcsRemote.TrayHost\PcsRemote.TrayHost.csproj -c Release -r win-x64 --self-contained -o publish`

The WiX project references the publish output at `../../publish` (relative to `src/PcsRemote.Installer/`).

---

## 3. Out of Scope

- Build script automation (S-007)
- Configuration-write custom action — `appsettings.json` content is written by the S-004 custom action, not by S-002. S-002 only handles file placement and `NeverOverwrite` marking.
- Custom wizard UI (S-003)
- Any custom actions (S-004, S-005)

---

## 4. Branch Strategy

- Branch: `feature/S-002-publish-harvesting`
- Target: `master`
- Merge: squash-merge after adversarial code review

---

## 5. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | The installer project builds with `dotnet build src/PcsRemote.Installer` (after a prior `dotnet publish` of TrayHost) producing a `.msi` with 0 errors and 0 warnings. |
| AC-2 | The MSI installs all TrayHost publish output files to the target directory (default `C:\PcsRemote\`). File count in the installed directory matches the publish output file count. |
| AC-3 | The `appsettings.json` file is marked `NeverOverwrite` — verified by inspecting the WiX source (component definition includes `NeverOverwrite="yes"` on the File element). |
| AC-4 | The default install directory is `C:\PcsRemote\`, not under `Program Files`. The directory remains user-configurable via the wizard. |
| AC-5 | Clean uninstall removes all application files from the install directory. |
| AC-6 | The trivial PoC payload (`Payload/README.txt`) is removed from the installer project. |
| AC-7 | The existing solution continues to build and test cleanly: `dotnet build PCS_Remote.slnx` produces 0 errors/0 warnings and `dotnet test PCS_Remote.slnx` passes all non-skipped tests. |
| AC-8 | Subdirectory structure from the publish output is preserved in the installed directory (e.g., `wwwroot/` and its children appear at the correct relative paths). |

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| Heat-generated component IDs may change between builds if files are added/removed, potentially causing upgrade issues | Acceptable for S-002 — `MajorUpgrade` (already in place from S-001) handles file-set changes by removing the old version entirely before installing the new one. Component ID stability is not required for major upgrade. |
| Publish output must exist before installer build — build order dependency | Documented in §2.5. Build script (S-007) will automate this. For S-002, manual publish is required and documented. |
| MSI size may be large (~35 MB self-contained publish → ~15-20 MB compressed MSI) | Accepted per HLPS-014 I-R-4. Cabinet compression is handled by WiX automatically. |

---

## 7. Decision Log

| # | Decision | Rationale |
|---|---|---|
| D-1 | INSTALLFOLDER placed under `ProgramFilesFolder` in WiX source tree but Property-overridden to `C:\PcsRemote\` at install time | WiX v5 `Guid="*"` auto-generation (WIX0231) requires a standard-directory ancestor. Using `TARGETDIR` directly caused 646 build errors. The Property override sets the runtime default to `C:\PcsRemote\` per HLPS-014 §2. ICE48 warning suppressed — the hardcoded drive letter is intentional. |
| D-2 | `NeverOverwrite` attribute placed on `Component` element, not `File` element | WiX v5 schema defines `NeverOverwrite` as a Component-level attribute. AC-3 wording references File element — the intent (preserve user config on upgrade) is satisfied. |
| D-3 | `WixToolset.Heat` NuGet package added for build-time directory harvesting | `HeatDirectory` MSBuild item is not built into `WixToolset.Sdk` — requires the separate `WixToolset.Heat/5.0.2` package. |
| D-4 | XSLT transform used to exclude `appsettings.json` from heat harvest | Heat has no built-in file-exclusion metadata. An XSLT identity transform with targeted suppression removes both the Component and its ComponentRef cleanly. |
| D-5 | Publish output contains 644 application files | Self-contained .NET 8 publish includes satellite assemblies in 14 locale subdirectories. Runtime artifacts (`logs/`) are excluded via XSLT. 643 harvested + 1 hand-authored appsettings.json = 644 total. |
| D-6 | `MajorUpgrade Schedule="afterInstallExecute"` | Default schedule (`afterInstallValidate`) uninstalls the old product before installing the new one, making `NeverOverwrite` ineffective — the file is deleted before the new install checks for it. `afterInstallExecute` installs new files first so existing `appsettings.json` is preserved. |
| D-7 | Cabinet embedded in MSI (`MediaTemplate EmbedCab="yes"`) | Default WiX v5 behaviour creates an external `.cab` alongside the `.msi`. Embedding produces a single distributable artifact (~67 MB). |
| D-8 | `CustomActionPoC-OptionB.wxs` removed | S-001 PoC CA prototype is dead code post-S-001. Tree-shaken by the linker today but a latent hazard if a future Fragment references it. Removed as part of PoC cleanup (AC-6). |

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-22 | Opus 4.7, GPT 5.4, Sonnet 4.6 | Unanimous finding: §2.1 "Exclude no files" contradicts §2.3 (appsettings.json must be hand-authored separately with NeverOverwrite). Opus: MEDIUM, GPT: HIGH, Sonnet: HIGH. Accepted — §2.1 amended in v0.2 to explicitly carve out appsettings.json from the harvest. |
| R2 | 2026-04-22 | Sonnet 4.6 | APPROVE. R1 fix verified — §2.1/§2.3 contradiction resolved; AC-2 file count invariant confirmed intact; no regressions. |
| R1-code | 2026-04-22 | Opus 4.7, GPT 5.4, Sonnet 4.6 | Code review of branch diff. Opus: COMMENT (MEDIUM — NeverOverwrite ineffective with default MajorUpgrade schedule). GPT: REQUEST CHANGES (HIGH — log files in harvest; HIGH — external CAB). Sonnet: REQUEST CHANGES (HIGH — log files in harvest; MEDIUM — PoC wxs not deleted). All accepted and fixed: extended XSLT to exclude logs/, embedded CAB, deleted PoC wxs, changed MajorUpgrade to afterInstallExecute. |
