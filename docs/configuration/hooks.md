# Hooks

Hook commands live in `hooks.json`, not in `config.json`. DotCraft loads global hooks from `~/.craft/hooks.json`, workspace hooks from `.craft/hooks.json`, and plugin hooks from enabled plugin hook files. For a user-facing overview and the Desktop workflow, start with [Lifecycle Hooks](../guide/hooks).

| Field | Description | Default |
|-------|-------------|---------|
| `Hooks.Enabled` | Enables Hooks | `true` |
| `Hooks.State` | Per-hook user state keyed by stable hook key. Stores `Enabled` and `TrustedHash` for Desktop toggle/trust actions | `{}` |

## Hook files

Hook quick start (`.craft/hooks.json`):

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Exec",
        "hooks": [
          {
            "type": "command",
            "command": "node .craft/hooks/log-tool-call.js",
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

## Hook fields

Hook matcher group fields:

| Field | Description |
|---|---|
| `matcher` | Regex for matching tool names. Empty matches all tool-related events |
| `hooks` | Ordered list of hook handlers for the event and matcher |

Hook handler fields:

| Field | Description |
|---|---|
| `type` | Supports `"command"` |
| `command` | Shell command to run |
| `timeout` | Hook timeout in seconds |
| `if` | Optional condition such as `Bash(git commit:*)` |
| `shell` | Optional shell override |
| `statusMessage` | Optional UI status label |
| `async` | Run without blocking the current action |
| `asyncRewake` | Allows hook feedback to enqueue a follow-up turn |
| `rewakeMessage` | Prefix for follow-up feedback |
| `rewakeSummary` | Short follow-up summary |

## Lifecycle events

Lifecycle events:

| Event | Purpose |
|---|---|
| `SessionStart` | Runs when a new session starts |
| `UserPromptSubmit` | Runs when a user prompt is submitted, before prompt assembly |
| `PrePrompt` | DotCraft-native compatibility event before the assembled prompt is sent |
| `PreToolUse` | Checks or blocks before tool calls |
| `PermissionRequest` | Runs before a permission request is shown |
| `PostToolUse` | Logs, formats, or notifies after successful tool calls |
| `PostToolUseFailure` | Runs after a tool call fails |
| `PreCompact` / `PostCompact` | Run around context compaction |
| `SubagentStart` / `SubagentStop` | Run around subagent lifecycle |
| `Stop` | Runs after the assistant response and can enqueue follow-up feedback |
| `StopFailure` | Runs after Stop hook handling fails |

## Input and output

Tool-related Hook stdin usually includes:

```json
{
  "hook_event_name": "PreToolUse",
  "cwd": "/workspace/example",
  "sessionId": "thread-id",
  "session_id": "thread-id",
  "toolName": "Bash",
  "tool_name": "Bash",
  "toolArgs": {
    "command": "dotnet test"
  },
  "tool_input": {
    "command": "dotnet test"
  }
}
```

Turn-related Hook stdin usually includes:

```json
{
  "hook_event_name": "Stop",
  "cwd": "/workspace/example",
  "sessionId": "thread-id",
  "session_id": "thread-id",
  "last_assistant_message": "Agent completed the turn",
  "stop_hook_active": false
}
```

DotCraft emits both camelCase and snake_case field names. JSON stdout can return
`hookSpecificOutput.additionalContext` to inject model-visible context, or
`decision: "block"` plus `reason` to block supported events or request a rewake
follow-up from an `asyncRewake` hook. The complete engineering contract lives in
`specs/features/lifecycle-hooks.md`.

Exit code semantics:

| Exit code | Meaning |
|---|---|
| `0` | Success, continue |
| `2` | Block supported events, or request follow-up feedback for rewake hooks |
| Other non-zero | Hook failed; DotCraft records the failure and continues according to the event's runtime policy |

Matcher examples:

| matcher | Matches |
|---|---|
| `WriteFile\|EditFile` | File writes and edits |
| `Exec` | Shell commands |
| `.*` | All tools |

## Hook state

Desktop manages per-user hook state in `~/.craft/config.json`:

```json
{
  "Hooks": {
    "State": {
      "/workspace/.craft/hooks.json:pre_tool_use:0:0": {
        "Enabled": false,
        "TrustedHash": "sha256:..."
      }
    }
  }
}
```

`Enabled: false` disables one hook without editing the source file. `TrustedHash` records the last trusted normalized hook definition. Hooks from config and plugins must be trusted before they run, and modified hooks must be trusted again. Plugin hooks are usually trusted as one plugin bundle in Desktop, while the saved state remains per hook.

## Plugin hooks

Plugin hook files use the same `hooks.json` structure. In plugin hook commands, DotCraft expands `${DOTCRAFT_PLUGIN_ROOT}` and `${DOTCRAFT_PLUGIN_DATA}` and injects the same names as environment variables.
