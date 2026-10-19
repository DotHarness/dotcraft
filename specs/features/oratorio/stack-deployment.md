# DotCraft Oratorio Stack Deployment Specification

| Field | Value |
| --- | --- |
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Parent Spec | [Oratorio Design](oratorio-design.md) |
| Related Specs | [Runtime Module Boundaries](../../architecture/runtime-module-boundaries.md), [Server Webhook Ingress](server-webhook-ingress.md) |

## Overview

The official DotCraft Stack runs DotCraft AppServer and Oratorio as one
deployment. Both services mount the same Workspace at the same absolute path,
so worktrees Oratorio creates open in AppServer without path translation.

## Supported topology

- **DotCraft**: AppServer and Dashboard.
- **Oratorio**: headless sync, automation, review, delivery, settings API and
  realtime stream.
- **Webhook gateway**: optional overlay exposing only declared webhook paths
  (Server Webhook Ingress).

AppServer, Dashboard and Oratorio bind to host loopback by default. Remote
Desktop access uses separate SSH tunnels. The webhook gateway is the only
component meant for public traffic.

Managed worktrees live inside the shared Workspace. Oratorio's state and its
writable configuration persist in the deployment's Oratorio state directory;
DotCraft's user configuration, marketplace cache and installed plugins persist
in its DotCraft state directory. Replacing a container never loses either.

## Plugin catalog

The DotCraft image carries every repository-owned bundled plugin as an
installable catalog entry; bundling does not install them. Installing a plugin
copies only that plugin into the persisted user state.

The image points at the official plugin marketplace by default. Users may turn
the default off or add marketplaces through the usual plugin configuration.
Marketplace configuration and cached snapshots live in the persisted user
state, so replacing the DotCraft container keeps them.

## Authentication and configuration

A Stack can use an independently deployed DotCraft Model Service. Remote mode
supplies the service address, a Stack-specific inference credential and the
selected provider and model. The Stack mounts only its own user and workspace
state; upstream credentials and login belong to the model service deployment
and its own authentication helper. Remote startup and doctor check the service
and model rather than local OAuth state; dry-run never invokes Docker.

The deployment holds two independent high-entropy secrets: an AppServer token
and an Oratorio service token. DotCraft uses the first; Oratorio uses the first
for its SDK calls and the second to protect its own API.

Secrets are never printed by status, doctor, logs or dry-run. A newly generated
secret may be printed once by an explicit initialization or webhook enable.
Oratorio Settings stay writable through the authenticated local or tunneled API
with the same revision and secret semantics as Desktop local mode.

Each dispatchable GitHub or GitLab project has an explicit source key and a
Workspace route in Oratorio's configuration. There is no fallback Workspace.

The Stack requires an explicit model authentication method: an API key or a
ChatGPT subscription. With a subscription, the configured OpenAI provider uses
subscription authentication and ignores API-key endpoint settings; DotCraft
requires a signed-in account and a selected model before AppServer starts, and
never silently falls back to an API key when subscription credentials are
missing or expired.

On headless Linux hosts an opt-in auth helper shares the Stack's DotCraft state
and uses the host network only during login. The operator forwards the
loopback OAuth callback ports to the server over SSH, runs the no-browser login
in the helper, and finishes the browser flow locally. The helper receives no
AppServer token or model API key. Each deployment has its own credentials, even
for the same ChatGPT account. Authorization URLs may be shown to the operator;
authorization codes and tokens never enter CLI arguments or logs.

Subscription-backed automated work runs file and shell tools inside the
DotCraft container, which holds the persisted authentication state, so those
tools can reach it.

## `dotcraft stack` contract

`dotcraft stack` is the only supported deployment CLI:

- `init`: creates the deployment files, state directories, secrets and optional
  first project configuration.
- `add-project`: adds or updates a GitHub or GitLab project and its Workspace
  route.
- `doctor`: checks deployment assets, Docker and Compose, secrets, mounts,
  configuration and service health.
- `status`, `logs`, `restart`, `upgrade`: bounded Compose lifecycle operations.
- `webhook enable/status/disable`: manages public webhook ingress without
  exposing other APIs.

Every command accepts `--dir`. Mutating commands accept `--dry-run`, which
validates and reports planned effects without writing files, generating secrets,
cloning repositories or invoking Docker.

## Failure behavior

- Initializing into a non-empty target is rejected.
- Invalid providers, source keys, Workspace paths, ports and missing required
  values fail before anything is written.
- File writes are atomic where supported.
- Lifecycle failures exit non-zero and keep bounded, redacted diagnostics.
- Doctor reports a missing or read-only DotCraft user state mount, so an
  image-only upgrade is not mistaken for a complete update. Upgrade never
  rewrites locally customized Compose.
