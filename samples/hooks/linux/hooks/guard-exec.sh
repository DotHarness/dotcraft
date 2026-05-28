#!/usr/bin/env bash

# Always consume stdin to avoid broken pipe issues.
INPUT="$(cat)"

if [ -z "$INPUT" ]; then
    exit 0
fi

COMMAND_TEXT="$(printf '%s' "$INPUT" | jq -r '.toolArgs.command // empty')"

if [ -z "$COMMAND_TEXT" ]; then
    exit 0
fi

if printf '%s' "$COMMAND_TEXT" | grep -Eiq 'rm -[rf]{1,2}|Remove-Item.*-Recurse|del /[fqs]|rmdir /s|format|mkfs|diskpart|Stop-Computer|Restart-Computer|shutdown|reboot|poweroff'; then
    printf 'Blocked dangerous Exec command: %s\n' "$COMMAND_TEXT" >&2
    exit 2
fi

exit 0
