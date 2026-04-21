# SPEC-S-006 — Club Logo & Favicon

**Status:** DRAFT  
**Parent:** IS-013 S-006  
**HLPS traceability:** SC-8, C-5, A-4, A-8

---

## §1 Objective

Copy the HHCC logo SVG into the Web UI static assets, render it in the header alongside the application title using pure Blazor markup (no JavaScript), and add rasterised favicon variants so the icon appears in browser tabs and bookmarks.

---

## §2 Requirements

| ID | Requirement |
|----|-------------|
| R-1 | Copy `HHCC logo.svg` from `resources/` to `src/PcsRemote.Web/wwwroot/images/hhcc-logo.svg`. |
| R-2 | Render the logo as an `<img>` element in `MainLayout.razor` header, to the left of the "PCS Remote" title. Size: 40px height, auto width. |
| R-3 | Generate rasterised favicon PNGs from the SVG at 16×16, 32×32, and 180×180 sizes using a one-time Node.js script (sharp). Commit the PNGs as static assets in `wwwroot/favicon/`. |
| R-4 | Add favicon link tags to `_Host.cshtml` `<head>`: `<link rel="icon" type="image/png" href="/favicon/favicon-16x16.png" sizes="16x16">`, `<link rel="icon" type="image/png" href="/favicon/favicon-32x32.png" sizes="32x32">`, `<link rel="apple-touch-icon" href="/favicon/apple-touch-icon.png" sizes="180x180">`. |
| R-5 | No JavaScript added to the runtime application (C-5). The Node.js script is a build-time asset generation tool only. |

---

## §3 Test Cases

| ID | Scope | Description |
|----|-------|-------------|
| TC-1 | Web | bUnit render test: `MainLayout` renders an `<img>` element with `src` containing `hhcc-logo.svg` |
| TC-2 | Build | `_Host.cshtml` file content contains `<link rel="icon"` and `<link rel="apple-touch-icon"` tags (file content assertion) |
| TC-3 | Build | Build succeeds with 0 errors, 0 warnings |

---

## §4 Files Changed

| File | Change |
|------|--------|
| `src/PcsRemote.Web/wwwroot/images/hhcc-logo.svg` | NEW — copied from resources |
| `src/PcsRemote.Web/wwwroot/favicon/favicon-16x16.png` | NEW — generated |
| `src/PcsRemote.Web/wwwroot/favicon/favicon-32x32.png` | NEW — generated |
| `src/PcsRemote.Web/wwwroot/favicon/apple-touch-icon.png` | NEW — 180×180 generated |
| `src/PcsRemote.Web/Shared/MainLayout.razor` | Add logo `<img>` to header |
| `src/PcsRemote.Web/Pages/_Host.cshtml` | Add favicon link tags |
| `src/PcsRemote.Web/wwwroot/css/app.css` | Add logo sizing styles |
| `tests/PcsRemote.Web.Tests/` | TC-1 |

---

## §5 Review History

*(populated by adversarial review panel)*
