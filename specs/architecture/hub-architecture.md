# DotCraft Hub Local Coordinator Specification

| Field | Value |
|-------|-------|
| **Version** | 0.6.0 |
| **Status** | Living |
| **Date** | 2026-09-06 |
| **Related Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Default Chat Workspace](../features/default-chat-workspace.md), [Desktop Client](../clients/desktop-client.md), [Remote Tool Host](remote-tool-host.md), [Satellite](../clients/satellite.md) |

Purpose: Define DotCraft Hub as a local coordinator that discovers, starts, reuses, monitors, and stops workspace-bound AppServer processes and a small set of product-owned local services without changing the AppServer Protocol or replacing DotCraft's per-workspace runtime model. Hub is also the rendezvous point for paired Remote Tool Hosts on other machines: it accepts their outbound connections and relays each execution session to a local AppServer as an opaque byte stream.

This specification is the canonical Hub design. Earlier interim specs have been consolidated here and removed.

---

## 1. Motivation

DotCraft is intentionally workspace-centric:

- One AppServer process owns one workspace runtime.
- The AppServer loads that workspace's `.craft/` state, sessions, memory, skills, tools, MCP servers, channels, and configuration.
- Desktop, CLI, ACP, and future clients speak AppServer Protocol to a workspace-bound server.

This ownership model is a feature and must be preserved.

The local coordination problem is that multiple clients can open the same workspace without knowing an AppServer already exists. If each client starts its own stdio AppServer, the workspace can end up with duplicate processes competing for files, MCP side effects, background work, dashboard ports, API ports, and runtime state.

Hub solves that by acting like a local container manager:

- Each workspace still has its own AppServer.
- Hub does not host workspace runtimes.
- Hub does not proxy normal AppServer Protocol traffic. It does relay Remote Tool Host sessions between a local AppServer and a paired remote machine, as an opaque byte bridge that never interprets the relayed traffic (§6.1).
- Hub helps local clients find or create the correct AppServer and then gets out of the hot path.

---

## 2. Design Principles

1. **Preserve per-workspace AppServer ownership.** Hub must not become a multi-workspace runtime process.
2. **Do not change AppServer Protocol for local coordination.** No workspace routing fields are added to AppServer methods.
3. **Keep Hub off the conversation hot path.** After bootstrap, clients connect directly to the AppServer WebSocket endpoint.
4. **Use stdio for supervision, WebSocket for sharing.** Hub uses stdio to supervise managed AppServers; local clients use WebSocket to share the same AppServer.
5. **Keep Hub local and single-user.** The Hub Local API always binds to loopback and uses same-user local trust assumptions. Hub may additionally open a separate, opt-in satellite listener on a LAN address that serves only Remote Tool Host pairing and transport routes; the two listeners are separate applications with separate route tables, and no `/v1/*` endpoint is ever reachable from the satellite listener.
6. **Keep standalone AppServer valid.** `dotcraft app-server` remains available for explicit remote hosting, CI, bots, and debugging.
7. **Keep UI ownership in Desktop.** Hub is headless; tray and OS notifications belong to Desktop/Electron.
8. **Keep product services closed and explicit.** Hub may supervise product-owned local services registered by DotCraft composition, but it is not a native-process extension point for plugins.

---

## 3. Architecture

```text
dotcraft hub
  - Hub Local API on loopback
  - workspace registry
  - AppServer supervisor
  - in-memory product service supervisor
  - lifecycle events
  - satellite peer registry and opt-in satellite listener

dotcraft app-server, one per workspace
  - cwd = workspace root
  - owns WorkspaceRuntime and .craft state
  - exposes AppServer Protocol over WebSocket
  - holds workspace appserver.lock

Desktop / CLI
  - locate or start Hub
  - ask Hub to ensure the workspace AppServer
  - connect directly to the returned AppServer WebSocket URL
```

Hub state lives under `~/.craft/hub/`. Hub itself only loads global configuration and must not require the current directory to be a `.craft` workspace.

Hub also defines the default Chat workspace path `~/.craft/workspaces/chats` as a reusable local bootstrap target. This path is still a normal workspace root; Hub does not add any chat-specific AppServer Protocol routing.

---

## 4. Hub Process

Hub is started by `dotcraft hub` and runs as a global, per-user background process.

Core properties:

- It is workspace-independent.
- It uses a single-instance `hub.lock` discovery file.
- It exposes a loopback HTTP JSON API.
- It publishes a random bearer token in `hub.lock`.
- It stores best-effort registry metadata under `~/.craft/hub/appservers.json`.
- It reports `tray=false`; tray presence is a Desktop capability, not a Hub capability.

Hub may be started explicitly by the user, or automatically by Desktop, CLI, or tray bootstrap.

---

## 5. Managed AppServer

A Hub-managed AppServer is still a normal `dotcraft app-server` process.

Managed launch contract:

- Working directory is the workspace root.
- AppServer mode is `StdioAndWebSocket`.
- WebSocket host is loopback.
- WebSocket port and token are allocated by Hub.
- Dashboard ports are allocated by Hub when the service is enabled and available.
- Runtime overrides are injected ephemerally and must not rewrite `.craft/config.json`.
- Hub keeps the stdio supervisor connection open for readiness and graceful shutdown.

Readiness requires:

- Process is alive.
- Stdio `initialize` handshake succeeds.
- WebSocket endpoint accepts an AppServer `initialize` probe.
- Workspace `appserver.lock` is owned by the expected process.

---

## 6. Hub Local API

Hub Local API is separate from AppServer Protocol. It must not expose `thread/*`, `turn/*`, `approval/*`, `mcp/*`, `skills/*`, `workspace/config/*`, or normal AppServer extension methods.

Transport:

- HTTP JSON over loopback.
- Discovery through `~/.craft/hub/hub.lock`.
- Mutating and management calls require `Authorization: Bearer <token>`.
- `GET /v1/status` is public for local discovery.

Required endpoints:

| Endpoint | Purpose |
|----------|---------|
| `GET /v1/status` | Return Hub metadata and capabilities. |
| `POST /v1/shutdown` | Stop Hub and Hub-managed AppServers. |
| `POST /v1/appservers/ensure` | Ensure a workspace AppServer and optional workspace sidecars exist, then return connection metadata. |
| `GET /v1/appservers` | List live and known AppServer registry entries. |
| `GET /v1/appservers/by-workspace?path=...` | Inspect one workspace without starting it. |
| `POST /v1/appservers/stop` | Stop a managed AppServer. |
| `POST /v1/appservers/restart` | Restart a workspace AppServer through Hub. |
| `POST /v1/services/ensure` | Start or reuse a registered product-owned local service. |
| `GET /v1/services/by-id?id=...` | Inspect one registered local service without starting it. |
| `POST /v1/services/stop` | Stop a Hub-managed local service. |
| `POST /v1/services/restart` | Restart a registered local service. |
| `GET /v1/events` | Stream Hub lifecycle events as SSE. |
| `POST /v1/notifications/request` | Accept a local notification request and emit a Hub event. |
| `GET /v1/satellites` | List paired Remote Tool Hosts with their online state and last reported workspaces. |
| `POST /v1/satellites/invites` | Mint a one-time pairing invitation, optionally carrying a purpose, and start the satellite listener when it is not running. |
| `DELETE /v1/satellites/{peerId}` | Revoke a pairing and close its live connections. |
| `GET /v1/satellites/{peerId}/bridge?session=...` | WebSocket. Open one relayed Remote Tool Host session for a local AppServer. |

The Hub Local API remains loopback-only; the satellite routes above are for local AppServers and local clients, not for the remote machine.

Errors use this shape:

```json
{
  "error": {
    "code": "workspaceLocked",
    "message": "A live process appears to own the workspace.",
    "details": {}
  }
}
```

Default Chat helpers do not add another Hub endpoint. They resolve and initialize `~/.craft/workspaces/chats`, then call `POST /v1/appservers/ensure` with that concrete `workspacePath`.

Common error codes include `unauthorized`, `workspaceNotFound`, `workspaceLocked`, `appServerStartFailed`, `appServerUnhealthy`, `portUnavailable`, `invalidNotification`, `satelliteNotFound`, `satelliteOffline`, `inviteInvalid`, `sessionConflict`, and `hubInternalError`.

### 6.1 Satellite listener

The satellite listener is a second, opt-in HTTP application that Hub binds to a configurable LAN address and a fixed port (`Hub.SatelliteHost`, `Hub.SatellitePort`, default `47600`). It is disabled by default, starts when the first invitation is minted or when at least one pairing exists, and serves only these routes:

| Route | Purpose |
|-------|---------|
| `GET /i/{inviteId}` | The invitation, in the representation the caller asks for. |
| `GET /satellite/installer` | Download the Satellite installer for this Hub's build. |
| `GET /satellite/control` | WebSocket. Control channel from a Remote Tool Host, authenticated by a one-time invite id on first connection or by the peer credential afterwards. |
| `GET /satellite/data?peer=...&session=...` | WebSocket. Data connection opened by a Remote Tool Host in answer to `openSession`, authenticated by the peer credential. |

`GET /i/{inviteId}` is content-negotiated and never consumes the invitation, so a person, a client, and the CLI may all read it, repeatedly, before anyone decides:

| `Accept` | Representation |
|----------|----------------|
| `application/json` | `{ inviteId, inviterDisplayName, purpose, expiresAt, hubEndpoint }`, the details a client needs to ask the machine owner for consent. |
| `text/html` | A page naming the inviter and the purpose, offering the installer download and an **Open in Satellite** action for `dotcraft://satellite/join?invite=<url-encoded invite URL>`, which it retries while the page stays open. It carries no external asset reference. |
| anything else | The plain-text join command, for a terminal. |

`GET /satellite/installer` serves the Satellite installer that ships beside the running DotCraft executable. When that file is absent, Hub answers `404` with a short message pointing at the published releases rather than fabricating a download. Both routes answer `no-store`.

Hub relays every data connection to the matching `/v1/satellites/{peerId}/bridge` WebSocket frame for frame, preserving message type and fragment boundaries. It never parses, rewrites, inspects, logs, or persists the relayed payload and never holds a Remote Tool Host lease. The port is fixed rather than allocated because invitation URLs and stored peer endpoints must survive a Hub restart; when the port is unavailable, minting an invitation fails with `portUnavailable` and Hub does not fall back to another port.

The control channel, its frames, heartbeats, reconnect behavior, and the pairing ceremony are specified by [Remote Tool Host](remote-tool-host.md) §8 and §9.

---

## 7. Registry, Locks, and State

### Hub Lock

`~/.craft/hub/hub.lock` is the discovery file for the live Hub process. It records:

- Hub pid.
- API base URL.
- bearer token.
- start time.
- Hub version.
- Optional binary path for the DotCraft executable that started the Hub.

The Hub keeps `hub.lock` open for its lifetime. The same file is both the cross-process mutex and the discovery metadata: other processes may read it, but cannot acquire or replace it while the owner holds the file handle. A leftover file with no live handle is stale and may be recovered before a new Hub publishes replacement metadata.

Clients must verify both process liveness and `/v1/status` before trusting the metadata. Local development clients may also compare the optional binary path against the expected development build and restart Hub when it points at another executable.

### Hub Registry

`~/.craft/hub/appservers.json` stores best-effort known AppServer metadata:

- workspace path and canonical path.
- display name.
- state.
- pid.
- endpoints and service status.
- server version.
- last started/seen/exited metadata.
- exit diagnostics and recent stderr.

The registry is not the source of truth for workspace ownership. The live OS process and workspace lock are authoritative.

If Hub restarts and sees an old live workspace lock, it may display or return that AppServer as external/known, but it must not silently take over a process handle it did not start.

### Satellite Registry

`~/.craft/hub/satellites.json` stores paired Remote Tool Hosts and pending invitations:

- peer id, display name, and the hash of the peer credential.
- machine name, operating system, user, and build version reported by the peer.
- last reported workspaces and last-seen time.
- pending invitations as the hash of the invite id, its label, its optional purpose, and its expiry.

Raw peer credentials and raw invite ids are never written to disk. Online state and open sessions are in-memory and Hub-lifetime scoped.

### Workspace Lock

Every AppServer, managed or direct, participates in `<workspace>/.craft/appserver.lock`.

The AppServer keeps `appserver.lock` open for its lifetime. The same file is both the cross-process mutex and the owner metadata, including pid, workspace path, managed-by-Hub flag, Hub URL, version, start time, and published endpoints. Other processes may read the metadata, but cannot acquire or replace the file while the owner holds it.

An existing lock file is recoverable only when no process holds its file handle. Empty, partially written, or stale metadata does not permit takeover while the handle remains held.

When Hub encounters a live lock owned by a process it does not supervise, it should probe the published `appServerWebSocket` endpoint. If the endpoint accepts an AppServer initialize handshake, `ensure` may return that endpoint as an external running AppServer without taking ownership of the process. If the endpoint is missing or unhealthy, Hub must keep the workspace protected and return `workspaceLocked`.

---

## 8. Lifecycle and Health

Managed AppServer states:

```text
stopped -> starting -> running -> unhealthy/exited
running -> stopping -> stopped
unhealthy/exited -> starting, when ensure or restart is requested
```

Hub start flow:

1. Canonicalize and validate the workspace.
2. Reuse a healthy managed entry if one exists.
3. Recover confirmed stale workspace locks, reuse a healthy external AppServer published by a live workspace lock, or refuse startup when a live lock cannot be safely reused.
4. Allocate local endpoints.
5. Start `dotcraft app-server`.
6. Complete stdio and WebSocket readiness checks.
7. Persist registry metadata.
8. Return the AppServer WebSocket endpoint.

Health checks are lightweight and only apply to processes supervised by the current Hub instance:

- process liveness.
- workspace lock ownership.
- short WebSocket `initialize` probe.

If health fails, Hub marks the entry `unhealthy`, records diagnostics, and emits `appserver.unhealthy`. Hub does not automatically restart unhealthy or exited AppServers; restart is explicit or triggered by a later `ensure`.

Closing Desktop or another local client does not stop a healthy Hub-managed AppServer and must not cancel already-started persisted turns. The client WebSocket connection and passive subscriptions are connection-scoped; active turn execution remains AppServer-scoped and continues in the background until completion, failure, cancellation, Hub shutdown, or explicit AppServer stop/restart.

`POST /v1/appservers/ensure` is a non-destructive reconnect/bootstrap operation for a healthy running AppServer. If a local client reconnects with sidecar settings that differ from the running process, Hub must return the existing AppServer endpoint instead of stopping or replacing the process. Services that require AppServer recreation to apply the new settings should be reported with `serviceStatus.<service>.state = "restartRequired"` and a diagnostic reason. Only explicit `POST /v1/appservers/restart`, `POST /v1/appservers/stop`, Hub shutdown, or a later ensure of an unhealthy/exited entry may stop a managed AppServer.

Hub shutdown stops AppServers it manages, releases local state, and removes its `hub.lock`.

### Product-owned local services

Hub may also supervise a fixed, composition-registered set of user-level product services. This is a deliberately thin process-lifecycle facility, not plugin discovery or a general process launcher. A client selects only a registered `serviceId` and may provide the resolved executable path; arguments, environment shape, state root, health path, and readiness contract are owned by DotCraft composition.

Service entries are in-memory and scoped to the current Hub lifetime. Concurrent ensure calls for the same service coalesce. Hub allocates a loopback endpoint and ephemeral bearer, starts the process with the standard `DOTCRAFT_MANAGED_SERVICE_*` environment, waits for a standard JSON ready record, and confirms `/health`. It detects process exit and supports explicit ensure, status, stop, and restart. Hub shutdown terminates services it started. It does not persist service credentials, automatically restart failed services, proxy service traffic, expose service discovery to plugins, or generalize AppServer management through this facility.

### Satellite peers

Hub accepts one outbound control connection per paired Remote Tool Host, tracks its online state from heartbeats, brokers data sessions on demand, and emits `satellite.joined`, `satellite.online`, `satellite.offline`, and `satellite.revoked` lifecycle events on SSE. Hub does not start, stop, supervise, or update the remote process; the remote machine owns its lifecycle. Hub shutdown closes all satellite connections; peers reconnect on their own when Hub returns.

---

## 9. Client Bootstrap and UX

Local clients should default to Hub-managed local mode:

1. Determine the target workspace.
2. Locate a live Hub through `hub.lock`.
3. Start Hub if no live Hub exists and auto-start is enabled.
4. Call `POST /v1/appservers/ensure`.
5. Connect to the returned AppServer WebSocket URL.
6. Perform normal AppServer Protocol handshake.
7. Continue without Hub in the normal conversation path.

Desktop and CLI expose local mode as Hub-managed local execution. Explicit remote WebSocket mode remains available and bypasses Hub.

Local mode does not require users to configure AppServer or Dashboard ports. Hub owns those runtime allocations for managed processes.

When a Desktop window closes during a running turn, notification delivery to that window is best-effort and may stop immediately. Reopening the workspace should reuse the same managed AppServer and recover the thread state through normal AppServer Protocol reads or subscriptions.

Clients should present failures as local runtime availability problems, such as:

- Hub could not start.
- Workspace is locked by another live process.
- Managed AppServer failed during startup.
- AppServer endpoint did not become ready.
- Managed AppServer became unhealthy or exited.

---

## 10. Tray and Notifications

Hub remains headless. Desktop owns the tray process.

Tray responsibilities:

- Run as an independent Desktop background process.
- Enforce one tray process per user.
- Start or discover Hub.
- Show Hub and known workspace status without exposing AppServer terminology in user-facing tray labels.
- Open recent or running workspaces.
- Restart or stop Hub-managed AppServers through Hub Protocol.
- Stop Hub and Hub-managed AppServers on tray Exit.
- Display OS notifications for Hub `notification.requested` events.

Notification flow:

1. A client or managed AppServer calls `POST /v1/notifications/request`.
2. Hub validates the request and emits `notification.requested` on SSE.
3. Desktop tray receives the event and displays the OS notification with the DotCraft app icon.
4. Clicking the notification opens the related action URL. Desktop workspace links may activate an existing workspace window before starting a new one.

Desktop task-completion notification settings apply to AppServer-managed turn result notifications (`turnCompleted` and `turnFailed`). `never` suppresses the OS notification, `always` displays it, and `whenUnfocused` displays it only when no focused Desktop window has the related workspace as its foreground workspace. Because tray runs in a separate process, it checks a Desktop workspace activation endpoint using a read-only window-state query; if the window state cannot be queried, the notification is treated as unfocused and remains visible.

Desktop may connect one window to multiple Hub-managed local AppServers at once. These secondary connections are client connections only: Hub continues to own one AppServer runtime per workspace, and Desktop must not start stopped recent workspaces merely to populate the multi-workspace UI.

Turn-related OS notifications are for user-visible work. AppServer-managed turn notifications must suppress internal-only helper threads, such as threads marked with `dotcraft.internal` metadata or known internal origins used for welcome suggestions and commit-message suggestions. User-visible copy should use the thread display name instead of the internal thread ID.

For AppServer-managed turn notifications, Desktop-opening actions are allowed only when the thread originated from `dotcraft-desktop`. Other origins may still request a notification, but they must not attach a `dotcraft://workspace/open` action and should set `openDesktopOnClick=false`.

Hub itself never displays OS UI.

---

## 11. Port and Endpoint Management

Managed endpoints bind to loopback by default.

Hub allocates ports for:

- AppServer WebSocket.
- Dashboard when enabled.

The satellite listener is the one endpoint that binds a non-loopback address, and it uses a fixed configured port (`Hub.SatellitePort`, default `47600`) instead of an allocated one, because invitation URLs and the endpoints stored by paired Remote Tool Hosts must survive a Hub restart.

If optional modules are disabled or unavailable, Hub reports service status as `disabled` or `unavailable` and still starts the AppServer.

Desktop and other local clients may pass local runtime tool hints, such as a resolved bundled `rg` path, Electron-as-Node path, Electron run-as-Node flag, bundled TypeScript modules directory, and bundled built-in plugin roots, in `POST /v1/appservers/ensure` or restart requests. Hub persists these hints under `~/.craft/hub/runtime.json` and forwards them only as AppServer process environment variables: `DOTCRAFT_RG_PATH`, `DOTCRAFT_NODE_BIN`, `DOTCRAFT_NODE_RUN_AS_NODE`, `DOTCRAFT_MODULES_DIR`, and `DOTCRAFT_BUILTIN_PLUGIN_ROOTS`. Hub must not expose secrets in runtime-tool status payloads; `serviceStatus.typescriptRuntime` may report `allocated`, `unavailable`, or `restartRequired`.

Hub must not silently rewrite unrelated user-configured ports for native channels, webhook modules, or future integrations unless a service explicitly participates in Hub-managed runtime overrides.

---

## 12. Security Model

Hub is a same-user local coordinator, not a security boundary against malicious processes running as the same OS user.

Security constraints:

- Hub API binds to loopback.
- Managed AppServer endpoints bind to loopback.
- Hub API uses bearer token authorization for protected endpoints.
- Managed AppServer WebSocket endpoints use per-process tokens when available.
- The satellite listener may bind a non-loopback address. It is disabled by default, serves no `/v1/*` route, and authenticates every connection with a one-time invite id or a per-peer bearer credential of which Hub stores only the hash. Profile v1 uses plain `ws://` and assumes a trusted intranet; invitations are single-use and expire.
- Remote or multi-user Hub scenarios beyond satellite pairing require a separate security design.

---

## 13. Compatibility

AppServer Protocol is unchanged. Clients still use existing AppServer methods after connecting to the workspace AppServer.

The default Chat workspace is compatible with this rule: it is a product alias for a concrete workspace path, not a new routing mode or thread type.

Existing AppServer modes remain valid:

| Mode | Status |
|------|--------|
| `stdio` | Supported for direct subprocess clients and debugging. |
| `websocket` | Supported for explicit remote/local hosting. |
| `stdio + websocket` | Required for Hub-managed AppServers. |

ACP itself remains an AppServer client bridge: it translates editor ACP stdio traffic to the existing AppServer wire protocol. It does not require AppServer Protocol changes. If local ACP mode starts its own workspace AppServer subprocess, only that bootstrap path may later choose to use Hub to avoid duplicate local AppServer ownership.

---

## 14. Remaining Work

The implemented Hub design still leaves several product and hardening areas for future work:

- Optional ACP local bootstrap alignment: ACP's protocol bridge is already AppServer-based; only its default local subprocess startup would need Hub if IDE integrations should share the same managed AppServer as Desktop and CLI.
- More complete Desktop multi-workspace management UI beyond recent local workspace secondary connections.
- Notification preferences such as quiet hours, per-workspace mute, and frequency control.
- Better recovery or explicit cleanup flow for live AppServers left behind after Hub restart.
- Optional named pipe or Unix socket transport for stronger local API ergonomics.
- Configurable Hub-managed port ranges.
- Idle shutdown or lease-based AppServer lifetime management.
- Manual packaged-app verification for tray behavior, OS notifications, and hidden Windows child processes.
- A TLS profile for the satellite listener.
- Pairing one Remote Tool Host with several Hubs at once.
