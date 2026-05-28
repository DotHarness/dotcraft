#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
plugin_root="$(cd "$script_dir/../../.." && pwd)"
version="${CLANGD_VERSION:-22.1.0}"

if [ -n "${CLANGD_PLATFORM:-}" ]; then
  platform="$CLANGD_PLATFORM"
else
  case "$(uname -s)" in
    Darwin) platform="mac" ;;
    Linux) platform="linux" ;;
    MINGW*|MSYS*|CYGWIN*) platform="windows" ;;
    *) echo "Unsupported platform: $(uname -s)" >&2; exit 1 ;;
  esac
fi

archive_name="clangd-$platform-$version.zip"
url="${CLANGD_URL:-https://github.com/clangd/clangd/releases/download/$version/$archive_name}"
server_root="$plugin_root/server/clangd"
stage_root="$server_root/_extract"
archive_path="$server_root/$archive_name"

mkdir -p "$server_root"
rm -rf "$stage_root"
mkdir -p "$stage_root"

if command -v curl >/dev/null 2>&1; then
  curl -L "$url" -o "$archive_path"
elif command -v wget >/dev/null 2>&1; then
  wget -O "$archive_path" "$url"
else
  echo "curl or wget is required to download clangd." >&2
  exit 1
fi

if command -v python3 >/dev/null 2>&1; then
  python3 - "$archive_path" "$stage_root" <<'PY'
import sys
import zipfile

with zipfile.ZipFile(sys.argv[1]) as archive:
    archive.extractall(sys.argv[2])
PY
elif command -v unzip >/dev/null 2>&1; then
  unzip -q "$archive_path" -d "$stage_root"
else
  echo "python3 or unzip is required to extract clangd." >&2
  exit 1
fi

executable="clangd"
if [ "$platform" = "windows" ]; then
  executable="clangd.exe"
fi

clangd_path="$(find "$stage_root" -type f -path "*/bin/$executable" -print -quit)"
if [ -z "$clangd_path" ]; then
  echo "Could not find bin/$executable in $archive_name" >&2
  exit 1
fi

payload_root="$(dirname "$(dirname "$clangd_path")")"
cp -R "$payload_root"/. "$server_root"/
chmod +x "$server_root/bin/$executable" 2>/dev/null || true
rm -rf "$stage_root" "$archive_path"

echo "clangd $version is installed under $server_root"
