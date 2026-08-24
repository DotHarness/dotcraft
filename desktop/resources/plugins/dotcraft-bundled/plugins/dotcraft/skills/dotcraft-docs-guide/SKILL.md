---
name: dotcraft-docs-guide
description: Write, revise, and validate DotCraft user or developer documentation, including documentation-site pages, repository and sample READMEs, SDK/API guides, protocol references, lifecycle docs, and integration guides. Does not cover release notes or code comments.
---

# DotCraft documentation guide

Write current, task-focused documentation and keep applicable localized mirrors aligned. When documentation accompanies product work, use `dotcraft-dev-guide` for the broader development workflow.

## Workflow

1. Classify the artifact before applying conventions:
   - A page under `docs/` follows the documentation-site, localization, footer, and build rules in `references/project-profile.md`.
   - A README under a `samples/` or `examples/` tree follows `references/sample-readmes.md`.
   - Another repository README follows its owning package or project and any existing mirrors; do not apply documentation-site-only rules by default.
2. Inspect the current repository, the artifact's owner, nearby pages or READMEs, and the applicable navigation or entry points.
3. Identify one audience and one page job:
   - End user: tutorial, how-to, or feature explanation.
   - Developer: how-to, reference, or architecture/lifecycle explanation.
4. Load only the references the task needs:
   - Read `references/style-and-mechanics.md` before a substantial writing or editing pass.
   - Read `references/page-templates.md` when creating a documentation-site page or changing its archetype.
   - Read `references/sdk-and-protocol-docs.md` for SDK, AppServer, Hub, JSON-RPC, package-installation, generated-contract, Desktop-client, or Channel-client documentation.
   - Read `references/sample-readmes.md` for a README under a `samples/` or `examples/` tree.
5. Establish current behavior from source, tests, manifests, generated contracts, and durable specs before writing. Treat nearby docs as style evidence, not proof that a command or API still works.
6. Edit the smallest coherent set of artifacts. Keep one source of truth for installation and other drift-prone procedures; link to it instead of copying it.
7. For a documentation-site page or another artifact with established localized mirrors, update every mirror in the same change. Preserve heading hierarchy, code blocks, links, admonitions, images, and example order while translating prose naturally.
8. Validate proportionally:
   - Build the documentation site when a page under `docs/` changes.
   - Verify changed commands, symbols, options, and package claims against their owning code or toolchain.
   - For a sample README, validate its links and commands and run the smallest relevant sample build or verification.
   - Run targeted SDK/type checks when examples describe a current API surface.
   - Search for stale names, invalid install commands, old option names, and superseded guidance.
   - Compare locale page shapes mechanically when several mirrored pages changed.

## Editing rules

- Give each artifact one job. Link across tutorial, explanation, and reference layers instead of mixing them.
- Lead user pages with the outcome. Keep developer pages neutral, exact, and organized around the contract.
- Describe current behavior only. Keep migration rationale, compatibility history, and maintainer requirements in specs or issues.
- State load-bearing rules in prose; never make an example the only place a requirement appears.
- Keep examples minimal and executable. Use typed APIs for known contracts and explicitly named raw APIs only for open extensions.
- Use one H1, sentence-case headings, tagged code fences, relative internal links, and accessible image alt text.
- End documentation-site content pages with a localized `Related docs` footer. Do not impose that footer on repository READMEs unless their local convention uses it.
- Preserve established product names, commands, identifiers, JSON fields, and code in every locale.

## Completion check

- Audience, page job, and source of truth are clear.
- The documented behavior and package availability match the current repository and release state.
- Parallel language examples and any localized mirrors have the same structure.
- No duplicated procedure, dead link, stale identifier, historical narrative, FAQ, or troubleshooting dump remains.
- Documentation and relevant API/tooling validation pass.

## References

- `references/project-profile.md` — DotCraft paths, locales, VitePress syntax, code-group order, assets, and terminology. Read first for a page under `docs/`.
- `references/style-and-mechanics.md` — voice, grammar, headings, links, code, translation, and anti-patterns.
- `references/page-templates.md` — page skeletons by audience and purpose.
- `references/sdk-and-protocol-docs.md` — layered SDK/protocol information architecture, release-aware installation guidance, API boundaries, and validation.
- `references/sample-readmes.md` — minimal repository README guidance for runnable samples and examples.
