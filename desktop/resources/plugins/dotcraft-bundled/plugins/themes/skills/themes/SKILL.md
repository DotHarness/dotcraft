---
name: themes
description: Create or edit DotCraft app themes with the Desktop Theme tool. A theme sets app colors, a backdrop behind the app (animated scene, gradient, or flat color), and the tint of the surfaces over it. Use for the look of DotCraft itself, not operating-system wallpaper or ordinary image generation.
---

# Themes

Create a theme, preview it in DotCraft, and let the user apply it. Use `desktop.Theme` on the connected Desktop device. Search for that tool if it is deferred. If it is unavailable, explain that theme creation needs a connected DotCraft Desktop window rather than claiming to apply a theme.

## Create and revise

Use the user's description and the current conversation. Ask only when an unanswered choice materially affects the theme. For an edit, read the current theme, preserve its id, and pass its revision as `expectedRevision` when saving. Create a new id when adapting a bundled theme. `list` returns names and revisions without source. `read` returns a user theme's full content.

Pick the smallest theme that gives the look. Colors alone recolor the whole app. A gradient or flat color gives a still backdrop. Draw a scene only when the request needs artwork or motion.

Save with `action: "save"` and a `theme` object:

- `id`: stable lowercase letters, digits, and hyphens, at most 64 characters. Do not use the reserved `builtin-` prefix.
- `name`, `colorScheme`: a short name and `light` or `dark`. A theme is drawn only in the appearance it is made for.
- `colors`: six-digit hex `surface`, `ink`, `accent`, and numeric `contrast` from 0 to 100. The app derives its whole palette from these. `surface` is the base plane and `ink` the main text. Saving fails when `ink` does not reach 4.5:1 against `surface`. Take the surface from the theme's hue rather than pure black or white, and keep danger red, success green, and warning amber readable beside the accent.
- `backdrop`: one of
  - `{ kind: "color", color }`
  - `{ kind: "gradient", stops, angle, shape }` with 2 to 16 colors, an angle in degrees as in CSS, and `linear` or `radial`
  - `{ kind: "scene", format, source, poster }`, described below
- `tint`: the color of the sidebar, chat, and Composer surfaces over the backdrop. Pick it from the backdrop so the surfaces belong to the scene. Only valid with a backdrop. The user sets how blurred and solid the surfaces are.
- `pair`: the id of the same theme made for the other appearance. Applying one applies both. Make the second a version of the idea, not an inversion, and point each at the other.
- `expectedRevision`: the current revision, required when replacing an existing theme.

A theme needs `colors`, a `backdrop`, or both. Backdrop and tint colors are hex, `rgb()`, or `hsl()`.

Then call `action: "preview", id: "your-theme"`. When this is the foreground authoring conversation, the user sees the theme with the picker open and applies it by selecting it, everywhere or only in this chat. Saving does not apply a theme. Do not claim it was applied until the user applies it. For another iteration, read, revise, save, and preview the same theme.

## Scenes

`format` is `svg` or `html` and `source` is the complete self-contained source, at most 1 MiB. Choose animated SVG for artwork and patterns expressible with SVG and CSS. Choose HTML for Canvas and other effects that need JavaScript. `poster` is a complete static SVG, at most 256 KiB, composed as a finished still frame. It is used for thumbnails, reduced motion, and runtime fallback, and holds no scripts, event handlers, foreign objects, CSS animation, or SVG animation elements.

Compose for a full, resizable window with quiet space where conversation text sits. Use `viewBox` and `preserveAspectRatio="xMidYMid slice"` in SVG. HTML fills the viewport without scrollbars. For Canvas, resize with the viewport, cap device pixel ratio and particle count, and use one animation loop. Backgrounds receive no pointer or keyboard input, so provide no click targets, controls, or explanatory text.

Source runs in a sandbox without the host DOM, preload, tools, conversation data, or storage. Network access, external scripts, fonts, images and stylesheets, nested frames, forms, workers, and navigation are blocked. Embed assets as data URLs or inline markup. The host swaps in the poster for reduced motion, disabled animation, hidden windows, or unavailable GPU acceleration, and restores a fresh scene when animation resumes.

Return a concise description of the theme and point to the preview. Keep the source in the saved theme rather than repeating it in chat.
