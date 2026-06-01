# SubAgents

A SubAgent lets the main agent hand off a self-contained task to a focused helper that works in its own context and reports the result back — so the main conversation stays clean. Two things shape a SubAgent:

- `agentRole` — what it's allowed to do: its behavior, tool boundary, and prompt constraints.
- `profile` — which runtime runs it: DotCraft native or an external CLI.

If you only want safe one-level delegation, you usually do not need to change anything. The defaults allow the root agent to spawn a first-level SubAgent and prevent SubAgents from spawning further SubAgents.

## Quick Start

The default behavior is intentionally conservative:

- The root agent can call `SpawnAgent` to create a first-level SubAgent.
- First-level SubAgents are already at the default depth limit and cannot spawn new ones.
- `agentRole` defaults to `default` when omitted.
- Native SubAgents use the lightweight `subagent-light` prompt profile by default.

Full role and profile configuration fields are in [SubAgent and External CLI Profiles](../../developing/configuration#subagent-and-external-cli-profiles).

## Built-in Roles

| Role | Best for | Tool policy |
|---|---|---|
| `default` | General first-level collaboration, summary, local analysis | Disable AgentTools, conservative tool set |
| `worker` | Implementation, validation, file changes | Allow read/write, shell, web; AgentTools still bound by depth |
| `explorer` | Read-only code exploration, research | Only read-only exploration + web; disables writes, shell, Plan/Todo, SkillManage, AgentTools |

`worker` has the capability model for recursive delegation, but recursion remains an explicit opt-in through configuration.

## The Lightweight Prompt

`subagent-light` is the default prompt profile for native session-backed SubAgents. It keeps:

- DotCraft base identity
- Current workspace and environment info
- Role instructions
- Tool capabilities and limits
- `.craft/AGENTS.md`
- Necessary file references and working style

It skips by default:

- Full Memory context
- Long Skill self-learning instructions
- Custom commands summary
- Deferred MCP discovery instructions
- Plan / Todo injection

This makes short-task SubAgents start faster and avoids copying the main thread's long-term context to the child.

## Profile: Choosing a Runtime

| Profile | Description |
|---|---|
| `native` | DotCraft native SubAgent with role-resolved tool filtering |
| `codex-cli` | One-shot external SubAgent backed by Codex CLI |
| `cursor-cli` | One-shot external SubAgent backed by Cursor CLI |
| `custom-cli-oneshot` | Template profile for a configured external CLI |

DotCraft passes role instructions to external CLIs but cannot enforce tool filtering inside the external CLI itself. For strong isolation, prefer `native` and combine it with role allow/deny lists and [Security & Sandbox](../self-hosted/security).

## Using External CLIs as SubAgents

External CLI SubAgents wrap an existing coding-agent CLI as a short-lived process. Compared with `native`, an external CLI usually gives stage-level progress, not per-tool-call detail.

Built-in external profiles support Codex CLI and Cursor CLI. DotCraft can also reuse external CLI sessions when that setting is enabled and the profile supports resume. Matching is conservative: it prefers the same profile, label, and working directory rather than blindly resuming any saved external session.

Custom external CLI profiles, resume extraction, permission forwarding, and vendor headless details are documented in [SubAgent and External CLI Profiles](../../developing/configuration#subagent-and-external-cli-profiles).

## Approval & Permission Forwarding

**Native SubAgents**

- A native SubAgent's internal file and shell tool calls reuse the current session's approval service.
- Approval requests are prefixed with the SubAgent label so users can see where they came from.

**External CLI SubAgents**

- DotCraft does not intercept the CLI's internal tool calls.
- It translates the current approval mode into startup arguments when the profile defines a permission mapping.
- The resume argument is inserted before approval arguments, but DotCraft still decides whether to resume.

## When to Use Which

| Situation | Recommendation |
|---|---|
| Need bounded read-only exploration | `explorer` role on `native` |
| Need implementation help inside the current workspace policy | `worker` role on `native` |
| Need a specific external coding CLI workflow | External CLI profile |
| Need strong tool isolation | Prefer `native` plus allow/deny lists and sandbox |
| Need recurring team behavior | Define a workspace role in configuration |

## Related docs

- [Project Workspace](../project-first)
- [Security & Sandbox](../self-hosted/security) — bound SubAgent behavior with workspace boundary and sandbox
- [Observability](../self-hosted/observability) — view SubAgent calls and approvals in Dashboard
- [Configuration Reference](../../developing/configuration#subagent-and-external-cli-profiles)
