#!/usr/bin/env sh
set -eu

if [ "$#" -gt 0 ]; then
  exec "$@"
fi

WORKSPACE="${DOTCRAFT_WORKSPACE:-/workspace}"
CRAFT_DIR="${WORKSPACE}/.craft"
TOKEN_FILE="${CRAFT_DIR}/appserver.token"

mkdir -p "$CRAFT_DIR" "$HOME/.craft"

if [ -z "${APPSERVER_TOKEN:-}" ]; then
  if [ -f "$TOKEN_FILE" ]; then
    APPSERVER_TOKEN="$(cat "$TOKEN_FILE")"
  else
    APPSERVER_TOKEN="$(node -e "process.stdout.write(require('node:crypto').randomBytes(32).toString('base64url'))")"
    printf '%s' "$APPSERVER_TOKEN" > "$TOKEN_FILE"
    chmod 600 "$TOKEN_FILE" 2>/dev/null || true
  fi
  export APPSERVER_TOKEN
  echo "Generated AppServer token at ${TOKEN_FILE}."
fi

node /opt/dotcraft/render-config.mjs

APP_HOST="${APPSERVER_LISTEN_HOST:-0.0.0.0}"
APP_PORT="${APPSERVER_PORT:-9100}"

echo "Starting DotCraft AppServer on ws://${APP_HOST}:${APP_PORT}/ws"
cd "$WORKSPACE"
exec dotcraft app-server \
  --listen "ws://${APP_HOST}:${APP_PORT}" \
  --token "$APPSERVER_TOKEN"
