# SPEC-S-002: BroadcastTitleRenderer (Title Template Engine)

| Field | Value |
|---|---|
| **Document** | SPEC-S-002-BroadcastTitleRenderer.md |
| **Status** | APPROVED — Pending user approval |
| **Version** | 0.2 |
| **Date** | 2026-04-17 |
| **IS Step** | S-002 |
| **Governing HLPS** | HLPS-008-YouTube-LiveStream.md v0.4 (APPROVED) |
| **Governing IS** | IS-008-YouTube-LiveStream.md v0.3 (APPROVED) |
| **Branch** | `feature/IS-008-S-002-broadcast-title-renderer` |

---

## 1. Objective

Add a `BroadcastTitleRenderer` class to `PcsRemote.Core` that substitutes named tokens in a configurable title template string using data from a `MatchInfo` record. This is a pure business logic component with no project-layer dependencies (only `Microsoft.Extensions.Logging.Abstractions`), consumed by both the mock and real YouTube service implementations to generate broadcast titles.

---

## 2. Scope

### In Scope

1. **`BroadcastTitleRenderer` class** in `PcsRemote.Core` — a sealed class with a single public method that renders a title string from a template and a `MatchInfo`.

2. **Constructor** — accepts an `ILogger<BroadcastTitleRenderer>` for structured logging of fallback/warning scenarios. No other dependencies.

3. **`Render` method** — signature: `string Render(string? template, MatchInfo match)`.
   - Accepts a nullable template string and a non-null `MatchInfo`.
   - Returns the fully substituted, validated title string.

4. **Token substitution** — per HLPS-008 §4.5 grammar:
   - `{HomeTeam}` → `match.HomeTeam`
   - `{AwayTeam}` → `match.AwayTeam`
   - `{MatchType}` → `match.MatchType`
   - `{Date}` → `match.MatchDate.ToString("d")` (short date, current culture)
   - `{Date:format}` → `match.MatchDate.ToString(format)` where `format` is a valid `DateOnly` format string

5. **Error handling** — all degradation is graceful, no exceptions thrown:
   - **Empty, null, or whitespace-only template** → use default `"{HomeTeam} vs {AwayTeam}"` (S-YT-9)
   - **Unknown token** (e.g. `{Venue}`) → retained literally in the output
   - **Null or empty match field** → substituted with empty string (defensive; `MatchInfo` fields are non-nullable with sentinel defaults, but the renderer handles empty strings gracefully)
   - **Invalid `{Date:format}`** (including time-related specifiers `H`, `h`, `m`, `s`, `t`, `z`) → fall back to short date (`match.MatchDate.ToString("d")`), log a `Warning` with the invalid format string
   - **Resulting title exceeds 100 characters** → truncated to 97 characters + `"..."` (YouTube API limit)

6. **Unit tests** in `PcsRemote.Core.Tests/BroadcastTitleRendererTests.cs`.

### Out of Scope

- Configuration binding (`YouTube:BroadcastTitleTemplate` key) — wired in S-004 DI step.
- Integration with `IYouTubeLiveStreamService` — consumed by S-003 (mock) and S-006 (real).
- Token set extensibility — fixed set of 4 tokens per HLPS-008 §4.5.

### HLPS Deviation

IS-008 §S-002 explicitly relocates `BroadcastTitleRenderer` from `PcsRemote.YouTube` (per HLPS-008 §5.1) to `PcsRemote.Core`. Rationale: `PcsRemote.YouTube.Mock` (net8.0) depends on it but cannot reference `PcsRemote.YouTube` (net8.0-windows with Google API dependencies). `BroadcastTitleRenderer` has no external dependencies and aligns with Core's zero-dependency rule.

### IS Deviation

IS-008 S-002 states time-related format specifiers are "rejected at startup". HLPS-008 §4.5 v0.4 explicitly overrides this to runtime-only fallback (confirmed by HLPS audit trail item 4: "Unified on runtime-fallback; all invalid forms degrade with Warning log"). This SPEC follows the HLPS as the governing document — all validation is runtime-only with graceful degradation.

---

## 3. Implementation Detail

### 3.1 Token Parsing Strategy

Use `Regex` to find all `{TokenName}` and `{TokenName:format}` patterns. A single regex pass replaces each match via a `MatchEvaluator` delegate.

**Pattern:** `\{(\w+)(?::([^}]+))?\}`

This captures:
- Group 1: token name (e.g. `HomeTeam`, `Date`)
- Group 2 (optional): format specifier (e.g. `d`, `yyyy-MM-dd`)

### 3.2 Token Resolution

The `MatchEvaluator` operates as follows:

1. Extract token name (group 1) and optional format (group 2).
2. Switch on token name (**case-sensitive**, per HLPS-008 §4.5 which defines exact PascalCase tokens):
   - `HomeTeam` → return `match.HomeTeam` (empty string if empty)
   - `AwayTeam` → return `match.AwayTeam` (empty string if empty)
   - `MatchType` → return `match.MatchType` (empty string if empty)
   - `Date` → if no format specifier, return `match.MatchDate.ToString("d")`; if format specifier present, attempt `match.MatchDate.ToString(format)` inside a `try/catch (FormatException)` — on failure, log warning and fall back to `match.MatchDate.ToString("d")`
   - Any other token name → return the original match text (e.g. `{Venue}` → `"{Venue}"`, `{homeTeam}` → `"{homeTeam}"`)

3. **Invalid format handling:** All invalid `{Date:format}` values (including time-related specifiers, malformed format strings, and empty format after colon) are caught by the `try/catch (FormatException)` block. No character-level pre-validation is performed — the `DateOnly.ToString(format)` call is the single point of validation. This simplifies maintenance and avoids maintaining an incomplete list of invalid characters.

### 3.3 Truncation

After all substitutions:
- If `result.Length > 100`, set `result = result[..97] + "..."`.

### 3.4 Class Structure

```csharp
namespace PcsRemote.Core;

/// <summary>
/// Renders a YouTube broadcast title from a template string and match data.
/// Implements the token grammar defined in HLPS-008 §4.5.
/// </summary>
public sealed class BroadcastTitleRenderer
{
    private static readonly Regex TokenPattern = new(
        @"\{(\w+)(?::([^}]+))?\}",
        RegexOptions.Compiled);

    private const string DefaultTemplate = "{HomeTeam} vs {AwayTeam}";
    private const int MaxTitleLength = 100;

    private readonly ILogger<BroadcastTitleRenderer> _logger;

    public BroadcastTitleRenderer(ILogger<BroadcastTitleRenderer> logger)
    {
        _logger = logger;
    }

    public string Render(string? template, MatchInfo match)
    {
        // ... implementation per §3.1–3.3
    }
}
```

---

## 4. Acceptance Criteria

| AC | Description | Test Method |
|---|---|---|
| AC-1 | `{HomeTeam}` token substituted with `match.HomeTeam` value | `Render_HomeTeamToken_SubstitutedCorrectly` |
| AC-2 | `{AwayTeam}` token substituted with `match.AwayTeam` value | `Render_AwayTeamToken_SubstitutedCorrectly` |
| AC-3 | `{MatchType}` token substituted with `match.MatchType` value | `Render_MatchTypeToken_SubstitutedCorrectly` |
| AC-4 | `{Date}` token (no format) substituted with short date | `Render_DateTokenNoFormat_UsesShortDate` |
| AC-5 | `{Date:format}` with valid format substituted correctly | `Render_DateTokenWithValidFormat_FormatsCorrectly` |
| AC-6 | `{Date:HHmm}` (time-related specifier) falls back to short date with warning log | `Render_DateTokenWithTimeFormat_FallsBackToShortDate` |
| AC-7 | Unknown token `{Venue}` retained literally in output | `Render_UnknownToken_RetainedLiterally` |
| AC-8 | Empty `HomeTeam` field (`""`) substituted as empty string | `Render_EmptyMatchField_SubstitutedAsEmpty` |
| AC-9 | Title exceeding 100 chars truncated to 97 + `"..."` | `Render_LongTitle_TruncatedWithEllipsis` |
| AC-10 | Title of exactly 100 chars is NOT truncated | `Render_ExactlyMaxLength_NotTruncated` |
| AC-11 | Empty template uses default `"{HomeTeam} vs {AwayTeam}"` | `Render_EmptyTemplate_UsesDefault` |
| AC-12 | Null template uses default `"{HomeTeam} vs {AwayTeam}"` | `Render_NullTemplate_UsesDefault` |
| AC-12a | Whitespace-only template uses default `"{HomeTeam} vs {AwayTeam}"` | `Render_WhitespaceTemplate_UsesDefault` |
| AC-13 | Multiple tokens in one template all substituted | `Render_MultipleTokens_AllSubstituted` |
| AC-14 | Build succeeds with 0 errors, 0 warnings | (build verification) |
| AC-15 | All existing tests continue to pass | (test suite verification) |

---

## 5. File Inventory

| File | Action | Project |
|---|---|---|
| `src/PcsRemote.Core/BroadcastTitleRenderer.cs` | Create | PcsRemote.Core |
| `src/PcsRemote.Core/PcsRemote.Core.csproj` | Modify (add `Microsoft.Extensions.Logging.Abstractions` PackageReference) | PcsRemote.Core |
| `tests/PcsRemote.Core.Tests/BroadcastTitleRendererTests.cs` | Create | PcsRemote.Core.Tests |

---

## 6. Dependencies

- **S-001** (DELIVERED) — `MatchInfo` record exists in `PcsRemote.Core`.
- **No new NuGet packages required** — `System.Text.RegularExpressions` is in-box for .NET 8.
- **`Microsoft.Extensions.Logging.Abstractions`** — **new** PackageReference to be added to `PcsRemote.Core`. This is a first-party Microsoft abstraction package (not a project reference), compatible with Core's zero-project-dependency rule. Required for `ILogger<BroadcastTitleRenderer>` constructor parameter.

---

## 7. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Regex performance on very long templates | LOW | Templates are short config strings; compiled regex is sufficient |
| Culture-sensitive date formatting | LOW | `DateOnly.ToString("d")` uses `CultureInfo.CurrentCulture`; acceptable for single-machine deployment |
| `MatchInfo` field nullability — record has defaults not nulls | LOW | `MatchInfo` fields are non-nullable with sentinel defaults; renderer tests use empty string not null. Defensive null-coalescing in code is not unit-tested but provides safety if NRT is bypassed |
| Unicode truncation — `string.Length` vs grapheme clusters | LOW | YouTube API counts characters, not graphemes; `string.Length` matches API behaviour for practical English titles |

---

## 8. Traceability

| HLPS Requirement | Coverage |
|---|---|
| S-YT-2 (broadcast title from template) | AC-1 through AC-5, AC-13 |
| S-YT-9 (default template) | AC-11, AC-12 |
| §4.5 unknown tokens | AC-7 |
| §4.5 null/empty field | AC-8 |
| §4.5 invalid date format | AC-6 |
| §4.5 100-char truncation | AC-9, AC-10 |

---

## 9. Review History

### R1 — Opus 4.6 + GPT-5.4 (2026-04-17)

| # | Source | Severity | Title | Disposition |
|---|---|---|---|---|
| 1 | Both | CRITICAL→HIGH | IS/HLPS contradiction on time-format handling undocumented | Accept — added IS Deviation note in §2 |
| 2 | Both | HIGH | AC-8 untestable — `MatchInfo.HomeTeam` is non-nullable | Accept — changed AC-8 to empty string test |
| 3 | Opus | HIGH | `Microsoft.Extensions.Logging.Abstractions` not in Core csproj | Accept — corrected §6, added csproj to §5 file inventory |
| 4 | Both | MEDIUM→HIGH | Case-insensitive token matching is undocumented extension | Accept — made case-sensitive per HLPS grammar |
| 5 | Both | MEDIUM | Time-specifier guard character list incomplete | Accept — simplified to try/catch only, removed character guard |
| 6 | Opus | LOW | Whitespace-only template behavior undefined | Accept — added AC-12a, use `IsNullOrWhiteSpace` |
| 7 | GPT | MEDIUM | Build AC "0 warnings" exceeds IS requirement | Dismiss — 0 warnings is project-wide standard per copilot-instructions |
| 8 | Both | LOW | File inventory missing csproj entries | Accept — merged into fix #3 |
| 9 | GPT | LOW | "Zero external dependencies" wording inaccurate | Accept — reworded to "no project-layer dependencies" |
