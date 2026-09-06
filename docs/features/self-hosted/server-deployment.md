# Server Deployment

Run DotCraft AppServer and [Oratorio](../oratorio) together on one Linux server with the official Docker Compose stack. Both services share the same workspace directory. Work that Oratorio dispatches opens in Desktop as an ordinary thread and worktree.

## Initialize the stack

Install the DotCraft CLI, then create a deployment directory:

```bash
dotcraft stack init --dir /opt/dotcraft-stack --no-start
```

The command generates the Compose files, separate credentials for each service, and the `workspace`, `state`, and `secrets` directories. Generated secrets are shown once and then live in `/opt/dotcraft-stack/.env`.

Edit `.env` and fill in your model provider:

```dotenv
DOTCRAFT_PROVIDER=openai
DOTCRAFT_MODEL=your-model-id
DOTCRAFT_API_KEY=your-api-key
```

> [!CAUTION]
> `.env` holds API keys and service credentials. Keep it private and never commit it.

Start the deployment, then run a health check:

```bash
cd /opt/dotcraft-stack
docker compose up -d
dotcraft stack doctor --dir /opt/dotcraft-stack
```

## Add a project

Clone each repository below the `workspace` directory, then bind it to its exact path inside the container:

```bash
git clone https://github.com/acme/example.git /opt/dotcraft-stack/workspace/example
dotcraft stack add-project \
  --dir /opt/dotcraft-stack \
  --provider github \
  --project acme/example \
  --workspace /workspace/example
dotcraft stack restart --dir /opt/dotcraft-stack
```

Use `--provider gitlab` for GitLab projects. Every project that can receive dispatched work needs an explicit `/workspace/...` mapping — DotCraft never guesses one.

## Connect from Desktop

![Desktop server settings](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/servers.gif)

The services listen only on the server itself. Desktop reaches them through tunnels opened by your system SSH client, so no port has to face the public internet.

First confirm that non-interactive SSH works:

```bash
ssh -o BatchMode=yes user@host "echo ok"
```

Then open **Settings → Connections → SSH → Add server**, enter the SSH target, set the deployment folder to `/opt/dotcraft-stack`, and keep the default ports (AppServer `9100`, Oratorio `5087`, Dashboard `8080`). Finish with **Open in Desktop**.

## Manage plugins

Open **Plugins** once you are connected. The server ships with the official plugin marketplace and every bundled plugin, and anything you install applies to that server's shared workspace only — your local workspaces are untouched.

When you replace or move the deployment, carry `state/dotcraft` and `workspace/.craft` with it: plugins, marketplace sources, and caches all live in those two directories. To point the server at your own plugin registry, see the [configuration reference](../../developing/configuration#plugins-mcp-and-lsp).

## Operate the stack

```bash
dotcraft stack status --dir /opt/dotcraft-stack
dotcraft stack logs --dir /opt/dotcraft-stack --service oratorio
dotcraft stack restart --dir /opt/dotcraft-stack
dotcraft stack upgrade --dir /opt/dotcraft-stack
```

Add `--dry-run` to any command that changes state to see exactly what it would do before running it for real.

## Open the GitHub webhook endpoint

To receive GitHub events, use the optional Caddy gateway to expose the webhook endpoint and nothing else:

```bash
dotcraft stack webhook enable \
  --dir /opt/dotcraft-stack \
  --public-host hooks.example.com
```

The gateway accepts only `POST /api/v1/sources/github/webhook`. Everything else stays on the loopback interface. Put the secret the command prints into your GitHub App, then follow [Connect GitHub to Oratorio](../oratorio/github) for the rest of the setup.

Disabling the gateway leaves stack state and secrets in place:

```bash
dotcraft stack webhook disable --dir /opt/dotcraft-stack
```

## Related docs

- [Oratorio](../oratorio) — dispatch and track work on this deployment
- [Security & Sandbox](./security) — tighten what the agent can reach on the server
- [Observability](./observability) — review runs and session traces in the Dashboard
