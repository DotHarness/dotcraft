# Security

DotCraft holds the agent inside four layers of guardrails: a file blacklist, the workspace boundary, and tool capability switches. A personal local project needs the defaults plus a few sensitive paths. Once DotCraft is exposed through external channels or the public internet, work through the strict deployment checklist below.

![DotCraft security guardrails overview](/security-guardrails-overview.svg)

## Default safety baseline

In a freshly created workspace:

- File and shell operations outside the workspace require approval.
- Shell commands that force-delete files or open a URL ask before running. Other commands inside the workspace run without a prompt.
- The blacklist is empty, so add the credential and secret directories that matter on your machine.
- Every built-in tool is available until you narrow the tool surface.

Field names, defaults, and JSON examples for all of these live in [Tools and Security](../../developing/configuration#tools-and-security).

## File blacklist

The blacklist lists paths the agent must never touch. It applies the same way to the CLI, Desktop, external channels, and automations.

Reads, writes, edits, and searches on those paths are denied, and so are shell commands that reference them. The blacklist outranks workspace-boundary approval: a blacklisted path is refused outright rather than sent for approval. Absolute paths and paths starting with `~` both work, and subpaths are covered too.

## Workspace boundary

Before running a shell command, DotCraft expands every path the command references: Unix absolute paths, home-directory paths starting with `~`, environment variables, Windows drive-letter paths, and UNC paths like `\\server\share`.

If a path resolves outside the workspace, DotCraft either denies it or asks the active interaction source for approval, depending on the workspace policy. File tools use the same expansion rules, so file operations and shell commands reach the same verdict.

## Shell commands

Before a command runs, DotCraft resolves which shell will execute it, breaks the script into the commands it contains, and checks each one. A command runs without asking when it stays inside the workspace and matches no rule. It asks first when it force-deletes files or opens a URL, when it references or moves into a path outside the workspace, when it changes directory to somewhere DotCraft can't follow, or when one of your rules says so. A shell name DotCraft doesn't recognize is refused.

Some commands keep running and wait for more input, such as an interactive shell or a language prompt. Whatever the agent types into one of those is checked the same way a new command would be, against the shell that terminal is running and the directory it was last known to be in. Once the terminal moves somewhere DotCraft can't follow, everything typed into it afterwards asks first.

The approval shows the shell, the commands as DotCraft read them, and why it's asking. If the script uses syntax DotCraft can't read in advance, the approval says so and shows the script as a whole.

Each choice remembers a different amount:

- **Allow** runs the command once.
- **Allow for this session** skips the prompt for this exact command, in this directory, for the rest of the conversation.
- **Always allow** writes a rule that allows commands starting with the same words. Dangerous commands and unreadable scripts are an exception: they're remembered exactly, never as a rule.

Rules you write yourself decide a command before any other check. Each rule names the leading words of a command and whether to allow it, ask, or refuse it, so `git push` can always ask and `rm` can always be refused. The field format is in [Tools and Security](../../developing/configuration#tools-and-security).

## Tool capability switches

Tool policies decide which built-in tools the agent can see, whether outside-workspace file and shell actions need approval, how much content file and web responses may return, and whether LSP tools are enabled. When you need a precise allow-list, a web-search provider, a timeout, or an output limit, look up the field in [Tools and Security](../../developing/configuration#tools-and-security).

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
| Workspace boundary | Require approval for outside-workspace file and shell actions |
| Blacklist | Deny secret and credential directories |
| Tool surface | Keep only the tools the deployment needs |
| AppServer | Use a strong random WebSocket token for remote access |
| Subagents | Keep recursive delegation bounded unless you explicitly need it |

## Scenarios

| Scenario | Recommendation |
|---|---|
| Personal local project | Keep outside-workspace approvals, and blacklist SSH, cloud credential, and password manager directories |
| Team shared workspace | Put the security policy in the workspace `.craft/config.json` so every entry point enforces it |
| External channel or bot | Approvals on, tools restricted, strong tokens |
| Automation tasks | Enable the sandbox or tighten the tool surface per task |

## Related docs

- [Observability](./observability) — review approval and block records in Dashboard
- [Subagents](../agent-system/subagents) — bound delegated work with a role's tool policy
