---
name: create-hooks
description: Create and configure DotCraft lifecycle hooks (hooks.json) for workspaces. Use when the user wants to add hooks, create hook scripts, set up security guards, auto-formatting, logging, notifications, or any shell-based automation triggered by DotCraft agent lifecycle events.
---

# Create DotCraft Hooks

## Overview

DotCraft hooks are lifecycle event triggers that run external shell commands at key agent execution points. Hooks are configured in `hooks.json` and can observe, log, or block agent actions.

## Workflow

1. Ask the user what they want to achieve (security guard, auto-format, logging, notification, etc.)
2. Determine which lifecycle event(s) to use
3. Determine scope: workspace (`.craft/hooks.json`), global (`~/.craft/hooks.json`), or plugin (`<plugin-root>/hooks/hooks.json`)
4. Generate the `hooks.json` config and any helper scripts
5. Place scripts in `.craft/hooks/`, the user's chosen global hooks directory, or the plugin's `hooks/` directory

## Config File Locations

| Scope | Path | Purpose |
|-------|------|---------|
| Global | `~/.craft/hooks.json` | Shared across all workspaces |
| Workspace | `<workspace>/.craft/hooks.json` | Current workspace only |
| Plugin | `<plugin-root>/hooks/hooks.json` | Contributed by an installed and enabled plugin |

Global hooks load first, workspace hooks are appended, and enabled plugin hooks run after config hooks. Plugin hooks are additive and read-only from Desktop; Desktop manages only user state.

## Config Format

```json
{
    "hooks": {
        "<EventName>": [
            {
                "matcher": "<regex for tool names>",
                "hooks": [
                    {
                        "type": "command",
                        "command": "<shell command>",
                        "timeout": 30
                    }
                ]
            }
        ]
    }
}
```

### Fields

| Field | Type | Description |
|-------|------|-------------|
| `matcher` | string | Regex matching tool names. Empty string = match all. Only applies to tool-related events |
| `type` | string | Always `"command"` |
| `command` | string | Shell command. Linux/macOS: `/bin/bash -c`; Windows: `powershell.exe` |
| `timeout` | number | Seconds before kill, default `30` |

## Trust and User State

DotCraft stores per-hook user state in global `~/.craft/config.json` under `Hooks.State`.

```json
{
  "Hooks": {
    "State": {
      "<hook-key>": {
        "Enabled": false,
        "TrustedHash": "sha256:..."
      }
    }
  }
}
```

- `Enabled: false` disables one hook without editing `hooks.json`.
- `TrustedHash` records the normalized hook definition the user approved.
- Config and plugin hooks must be trusted before they run. Modified hooks need trust again.
- Do not write trust state into workspace `.craft/config.json`; it is personal user state.

For plugin hooks, commands may use `${DOTCRAFT_PLUGIN_ROOT}` and `${DOTCRAFT_PLUGIN_DATA}`. DotCraft expands these variables in the command and also injects them as environment variables.

## Lifecycle Events

| Event | Trigger | Can Block? | stdin JSON Fields |
|-------|---------|-----------|-------------------|
| `SessionStart` | Session created/resumed | No | `sessionId` |
| `PreToolUse` | Before tool executes | Yes (exit 2) | `sessionId`, `toolName`, `toolArgs` |
| `PostToolUse` | After tool succeeds | No | `sessionId`, `toolName`, `toolArgs`, `toolResult` |
| `PostToolUseFailure` | After tool fails | No | `sessionId`, `toolName`, `toolArgs`, `error` |
| `PrePrompt` | Before prompt sent to agent | Yes (exit 2) | `sessionId`, `prompt` |
| `Stop` | After agent finishes | No | `sessionId` |

## Exit Codes

| Code | Meaning | Behavior |
|------|---------|----------|
| `0` | Success | Continue |
| `2` | Block | Block the action (only `PreToolUse` and `PrePrompt`). stderr = block reason |
| Other | Error | **Fail-open**: warning logged, execution continues |

## Built-in Tool Names (for `matcher`)

| Tool | Description |
|------|-------------|
| `Exec` | Shell command execution |
| `ReadFile` | Read file contents (also lists directory when path is a directory) |
| `WriteFile` | Write file |
| `EditFile` | Edit file (partial replace) |
| `GrepFiles` | Search file contents |
| `FindFiles` | Find files by name pattern |
| `WebFetch` | Fetch web page |
| `WebSearch` | Search web |
| `SpawnAgent` | Spawn a subagent child thread for background tasks |

Matcher is case-insensitive regex. Examples: `""` (all), `"WriteFile|EditFile"` (write ops), `".*File"` (all file ops), `"Exec"` (shell only).

## Platform Differences

- **Linux/macOS**: Commands run via `/bin/bash -c '<command>'`. Use standard bash syntax, `jq` for JSON parsing.
- **Windows**: Commands run via `powershell.exe -File <temp.ps1>`. Use PowerShell syntax, `ConvertFrom-Json` for JSON parsing.

### Windows (PowerShell) stdin reading pattern

```powershell
$input_data = [Console]::In.ReadToEnd() | ConvertFrom-Json
$toolName = $input_data.toolName
$toolArgs = $input_data.toolArgs
```

### Windows blocking pattern (exit 2)

```powershell
$input_data = [Console]::In.ReadToEnd() | ConvertFrom-Json
# ... check logic ...
if ($shouldBlock) {
    [Console]::Error.WriteLine("Block reason here")
    exit 2
}
exit 0
```

### Linux/macOS stdin reading pattern

```bash
INPUT=$(cat)
TOOL_NAME=$(echo "$INPUT" | jq -r '.toolName // empty')
TOOL_ARGS=$(echo "$INPUT" | jq -c '.toolArgs')
```

## Best Practices

1. **Always consume stdin** — even if unused, read it (`cat > /dev/null` or `[Console]::In.ReadToEnd() | Out-Null`) to avoid broken pipe errors
2. **Use `jq` (bash) or `ConvertFrom-Json` (PowerShell)** for JSON parsing
3. **Append `|| true` (bash) or `try/catch` (PowerShell)** inside helper scripts for non-critical work
4. **Set reasonable timeouts** — default is 30s, increase for slow operations
5. **Only use `exit 2` to block** — reserve for `PreToolUse` and `PrePrompt` when you genuinely want to prevent the action
6. **Write block reasons to stderr** — `echo "reason" >&2` (bash) or `[Console]::Error.WriteLine("reason")` (PowerShell)
7. **No interactive commands** — hooks run in background without user input
8. **Place complex logic in script files** — store in `.craft/hooks/` and reference from `hooks.json`

## Generation Rules

When generating hooks for the user:

1. **Detect the OS from workspace context** — use PowerShell syntax on Windows, bash on Linux/macOS
2. **For complex hooks, create script files** in the selected hooks directory and reference them in the `command` field
3. **Always create the hooks script directory** before placing script files there
4. **Merge with existing config** — if `.craft/hooks.json` already exists, read it first and merge new hooks into the existing config rather than overwriting
5. **Validate event names** — only use: `SessionStart`, `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `PrePrompt`, `Stop`
6. **Validate matcher patterns** — ensure regex is valid
7. **Ensure hooks are enabled** — check that `config.json` does not have `"Hooks": { "Enabled": false }`
8. **Leave trust to the user** — mention that new or modified hooks must be trusted through Desktop Hooks settings or `hooks/setState`
