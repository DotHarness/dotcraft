# DotCraft Mobile

| Field | Value |
|---|---|
| Version | 0.9.3 |
| Status | Draft |
| Date | 2026-10-22 |
| Parent spec | [Hub Architecture](../architecture/hub-architecture.md) |
| Related Specs | [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](desktop-client.md), [TypeScript SDK](../sdk/typescript.md) |

## 1. Overview

DotCraft Mobile is a phone app for keeping up with the agent while away from the desk. The agent
keeps running on the user's computer. The phone shows what is running, answers approvals and
questions, sends follow-up messages, stops work, and starts new chats in the computer's projects.

The phone is an ordinary AppServer client. It reaches the computer through the **mobile gateway**,
an opt-in Hub listener that pairs phones and relays their AppServer connections to the workspace
AppServers Hub manages. The AppServer Protocol carries the conversation unchanged.

```
Phone app ──TLS, pinned certificate──> Hub mobile gateway ──loopback──> workspace AppServers
                                       (pairing, devices,              (one per workspace,
                                        relay, workspace list)          protocol unchanged)
```

## 2. Goals

- Pair a phone with one scan, and let the user see and revoke every paired phone on the computer.
- Pair one phone with several computers and switch between them, while work on each stays
  connected and can reach the user.
- Read a chat as it streams, answer its approvals and questions, add a message, or stop it.
- Start a new chat in any project on the computer, including one whose runtime is not running.
- Compose like on Desktop: attach photos and files, switch to plan mode, and insert commands and
  skills.
- See what a chat changed, open a file it refers to, and check remaining context and account usage.
- Keep a dropped connection invisible apart from a status line: reconnect, catch up, and replay
  anything still waiting for an answer.

## 3. Non-goals

- Running the agent on the phone, or in a cloud service.
- Editing settings, providers, plugins, skills, automations, or Agent Profiles from the phone.
- Editing files, browsing the computer's folders, typing into terminals, the in-app browser, or
  Desktop plugin surfaces.
- Controlling a computer that belongs to someone else. A pairing grants the phone the authority of
  the computer's signed-in user, and the product says so.
- iOS. The app ships for Android; the wire contract stays platform-neutral.
- A push service. While work is running or waiting, the phone keeps a live session in the background
  and posts its own notifications (§8.5).
- A hosted public relay. Users who want access from other networks run their own relay (§12).

## 4. Architecture

| Part | Owner | Role |
|---|---|---|
| Mobile app | `mobile/` in this repository; application id `com.dotharness.dotcraft` | Pairs with one or more computers, keeps one gateway connection per running project of each, renders chats. Uses the `@dotcraft/sdk` contracts and wire client over the pinned transport. |
| Mobile gateway | Hub (§5) | Opt-in TLS listener on the computer: pairing, device credentials, project list, AppServer relay. |
| Workspace AppServers | Hub-managed, unchanged | Own threads, turns, approvals, and history exactly as for Desktop. |
| Phones segment | Desktop Connections settings (§9) | Turns the gateway on and off, shows the pairing code, lists and revokes phones. |

The app connects only through a transport that trusts the certificate whose fingerprint was pinned
at pairing, for both HTTPS and WebSockets. It never falls back to system trust or unencrypted
connections.

The phone never receives a Hub token or an AppServer process token. Hub authenticates each relayed
connection to its AppServer itself, so the device credential is the only secret the phone holds.

## 5. Mobile gateway

### 5.1 Listener

The gateway is a Hub-hosted HTTPS application bound to `Hub.MobileHost` (default `0.0.0.0`) on the
fixed port `Hub.MobilePort` (default `47610`). It is off by default. Turning it on starts the
listener and records the choice in `~/.craft/hub/mobile.json`, so it returns when Hub restarts.
Turning it off stops the listener, closes every relayed connection and event socket, and keeps
paired devices. The port is fixed because paired phones store it; when it is unavailable, turning
the gateway on fails with `portUnavailable`, the state becomes `failed`, and Hub does not fall back
to another port.

The listener serves TLS only. When the gateway is first turned on, Hub creates a self-signed ECDSA
P-256 certificate whose subject alternative name is the computer name, valid for 20 years, stored as
`~/.craft/hub/mobile-certificate.pfx` and reused afterwards so the fingerprint survives restarts. The
fingerprint is the lowercase hexadecimal SHA-256 of the DER-encoded certificate. Phones pin the
fingerprint delivered in the pairing code (§6) and refuse any other certificate.

The gateway accepts connections only from loopback, private IPv4 ranges (10/8, 172.16/12,
192.168/16), link-local addresses (169.254/16, fe80::/10), the shared range 100.64/10 used by
private networks such as Tailscale, and IPv6 unique local addresses (fc00::/7). An IPv4-mapped IPv6
source is judged by its IPv4 address. Any other source is closed before the TLS handshake.

Hub advertises the addresses a phone may try: the IPv4 addresses of up network interfaces,
excluding loopback and auto-configured 169.254/16, with the default route's address first. The list
is part of the pairing code and of `GET /m/hello`, so a phone refreshes it on every successful
connection.

### 5.2 Routes

Every route except `POST /m/pair` requires `Authorization: Bearer <device credential>`; a missing,
unknown, or revoked credential is answered `401 unauthorized`. The gateway serves no `/v1/*` route.
Bodies are JSON with camelCase fields; times are ISO-8601 strings.

| Route | Purpose |
|---|---|
| `POST /m/pair` | Consume a pairing code and return a device credential (§6). |
| `GET /m/hello` | Computer id and name, DotCraft version, port, fingerprint, and advertised addresses. Also refreshes the device's last-seen time. |
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
    "computerId": "W2Hk9qD1xP7vZb3nLc0aTg",
    "name": "ANN-PC",
    "port": 47610,
    "fingerprint": "3f9c1b0e…",
    "addresses": ["192.168.1.20", "100.101.102.103"]
  }
}
```

`platform` is `ios` or `android`; `displayName` is required; `osVersion` and `appVersion` are
free-form. A malformed request is answered `400 invalidRequest` before the code is checked, so it
does not use the code up.

`GET /m/hello` answers `{ "computerId", "name", "version", "port", "fingerprint", "addresses" }`,
with the meanings above and `version` the DotCraft version.

`computerId` is 128 random bits encoded as base64url, created with the gateway and kept in
`~/.craft/hub/mobile.json`. It never changes, even when the computer's name, addresses, or
certificate change, so a phone keys everything about a computer by it.

`GET /m/projects` answers:

```json
{
  "projects": [
    {
      "projectId": "9c1d0e5a2b7f4c3d8e6a1b0f",
      "displayName": "dotcraft",
      "kind": "project",
      "running": true,
      "lastActiveAt": "2026-10-03T08:12:00+00:00"
    }
  ]
}
```

`POST /m/projects/{projectId}/ensure` answers one project object, with `running` true once the
AppServer is up.

The projects are Hub's project list ([Hub Architecture §7](../architecture/hub-architecture.md#7-registry-locks-and-state)),
plus the default Chat workspace; a project removed on the computer disappears from the phone. A
`projectId` not in that list is answered `404 projectNotFound` by every `/m/projects` route.
`projectId` is the first 24 hexadecimal digits of the SHA-256 of the normalized workspace path,
case-folded on Windows, so it is stable and never reveals the path. `displayName` is the folder
name; the default Chat workspace is always listed as `Chats` with `kind` `chats` (otherwise
`project`), and the phone identifies it by `kind`, never by name. `lastActiveAt` is the last-opened
time, or `null` for Chats; projects are ordered by it, most recent first.

The relay is opaque: Hub opens the AppServer's loopback WebSocket with that process's token and
copies frames both ways, preserving message type and fragment boundaries. It never parses, rewrites,
logs, or persists payloads. Closing either side closes the other. A relay for a project whose
AppServer is not running is answered `409 projectNotRunning` before the upgrade.

Errors use the Hub error shape ([Hub Architecture §6](../architecture/hub-architecture.md#6-hub-local-api)):

| Code | Status | When |
|---|---|---|
| `unauthorized` | 401 | Missing, unknown, or revoked device credential. |
| `invalidRequest` | 400 | Malformed pairing request, or a WebSocket route reached without an upgrade. |
| `pairingCodeInvalid` | 400 | Unknown, used, superseded, or expired pairing code. |
| `projectNotFound` | 404 | No known project has that id. |
| `projectNotRunning` | 409 | A relay was requested for a project whose AppServer is not running. |
| `appServerStartFailed` | 500 | Ensure could not start the project. The error carries no workspace path or diagnostics. |
| `hubInternalError` | 500 | Unexpected failure. |

Hub closes a gateway WebSocket with reason `deviceRevoked` (status 1008) when the phone is revoked,
and `gatewayOff` (status 1001) when the gateway turns off. When Hub itself stops, it closes them
without a reason, and the phone treats the computer as offline.

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
   128 random bits encoded as base64url, single-use, valid for 10 minutes, stored only as a hash.
   Minting replaces the previous code, so only the most recent unexpired code is valid. Each code
   has a `pairingId` that names it in Hub events without revealing it.
2. Desktop shows the code as a QR code encoding
   `dotcraft://pair?v=1&id=<computerId>&name=<computer>&port=<port>&fp=<sha256>&addr=<a1>,<a2>&code=<code>`,
   each value URL-encoded and addresses comma-joined. The QR code is the only pairing path; it
   carries the fingerprint, so the phone never trusts a certificate on first use.
3. The phone scans it, connects to the first reachable address with the pinned certificate, and asks
   the user to allow this phone to control the named computer.
4. **Allow** calls `POST /m/pair`. Hub returns a `deviceId` and a device credential of 256 random
   bits encoded as base64url, and emits `mobile.devicePaired` with the code's `pairingId` so Desktop
   moves to the paired state without polling.
5. The phone stores the credential in the Android Keystore and the computer record (computerId,
   name, addresses, port, fingerprint, deviceId) in app storage, both keyed by `computerId`.

The credential travels only in the `Authorization` header. It never enters URLs, logs, traces, model
context, or session data, and Hub never logs a pairing code. Hub stores the credential's hash with
the device record in `~/.craft/hub/mobile.json`: `deviceId`, display name, platform, OS version, app
version, paired time, and last-seen time.

Revoking a phone on the computer deletes its record and closes its connections; the phone receives
`deviceRevoked` on `/m/events` when connected, or `unauthorized` on its next request, and forgets
that computer. Removing a computer on the phone calls `DELETE /m/device` when reachable and forgets
the computer either way. Neither touches the phone's other computers.

## 7. Hub Local API for the gateway

Desktop manages the gateway through the loopback Hub Local API routes listed in
[Hub Architecture §6](../architecture/hub-architecture.md#6-hub-local-api), protected by the Hub
token. Their mobile-specific results:

| Endpoint | Result |
|---|---|
| `GET /v1/mobile` | The gateway state object. |
| `POST /v1/mobile/enable` | The state object. A port conflict answers `409 portUnavailable` and leaves the state `failed` with that `failureCode`. |
| `POST /v1/mobile/disable` | The state object. Devices are kept. |
| `POST /v1/mobile/pairings` | `{ "pairingId", "qrPayload", "expiresAt" }`, where `qrPayload` is the `dotcraft://pair` URL of §6. Answers `409 gatewayOff` unless the gateway is on. |
| `DELETE /v1/mobile/devices/{deviceId}` | `204`, or `404 deviceNotFound`. |
| `PUT` / `DELETE /v1/mobile/relay` | The state object (§12.2). |

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
  ],
  "relay": null
}
```

`state` is `off`, `on`, or `failed`. A `failed` state adds `failureCode`, such as `portUnavailable`,
and an English `failureMessage`. `port` is `Hub.MobilePort`. `lastSeenAt` is `null` until the phone
first connects; `connected` is true while the phone has a relay or event socket open. `relay` is
described in §12.2.

Hub SSE events, in the standard event envelope:

| Event | `data` |
|---|---|
| `mobile.stateChanged` | The state object, whenever the gateway turns on, turns off, or fails, and whenever the relay or its `state` changes. |
| `mobile.devicePaired` | `{ deviceId, displayName, platform, pairingId }`. |
| `mobile.deviceRevoked` | `{ deviceId }`, whether the computer or the phone removed the pairing. |
| `mobile.deviceSeen` | `{ deviceId, lastSeenAt, connected }` when the phone's first socket opens, when its last socket closes, and on `GET /m/hello` at most once a minute per phone. |

## 8. Mobile app

### 8.1 Computers and connections

The app pairs with any number of computers. Scanning an unpaired computer adds and selects it once
allowed; scanning one already paired, recognized by `computerId`, replaces its pairing once allowed
and removes the old pairing from the computer. Abandoning a pairing changes nothing. Each computer
keeps its own credential, record, status, projects, and chats, and one computer's failure,
revocation, or removal leaves the others alone. The app remembers the selected computer; every
screen below Home belongs to the computer it was opened from.

In the foreground, and during a background live session (§8.5), the app keeps for every paired
computer its `/m/events` socket and one relayed AppServer connection per running project; otherwise
it closes them in the background. Each relayed connection initializes as an approval-capable client
that supports user-input requests, streaming, and background terminal notifications, and opts out
of `terminal/outputDelta`, reading terminal output from snapshots instead.

- **Status.** A computer is **online** when a gateway route answers, **connecting** while trying
  addresses, and **offline** otherwise. Offline shows its last known projects and chats read-only.
- **Reconnect.** Each computer reconnects independently with exponential backoff and jitter from 1
  to 30 seconds, restarted immediately when the app returns to the foreground or the network
  changes (a repeated report of the same network is not a change). Only launch, foreground return,
  a network change, or a dropped connection shows connecting; background retries keep an offline
  computer offline until one succeeds.
- **Catching up.** After any reconnect the app follows the AppServer recovery rules: read the thread
  header, reload history head pages, and subscribe again. Subscribing replays pending approvals and
  questions, so nothing waiting is lost.
- **Refresh.** Pulling down Home, a project, or a chat refreshes its computer until the refresh
  settles. Online, the phone reloads the project list, connects newly started projects, relists
  chats, rereads the workspace approval default, and on a chat rereads its header and history head
  pages. Offline, with access off, or waiting for a backoff retry, it reconnects at once and settles
  with that attempt (or with one already running). A computer whose identity changed does nothing.

### 8.2 Screens

| Screen | Content |
|---|---|
| Pair | Camera scan, the Allow confirmation, and a connected confirmation. |
| Home | The selected computer: its status and a menu with **Add computer** and **Settings**; one chip per paired computer to switch selection; **Projects**, marking those not running; **Recent** chats of running projects with their state, omitted when empty; search over chat titles; and a compact composer that expands in place into New chat for the most recently used project, keeping any draft when folded back. |
| Project | The project's chats, newest first, above the compact composer. Opening it starts a project that is not running, so its chats and model controls load; Home never starts a project by itself. |
| New chat | The expanded composer, which is the chat's composer, so model, reasoning, approval policy, and Plan mode are set before the first message; a project switcher lists the computer's projects, most recently used first. Expanding it starts a stopped project. Send keeps the message while the chat is created, then opens the chat in place; a failed send returns the message with the error. |
| Chat | Top bar with Back, title, project and computer, the context ring (opens Status), and the chat menu. The transcript follows Desktop's turn layout: a finished turn folds work before its final reply into one **Worked for** row (keeping steering messages, the last plan card, and generated images visible); consecutive tool calls group as on Desktop, into one row naming each kind of work done without counts, with reasoning between them never splitting a group and the trailing group of a running turn naming the call in progress; `CodeMode`, `CodeModeWait`, `clock__Sleep`, and `SendUserMessageAsync` calls are hidden as on Desktop, while the calls a script makes still show; a running turn ends with Desktop's working line: the thinking glyph loops while the turn works, the label shows the turn's reasoning title as on Desktop (its latest heading line, else the first line of its latest written paragraph) while it reasons, keeps a finished reasoning's line until it has shown for two seconds, and otherwise shows Thinking only while nothing else in the turn is on screen; when the turn stops, or while a decision waits, the glyph fades out at once, and the line keeps its space until the next message appears; Copy follows a completed turn's final reply; a completed turn that changed files ends with an **Edited** card carrying Desktop's changes icon. While a decision waits, the decision card ends the transcript and the composer is hidden. The composer carries Add, the approval policy, and model controls; during a turn it steers and offers Stop. Pills above the composer open Changes (while a running turn has changed files), reading **Changes** with the line counts, and Background tasks (while any of the chat's background tasks runs); centered when they fit, the row scrolls sideways when they do not, so neither pill shrinks. A jump-to-latest control appears when scrolled away. History loads as on Desktop: opening a chat reads its newest page of whole Turns, and scrolling near the top (or a page too short to scroll) reads the next older page, keeping the reading position. |
| Add menu | **Photo**, **File**, and **Plan mode** (checked while on). |
| Picker | System actions, commands, and skills matching what follows `/` or `$` in the composer. |
| Changes | Every file the turn changed with additions and deletions, each expandable to its diff. Paths are relative to the chat's `effectiveWorkspacePath`, which is also the base for relative file links in the transcript. |
| File | One read-only file from the computer with **Download** to the phone's `Download/DotCraft/` folder. Images open full screen. |
| Background tasks | **Running** (expanded) and **Completed** (collapsed, counted). Each task shows its title with the elapsed or total duration trailing in tabular figures, over its kind (and an agent's model and tokens), marked Stopped or Failed when it did not finish normally. Running tasks offer Stop; finished tasks expand to their detail: a shell task's last output lines, an agent's role, or a workflow's agent count. |
| Status | Context left, account usage when the provider reports it, the project folder, and the chat ID to copy. |
| Decision card | One card for every decision a chat waits on: an approval, a question, or a plan confirmation. It is the last transcript entry and scrolls with it; the composer stays hidden until the decision is made. |
| Settings | Every paired computer with its status and Remove, **Add computer**, and app information. |

Chat states use one vocabulary: **running**, **needs approval**, **needs answer**, **done**,
**failed**, from `thread/list` runtime snapshots and `thread/status/changed`. A stopped turn is done
and shows a Stopped line. Thread summaries do not carry the last turn's result, so a list shows
**failed** only for a failure the phone has seen.

Screens paint Desktop's conversation surface: in dark, its vertical gradient from the background
lifted by 0.5% of the ink to the background darkened by 20% black; in light, its white surface. Bars,
the composer dock, and the system bars take the surface colour at their edge, and the jump-to-latest
control uses that surface at 70% with a hairline and no shadow, so it reads as the background.

### 8.3 Working with a chat

- The phone lists and starts chats under the Desktop channel identity, so a chat started on either
  can be continued on the other.
- New chat ensures the project's AppServer when needed, starts a thread with the project's defaults,
  and calls `turn/start`.
- A message sent during a turn uses `turn/steer` when the server accepts steering and `turn/enqueue`
  otherwise; Stop calls `turn/interrupt`.
- The decision card shows one request at a time and counts the others waiting. Options are choice
  rows: tapping selects, tapping a selected row answers, and **Submit** answers with the selection.
- An approval shows its type, operation, target, and reason, with **Allow once**, **Allow for
  session**, and **Reject**, answering `item/approval/request` with `accept`, `acceptForSession`, or
  `decline`. The phone never offers `acceptAlways`; permanent grants belong on the computer. An
  approval has no dismiss.
- A question follows Desktop's format: options as rows plus **Other** for typed text, paging through
  multiple questions; **Submit** answers `item/tool/requestUserInput` with every answer.
  **Dismiss** answers a non-blocking request with no answers and interrupts the turn for a blocking
  one, as Desktop does.
- The chat menu offers Copy session ID, Rename (`thread/rename`), Fork (`thread/fork` into the same
  project, opening the copy), and Archive (`thread/archive`, returning to the list). Worktree forks,
  pinning, and actions that open things on the computer stay on Desktop.
- The composer shows the chat's model, reasoning effort, speed, and approval policy (`prompt` or
  `autoApprove`). Any other chat policy, and New chat until the user picks one, shows the project's
  `Permissions.DefaultApprovalPolicy` (`autoApprove`, else `prompt`), read with `config/read` when the
  AppServer advertises `workspaceConfigManagement` and again on `config/changed` for that key.
  Changing a setting sends the whole configuration with `thread/config/update`, effective from the
  next turn. New chat starts on the provider's `isDefault` model from `model/list`, and switching
  provider selects that provider's `isDefault` model; `thread/start` names provider and model and
  carries reasoning, speed, and approval policy only when the user changed them, so AppServer fills
  the rest from the provider's preference. Models and their options come from `model/list`, never
  from app rules, and each control is hidden when the server lacks its capability. The phone never
  changes a chat's Agent Profile.
- **Photo** picks images, scales each to at most 2048 px on its longer side, and re-encodes it as
  JPEG. Before sending, the phone writes each photo to
  `<project>/.craft/attachments/images/<id>.jpg` with `fs/createDirectory` and `fs/writeFile` and
  sends it as a `localImage` part, so the agent can reach it by path later. **File** picks any
  document up to 2 MiB, writes it under `<project>/.craft/attachments/<id>/`, and sends a `fileRef`.
  Attachments wait in the composer until sent. A failed send or upload returns the draft to the
  composer with a notice (naming the file for a failed upload) and **Retry**. A file over the limit
  is refused when picked. **Photo** and **File** are hidden without `capabilities.fileSystem`.
- **Plan mode** toggles with `thread/mode/set` (`plan` or `agent`); New chat in plan mode starts the
  thread with `mode: plan`. A successful `CreatePlan` shows as Desktop's plan card, expandable to
  the full body and to-dos, with Copy. A pending plan confirmation appears in the decision card with
  an implement option and a row to describe adjustments: implementing switches the chat to `agent`
  and sends "Implement the plan." as Desktop does; an adjustment is sent as feedback and stays in
  plan mode. **Dismiss** returns the composer in plan mode. If the switch to `agent` fails, nothing
  is sent and the failure is reported. A chat running an Agent Profile has no Plan mode, because its
  agent keeps a fixed capability scope.
- Typing `/` at the start of a word opens the Picker with system actions, then custom commands from
  `command/list`, then enabled skills from `skills/list`; `$` opens skills only. Matching follows
  Desktop: query characters match in order, each skip lands on a word start, a mid-word start needs
  three characters, and within each group prefixes rank above word starts above mid-word matches. A
  command or skill is inserted inline as `/name` or `$name`, deleted as a whole, and sent as a
  `commandRef` or `skillRef`. Built-in commands are not offered.
- The system actions are **Plan mode**, shown when the chat offers it and toggling it as the Add menu
  does, and **Compact**, shown when `capabilities.manualCompaction` is set, the chat has a turn, and
  nothing is running or compacting. Choosing one removes the typed `/query`. Compact calls
  `thread/compact/start` with a long wait while the send button shows progress; a skipped or failed
  result is reported above the composer.
- The changes pill shows the file count and added and deleted lines from `turn/diff/updated`, or
  from the turn's `fileChange` results until a diff arrives, and goes when the turn ends. A
  completed turn that changed files ends with an **Edited** card with the same counts, rebuilt from
  history's `fileChange` results on reopen; stopped or failed turns have none. Both open Changes for
  that turn. The phone offers no Undo.
- A file chip or a file in Changes opens File with `fs/readFile`. Text shows syntax-highlighted with
  wrapping, images as images, and Markdown as a rendered preview (leading YAML frontmatter as a
  key/value card) switchable to source. Other types, or files over the read limit, show the name and
  path to copy. Without `capabilities.fileSystem`, a chip only shows its path.
- The context ring starts from `contextUsage` in `thread/read` and follows `contextUsage` in
  `item/usage/delta` and terminal compaction events, as on Desktop; without a snapshot it is empty.
  Status shows context left with tokens used and window size, and, for a ChatGPT sign-in, each usage
  window from `provider/auth/usage` for the active provider with share left and reset time.
- Download writes through Android's shared Downloads collection and needs no storage permission; on
  Android 9 and earlier saving there is not supported.
- Replies render GitHub-flavored Markdown as on Desktop. Images show inline: web images load
  directly; images on the computer, including those referenced by `localImage` from any client, are
  read with `fs/readFile`. Local file links show as file chips that open File; web links open the
  browser. File and skill references in user messages show as chips, and sent photos as thumbnails.
  Math is not typeset: formulas show their TeX source as code.
- The phone renders neither Generative UI blocks nor visualizations. A top-level `dotcraft-ui` block
  shows as a tool activity line whose details hold its source; a visualization reference line is
  omitted; Copy keeps only prose.
- A tool activity line fits one line with the tool kind's icon; tapping shows the full command or
  target and output. A running tool reads in the present tense using Desktop's wording, with
  elapsed time. In-progress text (running tools, streaming reasoning, generating images) uses
  Desktop's shimmer treatment, still under reduced motion.
- A user message the computer sent shows Desktop's origin line above it: sent from a workflow,
  automation, app, goal auto-continue, or DotCraft from another thread (`subagentFollowupTask`,
  `subagentInput`). The first message of a chat started from another chat
  (`source.spawnedFromThreadId`) says so too, and opens that chat when tapped. A message with
  `sentAsGoal` is marked as sent as a goal.
- The timeline shows Desktop's dividers for `SystemNotice` items: forked from a conversation,
  connected to or disconnected from a remote host, execution switched back to the computer,
  continuing or stopped continuing in a channel, and context compacted (automatic, manual, or
  recovered from overflow; `micro` shows nothing). Successful Desktop `CreateThread` and
  `SendMessageToThread` calls show as **Chat created** or **Message sent** cards that open the
  chat.
- Live states follow Desktop: `system/event` `compacting` shows a compacting divider after the last
  turn (manual without a `turnId`, automatic with one) until a terminal compaction event or turn
  change; a repeated `streamError` shows the reconnect attempt until the turn ends; a turn the phone
  stopped reads **You stopped** until the chat reloads.
- Failures render as on Desktop: each `error` item shows in place as an error line with its message
  and **Copy**, and a failed turn whose `error` matches no error item adds one at its end. A
  `usageLimitExceeded` failure shows Desktop's usage-limit divider instead; `serverOverloaded` and
  `rateLimitExceeded` show Desktop's overload wording while Copy keeps the original message. When
  the chat's last turn failed with `serverOverloaded` and nothing is running, its last error line
  offers **Retry**, which starts a turn with no input; consecutive capacity retries count down 10,
  30, 120, then 300 seconds before retrying automatically, after which Retry waits for a tap. Of a
  run of turns that hold only identical errors, only the latest shows.
- A background task notification (`triggerKind` `terminal` or `subagent`, or `workflow` with a
  `triggerStatus`) is never a user message. When it starts a turn, the turn has no user message and
  its **Worked for** row starts with the task kind's icon; its accessible name says how the task
  ended, in Desktop's wording. A notification drained into a running turn adds nothing.
- Background task tools read as Desktop names them: an `Exec` still running after its result stays a
  running line while its turn runs and afterwards reads as a started (or stopped) background
  terminal; `SpawnAgent`, `SendMessage`, `ListAgents`, and `TaskStop` read as Desktop labels them,
  and `TaskStop` opens to the subagent's last message when its result carries one.
- **Background tasks** are the chat's work that outlives its turns, from three sources, each used
  only when `initialize` reports its capability:
  - Background shell commands (`capabilities.backgroundTerminals`): `terminal/list` for the thread,
    keeping terminals started with `runInBackground` and terminals still running after their turn. A
    running terminal of the active turn is left out until that turn ends, as on Desktop, and the
    list is reread then. A listed terminal stays listed once it ends. They follow
    `terminal/started`, `terminal/completed`, and `terminal/cleaned`; Stop calls `terminal/stop`.
  - SubAgents (`capabilities.subAgentSessions`): `subagent/children/list` without closed children,
    reread on `subagent/graph/changed` for the chat and `thread/status/changed` for a child. A child
    runs per its thread runtime; Stop calls `turn/interrupt` on its active turn, leaving it open. An
    agent's card adds its model and tokens used: the sum of its turns' `tokenUsage` from
    `thread/turns/list`, raised to `total` from each `thread/tokenUsage/updated`, never decreasing.
    A non-running child reads how its latest turn ended (completed, failed, or stopped when
    cancelled). A child that disappears while running counts as stopped.
  - Workflow runs (`capabilities.extensions.dynamicWorkflows`): `workflow/run/list` for the thread,
    reread on `workflow/run/updated`; paused runs count as running; Stop calls `workflow/run/stop`
    when the run's controls allow it and the capability lists `stop`.

  The phone loads a chat's tasks when it opens or subscribes to the chat and reloads every known
  chat's tasks after reconnecting to the project. Notifications are honoured for any listed chat,
  and repeated refreshes of one source for one chat coalesce into one request at a time. Background
  tasks never mark the chat itself as running.

### 8.4 Mascot

The app renders the DotCraft mascot with the shared `@dotcraft/avatar` package, so it matches
Desktop. It marks moments, not every screen:

| Moment | Mascot |
|---|---|
| Home | Reflects the selected computer: idle when online, looking around while connecting, asleep when offline or phone access is off, and holding up its question sign while any chat needs the user. |
| Pair | Greets on the scan screen, waits while reaching the computer, and celebrates when pairing completes. |
| Transitions | Opening a chat while history loads, a project starting, and reconnecting show the mascot working with one line saying what is happening, in place of skeleton rows. |
| Chat | A new chat's empty transcript greets with the mascot; a chat running an Agent Profile shows that profile's avatar instead. |
| Empty and error states | Empty lists, the revoked notice, and the identity-changed notice pair one sentence with a matching expression. |

The mascot uses the original brand appearance, not the Desktop pet's outfit. Motion follows the
reduced-motion setting, and the mascot is decorative to screen readers.

### 8.5 Live session

When the app moves to the background while any chat on any computer is running, waiting on the
user, or has a background task running, it keeps its connections in an Android foreground service of
type `connectedDevice` with one ongoing notification. The notification follows one chat across all
computers — the most recent one waiting on the user, else the most recently updated running one —
showing its title and its pending request, latest tool step, or that it is thinking or replying. Its
header names the followed chat's computer, or counts running and waiting chats when there are
several; tapping opens the followed chat. On Android 16 and later it requests promotion to a Live
Update with the followed chat's state as its status chip. A chat followed only for its background
tasks shows their count and does not count as a running chat in the header.

- During a live session the phone subscribes to every running or waiting chat and every chat with a
  running background task, because pending requests and turn results reach only subscribed
  connections ([AppServer Protocol](../protocols/appserver-protocol.md) §7.6). So the turn a finished
  Workflow starts reaches the phone and ends in the usual done or failed notification. Tasks started
  while the phone was away, in a chat it has not opened since, do not keep a live session.
- A new approval posts a heads-up notification naming the chat (and its computer when more than one
  is paired) with **Allow once** and **Reject** actions that answer without opening the app; its body
  opens the chat.
- A new question posts a notification that opens the chat.
- A turn that ends while the app is in the background posts one done or failed notification.
- The session ends, closing its connections and its ongoing, approval, and question notifications,
  when nothing has been running, waiting, or running in the background for two minutes, when the user
  chooses **Disconnect** on the ongoing notification, or when every computer with work has been
  unreachable for two minutes. Done and failed notifications stay.
- Android cannot request permission from the background, so the app requests notification
  permission the first time a chat is running or waiting while it is open. Without it, the app
  closes connections in the background as outside a live session.

### 8.6 Localization

The app ships the same locales as Desktop and follows the phone's language.

## 9. Desktop Phones segment

The **Phones** segment of Desktop's Connections settings turns the gateway on and off, mints
pairing codes, and lists and revokes phones through §7. Its Desktop rules are owned by
[Desktop Client](desktop-client.md) §6.15. The contract it relies on:

- The access switch maps to enable and disable, and its description states that a paired phone can
  do anything the user can do in DotCraft on this computer.
- Hub keeps one unexpired code at a time, so minting again invalidates the previous code. The
  pairing dialog moves to its paired state on `mobile.devicePaired` for the code it minted.
- The phone list stays available while access is off or failed, so a phone can be removed without
  turning access on.
- The Hub token and pairing codes never enter the renderer's persisted state.

## 10. Attention notifications

Turns that start waiting on an approval or a question raise the `approvalRequested` and
`inputRequested` notifications defined in
[Hub Architecture §10](../architecture/hub-architecture.md#10-tray-and-notifications), so the user
learns that work is blocked whether the answer then comes from the computer or the phone.

## 11. Multi-client rules

A phone and a Desktop window may watch the same thread. Pending approvals and questions belong to
the thread: both receive them, the first answer resolves each one and both dismiss it, and a
connection that goes away never resolves one. A request waits without a timeout until answered or
its turn ends, so a phone that reconnects or opens the chat later can still answer it. The AppServer
owns these guarantees; see [AppServer Protocol](../protocols/appserver-protocol.md) §7.6.

## 12. Off-network access

A relay lets a phone reach the gateway from any network. The user runs it on a server both sides can
reach, with `dotcraft relay serve` or the relay container image. The relay forwards bytes only: the
phone still speaks TLS to the gateway with the pinned certificate and its device credential, so the
relay sees only ciphertext and never a credential, project, or chat.

### 12.1 Relay

`dotcraft relay serve --listen <url> --token <token>` serves three WebSocket routes, normally behind a
reverse proxy that terminates public TLS. The token may come from `DOTCRAFT_RELAY_TOKEN` instead of
`--token`, and the relay compares it in constant time.

| Route | Caller | Purpose |
|---|---|---|
| `GET /r/host?host=<hostId>` | Hub | Control channel for one computer, authenticated by `Authorization: Bearer <relay token>`. One live control channel per host id; a newer one replaces the older, which is closed with the reason `replaced`. |
| `GET /r/connect?host=<hostId>` | Phone | Opens a tunnel to that computer. Answered `404 hostOffline` when the computer has no control channel. |
| `GET /r/accept?tunnel=<tunnelId>` | Hub | Hub's side of a tunnel, authenticated by the relay token. |

For each phone connection the relay picks a random `tunnelId`, sends
`{ "type": "open", "tunnel": "…" }` on the host's control channel, and splices the phone's socket
with Hub's `/r/accept` socket for that tunnel if it arrives within ten seconds, otherwise closing the
phone's socket. Tunnel payloads are binary WebSocket messages carrying the raw bytes of one TCP
stream. The relay keeps no state beyond live sockets, logs no payloads, and closes a tunnel when
either side closes. Its errors use the Hub error shape; a missing or wrong token is answered
`401 unauthorized`.

The host id is the computer's `computerId` (§5.2). Reaching a host id still requires the pinned
certificate and a device credential.

### 12.2 Hub

When the gateway is on and a relay is configured, Hub keeps the control channel open with jittered
backoff from 1 to 30 seconds. For each `open` it connects `/r/accept` to a loopback TCP connection to
the gateway port and copies bytes both ways, so relayed phones arrive from loopback and follow §5
unchanged.

The relay URL and token are stored in `~/.craft/hub/mobile.json` and set with
`PUT /v1/mobile/relay` (`{ "url", "token" }`) or removed with `DELETE /v1/mobile/relay`. The state
object carries `relay: { "url", "state" }` with `state` `connecting`, `connected`, or `failed`, or
`relay: null`. `url` is the relay's base URL with an `https` or `wss` scheme (`http` and `ws` are
accepted for testing); Hub and phones append the route paths and connect with the matching
WebSocket scheme. A malformed URL or empty token is answered `400 invalidRequest`. While the gateway
is off, the relay stays configured but Hub does not connect, and clients present it as paused. The
token never appears in the state object or events.

The pairing payload adds `relay=<url>` when a relay is configured, and `GET /m/hello` adds `relay`
(`{ "url" }` or `null`), so phones paired before the relay was set learn it on their next direct
connection. Setting the relay does not change existing pairings.

### 12.3 Phone

The phone tries the computer's direct addresses first, each with a three-second connect timeout, then
the relay, and keeps whichever path answered for that connection. Over the relay, each TLS
connection the phone would open runs through its own tunnel: the relay base URL with its scheme
mapped to `ws` or `wss`, followed by `/r/connect?host=<hostId>`, carrying the same pinned certificate,
device credential, routes, and AppServer protocol. The computer is offline when neither path
answers, including when the relay answers `hostOffline`. Release builds allow only `https` and `wss`
relays.

## 13. Failure behavior

| Situation | Behavior |
|---|---|
| Gateway port in use | Turning on fails with `portUnavailable` and the state becomes `failed`; Desktop shows the reason. Hub retries the port on its next start while the gateway stays on. |
| Certificate mismatch | The phone refuses that computer and shows that its identity changed, with Remove as the only action. Other computers are unaffected. |
| Pairing code expired or used | `pairingCodeInvalid`; the phone asks for a new code. |
| Device revoked | The phone forgets that computer with a notice and selects another paired computer; with none left it returns to Pair with the notice. |
| Relay requested for a stopped project | `projectNotRunning` before the upgrade; the phone treats the project as stopped. |
| Project AppServer stops | The relay closes; open chats of that project become read-only with a notice and a Start action, and the phone reconnects when `/m/events` reports it started. |
| Computer asleep or unreachable | The computer shows offline. |
| Relay unreachable or wrong token | Hub reports `relay.state` `failed` and keeps retrying; phones on the local network are unaffected. |
| Gateway turned off | Connections close with `gatewayOff`; the phone keeps the last synced chats read-only and shows that phone access is off, remembering that reason until a connection succeeds again so a restarted app does not report it as merely offline. |
| Project cannot start | Opening a stopped project while the computer is offline or access is off, or when its start fails, reports that the computer can't start that project right now. |
