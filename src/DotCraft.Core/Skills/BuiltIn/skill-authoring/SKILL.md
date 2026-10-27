---
name: skill-authoring
description: "Use when the user asks to create, update, or restructure a DotCraft skill."
tools: ReadFile, WriteFile, EditFile, Exec
---

# Skill Authoring

A skill is a directory with a `SKILL.md` and optional supporting files. Create and change skills with `ReadFile`, `WriteFile`, and `EditFile`, then check the result with `dotcraft skill verify`.

Write a skill for a task type that recurs: when to use it, the exact steps, the pitfalls, and how to confirm success. Keep simple answers and preferences out of skills.

## Where the skill lives

Create a new skill at `.craft/skills/<name>/` in the workspace unless the user names another place. A skill in the user skill root is visible to every workspace, and one in a `.agents/skills` directory, in the workspace or the home folder, is also read by other agent products, so put it there only when the user asks.

Before editing an existing skill, check where it lives:

- A workspace skill under `.craft/skills/<name>/` can be edited in place.
- A built-in skill (its directory holds a `.builtin` marker) is overwritten when DotCraft updates. To keep an edit, delete the `.builtin` marker, which turns it into a workspace skill that no longer updates. Tell the user when you do this.
- A plugin skill is replaced when its plugin updates, and a user or shared skill affects other workspaces. To adapt one for this workspace only, copy its directory to `.craft/skills/<name>/` with the same name and edit the copy.

## SKILL.md

Start the file at byte 0 with frontmatter:

```yaml
---
name: my-skill
description: Use when <the task that should select this skill>.
---
```

- `name` matches the directory name: letters, digits, hyphens, dots, or underscores, at most 64 characters.
- `description` says when to use the skill, in at most 1,024 characters. It is all the agent sees before loading the skill, so name the task, not the current conversation.
- The body is the workflow. Keep it focused on triggers, steps, pitfalls, and verification.

## Supporting files

Put helpers in `scripts/`, long reference material in `references/`, and templates or examples in `assets/`. Refer to them from `SKILL.md` by paths relative to the skill directory, such as `scripts/check.py`, and say when to read or run each one. Keep each file under 1 MiB.

## Verify

```powershell
dotcraft skill verify --candidate "<skill-dir>" --json
```

Fix every reported error. Verification checks structure and limits only, so also run any script you added.

A new or renamed skill can be loaded with `SkillView` right away, but it joins the skills list only after the user runs `/reload-skills` or starts a new conversation. Say so when you finish.

## Pitfalls

- A broad skill that covers a whole domain. Split it by trigger and workflow.
- Missing commands, paths, or checks. A later run needs concrete instructions.
- Absolute paths to the skill's own files. They break when the skill is copied or moved.
