# SPEC-S-007 — Custom Tray Icon

**Status:** DRAFT  
**IS ref:** IS-013 S-007  
**HLPS traceability:** SC-9, C-3, A-5, R-4

---

## Overview

Replace the generic `SystemIcons.Application` and `SystemIcons.Warning` with a custom PCS Remote tray icon derived from the provided `PCSPRO icon.ico` reference. The normal icon is used in automated mode; a warm-tinted variant signals manual mode.

## Requirements

| ID | Requirement |
|----|-------------|
| R-1 | Place `pcs-remote-normal.ico` and `pcs-remote-manual.ico` in `src/PcsRemote.TrayHost/Resources/`. Include both via `<EmbeddedResource>` in the csproj. Each ICO must contain 4 layers: 16×16, 24×24, 32×32, and 48×48 (C-3). |
| R-2 | The manual-mode variant applies a warm orange tint overlay to each layer. At 16×16 the two variants must be distinguishable by brightness/warmth difference alone. User sign-off (R-7) is the acceptance gate for visual quality. |
| R-3 | Load both icons at construction time via `typeof(TrayApplicationContext).Assembly.GetManifestResourceStream`. If either resource fails to load, throw `InvalidOperationException` with the missing resource name (fail-fast — no silent fallback). |
| R-4 | Expose an `internal Icon GetIconForMode(bool isManualMode)` method that returns the correct icon instance. `UpdateToggleState` calls this method instead of referencing `SystemIcons`. |
| R-5 | Dispose both loaded `Icon` instances in the existing `Dispose(bool)` method, after the `NotifyIcon` is disposed. |
| R-6 | Build produces 0 errors, 0 warnings (SC-10). |
| R-7 | First draft provided for user review — iterate on visual appearance if needed (R-4 from IS). |

## Source Icon

`resources/PCSPRO icon.ico` — 12 KB, 3 layers: 48×48 (24bpp), 32×32 (24bpp), 16×16 (8bpp).  
A 24×24 layer is generated from the 32×32 source via bicubic downscale. All 4 layers (16, 24, 32, 48) are included in both output ICOs.

## Embedded Resource Convention

Files committed to `src/PcsRemote.TrayHost/Resources/`:
- `pcs-remote-normal.ico`
- `pcs-remote-manual.ico`

Csproj entry:
```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\pcs-remote-normal.ico" />
  <EmbeddedResource Include="Resources\pcs-remote-manual.ico" />
</ItemGroup>
```

Manifest stream names (loaded via `typeof(TrayApplicationContext).Assembly`):
- `PcsRemote.TrayHost.Resources.pcs-remote-normal.ico`
- `PcsRemote.TrayHost.Resources.pcs-remote-manual.ico`

## Test Cases

| ID | Test | Assertion |
|----|------|-----------|
| TC-1 | Normal icon resource loads | `Assembly.GetManifestResourceStream` returns non-null, non-empty stream. |
| TC-2 | Manual icon resource loads | Same as TC-1 for manual variant. |
| TC-3 | Normal icon is valid ICO | Stream can be loaded with `new Icon(stream)` without exception. |
| TC-4 | Manual icon is valid ICO | Same as TC-3 for manual variant. |
| TC-5 | GetIconForMode returns normal | `GetIconForMode(false)` returns `_normalIcon` (via reference equality or non-null check). |
| TC-6 | GetIconForMode returns manual | `GetIconForMode(true)` returns `_manualIcon` (different instance from normal). |
| TC-7 | Build 0 warnings | `dotnet build --warnaserror` succeeds. |

## Out of Scope

- Runtime-configurable icon selection (single-tenant deployment — A-8).
- Application icon (`.exe` icon in Explorer) — that requires `<ApplicationIcon>` in csproj and is a separate concern.
