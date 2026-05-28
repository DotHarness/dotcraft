#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
plugin_root="$(cd "$script_dir/../../.." && pwd)"
version="${CSHARP_LS_VERSION:-0.24.0}"
tool_path="$plugin_root/server/csharp-ls"

mkdir -p "$tool_path"

if [ -x "$tool_path/csharp-ls" ] || [ -x "$tool_path/csharp-ls.exe" ]; then
  dotnet tool update csharp-ls --tool-path "$tool_path" --version "$version"
else
  dotnet tool install csharp-ls --tool-path "$tool_path" --version "$version"
fi

echo "csharp-ls $version is installed under $tool_path"
