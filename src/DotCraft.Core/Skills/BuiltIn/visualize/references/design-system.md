# Runtime styles, layout, and controls

### Color

- In each color pair, the base token is a surface and its `-foreground` token is the content on that surface. `--muted` is a surface fill; use `--muted-foreground` for secondary text.
- Make every fill, stroke, text, border, shadow, chart, and canvas color theme-aware. Never hardcode light or dark palettes such as white panels, off-white backgrounds, black text, slate strokes, or Tailwind color literals.
- Keep text readable against its actual background. Muted or secondary colors must retain clear contrast; never use `.text-muted` inside `.card` or another filled container unless its background preserves that contrast.
- Available theme variables are `--background`, `--foreground`, `--card`, `--card-foreground`, `--popover`, `--popover-foreground`, `--primary`, `--primary-foreground`, `--secondary`, `--secondary-foreground`, `--muted`, `--muted-foreground`, `--accent`, `--accent-foreground`, `--destructive`, `--border`, `--input`, `--ring`, `--viz-series-1` through `--viz-series-6`, and the named hues `--blue`, `--orange`, `--green`, `--red`, `--purple`, and `--yellow`. Use `currentColor` inside SVG.
- `--popover` and `--popover-foreground` are the tooltip pair: an inverted surface that stands apart from the visualization. Use them only for transient annotations such as a chart tooltip.
- Named hues follow the host theme. Use them for status or category identity in marks, swatches, and soft tinted fills, never for text or controls.
- The document follows the host appearance, so `light-dark(<light>, <dark>)` resolves to the active theme.
- Never add decorative borders, outlines, or strokes to progress tracks, meters, bars, stacked segments, or other filled quantitative marks. Use a subtle neutral or translucent track and distinguish marks with fill, contrast, spacing, or opacity.
- Use `--viz-series-1` for one measure or active state. Use `--viz-series-2` through `--viz-series-6` only for important persistent category, series, or status identity; never give every peer a different color by default.
  - For categorical tiles or nodes, prefer a soft low-opacity series fill with a neutral or transparent border; never color every outline.
  - Keep mappings stable and pair color with labels, shapes, or line styles.
  - Secondary series colors are theme-derived; never assume hues or use them decoratively.
- When color encodes a category or series, apply it consistently to the corresponding visual marks, not just the legend, and keep large-area fills subtle.
- Use series colors only for chart lines, marks, and legend swatches. Keep values, axis text, and direct labels in `--foreground` or `--muted-foreground`.
- Keep chart grids and inactive structure thin and neutral. Use 1-2px neutral structural paths; never thicken, dash, or double-stroke the whole structure.
- Use `.btn-primary` for high-emphasis actions; its neutral fill is supplied by the utility. Buttons with `aria-pressed="true"`, `.active`, or `data-state="active"` already use the selected style; `.nav-pills .nav-link.active` and a selected `.viz-tile` keep selection neutral. Reserve `--accent` and `--accent-foreground` for subtle interactive surfaces and soft highlights.

### Typography

- Scale type with `--font-size-base`, which follows the host's conversation text size. Use normal text by default and `.text-small` only for secondary annotations; at the default scale these are 14px and 12px. Never make supporting text smaller than 11px.
- `h1`, `h2`, and `h3` are styled by the host. Use one concise visible heading (`h2` or `h3`) for a self-contained chart or graph, with short panel headings only when needed. Do not restate the prompt or add a redundant title to other visualizations.
- Use only weights `400` and `500` in your own styles. Never set custom font sizes or line heights.
- Use `.tabular-nums` on changing or aligned numbers. Avoid it for editorial or decorative numerals.

## Layout and accessibility

- Use semantic HTML, keyboard-accessible controls, and concise labels.
- Use `aria-live="polite"` for dynamic results, selections, and simulator updates. Use `role="alert"` for validation errors. Do not announce every hover or animation frame.
- Keep the top-level surface transparent and unframed, and fill the available conversation width. Design for 736px, or 1,024px in wide mode, and support widths down to 320px. Stack side-by-side content when it no longer fits.
- Host controls sit outside the view, so never reserve space for them.
- At every supported width, text, controls, cards, toolbars, and dynamic content must fit without overlap or clipping. Reflow by stacking or wrapping; use `.table-responsive` only when table columns cannot fit. The host sizes the frame to its content, so avoid fixed outer widths, other horizontal overflow, internal scrolling, `position: fixed`, and viewport-height layouts.
- Size every SVG from its actual container. At narrow widths, reduce ticks, declutter annotations, and keep visible text at least 11 screen pixels; never shrink a fixed-width `viewBox`.
- Keep native tab order; never add `tabindex` except `tabindex="0"` on an SVG mark that carries `data-tooltip`.
- Use native `button`, `input`, `select`, and `textarea` elements with matching utilities; never recreate controls.
- Keep the host focus styles; never override them.
- On coarse pointers, provide non-overlapping effective targets about 44px by 44px without breaking 320px layouts; visible icons and marks may stay small. Keep fine-pointer controls compact.
- Keep essential content and actions available without hover.
- The host honors reduced motion; never loop motion.

## Design system

- Let utilities own geometry, appearance, and interaction. Use the matching utility for every button and form control. Never restyle utilities, descendants, or pseudo-elements: no custom sizes, spacing, borders, radii, shadows, colors, or interaction states. Native `button` elements already use the `.btn` appearance.

### Surfaces and layout

- `.card`: The only card-like HTML surface. Use its base class unchanged for a necessary numeric summary, selected-item summary, or bounded interactive field. Before adding a fill, border, radius, or shadow to any layout container, either use `.card` or leave it transparent and unframed; never recreate card chrome on rows, panels, tiles, sections, or wrappers. Keep charts, maps, diagrams, tables, controls, and the whole visualization unframed. Never nest cards; show 2-4 summaries near the top only when useful. Structural groupings and repeated content are not bounded interactive fields. Organize them with layout or visual marks, not container chrome.
- `.viz-stat`: Use a summary `.card` with one muted label, one `.viz-stat-value`, and at most one short context or delta line.
- `.viz-grid`: Use for peer metrics or choices instead of a custom grid. It creates as many equal-width columns as fit and stacks when narrow. Never use it for the whole visual or a horizontally scrolling card row. Keep groups to 2-3 columns at 736px and controls in a separate row.
- `.viz-row`: Use as a wrapping horizontal group with centered related values or inline actions that may wrap when narrow.
- `<hr>`: Use a native horizontal rule for a subtle theme-aware separator.
- `.nav.nav-pills` + `.nav-link`: Use the accessible, interactive [Tabs](#tabs) API below.
- `.progress` + `.progress-bar`: `<div class="progress" role="progressbar" aria-label="Progress" aria-valuenow="25" aria-valuemin="0" aria-valuemax="100"><div class="progress-bar" style="width:25%"></div></div>`
- `.viz-tile`: Add to a selectable dense-grid `.btn` inside `.viz-grid`. It fills its grid cell, keeps a root-scoped category fill, and marks selection (`aria-pressed="true"` or `.active`) with an accent ring instead of a solid fill. Never add another selected, pressed, border, outline, or shadow rule.
- `.viz-badge`: Use as a compact display-only accent pill for a short status, category, or value; never as a button.
- `.viz-controls`: Use as a wrapping row for controls affecting the same visualization. Keep button groups compact. Put labeled fields directly inside as `.form-label`; fields stack when narrow.

### Tabs

- `.nav.nav-pills[role="tablist"]`: Group content-width native `.nav-link[role="tab"]` buttons and label the group with `aria-label`. Add `.nav-justified` only when tabs should share and fill the row equally.
- `.nav-link[role="tab"]`: Give each button a unique `id`, `type="button"`, `aria-controls`, and `aria-selected`. Mark the initial tab `.active` and `aria-selected="true"`; use `disabled` or `aria-disabled="true"` when needed.
- `[role="tabpanel"]`: Match `id` to its tab's `aria-controls`, set `aria-labelledby` to the tab's `id`, and mark inactive panels `hidden`. Tabs can have separate panels or point to one shared panel; listen for `click` on the tabs to refill a shared panel.
- The runtime already implements selection, panel visibility, and arrow, Home, and End keys; do not wire them yourself.

```html
<div class="nav nav-pills" role="tablist" aria-label="Platform">
  <button class="nav-link active" id="mac" role="tab" aria-controls="mac-panel" aria-selected="true" type="button">macOS</button>
  <button class="nav-link" id="linux" role="tab" aria-controls="linux-panel" aria-selected="false" type="button">Linux</button>
</div>
<div id="mac-panel" role="tabpanel" aria-labelledby="mac">macOS content</div>
<div id="linux-panel" role="tabpanel" aria-labelledby="linux" hidden>Linux content</div>
```

### Controls

- Use `.cursor-interaction` on custom interactive elements, including locally styled controls. It follows the host cursor preference; never hardcode `cursor: pointer`. Shared controls already apply it. Preserve text, drag, and disabled cursors for those states.
- Place related inputs and buttons on one row, aligned at their vertical centers. Put labels and values on a separate row above them.
- `.btn`: Use for a content-sized secondary action. Add `.btn-primary` for one main action per control group or `.btn-ghost` for low emphasis.
- `.btn-block`: Add to a `.btn` only when the action should intentionally fill the available inline space. Never use it for ordinary row actions.
- `<a>`: Use for links. Add `.btn` to style a link as a button. An `http` or `https` link opens in the user's browser after they confirm; the visualization itself never navigates, and other link targets do nothing.
- `<a download="name.ext">` with a `blob:` or `data:` `href` lets the user save generated content through a save dialog, up to 2 MB. Clicking a detached anchor works too, and revoking its object URL right after `click()` is safe.
- `[data-tooltip]`: Use for concise supplementary plain text on static or dynamic HTML triggers and SVG marks; the runtime shows it on hover, keyboard focus, and tap. Keep essential content visible and triggers labeled. Never use `title`, custom markup, or initialization. Example: `<button type="button" data-tooltip="Reset view">Reset</button>`.
- `[data-tooltip-placement]`: Optionally prefer `top` (default), `right`, `bottom`, or `left`; the runtime flips and shifts the tooltip to keep it inside the view.
- When a visible label is truncated, put its full text in `data-tooltip` on the existing accessible trigger. The calendar does this automatically.
- `.form-check`: Add to a native checkbox or radio input, and wrap the input and its text in one `<label>` so the whole row is clickable.
- `.form-switch`: Add to a native checkbox input to render it as a switch, wrapped with its text in one `<label>`.
- `.form-control`: Pair a native text, date, file, or color input, or a textarea, with `.form-label`.
- `.form-control-color`: Add to `.form-control` for a compact native color input.
- `.form-select`: Pair a native select with `.form-label`.
- `.form-range`: Pair a native range with a visible label; put its current value and units immediately before it.

### Tables

- `.table`: Use on a semantic table for a quiet, unframed data view with wrapping cells, subtle horizontal dividers, and no vertical gridlines. Use sentence case for headers.
- `.table-responsive`: Wrap a table when its columns cannot fit at narrow widths. It contains horizontal overflow without clipping the visualization.
- `.table-sm`: Add to `.table` when more rows need to fit; it reduces cell padding without shrinking text.
- `.text-end`, `.text-center`, and `.text-nowrap`: Use inside `.table` for numeric/end alignment, centered values, or values that must stay on one line. End-aligned cells already use tabular figures.

### Text

- `.text-small`: Use for the smallest host-scaled secondary chart labels and annotations, never below 11px or for essential content.
- `.text-muted`: Use for secondary units, captions, timestamps, and context, never essential values or labels.
- `.text-destructive`: Use only for error or validation text the user needs to notice or act on.
- `<code>`: Use for inline commands, file names, symbols, or short references; put multiline code in `<pre><code>`.
- `.sr-only`: Use for visually hidden accessible text.

## Icons

- Use the sandbox-provided global `lucide`. Add an icon name with `data-lucide`:

  ```html
  <i data-lucide="search" aria-hidden="true"></i>
  ```

- Never author inline icon SVG or icon paths. Use only Lucide names; the sandbox replaces each placeholder with a host-sized `currentColor` SVG. Reserve authored inline SVG for charts and data marks.
- Mark decorative icons `aria-hidden="true"`. Put action icons inside labeled controls; use a visible label or `aria-label` for icon-only actions.
- The sandbox initializes static icons after the fragment. After adding icons dynamically, call `lucide.createIcons({ attrs: { width: 16, height: 16 } })`.
- Never load Lucide or another icon library from the network.
- Use visibly labeled buttons and inputs for small interactions. Keep all presentation-only interaction local to the fragment and make the first render useful before input changes.
- Use semantic controls, realistic spacing, and restrained chrome for mockups. Never fake product screenshots when inspectable UI is needed.
