#!/usr/bin/env bash
set -euo pipefail

REPO="${DOTCRAFT_REPO:-DotHarness/dotcraft}"
INSTALL_DIR="${DOTCRAFT_INSTALL_DIR:-$HOME/.craft/bin}"
VERSION="${DOTCRAFT_VERSION:-latest}"

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

case "$(uname -m)" in
  x86_64|amd64) arch="x64" ;;
  *)
    echo "error: unsupported architecture: $(uname -m). DotCraft CLI releases are currently x64-only." >&2
    exit 1
    ;;
esac

if [ "$VERSION" = "latest" ]; then
  VERSION="$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' | head -n 1)"
fi

if [ -z "$VERSION" ]; then
  echo "error: could not resolve DotCraft version" >&2
  exit 1
fi

archive="DotCraft-${VERSION}-${platform}-${arch}.tar.gz"
url="https://github.com/${REPO}/releases/download/${VERSION}/${archive}"
tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

echo "Downloading ${url}"
curl -fL "$url" -o "$tmpdir/$archive"

mkdir -p "$INSTALL_DIR"
tar -xzf "$tmpdir/$archive" -C "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/dotcraft" "$INSTALL_DIR/dotcraft-tui" 2>/dev/null || true

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
