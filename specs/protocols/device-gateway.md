# DotCraft Device Gateway

| Field | Value |
|---|---|
| Version | 0.10.4 |
| Status | Draft |
| Date | 2026-11-07 |
| Parent spec | [Hub Architecture](../architecture/hub-architecture.md) |
| Related Specs | [DotCraft Mobile](../clients/mobile.md), [Desktop Client](../clients/desktop-client.md), [AppServer Protocol](appserver-protocol.md), [Remote Machines over SSH](../features/remote-server-management.md) |

## 1. Overview

The device gateway lets a paired device control DotCraft on another computer, on the same network or
through a relay the user runs. A phone and a computer running Desktop pair and connect the same way.

```
Paired device ──TLS, pinned certificate──> Hub device gateway ──loopback──> workspace AppServers
      │                                     (pairing, devices,              (one per workspace,
      │                                      relay, project list)            protocol unchanged)
      └─ directly, or through a tunnel on the user's relay
```

Client behavior belongs to [DotCraft Mobile](../clients/mobile.md) and
[Desktop Client §6.15–§6.16](../clients/desktop-client.md#615-control-this-pc). Identifiers with a
`mobile` or `m` prefix name this gateway and say nothing about the type of client.

## 2. Goals

- Pair a device in one step, and let the user see and revoke every paired device on the computer.
- Let any paired device reach the computer's projects with one contract, whichever network it is on.
- Keep the conversation on the AppServer Protocol, unchanged and unread by the gateway or relay.
- Keep every credential and all content away from the relay.

## 3. Non-goals

- A hosted public relay, an account system, or discovering computers without pairing.
- Exposing the Hub Local API, or issuing Hub or AppServer tokens to devices.
- Installing or updating DotCraft on the computer. [Remote Machines over SSH](../features/remote-server-management.md) covers that.
- Reaching a standalone AppServer that no Hub manages.
- Controlling a computer that belongs to someone else.

## 4. Authority

A **computer** is one Hub and its user. A **paired device** holds one device credential for one
computer. The credential grants the authority of the computer's signed-in user over DotCraft and
whatever that user's agents can do; the product says so when the computer allows a pairing.
Every paired device holds the same authority, and clients decide which features they offer.

A client connects only through a transport that trusts the certificate whose fingerprint was pinned
at pairing, for both HTTPS and WebSockets. It verifies the certificate before sending the credential
and never falls back to system trust or unencrypted connections.

Hub authenticates each relayed connection to its AppServer itself, so the device credential is the
only secret a device holds.

## 5. Listener

The gateway is a Hub-hosted HTTPS application bound to `Hub.MobileHost` (default `0.0.0.0`) on the
fixed port `Hub.MobilePort` (default `47610`). It is off by default. Turning it on starts the
listener and records the choice in `~/.craft/hub/mobile.json`, so it returns when Hub restarts.
Turning it off stops the listener, closes every relayed connection and event socket, and keeps
paired devices. The port is fixed because paired devices store it; when it is unavailable, turning
the gateway on fails with `portUnavailable`, the state becomes `failed`, and Hub does not fall back
to another port.

The listener serves TLS only. When the gateway is first turned on, Hub creates a self-signed ECDSA
P-256 certificate whose subject alternative name is the computer name, valid for 20 years, stored as
`~/.craft/hub/mobile-certificate.pfx` and reused afterwards so the fingerprint survives restarts. The
fingerprint is the lowercase hexadecimal SHA-256 of the DER-encoded certificate. Devices pin the
fingerprint delivered in the pairing URL (§8) and refuse any other certificate.

The gateway accepts connections only from loopback, private IPv4 ranges (10/8, 172.16/12,
192.168/16), link-local addresses (169.254/16, fe80::/10), the shared range 100.64/10 used by
private networks such as Tailscale, and IPv6 unique local addresses (fc00::/7). An IPv4-mapped IPv6
source is judged by its IPv4 address. Any other source is closed before the TLS handshake.

Hub advertises the addresses a device may try: the IPv4 addresses of up network interfaces,
excluding loopback and auto-configured 169.254/16, with the default route's address first. The list
is part of the pairing URL and of `GET /m/hello`, so a device refreshes it on every successful
connection.

## 6. Routes

Every route except `POST /m/pair` requires `Authorization: Bearer <device credential>`; a missing,
unknown, or revoked credential is answered `401 unauthorized`. The gateway serves no `/v1/*` route.
Bodies are JSON with camelCase fields; times are ISO-8601 strings.

| Route | Purpose |
|---|---|
| `POST /m/pair` | Ask to pair with a pairing code; once the computer allows it, return a device credential (§8). |
| `GET /m/hello` | Computer id and name, DotCraft version, port, fingerprint, advertised addresses, and relay. Also refreshes the device's last-seen time. |
| `GET /m/projects` | The computer's projects, without starting any. |
| `POST /m/projects` | Add a folder to the computer's projects, or mark a listed one as just opened, through the same path as `POST /v1/projects/open`. It does not start the project. |
| `POST /m/projects/{projectId}/ensure` | Start or reuse the project's AppServer through the same path as `POST /v1/appservers/ensure`, as for a remote client: the device sends no runtime tool hints, and Hub uses the ones it has stored. |
| `GET /m/projects/{projectId}/appserver` | WebSocket. Relayed AppServer connection for a running project. |
| `GET /m/events` | WebSocket. Gateway events for this device (§7). |
| `DELETE /m/device` | The device removes its own pairing. Answers `204`. |

`POST /m/pair` request:

```json
{
  "code": "q3Zt…",
  "displayName": "Ann's Pixel",
  "platform": "android",
  "osVersion": "16",
  "appVersion": "0.8.1"
}
```

Response:

```json
{
  "deviceId": "dev_6f1c…",
  "credential": "Yx2…",
  "computer": {
    "computerId": "W2Hk9qD1xP7vZb3nLc0aTg",
    "name": "ANN-PC",
    "port": 47610,
    "fingerprint": "3f9c1b0e…",
    "addresses": ["192.168.1.20", "100.101.102.103"]
  }
}
```

`platform` is `ios`, `android`, `windows`, `macos`, or `linux`; `displayName` is required;
`osVersion` and `appVersion` are free-form. A malformed request is answered `400 invalidRequest`
before the code is checked, so it does not use the code up.

The first call uses the code up, opens a pairing request, and answers at once with
`202 { "status": "pending", "requestKey" }`, where `requestKey` is a Hub-generated secret for this
request. The device then repeats the call with that `requestKey` added. Each such call waits up to
10 seconds for the computer's decision, then answers `200` with the response above once the computer
allowed it, `202 { "status": "pending" }` while it has not decided, or `403 pairingDenied` if it
declined. A used code without that request's key is answered `400 pairingCodeInvalid`, so a copied
code cannot collect the credential the computer allowed for another device.

`GET /m/hello` answers `{ "computerId", "name", "version", "port", "fingerprint", "addresses", "relay" }`,
with the meanings above, `version` the DotCraft version, and `relay` either `{ "url" }` or `null`.

`computerId` is 128 random bits encoded as base64url, created with the gateway and kept in
`~/.craft/hub/mobile.json`. It never changes, even when the computer's name, addresses, or
certificate change, so a device keys everything about a computer by it.

`GET /m/projects` answers:

```json
{
  "projects": [
    {
      "projectId": "9c1d0e5a2b7f4c3d8e6a1b0f",
      "displayName": "dotcraft",
      "path": "D:/work/dotcraft",
      "kind": "project",
      "running": true,
      "lastActiveAt": "2026-10-03T08:12:00+00:00"
    }
  ]
}
```

`POST /m/projects` takes `{ "path" }`, an absolute path on the computer, and answers one project
object. A relative path is answered `400 invalidRequest`, and a folder that does not exist
`404 workspaceNotFound`. `POST /m/projects/{projectId}/ensure` answers one project object, with
`running` true once the AppServer is up.

The projects are Hub's project list ([Hub Architecture §7](../architecture/hub-architecture.md#7-registry-locks-and-state)),
plus the default Chat workspace; a project removed on the computer disappears from every device. A
`projectId` not in that list is answered `404 projectNotFound` by every `/m/projects/{projectId}`
route. `projectId` is the first 24 hexadecimal digits of the SHA-256 of the normalized workspace
path, case-folded on Windows, so it stays stable. `path` is the absolute workspace path on the
computer. `displayName` is the folder name; the default Chat workspace is always listed as `Chats`
with `kind` `chats` (otherwise `project`), and devices identify it by `kind`, never by name.
`lastActiveAt` is the last-opened time, or `null` for Chats; projects are ordered by it, most recent
first.

The relay route is opaque: Hub opens the AppServer's loopback WebSocket with that process's token
and copies frames both ways, preserving message type and fragment boundaries. It never parses,
rewrites, logs, or persists payloads. Closing either side closes the other. A relay for a project
whose AppServer is not running is answered `409 projectNotRunning` before the upgrade.

Errors use the Hub error shape ([Hub Architecture §6](../architecture/hub-architecture.md#6-hub-local-api)):

| Code | Status | When |
|---|---|---|
| `unauthorized` | 401 | Missing, unknown, or revoked device credential. |
| `invalidRequest` | 400 | Malformed pairing request, a project path that is not absolute, or a WebSocket route reached without an upgrade. |
| `pairingCodeInvalid` | 400 | Unknown, used, superseded, or expired pairing code, or a pairing request that ended. |
| `pairingDenied` | 403 | The computer declined the pairing request. |
| `workspaceNotFound` | 404 | The folder to add does not exist on the computer. |
| `projectNotFound` | 404 | No known project has that id. |
| `projectNotRunning` | 409 | A relay was requested for a project whose AppServer is not running. |
| `appServerStartFailed` | 500 | Ensure could not start the project. The error carries no workspace path or diagnostics. |
| `hubInternalError` | 500 | Unexpected failure. |

Hub closes a gateway WebSocket with reason `deviceRevoked` (status 1008) when the device is revoked,
and `gatewayOff` (status 1001) when the gateway turns off. When Hub itself stops, it closes them
without a reason, and the device treats the computer as offline.

## 7. Gateway events

`GET /m/events` carries one JSON text message per event:

| Message | When |
|---|---|
| `{ "type": "projectStarted", "projectId": "…" }` | A project's AppServer started or became reachable. |
| `{ "type": "projectStopped", "projectId": "…" }` | A project's AppServer stopped or exited. |
| `{ "type": "deviceRevoked" }` | This device was revoked; the socket closes next. |
| `{ "type": "gatewayOff" }` | The gateway is turning off; the socket closes next. |

Hub ignores messages a device sends on this socket. Revoking a device or turning the gateway off
closes that device's, or every device's, relays and event sockets within one second.

## 8. Pairing and devices

1. On the computer, the user turns on the gateway and starts adding a device. Hub mints a pairing
   code: 128 random bits encoded as base64url, single-use, valid for 10 minutes, stored only as a
   hash. Minting replaces the previous code, so only the most recent unexpired code is valid. Each
   code has a `pairingId` that names it in Hub events without revealing it.
2. The computer presents the pairing URL
   `dotcraft://pair?v=1&id=<computerId>&name=<computer>&port=<port>&fp=<sha256>&addr=<a1>,<a2>&code=<code>`,
   each value URL-encoded and addresses comma-joined, with `relay=<url>` added when a relay is
   configured. Desktop shows it both as a QR code and as a link to copy. The URL carries the
   fingerprint, so a device never trusts a certificate on first use.
3. The device connects to the first reachable path (§10.3) with the pinned certificate and calls
   `POST /m/pair`. Hub opens a pairing request and emits `mobile.pairingRequested`; the device asks
   the user to confirm on the named computer.
4. The computer, not the device, decides: it asks whether the named device may control this
   computer. Allowing makes the device's next call return a `deviceId` and a device credential of
   256 random bits encoded as base64url; Hub records the device only then and emits
   `mobile.devicePaired` with the code's `pairingId`. Declining answers the device `pairingDenied`.
5. A pairing request ends without a device when the computer declines, mints a new code, or turns
   the gateway off, or when the device has not called for 30 seconds, as when the user leaves the
   pairing screen; Hub then emits `mobile.pairingCancelled` unless the computer declined.
6. The device stores the credential in the operating system's protected storage, and the computer
   record (computerId, name, addresses, port, fingerprint, relay, deviceId), both keyed by
   `computerId`. Pairing again with a computer it already holds replaces that pairing once allowed
   and removes the old pairing from the computer.

The credential travels only in the `Authorization` header. It never enters URLs, logs, traces, model
context, or session data, and Hub never logs a pairing code. A pairing URL is a one-time secret: a
client never persists it. Hub stores the credential's hash with the device record in
`~/.craft/hub/mobile.json`: `deviceId`, display name, platform, OS version, app version, paired time,
and last-seen time.

Revoking a device on the computer deletes its record and closes its connections; the device receives
`deviceRevoked` on `/m/events` when connected, or `unauthorized` on its next request, and forgets
that computer. Removing a computer on the device calls `DELETE /m/device` when reachable and forgets
the computer either way. Neither touches the device's other computers.

## 9. Hub Local API for the gateway

The computer manages the gateway through the loopback Hub Local API routes listed in
[Hub Architecture §6](../architecture/hub-architecture.md#6-hub-local-api), protected by the Hub
token. Their gateway-specific results:

| Endpoint | Result |
|---|---|
| `GET /v1/mobile` | The gateway state object. |
| `POST /v1/mobile/enable` | The state object. A port conflict answers `409 portUnavailable` and leaves the state `failed` with that `failureCode`. |
| `POST /v1/mobile/disable` | The state object. Devices are kept. |
| `POST /v1/mobile/pairings` | `{ "pairingId", "qrPayload", "expiresAt" }`, where `qrPayload` is the pairing URL of §8. Answers `409 gatewayOff` unless the gateway is on. |
| `POST /v1/mobile/pairings/{pairingId}/allow` | `204`; the device receives its credential on its next call (§8). `404 pairingNotFound` when no open request has that `pairingId`. |
| `POST /v1/mobile/pairings/{pairingId}/deny` | `204`, or `404 pairingNotFound`. |
| `DELETE /v1/mobile/devices/{deviceId}` | `204`, or `404 deviceNotFound`. |
| `PUT` / `DELETE /v1/mobile/relay` | The state object (§10.2). |

The state object:

```json
{
  "state": "on",
  "computerId": "W2Hk9qD1xP7vZb3nLc0aTg",
  "port": 47610,
  "addresses": ["192.168.1.20", "100.101.102.103"],
  "devices": [
    {
      "deviceId": "dev_6f1c…",
      "displayName": "Ann's Pixel",
      "platform": "android",
      "osVersion": "16",
      "appVersion": "0.8.1",
      "pairedAt": "2026-10-03T08:00:00+00:00",
      "lastSeenAt": "2026-10-03T08:12:00+00:00",
      "connected": true
    }
  ],
  "relay": null
}
```

`state` is `off`, `on`, or `failed`. A `failed` state adds `failureCode`, such as `portUnavailable`,
and an English `failureMessage`. `computerId` is `null` until the gateway first turns on. `port` is
`Hub.MobilePort`. `lastSeenAt` is `null` until the device first connects; `connected` is true while
the device has a relay or event socket open. `relay` is described in §10.2.

Hub SSE events, in the standard event envelope:

| Event | `data` |
|---|---|
| `mobile.stateChanged` | The state object, whenever the gateway turns on, turns off, or fails, and whenever the relay or its `state` changes. |
| `mobile.pairingRequested` | `{ pairingId, displayName, platform }` when a device asks to pair with the code `pairingId` names. |
| `mobile.pairingCancelled` | `{ pairingId }` when a pairing request ends without a device and without being declined (§8). |
| `mobile.devicePaired` | `{ deviceId, displayName, platform, pairingId }`. |
| `mobile.deviceRevoked` | `{ deviceId }`, whether the computer or the device removed the pairing. |
| `mobile.deviceSeen` | `{ deviceId, lastSeenAt, connected }` when the device's first socket opens, when its last socket closes, and on `GET /m/hello` at most once a minute per device. |

## 10. Off-network access

A relay lets a device reach the gateway from any network. The user runs it on a server both sides
can reach, with `dotcraft relay serve` or the relay container image. The relay forwards bytes only:
the device still speaks TLS to the gateway with the pinned certificate and its device credential, so
the relay sees only ciphertext and never a credential, project, or chat.

### 10.1 Relay

`dotcraft relay serve --listen <url> --token <token>` serves three WebSocket routes, normally behind a
reverse proxy that terminates public TLS. The token may come from `DOTCRAFT_RELAY_TOKEN` instead of
`--token`, and the relay compares it in constant time.

| Route | Caller | Purpose |
|---|---|---|
| `GET /r/host?host=<hostId>` | Hub | Control channel for one computer, authenticated by `Authorization: Bearer <relay token>`. One live control channel per host id; a newer one replaces the older, which is closed with the reason `replaced`. |
| `GET /r/connect?host=<hostId>` | Device | Opens a tunnel to that computer. Answered `404 hostOffline` when the computer has no control channel. |
| `GET /r/accept?tunnel=<tunnelId>` | Hub | Hub's side of a tunnel, authenticated by the relay token. |

For each device connection the relay picks a random `tunnelId`, sends
`{ "type": "open", "tunnel": "…" }` on the host's control channel, and splices the device's socket
with Hub's `/r/accept` socket for that tunnel if it arrives within ten seconds, otherwise closing the
device's socket. Tunnel payloads are binary WebSocket messages carrying the raw bytes of one TCP
stream. The relay keeps no state beyond live sockets, logs no payloads, and closes a tunnel when
either side closes. Its errors use the Hub error shape; a missing or wrong token is answered
`401 unauthorized`.

The host id is the computer's `computerId` (§6). Reaching a host id still requires the pinned
certificate and a device credential.

### 10.2 Hub

When the gateway is on and a relay is configured, Hub keeps the control channel open with jittered
backoff from 1 to 30 seconds. For each `open` it connects `/r/accept` to a loopback TCP connection to
the gateway port and copies bytes both ways, so relayed devices arrive from loopback and follow §5
and §6 unchanged.

The relay URL and token are stored in `~/.craft/hub/mobile.json` and set with
`PUT /v1/mobile/relay` (`{ "url", "token" }`) or removed with `DELETE /v1/mobile/relay`. The state
object carries `relay: { "url", "state" }` with `state` `connecting`, `connected`, or `failed`, or
`relay: null`. `url` is the relay's base URL with an `https` or `wss` scheme (`http` and `ws` are
accepted for testing); Hub and devices append the route paths and connect with the matching
WebSocket scheme. A malformed URL or empty token is answered `400 invalidRequest`. While the gateway
is off, the relay stays configured but Hub does not connect, and clients present it as paused. The
token never appears in the state object or events.

Devices paired before the relay was set learn it from `GET /m/hello` on their next direct connection.
Setting the relay does not change existing pairings.

### 10.3 Reaching a computer

A device tries the computer's direct addresses first, each with a three-second connect timeout, then
the relay, and keeps whichever path answered for that connection. Over the relay, each TLS connection
the device would open runs through its own tunnel: the relay base URL with its scheme mapped to `ws`
or `wss`, followed by `/r/connect?host=<hostId>`, carrying the same pinned certificate, device
credential, routes, and AppServer protocol. The computer is offline when neither path answers,
including when the relay answers `hostOffline`. Release builds allow only `https` and `wss` relays.

## 11. Failure behavior

| Situation | Behavior |
|---|---|
| Gateway port in use | Turning on fails with `portUnavailable` and the state becomes `failed`. Hub retries the port on its next start while the gateway stays on. |
| Certificate mismatch | The device refuses that computer and reports that its identity changed, with removal as the only action. Other computers are unaffected. |
| Pairing code expired or used | `pairingCodeInvalid`; the device asks for a new code. |
| Device revoked | The device forgets that computer and tells the user. |
| Relay requested for a stopped project | `projectNotRunning` before the upgrade; the device treats the project as stopped. |
| Project AppServer stops | The relayed connection closes, and `/m/events` reports `projectStarted` when it returns. |
| Computer asleep or unreachable | The computer is offline. |
| Relay unreachable or wrong token | Hub reports `relay.state` `failed` and keeps retrying; devices on the local network are unaffected. |
| Gateway turned off | Connections close with `gatewayOff`, and the device reports that access is off rather than that the computer is offline. |
