#!/usr/bin/env sh
set -eu

if [ -z "${DOTCRAFT_MANAGED_SERVICE_TOKEN:-}" ]; then
  echo "oratorio: DOTCRAFT_MANAGED_SERVICE_TOKEN is required" >&2
  exit 1
fi
if [ -z "${Oratorio__DotCraft__AppServerToken:-}" ]; then
  echo "oratorio: Oratorio__DotCraft__AppServerToken is required" >&2
  exit 1
fi

git config --global user.name "${ORATORIO_GIT_USER_NAME:-Oratorio}"
git config --global user.email "${ORATORIO_GIT_USER_EMAIL:-oratorio@localhost}"
# Mounted workspaces can be owned by another uid.
git config --global --add safe.directory '*'

export DOTCRAFT_MANAGED_SERVICE_STATE_ROOT="${DOTCRAFT_MANAGED_SERVICE_STATE_ROOT:-/data/oratorio}"
mkdir -p "$DOTCRAFT_MANAGED_SERVICE_STATE_ROOT"

exec dotcraft oratorio serve "$@"
