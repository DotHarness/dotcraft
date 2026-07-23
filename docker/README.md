# DotCraft Docker deployment

This directory contains the Docker Compose deployment for DotCraft AppServer, bundled channel adapters, and optional OpenSandbox.

## Quick start

```bash
cp .env.example .env
# Set DOTCRAFT_API_KEY and DOTCRAFT_MODEL.
# Optionally configure another provider, endpoint, or channel.
docker compose up -d
```

Keep `.env` private; it contains API keys and optional channel credentials.

For SSH setup, channel configuration, sandbox options, and production guidance, see the complete deployment guide:

- [Server deployment](../docs/features/self-hosted/server-deployment.md)
- [服务器部署](../docs/zh/features/self-hosted/server-deployment.md)

## Build the image locally

The Compose file pulls `ghcr.io/dotharness/dotcraft:latest` by default. To build from this checkout, uncomment the `build:` section under the `dotcraft` service, then run:

```bash
docker compose build dotcraft
docker compose up -d
```
