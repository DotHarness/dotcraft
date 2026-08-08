#!/usr/bin/env sh
#
# Oratorio backend container entrypoint.
#
# Responsibilities before launching the server:
#   1. Give git an identity (managed worktree branches need user.name/email).
#   2. Trust volume-mounted repos that may be owned by another uid.
#   3. Validate the service and AppServer credentials supplied by the Stack.
#
set -eu

# 1 + 2: git configuration for worktree operations on the shared volume.
git config --global user.name  "${ORATORIO_GIT_USER_NAME:-Oratorio}"
git config --global user.email "${ORATORIO_GIT_USER_EMAIL:-oratorio@localhost}"
git config --global --add safe.directory '*'

if [ -z "${DOTCRAFT_MANAGED_SERVICE_TOKEN:-}" ]; then
  echo "oratorio: DOTCRAFT_MANAGED_SERVICE_TOKEN is required" >&2
  exit 1
fi
if [ -z "${Oratorio__DotCraft__AppServerToken:-}" ]; then
  echo "oratorio: Oratorio__DotCraft__AppServerToken is required" >&2
  exit 1
fi

mkdir -p "${DOTCRAFT_MANAGED_SERVICE_STATE_ROOT:-/data/oratorio}"

# Allow `docker run ... <args>` to override; otherwise launch the server.
if [ "$#" -gt 0 ]; then
  exec "$@"
fi

cd /opt/oratorio
exec ./oratorio-server
