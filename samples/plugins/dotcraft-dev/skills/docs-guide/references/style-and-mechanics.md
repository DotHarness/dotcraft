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
- **User-facing pages** read like a helpful colleague: encouraging, plain, outcome-first. **Developer pages** read like a spec: neutral, exact, no filler. The product is one; the register changes with the audience.
- Contractions are fine and preferred in user docs ("it's," "you'll") — they read naturally. Developer reference can be more formal but needn't be stiff.

## 2. Sentences and word choice

- Bigger ideas, fewer words. Shorter is almost always better. If a sentence runs past ~25 words, split it.
- Front-load the point. Put the keyword or outcome first so a scanning reader catches it.
- Active voice by default. Passive only when the actor is irrelevant.
- Define a term on first use or avoid it. Don't expose internal file paths, class names, or infra jargon in user docs unless the reader will type them.
- One idea per paragraph. White space is a feature.

## 3. Headings and capitalization

- Exactly one `#` (H1) per file — the generator makes it the title and often the URL.
- **Sentence case** for headings: "Getting started," not "Getting Started," except where a heading is a proper product name. Be consistent within a page and with its siblings.
- Headings are descriptive and scannable. A reader should navigate by headings alone. Prefer "Connect a remote server" over "Usage."
- No trailing punctuation on headings.
- Use `##` for major sections, `###` for steps/subsections. Reserve `####` for deep reference only; if you need it often, the page may be doing too much.

## 4. Code blocks

- Tag every fence with its language; untagged blocks lose highlighting.
- If the project targets multiple platforms, show the primary shell first and an alternative where commands differ (see the profile for the convention).
- Group parallel multi-language SDK/API examples in tabs, in a fixed language order, every language present and the steps kept parallel (syntax + order: profile).
- Keep examples minimal and runnable. User docs: only what the reader types and the expected output. Developer docs: complete and copy-pasteable, with real values rather than `<foo>` where a concrete example is clearer.
- Inline code (backticks) for commands, flags, file paths, env vars, and identifiers.

## 5. Tables

- Use tables for structured, parallel facts (options, concepts, platforms). Use prose for narrative and reasoning.
- Cap at ~5 columns; wider tables break on mobile and stop being scannable.
- Bold the key term in the first column to anchor the eye.
- Don't tabularize a process — that's a numbered list. Don't prose-ify a set of options — that's a table.

## 6. Links and cross-references

- Internal links are **relative** and omit the file extension and locale prefix (the generator handles routing — see profile).
- Link the first mention of another doc's concept to that doc — treat every page as a possible entry point ("every page is page one"). A developer reference should link the first occurrence of a term to its concept page.
- End every content page with a **Related docs** section: 2–5 sibling links that are the natural next step. This is the site's connective tissue; pages without it are dead ends.
- External links: full URLs.

## 7. Admonitions — when each one fits

Used sparingly — they only work when rare (syntax in the profile).

- **Note** — a clarification that prevents a wrong mental model.
- **Tip** — a shortcut, default, or nicety the reader would be glad to learn.
- **Caution** — a real, often irreversible consequence: data loss, an open network port, an overwrite. Reserve it; if everything is a caution, nothing is.

If a page has more than two or three callouts per screen, fold most back into prose.

## 8. Images and diagrams

- Store diagrams where the generator serves static assets and reference them with meaningful alt text — it's both the accessible and the search-indexed description (location + naming: profile).
- Reuse an existing diagram before commissioning a new one.
- Prefer a short embedded GIF/video over a wall of screenshots for interaction-heavy flows.
- Alt text and any in-image captions must exist in every language version.

## 9. Multi-language sync

Applies when the project ships docs in more than one language (languages + paths: profile).

- Every page exists once per language. Shipping one language without the others is incomplete work.
- Translate **meaning**, not words. A translated page should read naturally to a native reader, not like machine output.
- Keep them structurally identical: same headings in the same order, same code blocks, same links, same admonitions, same images. A reader switching locales should land on the same page shape.
- Code, commands, identifiers, and product names stay in the source language in every version. Translate the prose around them.
- When you edit one language, edit the others in the same change. Don't leave a "translate later" gap — it rots.

## 10. Anti-patterns — what must NOT appear (before/after)

The recurring ways docs lose readers, with worked fixes. Each maps to a numbered rule in SKILL.md's "What must NOT appear." The examples are illustrative (not quotes from any one page); pattern-match against them when reviewing.

### 10.1 Internal mechanics on a user/feature page (rule 1)

A feature page explains the idea; the moment a path, config key, tool name, or enum appears, it should be a link to reference — not body text.

> **Before** — a "what is memory" page whose first table reads:
> `| Session history | internal/storage/path | Engine, automatically | Full Record/Turn/Item timeline |`
>
> **After:** "The agent keeps a full history of every session, plus long-term notes it writes as it learns your project." Move the storage path and the internal data model to the reference page and link: "See [how sessions are stored](…)."

The test: would a non-developer need this token to understand the feature? If not, it's reference, and it belongs behind a link.

### 10.2 Architecture before the reader needs it (rule 2)

> **Before** — a getting-started page that, right after the user's first action, opens a section on the internal "execution engine" and an architecture comparison table.
>
> **After:** End the tutorial at "you did the thing — here's what to try next." If the reader wants the why, link once: "Curious how it works under the hood? See [Architecture Overview](…)." The internals live there, for the audience that wants them.

### 10.3 Register whiplash (rule 3)

> **Before** — a feature page that swings within a few lines from "you give one ask and get the finished result" to "X is a managed runtime built on the internal session subsystem."
>
> **After:** Keep the whole feature page in the user register. The "managed runtime built on …" sentence moves to a developer page about how the feature is implemented.

### 10.4 Jargon before definition (rule 4)

> **Before** — step 3 of an onboarding flow opens with an undefined internal term ("uses a provider registry"), then drops a 17-line config blob.
>
> **After:** Lead with the recommended path ("the setup wizard does this: pick a provider, paste a key"). Keep the raw config as an optional "edit directly" fallback *below* it, and define the term in one plain clause the first time it appears.

### 10.5 History / migration / compatibility rationale (rule 5)

> **Before:** "…historical records aren't migrated, so older data may still use the previous granularity." / "ids are kept for compatibility."
>
> **After:** Delete both. State only current behavior. A reader integrating today can't act on what old data looked like.

### 10.6 Internal-only / spec-voice content (rule 6)

> **Before:** "the error screen's primary action **should** open connection settings…" and maintainer-only env flags in a consumer-facing page.
>
> **After:** Describe observed behavior to the user: "If a saved connection is invalid, the error screen offers **Open connection settings** so you can fix it." Keep maintainer flags and "should"-requirements in specs, not in user or consumer docs.

### 10.7 Duplicated content that drifts (rule 7)

> **Before:** the same comparison table appears on two pages — with *different* column labels — so a reader can't tell if they're the same thing.
>
> **After:** Put the table on one page; everywhere else, link to it. Single source = no drift, one vocabulary.

### 10.8 Unnatural / translated phrasing (rule 8)

> **Before:** "The three files decide \"who this agent is and what rules it follows\"."
> **After:** "Three files define the agent's identity and rules for this project."
>
> **Before:** one sentence listing six path types inline, ending in an undefined term.
> **After:** a lead sentence + a short bullet list of the types, with the term defined or dropped.

Read every paragraph aloud. Noun piles, hidden actors, and 30-word sentences are the tells.

### 10.9 Load-bearing rules left implicit (rule 9)

> **Before:** a connection rule (e.g. clients must append a path suffix to the server URL) is shown only by example, scattered across pages, and resurfaces as a troubleshooting item.
>
> **After:** State it once as a rule on the relevant page: "The server listens on `host:port`; clients append `/suffix`." Examples then just confirm the rule instead of being the only place it lives.

### Also still watch for

- **Dead-end pages** — no "Related docs" footer. Add onward links.
- **Callout overuse** — stacked notes that should be prose. Thin them.
- **Table-vs-prose mismatch** — steps in a table, or options in paragraphs. Match form to content.
- **Out-of-sync translations** — one language edited, the others left stale. Update them in the same change.
