# Project Profile — DotCraft

This file records DotCraft-specific conventions and examples. Verify them against the current repository before writing; locale support and product terminology can evolve.

The documentation-site rules in this profile apply to pages under `docs/`. Repository READMEs use
ordinary repository links and the conventions of their owning project; sample and example READMEs
follow `sample-readmes.md`.

## Doc system

- Static site generator: **VitePress**, sources under `docs/`.
- Page title comes from the single `#` H1. Content pages carry **no frontmatter** (only `index.md` does).
- Local preview / build: `npm run dev` / `npm run build` in `docs/`.

## Section layout (audience map)

- **End-user** sections: `overview/` (what-is, getting-started), `features/`, `resources/`.
- **Developer** sections: `developing/` (architecture, lifecycle, configuration, protocols, sdks, integrations, channels).

Place a new page where its audience already lives. If unsure where it goes, ask before creating it.

## Localized documentation-site pages

- Before editing, inspect the current VitePress locale configuration and existing page mirrors to discover every supported documentation locale and its path.
- Keep all localized versions structurally aligned: headings, links, code, admonitions, and images should match, and affected versions should be updated together.
- Follow the current locale routing and internal-link conventions found in the site configuration and nearby pages.
- UI localization is separate and is covered by `dotcraft-dev-guide`.

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

## Documentation-site page footer

End every content page under `docs/` with a **Related docs** section, translated to the locale's natural equivalent, containing 2–5 relative links to sibling pages. This footer is not required for repository READMEs.

## Protected terms (examples, not an exhaustive list)

Preserve established product terminology exactly. Examples (e.g.) include `workspace`, `.craft/`, `Agent Teams`, `Mission`, `Team Leader`, `AppServer`, `Hub`, `Dreams`, `Souls`, `App Binding`, and `Unified Session Core`. Before writing, inspect current UI copy, specs, code, and nearby docs for additional protected terms. Keep code, commands, identifiers, and product names unchanged unless the current project explicitly localizes them.

## Cross-platform shells

The product is cross-platform: show `bash` first and a `powershell` alternative where commands differ (use `$null`, `$env:VAR` in PowerShell).

## Keeping this profile current

Treat the entries above as guidance, not a closed inventory. When repository configuration or established usage disagrees with this file, follow the current repository and update the profile.
