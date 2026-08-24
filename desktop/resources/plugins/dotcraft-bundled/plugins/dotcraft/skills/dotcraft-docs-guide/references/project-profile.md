# Project Profile — DotCraft

This file records DotCraft-specific conventions and examples. Verify them against the current repository before writing; locale support and product terminology can evolve.

The documentation-site rules in this profile apply only to pages included by the current VitePress
configuration. Inspect `srcExclude` and nearby project configuration before classifying a Markdown
file under `docs/`. Excluded and other repository READMEs use ordinary repository links and the
conventions of their owning project; sample and example READMEs follow `sample-readmes.md`.

## Doc system

- Static site generator: **VitePress**, sources under `docs/`.
- Ordinary content pages use one `#` H1 as the title and carry no frontmatter. The English and Chinese locale home pages use frontmatter and custom HTML instead; preserve their existing structure.
- Local preview / build: `npm run dev` / `npm run build` in `docs/`.

## Section layout (audience map)

- **End-user** content: root `getting-started.md` and `features/`.
- **Developer** sections: `developing/` (architecture, lifecycle, configuration, protocols, sdks, integrations, channels).

Treat the current VitePress navigation as authoritative. Place a new page where its audience already lives; ask before creating it when no current section owns the topic.

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

- Diagrams are SVGs in `docs/public/`, referenced from the site root: `![alt](/name-topology.svg)`. Established descriptive suffixes include `*-topology.svg`, `*-flow.svg`, and `*-overview.svg`. Reuse an existing one before drawing a new one.
- External media (GIFs, screenshots) use the project's CDN / raw GitHub URLs already used elsewhere.

## Documentation-site page footer

End ordinary documentation-site content pages with a **Related docs** section, translated to the locale's natural equivalent, containing concise relative links to relevant next steps or authoritative pages. Custom locale landing pages and repository READMEs follow their own structure.

## Protected terms (examples, not an exhaustive list)

Preserve established product terminology exactly. Examples include `workspace`, `.craft/`, `Agent Teams`, `Mission`, `Team Leader`, `AppServer`, `Hub`, `Dreams`, `App Binding`, and `Unified Session Core`. Before writing, inspect current UI copy, specs, code, and nearby docs rather than treating this list as a product catalog. Keep code, commands, identifiers, and product names unchanged unless the current project explicitly localizes them.

## Cross-platform shells

Prefer one cross-platform command when possible. Show labeled shell variants only when behavior differs, and follow the owning surface or nearby page for their order.

## Keeping this profile current

Treat the entries above as guidance, not a closed inventory. When repository configuration or established usage disagrees with this file, follow the current repository and update the profile.
