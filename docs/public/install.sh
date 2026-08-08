#!/usr/bin/env bash
set -euo pipefail

DEFAULT_REPO="DotHarness/dotcraft"
REPO="${DOTCRAFT_REPO:-$DEFAULT_REPO}"
INSTALL_DIR="${DOTCRAFT_INSTALL_DIR:-$HOME/.craft/bin}"
VERSION="${DOTCRAFT_VERSION:-latest}"
MANIFEST_URL="https://www.dotcraft.net/release-downloads.json"

need() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "error: $1 is required" >&2
    exit 1
  fi
}

need curl
need tar

case "$(uname -s)" in
  Linux) platform="linux" ;;
  Darwin) platform="macos" ;;
  *)
    echo "error: unsupported OS: $(uname -s)" >&2
    exit 1
    ;;
esac

machine="$(uname -m)"
case "${platform}:${machine}" in
  linux:x86_64|linux:amd64|macos:x86_64|macos:amd64) arch="x64" ;;
  macos:arm64|macos:aarch64) arch="arm64" ;;
  linux:*)
    echo "error: unsupported architecture: ${machine}. DotCraft CLI releases for Linux are currently x64-only." >&2
    exit 1
    ;;
  *)
    echo "error: unsupported architecture: ${machine}. DotCraft CLI releases are available for macOS x64, macOS arm64, and Linux x64." >&2
    exit 1
    ;;
esac

archive=""
url=""

if [ "$VERSION" = "latest" ]; then
  if [ "$REPO" = "$DEFAULT_REPO" ]; then
    manifest="$(curl -fsSL "$MANIFEST_URL")"
    VERSION="$(printf '%s\n' "$manifest" | sed -n 's/.*"tag"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)"
    asset_id="cli-${platform}-${arch}"
    asset_block="$(printf '%s\n' "$manifest" | sed -n "/\"${asset_id}\"[[:space:]]*:/,/^[[:space:]]*}/p")"
    archive="$(printf '%s\n' "$asset_block" | sed -n 's/.*"fileName"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)"
    url="$(printf '%s\n' "$asset_block" | sed -n 's/.*"url"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)"
  else
    VERSION="$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' | head -n 1)"
  fi
fi

if [ -z "$VERSION" ]; then
  echo "error: could not resolve DotCraft version" >&2
  exit 1
fi

if [ -z "$url" ]; then
  archive="DotCraft-${VERSION}-${platform}-${arch}.tar.gz"
  url="https://github.com/${REPO}/releases/download/${VERSION}/${archive}"
fi
tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

echo "Downloading ${url}"
curl -fL "$url" -o "$tmpdir/$archive"

mkdir -p "$INSTALL_DIR"
tar -xzf "$tmpdir/$archive" -C "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/dotcraft" 2>/dev/null || true

echo "DotCraft ${VERSION} installed to ${INSTALL_DIR}"
case ":$PATH:" in
  *":$INSTALL_DIR:"*) ;;
  *)
    echo
    echo "Add DotCraft to PATH:"
    echo "  export PATH=\"$INSTALL_DIR:\$PATH\""
    ;;
esac

"$INSTALL_DIR/dotcraft" --version 2>/dev/null || true
