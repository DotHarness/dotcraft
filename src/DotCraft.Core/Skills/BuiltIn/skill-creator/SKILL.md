---
name: skill-creator
description: "Create, update, or restructure a DotCraft skill, including turning a workflow from this conversation into a reusable skill or fixing one that misses a step."
---

# Skill Creator

A skill is a directory with a `SKILL.md` and optional supporting files. Write one for a task that recurs and that a later run would otherwise handle worse, because it needs non-obvious steps, project knowledge, or checks that are easy to miss. Keep one-off answers and preferences out of skills.

Create and change skills with `ReadFile`, `WriteFile`, and `EditFile`.

## Start from what is known

When the conversation already shows the workflow, start from it: the steps taken, the tools used, the corrections the user made, and what counted as success. Ask only about gaps that would change the skill, such as which requests should select it.

## Where the skill lives

Create a new skill at `.craft/skills/<name>/` in the workspace unless the user names another place. A skill in `~/.craft/skills/` is visible to every workspace, and one in a `.agents/skills` directory, in the workspace or the home folder, is also read by other agent products, so put it there only when the user asks.

Before editing an existing skill, check where it lives:

- A workspace skill under `.craft/skills/<name>/` can be edited in place.
- A built-in skill (its directory holds a `.builtin` marker) is overwritten when DotCraft updates. To keep an edit, delete the `.builtin` marker, which turns it into a workspace skill that no longer updates. Tell the user when you do this.
- A plugin skill is replaced when its plugin updates, and a user or shared skill affects other workspaces. To adapt one for this workspace only, copy its directory to `.craft/skills/<name>/` with the same name and edit the copy.

An update keeps the skill's name and any metadata the request does not change.

## SKILL.md

Start the file at byte 0 with frontmatter:

```yaml
---
name: my-skill
description: <what the skill does and when to use it>.
---
```

- `name` matches the directory name: letters, digits, hyphens, dots, or underscores, starting with a letter or digit, at most 64 characters.
- `description` states what the skill does and which requests should select it, in at most 1,024 characters. It is all the agent sees before loading the skill, so selection conditions belong here and not in the body. Name the task, not the current conversation, and add an exclusion when a nearby request belongs elsewhere.
- `always: true` loads the instructions into every conversation, so set it only when the user asks.

The body holds what a later run needs and could not work out alone: the outcome, real constraints and their reasons, non-obvious steps, pitfalls, and how to confirm success. Leave out general knowledge. Give exact commands where only one sequence is safe, and describe the goal and decision criteria where several approaches work.

## Supporting files

Put helpers in `scripts/`, reference material in `references/`, and templates or examples in `assets/`. Refer to them from `SKILL.md` by paths relative to the skill directory, such as `scripts/check.py`, and say when to read or run each one. Keep each file under 1 MiB.

Keep the shared workflow in `SKILL.md` and move detail that only one mode needs into its own reference. Add a script when each run would otherwise rewrite the same logic.

When a skill appears in a list for people to browse, such as in a plugin, add `agents/openai.yaml`:

```yaml
interface:
  display_name: "My Skill"
  short_description: "One line about what it does"
  default_prompt: "Use $my-skill to handle a realistic request."
  icon_small: "./assets/my-skill-small.svg"
  icon_large: "./assets/my-skill.svg"
```

A plugin skill can reuse its plugin's icons with paths such as `../../assets/icon.svg`.

## Verify

```powershell
dotcraft skill verify --candidate "<skill-dir>" --json
```

Fix every reported error. If `dotcraft` cannot run, say that verification did not run.

Verification checks structure and limits only. Also run any script you added, and try a new or substantially changed skill on a realistic request with no hints, checking the result against what the user expected. When the description is uncertain, also try a nearby request that should not select it. Use an isolated temporary workspace when the skill has side effects, and ask first when a run would touch live systems or cost significant time or money.

A new or renamed skill can be loaded with `SkillView` right away, but it joins the skills list only after the user runs `/reload-skills` or starts a new conversation. Say so when you finish.

## Pitfalls

- A skill whose triggers share no workflow. Split it. Modes of one workflow stay together, with their detail in references.
- A rule written for one failed example. Generalize it or leave it out, because later runs will face different requests.
- Absolute paths to the skill's own files. They break when the skill is copied or moved.
