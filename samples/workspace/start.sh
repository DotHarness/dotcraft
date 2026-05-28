#!/usr/bin/env bash
# Build DotCraft from this repo and run it from the sample workspace.

set -euo pipefail

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$PATH:$HOME/.dotnet:$HOME/.dotnet/tools"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"

cd "$REPO_ROOT"
dotnet build

cd "$SCRIPT_DIR"
dotnet ../../src/DotCraft.App/bin/Debug/net10.0/dotcraft.dll