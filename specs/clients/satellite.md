# DotCraft Satellite

| Field | Value |
|---|---|
| Version | 0.9.1 |
| Status | Draft |
| Date | 2026-10-19 |
| Parent spec | [Remote Tool Host](../architecture/remote-tool-host.md) |
| Related Specs | [Hub Architecture](../architecture/hub-architecture.md), [Remote Screen View](../features/remote-screen-view.md) |

## Overview

DotCraft Satellite is the Windows tray application that makes a Remote Tool Host operable by a
non-technical machine owner. A colleague installs it once, accepts an engineer's invitation in one
consent window, and from then on the engineer's DotCraft agent can run tools on that machine while
the owner sees who is connected and can stop it at any time.

The user-facing product name is Satellite (卫星). Technical identifiers keep the Remote Tool Host
terms: the CLI noun is `tool-host`, and the executable is `dotcraft-satellite.exe`.

The key words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative.

## Non-goals

- Administrator rights, a Windows service, or any HKLM write.
- Changing the Remote Tool Host security model: one shared secret per pairing, stored in the
  Host state directory.
- Multi-user roles or approval routing to third parties.
- Replacing DotCraft Desktop or hosting a conversation surface.
- Remote input, remote control, or recording of the screen.

## Application boundary

Satellite MUST host the Remote Tool Host runtime in-process through its public hosting entry point
and MUST NOT spawn `dotcraft.exe` to serve. Exactly one Satellite process and one tray icon run per
signed-in user. In tray mode the process ends only through explicit quit, uninstall, or external
termination. Closing or losing an auxiliary window such as the island or consent surface MUST NOT
stop the runtime or its Hub control connection; a failed island degrades to the tray.

Satellite may connect through DotCraft Hub or an embedding service using the
[embedded execution surface](../architecture/remote-tool-host.md#21-embedded-execution-surface).
Satellite owns device consent and visibility; conversations and permissions belong to the embedding
product. An embedding product may host its own Remote Tool Host under its own state root; those
pairings belong to the product.

Tool sessions follow the [execution-session lifecycle](../architecture/remote-tool-host.md#52-execution-session-ownership).
Owner pause, disconnect, authorization changes, and revoke keep their device-level effect.

Satellite requires the WebView2 Runtime. Without it the consent window says so and offers Decline
only.

## State sharing with the CLI

Satellite and `dotcraft tool-host *` MUST resolve the same state root `~/.craft/remote-tool-host/`,
which also holds the pairing credentials, so `dotcraft tool-host status` reflects state written by
Satellite and vice versa. Satellite MUST NOT introduce a second state root or an environment
override.

When Satellite is running, `dotcraft tool-host join` MUST forward the invitation to it instead of
pairing directly, so the consent window is always the path by which a credential is stored. The
hand-off is the user-level named pipe `DotCraft.Satellite.<user SID>`, carrying one JSON line
`{"kind":"join","url":"<invite-url>"}`. When no listener answers within a short probe, the CLI pairs
directly.

## Consent

Satellite MUST show inviter and Hub before storing credentials. Consent offers `fullAccess`
(default) and `workspacePreferred` side by side, the default first, each stating its consequences
whether or not it is selected. `workspacePreferred` allows ordinary task-folder file operations and
asks the local owner before external files, new commands, nonempty terminal input, and
language-server execution. This is approval-based, not an OS sandbox. Full access remains subject
to Windows permissions and Host deny policy.

Selecting the workspace option MUST open the folder picker on that transition alone, so choosing a
folder never requires typing a path; reselecting it does not reopen the picker, a distinct change
action does. Cancelling the picker keeps the option and the suggested folder. A new per-pairing task
folder is suggested and created only on acceptance; the owner may choose an existing folder instead.
Whole drives and user profiles are invalid. An unusable folder MUST NOT block full access.
Reauthorizing an existing pairing MUST NOT open the picker.

Owner requests show the inviter and exact operation, offer Allow once or Deny, queue serially, and
expire after two minutes. Cancellation, disconnect, pause, revoke, and authorization changes
invalidate pending decisions. No local UI means deny. Inviter approval never replaces owner
approval. Shell approval explicitly covers possible effects outside the task folder. A pairing with
no authorization mode requires local reauthorization. The tray exposes mode, folder, and
authorization management; mode changes drain execution resources before changing authorization.

Viewing the machine's screen ([Remote Screen View](../features/remote-screen-view.md)) needs no
separate consent: a view is available to every pairing whose authorization is valid while sharing is
not paused. Pause refuses new views and ends live ones; disconnect, revoke, and authorization
changes end them. The owner is always shown that it is happening, on the island and by notification.

Parsing an invitation link MUST be a pure operation. Filling the window costs exactly one `GET` of
the invitation URL, which writes nothing on either machine; a failed fetch MUST still show the window
with whatever the link carries. Nothing is stored and no pairing exists until the owner chooses
Allow. Decline leaves no trace beyond an audit entry. The inviter name is attacker-influenced: it
MUST be rendered as plain text and length-capped.

The consent window uses a standard title bar and the DotCraft accent color regardless of the
operating-system accent, so a security prompt looks the same on every machine. Its content is a
hosted web page that receives every string from the Satellite catalogs; the window owns the title
bar, size, folder picker, and accept path.

## States

Satellite has exactly four states with the precedence `offline > paused > connected > standby`:

| State | Condition |
|---|---|
| `offline` | no pairing exists, or the control connection to the Hub is down, including while retrying |
| `paused` | the owner paused sharing; the control connection stays up and data sessions are refused |
| `connected` | at least one data session is open, of either kind; a screen view alone is enough |
| `standby` | paired, control connection up, no data session |

The tray icon, tooltip, and menu status line MUST reflect the current state, and the menu MUST show
who is connected and since when. Offline outranks paused so the owner is never told that resuming
would help while the Hub is unreachable.

## Island

The island is a floating capsule at the top of the primary display, above other windows, with no
taskbar or Alt-Tab presence. It MUST be visible whenever at least one pairing exists, in every state,
so a lent-out machine always says so. It is hidden only while no pairing exists or its page failed to
load. It has no close or hide action; the whole capsule drags, and its position is remembered per
display in `~/.craft/satellite.json`.

Outside `connected` the capsule is one quiet line using the tray status wording, with no rows,
expansion, or approval: `standby` says the machine is ready; `paused` says sharing is paused and
offers resume; `offline` says the machine is not connected. At sign-in the capsule waits a short
grace for the first dial before showing `offline`, so a machine that connects within seconds never
flashes it.

Within `connected` its states have the precedence `approval > running > expanded > compact`:

- **Compact** names the peer and when it connected, or counts machines when there are several.
- **Running** names the current operation in the approval vocabulary, previews the command on one
  truncated line, and counts concurrent tools, so work on the machine is visible without opening
  anything.
- **Expanded** (hover or click) adds one row per connected machine with its access mode and
  connection time, disconnect and open-task-folder actions, and one machine-wide pause. Nothing
  already on the compact line is repeated.
- **Approval** names the inviter, operation, and target, counts queued requests, and shows the time
  left.

While a machine's screen is being viewed, the capsule's glyph becomes an eye and that machine's row
says the screen is shared. The marker changes no mode, uses no colour, and has no action; pause is
the owner's answer.

Text from a peer or inviter is attacker-influenced: it MUST be rendered as plain text, length-capped,
and truncated to one line, with the full value on hover.

The capsule is a hosted web page in a transparent, always-on-top window. The page owns look and
motion; the window owns geometry, input, and dragging, and keeps the margin around the capsule
click-through. Size changes animate as one continuous shape. Localized copy reuses the consent and
approval keys.

## Notifications

Satellite MUST raise an operating-system notification when a peer connects, disconnects, starts
viewing the screen, and stops. Notifications MUST NOT include command output, file contents, or any
credential.

An owner request MUST be answered and expire on the island, never as a notification.

## Invitation link

The Hub's invitation URL is `http://<hub-host>:<port>/i/<inviteId>`. Opened in a browser it is the
whole install path: the page names the inviter, offers the Satellite installer from the same Hub,
and repeatedly attempts the deep link `dotcraft://satellite/join?invite=<url-encoded invitation URL>`,
so consent appears as soon as Satellite exists on the machine. Satellite MUST accept both forms in
`join`, MUST reject invitations whose Hub endpoint is not an `http` or `https` URL, and MUST reject
expired invitations with a message telling the owner to ask for a new link.

## Coexistence with Desktop

Registration of the `dotcraft://` protocol under `HKCU\Software\Classes` follows one rule:

- when no handler is registered, or the handler is a stale path to Satellite itself, Satellite
  registers or repairs it;
- when another program owns the handler, Satellite MUST NOT overwrite it. It publishes its executable
  path under `HKCU\Software\DotCraft\Satellite`, and Desktop forwards every `dotcraft://satellite/*`
  link there. Desktop never completes a pairing itself.

The tray menu MUST offer a paste-invite-link action so an invitation can be accepted regardless of
who owns the protocol handler.

When Desktop is installed on the same machine, it may present Satellite's runtime state (installed
or not, who may run tools here, their reachable folder, and when they were paired). Desktop only
reads that state; Satellite remains the only writer of pairings and credentials.

## Autostart and lifecycle

Satellite registers login autostart under the current user's `Run` key with a background flag that
suppresses any window. Installing Satellite autostart MUST remove the CLI autostart entry, and
`dotcraft tool-host autostart install` MUST refuse while Satellite autostart exists, so a machine
never runs two Remote Tool Host processes against one state root.

A second Satellite instance MUST hand any invitation it was started with to the running instance and
exit without initializing its user interface.

A control or data connection failure or an unexpected runtime completion leaves the tray alive.
Diagnostics follow [Logging](../architecture/logging.md) and exclude credentials, RPC arguments,
command output, and file contents. Tray callback failures are contained to the affected operation;
island failures disable the island while the tray and Remote Tool Host remain available.

## Localization

Simplified Chinese and English are required. A missing translation MUST fall back to English and
MUST NOT surface a raw key. Adding a locale requires only adding its catalog. Locale resolution
follows the Desktop alias table (`zh`, `zh-CN`, `zh-SG` map to `zh-Hans`).

## Installer and update

Satellite ships as a per-user installer that requires no elevation and provides an update channel.
Its version follows the DotCraft product version, and the inviting Hub serves the matching build, so
the two ends cannot drift between invitation and pairing. Uninstall MUST stop the runtime, remove
autostart, remove the protocol handler only when Satellite owns it, remove the notification
registration, and offer to revoke every pairing, defaulting to revoke, so no live shared secret
outlives the application.
