# DotCraft Hub Local Coordinator Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |

Purpose: Define DotCraft Hub as a local coordinator that discovers, starts, reuses, monitors, and stops workspace-bound AppServer processes and a small set of product-owned local services, without changing the AppServer Protocol or the per-workspace runtime model. Hub is also the rendezvous point for paired Remote Tool Hosts and the gateway for paired phones; in both roles it relays connections as opaque byte streams.

## 1. Motivation

DotCraft is workspace-centric: one AppServer process owns one workspace runtime and its `.craft/` state, sessions, memory, skills, tools, MCP servers, channels, and configuration. Clients speak AppServer Protocol to that workspace-bound server. This ownership model must be preserved.

Multiple local clients can open the same workspace without knowing an AppServer already exists. If each starts its own stdio AppServer, duplicate processes compete for files, MCP side effects, background work, ports, and runtime state.

Hub acts like a local container manager:

- Each workspace still has its own AppServer; Hub does not host workspace runtimes.
- Hub does not proxy local clients' AppServer Protocol traffic. It relays Remote Tool Host sessions (§6.1) and paired phones' AppServer connections (§6.2) as opaque byte bridges that never interpret the traffic.
- Hub helps local clients find or create the correct AppServer and then gets out of the hot path.

## 2. Design Principles

1. **Preserve per-workspace AppServer ownership.** Hub must not become a multi-workspace runtime process.
2. **Do not change AppServer Protocol for local coordination.** No workspace routing fields are added to AppServer methods.
3. **Keep Hub off the conversation hot path.** After bootstrap, clients connect directly to the AppServer WebSocket endpoint.
4. **Use stdio for supervision, WebSocket for sharing.** Hub supervises managed AppServers over stdio; local clients share them over WebSocket.
5. **Keep Hub local and single-user.** The Hub Local API binds to loopback with same-user trust. A client on another machine reaches it only through an SSH local forward authenticated as the same OS user (§9.1). Hub may open two separate, opt-in LAN listeners: the satellite listener (Remote Tool Host pairing and transport only) and the mobile gateway (phone pairing and relay only). Each has its own route table; no `/v1/*` endpoint is reachable from either.
6. **Keep standalone AppServer valid.** `dotcraft app-server` remains available for explicit remote hosting, CI, bots, and debugging.
7. **Keep UI ownership in Desktop.** Hub is headless; tray and OS notifications belong to Desktop.
8. **Keep product services closed and explicit.** Hub may supervise product-owned services registered by DotCraft composition; it is not a native-process extension point for plugins.

## 3. Architecture

```text
dotcraft hub
  - Hub Local API on loopback
  - workspace registry
  - AppServer supervisor
  - in-memory product service supervisor
  - lifecycle events
  - satellite peer registry and opt-in satellite listener
  - mobile device registry and opt-in mobile gateway

dotcraft app-server, one per workspace
  - cwd = workspace root
  - owns the workspace runtime and .craft state
  - exposes AppServer Protocol over WebSocket
  - holds workspace appserver.lock

Local clients
  - locate or start Hub
  - ask Hub to ensure the workspace AppServer
  - connect directly to the returned AppServer WebSocket URL
```

Hub state lives under `~/.craft/hub/`. Hub loads only global configuration and does not require the current directory to be a workspace.

The default Chat workspace `~/.craft/workspaces/chats` is an ordinary workspace root that clients bootstrap through Hub; it adds no routing mode.

## 4. Hub Process

`dotcraft hub` runs as a global, per-user background process. It is workspace-independent, uses a single-instance `hub.lock` discovery file, exposes a loopback HTTP JSON API, publishes a random bearer token in `hub.lock`, stores best-effort registry metadata in `~/.craft/hub/appservers.json`, and reports `tray=false`. Hub may be started by the user or automatically by a client or the tray.

## 5. Managed AppServer

A Hub-managed AppServer is a normal `dotcraft app-server` process launched with:

- working directory = workspace root;
- mode `StdioAndWebSocket`, WebSocket on loopback;
- WebSocket port and token, and Dashboard ports when enabled, allocated by Hub;
- runtime overrides injected ephemerally, never rewriting `.craft/config.json`;
- the stdio supervisor connection kept open for readiness and graceful shutdown.

Readiness requires a live process, a successful stdio `initialize`, a WebSocket endpoint that accepts an `initialize` probe, and workspace `appserver.lock` owned by the expected process.

## 6. Hub Local API

The Hub Local API is separate from AppServer Protocol and must not expose `thread/*`, `turn/*`, `approval/*`, `mcp/*`, `skills/*`, `workspace/config/*`, or AppServer extension methods.

Transport: HTTP JSON over loopback, discovered through `~/.craft/hub/hub.lock`. Mutating and management calls require `Authorization: Bearer <token>`; `GET /v1/status` is public.

| Endpoint | Purpose |
|----------|---------|
| `GET /v1/status` | Return Hub metadata and capabilities. |
| `POST /v1/shutdown` | Stop Hub and Hub-managed AppServers. |
| `POST /v1/appservers/ensure` | Ensure a workspace AppServer and optional workspace sidecars exist, then return connection metadata. |
| `GET /v1/appservers` | List running AppServers, and those this Hub process started that have since stopped, with their exit diagnostics. |
| `GET /v1/appservers/by-workspace?path=...` | Inspect one workspace without starting it. |
| `POST /v1/appservers/stop` | Stop a managed AppServer. |
| `POST /v1/appservers/restart` | Restart a workspace AppServer through Hub. |
| `POST /v1/services/ensure` | Start or reuse a registered product-owned local service. |
| `GET /v1/services/by-id?id=...` | Inspect one registered local service without starting it. |
| `POST /v1/services/stop` | Stop a Hub-managed local service. |
| `POST /v1/services/restart` | Restart a registered local service. |
| `GET /v1/projects` | List the computer's projects (§7 Projects). |
| `POST /v1/projects/open` | Add a project, or mark an existing one as just opened. Body: `{ "path" }`; the folder must exist. |
| `POST /v1/projects/remove` | Remove a project from the list. Body: `{ "path" }`. Its files, threads, and running AppServer are untouched. |
| `GET /v1/events` | Stream Hub lifecycle events as SSE. |
| `POST /v1/notifications/request` | Accept a local notification request and emit a Hub event. |
| `GET /v1/satellites` | List paired Remote Tool Hosts with their online state, declared capabilities, and last reported workspaces. |
| `POST /v1/satellites/invites` | Mint a one-time pairing invitation and start the satellite listener when it is not running. |
| `DELETE /v1/satellites/{peerId}` | Revoke a pairing and close its live connections. |
| `GET /v1/satellites/{peerId}/bridge?session=...&kind=...` | WebSocket. Open one relayed session of the given kind with a paired Remote Tool Host: `tools` (the default) for a local AppServer, `screen` for a local client. A paired host without a live control connection is answered `503 satelliteOffline` before any capability check. |
| `GET /v1/mobile` | Mobile gateway state: `off`, `on`, or `failed` with a reason, the port, advertised addresses, paired phones, and the relay with its connection state. |
| `POST /v1/mobile/enable` | Turn the mobile gateway on and keep it on across Hub restarts. |
| `POST /v1/mobile/disable` | Turn the mobile gateway off and close every phone connection. Paired phones are kept. |
| `POST /v1/mobile/pairings` | Mint a phone pairing code and return its QR payload and expiry. |
| `POST /v1/mobile/pairings/{pairingId}/allow`, `/deny` | Allow or decline the phone that asked to pair with that code. |
| `DELETE /v1/mobile/devices/{deviceId}` | Revoke a paired phone and close its connections. |
| `PUT /v1/mobile/relay` | Set the relay URL and token that let phones reach the gateway from other networks. |
| `DELETE /v1/mobile/relay` | Remove the relay. |

`GET /v1/status` reports `capabilities.satellites: true` when Hub serves the `/v1/satellites*` routes and the satellite listener, and `capabilities.mobile: true` when it serves the `/v1/mobile*` routes and the mobile gateway, so a client can tell a Hub without them from a failure. The mutating `/v1/mobile*` routes other than `PUT /v1/mobile/relay` ignore any request body; clients may send `{}`. These routes serve local AppServers and local clients, never the remote machine or the phone.

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

`GET /v1/projects` answers `{ "projects": [{ "path", "displayName", "addedAt", "lastOpenedAt", "running" }] }`, most recently opened first. `displayName` is the folder name; `addedAt` is set when the project enters the list and never changes, so clients can keep a stable order; `running` reflects the live registry. `POST /v1/projects/open` answers the project object. Both mutating routes emit a payload-free `projects.changed` event; clients re-read the list.

The default Chat workspace uses `POST /v1/appservers/ensure` with its concrete `workspacePath`; it has no dedicated endpoint.

Common error codes include `unauthorized`, `workspaceNotFound`, `workspaceLocked`, `appServerStartFailed`, `appServerUnhealthy`, `portUnavailable`, `invalidNotification`, `satelliteNotFound`, `satelliteOffline`, `inviteInvalid`, `sessionConflict`, `sessionKindUnsupported`, `satelliteScreenUnsupported`, `gatewayOff`, `deviceNotFound`, `projectNotFound`, `invalidRequest`, and `hubInternalError`.

### 6.1 Satellite listener

The satellite listener is a second, opt-in HTTP application bound to a configurable LAN address and a fixed port (`Hub.SatelliteHost`, `Hub.SatellitePort`, default `47600`). It is off by default, starts when the first invitation is minted or when at least one pairing exists, and serves only:

| Route | Purpose |
|-------|---------|
| `GET /i/{inviteId}` | The invitation, in the representation the caller asks for. |
| `GET /satellite/installer` | Download the Satellite installer for this Hub's build. |
| `GET /satellite/control` | WebSocket. Control channel from a Remote Tool Host, authenticated by a one-time invite id on first connection or by the peer credential afterwards. |
| `GET /satellite/data?peer=...&session=...` | WebSocket. Data connection opened by a Remote Tool Host in answer to `openSession`, authenticated by the peer credential. |

`GET /i/{inviteId}` is content-negotiated and never consumes the invitation, so a person, a client, and the CLI may read it repeatedly before anyone decides:

| `Accept` | Representation |
|----------|----------------|
| `application/json` | `{ inviteId, inviterDisplayName, expiresAt, hubEndpoint }`, the details a client needs to ask the machine owner for consent. |
| `text/html` | A self-contained page naming the inviter, offering the installer and an **Open in Satellite** action for `dotcraft://satellite/join?invite=<url-encoded invite URL>`, retried while the page stays open. |
| anything else | The plain-text join command, for a terminal. |

`GET /satellite/installer` serves the Satellite installer shipped beside the running DotCraft executable; when absent, Hub answers `404` pointing at the published releases. Both routes answer `no-store`.

Hub relays every data connection to the matching `/v1/satellites/{peerId}/bridge` WebSocket frame for frame, preserving message type and fragment boundaries, for every session kind and with no size limit of its own. It never parses, rewrites, inspects, logs, or persists the payload and never holds a Remote Tool Host lease. The port is fixed because invitation URLs and stored peer endpoints must survive a Hub restart; when it is unavailable, minting an invitation fails with `portUnavailable` and Hub does not fall back to another port.

The control channel, its frames, heartbeats, reconnect behavior, and the pairing ceremony are specified by [Remote Tool Host](remote-tool-host.md) §8 and §9.

### 6.2 Mobile gateway

The mobile gateway is a second, opt-in listener that paired phones use to reach workspace AppServers. It serves HTTPS with a self-signed certificate that phones pin, on `Hub.MobileHost` and the fixed port `Hub.MobilePort` (default `47610`), and only `/m/*` routes: pairing, a project list, project ensure, a relayed AppServer WebSocket per project, and a gateway event socket. It refuses sources outside loopback and the private, link-local, shared (100.64/10), and unique-local ranges before the TLS handshake. Turning it on or off is a Hub Local API call, and the choice survives Hub restarts.

When a relay is configured and the gateway is on, Hub keeps an outbound WebSocket control channel to the user's relay, reconnecting with jittered backoff from 1 to 30 seconds. For each tunnel the relay opens, Hub connects the relay's accept route to a loopback TCP connection to the gateway port and copies bytes both ways without interpreting them, so a relayed phone reaches the gateway from loopback, completes TLS with the pinned certificate, and authenticates as on the local network. The relay runs as `dotcraft relay serve` on a server both sides reach; it is not part of Hub.

Hub emits `mobile.stateChanged`, `mobile.pairingRequested`, `mobile.pairingCancelled`, `mobile.devicePaired`, `mobile.deviceRevoked`, and `mobile.deviceSeen` on SSE. The `/m/*` routes, pairing ceremony, relay protocol, and event payloads are owned by the mobile client contract, not by this spec.

## 7. Registry, Locks, and State

### Hub Lock

`~/.craft/hub/hub.lock` is the discovery file for the live Hub. It records Hub pid, API base URL, bearer token, start time, Hub version, and optionally the path of the DotCraft executable that started Hub.

Hub keeps `hub.lock` open for its lifetime; the file is both the cross-process mutex and the discovery metadata. Other processes may read it but cannot acquire or replace it while the handle is held. A leftover file with no live handle is stale and may be recovered.

Clients verify both process liveness and `/v1/status` before trusting the metadata. Development clients may compare the binary path against their expected build and restart Hub when it differs.

### Hub Registry

`~/.craft/hub/appservers.json` stores best-effort metadata for running AppServers — workspace and canonical path, display name, state, pid, endpoints and service status, server version, last started/seen — so a restarted Hub can find them again. An entry leaves the file when its AppServer exits; exit diagnostics and recent stderr are kept in memory for the Hub's lifetime only. The registry is never a list of past workspaces.

The registry is not the source of truth for workspace ownership; the live OS process and workspace lock are. A restarted Hub that sees a live workspace lock may report that AppServer as external, but must not take over a process handle it did not start.

### Projects

`~/.craft/hub/projects.json` is the computer's one list of projects: local workspaces a user opened in a client and has not removed, each with its path, added time, and last-opened time. Every local client shows this list and nothing else as its projects. A client adds a project with `POST /v1/projects/open` when the user opens a workspace and removes it with `POST /v1/projects/remove`. Starting an AppServer never adds a project, so automation, CLI runs, and tests leave the list alone. The default Chat workspace is not a project. Projects on other machines belong to the client that connects to them.

### Satellite Registry

`~/.craft/hub/satellites.json` stores paired Remote Tool Hosts and pending invitations:

- peer id, display name, and the hash of the peer credential;
- machine name, operating system, user, and build version reported by the peer;
- last reported workspaces and last-seen time;
- pending invitations as the hash of the invite id, its label, and its expiry.

Raw peer credentials and invite ids are never written to disk. Online state and open sessions are in memory for the Hub's lifetime.

### Mobile Registry

`~/.craft/hub/mobile.json` stores the mobile gateway state:

- whether the gateway is on, so Hub turns it back on at start;
- paired phones: device id, display name, platform, OS version, app version, paired and last-seen times, and the hash of the device credential;
- the current pairing code as its hash, its pairing id, and its expiry;
- the computer id, 128 random bits created when the gateway first turns on, that names this computer to phones and on a relay;
- the relay URL and relay token, when configured.

`~/.craft/hub/mobile-certificate.pfx` holds the gateway certificate and private key, readable only by the user where the OS allows. Raw device credentials and pairing codes are never written to disk. The relay token is stored as given because Hub presents it to the relay, so `mobile.json` is created user-readable only on Unix-like systems. Connection and relay state are in memory for the Hub's lifetime.

### Workspace Lock

Every AppServer, managed or direct, holds `<workspace>/.craft/appserver.lock` open for its lifetime. The file is both the cross-process mutex and the owner metadata: pid, workspace path, managed-by-Hub flag, Hub URL, version, start time, and published endpoints. It is recoverable only when no process holds its handle; empty, partial, or stale metadata does not permit takeover while the handle is held.

When Hub finds a live lock owned by a process it does not supervise, it probes the published `appServerWebSocket` endpoint. If it accepts an AppServer `initialize`, `ensure` may return it as an external running AppServer without taking ownership. Otherwise Hub keeps the workspace protected and returns `workspaceLocked`.

## 8. Lifecycle and Health

Managed AppServer states:

```text
stopped -> starting -> running -> unhealthy/exited
running -> stopping -> stopped
unhealthy/exited -> starting, when ensure or restart is requested
```

Ensure flow:

1. Canonicalize and validate the workspace.
2. Reuse a healthy managed entry if one exists.
3. Recover confirmed stale workspace locks, reuse a healthy external AppServer published by a live lock, or refuse when a live lock cannot be safely reused.
4. Allocate local endpoints.
5. Start `dotcraft app-server`.
6. Complete stdio and WebSocket readiness checks.
7. Persist registry metadata.
8. Return the AppServer WebSocket endpoint.

The stdio `initialize` in step 6 has no deadline, because it covers the AppServer's whole cold start. Hub keeps waiting while the process runs, logs a warning once startup passes 30 seconds, and fails the start only when the process exits, closes stdio, answers with an error, or the ensure is cancelled.

Health checks apply only to processes supervised by the current Hub: process liveness, workspace lock ownership, and a short WebSocket `initialize` probe. On failure Hub marks the entry `unhealthy`, records diagnostics, and emits `appserver.unhealthy`. Hub never restarts automatically; restart is explicit or triggered by a later `ensure`.

Closing a local client does not stop a healthy managed AppServer or cancel started turns. Client connections and subscriptions are connection-scoped; turn execution is AppServer-scoped and continues until completion, failure, cancellation, Hub shutdown, or explicit stop/restart.

`POST /v1/appservers/ensure` is non-destructive for a healthy running AppServer. If a client reconnects with sidecar settings that differ from the running process, Hub returns the existing endpoint and reports services needing recreation as `serviceStatus.<service>.state = "restartRequired"` with a reason. Only explicit restart, stop, Hub shutdown, or ensure of an unhealthy/exited entry may stop a managed AppServer.

Hub shutdown stops AppServers it manages, releases local state, and removes `hub.lock`.

### Product-owned local services

Hub may supervise a fixed, composition-registered set of user-level product services. This is a thin process-lifecycle facility, not plugin discovery or a general process launcher. A client selects only a registered `serviceId` and may provide the resolved executable path; arguments, environment, state root, health path, and readiness are owned by DotCraft composition.

Service entries are in memory for the Hub's lifetime. Concurrent ensures for one service coalesce. Hub allocates a loopback endpoint and ephemeral bearer, starts the process with the standard `DOTCRAFT_MANAGED_SERVICE_*` environment, waits for a standard JSON ready record, and confirms `/health`. It detects exit and supports ensure, status, stop, and restart; shutdown terminates services it started. It does not persist service credentials, restart failed services automatically, proxy service traffic, or expose service discovery to plugins.

### Satellite peers

Hub accepts one outbound control connection per paired Remote Tool Host, tracks online state from heartbeats, brokers data sessions of either kind on demand, and emits `satellite.joined`, `satellite.online`, `satellite.offline`, and `satellite.revoked` on SSE. The first `satellite.joined` carries `{ peerId, inviteId }`, naming the consumed invitation; later presence events carry only `peerId`. Hub does not start, stop, supervise, or update the remote process. Hub shutdown closes all satellite connections; peers reconnect when Hub returns.

## 9. Client Bootstrap and UX

Local clients default to Hub-managed local mode:

1. Determine the target workspace.
2. Locate a live Hub through `hub.lock`.
3. Start Hub if none is live and auto-start is enabled.
4. Call `POST /v1/appservers/ensure`.
5. Connect to the returned AppServer WebSocket URL.
6. Perform the normal AppServer Protocol handshake.
7. Continue without Hub in the conversation path.

Explicit remote WebSocket mode remains available and bypasses Hub.

### 9.1 Remote Hub over SSH

A client may use the Hub of another machine it reaches over SSH. That Hub is an ordinary Hub run by the remote user: it binds loopback on its own machine, is single-user, and is unchanged by being used remotely.

- The client starts the remote Hub over SSH detached from the session; a second start while a Hub holds `hub.lock` exits without effect.
- The client reads the remote `hub.lock` over SSH, opens an SSH local forward to the port in `apiBaseUrl`, and trusts the metadata only after `GET /v1/status` answers through the forward.
- `POST /v1/appservers/ensure` takes the remote absolute workspace path. The client forwards the port of the returned `appServerWebSocket` endpoint and connects to it; it follows `appserver.running` and `port.allocated` events, or ensures again, when that port changes.

Remote client mode differs from local bootstrap:

- It never reads a local lock, checks a process id, or starts a local Hub.
- It never stops or restarts a remote Hub because its binary differs from the client's; the remote Hub is stopped only by an explicit user update of DotCraft on that machine.
- It never sends runtime tool hints, because local paths mean nothing on the remote machine.
- It never mints satellite invitations, so the remote satellite listener stays off.
- Disconnecting closes forwards only; the remote Hub and its AppServers keep running.

Local mode does not require users to configure AppServer or Dashboard ports.

When a client window closes during a running turn, notification delivery to it may stop immediately. Reopening the workspace reuses the same managed AppServer and recovers thread state through normal AppServer Protocol reads or subscriptions.

Clients present failures as local runtime availability problems: Hub could not start, the workspace is locked by another live process, the AppServer failed during startup or did not become ready, or it became unhealthy or exited.

## 10. Tray and Notifications

Hub remains headless and never displays OS UI. Desktop owns the tray process, which:

- runs as an independent Desktop background process, one per user;
- starts or discovers Hub;
- shows Hub and workspace status without AppServer terminology in user-facing labels;
- opens recent or running workspaces, and restarts or stops managed AppServers through Hub;
- treats tray Exit as exiting DotCraft Desktop: it asks every registered Desktop window on the machine to quit through its authenticated local control endpoint, then stops Hub and its AppServers, then exits. This applies to every window regardless of connection state; a failed or stale window registration must not block Hub shutdown;
- displays OS notifications for Hub `notification.requested` events.

Notification flow:

1. A client or managed AppServer calls `POST /v1/notifications/request`.
2. Hub validates it and emits `notification.requested` on SSE.
3. The tray displays the OS notification with the DotCraft app icon.
4. Clicking opens the related action URL; workspace links may activate an existing window.

A managed AppServer requests four kinds of turn notification, each naming the chat by its display name (never its internal id):

| Kind | Raised when | Desktop setting |
|------|-------------|-----------------|
| `turnCompleted` | A turn completes | Task completion: `never`, `always`, or `whenUnfocused` |
| `turnFailed` | A turn fails | Task completion |
| `approvalRequested` | A turn starts waiting on an approval | Approval requests: on (default) or off, shown only when unfocused |
| `inputRequested` | A turn starts waiting on a question | Questions: on (default) or off, shown only when unfocused |

`never` and off suppress the notification; `always` displays it. `whenUnfocused`, and the approval and question kinds while on, display it only when no focused Desktop window has the related workspace in the foreground. The tray queries a read-only Desktop window-state endpoint; if the state cannot be queried, the notification is treated as unfocused.

`approvalRequested` and `inputRequested` are raised once per request, when the AppServer starts waiting. Resolving a request raises nothing. Clients that receive or replay a pending request do not raise their own OS notification while this path can deliver it, so one request produces at most one notification however many clients watch the thread.

Turn notifications are for user-visible work: they are suppressed for internal-only helper threads (such as threads with `dotcraft.internal` metadata or internal suggestion origins) and SubAgent threads. Only threads originating from `dotcraft-desktop` may attach a `dotcraft://workspace/open` action; other origins set `openDesktopOnClick=false`.

A client may connect to multiple managed AppServers at once. These are client connections only; Hub still owns one runtime per workspace, and clients must not start stopped workspaces merely to populate a multi-workspace UI.

## 11. Port and Endpoint Management

Managed endpoints bind to loopback. Hub allocates ports for the AppServer WebSocket and, when enabled, the Dashboard.

The satellite listener and the mobile gateway are the only endpoints that bind a non-loopback address. Each uses a fixed configured port that paired peers store — `Hub.SatellitePort` (default `47600`) and `Hub.MobilePort` (default `47610`) — and neither falls back to another port when its own is taken. The mobile relay binds nothing on the computer: Hub dials out and reaches the gateway over loopback.

If optional modules are disabled or unavailable, Hub reports service status as `disabled` or `unavailable` and still starts the AppServer.

Local clients, never a remote client (§9.1), may pass runtime tool hints — bundled `rg` path, Node binary path, run-as-Node flag, bundled TypeScript modules directory, and built-in plugin roots — in ensure or restart requests. Hub persists them in `~/.craft/hub/runtime.json` and forwards them only as AppServer environment variables: `DOTCRAFT_RG_PATH`, `DOTCRAFT_NODE_BIN`, `DOTCRAFT_NODE_RUN_AS_NODE`, `DOTCRAFT_MODULES_DIR`, and `DOTCRAFT_BUILTIN_PLUGIN_ROOTS`. Runtime-tool status never exposes secrets; `serviceStatus.typescriptRuntime` may be `allocated`, `unavailable`, or `restartRequired`.

Hub does not rewrite user-configured ports for channels, webhook modules, or other integrations unless a service explicitly participates in Hub-managed runtime overrides.

## 12. Security Model

Hub is a same-user local coordinator, not a security boundary against malicious processes running as the same OS user.

- The Hub API and managed AppServer endpoints bind to loopback. The Hub API requires its bearer token on protected endpoints; managed AppServer WebSockets use per-process tokens when available.
- The satellite listener may bind a non-loopback address. It is off by default, serves no `/v1/*` route, and authenticates every connection with a one-time invite id or a per-peer bearer credential stored only as a hash. It uses plain `ws://` and assumes a trusted intranet; invitations are single-use and expire. Relayed screen frames inherit this profile.
- The mobile gateway may bind a non-loopback address. It is off by default, serves no `/v1/*` route, refuses sources outside loopback and private ranges before TLS, and serves TLS with a self-signed certificate that phones pin by fingerprint. Every route but pairing requires a per-device bearer credential stored only as a hash; pairing codes are single-use, expire after 10 minutes, and are never stored raw. A paired phone holds the authority of the Hub's OS user; Hub authenticates the relayed AppServer connection itself, so the phone never receives a Hub or AppServer token.
- The mobile relay connection is outbound only and authenticates with a user-chosen relay token. The relay sees only TLS ciphertext between phone and gateway; a relay or token holder can drop or refuse tunnels but cannot impersonate the gateway, because phones pin its certificate.
- A client on another machine reaches Hub only through an SSH local forward authenticated as the Hub's OS user (§9.1); the Hub token travels over that SSH session and is held only in the client's memory.
- Multi-user Hub scenarios, and remote access other than satellite pairing, the mobile gateway and relay, and SSH forwarding, require a separate security design.

## 13. Compatibility

AppServer Protocol is unchanged; clients use existing methods after connecting to the workspace AppServer. The default Chat workspace is a product alias for a concrete workspace path, not a routing mode or thread type.

| AppServer mode | Status |
|------|--------|
| `stdio` | Supported for direct subprocess clients and debugging. |
| `websocket` | Supported for explicit remote/local hosting. |
| `stdio + websocket` | Required for Hub-managed AppServers. |

ACP is an AppServer client bridge that translates editor ACP stdio traffic to the AppServer wire protocol and requires no protocol changes.
