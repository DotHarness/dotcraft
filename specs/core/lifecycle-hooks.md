# DotCraft Lifecycle Hooks Specification

| Field | Value |
|-------|-------|
| **Version** | 2.0.0 |
| **Status** | Living |
| **Date** | 2026-07-04 |
| **Related Specs** | [Session Core](session-core.md), [Plugin Architecture](../extensions/plugin-architecture.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: define DotCraft lifecycle hooks as a durable runtime contract. Hooks let
user config, workspace config, and enabled plugins run trusted local commands at
well-defined points in a thread, turn, tool call, compaction, or subagent
lifecycle.

---

## 1. Sources And Trust

Hooks can come from:

1. User config: `~/.craft/hooks.json`.
2. Workspace config: `.craft/hooks.json`.
3. Enabled plugin hook files declared by `.craft-plugin/plugin.json`.
4. Reserved managed hooks owned by DotCraft runtime features.

Command-bearing hook files are the source of truth. Clients must not edit hook
commands through hook-management APIs. Clients may only write user-local state:

```json
{
  "Hooks": {
    "Enabled": true,
    "State": {
      "<hook-key>": {
        "Enabled": true,
        "TrustedHash": "sha256:..."
      }
    }
  }
}
```

Config and plugin hooks are unmanaged and do not run until trusted. Installing
or enabling a plugin does not trust its hooks. Any behavior-changing edit changes
the hook hash and returns the hook to `modified` until the user trusts the new
hash. State is stored in user-global config so personal trust and disable choices
are not committed to a workspace.

Runtime execution order is:

1. User config hooks.
2. Workspace config hooks.
3. Enabled plugin hooks.
4. Managed hooks.

Within one source, hooks run in file order, then event group order, then handler
order. A blocking result stops later hooks for that event.

---

## 2. Events

DotCraft recognizes these event names:

| Event | Trigger | Blocking | Context output |
|-------|---------|----------|----------------|
| `SessionStart` | First usable turn for a thread/session. | No | Yes |
| `UserPromptSubmit` | A user prompt is submitted, before prompt assembly and model execution. | Yes | Yes |
| `PrePrompt` | DotCraft-native compatibility event before the assembled user prompt is sent. | Yes | Yes |
| `PreToolUse` | Before a tool executes. | Yes | Yes |
| `PermissionRequest` | Before a permission prompt is shown. | Yes | No |
| `PostToolUse` | After a tool succeeds. | No | Yes |
| `PostToolUseFailure` | After a tool fails. | No | Yes |
| `PreCompact` | Before context compaction. | Yes | Yes |
| `PostCompact` | After context compaction. | No | Yes |
| `SubagentStart` | Before a subagent thread starts. | No | Yes |
| `SubagentStop` | After a subagent thread stops. | No | Yes |
| `Stop` | After the assistant response for a turn, before queued follow-up work starts. | No | Continuation |
| `StopFailure` | After Stop hook execution fails or a Stop-triggered continuation fails. | No | No |

`PrePrompt` is retained for existing DotCraft hooks. New plugin ecosystems should
prefer `UserPromptSubmit` because it runs before baseline capture, tool use, and
prompt assembly.

---

## 3. Hook File Shape

Hook files use this shape:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Bash|Write|Edit",
        "hooks": [
          {
            "type": "command",
            "command": "node \"${DOTCRAFT_PLUGIN_ROOT}/hooks/check.js\"",
            "timeout": 30,
            "if": "Bash(git commit:*)",
            "statusMessage": "Running security review",
            "asyncRewake": true,
            "rewakeMessage": "Review feedback:",
            "rewakeSummary": "Review found issues"
          }
        ]
      }
    ]
  }
}
```

Supported command fields:

| Field | Type | Description |
|-------|------|-------------|
| `type` | string | `command`. Other values are reserved and reported as unsupported diagnostics. |
| `command` | string | Shell command to execute from the workspace root. |
| `timeout` | integer | Timeout in seconds. Defaults to 30 and is clamped to at least 1. |
| `matcher` | string | Group-level tool/event matcher. Empty matches all. |
| `if` | string | Optional command condition such as `Bash(git commit:*)`. |
| `shell` | string | Optional shell override. |
| `statusMessage` | string | Optional user-visible execution label. |
| `environmentVariables` | object | Extra environment variables for the hook process. |
| `async` | boolean | Run without blocking the current action. |
| `asyncRewake` | boolean | If output requests continuation, enqueue a hook-origin follow-up turn. |
| `rewakeMessage` | string | Prefix for model-visible continuation feedback. |
| `rewakeSummary` | string | Short user-visible continuation summary. |
| `once` | boolean | Reserved for future per-session de-duplication. |

Reserved handler types are `prompt`, `agent`, and `http`. DotCraft may list them
with diagnostics but does not execute them until a later spec version.

---

## 4. Matching

For tool events, `matcher` is evaluated against the DotCraft tool name and its
standard aliases. Empty matcher matches all tool events. Invalid matcher syntax
does not match.

`if` supports a portable subset:

```text
ToolAlias(pattern)
```

`ToolAlias` is matched against the canonical tool alias set. `pattern` is a
case-insensitive wildcard expression matched against the best-known string
payload for that tool. For shell tools, the payload is the command string. For
file tools, it is the path and relevant edit/write text. If the condition cannot
be evaluated, the hook does not run.

DotCraft must provide stable aliases for common tools:

| DotCraft capability | Required aliases |
|---------------------|------------------|
| Shell execution | `Bash`, `Shell`, `Exec` |
| Full file write | `Write`, `WriteFile` |
| Search/replace edit | `Edit`, `EditFile` |
| Multi-edit adapters | `MultiEdit` |
| Notebook edit adapters | `NotebookEdit` |

---

## 5. Hook Input

Command hooks receive one JSON object on stdin. DotCraft emits both camelCase
fields and snake_case fields for compatibility. Unknown fields must be ignored.

Common fields:

| Field | Description |
|-------|-------------|
| `sessionId` / `session_id` | Thread/session id. |
| `turnId` / `turn_id` | Current turn id when available. |
| `cwd` | Workspace root. |
| `hookEventName` / `hook_event_name` | Event name. |
| `source` | Source kind: `user`, `workspace`, `plugin`, or `managed`. |
| `pluginId` / `plugin_id` | Owning plugin id when available. |
| `transcriptPath` / `transcript_path` | Reserved transcript path when available. |
| `model` | Current model id when available. |
| `permissionMode` / `permission_mode` | Current approval/permission mode when available. |

Prompt fields:

| Field | Description |
|-------|-------------|
| `prompt` | User prompt text. |
| `runtimeContext` / `runtime_context` | Runtime context already known at the event point. |

Tool fields:

| Field | Description |
|-------|-------------|
| `toolName` / `tool_name` | Canonical hook-facing tool alias. |
| `toolArgs` / `tool_args` | DotCraft-native tool arguments. |
| `toolInput` / `tool_input` | Portable tool input shape. |
| `toolResult` / `tool_result` | Tool result summary for successful tool events. |
| `toolResponse` / `tool_response` | Alias for tool result. |
| `error` | Failure message for failed tool events. |

Stop fields:

| Field | Description |
|-------|-------------|
| `response` | Assistant response text. |
| `lastAssistantMessage` / `last_assistant_message` | Alias for assistant response text. |
| `stopHookActive` / `stop_hook_active` | True for hook-origin continuation turns that must not recursively rewake. |

---

## 6. Hook Output

Hook stdout can be plain text or JSON.

Plain text output is treated as additional context for context-capable events.
For `Stop`, plain text becomes continuation feedback only when the hook is marked
for rewake.

JSON output supports:

| Field | Description |
|-------|-------------|
| `continue` | `false` suppresses continuation. |
| `decision` | `block` requests blocking or continuation, depending on event. |
| `reason` | Human-readable block or continuation reason. |
| `stopReason` | Fallback block/stop reason. |
| `suppressOutput` | Hint that clients should not surface raw output. |
| `systemMessage` | User-visible system message. |
| `rewakeSummary` | Per-run summary override for rewake notifications. |
| `hookSpecificOutput.additionalContext` | Model-visible additional context. |

Exit code semantics:

| Exit code | Meaning |
|-----------|---------|
| `0` | Continue. Parse stdout for optional context or continuation. |
| `2` | Block a blocking event, or request continuation for rewake-capable Stop/PostToolUse hooks. |
| Other | Fail open; record warning/diagnostics. |

Blocking events return an error to the current action. Non-blocking events fail
open. `asyncRewake` hooks may enqueue a new hook-origin turn instead of blocking
the current turn.

---

## 7. Plugin Variables And Environment

Plugin hook commands may use:

| Variable | Description |
|----------|-------------|
| `${DOTCRAFT_PLUGIN_ROOT}` | Absolute plugin root. |
| `${DOTCRAFT_PLUGIN_DATA}` | User-local plugin data directory. |

DotCraft injects the same names as environment variables. Compatibility aliases
may be injected for imported plugin ecosystems, but DotCraft-authored examples
must use `DOTCRAFT_*` names.

Plugin data paths are user-local and must not be committed into workspace config.
Hook examples must not contain real machine paths.

---

## 8. AppServer And Desktop Projection

AppServer exposes `capabilities.hooksManagement` and the methods:

- `hooks/list`
- `hooks/setState`

`hooks/list` returns metadata, warnings, and errors for all discovered hooks.
`hooks/setState` writes per-user enable/trust state, refreshes the runtime
snapshot, and emits `workspace/configChanged` with region `hooks`.

Clients should display hooks grouped by source and event, show trust state and
condition/execution metadata, and let users enable/disable or trust/re-trust
individual hooks. Clients must not edit commands.

Hook run notifications are best-effort and transient. They include run id, hook
key, event, thread id, turn id, status, duration, exit code, output entries, and
whether a continuation was queued.

---

## 9. Acceptance Checklist

- Existing DotCraft config hooks continue to run.
- Plugin hooks are discovered, listed, trusted, toggled, and executed.
- `UserPromptSubmit` can inject additional context and block a prompt.
- Tool hooks receive portable tool aliases and portable tool input.
- `if` conditions match shell command patterns.
- JSON hook output injects additional context without leaking raw JSON.
- `Stop` hooks can request a queued follow-up turn through rewake.
- Rewake follow-up turns do not recursively trigger infinite Stop rewake loops.
- AppServer and Desktop expose the new metadata without command editing.
