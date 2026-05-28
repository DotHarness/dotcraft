---
description: Create lifecycle hooks (hooks.json) for the workspace based on user requirements
---

Read the `create-hooks` skill to understand the DotCraft hooks system, then create hooks for this workspace.

User requirements: $ARGUMENTS

Follow this process:

1. If no requirements are provided, ask the user what kind of hooks they need (security guard, auto-format, logging, notification, prompt filter, etc.)
2. Read the `create-hooks` skill file from the skills directory to load the full hooks reference
3. Determine the target lifecycle event(s) and matcher patterns
4. Check if `.craft/hooks.json` already exists — if so, read it and merge new hooks into the existing config
5. Generate the `hooks.json` config with appropriate commands for the current OS platform
6. For complex logic, create script files in `.craft/hooks/` and reference them from the config
7. Verify that hooks are enabled in `config.json` (not explicitly disabled)
