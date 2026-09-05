# DotCraft Satellite

| Field | Value |
|---|---|
| Version | 0.1.0 |
| Status | Draft |
| Date | 2026-09-05 |
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
- A tray icon with four states and a menu that shows who is connected, what is running, and offers
  disconnect, pause, revoke, open folder, paste invite link, and quit.
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

## State sharing with the CLI

Satellite and `dotcraft tool-host *` MUST resolve the same state root
`~/.craft/remote-tool-host/` and the same credential-store prefix. `dotcraft tool-host status` on
the machine MUST reflect state written by Satellite and vice versa. Satellite MUST NOT introduce a
second state root or an environment override.

When Satellite is running, `dotcraft tool-host join <url>` MUST forward the invitation to the
running Satellite instead of pairing directly, so the consent window is always the path by which a
credential is stored. The hand-off is the user-level named pipe `DotCraft.Satellite.<user SID>`,
carrying one JSON line `{"kind":"join","url":"<invite-url>"}`. When no listener answers within a
short probe, the CLI pairs directly.

## Consent

Satellite MUST show a consent window before storing any peer credential. The window MUST show:

- the inviter's display name and the Hub machine it comes from;
- the purpose text carried by the invitation;
- the proposed workspace folder, which the owner MAY change before accepting;
- an explicit list of what acceptance grants: reading and changing files inside that folder,
  running commands on this machine, and that access exists only while this machine stays signed in.

Parsing an invitation link MUST be a pure operation. Filling the window costs exactly one `GET` of
the invitation URL, which reads the inviter, purpose, proposed folder, and expiry and writes
nothing on either machine; a failed or unanswered fetch MUST still show the window with whatever
the link itself carries. Nothing is stored and no pairing exists until the owner chooses Allow.
Decline leaves no trace beyond an audit entry. Purpose and inviter name are attacker-influenced
text: they MUST be rendered as plain text and MUST be length-capped.

The consent window uses a standard title bar and the DotCraft accent color regardless of the
operating-system accent, so a security prompt looks the same on every machine.

## States

Satellite has exactly four states with the precedence `offline > paused > connected > standby`:

| State | Condition |
|---|---|
| `offline` | no pairing exists, or the control connection to the Hub is down, including while retrying |
| `paused` | the owner paused sharing; the control connection stays up and data sessions are refused |
| `connected` | at least one data session is open |
| `standby` | paired, control connection up, no data session |

The tray icon, its tooltip, and the menu status line MUST reflect the current state. The menu MUST
show who is connected and since when, and the current command while one runs. Offline outranks
paused so the owner is never told that resuming would help while the Hub is unreachable.

## Notifications

Satellite MUST raise an operating-system notification when a peer connects and when it
disconnects. Notifications MUST NOT include command output, file contents, or any credential.

## Invitation link

The invitation URL served by the Hub is `http://<hub-host>:<port>/i/<inviteId>`. Opened in a
browser it is the whole install path: the page names the inviter and the purpose, offers the
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
  `connected`, names the engineer, and shows the running command.
- Disconnect, pause, and revoke from the tray take effect immediately and are visible on the
  engineer's side.
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
