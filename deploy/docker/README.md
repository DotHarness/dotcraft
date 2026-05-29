# DotCraft Docker Deployment

Run DotCraft AppServer, bundled TypeScript channel adapters, and optional OpenSandbox from one Compose stack.

## Quick Start

```bash
cd deploy/docker
cp .env.example .env
# edit .env: provider, model, API key, ENABLED_CHANNELS, and channel secrets
docker compose up -d
```

The stack stores workspace state in `./workspace`. On first start, the entrypoint creates:

- `./workspace/.craft/config.json`
- `./workspace/.craft/<channel>.json` for enabled channels
- `./workspace/.craft/appserver.token` when `APPSERVER_TOKEN` is empty

The default AppServer endpoint is `ws://<server>:9100/ws`. Dashboard is exposed on port `8080`.

## Enable Channels

Set `ENABLED_CHANNELS` in `.env`:

```dotenv
ENABLED_CHANNELS=telegram,feishu
```

Supported values: `telegram`, `feishu`, `qq`, `wecom`, `weixin`. Use `all` for `telegram,feishu,qq,wecom`.

Only required credentials are rendered from `.env`. Advanced fields, such as allowlists or mention rules, are edited in the mounted files under `./workspace/.craft/<channel>.json`; the renderer only fills missing required fields on restart.

Common required values:

```dotenv
TELEGRAM_BOT_TOKEN=
FEISHU_APP_ID=
FEISHU_APP_SECRET=
QQ_ACCESS_TOKEN=
WECOM_ROBOT_TOKEN=
WECOM_ROBOT_AES_KEY=
```

QQ listens on `${QQ_PORT:-6700}` for OneBot reverse WebSocket clients. WeCom listens on `${WECOM_PORT:-9000}` for callback requests.

Weixin requires interactive QR login. When enabled, watch `./workspace/.craft/tmp/channel-weixin-standard/qr.png`.

## Optional Sandbox

Sandbox is off by default, so normal `docker compose up -d` does not mount the Docker socket.

To enable OpenSandbox:

```bash
SANDBOX_ENABLED=true docker compose --profile sandbox up -d
```

This starts a second service from the same DotCraft image and mounts `/var/run/docker.sock`. DotCraft points `Tools.Sandbox.Domain` at `opensandbox:5880`.

## Build Locally

The Compose file pulls `ghcr.io/dotharness/dotcraft:latest`. To build from a source checkout, uncomment the `build:` section under the `dotcraft` service.

```bash
docker compose build dotcraft
docker compose up -d
```

## Production Notes

- Use a strong `APPSERVER_TOKEN` when exposing port `9100`.
- Terminate TLS with a reverse proxy; the embedded AppServer listener serves `ws://`, not `wss://`.
- Current images are linux-x64. Arm64 requires a future release target.
