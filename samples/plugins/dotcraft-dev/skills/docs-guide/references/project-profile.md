# Project Profile — DotCraft

This is the **only** project-specific file in the skill. `SKILL.md`, `page-templates.md`, and `style-and-mechanics.md` are portable writing guidance and reference this file for anything concrete (framework, file paths, languages, syntax, product terms). To reuse the skill on another project, replace **this file** and leave the rest intact.

## Doc system

- Static site generator: **VitePress**, sources under `docs/`.
- Page title comes from the single `#` H1. Content pages carry **no frontmatter** (only `index.md` does).
- Local preview / build: `npm run dev` / `npm run build` in `docs/`.

## Section layout (audience map)

- **End-user** sections: `overview/` (what-is, getting-started), `features/`, `resources/`.
- **Developer** sections: `developing/` (architecture, lifecycle, configuration, protocols, sdks, integrations, channels).

Place a new page where its audience already lives. If unsure where it goes, ask before creating it.

## Languages (bilingual)

- Two languages only: English at `docs/<path>.md`, Chinese mirror at `docs/zh/<path>.md`.
- Keep structure, headings, links, code, admonitions, and images identical across both, and edit both in the same change.
- VitePress handles `/zh/` routing; internal links omit the locale prefix and the `.md` extension.
- Desktop UI strings are a separate, multi-locale system — out of scope here (see the `dotcraft-dev-guide` skill).

## Callouts / admonitions

GitHub-flavored, used sparingly:

- `> [!NOTE]` — a clarification that prevents a wrong mental model.
- `> [!TIP]` — a shortcut or nicety.
- `> [!CAUTION]` — a real, often irreversible consequence (data loss, an open port).

## Multi-language code

Group parallel examples with VitePress code-group, canonical order **TypeScript → .NET → Python**:

```
::: code-group
\`\`\`ts [TypeScript]
\`\`\`
\`\`\`csharp [.NET]
\`\`\`
\`\`\`python [Python]
\`\`\`
:::
```

## Diagrams and media

- Diagrams are SVGs in `docs/public/`, referenced from the site root: `![alt](/name-topology.svg)`. Naming: `*-topology.svg`, `*-flow.svg`. Reuse an existing one before drawing a new one.
- External media (GIFs, screenshots) use the project's CDN / raw GitHub URLs already used elsewhere.

## Page footer

End every content page with a **Related docs** section: 2–5 relative links to sibling pages.

## Protected terms (don't paraphrase or translate)

`workspace`, `.craft/`, `Agent Teams`, `Mission`, `Team Leader`, `AppServer`, `Hub`, `Dreams`, `Souls`, `App Binding`, `Unified Session Core`. Code, commands, identifiers, and product names stay in English in both language versions.

## Cross-platform shells

The product is cross-platform: show `bash` first and a `powershell` alternative where commands differ (use `$null`, `$env:VAR` in PowerShell).

## Adapting this skill to another project

Replace this file with your project's profile. Cover: doc generator, section/audience layout, languages + file paths, callout syntax, code-tab syntax + language order, asset location, footer convention, and protected product terms. The portable files already point here for all of it.
