---
name: svg-design
description: Design, simplify, edit, preview, and validate SVG assets for product UI, desktop apps, documentation, skill icons, plugin icons, empty states, diagrams, or other repo-native vector graphics. Use when Codex needs to create a new SVG, refine an existing SVG, adapt a logo into a small icon, reduce visual clutter or overlap, preserve brand recognition at small sizes, or build a preview-and-screenshot workflow for SVG QA.
---

# SVG Asset Design

## Overview

Use this skill for repo-native SVG work where the final asset should remain editable vector code. Favor simple shapes, clear silhouettes, and screenshot-based validation at the sizes where the SVG will actually render.

## Workflow

1. Inspect the target context before editing: UI surface, surrounding icons, expected size, background color, and any source logo or brand mark.
2. Find existing local SVG or icon components first. Reuse their geometry, stroke style, radius, and color vocabulary when possible.
3. Simplify before decorating. Keep one primary symbol, one supporting shape, and at most one accent color at small sizes.
4. Edit SVG files directly with `apply_patch`. Keep SVGs deterministic and readable: explicit viewBox, no editor metadata, no embedded raster images unless requested.
5. Preview the SVG at real sizes. Use `scripts/preview_svgs.mjs` or an equivalent HTML preview in `references/`.
6. Screenshot the preview and inspect it visually. Iterate if any shape overlaps, detail disappears, color vibrates, or the meaning is unclear at 16/20/32px.
7. Run focused tests only when the SVG is surfaced by tested UI behavior. Pure asset iteration normally needs visual validation more than unit tests.

## Design Rules

- Start from a simplified silhouette. For app-logo adaptations, remove tiny secondary parts before adding color.
- Avoid multiple outlines that touch or nearly touch at small sizes.
- Avoid decorative dots, micro-gradients, tiny labels, and nested icons inside icons.
- Prefer stroke widths that survive downscaling: about 1.8-2.5 in 24px viewBox, 2.5-4 in 48px/64px viewBox.
- Use rounded caps and joins for product UI icons unless the local style is sharper.
- Keep color count low. A reliable pattern is dark base, light main stroke/fill, one brand accent, and one small highlight.
- Check both dark and light backgrounds when the asset may appear in docs or settings surfaces.
- If text or symbols are needed, use geometric paths, not font text.

## Preview Script

Use the bundled preview script for quick visual QA:

```powershell
node "path\to\svg-design\scripts\preview_svgs.mjs" `
  --out references/svg-preview `
  --title "SVG Preview" `
  --hero-size 720 `
  --sizes 16,20,32,48,64 `
  path\to\icon.svg path\to\other.svg
```

The script writes `preview.html`, a combined `preview.png`, and one large `preview-N-name.png` screenshot per SVG in the output directory. Each SVG is shown first in near half-screen dark and light panels for screenshot inspection, then in the requested real-size strip. Increase `--hero-size` when an agent needs an even larger crop for visual review. If `playwright` is not available from the current project, run from a workspace that has Playwright installed or use the same HTML page with another browser screenshot tool.

## Checklist

Read `references/svg_design_checklist.md` when doing a larger icon set, adapting a brand logo, or reviewing someone else's SVGs.
