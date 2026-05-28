#!/usr/bin/env bash

# Always consume stdin to avoid broken pipe issues.
INPUT="$(cat)"

if [ -z "$INPUT" ]; then
    exit 0
fi

TOOL_NAME="$(printf '%s' "$INPUT" | jq -r '.toolName // empty')"
FILE_PATH="$(printf '%s' "$INPUT" | jq -r '.toolArgs.path // .toolArgs.filePath // empty')"

if [ -z "$FILE_PATH" ]; then
    FILE_PATH="(unknown path)"
fi

mkdir -p ".craft/hooks"
printf '%s\t%s\t%s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$TOOL_NAME" "$FILE_PATH" >> ".craft/hooks/hooks.log"

exit 0
