# Page Templates

Copy the skeleton that matches the page's audience × purpose (see SKILL.md), then fill it in. These are portable structures. The concrete syntax they use — admonitions (`> [!NOTE]`), multi-language code tabs (`::: code-group`), asset paths, and the "Related docs" footer — follows this repo's conventions in `references/project-profile.md`; swap that syntax if you adapt the skill elsewhere. If the project is multilingual, mirror every page in each language with identical structure.

## Table of contents

1. Quickstart / Getting Started — *end user × tutorial*
2. Feature overview — *end user × explanation (+ light how-to)*
3. How-to / task guide — *either audience × how-to*
4. Concept / architecture explainer — *developer × explanation*
5. Reference (config / CLI / protocol / SDK) — *developer × reference*

*(There is intentionally no troubleshooting / FAQ archetype — see SKILL.md "What must NOT appear" #10.)*

---

## 1. Quickstart / Getting Started — end user × tutorial

Goal: one happy path to a first visible result, fast. No branching, no internals, no "you could also." Defer choices and auth to the moment they're needed.

```markdown
# <Product / Feature> Getting Started

One sentence on who this is for and what they'll have working by the end.

## Quick Start

### 1. <First concrete action>

Imperative instruction. Show the exact command and what they should see.

\`\`\`bash
<command>
\`\`\`

### 2. <Next action>

Keep steps short and ordered. One outcome per step. Lead with the recommended path; keep advanced/manual options as a clearly-labeled fallback below it.

## Next steps

- Link onward to the next thing to try, not everything at once.

## Related docs

- [<next logical page>](./...)
```

Rules: lead with the outcome; minimize explanation (link to a concept page for "why"); every step ends in something the reader can see. Don't explain the architecture here — link to it.

---

## 2. Feature overview — end user × explanation (+ light how-to)

Goal: explain a capability and let the reader use it, without turning into a reference. Friendly, concept-first, one diagram if it helps. Hand wire-level detail off to a developer reference page.

```markdown
# <Feature Name>

Plain-language paragraph: what it does for the user and why it's worth using. Lead with value, not architecture.

## Key Concepts

| Concept | Meaning |
|---|---|
| **<Term>** | One-line definition in plain language. |

## How it works

Short narrative or a small diagram. Keep it conceptual.

![<alt text>](/feature-topology.svg)

> [!TIP]
> A genuinely useful shortcut or default worth surfacing.

## Using it

Task-first steps or a short example of the common path.

## Related docs

- [<deeper developer reference>](../...)
- [<sibling feature>](./...)
```

Rules: if you find yourself writing a multi-column field table of internals, stop and link to the reference page instead.

---

## 3. How-to / task guide — either audience × how-to

Goal: get a competent reader through one real task. Action only — no teaching detours. Match voice to the audience (warm for user-facing, neutral for developer).

```markdown
# <Do the specific task>

One sentence on the goal and the prerequisite state ("Assumes you have … already set up").

## Steps

### 1. <Action>

\`\`\`bash
# the actual command
\`\`\`

### 2. <Action>

Direct, ordered, no digression. Note the one gotcha inline only if it blocks success.

> [!CAUTION]
> Only when a step has a real, irreversible consequence.

## Verify

How the reader confirms it worked.

## Related docs

- [...](./...)
```

Rules: a how-to solves a problem, not "operate feature X." Title it by the goal ("Connect a remote server"), not the tool.

---

## 4. Concept / architecture explainer — developer × explanation

Goal: build understanding. Name the audience up front, take the wider view, explain trade-offs. Neutral and precise.

```markdown
# <Concept / Architecture Area> Overview

One paragraph stating scope and audience explicitly — e.g. "This page targets integrators and contributors; it explains the boundaries that matter for extension and troubleshooting."

![<alt text>](/architecture-topology.svg)

## <Core structure>

Define the moving parts as a table, then discuss how they relate.

| Type | Description | Examples |
|---|---|---|
| **<Module/Concept>** | What it is | ... |

> [!NOTE]
> A non-obvious clarification that prevents a wrong mental model.

## <Trade-off / behavior section>

Discursive prose: why it's built this way, what it implies for callers. Link to the reference page for exact options.

## Related docs

- [<protocol/reference page>](../...)
- [<configuration>](../...)
```

Rules: explanation may discuss alternatives and reasoning — the one place "why" belongs. Don't bury step-by-step instructions here; link to the how-to.

---

## 5. Reference (config / CLI / protocol / SDK) — developer × reference

Goal: complete, austere, authoritative. Structure mirrors the product. No motivation, no tutorial. Every option present and unambiguous.

```markdown
# <Component> Reference

One line on what this documents and where it applies.

## <Command / Endpoint / Method>

Short gloss of purpose, then the exact contract.

\`\`\`bash
<command> --flag <value>
\`\`\`

| Option | Description | Default |
|---|---|---|
| `--flag <value>` | ... | ... |

### Multi-language (SDK)

::: code-group

\`\`\`ts [TypeScript]
// example
\`\`\`

\`\`\`csharp [.NET]
// equivalent
\`\`\`

\`\`\`python [Python]
# equivalent
\`\`\`

:::

> [!CAUTION]
> Security or data consequence stated plainly.

## Related docs

- [<conceptual overview of this area>](../...)
```

Rules: keep language tabs parallel — same steps, same order, every language. Tag anything unstable. The reference describes; it does not persuade. State load-bearing rules explicitly, not only by example.

---

## A note on troubleshooting

This skill intentionally ships **no** troubleshooting / FAQ archetype. A recurring small problem is a signal to strengthen the guidance on the relevant page; a genuine bug belongs in the issue tracker. See SKILL.md "What must NOT appear" #10.
