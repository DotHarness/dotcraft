# DotCraft External CLI Subagent Design

| Field | Value |
|-------|-------|
| **Version** | 0.7.8 |
| **Status** | Living |
| **Date** | 2026-09-28 |
| **Parent Specs** | [SubAgent Core](subagents.md), [Session Core](../architecture/session-core.md) |

Purpose: extend the shared SubAgent Core contract for external coding CLIs while preserving DotCraft-owned approval semantics and, when enabled, allowing later turns to continue the same external CLI session without introducing a long-lived REPL process.

## 1. Context

An external SubAgent follows [SubAgent Core](subagents.md) for child identity, tasks, communication and parent ownership. This specification owns external process launch, approval-mode mapping and optional session resume.

## 2. Runtime Model

### 2.1 Runtime Types

Current runtime types:

- `native`
- `cli-oneshot`

### 2.2 Core Principle

DotCraft is the policy owner. Subagents are execution engines.

That means:

- native subagent tool calls must keep DotCraft approval behavior
- external CLI execution mode must be selected from DotCraft approval mode
- cancellation must reliably terminate delegated runtime process trees
- resumable external CLI behavior must still use short-lived subprocess launches owned by DotCraft

## 3. Core Abstractions

### 3.1 `ISubAgentRuntime`

The runtime creates a session handle, executes a task with progress events, cancels owned process trees and disposes the handle. Launch context carries the resolved working directory, mapped approval arguments, parent approval context and optional external `resumeSessionId`. A successful result may return a session id for later tasks.

### 3.2 `SubAgentProfile`

Profiles remain the runtime-specific extension point.

Shared fields:

- `runtime`, `bin`, `args`, `env`
- `workingDirectoryMode`
- `supportsStreaming`, `supportsResume`
- `trustLevel`
- `permissionModeMapping`
- `timeout`

Resume-specific external CLI fields:

- `resumeArgTemplate`
- `resumeSessionIdJsonPath`
- `resumeSessionIdRegex`

When `supportsResume=true`, the profile must define:

- `resumeArgTemplate`
- at least one session-id extractor (`resumeSessionIdJsonPath` or `resumeSessionIdRegex`)

### 3.3 `SubAgentCoordinator`

Coordinator owns orchestration logic only:

- resolve profile and runtime
- resolve current approval mode from the active `IApprovalService`
- look up `permissionModeMapping[mode]` and attach mapped args to the launch context
- optionally resolve a prior external CLI session id from the thread-scoped session store
- propagate `ApprovalContext`, `IApprovalService`, and optional `resumeSessionId` into the runtime
- route event/progress back to DotCraft session pipeline
- persist any newly returned external session id after a successful run
- aggregate final result to main agent

## 4. Execution and Coordination

### 4.1 Native Runtime

Native execution follows [SubAgent Core](subagents.md#41-native-runtime); this specification adds no native runtime policy.

### 4.2 External CLI Runtime

Uses one subprocess per delegated task. DotCraft cannot introspect the external CLI's internal tool calls, so policy is applied at launch:

- base args come from `profile.Args`
- optional resume args come from `resumeArgTemplate`
- mapped args from `permissionModeMapping[mode]` are appended after resume args
- output-file args are appended before the task payload when configured
- subprocess is bound to a platform-specific cleanup primitive so cancellation can terminate the full process tree

### 4.3 Resumable External CLI Sessions

Supported external CLIs may expose a stable non-interactive session/chat/thread id. DotCraft stores that id in thread metadata and reuses it later.

Important boundaries:

- DotCraft still launches a fresh process for every delegated task
- DotCraft does not keep a long-lived child process between turns
- resume is profile-driven and workspace-controlled
- resume is off by default

Matching behavior:

- primary match key: `profile + normalized label + workingDirectory`
- when `label` is absent, DotCraft auto-resumes only if there is exactly one saved candidate for the same `profile + workingDirectory`
- ambiguous no-label cases start a new external session

Persistence behavior:

- session ids are stored in thread metadata
- only successful runs update stored ids
- when a resume call succeeds but returns no new id, DotCraft keeps the previous one
- disabling the workspace switch stops reuse but does not delete stored ids

### 4.4 Built-in Resume Profiles

Built-in defaults:

- `codex-cli`
  - base args: `codex exec --skip-git-repo-check`
  - resume args: `resume {sessionId}`
  - final message: output file
  - session id extraction: regex from stdout JSON lines (`thread_id`)
- `cursor-cli`
  - base args: `cursor-agent -p --output-format json`
  - resume args: `--resume {sessionId}`
  - final message: JSON stdout
  - session id extraction: `session_id`

## 5. Approval and Permission Propagation

### 5.1 Approval Modes

DotCraft exposes three stable modes the subagent layer understands:

| Mode | Origin `IApprovalService` | Intent |
|------|---------------------------|--------|
| `interactive` | `SessionApprovalService` / `ConsoleApprovalService` | Real user present; conservative defaults, prompt on sensitive actions |
| `auto-approve` | `AutoApproveApprovalService` | Headless automation channel; skip prompts, use permissive runtime flags |
| `restricted` | `DenyApprovalService`, unknown or `null` services | DotCraft-side approval needs are denied as tool results; runtime should launch in its most conservative mode |

### 5.2 Native Subagent Propagation

Native approval inheritance follows [SubAgent Core](subagents.md#7-role-tools-and-approval-policy). External CLI policy is translated at launch as defined below.

### 5.3 External CLI Permission Mapping

External CLIs keep their own approval/sandbox semantics. DotCraft translates its own policy into the external CLI's launch flags:

- coordinator resolves the mode with `SubAgentApprovalModeResolver`
- coordinator reads `profile.PermissionModeMapping[mode]` and splits it into args
- mapped args are appended to the subprocess command line before the task payload is delivered
- a missing mapping entry is treated as "no extra args"

This mapping is advisory: DotCraft declares intent and selects the safest available flag set; actual enforcement still depends on the external CLI implementation.

### 5.4 Built-in Profile Defaults

| Profile | `trustLevel` | Notable mapping defaults |
|---------|--------------|--------------------------|
| `native` | `trusted` | N/A |
| `codex-cli` | `prompt` | `interactive` → `--sandbox read-only`; `auto-approve` → `--dangerously-bypass-approvals-and-sandbox`; `restricted` → `--sandbox read-only` |
| `cursor-cli` | `prompt` | `interactive` → `--mode ask --trust --approve-mcps`; `auto-approve` → `--mode auto --trust --approve-mcps`; `restricted` → `--mode ask` |
| `custom-cli-oneshot` | `restricted` | Empty mapping by default |

## 6. Integration with Existing DotCraft Systems

### 6.1 Session/Event Pipeline

External runtime support reuses the existing session event delivery:

- progress mapped to `SubAgentProgress`
- final output returned as standard tool result payload
- cancellation reflected as normal turn cancellation result

### 6.2 Desktop Settings

Desktop exposes one workspace-scoped switch in SubAgents settings:

- `EnableExternalCliSessionResume`
- default `false`
- affects only profiles with `supportsResume=true`

Desktop also exposes resume-specific profile fields for custom external CLI profiles.
