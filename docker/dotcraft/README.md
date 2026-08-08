# DotCraft Docker deployment

This directory contains the Docker Compose deployment for DotCraft AppServer, bundled channel adapters, and optional OpenSandbox.

The repository keeps image assets under `docker/dotcraft/` and
`docker/oratorio/`. Run Compose commands from this directory. Docker builds use
the repository root as their context.

## Quick start

```bash
cp .env.example .env
# Set DOTCRAFT_API_KEY and DOTCRAFT_MODEL.
# Optionally configure reasoning, speed, context window, another provider,
# endpoint, or channel.
docker compose up -d
```

Keep `.env` private; it contains API keys and optional channel credentials.

The renderer writes one complete `ProviderPreferences[DOTCRAFT_PROVIDER]` record. Optional model settings are:

| Variable | Allowed values | Default for a new record |
|---|---|---|
| **`DOTCRAFT_REASONING_EFFORT`** | `off`, `low`, `medium`, `high`, `extraHigh` | `off` |
| **`DOTCRAFT_REASONING_OUTPUT`** | `none`, `summary`, `full` | `full` |
| **`DOTCRAFT_SPEED`** | `standard`, `fast` | `standard` |
| **`DOTCRAFT_CONTEXT_WINDOW`** | `default`, `max` | `default` |

Omitted variables preserve fields in an existing preference. Invalid values stop startup and identify the variable and its allowed values.

For SSH setup, channel configuration, sandbox options, and production guidance, see the complete deployment guide:

- [Server deployment](../../docs/features/self-hosted/server-deployment.md)
- [服务器部署](../../docs/zh/features/self-hosted/server-deployment.md)

## Build the image locally

The Compose file pulls `ghcr.io/dotharness/dotcraft:latest` by default. To build from this checkout, uncomment the `build:` section under the `dotcraft` service, then run:

```bash
docker compose build dotcraft
docker compose up -d
```
