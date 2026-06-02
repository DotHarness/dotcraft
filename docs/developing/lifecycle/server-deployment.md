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

The stack creates a workspace under `deploy/docker/workspace`. By default, Compose publishes AppServer, Dashboard, and channel ingress ports only on the server's loopback interface:

- AppServer: `ws://127.0.0.1:9100/ws`
- Dashboard: `http://127.0.0.1:8080/dashboard`
- QQ OneBot reverse WebSocket: `ws://127.0.0.1:6700/`
- WeCom callback: `http://127.0.0.1:9000/dotcraft`

Use Desktop's remote server SSH tunnels, an SSH port forward, or a reverse proxy to reach AppServer and Dashboard remotely.

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

If a channel gateway runs outside the server, publish only the required channel port explicitly in `.env`, for example `QQ_PUBLISH_HOST=0.0.0.0`, and keep the channel access token strong.

## Optional Sandbox

Sandbox is off by default. Plain `docker compose up -d` does not mount the host Docker socket.

To enable OpenSandbox:

```bash
SANDBOX_ENABLED=true docker compose --profile sandbox up -d
```

This starts a second service from the same image and mounts `/var/run/docker.sock`. DotCraft config points `Tools.Sandbox.Domain` to `opensandbox:5880`.

## Production Notes

- The default Compose file does not expose AppServer or Dashboard beyond localhost.
- Use a strong `APPSERVER_TOKEN` and Dashboard username/password when exposing these services through a reverse proxy.
- Terminate TLS with a reverse proxy. The embedded AppServer listener serves `ws://`, not `wss://`.
- Current server Docker images are x64-only. Arm64 Linux hosts should use Docker emulation or build from source until arm64 server images are available.
- QQ uses the OneBot reverse WebSocket port (`6700` by default).
- WeCom uses the callback port (`9000` by default).
- Weixin writes QR login files under `workspace/.craft/tmp/channel-weixin-standard/`.

See also: [Channels & Bots](../../features/entry-points/channels), [Security & Sandbox](../../features/self-hosted/security), and the Docker quickstart in `deploy/docker/README.md`.
