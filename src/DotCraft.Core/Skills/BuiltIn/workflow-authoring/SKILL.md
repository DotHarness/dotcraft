---
name: workflow-authoring
description: Write, edit, review, or debug DotCraft Dynamic Workflow JavaScript. Use when creating or changing an inline or saved Workflow script, checking it for correctness, or diagnosing authoring and runtime-contract failures; do not use merely to run an existing saved Workflow unchanged.
---

# Workflow Authoring

Use DotCraft's current Workflow contract as the authority. Do not copy APIs or metadata shapes from other workflow runtimes.

## Read the matching reference

- For every script-writing or editing task, read [references/runtime.md](references/runtime.md) first.
- For topology selection or a new orchestration design, also read [references/patterns.md](references/patterns.md).
- For review, diagnosis, repair, or resume-sensitive edits, also read [references/review-debug.md](references/review-debug.md).

## Preserve these invariants

- Start with a literal `export const meta` and explicitly return plain JSON data.
- Keep enumeration, identity, ordering, deduplication, bounds, and failure accounting in JavaScript; delegate semantic work to Agents.
- Give every intended Agent operation a stable, unique label and preserve its work ID before filtering results.
- Treat `null` as missing coverage, not as a negative finding or successful check.
- Bound fan-out and loops to the task. Do not invent token or time limits the user did not request.
- Use only APIs documented by this skill and names supplied by the current DotCraft context.
