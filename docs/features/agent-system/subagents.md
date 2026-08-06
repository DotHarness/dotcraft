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
- Native SubAgents run on the same generated prompt as their parent; role limits are enforced when a tool is called, not by trimming the prompt.

Full role and profile configuration fields are in [SubAgent and External CLI Profiles](../../developing/configuration#subagent-and-external-cli-profiles).

## Built-in Roles

| Role | Best for | Tool policy |
|---|---|---|
| `default` | General first-level collaboration, summary, local analysis | Disable AgentTools, conservative tool set |
| `worker` | Implementation, validation, file changes | Allow read/write, shell, web; AgentTools still bound by depth |
| `explorer` | Read-only code exploration, research | Read-only exploration + web + non-mutating shell such as `git diff`; disables writes, Plan/Todo, SkillManage, AgentTools |

`worker` has the capability model for recursive delegation, but recursion remains an explicit opt-in through configuration.

## Shell access

A role bounds shell tools twice, and a call must pass both checks.

The allow/deny lists decide whether the shell tool is reachable at all. The role's shell access level then decides what a reachable shell may run: `None` rejects it, `ReadOnly` admits only non-mutating commands and rejects writes to process input, and `Full` adds nothing beyond the lists. A role that omits the level gets `Full`, so a role that already bounded shell through its allow-list keeps that boundary.

Read-only is a property of the command, not of the tool. That is why `explorer` can run `git diff`, `git log`, `git status`, `ls`, and `rg` while `git push` and file writes are rejected — and why a rejection names the command it refused rather than reporting the shell as unavailable. Chained commands are classified segment by segment: `git diff --stat; git log -1` is admitted, `git diff && rm -rf build` is not.

## The shared prompt

A native SubAgent starts from the same generated prompt as its parent thread, down to the byte. Its role text arrives separately, as a message at the start of its conversation rather than a section of the system prompt.

That is what lets a child reuse the prefix its parent already paid the provider to cache: the model sees an identical instruction block and tool list, so only the child's own task is new. Role limits still apply — a denied tool returns the reason when the SubAgent calls it.

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

## Conversation lifecycle

When a native SubAgent receives its own saved conversation, DotCraft keeps it as a child of the main conversation. Its parent relationship, role, runtime choice, and tool limits survive a restart.

Archiving the main conversation also archives its saved SubAgent conversations. Restoring it brings back only child conversations that were still open; conversations you closed explicitly stay archived. Permanently deleting the main conversation also removes its saved child conversations. DotCraft then attempts to clean up their supporting files; any failed cleanup can be retried.

## Related docs

- [Project Workspace](../project-workspace)
- [Security & Sandbox](../self-hosted/security) — bound SubAgent behavior with workspace boundary and sandbox
- [Observability](../self-hosted/observability) — view SubAgent calls and approvals in Dashboard
- [Configuration Reference](../../developing/configuration#subagent-and-external-cli-profiles)
- [Session persistence](../../developing/architecture/session-persistence)
