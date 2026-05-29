# Server Deployment

DotCraft can run as a headless AppServer on a Linux host. The recommended server path is the Docker Compose deployment in `deploy/docker`, which bundles:

- the self-contained `dotcraft` AppServer binary
- Node.js and the TypeScript channel modules (`telegram`, `feishu`, `qq`, `wecom`, `weixin`)
- optional OpenSandbox, started only when the `sandbox` Compose profile is enabled

## Docker Compose Quick Start

```bash
cd deploy/docker
cp .env.example .env
# edit .env
docker compose up -d
```

The stack creates a workspace under `deploy/docker/workspace`. AppServer listens on `ws://<server>:9100/ws`, and Dashboard listens on `http://<server>:8080`.

The container writes a stable AppServer token to `workspace/.craft/appserver.token` when `APPSERVER_TOKEN` is left empty.

## Choose Channels

Set `ENABLED_CHANNELS` in `.env`:

```dotenv
ENABLED_CHANNELS=telegram,feishu
```

Supported values are `telegram`, `feishu`, `qq`, `wecom`, and `weixin`. `all` enables `telegram,feishu,qq,wecom`; Weixin is intentionally excluded from `all` because it needs QR login.

The renderer writes `ExternalChannels` entries with `transport: "managedWebsocket"` and `builtinModule: "channel-<name>"`. AppServer then spawns the Node adapters and injects the WebSocket URL/token automatically.

Only required credentials are configured from environment variables:

```dotenv
TELEGRAM_BOT_TOKEN=
FEISHU_APP_ID=
FEISHU_APP_SECRET=
QQ_ACCESS_TOKEN=
WECOM_ROBOT_TOKEN=
WECOM_ROBOT_AES_KEY=
```

Advanced fields stay in the mounted channel files, for example `workspace/.craft/qq.json` and `workspace/.craft/wecom.json`. Edit those files for allowlists, mention rules, approval timeouts, callback paths, or card text. Restarts preserve those edits.

## Optional Sandbox

Sandbox is off by default. Plain `docker compose up -d` does not mount the host Docker socket.

To enable OpenSandbox:

```bash
SANDBOX_ENABLED=true docker compose --profile sandbox up -d
```

This starts a second service from the same image and mounts `/var/run/docker.sock`. DotCraft config points `Tools.Sandbox.Domain` to `opensandbox:5880`.

## Production Notes

- Use a strong `APPSERVER_TOKEN` when AppServer is exposed beyond localhost.
- Terminate TLS with a reverse proxy. The embedded AppServer listener serves `ws://`, not `wss://`.
- Current server images and release archives are x64-only. Arm64 hosts should use Docker emulation or build from source until arm64 artifacts are available.
- QQ uses the published OneBot reverse WebSocket port (`6700` by default).
- WeCom uses the published callback port (`9000` by default).
- Weixin writes QR login files under `workspace/.craft/tmp/channel-weixin-standard/`.

See also: [Channels & Bots](../features/entry-points/channels.md), [Security & Sandbox](../features/security.md), and the Docker quickstart in `deploy/docker/README.md`.
