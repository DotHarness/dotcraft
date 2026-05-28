# SVG Design Checklist

Use this checklist after the first preview screenshot.

## Shape

- The silhouette is recognizable without relying on color.
- The central symbol remains clear at the smallest target size.
- No two strokes or filled shapes accidentally merge.
- Negative space is intentional and not too narrow.

## Color

- The icon works on the expected background.
- Accent colors guide attention instead of outlining everything.
- Adjacent colors have enough contrast at small size.

## Technical

- SVG has a `viewBox` and explicit width/height when the host expects them.
- SVG does not contain editor metadata, comments about drafts, or unused definitions.
- Paths are deterministic and local; no remote references.
- If the file will be loaded by an app, paths in metadata stay inside the asset directory.

## Validation

- Preview at the exact UI size, plus one larger inspection size.
- Screenshot before final answer.
- Compare with nearby icons in the same surface.
- Iterate once more if the first explanation of the icon requires too many words.
