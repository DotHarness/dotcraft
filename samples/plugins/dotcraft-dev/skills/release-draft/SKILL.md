---
name: release-draft
description: Draft DotCraft GitHub Release notes from repository evidence. Use when asked to write, revise, or prepare release copy for a DotCraft version, especially from a prior release style, What's New cards, GIF asset links, generated GitHub notes, changelog ranges, README/docs feature descriptions, or release tag comparisons.
---

# Release Draft

Use this skill to produce a GitHub Release body that feels consistent with recent DotCraft releases while staying grounded in current repo evidence.

## Standard Workflow

1. Identify the target version, reference version or URL, previous tag, and target commit/branch.
2. Inspect the reference release first when provided. Use read-only release inspection commands such as:

```bash
gh release view vX.Y.Z --repo DotHarness/dotcraft --json name,tagName,body,url,publishedAt,assets
```

3. Read the target What's New catalog when present:

```bash
desktop/resources/whats-new/releases/<version-without-v>.json
```

Use each card's title, summary, and `media.url` as the core feature list and GIF source.

4. Read concise supporting docs for each feature before expanding copy. Prefer:

- `README.md` / `README_ZH.md`
- `docs/features/**` and `docs/zh/features/**`
- `docs/developing/sdk*.md` and `docs/zh/developing/sdk*.md` for App Binding / SDK items
- targeted `rg` results for feature names, PR titles, and config keys

5. Generate or reconstruct the GitHub "What's Changed" section. `releases/generate-notes` is allowed only as a non-publishing helper that returns text:

```bash
gh api repos/DotHarness/dotcraft/releases/generate-notes -X POST -f tag_name=vX.Y.Z -f target_commitish=<branch-or-sha> -f previous_tag_name=vA.B.C
```

Fallbacks: `git log --oneline vA.B.C..<target>` and PR titles from `gh pr view` / `gh pr list` when available.

6. Draft the release body as a copy-paste Markdown template. Publishing is a user action.

## Release Shape

Match the reference release's structure unless the user asks for a different format. For current DotCraft releases, use:

```markdown
# DotCraft vX.Y.Z

DotCraft vX.Y.Z is a major release focused on ...

## Core Features

1. **Feature Name**
   Short user-facing paragraph.

![](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/feature.gif)

## Infrastructure and Experience Improvements

1. **Improvement Name**
   Short user-facing paragraph.

## What's Changed

* ...

**Full Changelog**: https://github.com/DotHarness/dotcraft/compare/vA.B.C...vX.Y.Z
```

## Writing Rules

- Match the reference release's language, tone, heading style, and level of detail. If the reference is v0.1.6, use concise English GitHub Release copy with numbered feature sections.
- Lead with user-visible capabilities from What's New. Put reliability, tooling, and polish in "Infrastructure and Experience Improvements."
- Use the GIF URLs exactly from the What's New catalog when available.
- Expand terse What's New summaries with repo/docs evidence, not speculation.
- Preserve product terminology: `Agent Teams`, `Mission`, `Team Leader`, `App Binding`, `ChatGPT subscription`, `What's New`.
- Mention plan tiers only when supported by docs/code or existing release copy.
- Include generated "What's Changed" entries and the compare link verbatim unless cleaning obvious formatting only.
- Never run commands that create, edit, publish, delete, or upload assets to a GitHub Release, such as `gh release create`, `gh release edit`, `gh release delete`, or `gh release upload`.
- State that the user must perform the actual GitHub Release publishing step; you only provide the draft template.
- If a tag or release does not exist yet, state that clearly and draft against the current branch/HEAD.
- If evidence conflicts, surface the assumption briefly instead of silently choosing.

## Quality Bar

Before returning the draft:

- Confirm every highlighted feature has an evidence source.
- Confirm each media link renders from `https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/`.
- Confirm the compare range uses the previous release tag and target version.
- Keep the answer copy-paste ready. Add a short note after the draft only for sources, caveats, or missing data.
