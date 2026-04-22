# SPEC-S-001 — WiX Project Scaffold & PoC

| Field | Value |
|---|---|
| **Status** | APPROVED |
| **IS Step** | S-001 |
| **HLPS** | HLPS-014 (APPROVED) |
| **Author** | Copilot |
| **Created** | 2026-04-22 |
| **Version** | 0.3 |

---

## 1. Objective

Create a `PcsRemote.Installer` WiX project that builds alongside the existing solution, produces a minimal MSI, and resolves three blocking unknowns (I-U-1, I-U-2, I-U-4 partial). The PoC validates the entire WiX toolchain from build through install/uninstall and selects a custom action hosting model for all subsequent IS steps.

---

## 2. WiX SDK Version

HLPS-014 references "WiX v5 SDK" (Scope item 1, A-3, I-U-1). I-U-1 explicitly asks whether `WixToolset.Sdk` works with the project's .NET 8 build toolchain — the PoC's job is to determine the correct SDK version. Preliminary research indicates that WiX Toolset v7.0.0 is the current stable release, with v5 and v6 at end-of-community-support. The actual SDK version used is a **PoC deliverable**, recorded in the Decision Log (§7) during delivery. Delivery of this step will also update HLPS-014 A-3 to reflect the actual SDK version alongside the I-U-1 resolution.

---

## 3. Scope

### 3.1 WiX Project

Create `src/PcsRemote.Installer/PcsRemote.Installer.wixproj` using the `WixToolset.Sdk` project SDK (version determined by the PoC — see §2). The project produces an MSI (`OutputType=Package`).

**Solution integration:** The installer project is **not** added to `PCS_Remote.slnx`. The `.slnx` format does not support per-project build exclusion, and including the WiX project would break `dotnet build PCS_Remote.slnx` for developers without the WiX SDK. The project lives in the source tree at `src/PcsRemote.Installer/` and is built independently via `dotnet build src/PcsRemote.Installer` (and the future build script in S-007). This supersedes the "Add it to the solution" instruction in IS-014 S-001 — that document will be amended as part of this step's delivery (see AC-10).

**Directory.Build.props compatibility:** The root `Directory.Build.props` sets `Nullable`, `ImplicitUsings`, and `TreatWarningsAsErrors` — properties the WiX SDK does not consume and may reject. The installer directory must carry a local `Directory.Build.props` that does **not** import the parent (empty `<Project />` element or one that explicitly clears the incompatible properties). This ensures existing non-installer projects continue to inherit the root file unchanged.

**Central Package Management:** The installer project must set `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>` in its `.wixproj` because WiX extension packages use a parallel versioning scheme unrelated to the .NET package versions in `Directory.Packages.props`. This avoids CPM resolution conflicts while leaving the root CPM configuration intact for all other projects.

### 3.2 Minimal Product Definition

Create a `Package.wxs` file with:

- `Package` element with `Name`, `Manufacturer`, `Version`, `UpgradeCode`
- A stable `UpgradeCode` GUID (generated once, never changed — required for `MajorUpgrade` in S-006)
- A `StandardDirectory` for `ProgramFilesFolder` and a custom install directory
- A trivial payload: a single text file (e.g., `README.txt`) installed to the target directory
- The `MajorUpgrade` element with a downgrade error message (placeholder for S-006 refinement)

### 3.3 Custom Action Hosting Evaluation

Evaluate two CA hosting models by implementing a minimal "hello world" custom action using each:

**Option A — Managed C# via `WixToolset.Dtf`:** Create a minimal .NET Framework class library (`src/PcsRemote.Installer.CustomActions`, targeting `net472`) with a single `[CustomAction]` method that writes a log entry. Reference it from the WiX project. (`WixToolset.Dtf` is the production-grade managed CA path in WiX v5; `WixToolset.Dnc.wixext` targets .NET 8 but is experimental and not production-ready in v5.)

**Option B — PowerShell via `WixToolset.Util.wixext`:** Use `WixQuietExec` from the `WixToolset.Util.wixext` extension to run a simple PowerShell command that writes to a log file.

Evaluate each approach against these criteria:
- **Build complexity:** additional projects, NuGet packages, build steps
- **Debuggability:** ability to attach debugger, log inspection, error reporting
- **Dependency footprint:** does it require .NET on the target? (must not — per I-C-4)
- **Alignment with self-contained model:** does it work when no .NET runtime is pre-installed?
- **Reliability:** behaviour under UAC, deferred execution, impersonation

Select the approach that best satisfies these criteria. Document the decision with rationale.

### 3.4 I-U-4 Partial Validation

Verify that a `WixUI` dialog set can be referenced from the WiX project (add a `PackageReference` for `WixToolset.UI.wixext` and reference a standard dialog set). Full custom wizard panels are deferred to S-003.

---

## 4. Branch Strategy

- Branch: `feature/S-001-wix-poc`
- Target: `master`
- Merge: squash-merge after adversarial code review

---

## 5. Acceptance Criteria

| ID | Criterion |
|---|---|
| AC-1 | `src/PcsRemote.Installer/PcsRemote.Installer.wixproj` exists and builds with `dotnet build src/PcsRemote.Installer` producing a `.msi` file with 0 errors and 0 warnings. |
| AC-2 | The MSI can be installed on the development machine, placing the trivial payload in the target directory. Clean-machine validation is deferred to S-008's smoke test; AC-2 only demonstrates the install cycle technically functions. |
| AC-3 | The MSI can be uninstalled, removing the payload and install directory. Same dev-machine caveat as AC-2. |
| AC-4 | The chosen CA hosting model is documented in this spec's Decision Log (§7) with rationale against the evaluation criteria in §3.3. |
| AC-4a | A minimal working custom action prototype has been implemented for both Option A (`WixToolset.Dtf`, `net472`) and Option B (`WixToolset.Util.wixext` / `WixQuietExec`) prior to the hosting model decision. The non-selected option's rejection rationale is recorded in §7. |
| AC-5 | I-U-1 status in HLPS-014 updated to "Resolved" with the actual SDK version used. HLPS-014 A-3 updated to reflect the actual SDK version. IS-014 "WiX v5" references in the Overview and S-001 updated to reflect the actual SDK version. |
| AC-6 | I-U-2 status in HLPS-014 updated to "Resolved" with the chosen hosting model. |
| AC-7 | I-U-4 status in HLPS-014 updated to "Partially Resolved" (standard WixUI dialog set works; custom panels deferred to S-003). |
| AC-8 | The existing solution continues to build and test cleanly: `dotnet build PCS_Remote.slnx` produces 0 errors/0 warnings and `dotnet test PCS_Remote.slnx` passes all non-skipped tests. The installer project is not in the solution file and does not affect these commands. |
| AC-9 | The installer project's local `Directory.Build.props` and CPM opt-out do not alter the behaviour of `Directory.Build.props` or `Directory.Packages.props` for existing projects. |
| AC-10 | IS-014 S-001 amended to replace the "Add it to the solution" instruction with the independent-build approach adopted in this spec. |

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| WiX SDK incompatible with `Directory.Build.props` | The installer directory carries a local `Directory.Build.props` (empty `<Project />`) that blocks inheritance of incompatible properties. Existing projects are unaffected. |
| Managed C# CA requires a .NET runtime on target (violates I-C-4) | `WixToolset.Dtf` uses .NET Framework 4.x (pre-installed on Windows 10/11), so I-C-4 is satisfied. If Dtf were unavailable, PowerShell via `WixQuietExec` would be the fallback (also always available on Windows 10/11). |
| Central package management conflicts with WiX package references | The installer project disables CPM via `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>`. Root CPM is unchanged. |

---

## 7. Decision Log

| Decision | Option | Rationale |
|---|---|---|
| WiX SDK version | `WixToolset.Sdk/5.0.2` | v5.0.2 is the latest v5 release. v7.0.0 exists but requires OSMF EULA acceptance at build time (`WIX7015` error) — unnecessary friction for a cricket club project. v5.0.2 builds cleanly with .NET 8 SDK, produces valid MSI, resolves I-U-1. |
| CA hosting model | **Option B — PowerShell via `WixToolset.Util.wixext` / `WixQuietExec`** | Both options were prototyped and built successfully. Option B selected for: (1) no additional .NET project or build step — single WiX project vs. Option A's separate `net472` class library + DTF build; (2) PowerShell cmdlets natively handle all S-004/S-005 operations (`Register-ScheduledTask`, `New-NetFirewallRule`, `Set-Content`/`ConvertTo-Json`, `SetEnvironmentVariable`); (3) scripts are plain-text, testable outside MSI context — DTF CAs require native SfxCA wrapper, harder to debug; (4) both satisfy I-C-4 (PowerShell 5.1 and .NET Framework 4.x are both pre-installed on Windows 10/11); (5) lower cognitive complexity (one project vs. two with different target frameworks). |
| Option A rejection | Managed C# via `WixToolset.Dtf` | Prototyped successfully (`PcsRemote.Installer.CustomActions`, `net472`, `WixToolset.Dtf.WindowsInstaller` + `WixToolset.Dtf.CustomAction` 5.0.2). DTF `PackCustomAction` produced a `.CA.dll` with embedded SfxCA shim. Rejected because: (1) requires a second project with a different target framework (`net472` vs solution's `net8.0`); (2) all required system operations have native PowerShell cmdlets — no benefit from managed C#; (3) DTF SfxCA debugging is significantly harder than PowerShell script debugging; (4) namespace changed from `Microsoft.Deployment.WindowsInstaller` to `WixToolset.Dtf.WindowsInstaller` in v5 — documentation ecosystem is stale, increasing maintenance risk. |

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-22 | Opus 4.7, GPT 5.4, Sonnet 4.6 | Unanimous REQUEST CHANGES — 2 HIGH (v5→v7 pre-decision violates HLPS A-3; slnx exclusion infeasible), 5 MEDIUM (CPM strategy, Directory.Build.props fallback, both CA prototypes need AC, Decision Log preempts outcome, AC-8 test scope ambiguous), 4 LOW (dev-machine caveat, Dnc naming, A-3 update, no-.NET verification — deferred to delivery). All accepted findings applied in v0.2. |
| R2 | 2026-04-22 | GPT 5.4, Sonnet 4.6 | GPT: APPROVE. Sonnet: REQUEST CHANGES — 1 MEDIUM (IS-014 S-001 "Add it to the solution" contradiction not acknowledged), 1 LOW (AC-5 should cover IS-014 version refs). Both applied in v0.3. |
| R3 | 2026-04-22 | Sonnet 4.6 | APPROVE. R2 fixes verified, no regressions. |
