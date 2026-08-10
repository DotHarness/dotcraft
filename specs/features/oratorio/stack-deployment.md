# DotCraft Oratorio Stack Deployment Specification

| Field | Value |
| --- | --- |
| Version | 1.1.0 |
| Status | Living |
| Date | 2026-08-08 |
| Related Specs | [Remote Server Management](../remote-server-management.md), [Webhook Ingress](./server-webhook-ingress.md) |

## Overview

The official DotCraft Stack runs DotCraft AppServer and Oratorio as one deployment. Both services mount the same Workspace at the same absolute container path so Oratorio-created Worktrees can be opened by AppServer without path translation.

## Supported topology

- `dotcraft`: AppServer and Dashboard.
- `oratorio`: headless sync, automation, review, delivery, settings API, and realtime stream.
- `opensandbox`: optional Compose profile.
- `webhook-gateway`: optional overlay exposing only declared webhook paths.

AppServer, Dashboard, and Oratorio host ports bind to loopback by default. Remote Desktop access uses independent SSH tunnels. The webhook overlay is the only component intended for public ingress.

Repository-owned container assets are separated by image ownership:

- `docker/dotcraft/` contains the DotCraft image, official Compose stack,
  configuration renderer, and webhook overlay.
- `docker/oratorio/` contains the Oratorio Server image and entrypoint.

Both images use the repository root as their Docker build context.

Both primary containers mount the host Workspace as `/workspace`. Managed Worktrees live under `/workspace/.craft/oratorio/worktrees`. Oratorio state and its writable configuration live under the deployment's `state/oratorio` directory and mount as `/data/oratorio`. DotCraft user configuration and marketplace cache live under `state/dotcraft` and mount as `/root/.craft`; Workspace-installed plugins remain under `workspace/.craft/plugins`.

## Plugin catalog

The DotCraft image contains every repository-owned bundled plugin source under `/opt/dotcraft/plugins` and sets `DOTCRAFT_BUILTIN_PLUGIN_ROOTS` to that container. Bundling makes plugins visible as installable catalog entries; it does not install them. `plugin/install` copies only the selected plugin into `/workspace/.craft/plugins/<pluginId>`.

The image supplies the official plugin marketplace through `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL`. Users may disable the default or add marketplaces through the existing plugin configuration and marketplace APIs. Marketplace configuration and cached snapshots use the persisted `/root/.craft` mount, so replacing the DotCraft container does not remove them.

## Authentication and configuration

The deployment `.env` contains independent high-entropy `APPSERVER_TOKEN` and `ORATORIO_SERVICE_TOKEN` values. DotCraft consumes the first; Oratorio consumes both, using the AppServer token for SDK calls and the service token for its protected API.

Secrets are never printed by status, doctor, logs, or dry-run. A newly generated secret may be printed once by an explicit mutating initialization or webhook-enable operation. Oratorio Settings remain writable through the authenticated local or tunneled API and use the same revision/secret semantics as Desktop local mode.

Each dispatchable GitHub or GitLab project has an explicit source key and `/workspace/...` route in `state/oratorio/config.json`. There is no fallback Workspace.

## `dotcraft stack` contract

`dotcraft stack` is the only supported deployment CLI:

- `init`: creates the deployment files, state directories, secrets, and optional initial project configuration.
- `add-project`: adds or updates a GitHub/GitLab project and its explicit Workspace route.
- `doctor`: validates deployment assets, Docker/Compose availability, secrets, mounts, configuration, and service health.
- `status`, `logs`, `restart`, `upgrade`: run bounded Compose lifecycle operations.
- `webhook enable/status/disable`: manages the public ingress overlay without exposing other APIs.

All commands accept `--dir`. Mutating commands accept `--dry-run`; dry-run performs validation and reports planned effects without writing files, generating secret values, cloning repositories, or invoking Docker.

## Failure behavior

- Existing non-empty initialization targets are rejected.
- Invalid providers, source keys, Workspace paths, ports, and missing required values fail before writes.
- Partial writes use same-directory temporary files and atomic replacement where supported.
- Lifecycle failures return a non-zero exit code and preserve bounded, redacted diagnostics.
- Disabling webhook ingress preserves the base stack, state, secrets, and certificate volumes.

## Acceptance

- Compose configuration contains both primary services with identical `/workspace` mounts.
- A fresh Workspace lists every bundled plugin as uninstalled and installable, and installing one plugin copies only that plugin into `/workspace/.craft/plugins`.
- The official marketplace is available by default, and user marketplace configuration and cache survive container replacement.
- Headless workers start independently of Desktop.
- Remote Board, Settings, stream, and Thread navigation use the same persisted data as headless operation.
- CLI dry-run is non-mutating, lifecycle commands are allow-listed, and secret output follows this specification.
- Webhook routing exposes only the documented provider endpoint and passes signature headers unchanged.
