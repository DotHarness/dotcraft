---
name: dotcraft-release-draft
description: Draft DotCraft GitHub Release notes from repository evidence. Use when preparing or revising release copy for a version.
---

# Release Draft

Produce an English, copy-paste-ready GitHub Release body grounded in the changes since the previous release. Evidence may be localized; write the release copy in English.

## Standard Workflow

1. Identify the target version, previous release tag, target tag or commit, and any reference release the user provided. Check whether the target tag and GitHub Release exist. If the tag is absent, use the requested branch or commit, or current HEAD when none was specified, and say so.
2. Inspect the supplied reference release, or the most recent comparable release, before choosing the format. Use read-only commands such as:

```bash
gh release view vX.Y.Z --repo DotHarness/dotcraft --json name,tagName,body,url,publishedAt,assets
```

3. Inspect the entire `<previous-tag>..<target>` range with `git log`, including standalone commits that GitHub's generated notes may omit. Read relevant PR descriptions and focused diffs or specifications to verify candidate highlights and fixes.
4. Read the target What's New catalog if it exists:

```bash
desktop/resources/whats-new/releases/<version-without-v>.json
```

Treat its cards and `media.url` values as candidate features and media, then check them against the target range. If the catalog is absent, draft from commits and supporting sources without inventing cards or GIFs.

5. Read concise supporting docs for claims that need more context. Prefer the current README, relevant feature or developer docs, and their localized mirrors when they add useful evidence. Use targeted `rg` results for feature names, PR titles, and config keys.

6. Generate or reconstruct the GitHub "What's Changed" section. `releases/generate-notes` is allowed only as a non-publishing helper that returns text:

```bash
gh api repos/DotHarness/dotcraft/releases/generate-notes -X POST -f tag_name=vX.Y.Z -f target_commitish=<tag-or-branch-or-sha> -f previous_tag_name=vA.B.C
```

Fallbacks: the verified Git range and PR titles from `gh pr view` / `gh pr list` when available. Preserve generated links and attribution; do not treat a PR-only list as an inventory of every release-worthy change.

7. Draft the release body as copy-paste Markdown. Publishing is a user action.

## Release Shape

Match the reference release's tone, headings, and level of detail unless the user asks for a different format. Recent releases use a short introduction, **Highlights**, a **Fixed**, **Fixes**, or **Other Changes** section when warranted, and **What's Changed**. Omit empty sections and media when none is available. For example:

```markdown
# DotCraft vX.Y.Z

DotCraft vX.Y.Z brings ...

## Highlights

- **Feature Name** — What changed and what a user or developer can now do.

## Fixed

- A consequential failure mode and its corrected behavior.

## What's Changed

* ...

**Full Changelog**: https://github.com/DotHarness/dotcraft/compare/vA.B.C...vX.Y.Z
```

## Writing Rules

- Write the complete release body in English. Keep identifiers, product names, code, and URLs in their canonical form.
- Select Highlights by impact on user workflows or developer use, not by PR size or whether a change has a What's New card. Include significant standalone commits. Use the fixes section for meaningful reliability and correctness outcomes.
- Review documentation-only changes, routine UI wording, localization, and small polish without automatically promoting them into the release body. Include them when their impact defines this release or the user asks for them. The "What's Changed" list can remain broader than the curated summary.
- Use media URLs exactly from the What's New catalog when available, after verifying they resolve. Do not add a GIF merely to fill the template.
- Expand terse What's New summaries with repo/docs evidence, not speculation.
- Preserve established product terminology. Examples (e.g., not an exhaustive list) include `Agent Builder`, `Agent Profiles`, `App Binding`, `ChatGPT subscription`, and `What's New`; inspect current product and documentation sources for additional terms.
- Mention plan tiers only when supported by docs/code or existing release copy.
- Preserve generated "What's Changed" links and attribution. Translate any non-English entry titles faithfully so the release body remains English; do not add unsupported claims.
- Never run commands that create, edit, publish, delete, or upload assets to a GitHub Release, such as `gh release create`, `gh release edit`, `gh release delete`, or `gh release upload`.
- State that the user must perform the actual GitHub Release publishing step; you only provide the draft template.
- If the target GitHub Release does not exist yet, state that clearly.
- If evidence conflicts, surface the assumption briefly instead of silently choosing.

## Quality Bar

Before returning the draft:

- Confirm every highlighted feature and fix against the full Git range and an implementation, specification, or PR source.
- Confirm each included media link resolves and, when sourced from What's New, retains its catalog URL.
- Confirm the compare range uses the previous release tag and target version.
- Confirm the release body is English except for canonical identifiers, product names, code, and URLs.
- Keep the answer copy-paste ready. Add a short note after the draft only for sources, caveats, or missing data.
