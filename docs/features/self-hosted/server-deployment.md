# Deploy the DotCraft Stack

Run DotCraft AppServer and Oratorio together on a Linux server with the official Docker Compose stack. Both services share one workspace, so Oratorio can dispatch work and DotCraft can open the resulting threads and worktrees.

## Initialize the stack

Install the DotCraft CLI, then create a deployment directory:

```bash
dotcraft stack init --dir /opt/dotcraft-stack --no-start
```

The command creates the Compose files, independent AppServer and Oratorio service tokens, a writable Oratorio configuration, and local `workspace`, `state`, and `secrets` directories. Generated secrets appear once and remain in `/opt/dotcraft-stack/.env`. DotCraft marketplace configuration and cache data stay under `state/dotcraft`.

Edit `.env` and set your model provider:

```dotenv
DOTCRAFT_PROVIDER=openai
DOTCRAFT_MODEL=your-model-id
DOTCRAFT_API_KEY=your-api-key
```

> [!CAUTION]
> `.env` contains API keys and service credentials. Keep it private and never commit it.

Start the deployment:

```bash
cd /opt/dotcraft-stack
docker compose up -d
dotcraft stack doctor --dir /opt/dotcraft-stack
```

## Manage plugins

Open **Plugins** after connecting from Desktop. The server image exposes every bundled plugin as an installable catalog entry and enables the official plugin marketplace by default. Installing a plugin copies only that plugin into the shared Workspace under `workspace/.craft/plugins`.

User-added marketplace configuration and cached snapshots stay under `state/dotcraft`. Preserve both `state/dotcraft` and `workspace/.craft` when replacing or moving the deployment. To use another registry archive, set `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL` in `.env` before restarting the DotCraft service.

## Add a project

Clone each repository below the generated workspace directory. Then bind its source identity to the exact container path:

```bash
git clone https://github.com/acme/example.git /opt/dotcraft-stack/workspace/example
dotcraft stack add-project \
  --dir /opt/dotcraft-stack \
  --provider github \
  --project acme/example \
  --workspace /workspace/example
dotcraft stack restart --dir /opt/dotcraft-stack
```

Use `--provider gitlab` for GitLab projects. Every dispatchable project needs an explicit `/workspace/...` mapping; the runtime does not guess a fallback workspace.

## Connect from Desktop

![Desktop server settings](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/servers.gif)

Desktop connects through the system SSH client and keeps AppServer, Oratorio, and Dashboard credentials in the main process.

1. Confirm that non-interactive SSH works: `ssh -o BatchMode=yes user@host "echo ok"`.
2. Open **Settings -> Servers -> Add server**.
3. Enter the SSH target and `/opt/dotcraft-stack` as the deployment folder.
4. Keep the default ports: AppServer `9100`, Oratorio `5087`, Dashboard `8080`.
5. Select **Open in Desktop**.

Desktop opens separate loopback SSH tunnels for AppServer and Oratorio. The Oratorio endpoint and bearer never enter the renderer.

## Operate the stack

```bash
dotcraft stack status --dir /opt/dotcraft-stack
dotcraft stack logs --dir /opt/dotcraft-stack --service oratorio
dotcraft stack restart --dir /opt/dotcraft-stack
dotcraft stack upgrade --dir /opt/dotcraft-stack
```

Add `--dry-run` to mutating commands to inspect their effect without changing files or starting processes.

## Enable GitHub webhook ingress

Expose only the GitHub webhook endpoint through the optional Caddy gateway:

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

The gateway accepts only `POST /api/v1/sources/github/webhook`; AppServer, Dashboard, and the rest of the Oratorio API remain loopback-only. Configure the secret printed by the command in your GitHub App.

Disable the gateway without removing stack state or secrets:

```bash
dotcraft stack webhook disable --dir /opt/dotcraft-stack
```

## Related docs

- [Oratorio](../oratorio)
- [Configure Oratorio](../oratorio/settings)
- [Connect GitHub to Oratorio](../oratorio/github)
- [Security & Sandbox](./security)
- [Observability](./observability)
