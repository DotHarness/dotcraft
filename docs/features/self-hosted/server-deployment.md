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

The stack creates a workspace under `deploy/docker/workspace`. By default, Compose publishes the main service endpoints only on the server's loopback interface:

- AppServer: `ws://127.0.0.1:9100/ws`
- Dashboard: `http://127.0.0.1:8080/dashboard`

Use Desktop's remote server SSH tunnels, an SSH port forward, or a reverse proxy to reach AppServer and Dashboard remotely.

The container writes a stable AppServer token to `workspace/.craft/appserver.token` when `APPSERVER_TOKEN` is left empty.

## Connect from Desktop

![servers](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/servers.gif)

Desktop can connect to this Compose stack through your system SSH client. It reuses `~/.ssh/config`, `ssh-agent`, ProxyJump, hardware keys, and existing local keys. Desktop does not ask for or store SSH passwords, private keys, or key passphrases.

1. Make sure the stack is running on the server:

   ```bash
   cd /opt/dotcraft/deploy/docker
   docker compose up -d
   ```

2. Make sure your local machine can connect without an interactive password prompt:

   ```bash
   ssh -o BatchMode=yes user@host "echo ok"
   ```

3. In Desktop, open **Settings -> Servers -> Add server**.
4. Set **SSH target** to `user@host` or an SSH config alias such as `dotcraft-prod`.
5. Leave **Identity file override** empty unless you need to force a specific key. The default path is usually best because it lets SSH use your normal config, agent, and default keys.
6. Click **Test SSH**. After it succeeds, add a stack:
   - **Deployment folder**: the server directory containing the Compose file, for example `/opt/dotcraft/deploy/docker`
   - **Data folder**: leave blank to use the stack's `workspace` folder
   - **AppServer port**: `9100`
   - **Dashboard port**: `8080`
   - **Project name**: leave blank unless you use a custom Compose project name
7. Click **Open in Desktop**.

Desktop opens an SSH tunnel to the server, reads `workspace/.craft/appserver.token`, and connects to the container AppServer through `ws://127.0.0.1:<local-port>/ws`. Dashboard opens through a separate SSH tunnel.

If you already manage your own SSH port forward, you can use the manual remote WebSocket URL in Connections settings, but the Servers page is the recommended path because it recreates tunnels after reconnects and restarts.

## Set up SSH keys

Remote servers require key-based, non-interactive SSH. If your local machine does not already have a key, create one:

```bash
ssh-keygen -t ed25519 -C "dotcraft-remote"
```

Upload only the `.pub` public key to the server. Never copy your private key into DotCraft, into the repository, or onto the server.

On macOS or Linux:

```bash
ssh-copy-id user@host
```

On Windows PowerShell:

```powershell
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh user@host "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
```

If your key has a passphrase, unlock it in `ssh-agent` before opening Desktop:

```bash
ssh-add ~/.ssh/id_ed25519
```

On Windows, start the agent if needed:

```powershell
Start-Service ssh-agent
ssh-add $env:USERPROFILE\.ssh\id_ed25519
```

For repeated use, create an SSH config alias:

```text
Host dotcraft-prod
  HostName example.com
  User root
  Port 22
  IdentityFile ~/.ssh/id_ed25519
```

Then use `dotcraft-prod` as the Desktop SSH target. Verify the final target the same way Desktop does:

```bash
ssh -o BatchMode=yes dotcraft-prod "echo ok"
```

## Choose Channels

Set `ENABLED_CHANNELS` in `.env`:

```dotenv
ENABLED_CHANNELS=telegram,feishu
```

Supported values are `telegram`, `feishu`, `qq`, `wecom`, and `weixin`. `all` enables `telegram,feishu,qq,wecom`; Weixin is excluded from `all` because it needs QR login.

AppServer starts the Node adapter for each enabled channel and wires up its connection automatically — you don't configure ports or tokens between them by hand.

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

- The default Compose file does not expose AppServer or Dashboard beyond localhost. Do not publish them directly to the public internet; prefer Desktop SSH tunnels or a TLS reverse proxy with authentication.
- Use a strong `APPSERVER_TOKEN` and Dashboard username/password when exposing these services through a reverse proxy.
- Terminate TLS with a reverse proxy. The embedded AppServer listener serves `ws://`, not `wss://`.
- Server Docker images are x64-only. On an arm64 Linux host, run them under Docker emulation or build from source.

## Related docs

- [Channels & Bots](../entry-points/channels)
- [Security & Sandbox](./security)
- [Observability](./observability)
- Docker quickstart in `deploy/docker/README.md`
