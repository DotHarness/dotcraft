---
name: visualize
description: "Create visualizations and interactive tools directly in conversation. Use to show how something works; explore 'what happens when', 'what changes', or 'help me understand'; compare or inspect; create simulations, maps, charts, graphs, and mockups. Use standard tools for static scientific figures."
---

# Visualize

- A request for a new standalone file, website, app page, component, or other project change is not an in-conversation visualization request, even when the deliverable contains charts or interactive content. Build those in the open project.
- A request to preview, explain, or explore a proposed interface in the conversation is an in-conversation visualization request.
- Create a visual only when the user needs to see or explore it in the conversation and it materially improves the explanation. Do not create a visual merely because the request involves data, charts, or an interactive page.
- Use a normal Markdown table when the user asks for a table; return it directly and do not create a visualization file.
- Use Mermaid when labeled nodes and edges fully explain a static structure; return a normal fenced Mermaid block and no visualization file. Use HTML for dynamics, spatial motion, adjustable inputs, and other visuals.
- Create an HTML visualization only when the system context has an `# Visualizations` section with a visualization directory. Otherwise, use Mermaid, a Markdown table, an ASCII diagram, or concise prose, and never emit a content reference.
- Work silently unless blocked or the user explicitly asks for progress. Never send commentary or progress updates while reading this skill or writing or updating the file; the final response must be your first user-facing message.
- In user-facing prose, describe only what the visual helps the user see or decide. Keep it concise and do not repeat information already clear from the visual. Never announce this skill, a visualization surface, widgets, HTML, SVG, scripts, local files, inline data, or implementation details.

## Read the complete skill

Read this file in full before authoring. Reread truncated ranges in smaller calls. Copy into every compaction summary: `Reload the full visualize skill before creating or updating a visualization.`

## HTML output contract

### File

- For each new visualization, choose a concise ASCII lowercase-hyphenated title and write `<title>.html` with `WriteFile`, using an absolute path directly inside the visualization directory from the system context. Never write visualization fragments anywhere else.
- To update an existing visualization, rewrite its file in place with `WriteFile` or `EditFile`. Every reply that shows that path then displays the latest version.
- Read the file back with `ReadFile` after every write.

### Fragment

- Write only an HTML fragment: no `<!doctype>`, `<html>`, `<head>`, or `<body>`.
- Write literal markup: use `<div class="card">Hi</div>` plus a real newline, never `<div class=\"card\">Hi</div>\n`. Never embed the fragment in an inline Python, JavaScript, or shell string. Read it back; rewrite literal `\"` or `\n`.
- Keep CSS and JavaScript in the fragment only when base classes are insufficient. Load static resources only from the CDN allowlist. Never use `fetch`, XHR, WebSocket, or other API calls.
- Give the fragment root a unique ID and select it with `document.getElementById(...)`. Never derive the root from `document.currentScript`; scripts may sit outside the root.
- Keep visualizations under 1 MB. Aggregate, bin, downsample, reduce precision, or drop unused fields from large inline datasets.
- Check that JavaScript has no undefined identifiers, every queried element exists, and the primary interaction updates the visual. The host reports the first uncaught error or unhandled rejection to the user with an action that asks you to fix the file.

### Content and response

- Keep the fragment focused on the visualization. Do not include explanatory paragraphs, formulas, instructions, or narrative callouts. Include only necessary labels, legends, values, and accessible text alternatives.
- Use the normal response flow. Put any necessary concise explanation outside the fragment, and add this visualization content reference on its own line where the visual should appear, using the absolute file path:

```text
visualize{"path":"<absolute-path>/<title>.html"}
```

- The JSON object may also include a `title` of at most 200 characters when the visual needs a more descriptive accessible name: `visualize{"path":"<absolute-path>/<title>.html","title":"Monthly sales by region"}`. It may also include `"mode":"wide"` as described next. Use no other keys.
- Add `"mode":"wide"` for a full-screen desktop app mockup, including its application shell. For other visuals, add it only when several compact chart panels must stay side by side for direct comparison and would be unreadable at the normal width. Never widen a single plot, map, grid, diagram, or timeline merely because it is dense. Keep contained mockups, dialogs, and mobile screens at normal width, and stack separate self-contained views vertically. A wide view breaks out of the text column up to 1,024px when the conversation has room: `visualize{"path":"<absolute-path>/<title>.html","mode":"wide"}`
- Whenever you create or update a visualization, include its content reference only in that same turn's final response (never in commentary or progress updates), even when editing an existing file or reusing a path shown in an earlier turn.
- Emit only the content reference for the fragment. Never announce it as an artifact, website, output, attachment, link, or download, and never add a Markdown link to it. Do not append a Markdown table or repeat the visual's data; add at most one short conclusion when the user needs an explanation.

### External resources

- The CSP allows only `cdnjs.cloudflare.com`, `esm.sh`, `cdn.jsdelivr.net`, `unpkg.com`, `fonts.googleapis.com`, `fonts.gstatic.com`, and `fonts.bunny.net`. Other origins are blocked and fail silently. Network requests such as `fetch` are blocked even for these origins, so load data as script or module imports or inline it.

## Runtime styles

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

## Composition

Choose the smallest composition that fits.

- Prefer interaction detail over permanent panels, toolbars, repeated legends, or long stacks. Add only requested controls, use one mechanism per state, and never invent search, filter, or reset controls.
- Keep filters, selections, and other presentation-only interactions local. For drill-down actions that ask for further investigation or explanation of selected data, call `await window.openai.sendFollowUpMessage({ prompt, title, context })`, where the optional `title` is a concise confirmation-dialog heading of up to 250 characters. The user confirms before the message is sent. Include the selected values and requested investigation in the prompt, and label the action clearly. Pass the optional `context` (JSON, under 32 KiB) only when the follow-up needs structured visualization data beyond the prompt; it is appended to the user's message as visible JSON.
- Show only metrics that explain the requested behavior. Put live values in control headers or on the visual before cards. Treat maxima as ceilings, not targets. Never invent qualitative scores, status cards, or secondary fact grids to fill space.

### Remembering interaction state

- Read saved state from `window.openai.widgetState` when rendering. Listen for `openai:set_globals` and apply `event.detail.globals.widgetState` when present. Use defaults for missing or incompatible state.
- After meaningful interactions, call `window.openai.setWidgetState({ modelContent, privateContent }).catch(() => {})`. Updates are optimistic. Both fields accept JSON or `null` and may be omitted (treated as `null`). Each call replaces the snapshot; keep it under 16 KiB. Never save on load or state events.
- Put choices useful for follow-up questions in `modelContent`, UI details worth restoring in `privateContent`, and transient or recomputable values in memory. Only `modelContent` may reach the model, and delivery is best-effort; never rely on it. Saving never starts a turn. No secrets or images.

### Display mode

- The user can open any view fullscreen and step through the conversation's visualizations there; that copy shows the view up to 1,024px wide. `window.openai.displayMode` is `"inline"` or `"fullscreen"`, and `window.openai.maxHeight` is the host's height cap in pixels for the current mode. Read both when rendering and apply `event.detail.globals.displayMode` and `.maxHeight` from `openai:set_globals` when present.
- Content taller than `maxHeight` scrolls inside the view; never size the layout from the viewport height.
- Call `await window.openai.requestDisplayMode({ mode: "fullscreen" })` only from a clearly labeled control the user activates, and only when the view materially benefits from more room. It resolves with `{ mode }`, the mode actually granted; a request made without a user action stays `"inline"`. In fullscreen, `requestDisplayMode({ mode: "inline" })` returns to the conversation.
- Fullscreen uses Escape to close and the left and right arrow keys to page. Text fields keep these keys; call `event.preventDefault()` on them only when the view itself uses them, such as for a step-through.

### UI mockups

- When the user would benefit from comparing a few directions for the same component or screen, show them in a [variant carousel](#mockup-variants-and-design-controls) without waiting to be asked, and bind the settings worth exploring to [design controls](#design-controls). Keep ordinary mockup interactions local. Do not add design controls to charts, explainers, or simulations unless the user asks, and never build your own controls panel.
- "In the widget" means the in-conversation visualization, not a widget inside the depicted product.
- Use product and platform context already available in the conversation; don't search the project to render a mockup. Match the product's chrome, navigation, typography, colors, and content. If its design is unavailable, infer one from the platform and request.
- NEVER use visualization CSS variables or utility classes inside a mockup (for example, `--card`, `--font-size-base`, `.card`, or `.btn`). Define root-scoped, product-specific colors, typography, surfaces, and controls instead, and reset the host's default `button` styles within the mockup. This rule overrides all general visualization guidance.
- Keep only the surrounding conversation surface transparent. Give product windows, cards, menus, and popovers opaque backgrounds, and stack overlays above the product content.
- Follow the host's active appearance with product-specific `light-dark(<light>, <dark>)` colors unless a fixed theme is requested.
- **Contained mockup:** Frame a component, dialog, small feature, or mobile screen as a compact product surface. Add `.viz-dotted-background` to the surrounding preview area or the carousel root for a quiet, theme-aware dot grid. This preview-only class is an exception to the mockup utility rule; keep it outside the product UI and leave full-page mockups, charts, and explainers plain.
- **Full-page mockup:** Render a desktop window, application shell, or page at full width without an additional visualization card.
- Put app-wide navigation and pickers in the app chrome, and local controls in their component. Omit single-option pickers. Show realistic states, not invented dashboards, filler cards, or oversized icons.

### Mockup variants and design controls

#### Variant carousel

- Show one complete design at a time. Each design must differ in layout or interaction, not only in color. When the user asks to see designs side by side, lay them out side by side instead.
- Use one `.viz-carousel` root with an `aria-label` and one direct child per design. Give every child a unique, short `data-variant` name and an `aria-label`. Leave the first design visible and mark the others `hidden`.
- The host adds previous and next buttons, a picker of the names, and a count below the designs, and switches designs by toggling `hidden`, so each design keeps its DOM and local state. Never write carousel navigation markup, styles, or script.
- Keep the carousel root outside product-scoped styles; it and `.viz-dotted-background` are the only host classes allowed around a mockup. Scope product styles to the elements inside each design, because a rule on an ancestor, such as `#app button`, also restyles the carousel controls. The carousel reserves space below for its controls. Give every design the same responsive stage height, sized for the tallest one, so the controls stay put; at narrow widths raise that height or reflow the product content so nothing clips.
- Name designs by what sets them apart (`Compact`, `Editorial`), never with numbers or ordinals such as `01 · Compact`; the host already shows the count. Never start animation or audio, or send a follow-up, when a design becomes visible.
- For another language, set `data-previous-label` and `data-next-label` on the root; the count and picker follow the names. The visible design is local: it is not saved or sent to the model.

```html
<div class="viz-carousel viz-dotted-background" aria-label="Music player designs">
  <section data-variant="Minimal" aria-label="Minimal music player">
    <!-- A complete interactive player with product-specific styles -->
  </section>
  <section data-variant="Editorial" aria-label="Editorial music player" hidden>
    <!-- A distinct layout with its own controls and state -->
  </section>
  <section data-variant="Studio" aria-label="Studio music player" hidden>
    <!-- A third direction -->
  </section>
</div>
```

#### Design controls

The host provides `Tweak`, a helper that binds properties of your own state objects to controls in a host panel the user opens from the view. It does not draw a panel, so never import another controls library, add a controls launcher, or wire host messages yourself.

Render the initial state first, then register controls only when the helper exists, so the same HTML still works where it is unavailable:

```js
const player = document.getElementById("minimal-player");
const state = { radius: 18, accent: "#7c3aed", playing: true, density: "Comfortable" };

function render() {
  player.style.borderRadius = `${state.radius}px`;
  player.style.setProperty("--player-accent", state.accent);
  player.dataset.density = state.density.toLowerCase();
  player.querySelector(".play").textContent = state.playing ? "Pause" : "Play";
}
render();

if (globalThis.Tweak) {
  const tweak = new Tweak({ container: player, onChange: render });
  tweak.addSlider(state, "radius", { label: "Corner radius", min: 0, max: 40, unit: "px", reference: "player.radius" });
  tweak.addColorPicker(state, "accent", { label: "Accent", reference: "--player-accent" });
  tweak.addToggle(state, "playing", { label: "Playing" });
  tweak.addSelect(state, "density", { label: "Density", options: ["Compact", "Comfortable"] });
}
```

- `new Tweak({ container, onChange })` creates one group, headed by the container's `aria-label`. Use one instance per independently editable component and give each container a descriptive `aria-label`.
- `addSlider(object, property, { min, max, step = 1, unit?, label?, reference? })` binds a number; `unit` is only shown beside the value. `addColorPicker(object, property, { label?, reference? })` binds a hex color string. `addToggle(object, property, { label?, reference? })` binds a boolean. `addSelect(object, property, { options, label?, reference? })` binds a string; options are strings or `{ label, value }` objects. Each method returns the instance.
- The helper assigns a new value to the bound property and then calls `onChange`, including when the user resets or shows the original. Make `onChange` a deterministic local render that updates the registered element in place; never replace that element, write to the network, or take an irreversible action. The property's value at registration is its original.
- Use at most 12 controls per component, 12 options per select, and 16 components per view; extra controls are ignored. A `reference` hint names the token or state property a control changes, such as `--player-accent` or `player.radius`, using only letters, digits, and `_ - . / : @ $ #`.
- Inside a carousel, bind controls to components inside each design. The panel shows the controls of the visible design and keeps edits when the user switches. Controls bound outside the designs are always shown; use them for shared settings and apply them to every design they affect. Never add a control that switches designs.
- The user can add their edits to their next message. You then receive the visualization file, the visible design, and each changed control as `group · label (reference): from → to`; apply them to the file rather than asking the user to repeat them.
- `tweak.supported` is false when the host cannot show the controls; the helper is then inert. Cleanup is automatic; call `tweak.dispose()` only when you remove the component.

### Calendar

- Use the built-in `<viz-calendar>` for a compact day schedule; use custom HTML for any other calendar view. Supply verified dates and events; the element handles durations, overlap lanes, and responsive layout. Short blocks show only the title; hover, focus, or tap reveals the full title, time, and detail in the shared tooltip. Events that ended before `now` are drawn quieter. It draws its own card, so never wrap it in `.card` or add an inset, and do not stretch the schedule to fit secondary text.
- Attributes: `date` (required, `YYYY-MM-DD`), `start` and `end` (visible window as `HH:MM`, default `09:00`-`17:00`), `now` (current-time marker; only when verified), `time-zone` (short label; does not convert times), `lang` (date locale; defaults to the host language), `empty-label` (defaults to "No events"), and the boolean `interactive`.
- Each event needs `title`, `start`, and `end` as local wall-clock `HH:MM` (`24:00` is allowed as an end). Optional fields are `tone` (`blue`, `green`, `red`, `orange`, `purple`, or `yellow`; default `blue`), `detail` (short secondary text), and `video` (`true` shows a video-call icon). Keep a small, consistent tone mapping per source calendar or category. Convert source timestamps to the requested date and zone first. Supply at most 200 events; events outside the window are clipped or omitted. Invalid data shows an inline error until valid data arrives.
- Use the three hours around the next event for a brief check-in, or the requested range for a full day.
- For a fixed snapshot, put a JSON array in the `events` attribute, escaping apostrophes as `&#39;` inside single quotes. From JavaScript, configure the element before appending it and assign an array to `calendar.events`; assigning a new array redraws, while changing the array in place does not. The property overrides the attribute until it is set to `undefined`. Use the literal `viz-calendar` name; no import or initialization is needed.
- Add `interactive` only when the fragment handles selection. Listen for `eventselect`; `event.detail.event` is the supplied object and `event.detail.index` its position in the input array.
- The element shows one day. Add [Tabs](#tabs) outside it only when two or more supplied days exist, and assign the chosen day's `date` and `events` to the same element on selection.

```html
<viz-calendar date="2026-07-14" start="11:00" end="14:00" now="11:50"
  events='[{"title":"Standup","start":"12:00","end":"12:30"},{"title":"Design sync","start":"13:00","end":"14:00","detail":"Room 4","video":true}]'>
</viz-calendar>
```

### Interactive explainer or simulation

- Use compact controls or status, one compact dominant visual, and at most one single-line selected-state detail. Default to no summary cards; allow up to three only when changing metrics are central.
- Crop empty space and fit the available inline width. For step-throughs, add only requested step controls and update one current visual; never add parameter controls, formulas, metric cards, or side-by-side steps unless asked.

### Graphs and plots

- Use D3 for data-rich Cartesian or statistical plots and handwritten SVG for simple, directly labeled values. Keep diagrams, simulations, and maps under their own guidance. Load the version-pinned approved-CDN script `https://cdn.jsdelivr.net/npm/d3@7.9.0/dist/d3.min.js`.
- Render the figure, legend, and subplots directly on the transparent host surface. Frame only the SVG plot area; never wrap charts in `.card`, rounded panels, filled backgrounds, or shadowed containers.
- Give the figure a concise visible title. Render each Cartesian subplot in its own responsive SVG with a matching `viewBox`, a thin frame, and visible `text.axis-title[data-axis="x"]` and `text.axis-title[data-axis="y"]` showing quantities and units.
- Set each SVG `viewBox` from its own container's measured width, redraw with `ResizeObserver`, and reserve at least 64px for the y axis. Never scale down a fixed-width `viewBox`.
- Derive padded domains with `d3.extent(...)` over all observations, uncertainty, and references. Inset scale ranges for marker radii and keep every path inside `rect[data-chart-frame]`; never draw endpoint connectors outside the frame or guess or hard-code the domain.
- After every draw, measure tick, axis, and value-label bounds together. Leave 4px between labels, anchor edge labels inward, and remove optional annotations first. At 360px, show at most four x ticks and stack panels.
- Prefer `--viz-series-1` through `--viz-series-6` for chart series; use `--foreground` and `--border` for neutrals, cycle the six series tokens when more are needed, and never use literal or fallback colors. Give every SVG label `fill: var(--foreground)` and `font-size: 12px`; never shrink labels below 11 screen pixels. Stack subplots when their labels no longer fit.
- Keep observations, trends, and important values visible. Use bands for dense uncertainty, whiskers for isolated estimates, and one compact, wrapping legend. Render one real `<button type="button" aria-pressed="true">` per series with a small swatch and neutral text; toggle its line, markers, and tooltip row together. Give these legend buttons a root-scoped class that removes the host button border, background, minimum height, and pressed fill, so they stay transparent and indistinguishable from inline text; never use `.btn`, pills, badges, rounded borders, or filled and selected backgrounds.
- For a single mark, put `data-tooltip` on the mark itself (see Charts). For cross-series hover, share one root-relative, pointer-transparent `<div class="tooltip" role="tooltip">`; the `.tooltip` class supplies the `--popover` surface, so position it but do not restyle it. In each multi-series SVG, give the full-plot overlay both `data-chart-hit` and `data-chart-hover-overlay="cross-series"`. Keep the `data-chart-hover-guide` at the exact cursor x, interpolate every visible series there, and show one aligned `data-chart-hover-marker` and tooltip row per visible series; never snap the guide to a nearby sample. Let touch users pin the same cross-series details without requiring hover.
- Find ordered observations with `d3.bisector(d => d.x).center(values, x)`; never pass an accessor to `d3.bisectCenter`.
- Give isolated marks transparent `data-chart-hit` targets at least 32 screen pixels across on fine pointers and about 44px on coarse pointers; use one nearest-point overlay for dense scatter.
- For named numeric data and one-off analyses, start with the plot. Put values and takeaways on its marks, axes, or annotations. Never add a KPI row, controls, cards, or panels unless those UI elements are explicitly requested.
- For sequences or parallel work, use aligned lanes on one time axis. Encode phase and resource in the marks; annotate totals, waits, and bottlenecks on the axis or lanes, not above the plot.
- For distributions or multi-metric comparisons, use shared-scale facets or small multiples. Render every requested dimension simultaneously; never hide one behind a toggle.

### Maps

- Let the map dominate the composition. Use at most one compact selection/detail area and only requested controls.
- Always project published GeoJSON/TopoJSON and sourced longitude/latitude with `d3-geo`; never hard-code or hand-draw geographic outlines. Use schematic maps only when asked.
- For world countries, import `https://esm.sh/@d3-maps/atlas@1.0.0/world/countries/countries-110m` and convert it with `topojson-client@3.1.0` using `feature(world, world.objects.features).features`. Join input ISO3 directly to `feature.properties.id`, which is already ISO3; do not convert it to numbers.
- For US states or counties, import `https://cdn.jsdelivr.net/npm/us-atlas@3/counties-10m.json/+esm`. For ZIP/ZCTA or city boundaries, obtain official Census or local open-data GeoJSON and inline it; do not guess sibling atlas paths or import raw JSON as JavaScript.
- Keep maps geographically legible: for local points, use published neighborhood, street, or comparable geometry; a blank field or lone administrative outline is not a basemap. Show the full city or region behind points or partial choropleths, and frame the locations with modest padding.
- Include the verified geometry in the final HTML. Before replying, check for blank basemaps, failed imports, missing labels, and unprojected points.

### Dense categorical grid

- Use one compact horizontal selected-item summary, then a grid with exactly one readable identifier per cell, then one small legend. Render only that identifier as visible cell text; put all other metadata in an accessible label or one summary line, not badges or fact grids. Allow only selection unless asked.

### Part-to-whole or time allocation

- Use compact metrics and one stacked chart of category allocation per period. Never substitute totals-only bars or duplicate it as a heatmap and totals chart.

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

## Charts

- Prefer inline SVG for simple charts and version-pinned approved-CDN libraries when native interaction, scales, legends, or layout materially improve the result.
- Resolve theme colors before passing them to canvas or chart APIs that cannot parse CSS variables or `light-dark(...)`; redraw when the theme changes.
- Use a tooltip unless it would distract from a simple, directly labeled chart. Keep chart-library tooltips and grouped legend interactions native; never replace them with a custom one-point tooltip. For SVG, attach `data-tooltip` directly to the real pointer-accessible mark with its label, value, and units, and give the mark `tabindex="0"`; the runtime handles themed positioning, keyboard focus, and touch.
- Animate transitions between chart states so lines and marks move to their new values, resampling paths when point counts differ. Do not animate initial appearance or use fade-only effects.
- Scope SVG styles to the chart class. Never target every `svg` in a container that also contains Lucide icons.
- Include labeled axes, units, and directly labeled important values. Give every chart, SVG, canvas, and widget a concise screen-reader summary using a role and accessible name or description, SVG `<title>`/`<desc>`, fallback text, or an `.sr-only` heading or description.
- Reserve space for the longest formatted label at every supported width. Axis ticks are secondary and may use `.text-small` when space is tight. Never overlap or clip text against marks, axes, legends, labels, or edges; move or reduce labels rather than squeeze them.
- Add a legend only when multiple series cannot be labeled directly.
- Pair color with shape or text so meaning never depends on color alone.

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
