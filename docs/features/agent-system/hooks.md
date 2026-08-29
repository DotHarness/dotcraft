# Lifecycle Hooks

Hooks let DotCraft run your scripts at key moments in a session, a prompt, or a tool call. The routines your project repeats every time — checking a command before it runs, adding context when a session starts, reviewing the result after a tool finishes — can happen on their own.

![A session timeline of four moments — session starts, before a tool runs, after a tool finishes, turn stops — with your hook scripts from user config, workspace, or a plugin attaching to the moments you pick](/lifecycle-hooks-overview.svg)

## When to use hooks

Hooks are best for small guardrails and repeatable checks that belong near the agent's normal workflow.

| Use case | Good hook moment |
|---|---|
| Warn before risky shell commands | Before a tool runs |
| Add project reminders to a new conversation | When a session starts |
| Format or lint after file edits | After a tool finishes |
| Review a final diff or command output | When a turn stops |
| Notify another system | After a tool or turn finishes |

Keep the script focused. Once the logic grows, move the complicated part into a project script and call that from the hook.

## Where hooks come from

DotCraft discovers hooks from your personal config, the current workspace, and enabled plugins. That way private preferences stay in your own config, team policy goes in the workspace, and reusable hooks arrive with a plugin.

Hooks run local commands, so they need your trust first. A newly discovered hook starts untrusted, and a changed hook goes back to waiting for trust until you confirm it again. Plugin hooks are trusted as one bundle, so you can expand them to see what the plugin declares before you allow the set.

The hook file shape, available events, and worked examples are in the [Configuration Reference](../../developing/configuration#automations-goals-and-hooks).

## Manage hooks in Desktop

Open **Settings → Hooks** to see every discovered hook grouped by source. Expand one to inspect its command, matcher, source file, and trust state. User and workspace hooks can be enabled, disabled, and trusted right here, without editing the source file.

Configuration files remain the source of truth for hook commands. Desktop manages only your enable and trust state.

## Hook safety

Hooks are powerful because they run local commands, and that is where the risk sits too. Start with hooks that only observe and print context, confirm the output looks right, then add blocking behavior. Keep secrets out of workspace files, use environment variables for credentials, and install plugin hooks only from sources you trust.

## Related docs

- [Automations & Goals](./automations) — for work that runs as a whole task on a schedule, rather than at one moment
- [Plugins & Tools](./plugins-tools) — plugins that ship reusable hooks
- [Security & Sandbox](../self-hosted/security) — guardrails for file, shell, and sandbox behavior
