# DotCraft Remote Server Management Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-06-01 |
| **Related Specs** | [Desktop Client](../clients/desktop-client.md), [Desktop Visual Design](../clients/desktop-visual-design.md), [AppServer Protocol](../protocols/appserver-protocol.md), [Hub Architecture](hub-architecture.md) |
| **Reference** | [deploy/docker/README_ZH.md](../../deploy/docker/README_ZH.md), [deploy/docker/README.md](../../deploy/docker/README.md) |

Purpose: Define a Desktop-owned visual manager for remote DotCraft Docker stacks over SSH. Desktop manages multiple servers, and multiple DotCraft Compose stacks per server, using the system `ssh` client and a fixed allow-list of `docker compose` operations. It connects Desktop to a remote AppServer and Dashboard through local SSH tunnels, reusing the existing remote-connection path without changing AppServer Protocol or Hub Protocol.

---

## 1. Scope

### 1.1 What This Spec Defines

- A new Desktop **Servers** surface for managing remote DotCraft deployments.
- The settings schema for saved servers (hosts) and their stacks.
- The Desktop main-process API contract for remote host and stack management.
- The SSH execution and command-building model, including allow-listing and secret redaction.
- The DotCraft-specific Compose operations: status, logs, start/stop/restart, and one-click update.
- The tunnel-first connection model that bridges a remote stack into the existing Desktop remote AppServer flow.
- The UX workflow contract for the Servers surface (the visual contract is governed by [Desktop Visual Design](../clients/desktop-visual-design.md)).

### 1.2 What This Spec Does Not Define

- A generic Docker UI or machine-operations panel. This feature only manages deployments that follow the DotCraft `deploy/docker` layout.
- Arbitrary remote shell execution. The renderer can never submit free-form commands; only fixed, parameterized operations run remotely.
- Changes to AppServer Protocol or Hub Protocol. Remote stacks are reached through the existing `ws://` remote-connection path.
- Auto-update, scheduled update, or agent-based pull deployment. The first version supports manual one-click update only.
- SSH password prompts or storage of private keys / passphrases. The first version is key/agent based only.

---

## 2. Design Principles

1. **DotCraft-shaped, not Docker-generic.** Every operation assumes the `deploy/docker` Compose layout from the deployment docs: a `dotcraft` service, an optional `opensandbox` profile, a mounted `./workspace`, a rendered `.env`, and a generated `workspace/.craft/appserver.token`.
2. **SSH-first.** Use the system `ssh` executable rather than bundling an SSH library, so the feature inherits `~/.ssh/config`, `ProxyJump`, `ssh-agent`, hardware keys, and editor-style host aliases for free.
3. **Fixed allow-list.** Remote commands are a closed set of parameterized operations. The renderer chooses an operation and a target stack; it never supplies a command string.
4. **No protocol changes.** Reuse the existing remote AppServer connection contract from [Desktop Client §3.1.1](../clients/desktop-client.md). Tunnels make a remote endpoint look like a local `127.0.0.1` endpoint to the existing probe/connect path.
5. **Tunnel-first access.** AppServer and Dashboard are reached only through local SSH tunnels. The feature does not require the remote `9100`/`8080` ports to be publicly exposed.
6. **Redaction by construction.** The AppServer token and any secret-bearing values are redacted from logs, errors, settings views, and operation history at the boundary, not best-effort after the fact.
7. **Neutral, calm operational UI.** The surface follows the neutral-first design posture; semantic color is reserved for state and risk.

---

## 3. Architecture

```text
DotCraft Desktop
  -> system ssh / ssh config / ssh-agent
  -> remote deploy/docker directory
  -> docker compose status/logs/pull/up/restart
  -> local SSH tunnels for AppServer and Dashboard
  -> existing Desktop remote AppServer connection flow
```

- All remote work is performed by the Desktop **main process**, never the renderer. The renderer issues high-level requests over IPC and renders returned state.
- The main process spawns the system `ssh` binary for command execution and for tunnel (`-L` local port-forward) lifecycle.
- "Open in Desktop" reads the remote `workspace/.craft/appserver.token`, opens a local AppServer tunnel, then drives the existing remote-connection apply path with a `ws://127.0.0.1:<localPort>/ws` URL and the token. Desktop's connection state machine and capabilities flow are unchanged.
- "Open Dashboard" opens a separate Dashboard tunnel and points the browser surface at `http://127.0.0.1:<localPort>/dashboard`.

---

## 4. Domain Model and Settings Schema

Two levels: a **Host** is an SSH target; a **Stack** is one DotCraft Compose deployment on that host. One host has many stacks.

Saved servers live in Desktop client settings (not workspace config), because they describe this machine's view of remote deployments.

```jsonc
// AppSettings.remoteHosts: RemoteHost[]
{
  "id": "h_01J...",            // stable client-generated id
  "name": "DotCraftCloud",     // display name
  "sshTarget": "user@cloud",   // user@host, host, or ~/.ssh/config alias
  "identityFile": "~/.ssh/id_ed25519", // optional; key/agent only
  "stacks": [
    {
      "id": "s_01J...",
      "name": "prod",
      "composeDir": "~/dotcraft/deploy/docker", // dir containing compose file + .env
      "workspaceDir": "~/dotcraft/deploy/docker/workspace", // optional; defaults to <composeDir>/workspace
      "projectName": "dotcraft",   // optional; docker compose -p
      "appServerPort": 9100,        // remote AppServer port inside the stack
      "dashboardPort": 8080,        // remote Dashboard port
      "sandboxProfile": false       // when true, operations pass --profile sandbox
    }
  ]
}
```

Normalization rules:

- `id` values are generated by the main process; renderer-supplied ids on create are ignored.
- `sshTarget` is trimmed; empty target is invalid. A target containing whitespace or shell metacharacters that are not valid in an alias / `user@host` is rejected.
- `composeDir` and `workspaceDir` are validated as absolute or `~`-relative POSIX paths. `..` traversal that escapes the configured directory is rejected at the command-building layer.
- `appServerPort` / `dashboardPort` default to `9100` / `8080` and must be valid TCP ports.
- Unknown fields are dropped on read; missing optional fields fall back to documented defaults.
- The AppServer token is **never** stored in settings. It is read live over SSH at connection time.

---

## 5. Main-Process API Contract

Exposed to the renderer via the existing preload bridge (`window.api.*`) and handled in the main IPC layer. All methods are async and return redacted, serializable results. All operations target a saved `hostId` (+ `stackId` where applicable); none accept a command string.

### 5.1 Host management

| Method | Input | Result |
|--------|-------|--------|
| `remoteHosts.list` | — | `RemoteHost[]` (token-free) |
| `remoteHosts.create` | `{ name, sshTarget, identityFile? }` | created `RemoteHost` |
| `remoteHosts.update` | `{ id, patch }` | updated `RemoteHost` |
| `remoteHosts.delete` | `{ id }` | `{ ok }` (also tears down any tunnels for its stacks) |
| `remoteHosts.test` | `{ id }` or draft | `SshTestResult { reachable, latencyMs?, dockerOk?, composeOk?, errorCode?, message }` |

`remoteHosts.test` validates SSH reachability and, on success, probes for `docker` and `docker compose`. It may optionally return **discovered stacks** (candidate `deploy/docker` directories) to support the add-server discovery step (§9.5).

### 5.2 Stack management

| Method | Input | Result |
|--------|-------|--------|
| `remoteStacks.list` | `{ hostId }` | `Stack[]` |
| `remoteStacks.status` | `{ hostId, stackId }` | `StackStatus` (see §6.1) |
| `remoteStacks.logs` | `{ hostId, stackId, service?, tail? }` | bounded, redacted log text + cursor |
| `remoteStacks.action` | `{ hostId, stackId, action }` where `action ∈ { start, stop, restart, update }` | `OperationResult` |
| `remoteStacks.openAppServerTunnel` | `{ hostId, stackId }` | `{ localUrl, localPort, token }` (token used immediately by the connect path; never persisted) |
| `remoteStacks.openDashboardTunnel` | `{ hostId, stackId }` | `{ localUrl, localPort }` |

`update` is the only compound operation and always follows the ordered flow in §6.3. Stack add/edit/remove are persisted through `remoteHosts.update` (a stack is a member of its host record).

### 5.3 Events

The main process may push progress for long operations (update, logs streaming, tunnel lifecycle) over the existing notification channel, keyed by `hostId`/`stackId`/`operationId`, so the renderer can render live step and log state. All pushed payloads are redacted.

---

## 6. Compose Operations

All operations run in the stack's `composeDir`, use the stack's `projectName` when set, and pass `--profile sandbox` when `sandboxProfile` is true. Output is bounded and redacted before it leaves the main process.

### 6.1 Status

`remoteStacks.status` verifies, in one bounded SSH round-trip where possible:

- `docker` and `docker compose` are available;
- the `composeDir`, the compose file, and `.env` exist;
- `workspace/.craft/config.json` exists and `workspace/.craft/appserver.token` is present (presence only — never the value);
- container/service state from `docker compose ps`;
- current image tag and digest from the running containers / image labels.

`StackStatus` summarizes into a coherent health state:

| Health | Meaning |
|--------|---------|
| `running` | All expected services up. |
| `partial` | Some but not all expected services up (e.g. `1/2`). |
| `stopped` | No services running, no crash. |
| `unhealthy` | A service is restarting / exited non-zero / failing healthcheck. |
| `unknown` | Status could not be determined (SSH or Docker error). |

Status also reports `composeOk`, `envOk`, `configOk`, `tokenPresent`, `imageTag`, `imageDigestShort`, and a list of services with per-service state.

### 6.2 Logs

`remoteStacks.logs` streams bounded `docker compose logs --tail <N> --no-color` for the stack or a single service. Output is redacted (token and known secret patterns) before render. The renderer requests a `tail` size and an optional `service` filter; it does not follow logs indefinitely without a bound.

### 6.3 Lifecycle and Update

- `start` / `stop` / `restart` use Compose service/stack lifecycle commands for the stack (respecting profile).
- `update` runs a fixed ordered flow and reports each step:
  1. **Backup** — create a small timestamped copy of `.env` and `workspace/.craft/` metadata (config + channel json + token presence marker), under a backup directory beside the stack. Backups never include rendered secrets in plaintext logs.
  2. **Pull** — `docker compose pull` to fetch updated service images.
  3. **Up** — `docker compose up -d --remove-orphans` to recreate changed containers. Per Docker's documented behavior, `pull` fetches images and `up -d` recreates only changed containers while preserving mounted volumes (so `./workspace` and `.craft/` survive). See [compose pull](https://docs.docker.com/reference/cli/docker/compose/pull/) and [compose up](https://docs.docker.com/reference/cli/docker/compose/up/).
  4. **Refresh** — re-run status and report the result (`recreated` / `already up to date`).
- The update reports whether anything actually changed, derived from the pull/up output, rather than claiming an update when images were already current.

---

## 7. SSH Execution and Security

- The main process invokes the system `ssh` binary. Arguments (target, identity file, remote command) are passed as an argument vector — never assembled into a shell string on the local side.
- The remote command is built from the fixed operation template plus validated, individually-quoted parameters (paths, ports, service names, tail counts). Renderer input is treated as data, never as command syntax.
- Path parameters are validated (absolute or `~`-relative POSIX, no escaping traversal) before quoting.
- First version is **key/agent only**: no interactive password prompt, no passphrase capture, no private key storage. `BatchMode`-style non-interactive behavior is used so a missing key fails fast instead of hanging on a prompt.
- A bounded timeout applies to every remote operation; a hung SSH process is terminated and surfaced as a timed-out operation.
- **Redaction** is applied centrally before any SSH stdout/stderr, error, settings snapshot, or operation-history entry is returned to the renderer or written to disk: the AppServer token value and recognized secret env values (e.g. `*_TOKEN`, `*_SECRET`, `*_KEY`, `*_AES_KEY` from `.env`) are replaced with a masked marker. Token presence is reported as a boolean, never as the value.

---

## 8. Tunnel and Connection Model

- AppServer tunnel: a local `ssh -L <localPort>:127.0.0.1:<appServerPort> <sshTarget>` forward. The chosen local port is ephemeral and bound to `127.0.0.1` only.
- At connect time the main process reads `workspace/.craft/appserver.token` over SSH, opens the AppServer tunnel, and drives the existing remote apply path with `ws://127.0.0.1:<localPort>/ws` + token. The existing test-and-connect probe, capabilities load, and connection state machine from [Desktop Client §3.1.1](../clients/desktop-client.md) are reused unchanged.
- Dashboard tunnel: a separate `-L` forward; "Open Dashboard" points the browser surface at `http://127.0.0.1:<localPort>/dashboard`.
- Tunnels are owned by the main process and torn down on disconnect, on host/stack deletion, on workspace switch, and on app quit. A stale tunnel must never outlive its connection.
- Remote AppServer lifecycle is **not** owned by Desktop. Consistent with remote-mode rules, Desktop must not offer remote AppServer restart; container lifecycle (start/stop/restart of the stack) is a deployment action, distinct from AppServer process restart.

---

## 9. Desktop Servers Surface (UX Contract)

The visual contract is governed by [Desktop Visual Design](../clients/desktop-visual-design.md). This section defines the workflow contract; it does not freeze geometry.

### 9.1 Placement and Navigation

- The surface is a dedicated **Servers** tab in Settings (single-column, consistent with the settings grammar). It is separate from the existing Connections group.
- Navigation is **list → detail drill-in**: a list of saved servers; selecting one opens that server's detail view; a back affordance returns to the list. No new top-level navigation is introduced.

### 9.2 Server List

- Each server row shows: name, `sshTarget` summary, stack count, and an **SSH-reachability** status dot reflecting the last test (`not tested` / `reachable` / `unreachable` / `checking`).
- A small **"Active here"** tag marks a server when Desktop's current session is connected to one of its stacks. Host reachability and active-session state are distinct signals and must not be conflated.
- The list has at most one primary action: **Add server**.
- Empty state explains the feature in one line (manage remote DotCraft Docker stacks over SSH; uses system ssh and `~/.ssh/config`) with an Add action and a prerequisites link.

### 9.3 Server Detail

- Header: back affordance, server name, a **Test SSH** action, and an overflow for Edit/Remove. A read-only SSH summary (target, compose directory, auth note) sits below.
- When SSH is unreachable, the stacks region is replaced by a redacted error banner with a retry (Test SSH) and a troubleshooting link; stack actions are disabled.
- Stacks section lists one card per stack with an **Add stack** action.
- A **Recent operations** area shows timestamped, redacted entries (action, target, result) for that host.

### 9.4 Stack Card and Action Hierarchy

Per the visual spec's "at most one primary action per decision area," each stack card tiers its actions:

| Tier | Actions | Treatment |
|------|---------|-----------|
| Primary | **Open in Desktop** → **Disconnect** when this stack is the active session | neutral inverted button |
| Secondary | **Dashboard**, **Logs** (toggle) | neutral bordered |
| Overflow `⋯` | **Update**, **Restart**, **Stop / Start**, **Edit stack**, **Remove** | menu; lifecycle and destructive actions live here |

- The card shows stack health (§6.1) as a status dot, the current image tag/version, and port info; when a tunnel is active it also shows the bound local port.
- **Update available is informational, not risk** — it is shown as a neutral/info pill, never a warning color. When surfaced, **Update** is promoted from the overflow to an inline affordance on the card.
- Destructive actions (Stop, Remove) use explicit copy and require confirmation, with neutral chrome and an `--error` affordance.

### 9.5 Add / Edit Server

- Add/Edit server uses the same Settings drill-in pattern as MCP server configuration: selecting **Add server** or **Edit server** replaces the list/detail content with a second-level settings page and a Back affordance. It must not open a nested modal.
- The page collects: name, SSH target (with helper text noting system ssh / `~/.ssh/config` / ProxyJump / ssh-agent support), and an optional identity file override (key/agent only, no password). Leaving the identity override empty is the recommended path and must reuse the user's normal SSH configuration, agent, and default keys.
- The page should inspect the local SSH setup where possible and surface concrete `~/.ssh/config` `Host` aliases plus existing local key candidates. Choosing an alias should set the SSH target without forcing an identity override, so the system SSH client remains responsible for HostName/User/Port/IdentityFile/ProxyJump resolution.
- On a successful **Test**, the flow may present **discovered stacks** for one-click import. If discovery is unavailable or declined, the user creates the host and adds stacks manually from the detail view. Discovery is an enhancement; manual add is always available.

### 9.6 Add / Edit Stack

- A modal collects: name, compose directory, optional workspace directory (defaulting to `<composeDir>/workspace`), optional project name, AppServer port (default `9100`), Dashboard port (default `8080`), and a sandbox-profile toggle.
- The AppServer token is never entered. The UI shows token presence as "present / missing" only.

### 9.7 Logs Presentation

- Logs appear as an **inline expandable panel** under the stack card: a fixed-height, scrollable, monospace region with a service switcher, bounded by `--tail N --no-color`, redacted before render, with auto-scroll that pauses on manual scroll.

### 9.8 Single Source of Truth With Connections Settings

- "Open in Desktop" ultimately drives the same connection state used by the Connections group. When a Servers stack is the active session, the Connections group shows a read-only banner ("Connected via Servers ▸ &lt;host&gt; / &lt;stack&gt;") with a link back to Servers, instead of an editable raw URL. The raw URL/token form remains available for the manual/advanced case only. There must be one source of truth for the active connection.

---

## 10. First-Version Decisions

These are the v1 defaults; they are intended to be revisited in design review and as the implementation matures.

1. **Update detection.** v1 does not show a proactive "update available" pill. **Update** is always available in the overflow, and the result reports `recreated` vs `already up to date` after the pull. Proactive detection (registry digest compare) is deferred.
2. **Stack discovery.** The add-server flow includes an optional two-step **Test & discover** that imports detected `deploy/docker` stacks, degrading gracefully to manual stack entry.
3. **Connections source of truth.** The Connections group shows a read-only "Connected via Servers" banner while a Servers stack is active (§9.8).
4. **Action density.** Only Open / Dashboard / Logs sit on the stack card face; Update and lifecycle live in the overflow until state promotes them.

---

## 11. Test Plan

### 11.1 Unit

- Remote host/stack settings normalization (defaults, invalid ports, path validation, unknown-field dropping, id generation).
- Command-builder quoting and path validation (traversal rejection, metacharacter rejection, profile/project flags).
- Token and secret redaction from logs, errors, settings snapshots, and operation history.
- Compose status and update output parsing (health derivation, `recreated` vs `already up to date`).
- Tunnel URL construction and lifecycle cleanup.

### 11.2 Main process

- Mock SSH executor covering status, logs, start, stop, restart, update, and failure cases (unreachable, no docker, no compose, missing `.env`, timeout).
- Update step order is enforced: backup → pull → up → status refresh.
- The renderer cannot request an arbitrary command; only allow-listed operations execute.

### 11.3 Renderer

- Host/stack list, empty state, unhealthy/partial state, update confirmation, logs panel, disabled actions.
- Multi-host / multi-stack selection and drill-in navigation.
- "Open in Desktop" routes through the tunnel and the existing remote connection status, and the Connections group reflects the single source of truth.

### 11.4 Manual validation

- A local Linux VM or test server with `deploy/docker`.
- One stack without sandbox; one stack with the sandbox profile.
- AppServer and Dashboard reachable only through the SSH tunnel.
- Update from an older image/tag to latest, verifying volumes (`./workspace`, `.craft/`) survive.

---

## 12. Assumptions and Prior Art

- First version supports manual one-click update only, not auto-update or scheduled updates.
- Remote servers already have Docker Engine and Docker Compose v2 installed.
- The SSH user can run Docker commands without interactive sudo.
- DotCraft Docker stacks follow the current `deploy/docker` layout.
- No AppServer Protocol or Hub Protocol changes are required.
- Portainer/Edge Agent and Watchtower remain reference patterns, not dependencies; they are useful prior art for later agent-based management, but the first version stays SSH-first. See [Docker SSH access](https://docs.docker.com/engine/security/protect-access/), [Portainer Edge Agent](https://docs.portainer.io/admin/environments/add/docker/edge), and [Watchtower](https://containrrr.dev/watchtower/introduction/).

---

## 13. Related Specs

- [Desktop Client](../clients/desktop-client.md) — connection lifecycle, remote-mode ownership, settings surface.
- [Desktop Visual Design](../clients/desktop-visual-design.md) — color roles, action hierarchy, control styling.
- [AppServer Protocol](../protocols/appserver-protocol.md) — the connection contract reused over the tunnel.
- [Hub Architecture](hub-architecture.md) — local AppServer coordination (unchanged by this feature).
