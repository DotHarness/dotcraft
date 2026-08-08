#!/usr/bin/env sh
set -eu

CONFIG_PATH="${SANDBOX_CONFIG_PATH:-/etc/opensandbox/sandbox.toml}"
PORT="${SANDBOX_PORT:-5880}"

mkdir -p "$(dirname "$CONFIG_PATH")"

if [ ! -f "$CONFIG_PATH" ]; then
  opensandbox-server init-config "$CONFIG_PATH" --example docker
fi

python3 - "$CONFIG_PATH" "$PORT" <<'PY'
from pathlib import Path
import re
import sys

path = Path(sys.argv[1])
port = sys.argv[2]
content = path.read_text(encoding="utf-8")

content = re.sub(r"(?m)^(port\s*=\s*)\d+", rf"\g<1>{port}", content)
# DotCraft uses NetworkPolicy=allow by default. Removing [egress] avoids
# requiring the optional egress helper image in the one-click sandbox service.
content = re.sub(r"(?ms)\[egress\][^\[]*", "", content)

path.write_text(content, encoding="utf-8")
PY

exec opensandbox-server --config "$CONFIG_PATH"
