# Security & Sandbox

DotCraft keeps the agent inside guardrails you control, across four layers: a **file blacklist**, the **workspace boundary**, **tool capability switches**, and **sandbox isolation**. Most local projects only need the defaults plus a few sensitive paths; public deployments and external channel integrations should follow the strict deployment checklist.

## Default Safety Baseline

When a workspace is created:

- File and shell operations outside the workspace require approval.
- The blacklist starts empty, so add credential and secret directories that matter on your machine.
- Built-in tools are available unless you narrow the tool surface.
- Sandbox isolation is off until you explicitly enable it.

For exact field names, defaults, and JSON examples, see [Tools, Security, and Sandbox](../../developing/configuration#tools-security-and-sandbox).

## File Blacklist

The blacklist lists paths the agent must not access. It is enforced across CLI, Desktop, external channels, and automations.

Blacklist behavior:

- File reads, writes, edits, grep, and find operations on blacklisted paths are denied.
- Shell commands referencing blacklisted paths are denied.
- The blacklist takes precedence over workspace-boundary approvals.
- Absolute paths and home-directory expansion are supported; subpaths are also checked.

## Workspace Boundary

DotCraft analyzes paths before executing shell commands, covering Unix absolute paths, home-directory paths, environment variables, Windows drive-letter paths, UNC paths, and common security device paths.

If a command resolves outside the workspace, DotCraft either denies it or asks the active interaction source for approval, depending on the workspace policy. File tools use the same expansion rules so file and shell behavior stay consistent.

## Tool Capability Switches

Tool policies control which built-in tools are visible, whether outside-workspace file and shell actions need approval, how large file/web responses may be, and whether LSP or sandbox tools are enabled.

Use the configuration reference when you need a precise allow-list, web-search provider, timeout, output limit, or sandbox setting: [Tools, Security, and Sandbox](../../developing/configuration#tools-security-and-sandbox).

## Hooks

Hooks let DotCraft run external commands during lifecycle events. They are useful for formatting, auditing, notifications, environment checks, and security guards. Start with an observing Hook, then add blocking Hooks once behavior is clear.

Hook configuration, lifecycle events, stdin payloads, matcher rules, exit-code behavior, and examples live in [Automations, Goals, and Hooks](../../developing/configuration#automations-goals-and-hooks).

Best practices:

- Keep Hook scripts small; put complex logic in project scripts.
- Make blocking Hooks print clear error messages.
- Do not commit secrets; prefer environment variables or global config.
- Use workspace-relative command paths to avoid cwd differences across entry points.

## Sandbox (OpenSandbox)

[OpenSandbox](https://github.com/alibaba/OpenSandbox) isolates Shell and File tool execution inside a Docker container. It is useful when a workspace is exposed through a bot, a shared server, or an untrusted task queue.

OpenSandbox service prerequisites and all sandbox fields are in [Tools, Security, and Sandbox](../../developing/configuration#tools-security-and-sandbox).

## Strict Deployment Checklist

When DotCraft is exposed through external channels or the public internet, enable these together:

| Area | Recommendation |
|---|---|
| Workspace boundary | Require approval for outside-workspace file and shell actions |
| Blacklist | Deny secret and credential directories |
| Tool surface | Keep only the tools the deployment needs |
| AppServer | Use a strong WebSocket token for remote access |
| Sandbox | Enable OpenSandbox when further isolation is needed |
| SubAgents | Keep recursion bounded unless you explicitly need it |

## Scenarios

| Scenario | Recommendation |
|---|---|
| Personal local project | Keep outside-workspace approvals; add SSH / cloud credentials / password manager dirs to blacklist |
| Team shared workspace | Put security policy in workspace `.craft/config.json` so every entry enforces it |
| External channel or bot | Approvals on, restricted tools, strong tokens |
| Automation tasks | Per-task sandbox or tightened tool surface |

## Related docs

- [Observability](./observability) — view approval and block records in Dashboard
- [SubAgents](../agent-system/subagents) — bound delegated work with role tool policies
- [Samples](../../resources/samples) — Hooks templates
- [Configuration Reference](../../developing/configuration) — complete security and tool fields
