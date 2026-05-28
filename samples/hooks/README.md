# DotCraft Hooks Samples

**[中文](./README_ZH.md) | English**

This sample shows how to configure DotCraft lifecycle hooks in your workspace to block dangerous operations, log tool results, and extend agent behavior.

## Sample Contents

This sample provides two ready-to-use platform directories:

- [windows](./windows): Windows / PowerShell example
- [linux](./linux): Linux / macOS Bash example

Each directory includes:

- `hooks.json`
- `hooks/guard-exec.*`: blocks obviously dangerous `Exec` commands during `PreToolUse`
- `hooks/log-post-tool.*`: logs `WriteFile` / `EditFile` operations during `PostToolUse`

## Usage

Copy the files from the platform directory you want into your real workspace:

```text
<workspace>/.craft/hooks.json
<workspace>/.craft/hooks/...
```

If you use the shell scripts on Linux / macOS, remember to make them executable:

```bash
chmod +x hooks/*.sh
```

## create-hooks Skill

DotCraft includes a built-in `create-hooks` skill that can help you generate or adjust `hooks.json` and `hooks` scripts during an AI conversation.

If you do not want to write the configuration by hand, you can describe needs such as:

- Blocking specific shell commands
- Appending logs after file writes
- Sending notifications after the agent stops responding

## Important Note

After creating or modifying hooks, the user must restart DotCraft manually before the changes take effect.

## Events Demonstrated

| Event | Purpose |
|------|------|
| `PreToolUse` | Checks `Exec` commands before execution and blocks obviously dangerous ones |
| `PostToolUse` | Appends a log entry after a file write or edit succeeds |

## Expected Behavior

- When the agent tries to run an obviously dangerous shell command, the hook returns a blocking reason
- When the agent successfully runs `WriteFile` or `EditFile`, a log entry is appended to `hooks/hooks.log`
