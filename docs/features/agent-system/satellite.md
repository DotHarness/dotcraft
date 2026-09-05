# DotCraft Satellite

Satellite is a small tray application for the PC that owns a project folder. Install it once from an invitation link, approve the folder you are willing to share, and a colleague's DotCraft agent can build, test, and edit in that folder from their own machine. You can see who is connected, what is running, and stop it at any moment.

Satellite is for the person whose PC does the work. The colleague who sends the invitation uses [Remote Tool Host](./remote-tool-host) on their side.

## Install from the invitation link

Your colleague sends you a link that looks like `http://ann-pc:47600/i/inv_x1y2z3`. Open it in a browser on the PC you are sharing. The page names the person inviting you and what they want to do, and offers **Download DotCraft Satellite**.

Run the installer. It installs for your user only and asks for no administrator password.

> [!NOTE]
> Until DotCraft signs its installers, Windows SmartScreen shows a blue "Windows protected your PC" screen. Choose **More info**, then **Run anyway**.

The invitation page keeps trying to hand the link to Satellite, so the approval window appears on its own once the install finishes. If you closed the page, open the link again.

## Approve what you share

The approval window is the only place a connection is created. It shows who is asking, the PC the invitation came from, and why they want access.

Check the folder before you allow anything. Satellite proposes the folder your colleague suggested; use **Change…** to pick a different one. Pick the project folder itself, not a whole drive and not your user folder.

Allowing access lets that person read and change files inside that folder, and run commands on your PC. Access lasts only while you stay signed in.

**Decline** leaves nothing behind. Nothing is stored until you choose **Allow**.

## Read the tray icon

Satellite lives in the notification area with a coloured dot that tells you what is happening:

| Dot | Meaning |
|---|---|
| Grey | Not connected. Nobody can reach this PC right now. |
| Blue | Ready. Connected and waiting. |
| Green | In use. Someone is running something in your folder. |
| Amber | Paused. You stopped sharing; the connection stays open. |

Right-click the icon for the menu. It names who is connected and since when, shows the command currently running, and offers:

- **Disconnect** — end the current connection. They can reconnect.
- **Pause sharing** — refuse new work until you resume. Use it when you need the machine to yourself.
- **Revoke** — remove one person for good. Their access does not come back without a new invitation.
- **Open folder** — open the shared folder in File Explorer.
- **Paste invite link…** — accept an invitation you copied, when the link did not open by itself.
- **Quit** — stop sharing until the next time you sign in.

If DotCraft Desktop is installed on this PC as well, **Settings → Connections → Share this PC** lists the same pairings for reference; you still change them here in the tray app.

## Start with Windows

Satellite starts with Windows automatically and stays in the tray with no window, so your colleague finds the PC available without you doing anything. If you would rather start it yourself, remove `DotCraft Satellite` from **Settings → Apps → Startup**.

## Uninstall

Uninstall Satellite from **Settings → Apps → Installed apps**. It offers to revoke everyone who still has access; accept unless you plan to reinstall right away. Uninstalling stops sharing either way.

## Related docs

- [Remote Tool Host](./remote-tool-host) — the same feature from the inviting side, including the command-line path
- [Security & Sandbox](../self-hosted/security) — what an agent may and may not do inside a folder
