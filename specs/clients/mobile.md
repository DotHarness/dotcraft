# DotCraft Mobile

| Field | Value |
|---|---|
| Version | 0.8.1 |
| Status | Draft |
| Date | 2026-10-03 |
| Parent spec | [Hub Architecture](../architecture/hub-architecture.md) |
| Related Specs | [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](desktop-client.md), [TypeScript SDK](../sdk/typescript.md) |

## 1. Overview

DotCraft Mobile is a phone app for keeping up with the agent while away from the desk. The agent
keeps running on the user's computer. The phone shows what is running, answers approvals and
questions, sends follow-up messages, stops work, and starts new chats in the computer's projects.

The phone is an ordinary AppServer client. It reaches the computer through the **mobile gateway**,
an opt-in Hub listener that pairs phones and relays their AppServer connections to the workspace
AppServers Hub already manages. The AppServer Protocol carries the conversation unchanged.

```
Phone app ──TLS, pinned certificate──> Hub mobile gateway ──loopback──> workspace AppServers
                                       (pairing, devices,              (one per workspace,
                                        relay, workspace list)          protocol unchanged)
```

## 2. Goals

- Pair a phone with one scan, and let the user see and revoke every paired phone on the computer.
- Show what needs the user first: pending approvals and questions across every running project.
- Read a chat as it streams, answer its approvals and questions, add a message, or stop it.
- Start a new chat in any project on the computer, including one whose runtime is not running.
- Compose like on Desktop: attach photos and files, switch to plan mode, and insert commands and
  skills.
- See what a chat changed, open a file it refers to, and check how much context and account usage
  remain.
- Keep a dropped connection invisible apart from a status line: reconnect, catch up, and replay
  anything still waiting for an answer.

## 3. Non-goals

- Running the agent on the phone, or in a cloud service.
- Editing settings, providers, plugins, skills, automations, or Agent Profiles from the phone.
- Editing files, browsing the computer's folders, terminals, the in-app browser, or Desktop plugin
  surfaces.
- Controlling a computer that belongs to someone else. A pairing grants the phone the authority of
  the computer's signed-in user, and the product says so.
- iOS in this version. The app ships for Android first; the wire contract stays platform-neutral so
  iOS can follow without changes to Hub.
- A push service. While work is running or waiting, the phone keeps a live session in the background
  and posts its own notifications (§8.5).
- A hosted public relay. Users who want access from other networks run their own relay (§12).

## 4. Architecture

| Part | Owner | Role |
|---|---|---|
| Mobile app | `mobile/` in this repository; Expo and React Native, TypeScript; named DotCraft, with the application id `com.dotharness.dotcraft`; Android first, iOS later | Pairs, keeps one gateway connection per running project, renders chats. Uses the `@dotcraft/sdk` contracts and wire client over the pinned transport described below. |
| Mobile gateway | Hub (§5) | Opt-in TLS listener on the computer: pairing, device credentials, project list, AppServer relay. |
| Workspace AppServers | Hub-managed, unchanged | Own threads, turns, approvals, and history exactly as for Desktop. |
| Phones segment | Desktop Connections settings (§9) | Turns the gateway on and off, shows the pairing code, lists and revokes phones. |

Phones reject self-signed certificates by default, so the app connects through a small native module
that trusts only the certificate whose fingerprint was pinned at pairing, for both HTTPS requests and
WebSockets. It never falls back to system trust or to unencrypted connections.

The phone never receives a Hub token or an AppServer process token. Hub authenticates each relayed
connection to its AppServer itself, so the phone's device credential is the only secret it holds.

## 5. Mobile gateway

### 5.1 Listener

The gateway is a Hub-hosted HTTPS application bound to `Hub.MobileHost` (default `0.0.0.0`) on the
fixed port `Hub.MobilePort` (default `47610`). It is off by default. Turning it on starts the
listener and records the choice in `~/.craft/hub/mobile.json`, so the gateway comes back when Hub
restarts. Turning it off stops the listener, closes every relayed connection and event socket, and
keeps paired devices so turning it on again needs no new pairing. The port is fixed because paired
phones store it; when the port is unavailable, turning the gateway on fails with `portUnavailable`,
the gateway state becomes `failed`, and Hub does not fall back to another port.

The listener serves TLS only. The first time the gateway is turned on, Hub creates a self-signed
ECDSA P-256 certificate whose subject alternative name is the computer name, valid for 20 years, and
stores it as `~/.craft/hub/mobile-certificate.pfx`, readable only by the user where the operating
system allows. Later starts reuse it, so the fingerprint survives restarts. The fingerprint is the
SHA-256 of the DER-encoded certificate, in lowercase hexadecimal. Phones pin the fingerprint
delivered in the pairing code (§6) and refuse any other certificate.

The gateway accepts connections only from loopback, private IPv4 ranges (10/8, 172.16/12,
192.168/16), link-local addresses (169.254/16, fe80::/10), the shared address range 100.64/10 used
by private networks such as Tailscale, and IPv6 unique local addresses (fc00::/7). An IPv4-mapped
IPv6 source is judged by its IPv4 address. Any other source is closed as soon as it is accepted,
before the TLS handshake.

Hub advertises the addresses a phone may try: the IPv4 addresses of the computer's network
interfaces that are up, excluding loopback and auto-configured 169.254/16 addresses, with the address
of the default route first. Private-network addresses such as Tailscale's are included. The list is
part of the pairing code and of `GET /m/hello`, so a phone refreshes it on every successful
connection.

### 5.2 Routes

Every route except `POST /m/pair` requires `Authorization: Bearer <device credential>`; a missing,
unknown, or revoked credential is answered `401 unauthorized`. The gateway serves no `/v1/*` route.
Bodies are JSON with camelCase fields, and times are ISO-8601 strings.

| Route | Purpose |
|---|---|
| `POST /m/pair` | Consume a pairing code and return a device credential (§6). |
| `GET /m/hello` | Computer name, DotCraft version, port, fingerprint, and advertised addresses. Also refreshes the device's last-seen time. |
| `GET /m/projects` | The computer's projects, without starting any. |
| `POST /m/projects/{projectId}/ensure` | Start or reuse the project's AppServer through the same path as `POST /v1/appservers/ensure`, as for a remote client: the phone sends no runtime tool hints, and Hub uses the ones it has stored. |
| `GET /m/projects/{projectId}/appserver` | WebSocket. Relayed AppServer connection for a running project. |
| `GET /m/events` | WebSocket. Gateway events for this device (§5.3). |
| `DELETE /m/device` | The phone removes its own pairing. Answers `204`. |

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
    "name": "ANN-PC",
    "port": 47610,
    "fingerprint": "3f9c1b0e…",
    "addresses": ["192.168.1.20", "100.101.102.103"]
  }
}
```

`platform` is `ios` or `android`; `displayName` is required, and `osVersion` and `appVersion` are
free-form strings. A malformed request is answered `400 invalidRequest` before the code is checked,
so it does not use the code up.

`GET /m/hello` answers `{ "name", "version", "port", "fingerprint", "addresses" }`, with the meanings
of `computer` above and `version` the DotCraft version.

`GET /m/projects` answers:

```json
{
  "projects": [
    {
      "projectId": "9c1d0e5a2b7f4c3d8e6a1b0f",
      "displayName": "dotcraft",
      "running": true,
      "lastActiveAt": "2026-10-03T08:12:00+00:00"
    }
  ]
}
```

`POST /m/projects/{projectId}/ensure` answers one project object, with `running` true once the
AppServer is up.

The projects are Hub's project list (Hub Architecture §7 Projects), the same list Desktop shows, plus
the default Chat workspace; a project removed on the computer disappears from the phone. A
`projectId` that is not in that list is answered `404 projectNotFound` by every `/m/projects` route. `projectId` is the first 24 hexadecimal digits of the SHA-256 of the normalized workspace
path, case-folded on Windows, so it is stable and never reveals the path. `displayName` is the
folder name; the default Chat workspace is always listed, as `Chats`. `lastActiveAt` is the time the
project was last opened, or `null` for Chats, and projects are ordered by it, most recent first.

The relay is opaque: Hub opens the AppServer's loopback WebSocket with that process's token and
copies frames in both directions, preserving message type and fragment boundaries. It never parses,
rewrites, logs, or persists relayed payloads. Closing either side closes the other. A relay for a
project whose AppServer is not running is answered `409 projectNotRunning` before the upgrade.

Errors use the Hub error shape (§6 of [Hub Architecture](../architecture/hub-architecture.md)):

| Code | Status | When |
|---|---|---|
| `unauthorized` | 401 | Missing, unknown, or revoked device credential. |
| `invalidRequest` | 400 | Malformed pairing request, or a WebSocket route reached without an upgrade. |
| `pairingCodeInvalid` | 400 | Unknown, used, superseded, or expired pairing code. |
| `projectNotFound` | 404 | No known project has that id. |
| `projectNotRunning` | 409 | A relay was requested for a project whose AppServer is not running. |
| `appServerStartFailed` | 500 | Ensure could not start the project. The error carries no workspace path or diagnostics. |
| `hubInternalError` | 500 | Unexpected failure. |

Hub closes a gateway WebSocket with the close reason `deviceRevoked` (status 1008) when the phone is
revoked, and `gatewayOff` (status 1001) when the gateway turns off. When Hub itself stops, it closes
them without a reason, and the phone treats the computer as offline.

### 5.3 Gateway events

`GET /m/events` carries one JSON text message per event:

| Message | When |
|---|---|
| `{ "type": "projectStarted", "projectId": "…" }` | A project's AppServer started or became reachable. |
| `{ "type": "projectStopped", "projectId": "…" }` | A project's AppServer stopped or exited. |
| `{ "type": "deviceRevoked" }` | This phone was revoked; the socket closes next. |
| `{ "type": "gatewayOff" }` | The gateway is turning off; the socket closes next. |

Hub ignores messages the phone sends on this socket. Revoking a phone or turning the gateway off
closes that phone's, or every phone's, relays and event sockets within one second.

## 6. Pairing and devices

1. On the computer, the user turns on the gateway and opens **Add phone**. Hub mints a pairing code:
   128 random bits encoded as base64url, single-use, valid for 10 minutes, of which Hub stores only
   the hash. Minting replaces the previous code, so only the most recent unexpired code is valid.
   Each code has a `pairingId` that names it in Hub events without revealing it.
2. Desktop shows the code as a QR code encoding
   `dotcraft://pair?v=1&name=<computer>&port=<port>&fp=<sha256>&addr=<a1>,<a2>&code=<code>`, with
   each value URL-encoded and the addresses joined by commas. The QR code is the only pairing path;
   it carries the certificate fingerprint, so the phone never trusts a certificate on first use.
3. The phone scans it, connects to the first reachable address with the pinned certificate, and
   shows "Allow this phone to control <computer>?" with **Allow**.
4. **Allow** calls `POST /m/pair` with the code, the phone's display name, platform, OS version, and
   app version. Hub returns a `deviceId` and a device credential of 256 random bits encoded as
   base64url, and emits `mobile.devicePaired` with the code's `pairingId` so Desktop moves to the
   paired state without polling.
5. The phone stores the credential in the Android Keystore and the computer record
   (name, addresses, port, fingerprint, deviceId) in app storage.

The credential travels only in the `Authorization` header. It never enters URLs, logs, traces, model
context, or session data, and Hub never logs a pairing code. Hub stores the credential's hash with
the device record in `~/.craft/hub/mobile.json`: `deviceId`, display name, platform, OS version, app
version, paired time, and last-seen time.

Revoking a phone on the computer deletes its record and closes its connections; the phone receives
`deviceRevoked` on `/m/events` when connected, or `unauthorized` on its next request, and then
forgets the computer. Removing the computer on the phone calls `DELETE /m/device` when reachable and
forgets the computer either way.

## 7. Hub Local API additions

Desktop manages the gateway through loopback Hub Local API routes, protected by the Hub token:

| Endpoint | Purpose |
|---|---|
| `GET /v1/mobile` | The gateway state object. |
| `POST /v1/mobile/enable` | Turn the gateway on and answer the state object. A port conflict answers `409 portUnavailable` and leaves the state `failed` with that `failureCode`. |
| `POST /v1/mobile/disable` | Turn the gateway off and answer the state object. Devices are kept. |
| `POST /v1/mobile/pairings` | Mint a pairing code. Answers `409 gatewayOff` unless the gateway is on. |
| `DELETE /v1/mobile/devices/{deviceId}` | Revoke a phone. Answers `204`, or `404 deviceNotFound`. |

The `POST` routes ignore their request body; Desktop sends `{}`. `GET /v1/status` reports
`capabilities.mobile: true` when Hub serves these routes.

The state object:

```json
{
  "state": "on",
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
  ]
}
```

`state` is `off`, `on`, or `failed`. A `failed` state adds `failureCode`, such as `portUnavailable`,
and an English `failureMessage`; both fields are absent otherwise. `port` is `Hub.MobilePort`.
`lastSeenAt` is `null` until the phone first connects, and `connected` is true while the phone has a
relay or event socket open.

`POST /v1/mobile/pairings` answers `{ "pairingId", "qrPayload", "expiresAt" }`, where `qrPayload` is
the `dotcraft://pair` URL of §6.

Hub SSE adds these events, in the existing event envelope:

| Event | `data` |
|---|---|
| `mobile.stateChanged` | The state object, whenever the gateway turns on, turns off, or fails, and whenever the relay or its `state` changes (§12.2). |
| `mobile.devicePaired` | `{ deviceId, displayName, platform, pairingId }`. |
| `mobile.deviceRevoked` | `{ deviceId }`, whether the computer or the phone removed the pairing. |
| `mobile.deviceSeen` | `{ deviceId, lastSeenAt, connected }` when the phone's first socket opens, when its last socket closes, and on `GET /m/hello` at most once a minute per phone. |

## 8. Mobile app

### 8.1 Connections

The app pairs with one computer at a time; pairing another computer replaces the current pairing
once the new computer is allowed, so abandoning the new pairing keeps the old one. While in the
foreground, and during a live session in the background (§8.5), it keeps the computer's `/m/events`
socket and one relayed AppServer connection per running project; otherwise it closes them in the
background. Each relayed connection initializes as an approval-capable client
that supports user-input requests and streaming.

- **Status.** The computer is **online** when a gateway route answers, **connecting** while trying
  addresses, and **offline** otherwise. Offline shows the last known projects and chats read-only,
  with the time they were last updated.
- **Reconnect.** Exponential backoff with jitter from 1 to 30 seconds, restarted immediately when the
  app returns to the foreground or the network changes.
- **Catching up.** After any reconnect the app follows the AppServer recovery rules: read the thread
  header, reload history head pages, and subscribe again. Subscribing replays pending approvals and
  questions, so nothing waiting is lost while the phone was away.

### 8.2 Screens

| Screen | Content |
|---|---|
| Pair | Camera scan, the Allow confirmation, and a connected confirmation. |
| Home | No screen title. The top row centers the computer, with the mascot as its avatar, its name, its status, and a chevron; tapping it opens a menu with **Pair a different computer** and **Settings**, so the row has no other button. It stays in place while the lists scroll. **Needs you** lists every chat waiting on an approval or a question across running projects. **Projects** lists projects, marking those whose runtime is not running; **Recent** lists the other chats of running projects with a trailing state. Search over chat titles, and New chat, which asks for the project with the most recently used one first. |
| Project | The project's chats, newest first, and New chat. Opening a project whose runtime is not running starts it; Home never starts a project by itself. |
| Chat | A floating top bar over the transcript: Back, the chat title with its project and computer, the context ring, and a menu. The ring fills with the share of the context window in use; tapping it opens Status. The transcript collapses each tool activity to one line and hides reasoning behind a disclosure; finished replies offer Copy; a created plan shows as a plan card. While an approval, a question, or a plan confirmation waits, the decision card closes the transcript and the composer is hidden. The composer card names the computer it works on and carries Add (**+**), the approval policy, and the model controls; while a turn runs it adds a message to the turn and a Stop control interrupts it. Above the composer, a changes pill appears once a turn has changed files. |
| Add menu | **Photo**, **File**, and **Plan mode**, which shows a check while it is on. |
| Picker | Commands and skills matching what follows `/` or `$` in the composer. |
| Changes | Every file the turn changed with its additions and deletions; each file expands to its diff. |
| File | One file from the computer, read-only. |
| Status | Opened from the context ring: context left, account usage when the chat's provider reports it, the project folder, and the chat ID to copy. |
| Decision card | One card for every decision a chat waits on: an approval, a question, or a plan confirmation. It is the last entry of the transcript, held at the bottom of the screen when the chat is short, and scrolls with it, so earlier messages stay one swipe away. Like the composer, it shows the focus border only while its text field is in use. The composer stays hidden until the decision is made. |
| Settings | The paired computer with Remove, Pair a different computer, and app information. |

Every screen below Home leads with the same framed Back button as the chat's top bar, including
over the pairing camera.

Chat states use one vocabulary: **running**, **needs approval**, **needs answer**, **done**,
**failed**. They come from `thread/list` runtime snapshots and `thread/runtimeChanged`. A stopped
turn is done and shows a Stopped line. Thread summaries do not carry the last turn's result, so a
list shows **failed** only for a chat whose failure the phone has seen.

### 8.3 Working with a chat

- The phone lists and starts chats under the Desktop channel identity, so the computer and the phone
  show the same chats, and a chat started on either can be continued on the other.
- New chat ensures the project's AppServer when it is not running, starts a thread with the
  project's defaults, and calls `turn/start`.
- A message sent while a turn runs uses `turn/steer` when the server accepts steering and
  `turn/enqueue` otherwise; Stop calls `turn/interrupt`.
- The decision card shows one request at a time and counts the others waiting in the chat. Its
  options are Desktop's numbered choice rows: a row is selected by tapping it, a selected row answers
  when tapped again, and **Submit** answers with the selected row. A row's description sits behind
  its info icon.
- An approval in the card asks its question over Desktop's detail panel (type, operation, target,
  reason), then **Allow once**, **Allow for session**, and **Reject** as option rows. **Submit** is
  named after the selected row and answers `item/approval/request` with `accept`,
  `acceptForSession`, or `decline`; while another row is selected, **Reject** sits beside it. The phone never offers `acceptAlways`, because a permanent grant belongs on the
  computer. An approval has no dismiss; **Reject** declines it.
- A question in the card follows Desktop's question format: the question, its options as rows, and
  an **Other** row for typed text. A request with several questions pages through them with
  **Previous** and **Next** beside the question, and **Submit** answers
  `item/tool/requestUserInput` with every answer. **Dismiss** answers a non-blocking request with no
  answers and interrupts the turn for a blocking one, as Desktop does.
- The chat menu offers the basic chat actions Desktop's chat menu has: Rename (`thread/rename`),
  Fork (`thread/fork` into the same project, opening the copy), and Archive (`thread/archive`,
  returning to the list), plus Open project and, while a turn runs, Stop. Worktree forks, pinning,
  and actions that open things on the computer stay on Desktop.
- The model sheet lists its settings the way Desktop's model picker does: provider, model, and
  reasoning effort are each one row showing the current value, and tapping a row expands its choices
  in place and collapses the others; speed stays a Fast switch.
- The composer shows the chat's model, reasoning effort, speed, and approval policy (`prompt` or
  `autoApprove`). Changing one sends the whole configuration with `thread/config/update`, which takes
  effect from the next turn. New chat starts on the model `model/list` marks `isDefault` for the
  provider, and switching provider selects that provider's `isDefault` model; the `thread/start`
  configuration names that provider and model, and carries reasoning, speed, and approval policy
  only when the user changed them, so AppServer fills the rest from the provider's preference.
  Models and their reasoning and speed options come from `model/list`, never from rules in the app,
  and each control is hidden when the server lacks its capability. The phone never changes a chat's
  Agent Profile.
- **+** opens the Add menu. **Photo** picks images from the photo library; the phone scales each to
  at most 2048 px on its longer side, re-encodes it as JPEG, and sends it as an `image` input part.
  A message's photos together stay within about 3 MB once encoded, so the turn request fits one
  AppServer message; photos picked past that budget, in one selection or a later one, are not
  added and the composer says so.
  **File** picks any document up to 2 MiB; before the message is sent, the phone creates
  `<project>/.craft/attachments/<id>/` with `fs/createDirectory`, writes the file there with
  `fs/writeFile`, and sends a `fileRef` to it. Attachments wait in the composer as removable
  thumbnails and chips until sent; a failed upload keeps the draft and says which file failed. A
  file over the limit is refused when picked. **File** is hidden when the server lacks
  `capabilities.fileSystem`.
- **Plan mode** in the Add menu turns plan mode on, or off when it is on, with `thread/mode/set` (`plan` or `agent`); New chat in plan mode
  starts the thread with `mode: plan`. While on, the composer shows a Plan chip that turns it off.
  A successful `CreatePlan` shows in the transcript as Desktop's plan card: the Plan badge, the
  title, and the start of the plan body under a fade with **Expand plan**; the card's chevron or
  **Expand plan** opens the whole body and its to-dos in place, and Copy copies the plan. When the
  server reports a pending plan confirmation, the decision card
  asks **Implement this plan?** with **Yes, implement this plan** and a row to say how to adjust it,
  then **Submit**: yes switches the chat to `agent` and sends "Implement the plan.", the same as
  Desktop, and a typed adjustment is sent as feedback and stays in plan mode. **Dismiss** closes the
  card and returns the composer in plan mode. When the switch to `agent` fails, nothing is sent and
  the failure is reported. A chat that runs an Agent Profile has no
  **Plan mode** item or Plan chip, because its agent keeps a fixed capability scope, as on Desktop.
- Typing `/` at the start of a word opens the Picker with custom commands from `command/list` and
  enabled skills from `skills/list`; `$` opens it with skills only. Choosing one puts the reference inline in the
  text at the cursor, as `/name` or `$name` styled as a reference, the way Desktop's composer keeps
  references in the text; a reference is deleted as a whole and is sent as a `commandRef` or
  `skillRef` in its place. Built-in commands are not offered.
- The changes pill summarizes the latest turn that changed files: the number of files and the added
  and deleted lines. A live turn takes them from `turn/diff/updated`; a reopened chat rebuilds them
  from the turn's `fileChange` results in history, as Desktop does. Tapping the pill opens Changes.
- Tapping a file chip, or a file's name in Changes, opens File with `fs/readFile`. Text shows with its
  syntax and images show as images; another type, or a file over the read limit, shows its name and
  path to copy. Without `capabilities.fileSystem`, a file chip only shows its path.
- The context ring starts from the `contextUsage` in `thread/read` and follows the `contextUsage`
  carried by `item/usage/delta` and by terminal compaction events, as Desktop's ring does; a chat
  without a snapshot shows an empty ring. Status shows the share of the context window left with
  tokens used and the window size. When the chat's provider signs in with ChatGPT,
  it also shows each usage window from `auth/openai/usage` with the share left and its reset time.
- Replies render Markdown with the same GitHub-flavored rules as Desktop. A link to a local file shows
  as a file chip with its name that opens File. Web links open the browser. File and skill
  references in user messages show as chips, and photos sent with a message show as thumbnails in
  it, from the moment it is sent.
  Math uses Desktop's delimiters; the phone does not typeset it, so a formula shows its TeX source
  styled as inline code, or as a code block for display math.
- A tool activity line shows the tool kind's icon and fits one line, ending in an ellipsis; tapping it
  shows the full command or target and its output.

### 8.4 Mascot

The app renders the DotCraft mascot with the shared `@dotcraft/avatar` package, so the rig, palette,
and motion match Desktop. It marks moments, not every screen:

| Moment | Mascot |
|---|---|
| Home | As the computer's avatar in the top row, aligned by its drawn shape rather than its box, reflecting the computer: idle when online, looking around while connecting, asleep when offline or when phone access is off, and holding up its question sign while anything needs you. |
| Pair | Greets on the scan screen, waits while the phone reaches the computer, and celebrates once when pairing completes. |
| Transitions | Opening a chat while history loads, a project starting, and reconnecting show the mascot working with one line saying what is happening, in place of skeleton rows. |
| Chat | A new chat's empty transcript greets with the mascot; a chat that runs an Agent Profile shows that profile's name-derived avatar there instead. The composer carries no mascot, so its controls keep the room. |
| Empty and error states | Empty lists, the revoked notice, and the identity-changed notice pair one sentence with a matching expression. |

The mascot is the original brand appearance; the phone does not read the Desktop pet's outfit.
Motion follows the phone's reduced-motion setting, and the mascot is decorative to screen readers.

### 8.5 Live session

When the app moves to the background while any chat is running or waiting on the user, it keeps its
connections in an Android foreground service of type `connectedDevice` and shows one ongoing
notification for the computer,
such as "2 running · 1 needs you". On Android 16 and later the notification asks to be promoted to a
Live Update, so it stays in the status bar the way a live activity does.

- During a live session the phone subscribes to every running or waiting chat, because pending
  requests and turn results reach only subscribed connections ([AppServer Protocol](../protocols/appserver-protocol.md) §7.6).
- A new approval posts a heads-up notification naming the chat, with **Allow once** and **Reject**
  actions that answer without opening the app; its body opens the chat.
- A new question posts a notification that opens the chat.
- A turn that ends while the app is in the background posts one notification: done or failed.
- The session ends, its connections close, and its ongoing, approval, and question notifications go
  away when nothing has been running or waiting for two minutes, when the user chooses **End** on the
  ongoing notification, or when the computer becomes unreachable for two minutes. Done and failed
  notifications stay. Returning to the app continues normally.
- Android cannot ask for permission from the background, so the app asks for notification permission
  once, the first time a chat is running or waiting while the app is open. Without it, the app closes
  its connections in the background as it does outside a live session.

### 8.6 Localization

The app ships the same locales as Desktop and follows the phone's language.

## 9. Desktop Phones segment

The Connections settings page gains a **Phones** segment for the phones that may control this
computer. It follows the segment rules of [Desktop Client](desktop-client.md) §6.7.

- A switch, "Allow phones to control this computer", maps to enable and disable. Its description
  states that a paired phone can do anything the user can do in DotCraft on this computer.
- **Add phone** opens a dialog that mints a pairing code and shows the QR code, its expiry, and
  a refresh action. Hub keeps one unexpired code at a time, so refreshing or minting again
  invalidates the previous code. The dialog moves to a paired state on `mobile.devicePaired` for the
  code it minted, and a failed mint shows the error with a retry.
- The list shows each phone's name, platform, and last-seen time, with Remove behind a confirmation.
  It stays visible while phone access is off or failed, so a phone can be removed without turning
  access on.
- When the gateway fails to start, the segment shows the reason, such as the port being in use.
- The Hub token and pairing codes never enter the renderer's persisted state.

## 10. Attention notifications

AppServer-managed notifications gain two kinds beside `turnCompleted` and `turnFailed`:
`approvalRequested` when a turn starts waiting on an approval, and `inputRequested` when it starts
waiting on a question. They follow the same suppression and origin rules, and Desktop settings can
turn each kind off. They exist so the user learns that work is blocked, whether the answer then
comes from the computer or the phone.

## 11. Multi-client rules

A phone and a Desktop window may watch the same thread. Pending approvals and questions belong to
the thread: both receive them, the first answer resolves each one and both dismiss it, and a
connection that goes away never resolves one. A request waits without a timeout until it is
answered or its turn ends, so a phone that reconnects or opens the chat later can still answer it.
The AppServer owns these guarantees; see [AppServer Protocol](../protocols/appserver-protocol.md)
§7.6.

## 12. Off-network access

A relay lets a phone reach the gateway from any network. The user runs it on a server both sides can
reach, with `dotcraft relay serve` or the relay container image. The relay forwards bytes and nothing
else: the phone still speaks TLS to the gateway with the pinned certificate and its device
credential, so the relay sees only ciphertext and never a credential, project, or chat.

### 12.1 Relay

`dotcraft relay serve --listen <url> --token <token>` serves three WebSocket routes, normally behind a
reverse proxy that terminates public TLS. The token may come from `DOTCRAFT_RELAY_TOKEN` instead of
`--token`, and the relay compares it in constant time.

| Route | Caller | Purpose |
|---|---|---|
| `GET /r/host?host=<hostId>` | Hub | Control channel for one computer, authenticated by `Authorization: Bearer <relay token>`. One live control channel per host id; a newer one replaces the older, which is closed with the reason `replaced`. |
| `GET /r/connect?host=<hostId>` | Phone | Opens a tunnel to that computer. Answered `404 hostOffline` when the computer has no control channel. |
| `GET /r/accept?tunnel=<tunnelId>` | Hub | Hub's side of a tunnel, authenticated by the relay token. |

For each phone connection the relay picks a random `tunnelId`, sends `{ "type": "open", "tunnel": "…" }`
on the host's control channel, and splices the phone's socket with Hub's `/r/accept` socket for that
tunnel once it arrives within ten seconds, or closes the phone's socket. Tunnel payloads are binary
WebSocket messages carrying the raw bytes of one TCP stream. The relay keeps no state beyond live
sockets, logs no payloads, and closes a tunnel when either side closes. Its errors use the Hub error
shape; a missing or wrong token is answered `401 unauthorized`.

The host id is 128 random bits created with the gateway and kept in `~/.craft/hub/mobile.json`. Only
paired phones learn it, and reaching a host id still requires the pinned certificate and a device
credential.

### 12.2 Hub

When the gateway is on and a relay is configured, Hub keeps the control channel open, reconnecting
with jittered backoff from 1 to 30 seconds. For each `open` it connects `/r/accept` and a loopback TCP
connection to the gateway port and copies bytes both ways, so relayed phones arrive at the gateway
from loopback and follow §5 unchanged.

The relay URL and token are set from the Phones segment and stored in `~/.craft/hub/mobile.json`.
The Hub Local API adds `PUT /v1/mobile/relay` with `{ "url", "token" }` and `DELETE /v1/mobile/relay`,
both answering the state object, which gains `relay: { "url", "state" }` with `state` `connecting`,
`connected`, or `failed`, or `relay: null` when none is configured. `url` is the relay's base URL with
an `https` or `wss` scheme (`http` and `ws` are accepted for testing); Hub and phones append the route
paths to it and connect with the matching WebSocket scheme. A malformed URL or an empty token is
answered `400 invalidRequest`. While the gateway is off, the relay stays configured but Hub does not
connect, and clients present it as paused rather than by its `state`. The token never appears in the
state object or in events.

The pairing payload adds `relay=<url>&host=<hostId>` when a relay is configured, and `GET /m/hello`
adds `relay` (`{ "url", "hostId" }` or `null`), so phones paired before the relay was set learn it on
their next direct connection.

### 12.3 Phone

The phone tries the computer's direct addresses first, each with a three-second connect timeout, and
then the relay, and keeps using whichever path answered for that connection. Over the relay, each TLS
connection the phone would open to the gateway runs through its own tunnel: the relay base URL with
its scheme mapped to `ws` or `wss`, followed by `/r/connect?host=<hostId>`. It carries the same pinned
certificate, device credential, routes, and AppServer protocol as on the local network. The computer
is offline when neither path answers, including when the relay answers `hostOffline`. Release builds
allow only `https` and `wss` relays.

### 12.4 Desktop

The Phones segment adds **Access from anywhere** with the relay address and token, a status line for
`relay.state`, and Remove. Setting it does not change existing pairings.

## 13. Failure behavior

| Situation | Behavior |
|---|---|
| Gateway port in use | Turning on fails with `portUnavailable` and the state becomes `failed`; the Phones segment shows the reason. Hub retries the port on its next start while the gateway stays on. |
| Certificate mismatch | The phone refuses to connect and shows that the computer's identity changed, with Remove as the only action. |
| Pairing code expired or used | `pairingCodeInvalid`; the phone asks for a new code. |
| Device revoked | The phone forgets the computer and returns to Pair with a notice that the computer removed this phone. |
| Relay requested for a stopped project | `projectNotRunning` before the upgrade; the phone treats the project as stopped. |
| Project AppServer stops | The relay closes; open chats of that project become read-only with a notice that the project stopped on the computer and a Start action, and the phone reconnects when `/m/events` reports it started. |
| Computer asleep or unreachable | The computer shows offline with the last update time. |
| Relay unreachable or wrong token | Hub reports `relay.state` `failed` and keeps retrying; phones on the local network are unaffected. |
| Gateway turned off | Connections close with `gatewayOff`; the phone keeps the last synced chats read-only and shows that phone access is off on the computer. It remembers that reason until a connection succeeds again, so a restarted app still says access is off rather than offline. |
| Project cannot start | Opening a project that is not running while the computer is offline or access is off, or when its start fails, says the computer can't start that project right now. |

## 14. Acceptance checklist

- A phone pairs by one scan and appears in the Phones segment without a refresh; revoking it
  disconnects it within one second.
- The phone never trusts a certificate other than the pinned one.
- Home shows approvals and questions waiting in any running project.
- An approval answered on the phone resolves it on the computer, and the reverse.
- A message sent from the phone during a turn reaches that turn; Stop interrupts it.
- New chat works in a project whose AppServer was not running.
- After the phone loses its connection mid-turn, it catches up without duplicates and shows any
  approval still waiting.
- No credential or token appears in URLs, logs, or traces.
- Through a relay, the phone works from another network exactly as on the local network, and the
  relay never sees a credential or plaintext.
- With work running, the phone keeps a live session in the background, and an approval can be
  answered from its notification.
- A photo and a file sent from the phone reach the agent; the file lands under the project's
  `.craft/attachments/`, and a large photo still fits one message.
- Plan mode on the phone ends in **Implement this plan?** in the decision card, and yes continues
  the chat in agent mode.
- Approvals, questions, and plan confirmations all use the decision card, never a second copy of
  the same request; the transcript above it scrolls while the composer stays hidden.
- Commands and skills chosen in the Picker arrive as `commandRef` and `skillRef`.
- Changes lists the same files and line counts as Desktop for the same turn, and a file chip opens
  the file's contents.
- Status shows the same context share as Desktop and, for a ChatGPT sign-in, the usage windows.
