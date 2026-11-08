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
- When the system context has a `# Generative UI` section, use the `generative-ui` skill instead for calculators, forms, quizzes, choices, comparisons, and cards. Keep visualizations for dynamics, simulations, maps, custom graphics, and games.
- Create an HTML visualization only when the system context has an `# Visualizations` section with a visualization directory. Otherwise, use Mermaid, a Markdown table, an ASCII diagram, or concise prose, and never emit a content reference.
- Work silently unless blocked or the user explicitly asks for progress. Never send commentary or progress updates while reading this skill or writing or updating the file; the final response must be your first user-facing message.
- In user-facing prose, describe only what the visual helps the user see or decide. Keep it concise and do not repeat information already clear from the visual. Never announce this skill, a visualization surface, widgets, HTML, SVG, scripts, local files, inline data, or implementation details.

## Read the relevant guides

Read this file in full before authoring, then read the guides needed for the visual. Reread truncated ranges in smaller calls. Copy into every compaction summary: `Reload visualize and its selected guides before creating or updating a visualization.`

- Read [Runtime styles, layout, and controls](references/design-system.md) for theme tokens, accessibility, utilities, tabs, tooltips, and icons. Mockup styling overrides the general style rules.
- For a product interface, read [UI mockups](references/mockups.md), including variants and design controls.
- For Cartesian or statistical plots, read [Graphs and plots](references/charts.md).
- For geographic content, read [Maps](references/maps.md), even when the map sits inside another visual.
- For a compact day schedule, read [Calendar](references/calendar.md).
- Before adding saved state, follow-up actions, or fullscreen controls, read [Host interactions](references/interactions.md).

## HTML output contract

### File

- For each new visualization, choose a descriptive `.html` file name, such as `progress_demo.html`, and write it with `WriteFile` directly inside the visualization directory supplied by the host.
- To update an existing visualization, rewrite its file in place with `WriteFile` or `EditFile`. Every reply that shows that path then displays the latest version.
- Read the file back with `ReadFile` after every write.

### Fragment

- Write only an HTML fragment: no `<!doctype>`, `<html>`, `<head>`, or `<body>`.
- Write literal markup: use `<div class="card">Hi</div>` plus a real newline, never `<div class=\"card\">Hi</div>\n`. Never embed the fragment in an inline Python, JavaScript, or shell string. Read it back; rewrite literal `\"` or `\n`.
- Keep CSS and JavaScript in the fragment only when base classes are insufficient. Load static resources only from the CDN allowlist. Never use `fetch`, XHR, WebSocket, or other API calls.
- Give the fragment root a unique ID and select it with `document.getElementById(...)`. Never derive the root from `document.currentScript`; scripts may sit outside the root.
- Keep visualizations under 1 MB. Aggregate, bin, downsample, reduce precision, or drop unused fields from large inline datasets.
- Check that JavaScript has no undefined identifiers, every queried element exists, and the primary interaction updates the visual. Use the optional preview below when it would help check layout or runtime behavior. The host reports the first uncaught error or unhandled rejection to the user with an action that asks you to fix the file.

### Content and response

- Keep the fragment focused on the visualization. Do not include explanatory paragraphs, formulas, instructions, or narrative callouts. Include only necessary labels, legends, values, and accessible text alternatives.
- Use the normal response flow. Put any necessary concise explanation outside the fragment, and add this visualization content reference on its own line where the visual should appear, using just the file name:

```text
visualize{"path":"progress_demo.html"}
```

- The JSON object may also include a `title` of at most 200 characters when the visual needs a more descriptive accessible name: `visualize{"path":"progress_demo.html","title":"Monthly sales by region"}`. It may also include `"mode":"wide"` as described next. Only `path`, `title`, and `mode` affect rendering; omit unrelated fields.
- Add `"mode":"wide"` for a full-screen desktop app mockup, including its application shell. For other visuals, add it only when several compact chart panels must stay side by side for direct comparison and would be unreadable at the normal width. Never widen a single plot, map, grid, diagram, or timeline merely because it is dense. Keep contained mockups, dialogs, and mobile screens at normal width, and stack separate self-contained views vertically. A wide view breaks out of the text column up to 1,024px when the conversation has room: `visualize{"path":"progress_demo.html","mode":"wide"}`
- Whenever you create or update a visualization, include its content reference only in that same turn's final response (never in commentary or progress updates), even when editing an existing file or reusing a path shown in an earlier turn.
- Emit only the content reference for the fragment. Never announce it as an artifact, website, output, attachment, link, or download, and never add a Markdown link to it. Do not append a Markdown table or repeat the visual's data; add at most one short conclusion when the user needs an explanation.

### Minimal fragment

For this example, write `progress_demo.html` in the supplied directory, then use `visualize{"path":"progress_demo.html"}`. Choose the name and content appropriate to the requested visual.

```html
<div id="progress-demo">
  <label class="form-label" for="progress-value">Progress: <output aria-live="polite">50%</output></label>
  <input class="form-range" id="progress-value" type="range" min="0" max="100" value="50">
  <div class="progress" role="progressbar" aria-label="Progress" aria-valuemin="0" aria-valuemax="100" aria-valuenow="50">
    <div class="progress-bar" style="width:50%"></div>
  </div>
</div>
<script>
const root = document.getElementById("progress-demo");
root.querySelector("input").addEventListener("input", event => {
  const value = event.target.value;
  root.querySelector("output").textContent = value + "%";
  root.querySelector('[role="progressbar"]').setAttribute("aria-valuenow", value);
  root.querySelector(".progress-bar").style.width = value + "%";
});
</script>
```

### Optional browser preview

When a preview would help, run `node "<skill-dir>/scripts/render.mjs" "<fragment-path>" "<preview-output.html>"` to create standalone HTML, or add `--serve` to inspect the fragment at a temporary localhost URL. `<skill-dir>` is this file's directory. The fragment remains the editable source; reference it, not the preview document.

The preview uses the visualization runtime in a sandboxed iframe. Use frame-scoped browser locators to inspect its content and controls.

### External resources

- The CSP allows only `cdnjs.cloudflare.com`, `esm.sh`, `cdn.jsdelivr.net`, `unpkg.com`, `fonts.googleapis.com`, `fonts.gstatic.com`, and `fonts.bunny.net`. Other origins are blocked and fail silently. Network requests such as `fetch` are blocked even for these origins, so load data as script or module imports or inline it.

## Composition

Choose the smallest composition that fits.

- Prefer interaction detail over permanent panels, toolbars, repeated legends, or long stacks. Add only requested controls, use one mechanism per state, and never invent search, filter, or reset controls.
- Show only metrics that explain the requested behavior. Put live values in control headers or on the visual before cards. Treat maxima as ceilings, not targets. Never invent qualitative scores, status cards, or secondary fact grids to fill space.

### Interactive explainer or simulation

- Use compact controls or status, one compact dominant visual, and at most one single-line selected-state detail. Default to no summary cards; allow up to three only when changing metrics are central.
- Crop empty space and fit the available inline width. For step-throughs, add only requested step controls and update one current visual; never add parameter controls, formulas, metric cards, or side-by-side steps unless asked.

### Dense categorical grid

- Use one compact horizontal selected-item summary, then a grid with exactly one readable identifier per cell, then one small legend. Render only that identifier as visible cell text; put all other metadata in an accessible label or one summary line, not badges or fact grids. Allow only selection unless asked.

### Part-to-whole or time allocation

- Use compact metrics and one stacked chart of category allocation per period. Never substitute totals-only bars or duplicate it as a heatmap and totals chart.
