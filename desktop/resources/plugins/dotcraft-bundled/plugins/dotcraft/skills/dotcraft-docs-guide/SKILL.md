---
name: dotcraft-docs-guide
description: Write, revise, and validate DotCraft user or developer documentation, including documentation-site pages, repository and sample READMEs, SDK/API guides, protocol references, lifecycle docs, and integration guides. Does not cover release notes or code comments.
---

# DotCraft documentation guide

Write current, task-focused documentation and keep applicable localized mirrors aligned. When documentation accompanies product work, use `dotcraft-dev-guide` for the broader development workflow.

## Workflow

1. Classify the artifact before applying conventions:
   - A Markdown page included by the current VitePress configuration follows the documentation-site, localization, footer, and build rules in `references/project-profile.md`.
   - A README under a `samples/` or `examples/` tree follows `references/sample-readmes.md`; distinguish a runnable leaf sample from an aggregate examples index.
   - A README excluded from the documentation build, or another repository README, follows its owning package or project and any existing mirrors; do not apply documentation-site-only rules by default.
2. Inspect the current repository, the artifact's owner, nearby pages or READMEs, and the applicable navigation or entry points.
3. Identify one audience and one page job:
   - End user: tutorial, how-to, or feature explanation.
   - Developer: how-to, reference, or architecture/lifecycle explanation.
4. Load only the references the task needs:
   - Read `references/style-and-mechanics.md` before a substantial writing or editing pass.
   - Read `references/page-templates.md` when creating a documentation-site page or changing its archetype.
   - Read `references/sample-readmes.md` for a README under a `samples/` or `examples/` tree.
5. Establish current behavior from source, tests, manifests, generated contracts, and durable specs before writing. Treat nearby docs as style evidence, not proof that a command or API still works.
6. Edit the smallest coherent set of artifacts. Keep one source of truth for installation and other drift-prone procedures; link to it instead of copying it.
7. For a documentation-site page or another artifact with established localized mirrors, update every mirror in the same change. Preserve heading hierarchy, code blocks, links, admonitions, images, and example order while translating prose naturally.
8. Validate proportionally:
   - Build the documentation site when a rendered documentation-site page changes.
   - Verify changed commands, symbols, options, and package claims against their owning code or toolchain.
   - For a sample README, validate its links and commands and run the smallest relevant sample build or verification.
   - Run targeted SDK/type checks when examples describe a current API surface.
   - Search for stale names, invalid install commands, old option names, and superseded guidance.
   - Compare locale page shapes mechanically when several mirrored pages changed.
   - For zh pages, check punctuation (no "；") and the terminology rules in the profile.
   - When polishing rewrites a page's behavioral claims, spot-check the strongest ones against the owning code or spec — a faithful rewrite of a stale claim is still wrong.
   - When a change touches the theme's style layer, verify in a live browser against computed styles per the profile's "Site style layer" notes — never by reading CSS alone.

## Editing rules

- Give each artifact one job. Link across tutorial, explanation, and reference layers instead of mixing them.
- Include only information the reader needs to complete that job. A behavior being true or verified is not enough reason to document it. Omit defensive failure branches, redundant confirmation steps, and shutdown or cleanup guidance unless they require a distinct action, prevent a common blocker, or have a persistent or safety consequence.
- Lead user pages with the outcome. Keep developer pages neutral, exact, and organized around the contract.
- User pages answer three questions only: what the feature is for, when to reach for it, and how to turn it on or use it. Defaults, config keys, internal state names, tool identifiers, and edge-case caveats belong in the developing references — link once inline where the need arises. Never inventory a settings page's controls as bullets; give the UI path once and let a screenshot or GIF show the rest.
- Liveliness comes from concreteness and directness, not metaphor. Declarative sentences carry the page; at most an occasional light touch of humor. The target register: a capable colleague stating what the product does and when to reach for it — plain verbs, short sentences, zero marketing flourish.
- Use imperative sentences for steps the reader performs. Use declarative sentences for context, outcomes, responsibility boundaries, and product or agent behavior; do not turn an explanation into commands directed at the reader.
- State prerequisites only when the reader must satisfy them before the first step. When procedures differ by environment or distribution, label those paths directly instead of opening with a generic assumptions paragraph.
- Describe current behavior only. Keep migration rationale, compatibility history, and maintainer requirements in specs or issues.
- State load-bearing rules in prose; never make an example the only place a requirement appears.
- Keep examples minimal and executable. Verify API choices against the current implementation and owning specification instead of inferring them from this skill.
- Use one H1 on ordinary content pages, sentence-case headings, tagged code fences, relative internal links, and accessible image alt text. Follow the existing structure for custom landing pages.
- Place a page's primary figure directly after the intro block, above the first `##` heading; only a figure that explains one mid-page section follows that section's heading instead. Never use `---` horizontal rules — headings and whitespace carry the structure.
- A localized `Related docs` footer is optional, not standard. Keep it only when the page has genuine same-audience next steps (curated, usually 2–3); drop the section entirely when nothing qualifies. Developer references and architecture pages never appear in a user page's footer — link those inline at the sentence that needs them.
- Preserve established product names, commands, identifiers, JSON fields, and code in every locale. For Chinese pages, follow the terminology and punctuation rules in `references/project-profile.md`.

## Completion check

- Audience, page job, and source of truth are clear.
- The documented behavior and package availability match the current repository and release state.
- Parallel language examples and any localized mirrors have the same structure.
- No duplicated procedure, dead link, stale identifier, historical narrative, standalone FAQ, or troubleshooting dump remains.
- Documentation and relevant API/tooling validation pass.

## References

- `references/project-profile.md` — DotCraft paths, locales, VitePress syntax, code-group order, assets, and terminology. Read first for a page under `docs/`.
- `references/style-and-mechanics.md` — voice, grammar, headings, links, code, translation, and anti-patterns.
- `references/page-templates.md` — page skeletons by audience and purpose.
- `references/sample-readmes.md` — minimal repository README guidance for runnable samples and examples.
