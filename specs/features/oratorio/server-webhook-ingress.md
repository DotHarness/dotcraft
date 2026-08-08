# Oratorio Server Webhook Ingress Specification

| Field | Value |
| --- | --- |
| Version | 0.1.0 |
| Status | Living |
| Date | 2026-07-31 |
| Parent Spec | [Oratorio Design](./oratorio-design.md) |

This document defines the deployment contract for exposing the GitHub webhook
endpoint of a server-managed Oratorio stack without exposing the rest of the
backend API.

Reference material:

- [GitHub webhook best practices](https://docs.github.com/en/webhooks/using-webhooks/best-practices-for-using-webhooks)
- [GitHub webhook delivery validation](https://docs.github.com/en/webhooks/using-webhooks/validating-webhook-deliveries)
- [Caddy automatic HTTPS](https://caddyserver.com/docs/automatic-https)
- [Let's Encrypt short-lived and IP address certificates](https://letsencrypt.org/2026/01/15/6day-and-ip-general-availability.html)

---

## 1. Overview

An operator may add a Caddy gateway to a `dotcraft stack` Docker deployment. The
gateway accepts public HTTPS requests for exactly:

```text
POST /api/v1/sources/github/webhook
```

Every other HTTPS request returns `404`. Oratorio, DotCraft AppServer, and the
DotCraft dashboard remain published on host loopback only. DotCraft Desktop
continues to connect to Oratorio through an SSH tunnel.

The gateway is optional. A deployment without the generated Compose overlay and
Caddyfile retains the existing private-only topology.

## 2. Goals

The completed behavior must:

- provide a production HTTPS endpoint for GitHub webhook delivery;
- keep the full Oratorio API inaccessible through the public gateway;
- preserve the webhook request method, path, body, signature, delivery, and
  event headers;
- keep the existing Oratorio host port bound to `127.0.0.1`;
- support both DNS names and public IP addresses;
- generate, preserve, and inject a high-entropy webhook secret;
- make enable, status, disable, restart, logs, upgrade, and doctor behavior
  predictable for `dotcraft stack` deployments;
- leave GitHub App settings and host firewall policy under operator control.

## 3. Non-goals

The first version does not:

- expose any Oratorio endpoint other than the GitHub webhook POST;
- provide an HTTP-only mode or bypass certificate validation;
- add a webhook relay or tunnel service;
- expose GitLab webhooks;
- modify the backend API, database, Desktop UI, or `oratorio.config.json`;
- modify GitHub App settings, cloud firewalls, DNS, or host firewall rules;
- invite or grant repository access to the public command account.

## 4. Deployment Assets

An enabled `dotcraft stack` deployment contains:

```text
docker-compose.yml
docker-compose.webhook.yml
Caddyfile
.env
```

The base Compose file remains authoritative for the private stack. The webhook
overlay:

- adds a `webhook-gateway` service using `caddy:2.11-alpine`;
- publishes host TCP ports `80` and `443`;
- mounts `Caddyfile` read-only;
- persists `/data` and `/config` in named volumes;
- injects `Oratorio__GitHub__WebhookSecret` into the existing `oratorio`
  service from `.env`;
- does not change the Oratorio `5087` host binding.

CLI lifecycle commands automatically include the overlay when the managed
overlay file exists. Manual Compose users must pass both Compose files.

## 5. HTTPS and Routing

### 5.1 Domain host

A DNS host uses Caddy's normal automatic HTTPS behavior. The configured host
must resolve to the server and ports `80` and `443` must be reachable for ACME
validation and HTTPS traffic.

### 5.2 Public IP host

A public IP uses the Let's Encrypt ACME issuer with the `shortlived` profile.
Caddy stores certificate and account state in its persistent data volume and
renews the short-lived certificate automatically.

Loopback, private, link-local, multicast, unspecified, and other non-public IP
addresses are rejected. Host input must be a bare DNS name or IP address. URLs,
paths, query strings, fragments, wildcard hosts, ports, and `localhost` are
invalid.

### 5.3 Route boundary

Caddy matches both the exact method and exact path. It forwards the matching
request to `oratorio:5087` without rewriting the request body or GitHub
headers. It returns a plain `404` for:

- `GET` or any non-`POST` method on the webhook path;
- paths below or adjacent to the webhook path;
- health, board, settings, source, and every other API route.

Port `80` exists only for Caddy's ACME challenge and automatic HTTPS redirect.
The product does not provide an HTTP webhook endpoint.

## 6. CLI Contract

### 6.1 New deployment

`dotcraft stack init` accepts:

```text
--webhook-public-host <host-or-ip>
--webhook-acme-email <email>
--webhook-secret-file <path>
```

Interactive initialization asks for an optional public host. Leaving it empty
creates the private stack only. Non-interactive `--yes` mode enables the
gateway only when `--webhook-public-host` is explicitly present.

When enabled during initialization, the generated overlay and Caddyfile are
written before the stack starts. Dry-run reports the planned files and public
URL without printing or writing a secret.

### 6.2 Existing deployment

The CLI provides:

```text
dotcraft stack webhook enable --public-host <host-or-ip> \
  [--acme-email <email>] [--secret-file <path>] [--dir <path>] [--dry-run]
dotcraft stack webhook status [--dir <path>]
dotcraft stack webhook disable [--dir <path>]
```

`enable`:

1. validates the deployment and public host;
2. reads an existing non-empty secret from `.env`, or reads the requested
   secret file, or generates a 32-byte URL-safe secret;
3. validates that host ports `80` and `443` are available unless the existing
   managed gateway owns them;
4. atomically updates `.env` with mode `0600` on Unix;
5. writes only CLI-marked generated overlay and Caddy files;
6. validates the merged Compose model and Caddyfile;
7. starts the gateway and recreates Oratorio so the secret is injected.

Repeated enable preserves the secret. A newly generated secret is printed only
after a successful first enable. Dry-run performs validation and reports
actions without writing files, starting containers, or printing secret values.

`status` reports whether ingress is enabled, the public webhook URL, whether the
secret is configured, and the gateway container status. It never prints the
secret.

`disable` stops and removes the gateway, then removes only CLI-marked generated
overlay and Caddy files. It preserves the webhook secret in `.env`, Caddy named
volumes, the base stack, and the loopback Oratorio service.

If a target overlay or Caddyfile exists without the CLI ownership marker,
enable and disable fail without overwriting or deleting that file.

## 7. Secret Handling

The `.env` key is:

```text
ORATORIO_GITHUB_WEBHOOK_SECRET
```

The Compose overlay maps it to:

```text
Oratorio__GitHub__WebhookSecret
```

Generated secrets contain 32 random bytes encoded with URL-safe base64 without
padding. A secret file must contain one non-empty value after trimming terminal
line endings. The CLI never includes a secret in dry-run, status, doctor,
Compose validation output, Caddyfile, documentation, or error messages.

Updating `.env` preserves unrelated keys and comments. The replacement is
written through a same-directory temporary file followed by an atomic rename.
On Unix the final file mode is `0600`.

## 8. Operational Behavior

`status`, `logs`, `restart`, and `upgrade` automatically use the merged Compose
model whenever the managed overlay exists.

When ingress is enabled, `doctor` additionally checks:

- webhook secret presence, reported only as `configured` or `missing`;
- CLI ownership and presence of the overlay and Caddyfile;
- successful merged Compose validation;
- successful Caddyfile adaptation or validation;
- gateway container running state;
- host listeners on TCP `80` and `443`.

Ingress checks are absent for a private-only deployment. A failed check does not
mutate the deployment.

## 9. Operator Responsibilities

The CLI prints:

```text
https://<host-or-ip>/api/v1/sources/github/webhook
```

The operator must:

- point the DNS name to the host when using a domain;
- allow inbound TCP `80` and `443`;
- keep `5087` and the DotCraft ports closed publicly;
- enable the GitHub App webhook, paste the same secret, keep SSL verification
  active, and subscribe to `Issue comments`;
- test a delivery and the `@dotcraft-ai review` command in an open,
  configured pull request.

## 10. Failure Rules

- Port conflicts fail before the CLI changes a previously private deployment.
- Invalid host, missing base deployment files, invalid Compose, and invalid
  Caddy configuration fail closed.
- Existing webhook enable is idempotent when its generated files and settings
  are compatible.
- An interrupted file update leaves either the previous complete file or the
  new complete file, never a partially written `.env`.
- Disabling ingress must not stop or recreate the base Oratorio or DotCraft
  services.

## 11. Acceptance Checklist

- [x] Domain and public IP inputs render valid, distinct Caddy TLS policy.
- [x] Invalid and non-public hosts are rejected.
- [x] The generated Compose model keeps `5087` on loopback and publishes only
      gateway ports `80` and `443` publicly.
- [x] Only the exact GitHub webhook POST reaches Oratorio.
- [x] GitHub request body and signature/event headers reach Oratorio unchanged.
- [x] Secret generation, import, preservation, redaction, atomic write, and
      Unix permissions are tested.
- [x] Enable, dry-run, repeat enable, user-file conflict, status, and disable
      behavior are tested.
- [x] Existing lifecycle commands automatically include enabled ingress.
- [x] Doctor adds redacted ingress checks only when enabled.
- [x] Official Docker Compose and Caddy validation run in CI.
- [x] English and Chinese server deployment and GitHub integration docs remain
      structurally aligned.
- [x] Backend, CLI, documentation, and formatting checks pass.
