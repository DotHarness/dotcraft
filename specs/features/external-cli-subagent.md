# DotCraft External CLI Subagent Design

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Parent Specs** | [SubAgent Core](subagents.md), [Session Core](../architecture/session-core.md) |

Purpose: extend the shared SubAgent Core contract for external coding CLIs while preserving DotCraft-owned approval semantics and, when enabled, allowing later tasks to continue the same external CLI session without a long-lived process.

## 1. Context

An external SubAgent follows [SubAgent Core](subagents.md) for child identity, tasks, communication, native runtime behavior, and parent ownership. This specification owns external process launch, approval-mode mapping, and optional session resume.

DotCraft is the policy owner; subagents are execution engines. External CLI execution mode is selected from DotCraft approval mode, cancellation terminates the delegated process tree, and resumable sessions still use short-lived subprocesses owned by DotCraft.

Runtime types are `native` and `cli-oneshot`.

## 2. Profiles

Profiles are the runtime-specific extension point and part of workspace configuration.

| Fields | Purpose |
|--------|---------|
| `runtime`, `bin`, `args`, `env` | Process selection and base command line. |
| `workingDirectoryMode` | How the child working directory is chosen. |
| `supportsStreaming`, `supportsResume` | Advertised runtime capabilities. |
| `trustLevel` | Profile trust tier. |
| `permissionModeMapping` | Extra launch args per approval mode (section 4). |
| `timeout` | Per-task execution limit. |
| `resumeArgTemplate`, `resumeSessionIdJsonPath`, `resumeSessionIdRegex` | Session resume (section 3.2). |

When `supportsResume=true`, the profile must define `resumeArgTemplate` and at least one session-id extractor.

## 3. Execution

### 3.1 One process per task

Each delegated task runs one subprocess. DotCraft cannot introspect the CLI's internal tool calls, so policy is applied at launch. The command line is assembled in this order: `args`, resume args from `resumeArgTemplate`, mapped approval args, configured output-file args, then the task payload. The subprocess is bound to a platform cleanup primitive so cancellation terminates its full process tree.

Progress maps to SubAgent progress, the final output returns as the standard tool result, and cancellation is reported as normal Turn cancellation.

### 3.2 Resumable sessions

Supported CLIs may expose a stable non-interactive session id. DotCraft stores it in thread metadata and reuses it for later tasks. A fresh process is still launched for every task. Resume is profile-driven and gated by the workspace switch `EnableExternalCliSessionResume` (default `false`), which affects only profiles with `supportsResume=true`.

Matching:

- primary key: `profile + normalized label + workingDirectory`;
- without a `label`, DotCraft resumes only when exactly one saved candidate matches `profile + workingDirectory`; ambiguous cases start a new session.

Persistence:

- only successful runs update stored ids;
- a successful resume that returns no new id keeps the previous one;
- disabling the switch stops reuse but does not delete stored ids.

### 3.3 Built-in resume profiles

| Profile | Base args | Resume args | Final message | Session id |
|---------|-----------|-------------|---------------|------------|
| `codex-cli` | `codex exec --skip-git-repo-check` | `resume {sessionId}` | Output file | Regex over stdout JSON lines (`thread_id`) |
| `cursor-cli` | `cursor-agent -p --output-format json` | `--resume {sessionId}` | JSON stdout | `session_id` |

## 4. Approval and permission mapping

DotCraft resolves one of three approval modes from the active approval service:

| Mode | Origin | Intent |
|------|--------|--------|
| `interactive` | A real user is present to answer approvals. | Conservative defaults; prompt on sensitive actions. |
| `auto-approve` | Headless automation that approves automatically. | Skip prompts; permissive runtime flags. |
| `restricted` | Deny-all, unknown, or absent approval service. | DotCraft-side approvals are denied; the runtime launches in its most conservative mode. |

Native approval inheritance follows [SubAgent Core](subagents.md#7-role-tools-and-approval-policy). For external CLIs, DotCraft reads `permissionModeMapping[mode]`, splits it into args, and appends them before the task payload; a missing entry adds no args. The mapping is advisory: DotCraft selects the safest available flags, but enforcement depends on the external CLI.

Built-in defaults:

| Profile | `trustLevel` | Mapping |
|---------|--------------|---------|
| `native` | `trusted` | N/A |
| `codex-cli` | `prompt` | `interactive` → `--sandbox read-only`; `auto-approve` → `--dangerously-bypass-approvals-and-sandbox`; `restricted` → `--sandbox read-only` |
| `cursor-cli` | `prompt` | `interactive` → `--mode ask --trust --approve-mcps`; `auto-approve` → `--mode auto --trust --approve-mcps`; `restricted` → `--mode ask` |
| `custom-cli-oneshot` | `restricted` | Empty |

Desktop exposes the resume switch and resume-specific fields for custom external CLI profiles in SubAgents settings.
