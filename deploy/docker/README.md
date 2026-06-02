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

By default, the main service endpoints are published only on the server's loopback
interface:

- AppServer: `ws://127.0.0.1:9100/ws`
- Dashboard: `http://127.0.0.1:8080/dashboard`

Use Desktop's remote server SSH tunnels, an SSH port forward, or a reverse proxy
to reach AppServer and Dashboard remotely.

## Connect from Desktop

Desktop connects to this stack through your system SSH client and does not support SSH password entry or password storage. Make sure your local machine can SSH to the server non-interactively:

```bash
ssh-keygen -t ed25519 -C "dotcraft-remote"
ssh-copy-id user@host
ssh -o BatchMode=yes user@host "echo ok"
```

On Windows PowerShell, upload the public key with:

```powershell
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh user@host "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

Then open Desktop **Settings -> Servers -> Add server**, set the SSH target to `user@host` or an SSH config alias, leave the identity override blank, test SSH, and add this Compose directory as the stack deployment folder.

See the full guide in [Server Deployment](../../docs/features/self-hosted/server-deployment.md).

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

If NapCat, WeCom, or another gateway runs outside the server, explicitly publish
the required channel port in `.env`, for example:

```dotenv
QQ_PUBLISH_HOST=0.0.0.0
```

Keep channel access tokens strong when publishing channel ports directly.

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

- The default Compose file does not expose AppServer or Dashboard beyond localhost; prefer Desktop SSH tunnels instead of publishing them directly to the public internet.
- Use a strong `APPSERVER_TOKEN` and Dashboard username/password when exposing these services through a reverse proxy.
- Terminate TLS with a reverse proxy; the embedded AppServer listener serves `ws://`, not `wss://`.
- Current images are linux-x64. Arm64 requires a future release target.
