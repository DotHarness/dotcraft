---
name: skill-authoring
description: "Use when authoring or maintaining DotCraft workspace skills via SkillManage."
tools: SkillManage
---

# Skill Authoring

## Overview

Skills are procedural memory: reusable, narrow instructions for task types that are likely to recur. Load this skill when you need to create, rewrite, or patch a workspace skill with `SkillManage`.

Do not create skills for simple one-off answers. A good skill teaches when to use it, what exact steps to follow, what pitfalls to avoid, and how to verify the result.

## When To Use

Use `SkillManage` when:

- A complex task succeeded after several tool calls and produced a reusable workflow.
- A tricky error was fixed and the fix is likely to recur.
- The user explicitly asks you to remember a procedure.
- A user correction revealed a better stable workflow.
- An existing skill was used and found to be stale, incomplete, wrong, or missing a pitfall.

Do not use it for:

- Simple questions, one-off edits, or preferences that belong in memory.
- Speculative workflows that have not been exercised.
- Modifying built-in or user-global skills directly. Create or update a workspace skill instead.

## SkillManage Actions

| Action | Required Parameters | Use For | Example |
|---|---|---|---|
| `create` | `name`, `content` | New reusable workspace skill | `SkillManage(action: "create", name: "debug-api", content: "<full SKILL.md>")` |
| `patch` | `name`, `oldString`, `newString` | Targeted fixes to `SKILL.md` or a supporting file | `SkillManage(action: "patch", name: "debug-api", oldString: "old", newString: "new")` |
| `edit` | `name`, `content` | Full rewrite after reading the current skill | `SkillManage(action: "edit", name: "debug-api", content: "<full updated SKILL.md>")` |
| `write_file` | `name`, `filePath`, `fileContent` | Add or replace supporting files | `SkillManage(action: "write_file", name: "debug-api", filePath: "scripts/check.sh", fileContent: "...")` |
| `remove_file` | `name`, `filePath` | Remove a supporting file | `SkillManage(action: "remove_file", name: "debug-api", filePath: "assets/example.json")` |
| `delete` | `name` | Remove obsolete or harmful workspace skills, only when enabled | `SkillManage(action: "delete", name: "old-skill")` |

Prefer `patch` for small changes. Use `edit` only for major overhauls after reading the current skill.

## Required Frontmatter

Every `SKILL.md` created through `SkillManage` must start with YAML frontmatter:

```yaml
---
name: my-skill
description: One-sentence trigger description
version: 0.1.0
---
```

Rules:

- The file must start with `---` with no leading whitespace.
- `name` must match the `name` parameter.
- `description` should describe the trigger class, not the current task.
- The body must be non-empty and actionable.

## SKILL.md Structure

Use this structure unless a skill has a strong reason to differ:

```markdown
# Title

## Overview
What this skill is for and why it exists.

## When To Use
- Concrete trigger conditions.
- Counter-triggers if useful.

## Workflow
1. Exact steps, commands, files, APIs, or checks.
2. Keep steps specific enough to execute later.

## Common Pitfalls
- Known mistakes and fixes.

## Verification
- How to confirm the workflow succeeded.
```

## Supporting Files

Supporting files must stay inside the skill directory under one of:

- `scripts/` for helper scripts.
- `assets/` for static assets.

Use `write_file` for supporting files. Use `patch` with `filePath` for targeted edits to supporting files. Absolute paths and `..` traversal are rejected.

## Size Limits

- `SkillManage` enforces size limits for `SKILL.md` and supporting files.
- If a skill is growing too large, keep `SKILL.md` focused on triggers, workflow, pitfalls, and verification. Put executable helpers in `scripts/` and static examples in `assets/`.

## Common Pitfalls

1. Creating a skill before the workflow is proven. Wait until the task produced a reusable procedure.
2. Writing a broad skill that tries to cover an entire domain. Split by trigger and workflow.
3. Omitting exact commands, paths, or verification steps. Future use needs concrete instructions.
4. Editing built-in or user-global skills directly. Use a workspace skill.
5. Expecting a newly created skill to be available immediately in the current prompt. It is picked up on the next turn or session refresh.
6. Using `edit` for a tiny correction. Prefer `patch` with enough context in `oldString`.

## Verification

Before finishing, confirm the frontmatter starts at byte 0 and includes `name`, `description`, and `version`; `name` matches the directory and the `SkillManage` request; the description explains when to use the skill; the body includes workflow, pitfalls, and verification guidance; supporting files stay under `scripts/` or `assets/`; and the skill is narrow enough to be reused without confusion.
