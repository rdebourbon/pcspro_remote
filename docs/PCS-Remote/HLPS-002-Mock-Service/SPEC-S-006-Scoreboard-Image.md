# SPEC-S-006: Scoreboard Image Generation

| Field | Value |
|---|---|
| **Document** | SPEC-S-006-Scoreboard-Image.md |
| **Status** | APPROVED |
| **Version** | 0.3 |
| **Step** | IS-002 S-006 |
| **Date** | 2026-04-11 |
| **Governing Docs** | HLPS-002-Mock-Service.md (APPROVED v0.3), IS-002-Mock-Service.md (APPROVED v0.2) |
| **Branch** | `feature/hlps002-S006-scoreboard-image` |
| **Branch target** | `master` |

---

## 1. Context

S-005 (Match Data) is complete. `CaptureScoreboardImageAsync` currently throws `NotImplementedException`. This step implements it to return a valid JPEG byte array, and exercises the `ImageVariationProbability` option introduced in S-002.

The success criterion M-SC-4 requires:
- Returned bytes are a valid JPEG
- With `ImageVariationProbability = 1.0`, no two consecutive calls return identical bytes
- With `ImageVariationProbability = 0.0`, all calls return identical bytes

---

## 2. Scope

### 2.1 NuGet Dependency

`System.Drawing.Common` must be added as a NuGet package reference to `PcsRemote.Automation.Mock` and pinned in `Directory.Packages.props`. The package is Windows-only by design; this is an accepted constraint (C-1). If CI runners become Linux-hosted, this step must be revisited (IS-002 §S-006 risk note).

### 2.2 Image Generation

`CaptureScoreboardImageAsync` must:

- Generate a solid-colour bitmap with overlaid text (score placeholder such as `"PCS Remote — Mock Scoreboard"`).
- Encode the bitmap as a JPEG and the awaited result is a `byte[]`.
- JPEG format is validated for test purposes by checking that the returned byte sequence begins with `FF D8 FF` (JPEG SOI `FF D8` + first marker prefix `FF`) **and** ends with the JPEG EOI marker `FF D9`. This is a pragmatic proxy check; the `System.Drawing.Common` JPEG encoder is trusted to produce structurally sound output.

### 2.3 Variation Logic

The method uses the existing `_rng` field (shared with error injection, introduced in S-004):

- On each call, draw from `_rng.NextDouble()` and compare against `ImageVariationProbability` using **strictly less-than** (`draw < ImageVariationProbability`). This matches the error-injection convention already in the codebase.
- If variation is triggered **or** no image has been generated yet: produce a new image, cache the result, and return the new bytes.
- If variation is **not** triggered and a cached image exists: return the cached bytes unchanged.

The byte-level difference guarantee for R-3 requires a deterministic uniqueness mechanism. The implementation **MUST** embed a monotonically-increasing **image-generation counter** (an instance field, initialised to zero in the constructor, incremented once per image-generation event — not once per method call) into the rendered text. This ensures each generated image is textually unique regardless of any colour choices. Additional visual variation (colour, background) is optional.

> **Design note — shared `_rng` and test isolation:** The image variation draw and the error injection draws share the same `_rng` instance. When `RngSeed` is null (the default), `_rng` is assigned `Random.Shared` — a process-global static instance shared across all SUT instances in the test process. Tests for image variation **MUST** set `RngSeed` to a fixed integer value so that a private `new Random(seed)` instance is created, fully isolated from `Random.Shared` and from concurrent test methods. Additionally set `ErrorProbability = 0.0` as defensive hygiene when lifecycle methods are called before `CaptureScoreboardImageAsync` within the same test. Tests that omit `RngSeed` use `Random.Shared` and are not isolated. For the extreme boundary values IVP=0.0 and IVP=1.0 tested in AC-2 and AC-3 the RNG outcome does not affect the test result, but setting `RngSeed` is the correct defensive pattern and must be followed for all image AC tests.

> **Design note — unconditional RNG draw (R-5):** R-5 requires the `_rng.NextDouble()` draw to occur on every call, even when `ImageVariationProbability = 0.0`. This discipline is the same as the error injection pattern and cannot be verified by an automated test at the extreme boundary values. It is verified by code review during PR. This is an accepted code-review-only constraint; no numbered AC covers it.

### 2.4 Thread Safety

`CaptureScoreboardImageAsync` is not a lifecycle method and is not protected by the semaphore. Concurrent calls from multiple web clients are theoretically possible but are out of scope for the mock's correctness guarantee. The mock is a singleton registered for single-service-instance use; concurrent image capture is an accepted limitation.

---

## 3. Requirements

| Ref | Requirement |
|---|---|
| R-1 | `System.Drawing.Common` is added as a NuGet dependency to `PcsRemote.Automation.Mock` and pinned in `Directory.Packages.props`. |
| R-2 | The awaited result of `CaptureScoreboardImageAsync` is a `byte[]` whose first two bytes are `0xFF`, `0xD8` (JPEG SOI), third byte is `0xFF` (first marker prefix), and whose last two bytes are `0xFF`, `0xD9` (JPEG EOI). |
| R-3 | With `ImageVariationProbability = 1.0`, no two consecutive calls to `CaptureScoreboardImageAsync` resolve to byte-identical results. |
| R-4 | With `ImageVariationProbability = 0.0`, all calls to `CaptureScoreboardImageAsync` resolve to byte-identical results. |
| R-5 | The variation draw (`_rng.NextDouble() < ImageVariationProbability`) is performed on every call regardless of the probability value — no short-circuit. *(Code-review-only constraint; no automated AC.)* |
| R-6 | All pre-existing tests continue to pass without modification. |

---

## 4. Acceptance Criteria

### AC-1 — JPEG validity
With `RngSeed` set, the awaited result of `CaptureScoreboardImageAsync` is a non-empty byte array whose first three bytes are `0xFF`, `0xD8`, `0xFF` (JPEG SOI + marker prefix) and whose last two bytes are `0xFF`, `0xD9` (JPEG EOI).

### AC-2 — Probability 1.0: always different bytes
With `RngSeed` set and `ImageVariationProbability = 1.0`, three consecutive calls to `CaptureScoreboardImageAsync` all resolve to byte arrays that are pairwise non-identical (`!SequenceEqual`).

### AC-3 — Probability 0.0: always identical bytes
With `RngSeed` set and `ImageVariationProbability = 0.0`, three consecutive calls to `CaptureScoreboardImageAsync` all resolve to byte-identical results (`SequenceEqual`).

### AC-4 — First call with default options exercises cache-miss path
With default options (`ImageVariationProbability = 0.2`, `RngSeed` set), the first call to `CaptureScoreboardImageAsync` (cache empty) resolves to a non-empty byte array beginning with JPEG SOI+marker prefix and ending with JPEG EOI, confirming the cache-miss generation path is exercised. *(Note: the byte assertions are identical to AC-1; AC-4's distinct contribution is explicitly naming the cache-miss code path as a required test concern.)*

### AC-5 — Existing tests unaffected
All pre-existing tests in `PcsRemote.Automation.Mock.Tests` and `PcsRemote.Core.Tests` continue to pass without modification (no regressions).

---

## 5. Out of Scope

- Real scoreboard capture (FlaUI / PrintWindow — HLPS-006)
- Delta detection between consecutive images (HLPS-004)
- Thread-safe concurrent image capture
- Image format other than JPEG
- Any change to the lifecycle semaphore logic

---

## 6. Files Affected

| File | Change |
|---|---|
| `Directory.Packages.props` | Add `System.Drawing.Common` version pin |
| `src/PcsRemote.Automation.Mock/PcsRemote.Automation.Mock.csproj` | Add `System.Drawing.Common` package reference |
| `src/PcsRemote.Automation.Mock/MockPcsProAutomationService.cs` | Implement `CaptureScoreboardImageAsync`; add `_lastImageBytes` cached image field and `_imageGenCounter` instance field (image-generation counter) |
| `tests/PcsRemote.Automation.Mock.Tests/MockPcsProAutomationServiceTests.cs` | Add tests for AC-1 through AC-4 |

---

## 7. Test Strategy

Tests are added to the existing `MockPcsProAutomationServiceTests` class using the existing `CreateSut` helper. All image AC tests must set `RngSeed` to a fixed integer value to ensure a private, isolated `Random` instance (not `Random.Shared`). Also set `ErrorProbability = 0.0` as defensive hygiene if lifecycle methods are called before `CaptureScoreboardImageAsync` within the same test.

- **AC-1**: With `RngSeed` set, await `CaptureScoreboardImageAsync`; assert first three bytes are `FF D8 FF` (SOI + marker prefix) and last two bytes are `FF D9` (EOI).
- **AC-2**: Create SUT with `RngSeed` set, `ImageVariationProbability = 1.0`; await three calls; assert all three arrays are pairwise non-identical using `SequenceEqual`.
- **AC-3**: Create SUT with `RngSeed` set, `ImageVariationProbability = 0.0`; await three calls; assert all three arrays are byte-identical.
- **AC-4**: Create SUT with `RngSeed` set and default `ImageVariationProbability` (0.2); await first call (cache empty); assert result is non-empty, JPEG SOI+marker-prefix-prefixed, EOI-terminated.
- **AC-5**: Verified by running `dotnet test` — all pre-existing tests pass.

---

## 8. Review History

| Round | Date | Reviewers | Result |
|---|---|---|---|
| R1 | 2026-04-11 | Default + Claude Sonnet 4.6 | NEEDS REVIEW — 2 HIGH, 4 MEDIUM, 3 LOW (Default); 1 HIGH, 3 MEDIUM, 3 LOW (Sonnet) |
| R2 | 2026-04-11 | Default + Claude Sonnet 4.6 | **APPROVED** — 0 blockers each; 3 LOW polish each |

### R1 Disposition

| ID | Severity | Finding | Resolution |
|---|---|---|---|
| R1-1 (both) | HIGH | R-5 has no AC; untestable at boundary values | Demoted to code-review-only design note in §2.3; R-5 annotation added to requirements table; consistent with error injection precedent |
| R1-2 (both) | HIGH | `Random.Shared` isolation not addressed; `RngSeed` not mentioned in §2.3 or §7 | §2.3 design note extended with full `RngSeed`/`Random.Shared` guidance; §7 now explicitly requires `RngSeed` on all image AC tests |
| R1-3 (R1) | MEDIUM | "call counter" vs "image-generation counter" ambiguity | Terminology corrected throughout; §2.3 and §6 now use "image-generation counter"; counter increments on generation events only |
| R1-4 (R1) | MEDIUM | Counter was advisory ("e.g."); R-3 is absolute | Counter now stated as MUST in §2.3; "e.g." removed |
| R1-5/F-002 (both) | MEDIUM | Magic bytes insufficient for JPEG validity; EOI marker missing | AC-1 updated to assert both SOI (`FF D8 FF`) and EOI (`FF D9`); R-2 updated; §2.2 clarified as "pragmatic proxy check" |
| R1-6 (R1) | MEDIUM | `Random.Shared` interference risk unaddressed in design note | Addressed by R1-2 fix |
| R1-8 (R1) | LOW | Comparison operator `<` not specified | §2.3 now states "`draw < ImageVariationProbability`" explicitly; R-5 updated |
| R1-7/F-003 (both) | LOW | AC-5 hardcodes count 67 — fragile | AC-5 reworded to "all pre-existing tests … pass (no regressions)"; count-agnostic |
| R1-9 (R1) | LOW | Async method described as returning `byte[]` not `Task<byte[]>` | R-2 and ACs now say "awaited result" |
| F-005 (R2) | LOW | Counter scope (instance vs static) unspecified | §2.3 now specifies "instance field, initialised to zero in the constructor" |
| F-006 (R2) | LOW | AC-4 subsumed by AC-1 | AC-4 reframed as cache-miss path test (unique contribution made explicit) |
| F-007 (R2) | LOW | AC-2 three-call coverage doesn't probe R-5 boundary | Accepted; corollary of R1-1 fix — R-5 is code-review only |

### R2 Disposition

| ID | Severity | Finding | Resolution |
|---|---|---|---|
| R2-1 (R1) | LOW | AC-1 missing `RngSeed` precondition in §4 and §7 bullet | `RngSeed` precondition added to AC-1 §4 and §7 bullet |
| R2-2 (R1) | LOW | AC-4 cache-miss distinction is intent-only; overlap with AC-1 not acknowledged | Note added to AC-4 acknowledging overlap and stating distinct audit rationale |
| R2-3 (R1) | LOW | §7 EP=0.0 guidance unconditional vs conditional in §2.3 | §7 preamble aligned: "if lifecycle methods are called before `CaptureScoreboardImageAsync`" |
| R2-4 (R2) | LOW | IS-002 S-006 verification intent missing EOI | IS-002 updated to include EOI in verification intent |
| R2-5 (R2) | LOW | "JPEG SOI" label applied to 3 bytes; SOI is technically 2 bytes | Label corrected throughout: "JPEG SOI (`FF D8`) + first marker prefix (`FF`)" |
| R2-6 (R2) | LOW | C-1 never formally defined | Accepted; C-1 is established project context in HLPS-002 §2; no structural change warranted at SPEC level |
