# Oratorio Server Webhook Ingress Specification

| Field | Value |
| --- | --- |
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document defines how a server-managed Oratorio stack exposes its GitHub
webhook endpoint without exposing the rest of the backend.

Reference material:

- [GitHub webhook best practices](https://docs.github.com/en/webhooks/using-webhooks/best-practices-for-using-webhooks)
- [GitHub webhook delivery validation](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries)
- [Caddy automatic HTTPS](https://caddyserver.com/docs/automatic-https)
- [Let's Encrypt short-lived and IP address certificates](https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability.html)

## 1. Overview

An operator may add a Caddy gateway to a `dotcraft stack` deployment. The
gateway accepts public HTTPS requests for exactly:

```text
POST /api/v1/sources/github/webhook
```

Every other HTTPS request gets `404`. Oratorio, DotCraft AppServer and the
DotCraft dashboard stay published on host loopback only, and DotCraft Desktop
keeps reaching Oratorio through an SSH tunnel. The gateway passes the webhook's
method, path, body, signature, delivery and event headers through unchanged,
supports DNS names and public IP addresses, and generates, keeps and injects a
high-entropy webhook secret.

The gateway is optional; without it the deployment stays private-only.

Non-goals:

- any public Oratorio endpoint other than the GitHub webhook POST;
- an HTTP-only mode or skipped certificate validation;
- a webhook relay or tunnel service;
- public GitLab webhooks;
- managing GitHub App settings, cloud firewalls, DNS or host firewall rules;
- inviting or granting repository access to the public command account.

## 2. Deployment Assets

Enabling ingress adds a webhook Compose overlay and a gateway configuration
next to the base Compose file and the deployment's environment file. The base
Compose file stays authoritative for the private stack. The overlay adds the
gateway service, publishes host ports `80` and `443`, mounts the gateway
configuration read-only, keeps certificate and account state in persistent
volumes, passes the webhook secret from the environment file into Oratorio, and
leaves Oratorio's loopback binding unchanged.

Stack lifecycle commands (`status`, `logs`, `restart`, `upgrade`) use the
merged Compose model whenever the overlay exists. Manual Compose users must
pass both Compose files.

## 3. HTTPS and Routing

A DNS host uses Caddy's normal automatic HTTPS. The host must resolve to the
server, and ports `80` and `443` must be reachable for ACME validation and
HTTPS traffic.

A public IP uses Let's Encrypt short-lived certificates, kept in the gateway's
persistent state and renewed automatically.

Loopback, private, link-local, multicast, unspecified and other non-public
addresses are rejected. The host must be a bare DNS name or IP address; URLs,
paths, query strings, fragments, wildcards, ports and `localhost` are invalid.

The gateway matches the exact method and path and forwards only that request
to Oratorio, without rewriting the body or GitHub headers. It answers a plain
`404` for any other method on the webhook path, for paths below or beside it,
and for health, board, settings, source and every other API route.

Port `80` exists only for ACME challenges and the automatic HTTPS redirect;
there is no HTTP webhook endpoint.

## 4. CLI Contract

### 4.1 New deployment

`dotcraft stack init` accepts a public host, an optional ACME email and an
optional secret file:

```text
--webhook-public-host <host-or-ip>
--webhook-acme-email <email>
--webhook-secret-file <path>
```

Interactive initialization asks for an optional public host; leaving it empty
creates a private stack. Non-interactive initialization enables the gateway
only when a public host is given.

When enabled during initialization, the overlay and gateway configuration are
written before the stack starts. Dry-run reports the planned files and public
URL without writing or printing a secret.

### 4.2 Existing deployment

```text
dotcraft stack webhook enable --public-host <host-or-ip> \
  [--acme-email <email>] [--secret-file <path>] [--dir <path>] [--dry-run]
dotcraft stack webhook status [--dir <path>]
dotcraft stack webhook disable [--dir <path>]
```

`enable`:

1. validates the deployment and the public host;
2. keeps an existing secret, or reads the given secret file, or generates one;
3. checks that host ports `80` and `443` are free unless the managed gateway
   already holds them;
4. updates the environment file (§5);
5. writes only CLI-owned overlay and gateway files;
6. validates the merged Compose model and the gateway configuration;
7. starts the gateway and recreates Oratorio so it receives the secret.

Enabling again is idempotent when the generated files and settings are
compatible, and keeps the secret. A newly generated secret is printed once,
after the first successful enable, together with the public webhook URL
`https://<host-or-ip>/api/v1/sources/github/webhook`. Dry-run validates and
reports without writing files, starting containers or printing secrets.

`status` reports whether ingress is enabled, the public webhook URL, whether a
secret is configured and the gateway's state.

`disable` stops and removes the gateway, then removes only CLI-owned overlay and
gateway files. It keeps the webhook secret, the gateway's persistent state, the
base stack and the loopback Oratorio service, and never stops or recreates the
base Oratorio or DotCraft services.

If an overlay or gateway file exists without the CLI's ownership marker, enable
and disable fail without overwriting or deleting it.

## 5. Secret Handling

Generated secrets are 32 random bytes in URL-safe base64 without padding. A
secret file must hold one non-empty value after trimming trailing line endings.
A secret never appears in dry-run, status, doctor, Compose validation output,
the gateway configuration, documentation or error messages.

Updating the environment file keeps unrelated entries and comments, replaces
the file atomically so an interrupted update leaves either the complete
previous or the complete new file, and leaves it readable only by its owner on
Unix.

## 6. Doctor and Failure Rules

With ingress enabled, `doctor` also checks: the webhook secret (reported only as
configured or missing), presence and CLI ownership of the overlay and gateway
files, merged Compose validation, gateway configuration validation, that the
gateway is running, and host listeners on `80` and `443`. A private-only
deployment has no ingress checks. A failed check never changes the deployment.

Port conflicts fail before a private deployment is changed. An invalid host,
missing base deployment files, or invalid Compose or gateway configuration fail
closed.

## 7. Operator Responsibilities

The operator must:

- point the DNS name at the host when using a domain;
- allow inbound TCP `80` and `443`;
- keep Oratorio's and DotCraft's own ports closed to the public;
- enable the GitHub App webhook, paste the same secret, keep SSL verification
  on, and subscribe to issue comments;
- test a delivery on an open, configured pull request.
