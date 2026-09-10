# DotCraft Satellite

| Field | Value |
|---|---|
| Version | 0.4.0 |
| Status | Draft |
| Date | 2026-09-10 |
| Parent spec | [Remote Tool Host](../architecture/remote-tool-host.md) |
| Related Specs | [Hub Architecture](../architecture/hub-architecture.md), [Desktop Client](desktop-client.md) |

## Overview

DotCraft Satellite is the Windows tray application that makes a Remote Tool Host operable by a
non-technical machine owner. A colleague installs it once, accepts an invitation from an engineer
in one consent window, and from then on the engineer's DotCraft agent can run tools on that
machine while the owner sees who is connected and can stop it at any time.

The product name in user-facing copy is Satellite (卫星). Internal identifiers keep the technical
terms: the specification is Remote Tool Host, the CLI noun is `tool-host`, the project is
`DotCraft.Satellite`, and the executable is `dotcraft-satellite.exe`, matching `dotcraft.exe`.

The key words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative.

## Goal

Let a machine owner who never uses a command line install, accept, observe, pause, and revoke
remote tool execution on their own machine, with the same runtime, state, and security level as the
CLI-driven Remote Tool Host.

## Scope

- A per-user Windows application: one process, one tray icon, no administrator rights, no Windows
  service.
- A consent window shown for every invitation before any credential is stored.
- A tray icon with four states and a menu that shows who is connected and gives every paired
  machine one submenu of the actions that name it — open its task folder, manage its access,
  disconnect it, revoke it. Only what does not name a machine stays at the top level: pause
  or resume sharing, paste an invitation link, and quit.
- A floating island above the other windows for the whole time the machine is in use. It names who
  is using it, shows the running operation and command, lists the connected machines with a
  disconnect and an open-folder action each, offers one machine-wide pause, and carries the owner
  approval request.
- Operating-system notifications when a peer connects or disconnects.
- Login autostart, single-instance behavior, and handling of the `dotcraft://satellite/join` link.
- A per-user installer with an update channel.
- Localization in Simplified Chinese and English, extensible to the other Desktop locales.

## Non-goals

- Administrator rights, a Windows service, or any HKLM write.
- Changing the Remote Tool Host security model: one shared secret per pairing, stored in the
  Windows credential store.
- Multi-user roles or approval routing to third parties.
- Replacing DotCraft Desktop or hosting a conversation surface.
- Any Universe integration.

## Application boundary

Satellite MUST host the Remote Tool Host runtime in-process through the public hosting entry point
of `DotCraft.RemoteTools`. It MUST NOT spawn `dotcraft.exe` to serve. Exactly one Satellite process
and one tray icon run per signed-in user.

Satellite is a shipped DotCraft product artifact. Its solution is separate from `dotcraft.sln`
only because the cross-platform build runs on Linux; that exclusion is a build constraint, not a
sample designation.

Satellite requires the WebView2 Runtime, which ships with Windows 11 and with Microsoft Edge.
Without it the consent window shows a message saying so and offers Decline only. Pages are hosted
in WebView2's window-to-visual mode: plain windowed hosting inside a WinUI window receives no
mouse input on Windows 11, and WinUI's own XAML control cannot render a transparent page.

## State sharing with the CLI

Satellite and `dotcraft tool-host *` MUST resolve the same state root
`~/.craft/remote-tool-host/` and the same credential-store prefix. `dotcraft tool-host status` on
the machine MUST reflect state written by Satellite and vice versa. Satellite MUST NOT introduce a
second state root or an environment override.

When Satellite is running, `dotcraft tool-host join` MUST forward the invitation to the running
Satellite instead of pairing directly, so the consent window is always the path by which a
credential is stored. The hand-off is the user-level named pipe `DotCraft.Satellite.<user SID>`,
carrying one JSON line `{"kind":"join","url":"<invite-url>"}`. When no listener answers within a
short probe, the CLI pairs directly.

## Consent

Satellite MUST show inviter and Hub before storing credentials. Consent offers `fullAccess`
(default) and `workspacePreferred` as two side-by-side cards, the default first, each stating its
consequences in copy that is visible whether or not that card is selected. `workspacePreferred`
allows ordinary task-folder file operations and asks the local owner before external files, new
commands, nonempty terminal input and language-server execution. This is approval-based, not an
OS sandbox. Full access remains subject to Windows permissions and Host deny policy.

Full access is the default because lending a whole machine is the ordinary case, and it carries
no separate acknowledgement step. Its risk is stated by copy the owner has already read rather
than by an extra click, so the common path is: open the window, choose Allow.

Selecting the workspace card MUST open the folder picker on that transition alone, so choosing a
folder never requires typing a path. Selecting an already-selected card MUST NOT reopen it; a
distinct change action does. Cancelling the picker keeps the card selected and keeps the
suggested folder. A new per-pairing task folder is suggested and created only on acceptance. The
owner may choose an existing folder instead. Whole drives and user profiles remain invalid. An
unusable folder MUST NOT block full access, which is not scoped to a folder. Reauthorizing an
existing pairing MUST NOT open the picker.

Owner requests show the inviter and exact operation, offer Allow once or Deny, queue serially,
and expire after two minutes. Cancellation, disconnect, pause, revoke and authorization changes
invalidate pending decisions. No local UI means deny. Inviter approval never replaces owner
approval. Shell approval explicitly covers possible effects outside the task folder.
Existing pairings with no mode require local reauthorization. The tray exposes mode, folder and
authorization management. Mode changes drain execution resources before changing authorization.

Parsing an invitation link MUST be a pure operation. Filling the window costs exactly one `GET` of
the invitation URL, which reads the inviter and expiry and writes nothing on either
machine; a failed or unanswered fetch MUST still show the window with whatever the link itself
carries. Nothing is stored and no pairing exists until the owner chooses Allow. Decline leaves no
trace beyond an audit entry. The inviter name is attacker-influenced text: it MUST be rendered
as plain text and MUST be length-capped.

The consent window uses a standard title bar and the DotCraft accent color regardless of the
operating-system accent, so a security prompt looks the same on every machine.

The window's content is a web page hosted in a WebView2 control. The page owns the look and
receives every string it renders from the Satellite catalogs, inserting the inviter name as text;
the window owns the title bar, the size, the folder picker and the accept path. Its layout,
colour, type and the mascot artwork rendered from the shared avatar package are specified by the
design lab entry for `flows/satellite-consent`.

## States

Satellite has exactly four states with the precedence `offline > paused > connected > standby`:

| State | Condition |
|---|---|
| `offline` | no pairing exists, or the control connection to the Hub is down, including while retrying |
| `paused` | the owner paused sharing; the control connection stays up and data sessions are refused |
| `connected` | at least one data session is open |
| `standby` | paired, control connection up, no data session |

The tray icon, its tooltip, and the menu status line MUST reflect the current state. The menu MUST
show who is connected and since when. The island carries the current command, so work running on the
machine is visible without opening anything. Offline outranks paused so the owner is never told that
resuming would help while the Hub is unreachable.

## Island

The island is a floating capsule at the top of the primary display, above the other windows, with
no taskbar or Alt-Tab presence. It MUST be visible for the whole of `connected` and for none of the
other three states: `standby` has nobody to name, `paused` refuses sessions, and `offline` cannot
know. It has no close or hide action and no grip: the whole capsule surface drags, and its position
is remembered per display in `~/.craft/satellite.json`.

Its states have the precedence `approval > running > expanded > compact`. Compact names the peer
and when it connected, or counts the machines when there are several. Running names the operation
in the same vocabulary the approval request uses, previews the command on one truncated line, and
counts concurrent tools. Expanded, on hover or click, lists each connected machine with its access
mode and, per row, disconnect and open-task-folder; the machine-wide pause sits once under the
rows. Approval names the inviter, the operation and the target, counts the requests waiting behind
it, and shows the time left.

Text the island renders from a peer or an inviter is attacker-influenced: it MUST be rendered as
plain text, length-capped, and truncated to one line, with the full value available on hover.

Its surface is a translucent elevated fill (`#242424` at 90% in dark, white at 92% in light) with
no border line: the edge is a rim light that follows the pointer, and a soft shadow separates it
from the desktop. The capsule is a web page hosted in a WebView2 control inside a per-pixel
transparent, always-on-top window. The page owns the look and the motion; the window owns
geometry, input and the desktop: it places the capsule, hands the page its box on every state
change, keeps the margin around the capsule transparent to the mouse so clicks there reach
whatever is under it, and drags.

Every size property — the entrance drop, width, height and corner radius — follows one spring
(stiffness 400, damping 30, mass 1) from one start time, so the capsule reads as a shape that
stretches rather than a box swapped for another box. The content inside cross-fades under that
motion: what is leaving fades out over 120ms while the shape is already moving, and what replaces
it lands over 260ms after a 90ms delay.

Its geometry, colour, type and motion are specified by the design lab entry for
`components/satellite-island`, and its localized copy reuses the consent and approval keys.

## Notifications

Satellite MUST raise an operating-system notification when a peer connects and when it
disconnects. Notifications MUST NOT include command output, file contents, or any credential.

An owner request MUST be answered on the island, where the owner is already being told that the
machine is in use, and it expires there. It MUST NOT be raised as a notification.

## Invitation link

The invitation URL served by the Hub is `http://<hub-host>:<port>/i/<inviteId>`. Opened in a
browser it is the whole install path: the page names the inviter, offers the
Satellite installer from the same Hub, and repeatedly attempts to open the equivalent deep link
`dotcraft://satellite/join?invite=<url-encoded invitation URL>`, so the consent window appears as
soon as Satellite exists on the machine. Satellite MUST accept both forms
in `join`, MUST reject invitations whose Hub endpoint is not an `http` or `https` URL, and MUST
reject expired invitations with a message that tells the owner to ask for a new link.

## Coexistence with Desktop

Registration of the `dotcraft://` URL protocol under `HKCU\Software\Classes` follows one rule:

- when no handler is registered, or the registered handler is a stale path to Satellite itself,
  Satellite registers or repairs the handler;
- when another program owns the handler, Satellite MUST NOT overwrite it. It publishes its
  executable path under `HKCU\Software\DotCraft\Satellite`, and Desktop forwards every
  `dotcraft://satellite/*` link to that executable. Desktop never completes a pairing itself.

Satellite MUST provide a paste-invite-link action in its tray menu so an invitation can be accepted
regardless of which program owns the protocol handler.

When Desktop is installed on the same machine, its Connections page carries a Share this PC segment
that presents the same runtime state Satellite owns: whether Satellite is installed, who may run
tools here, which folder each of them may reach, and when they were paired. Desktop reads that
state; Satellite remains the only writer of pairings and credentials.

## Autostart and lifecycle

Satellite registers login autostart under the current user's `Run` key with a background flag that
suppresses any window. Installing Satellite autostart MUST remove the CLI autostart entry, and
`dotcraft tool-host autostart install` MUST refuse while Satellite autostart exists, so a machine
never runs two Remote Tool Host processes against the same state root.

A second Satellite instance MUST hand any invitation it was started with to the running instance
and exit without initializing its user interface.

## Localization

Simplified Chinese and English are required. A missing translation MUST fall back to English and
MUST NOT surface a raw key. Adding a locale MUST NOT require a code change beyond adding the
catalog. Locale resolution follows the Desktop alias table (`zh`, `zh-CN`, `zh-SG` map to
`zh-Hans`).

## Installer and update

Satellite ships as a per-user installer that requires no elevation and provides an update channel.
The installer version follows the DotCraft product version, and the inviting machine's Hub serves
the copy that matches its own build, so the two ends cannot drift apart between the invitation and
the pairing. Uninstall MUST stop the runtime, remove
autostart, remove the protocol handler only when Satellite owns it, remove the notification
registration, and offer to revoke every pairing, defaulting to revoke, so no live shared secret
outlives the application that used it.

## Acceptance checklist

- A fresh Windows machine without administrator rights installs Satellite, opens an invitation
  link, and accepts it in one window; the tray shows `standby` afterwards.
- The engineer's Desktop shows the machine and can run a command on it; the tray shows
  `connected` and the island appears, names the engineer, and shows the running command.
- The island stays above other windows for the whole session, survives being dragged to another
  position, comes back where it was left, and slides away when the last session closes.
- A tool that needs permission is answered on the island; a second request waits behind the first
  and is counted; an unanswered request denies itself after two minutes.
- Disconnect, pause, and revoke from the tray or the island take effect immediately and are visible
  on the engineer's side, and each cancels every request still waiting.
- Stopping the Hub on the engineer's machine moves the tray to `offline`; restarting it moves the
  tray back to `standby` without owner action.
- Signing out and back in restarts Satellite in the background with no window.
- `dotcraft tool-host status` on the machine reports the pairing Satellite created.
- On a machine with Desktop installed, `dotcraft://workspace/open` still opens Desktop and a
  `dotcraft://satellite/join` link still reaches Satellite.
- Uninstall leaves no autostart entry, no protocol handler owned by Satellite, no notification
  registration, and, when the owner accepts the default, no stored credential.

## Open questions

- Whether the consent window should also let the owner restrict which tool classes an inviter may
  run, or whether Host-local tool policy through the CLI remains the only knob in v1.
- Whether the tray should surface the daily audit summary directly or only open the audit folder.
