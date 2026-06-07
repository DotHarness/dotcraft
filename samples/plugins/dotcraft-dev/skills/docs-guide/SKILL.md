---
name: dotcraft-docs-guide
description: Standards for writing and editing DotCraft documentation under `docs/` (the bilingual VitePress site). Use whenever creating or revising a docs page — overview/getting-started, a feature page, an architecture/concept explainer, an SDK/API/CLI/protocol reference, or a how-to — or when deciding the right tone, structure, and code density for a page. Trigger this even when the user just says "write docs for X", "add a page about Y", "the docs for Z read badly", or "make this doc more user-friendly", and especially when a page must serve end users vs. developers differently. Does not cover GitHub release notes (use release-draft) or in-code XML/JSDoc comments.
---

# Documentation Guide

A reusable standard for writing product documentation. The single most important decision for any page is **who it is for** and **what job it does** — that choice drives tone, structure, code density, and what you leave out. Most weak docs fail because they mix a beginner's "show me" with a developer's "give me the contract" on one page. This skill keeps those separate.

The principles here are project-agnostic. Everything specific to *this* repository — doc framework, section layout, languages, file paths, callout syntax, protected product terms — lives in `references/project-profile.md`. Read that profile first; to reuse this skill on another project, replace only that file. (For the broader DotCraft dev workflow, see the `dotcraft-dev-guide` skill.)

## When to use

Use this when you write or revise any documentation page, or user-facing README copy. Before drafting prose, settle the two questions in Step 1 — they are the whole game.

## Step 1 — Decide audience and purpose

Every page answers two questions before you write a sentence.

**Who is the reader?** Keep the page true to the section it lives in (the exact section→audience map is in the profile):

| Reader | Their pages live in… | They want |
|---|---|---|
| **End user** | user-facing sections (overview, getting-started, features, guides) | To get a result. "What is this, why do I care, how do I do the thing." |
| **Developer** | reference sections (architecture, SDK/API, protocols, configuration) | To build against a contract. "Exactly how it behaves, every option, edge cases." |

**What job does the page do?** This is the Diátaxis distinction — a page does *one* of these well, not all four:

| Purpose | Reader is… | The page… | Typical home |
|---|---|---|---|
| **Tutorial** | learning by doing | walks one happy path to first success | getting-started, quickstarts |
| **How-to** | competent, has a goal | gives direct steps to solve one real task | feature pages, SDK task pages |
| **Reference** | needs a fact | describes options/behavior austerely and completely | config, protocols, CLI, API, SDK method docs |
| **Explanation** | wants to understand | discusses concepts, trade-offs, "why" | architecture, lifecycle, concept overviews |

If you can't name one cell in each table, the page has no focus yet — split it. A feature page that drifts into protocol field tables should hand that off to a reference page and link to it.

**Routing examples:** a "Getting Started" page = end user × tutorial → warm, one path, almost no code internals. A protocol page = developer × reference → neutral, exhaustive, table-driven. A "what is feature X" page = end user × explanation → friendly concept page with a diagram, light on code.

## Step 2 — Match the voice to the reader

This is where user and developer docs genuinely diverge. Same product, two registers:

| | **User-facing pages** | **Developer pages** |
|---|---|---|
| **Voice** | Warm, second-person, encouraging. Talk to the reader like a helpful colleague. | Neutral, precise, austere. Respect the reader's expertise; no hand-holding. |
| **Opening** | One plain sentence on the value: what they get and why it matters. | One sentence naming the audience and scope (e.g. "This page targets integrators and contributors"). |
| **Structure** | Task-first: "what you'll do" before "how it works." Lead with the outcome. | Mirrors the product: the doc's structure matches the API/config/protocol structure. |
| **Code** | Sparse. Commands they actually type, expected output. No internal types or wire details. | Dense and complete. Full signatures, params, payloads; multi-language grouped in tabs. |
| **"Why" vs "how"** | Show, don't lecture. Minimize explanation inside a tutorial. | Explain trade-offs and consequences where they affect correct use. |
| **Jargon** | Define or avoid. Prefer plain language over infra paths and internal names. | Use precise terms freely; link the first occurrence to its reference. |
| **Omit** | Internal mechanics, exhaustive flag lists, edge cases. | Marketing language, motivational framing, repetition of basics. |

When a page must serve both (e.g. a feature a power user will also script against), write the user narrative first and **link out** to the developer reference rather than inlining it. One page, one register.

The detailed mechanics — person, headings, capitalization, sentence length, contractions — are in `references/style-and-mechanics.md`. Read it before a substantial writing pass.

## Step 3 — Use the right page template

Don't invent structure. Copy the matching skeleton from `references/page-templates.md` (quickstart, feature overview, how-to, concept/architecture, reference, troubleshooting) and fill it in.

## House conventions (apply to every page)

These principles are universal; the exact syntax, paths, and terms for this repo are in the profile.

- **One H1 per file.** Most generators derive the page title from it.
- **Admonitions, used sparingly** — a *note* clarifies, a *tip* shortcuts, a *caution* warns of a real consequence. If half the page is callouts, none of them land. (Syntax: profile.)
- **Group parallel multi-language code in tabs** in a fixed language order. (Syntax + order: profile.)
- **Tables for structured facts**, capped at ~5 columns; bold the key term in the first column. Use prose for narrative — don't tabularize a story.
- **End with a "Related docs" footer** of 2–5 sibling links. Assume the reader arrived from search; always give them a next step. Use relative links.
- **Diagrams carry alt text**; reuse existing ones before drawing new. (Location: profile.)
- **Stay current; no history.** Don't document old-version migration rationale or "why the legacy behavior existed." Describe how it works now.
- **Preserve product/brand terms exactly** — don't paraphrase them. (List: profile.)
- **If the project ships more than one language**, keep all versions structurally identical and edit them in the same change. (Languages + paths: profile.)

## What must NOT appear

Docs most often lose readers by *including* things that don't belong on the page — not by leaving things out. This is the most common failure mode. Before shipping a page, hunt these down and cut or relocate them.

1. **Internal mechanics on a user or feature page.** Storage paths, config keys, tool/function schemas, state-machine enums, wire formats, class names. A feature page explains *what / why / when*; the instant one of these appears, replace it with a link to the reference page. *Smell: a "what is X" page whose first table exposes an internal storage path and data model the reader doesn't need.*

2. **Architecture explained before the reader needs it.** Don't introduce internal subsystem names or "how it's built" in onboarding or a feature intro. Establish what it does and why first; defer the internals to the developer section and link. *Smell: a getting-started page that lectures on the execution engine right after the user's first action.*

3. **Register whiplash within one page.** Swinging from marketing voice to spec voice leaves everyone unsure who the page is for. One audience per page; if two are genuinely needed, split and link.

4. **Any term used before it's defined.** Internal nouns, acronyms, and load-bearing rules relied on before — or without ever — being explained. Define on first use or link to the definition, especially on the onboarding path.

5. **Historical, migration, or compatibility rationale.** "previously…", "this used to…", "kept for compatibility", "older data isn't migrated". The reader can't act on it — it's changelog material. Describe only current, observable behavior.

6. **Internal-only and spec-voice content.** Maintainer-only env flags, internal TODOs, and requirement phrasing ("the screen **should** open settings…"). Reference docs describe how the product behaves *to a consumer*, not how it ought to behave *to its own developers*.

7. **Duplicated content that drifts.** The same comparison table or value-prop copied across pages — worse, with different labels in each — confuses instead of reinforcing. Single-source it on one page and link from the rest.

8. **Unnatural, translated-feeling phrasing.** Quote-as-subject sentences, actor-hidden noun piles, and long enumerations crammed into one sentence where a list would read instantly. Read it aloud — if you wouldn't say it, rewrite it.

9. **Load-bearing rules left implicit.** If a rule matters, state it once, plainly, as a rule — don't make the reader reverse-engineer it by diffing examples across pages.

10. **Troubleshooting, FAQ, or Q&A sections.** A small problem that keeps recurring means the guidance above it is too thin — strengthen that guidance so the problem doesn't arise, instead of bolting on an error→fix entry. A genuine bug belongs in the issue tracker, where it can be tracked and fixed, not frozen into a doc that quietly goes stale. When you remove a troubleshooting block, fold any load-bearing hint into the relevant step as positive guidance and cut the rest.

Worked before/after examples for each are in `references/style-and-mechanics.md` (§10).

## Quality checklist

Before you call a page done:

- [ ] Audience and purpose are unambiguous, and the page does only that one job.
- [ ] Voice matches the audience table (warm-user vs neutral-developer).
- [ ] One H1; headings are descriptive and in sentence case (see mechanics ref).
- [ ] Code examples are runnable and minimal for the audience; multi-language uses the project's tab syntax.
- [ ] Admonitions earn their place; no callout spam.
- [ ] "Related docs" footer present with relative links.
- [ ] No troubleshooting / FAQ / Q&A block — guidance gaps fixed inline, genuine bugs left to the issue tracker.
- [ ] Nothing from "What must NOT appear" is present — no internal mechanics on user pages, no premature architecture, no history/migration notes, no internal flags or spec-voice "should", no drifting duplicates, no undefined jargon.
- [ ] Product terms preserved; all project-profile conventions followed (languages in sync, callout/code syntax, asset location).
- [ ] New page placed where its audience already lives — if unsure, ask before creating it.

## Reference files

- `references/project-profile.md` — the project-specific profile (framework, sections, languages, syntax, terms). Read first; replace this one file to reuse the skill elsewhere.
- `references/page-templates.md` — copy-paste skeletons for each archetype. Read when starting a new page.
- `references/style-and-mechanics.md` — detailed voice, grammar, headings, links, images, multi-language sync, and the anti-pattern before/afters. Read before any substantial writing or editing pass.
