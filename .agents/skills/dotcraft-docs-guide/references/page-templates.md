# Page Templates

Choose the skeleton for the page's audience and purpose using [SKILL.md](../SKILL.md). Follow [project-profile.md](project-profile.md) for admonitions, multi-language code tabs, asset paths, and localization. Mirror each documentation-site page in every supported language with the same structure. Custom landing pages preserve their existing structure, and repository READMEs do not use these templates.

Use only sections that serve the page's job. A primary figure, when useful, belongs immediately after the intro and before the first `##` heading; a section-specific figure follows that section's heading. The skeletons omit footers: append the optional footer below only for genuine same-audience next steps. On user pages, link developer references inline only where needed.

## Table of contents

1. Quickstart / Getting Started — *end user × tutorial*
2. Feature overview — *end user × explanation (+ light how-to)*
3. How-to / task guide — *either audience × how-to*
4. Concept / architecture explainer — *developer × explanation*
5. Reference (config / CLI / protocol / SDK) — *developer × reference*

*(There is no standalone catch-all troubleshooting / FAQ archetype. Add concise recovery guidance to the owning page when readers can act on it.)*

---

## 1. Quickstart / Getting Started — end user × tutorial

Goal: one recommended path to a first visible result. Introduce required choices and authentication at the step that needs them; keep internal mechanics in developer references.

```markdown
# Get started with <product or feature>

One sentence on who this is for and what they'll have working by the end.

## Quick start

### 1. <First concrete action>

Imperative instruction. Show the exact command and what they should see.

\`\`\`bash
<command>
\`\`\`

### 2. <Next action>

Keep steps short and ordered. One outcome per step. Lead with the recommended path; keep advanced/manual options as a clearly-labeled fallback below it.
```

Rules: lead with the outcome; every step ends in something the reader can see. Link a developer reference inline only when the reader needs it to complete the task.

---

## 2. Feature overview — end user × explanation (+ light how-to)

Goal: explain the purpose of a capability, when to use it, and how to get a result. Keep the page in the user's language; internal mechanics belong in developer references.

```markdown
# <Feature name>

Plain-language paragraph: what the feature helps the reader accomplish.

![<alt text>](/feature-topology.svg)

## When to use it

Describe concrete situations where the feature helps the reader.

## Use <feature>

1. Open <UI path or entry point>.
2. <Perform the action needed for the result>.

Describe the visible result. Include a short example when it makes the task clearer.
```

Rules: omit the figure when it does not help. Describe actions and outcomes, without explaining internal state transitions, storage, protocols, or fallback machinery. Keep a command, path, or identifier only when the reader must enter or inspect it to complete the task; link to the owning developer reference inline where needed.

---

## 3. How-to / task guide — either audience × how-to

Goal: get a competent reader through one real task. Action only — no teaching detours. Match voice to the audience (warm for user-facing, neutral for developer).

```markdown
# <Do the specific task>

One sentence on the goal. Mention a prerequisite only when the reader must satisfy it before the first step.

## Steps

### 1. <Action>

\`\`\`bash
# the actual command
\`\`\`

### 2. <Action>

Direct, ordered, no digression. Add a gotcha only when it commonly blocks success and requires a different reader action.

> [!CAUTION]
> Only when a step has a real, irreversible consequence.

## Verify (only when success is not already visible)

How the reader confirms it worked.
```

Rules: a how-to solves a problem, not "operate feature X." Title it by the goal ("Connect a remote server"), not the tool. Do not append failure recovery, shutdown, or cleanup sections unless they change what the reader must do or prevent a persistent or safety consequence. When development and packaged environments use different paths, label them directly instead of framing one as an assumption and the other as an exception.

---

## 4. Concept / architecture explainer — developer × explanation

Goal: build understanding. Name the audience up front, take the wider view, explain trade-offs. Neutral and precise.

```markdown
# <Concept or architecture area> overview

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
```

Rules: developer explanations may discuss implementation alternatives and design rationale. Keep step-by-step instructions in the owning how-to and link to it where useful.

---

## 5. Reference (config / CLI / protocol / SDK) — developer × reference

Goal: complete, austere, authoritative. Structure mirrors the product. No motivation, no tutorial. Every option present and unambiguous.

```markdown
# <Component> reference

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

:::

> [!CAUTION]
> Security or data consequence stated plainly.
```

Rules: keep language tabs parallel — same steps and order for every applicable language. Tag anything unstable. The reference describes; it does not persuade. State load-bearing rules explicitly, not only by example.

---

## Optional related docs footer

Append this only when the page has genuine same-audience next steps, usually two or three links with a short reason each. Localize the heading. Omit the entire section when nothing qualifies; a user page's footer never lists architecture, configuration, or other developer references.

```markdown
## Related docs

- [<next task for this audience>](./...) — <why to continue here>.
- [<related page for this audience>](./...) — <what it helps with>.
```

## A note on troubleshooting

Do not create a standalone catch-all troubleshooting or FAQ page. Put concise, source-backed recovery steps for actionable non-bug failures on the page that owns the setup or operation. Report genuine product defects through the issue tracker. See [style-and-mechanics.md](style-and-mechanics.md) §10.
