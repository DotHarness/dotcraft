# Server deployment

DotCraft can run as a headless AppServer on a Linux host. The recommended server path is the Docker Compose deployment in `docker`, which bundles:

- the self-contained `dotcraft` AppServer binary
- Node.js and the TypeScript channel modules (`telegram`, `feishu`, `qq`, `wecom`, `weixin`)
- optional OpenSandbox, started only when the `sandbox` Compose profile is enabled

## Docker Compose quick start

```bash
cd docker
cp .env.example .env
```

Open `.env` and set at least:

```dotenv
DOTCRAFT_API_KEY=your-api-key
DOTCRAFT_MODEL=your-model-id
DOTCRAFT_REASONING_EFFORT=off
DOTCRAFT_REASONING_OUTPUT=full
DOTCRAFT_SPEED=standard
DOTCRAFT_CONTEXT_WINDOW=default
```

> [!CAUTION]
> `docker/.env` contains API keys and optional channel credentials. Keep it private and never commit it.

Start the stack:

```bash
docker compose up -d
```

## Configure the model provider

The example file uses OpenAI with the Chat Completions protocol. To use a different provider, protocol, or endpoint, update the complete provider block:

```dotenv
DOTCRAFT_PROVIDER=openai
DOTCRAFT_PROVIDER_DISPLAY_NAME=OpenAI
DOTCRAFT_PROVIDER_PROTOCOL=openai-chat-completions
DOTCRAFT_PROVIDER_ENDPOINT=https://api.openai.com/v1
DOTCRAFT_API_KEY=your-api-key
DOTCRAFT_MODEL=your-model-id
```

| Variable | Purpose |
|---|---|
| `DOTCRAFT_PROVIDER` | Stable provider ID, such as `openai` |
| `DOTCRAFT_PROVIDER_DISPLAY_NAME` | Name shown in DotCraft |
| `DOTCRAFT_PROVIDER_PROTOCOL` | `openai-chat-completions`, `openai-responses`, or `anthropic` |
| `DOTCRAFT_PROVIDER_ENDPOINT` | Provider base URL; use the provider's required URL |
| `DOTCRAFT_API_KEY` | Provider API key |
| `DOTCRAFT_MODEL` | Exact model ID accepted by the provider |
| `DOTCRAFT_REASONING_EFFORT` | `off`, `low`, `medium`, `high`, or `extraHigh` |
| `DOTCRAFT_REASONING_OUTPUT` | `none`, `summary`, or `full` |
| `DOTCRAFT_SPEED` | `standard` or `fast` |
| `DOTCRAFT_CONTEXT_WINDOW` | `default` or `max` |

The API key remains in the container environment. DotCraft stores a reference to that environment variable and writes one complete provider preference into the generated configuration. When a preference already exists, omitted model-option variables preserve their current fields. Invalid enum values stop startup with the variable name and allowed values.

The stack creates a workspace under `docker/workspace`. By default, Compose publishes the main service endpoints only on the server's loopback interface:

- AppServer: `ws://127.0.0.1:9100/ws`
- Dashboard: `http://127.0.0.1:8080/dashboard`

Use Desktop's remote server SSH tunnels, an SSH port forward, or a reverse proxy to reach AppServer and Dashboard remotely.

The container writes a stable AppServer token to `workspace/.craft/appserver.token` when `APPSERVER_TOKEN` is left empty.

## Connect from Desktop

![servers](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/servers.gif)

Desktop can connect to this Compose stack through your system SSH client. It reuses `~/.ssh/config`, `ssh-agent`, ProxyJump, hardware keys, and existing local keys. Desktop does not ask for or store SSH passwords, private keys, or key passphrases.

1. Make sure the stack is running on the server:

   ```bash
   cd /opt/dotcraft/docker
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
   - **Deployment folder**: the server directory containing the Compose file, for example `/opt/dotcraft/docker`
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

## Choose channels

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

Weixin requires an interactive QR login. After enabling it, open `workspace/.craft/tmp/channel-weixin-standard/qr.png` and scan the code.

## Optional sandbox

Sandbox is off by default. Plain `docker compose up -d` does not mount the host Docker socket.

To enable OpenSandbox:

```bash
SANDBOX_ENABLED=true docker compose --profile sandbox up -d
```

This starts a second service from the same image and mounts `/var/run/docker.sock`. DotCraft config points `Tools.Sandbox.Domain` to `opensandbox:5880`.

## Build the image locally

The Compose file pulls `ghcr.io/dotharness/dotcraft:latest` by default. To build from a source checkout, uncomment the `build:` section under the `dotcraft` service, then run:

```bash
docker compose build dotcraft
docker compose up -d
```

## Production notes

- The default Compose file does not expose AppServer or Dashboard beyond localhost. Do not publish them directly to the public internet; prefer Desktop SSH tunnels or a TLS reverse proxy with authentication.
- Use a strong `APPSERVER_TOKEN` and Dashboard username/password when exposing these services through a reverse proxy.
- Terminate TLS with a reverse proxy. The embedded AppServer listener serves `ws://`, not `wss://`.
- Server Docker images are x64-only. On an arm64 Linux host, run them under Docker emulation or build from source.

## Related docs

- [Channels & Bots](../entry-points/channels)
- [Security & Sandbox](./security)
- [Observability](./observability)
- [Configuration reference](../../developing/configuration)
