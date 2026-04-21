# SPEC-S-001: KnownElements Registry and DPI-Aware Startup

| Field | Value |
|---|---|
| **Document** | SPEC-S-001-KnownElements-DPI.md |
| **Status** | APPROVED |
| **Version** | 0.2 |
| **Date** | 2026-04-21 |
| **Step ID** | S-001 |
| **Governing HLPS** | HLPS-010-Automation-Hardening.md (APPROVED v0.3) |
| **Governing IS** | IS-010-Automation-Hardening.md (APPROVED v0.2) |
| **Branch** | `feature/S-001-known-elements-dpi` |

---

## 1. Purpose

This step delivers the two foundational pieces that all subsequent IS-010 steps depend on:

1. **KnownElements Registry** — a centralized static class in `PcsRemote.Automation` containing all element identifiers from the diagnostic tool's `KnownElements.cs`, ported as the canonical production registry.
2. **DPI-Aware Process Startup** — ensures the TrayHost process runs with DPI awareness so scoreboard captures at 150% scaling produce correct results.

Per IS-010 S-001: existing local constants in FlaUi* stub classes are **not** removed in this step — each class handles its own migration in its own step (S-003 through S-007). Subsequent steps may add element identifiers discovered inline in `DiagnosticRunner.cs` that were not elevated to the diagnostic `KnownElements.cs` (e.g., `ReplayScreenPreview`, `LiveStreamingControls`).

---

## 2. Scope

### 2.1 KnownElements Registry

**What:** Create a new static class in `PcsRemote.Automation` that contains all element identifier constants currently in the diagnostic tool's `KnownElements.cs`.

**Requirements:**

- R1: The class must be `internal static` (matching project visibility — the Automation project already has `InternalsVisibleTo` for its test project).
- R2: All constants from `tools/AutomationDiagnostic/KnownElements.cs` must be ported, except the three legacy constants removed per R3. Both names and values must match the diagnostic source exactly.
- R3: Three constants currently set to bare `"TODO"` are labelled "Legacy/unused" in the diagnostic tool and are not referenced in any proven diagnostic interaction pattern. Their only references in `FlaUiScoreboardAutomation.cs` are in `NotImplementedException` messages that S-006 will replace entirely. **Decision: remove from the production registry.** If a future step discovers they are needed, they will be added with actual values. The three constants are:
  - `SettingsCogHelpText`
  - `ScoreboardWindowClassName`
  - `ScoreboardWindowName`
- R4: Constants should use `const` where appropriate (compile-time string literals, integer values). The diagnostic tool uses `static` fields for some strings — evaluate whether `const` is more appropriate for the production version since these are fixed identifier values.
- R5: Organise constants with the same region/grouping structure as the diagnostic tool version (by automation step/feature area) for discoverability.
- R6: Preserve **all** inline `//` comment blocks from the diagnostic tool that explain non-obvious element behaviours (e.g., spinner `IsOffscreen` lifecycle, Enter-key-only search, ToolWindow panel structure, MatchTeamView home/away ordering, score summary detection-only purpose). Also preserve the class-level `///` summary.

**Out of scope:**
- Modifying any FlaUi* class to reference the new registry (deferred to S-003–S-007).
- Modifying the diagnostic tool's copy (it may remain standalone).
- Adding new element identifiers not present in the diagnostic tool.

### 2.2 DPI-Aware Process Startup

**What:** Add DPI awareness to the TrayHost process so that screen coordinates and captures are correct at non-100% DPI scaling.

**Requirements:**

- R7: The DPI awareness call must execute **before** any window creation or UI framework initialisation — specifically before `WebApplication.CreateBuilder()` in the TrayHost `Program.cs`.
- R8: Use the `SetProcessDPIAware()` P/Invoke approach (matching the proven diagnostic tool pattern). This is preferred over an app manifest because it is explicit, debuggable, and proven on the garage PC.
- R9: The P/Invoke declaration should follow standard .NET interop patterns with appropriate attributes.
- R10: If the process is running on a system without the API (theoretical — all target systems are Windows 10+), the call should not crash the application. A `try-catch` wrapper or platform guard is acceptable but not mandatory given the Windows-only target.

**Out of scope:**
- Per-monitor DPI awareness (V2) — not needed for single-monitor garage PC.
- Testing DPI correctness (verified manually via scoreboard capture in S-006 per IS-010).

---

## 3. Test Strategy

### 3.1 KnownElements Tests

The KnownElements registry is a static class of constants — it has no behaviour to unit test in the traditional sense. However, verification is still required:

- **T1 — Completeness and value verification:** A test or review step must verify that every constant in the diagnostic tool's version (except the three removed per R3) has a corresponding entry in the production version **with a matching value**. This can be done by automated diff, reflection-based comparison, or manual side-by-side review against the diagnostic source file.
- **T2 — No bare TODO values:** A test (or grep-based verification) should confirm that no constant value is a bare `"TODO"` string.
- **T3 — Compilation gate:** The class must not introduce any new build warnings. Since all subsequent steps depend on it, any compilation issue blocks the entire chain.

### 3.2 DPI Tests

DPI awareness cannot be meaningfully unit-tested — it is a process-level side effect verified by manual scoreboard capture quality on the garage PC (deferred to S-006, per H-SC-4).

- **T4 — Build verification:** The TrayHost project must compile without warnings after adding the P/Invoke declaration.

### 3.3 Regression

- **T5 — All existing tests pass:** All existing tests must remain green (baseline: 587 at HLPS-010 approval).

---

## 4. Acceptance Criteria

| ID | Criterion | Verification |
|----|-----------|-------------|
| AC-1 | Production KnownElements class exists in `PcsRemote.Automation` with class-level XML documentation | Build succeeds + Code review |
| AC-2 | All constants from diagnostic KnownElements are present with matching values (excluding three removed per R3) | T1 — value diff or side-by-side review |
| AC-3 | No constant has a bare `"TODO"` value | T2 — grep / test |
| AC-4 | DPI awareness call is present before UI initialisation in TrayHost | Code review |
| AC-5 | TrayHost compiles without warnings | T4 — build |
| AC-6 | All existing tests pass (baseline: 587 at HLPS-010 approval) | T5 — test run |
| AC-7 | Three legacy TODO constants removed from production registry (R3) | Code review |
| AC-8 | `PcsRemote.Automation` builds without new warnings after KnownElements addition | T3 — build |
| AC-9 | TrayHost launches without crash after DPI change (manual local check) | Manual smoke test |

---

## 5. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `const` vs `static` choice causes issues for downstream steps | Low | Low | Prefer `const` for string/int literals; `static readonly` only if runtime evaluation is needed |
| DPI P/Invoke fails in ASP.NET hosted context | Low | Medium | H-U-2 accepted as non-blocking; verified via S-006 garage PC test. Worst case: switch to manifest approach |
| Legacy TODO constants are referenced somewhere | Low | Low | Grep the codebase before removing; if referenced, resolve or stub with explanatory comment |

---

## 6. Documentation Updates

- No README or architectural doc changes required for this step.
- The KnownElements class-level XML documentation requirement is enforced by AC-1.

---

## 7. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-21 | Opus / GPT-5.4 / Sonnet | NEEDS REVISION → 9 fixes applied (v0.2) |
| R2 | 2026-04-21 | Opus / GPT-5.4 / Sonnet | APPROVED (unanimous, 9/9 PASS, 1 LOW addressed editorially) |
