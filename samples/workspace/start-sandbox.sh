#!/usr/bin/env bash
# Start the OpenSandbox server for DotCraft tool execution sandboxing.
# Requires: Python 3.10+, Docker, opensandbox-server on PATH.
#   Install: pip install opensandbox-server
#   Or:      uv pip install opensandbox-server --system
#
# The server port is read from .craft/config.json (Tools.Sandbox.Domain).
# A local sandbox config is generated at .craft/sandbox.toml on each run,
# inheriting all settings from the base config but overriding the port.
#
# Base config path: defaults to ~/.sandbox.toml
#   Generate: opensandbox-server init-config ~/.sandbox.toml --example docker
#   Override: set environment variable SANDBOX_CONFIG_PATH to a custom path
#
# If `opensandbox-server` is not on PATH, set environment variable
# OPENSANDBOX_SERVER_EXE to the full path of the executable.

set -euo pipefail

# --- Ensure Docker access ---
# If the current shell can't talk to Docker, try to fix it automatically:
#   1. Use setfacl to grant the current user rw access to docker.sock (persistent)
#   2. Fall back to re-exec under `sg docker` (only works interactively)
if ! docker info &>/dev/null; then
    SOCK="/var/run/docker.sock"
    if [[ -S "$SOCK" ]] && command -v setfacl &>/dev/null; then
        echo "Docker not accessible, attempting setfacl on ${SOCK}..."
        if sudo setfacl -m "u:$(whoami):rw" "$SOCK" 2>/dev/null && docker info &>/dev/null; then
            echo "Docker access granted via ACL."
        fi
    fi
    # If still no access, try sg (won't work in background/screen, but covers interactive use)
    if ! docker info &>/dev/null && [[ -z "${_SANDBOX_SG_DONE:-}" ]]; then
        if id -nG 2>/dev/null | grep -qw docker; then
            echo "Docker group not active in current shell, re-executing with sg docker..."
            export _SANDBOX_SG_DONE=1
            exec sg docker -c "bash $0 $*"
        fi
        echo "ERROR: Cannot access Docker. Ensure the current user is in the docker group and re-login." >&2
        exit 1
    fi
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CRAFT_DIR="${SCRIPT_DIR}/.craft"
DOTCRAFT_CFG="${CRAFT_DIR}/config.json"
LOCAL_TOML="${CRAFT_DIR}/sandbox.toml"

# --- Ensure PATH includes common locations ---
export PATH="$HOME/.local/bin:$PATH"

# --- Resolve sandbox executable ---
if [[ -n "${OPENSANDBOX_SERVER_EXE:-}" ]]; then
    SANDBOX_EXE="$OPENSANDBOX_SERVER_EXE"
elif command -v opensandbox-server &>/dev/null; then
    SANDBOX_EXE="$(command -v opensandbox-server)"
elif [[ -x "/data/workspace/OpenSandbox/server/.venv/bin/opensandbox-server" ]]; then
    SANDBOX_EXE="/data/workspace/OpenSandbox/server/.venv/bin/opensandbox-server"
else
    # Try to find it via python sysconfig
    SCRIPTS_DIR="$(python3 -c 'import sysconfig; print(sysconfig.get_path("scripts"))' 2>/dev/null || true)"
    if [[ -n "$SCRIPTS_DIR" && -x "${SCRIPTS_DIR}/opensandbox-server" ]]; then
        SANDBOX_EXE="${SCRIPTS_DIR}/opensandbox-server"
    else
        echo "ERROR: opensandbox-server not found." >&2
        echo "Install it with: pip install opensandbox-server" >&2
        echo "Or: uv pip install opensandbox-server --system" >&2
        echo "Or set OPENSANDBOX_SERVER_EXE to the full path of the executable." >&2
        exit 1
    fi
fi

echo "Using opensandbox-server: ${SANDBOX_EXE}"

# --- Resolve base config ---
BASE_TOML="${SANDBOX_CONFIG_PATH:-$HOME/.sandbox.toml}"

if [[ ! -f "$BASE_TOML" ]]; then
    echo "ERROR: Base config not found at ${BASE_TOML}." >&2
    echo "Generate it with: opensandbox-server init-config ${BASE_TOML} --example docker" >&2
    echo "Or set SANDBOX_CONFIG_PATH to point at an existing config file." >&2
    exit 1
fi

# --- Read port and sandbox image from .craft/config.json (Tools.Sandbox) ---
SANDBOX_PORT=5880
SANDBOX_IMAGE="ubuntu:latest"

if [[ -f "$DOTCRAFT_CFG" ]]; then
    # Extract domain from Tools.Sandbox.Domain
    DOMAIN="$(python3 -c "
import json, sys
with open('${DOTCRAFT_CFG}') as f:
    cfg = json.load(f)
domain = cfg.get('Tools', {}).get('Sandbox', {}).get('Domain', '')
print(domain)
" 2>/dev/null || true)"

    if [[ "$DOMAIN" == *:* ]]; then
        SANDBOX_PORT="${DOMAIN##*:}"
    fi

    # Extract image from Tools.Sandbox.Image
    IMAGE="$(python3 -c "
import json
with open('${DOTCRAFT_CFG}') as f:
    cfg = json.load(f)
img = cfg.get('Tools', {}).get('Sandbox', {}).get('Image', '')
print(img)
" 2>/dev/null || true)"

    if [[ -n "$IMAGE" ]]; then
        SANDBOX_IMAGE="$IMAGE"
    fi

    echo "Sandbox port from config.json (Tools.Sandbox.Domain): ${SANDBOX_PORT}"
    echo "Sandbox image: ${SANDBOX_IMAGE}"
else
    echo "WARNING: ${DOTCRAFT_CFG} not found, using default port ${SANDBOX_PORT}"
fi

# --- Generate local sandbox.toml with the resolved port ---
mkdir -p "$CRAFT_DIR"

# Read base config, override port, remove [egress] section
python3 -c "
import re, sys

with open('${BASE_TOML}', 'r') as f:
    content = f.read()

# Replace port value in [server] section
content = re.sub(r'(?m)^(port\s*=\s*)\d+', r'\g<1>${SANDBOX_PORT}', content)

# Remove the entire [egress] section — egress container is not needed (NetworkPolicy = allow)
# The server validates egress.image as non-empty, so we must drop the block entirely
content = re.sub(r'(?ms)\[egress\][^\[]*', '', content)

with open('${LOCAL_TOML}', 'w') as f:
    f.write(content)
"

echo "Generated ${LOCAL_TOML} (port=${SANDBOX_PORT})"

# --- Pre-pull images to eliminate cold-start on first tool call ---
if command -v docker &>/dev/null; then
    echo "Pre-pulling sandbox images (timeout 60s each)..."
    timeout 60 docker pull "$SANDBOX_IMAGE" 2>&1 | tail -1 || true
    timeout 60 docker pull "opensandbox/execd:v1.0.6" 2>&1 | tail -1 || true
    echo "Pre-pull done."
else
    echo "WARNING: docker not found; skipping pre-pull. First tool call may be slow."
fi

# --- Start server ---
echo "Starting OpenSandbox server (config: ${LOCAL_TOML})..."
exec "$SANDBOX_EXE" --config "$LOCAL_TOML"
