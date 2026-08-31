# Style and Mechanics

Detailed rules behind the SKILL.md decision framework. Read before a substantial writing or editing pass. The goal is clarity and consistency, not rule-worship — depart from a rule when doing so genuinely serves the reader, but know which rule you're departing from. Anything concrete to this repo (callout syntax, languages, file paths, asset location, protected terms) is in `references/project-profile.md`.

## Table of contents

1. Voice and person
2. Sentences and word choice
3. Headings and capitalization
4. Code blocks
5. Tables
6. Links and cross-references
7. Admonitions — when each one fits
8. Images and diagrams
9. Multi-language sync
10. Anti-patterns — what must NOT appear (before/after)

---

## 1. Voice and person

- Address the reader as **you**; refer to the product or agent by name. Avoid "we" — it's vague about who acts.
- **Imperative for instructions:** "Open Settings," "Run the command." Cut "you can" and "there is/are" — start statements with a verb.
- **Declarative for explanation:** Describe outcomes, responsibility boundaries, and product or agent behavior as facts. Do not phrase contextual explanation as an instruction to the reader.
- **User-facing pages** read like a helpful colleague: encouraging, plain, outcome-first. **Developer pages** are technical, neutral, and exact; link to the normative specification when contract detail matters. The product is one; the register changes with the audience.
- Contractions are fine and preferred in user docs ("it's," "you'll") — they read naturally. Developer reference can be more formal but needn't be stiff.
- **Metaphor is seasoning, not structure.** Liveliness comes from concrete, specific statements ("decisions you discussed, preferences you set"), not from imagery ("give it arms," "pin a goal," "potholes you hit"). One light touch per page at most; a stack of metaphors reads as unserious.

## 2. Sentences and word choice

- Bigger ideas, fewer words. Shorter is almost always better. If a sentence runs past ~25 words, split it.
- Front-load the point. Put the keyword or outcome first so a scanning reader catches it.
- Active voice by default. Passive only when the actor is irrelevant.
- Define a term on first use or avoid it. Don't expose internal file paths, class names, or infra jargon in user docs unless the reader will type them.
- One idea per paragraph. White space is a feature.

## 3. Headings and capitalization

- Use exactly one `#` (H1) on ordinary content pages. Preserve the established frontmatter and custom HTML structure of locale landing pages.
- **Sentence case** for headings: "Getting started," not "Getting Started," except where a heading is a proper product name. Be consistent within a page and with its siblings.
- Headings are descriptive and scannable. A reader should navigate by headings alone. Prefer "Connect a remote server" over "Usage."
- No trailing punctuation on headings.
- Use `##` for major sections, `###` for steps/subsections. Reserve `####` for deep reference only; if you need it often, the page may be doing too much.

## 4. Code blocks

- Tag every fence with its language; untagged blocks lose highlighting.
- If the project targets multiple platforms, prefer one cross-platform command and show labeled alternatives only where behavior differs (see the profile for ordering).
- Group parallel multi-language SDK/API examples in tabs, in a fixed language order, every language present and the steps kept parallel (syntax + order: profile).
- Keep examples minimal and runnable. User docs: only what the reader types and the expected output. Developer docs: complete and copy-pasteable, with real values rather than `<foo>` where a concrete example is clearer.
- Inline code (backticks) for commands, flags, file paths, env vars, and identifiers.

## 5. Tables

- Use tables for structured, parallel facts (options, concepts, platforms). Use prose for narrative and reasoning.
- Cap at ~5 columns; wider tables break on mobile and stop being scannable.
- Bold the key term in the first column to anchor the eye.
- Don't tabularize a process — that's a numbered list. Don't prose-ify a set of options — that's a table.

## 6. Links and cross-references

- Documentation-site links are **relative** and omit the file extension and locale prefix (the generator handles routing — see profile). Repository READMEs use ordinary relative file links.
- Link the first mention of another doc's concept to that doc — treat every page as a possible entry point ("every page is page one"). A developer reference should link the first occurrence of a term to its concept page.
- A **Related docs** footer is kept only when it earns its place: same-audience pages that are the natural next read, curated to 2–3 entries with a short reason each. A user page never lists config references or architecture pages in its footer — those links live inline, at the sentence that needs them. When nothing qualifies, omit the section; an inline link mid-prose is a better onward path than a token footer.
- External links: full URLs.

## 7. Admonitions — when each one fits

Used sparingly — they only work when rare (syntax in the profile).

- **Note** — a clarification that prevents a wrong mental model.
- **Tip** — a shortcut, default, or nicety the reader would be glad to learn.
- **Caution** — a real, often irreversible consequence: data loss, an open network port, an overwrite. Reserve it; if everything is a caution, nothing is.

If a page has more than two or three callouts per screen, fold most back into prose.

## 8. Images and diagrams

- Store diagrams where the generator serves static assets and reference them with meaningful alt text — it's both the accessible and the search-indexed description (location + naming: profile).
- **Placement:** the page's primary figure sits directly after the intro block, above the first `##` heading — site-wide convention. A figure that explains one mid-page section follows that section's heading instead.
- Reuse an existing diagram before commissioning a new one.
- Prefer a short embedded GIF/video over a wall of screenshots for interaction-heavy flows.
- Alt text and any in-image captions must exist in every applicable language version when localized mirrors exist.
- Never draw structure with `---` horizontal rules; the site style intentionally has no rules between sections (headings + whitespace carry the rhythm).

## 8a. Localized prose specifics

Generic localization principles first; per-locale rules follow.

- **Never coin a term.** When a proper noun or feature name needs a localized form, the ground-truth order is: the product's own UI copy in that locale → the prevailing name across established products and the surrounding ecosystem in that language → keep the source-language term. Survey real usage before deciding; a made-up translation is worse than an untranslated one.
- **One term, one state.** A given term is either translated everywhere or kept in the source language everywhere. Mixed states of the same word across pages (or within one page) are never acceptable; record settled decisions in the profile's terminology table.

Chinese (zh):

- **Almost never use the Chinese semicolon "；".** Split into separate sentences ending in "。". Keep colons and em-dashes restrained too — when a sentence can simply end, end it.
- Declarative sentences carry zh pages the same as en. Avoid stacked four-character flourishes and translated-sounding constructions; read each paragraph aloud.
- Follow the zh terminology rules in the profile; current feature names come from the product UI locale and the live sidebar labels, not from any list.
- zh is the polish source for user pages: settle the zh wording first, then align the en mirror to the same structure and meaning.

## 9. Documentation-site multi-language sync

Applies to documentation-site pages and other artifacts with established localized mirrors (languages + paths: profile).

- Every applicable documentation-site page exists once per language. Shipping one language without the others is incomplete work.
- Translate **meaning**, not words. A translated page should read naturally to a native reader, not like machine output.
- Keep them structurally identical: same headings in the same order, same code blocks, same links, same admonitions, same images. A reader switching locales should land on the same page shape.
- Code, commands, identifiers, and product names stay in the source language in every version. Translate the prose around them.
- When you edit one language, edit the others in the same change. Don't leave a "translate later" gap — it rots.

## 10. Anti-patterns — what must NOT appear (before/after)

The recurring ways docs lose readers, with worked fixes. The examples are illustrative (not quotes from any one page); pattern-match against them when reviewing.

### 10.1 Internal mechanics on a user/feature page

A feature page explains the idea. Keep a path, config key, tool name, or enum in the body when the reader must type or inspect it to complete the task; move implementation-only detail to the owning reference and link there.

> **Before** — a "what is memory" page whose first table reads:
> `| Session history | internal/storage/path | Engine, automatically | Full Record/Turn/Item timeline |`
>
> **After:** "The agent keeps a full history of every session, plus long-term notes it writes as it learns your project." Move the storage path and the internal data model to the reference page and link: "See [how sessions are stored](…)."

The test: does the reader need this token to understand or use the feature on this page? If not, it belongs in the owning reference behind a link.

### 10.2 Architecture before the reader needs it

> **Before** — a getting-started page that, right after the user's first action, opens a section on the internal "execution engine" and an architecture comparison table.
>
> **After:** End the tutorial at "you did the thing — here's what to try next." If the reader wants the why, link once: "Curious how it works under the hood? See [Architecture Overview](…)." The internals live there, for the audience that wants them.

### 10.3 Register whiplash

> **Before** — a feature page that swings within a few lines from "you give one ask and get the finished result" to "X is a managed runtime built on the internal session subsystem."
>
> **After:** Keep the whole feature page in the user register. The "managed runtime built on …" sentence moves to a developer page about how the feature is implemented.

### 10.4 Jargon before definition

> **Before** — step 3 of an onboarding flow opens with an undefined internal term ("uses a provider registry"), then drops a 17-line config blob.
>
> **After:** Lead with the recommended path ("the setup wizard does this: pick a provider, paste a key"). Keep the raw config as an optional "edit directly" fallback *below* it, and define the term in one plain clause the first time it appears.

### 10.5 History / migration / compatibility rationale

> **Before:** "…historical records aren't migrated, so older data may still use the previous granularity." / "ids are kept for compatibility."
>
> **After:** Delete both. State only current behavior. A reader integrating today can't act on what old data looked like.

### 10.6 Internal-only / spec-voice content

> **Before:** "the error screen's primary action **should** open connection settings…" and maintainer-only env flags in a consumer-facing page.
>
> **After:** Describe observed behavior to the user: "If a saved connection is invalid, the error screen offers **Open connection settings** so you can fix it." Keep maintainer flags and "should"-requirements in specs, not in user or consumer docs.

### 10.7 Duplicated content that drifts

> **Before:** the same comparison table appears on two pages — with *different* column labels — so a reader can't tell if they're the same thing.
>
> **After:** Put the table on one page; everywhere else, link to it. Single source = no drift, one vocabulary.

### 10.8 Unnatural / translated phrasing

> **Before:** "The three files decide \"who this agent is and what rules it follows\"."
> **After:** "Three files define the agent's identity and rules for this project."
>
> **Before:** one sentence listing six path types inline, ending in an undefined term.
> **After:** a lead sentence + a short bullet list of the types, with the term defined or dropped.

Read every paragraph aloud. Noun piles, hidden actors, and 30-word sentences are the tells.

### 10.9 Load-bearing rules left implicit

> **Before:** a connection rule (e.g. clients must append a path suffix to the server URL) is shown only by example, scattered across pages, and resurfaces as a troubleshooting item.
>
> **After:** State it once as a rule on the relevant page: "The server listens on `host:port`; clients append `/suffix`." Examples then just confirm the rule instead of being the only place it lives.

### 10.10 Defensive detail without a reader decision

> **Before:** A successful setup path adds checks for implied state, rare failure branches, and a final section explaining how to stop or clean up a transient session.
>
> **After:** Keep the actions required for the successful path. Add confirmation, recovery, or cleanup only when the reader must make a distinct decision, the omission commonly blocks completion, or the operation has a persistent or safety consequence.

Verified behavior is not automatically useful documentation. Ask what the reader does differently because of the sentence; if the answer is nothing, remove it.

### Also still watch for

- **Dead-end documentation-site pages** — no onward path at all. Add inline links where concepts are mentioned, or a curated footer when genuine next steps exist.
- **Settings-inventory dumps** — a bullet list reciting every control on a settings page ("the page offers: Enable X, Run now, Auto-update, Manage…"). Give the UI path once; the screenshot or GIF shows the controls.
- **Callout overuse** — stacked notes that should be prose. Thin them.
- **Table-vs-prose mismatch** — steps in a table, or options in paragraphs. Match form to content.
- **Out-of-sync translations** — one language edited, the others left stale. Update them in the same change.
- **Troubleshooting dumps** — keep short, source-backed recovery guidance with the owning page, but do not create a standalone catch-all FAQ or error catalog.
