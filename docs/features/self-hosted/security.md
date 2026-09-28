# Security

DotCraft provides file access restrictions, shell command approvals, and tool capability switches. Configure sensitive file paths, command rules, and available tools for your workspace.

## Default safety baseline

In a freshly created workspace:

- File-tool access outside the workspace and shell launches from outside directories require approval.
- Shell commands that force-delete files or open a URL ask before running. Other commands launched inside the workspace run without a prompt unless a rule matches.
- The blacklist is empty, so add the credential and secret directories that matter on your machine.
- Every built-in tool is available until you narrow the tool surface.

Field names, defaults, and JSON examples for all of these live in [Tools and Security](../../developing/configuration#tools-and-security).

## File blacklist

The blacklist lists paths file tools must not access. It applies the same way to the CLI, Desktop, external channels, and automations.

File-tool reads, writes, edits, and searches on those paths are denied. The blacklist outranks workspace-boundary approval: a blacklisted path is refused outright rather than sent for approval. Absolute paths and paths starting with `~` both work, and subpaths are covered too.

## Workspace boundary

File tools check whether their target paths are inside the workspace. Shell checks use the launch directory supplied with the call.

When a file-tool target or shell launch directory is outside the workspace, DotCraft denies it or asks for approval according to policy. Subsequent terminal input uses the same launch-directory approval context.

## Shell commands

Before a command runs, DotCraft checks your command rules, dangerous operations, and the launch directory. Rules can allow, prompt, or refuse a command. Without a matching rule, force-deleting files, opening a URL, or launching outside the workspace triggers approval. An unrecognized shell name is refused. Command text, including Windows options such as `/s` and `/t`, is passed to the selected shell.

Some commands keep running and wait for more input, such as an interactive shell or a language prompt. Agent input still passes command-rule and dangerous-operation checks. Approvals use the terminal's shell and launch directory as context.

The approval shows the shell, the commands as DotCraft read them, and why it's asking. If the script uses syntax DotCraft can't read in advance, the approval says so and shows the script as a whole.

Each choice remembers a different amount:

- **Allow** runs the command once.
- **Allow for this session** skips the prompt for this exact command, in this directory, for the rest of the conversation.
- **Always allow** writes a rule that allows commands starting with the same words. Dangerous commands and unreadable scripts are an exception: they're remembered exactly, never as a rule.

Rules you write yourself decide a command before any other check. Each rule names the leading words of a command and whether to allow it, ask, or refuse it, so `git push` can always ask and `rm` can always be refused. The field format is in [Tools and Security](../../developing/configuration#tools-and-security).

## Tool capability switches

Tool policies decide which built-in tools the agent can see, whether file-tool access and shell launches outside the workspace need approval, how much content file and web responses may return, and whether LSP tools are enabled. When you need a precise allow-list, a web-search provider, a timeout, or an output limit, look up the field in [Tools and Security](../../developing/configuration#tools-and-security).

## Hooks

Hooks turn security checks into checkpoints on the session lifecycle: inspect a command before it runs, review edits after a tool call, or stop before risky work and wait for your go-ahead. For the concept, see [Lifecycle Hooks](../agent-system/hooks). For events, matcher rules, and exit-code behavior, see the [configuration reference](../../developing/configuration#automations-goals-and-hooks).

When writing a Hook:

- Keep the script small and put complex logic in your project's own scripts.
- Make a blocking Hook print a clear error, or all you see is an action being refused.
- Never put secrets in a Hook — use environment variables or global config.
- Write command paths relative to the workspace — cwd differs across entry points.

## Strict deployment checklist

When DotCraft is exposed through external channels or the public internet, enable these together:

| Area | Recommendation |
|---|---|
| Workspace boundary | Require approval for file-tool access and shell launches outside the workspace |
| Blacklist | Deny file-tool access to secret and credential directories |
| Tool surface | Keep only the tools the deployment needs |
| AppServer | Use a strong random WebSocket token for remote access |
| Subagents | Keep recursive delegation bounded unless you explicitly need it |

## Scenarios

| Scenario | Recommendation |
|---|---|
| Personal local project | Keep outside-workspace approvals, and blacklist SSH, cloud credential, and password manager directories |
| Team shared workspace | Put the security policy in the workspace `.craft/config.json` so every entry point enforces it |
| External channel or bot | Approvals on, tools restricted, strong tokens |
| Automation tasks | Configure available tools and command rules per task |

## Related docs

- [Observability](./observability) — review approval and block records in Dashboard
- [Subagents](../agent-system/subagents) — bound delegated work with a role's tool policy
