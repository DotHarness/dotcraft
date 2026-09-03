---
name: visualize
description: Create visualizations and interactive tools in conversation. Use when asked to show or demonstrate visualization options, show how something works, or create simulators or labs, maps, plots, charts or graphs, comparisons, scenarios, adjustable inputs, and exploration.
---

# Visualize

- Create a visual when it materially improves the explanation or when the user explicitly asks for a visual or demonstration.
- When the user asks to demonstrate visualization capabilities and inline support is available, create an actual inline HTML visualization rather than listing available formats.
- Use Mermaid for static structures fully explained by labeled nodes and edges.
- Use an inline HTML visualization for dynamics, adjustable inputs, spatial motion, plots, maps, and interactive exploration only when the system context provides a DotCraft visualization directory.
- If inline visualization support is unavailable, fall back to Mermaid, a Markdown table, an ASCII diagram, or concise prose. Never emit an unusable directive.

## Inline output

1. Choose a new lowercase ASCII hyphenated name ending in `.html`. Never overwrite or edit an earlier visualization.
2. Build a literal HTML fragment only: no doctype, `html`, `head`, or `body` tags; no `fetch`, XHR, WebSocket, local files, or other API calls; keep it below 2 MiB.
3. Give the fragment a unique root id and the `viz-root` class. The root is an unframed conversation surface: do not wrap the whole visualization in `.card`. Scope scripts and content-specific styles to the root, but use the host button and form utilities instead of redefining ordinary product controls. Use semantic controls, visible focus, accessible chart descriptions, and layouts that reflow from 736px to 320px.
4. Use the ordinary `WriteFile` tool with an absolute path inside the visualization directory from system context.
5. Read the file back with `ReadFile` to confirm the write succeeded.
6. After a successful write and read-back, put this exact directive on its own line where the visual belongs:

```text
::dotcraft-inline-vis{file="<title>.html"}
```

Keep necessary explanation outside the fragment. Do not link to or announce the stored file.

## Host contract

- Theme variables: `--background`, `--foreground`, `--card`, `--card-foreground`, `--primary`, `--primary-foreground`, `--secondary`, `--secondary-foreground`, `--muted`, `--muted-foreground`, `--accent`, `--accent-foreground`, `--destructive`, `--border`, `--input`, `--ring`, and `--viz-series-1` through `--viz-series-6`.
- Utilities: `.viz-root`, `.card`, `.viz-grid`, `.viz-row`, `.viz-controls`, `.viz-stat`, `.viz-stat-value`, `.viz-badge`, `.btn`, `.btn-primary`, `.btn-ghost`, `.form-label`, `.form-control`, `.form-select`, `.form-range`, `.form-check`, `.form-switch`, `.table`, `.table-responsive`, `.text-small`, `.text-muted`, `.text-center`, `.text-end`, `.sr-only`, and `[data-tooltip]`.
- Prefer inline SVG for simple charts. Pair color with text or shape and label axes and units.
- Static CDN resources may come only from `cdnjs.cloudflare.com`, `esm.sh`, `cdn.jsdelivr.net`, `unpkg.com`, `fonts.googleapis.com`, `fonts.gstatic.com`, or `fonts.bunny.net`.
- Lucide is available as `globalThis.lucide`. Initial static icons are initialized by the host. Initialize dynamically added icons with `lucide.createIcons({ attrs: { width: 16, height: 16 } })`.
- A drill-down action may call `await window.openai.sendFollowUpMessage({ prompt, title })`. Keep `title` at most 250 characters. The trusted DotCraft host asks the user to confirm before sending.
