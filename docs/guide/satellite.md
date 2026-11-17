# DotCraft Satellite

Satellite lets your DotCraft work on another Windows PC. You keep chatting in Desktop, while the person sharing the PC decides what the agent may do, sees what it is doing, and can pause at any time. Enable the bundled Satellite plugin in Desktop to manage PCs, choose where tools run, and view their screens.

![The inviting PC runs Desktop and the Hub, and Satellite on the shared PC connects to it](/satellite-overview.svg)

## Person inviting: create an invitation

Open **Settings → Satellites** and choose **Invite**. Copy the link and send it to the other person.

If Windows asks whether to allow DotCraft through the firewall on your first invitation, allow it on a private network you trust. The shared PC must be able to reach the address in the invitation link.

## Person sharing: install and open Satellite

Open the invitation link on the PC you want to share, check who is asking, and choose **Download DotCraft Satellite**. When the installation finishes, **Share this PC** opens. If Satellite is already installed, use the invitation page's option to open it.

## Person sharing: choose an access mode

### Full access

Choose **Allow connection**. The agent can then work with files and run commands with your Windows permissions without asking each time. Choose this only when you trust the other person to operate this PC.

![Full access selected on the left, with Folder only offered beside it](https://github.com/DotHarness/resources/raw/master/dotcraft/satellite/authorize-full.png)

### Folder only

Choose **Folder only** and pick the folder to share, or cancel to use the suggested task folder. The agent can read and change files in that folder. Anything else, including local commands, waits for your approval. This is not a strict sandbox.

![Folder only selected, showing the folder it shares and the change-folder button](https://github.com/DotHarness/resources/raw/master/dotcraft/satellite/authorize-workspace.png)

Choose **Decline** to refuse the invitation.

## Person inviting: choose where to run

After the other person allows the connection, open **Run on** below the chat composer, select the remote PC and its workspace, and submit your task. File, Shell, and language tools then run on that PC. Select **This PC** to switch back.

The agent can still reach files on your own PC, and copy files or folders between the two PCs when needed. For example: “Upload my local checker CLI to the remote workspace and run it there.”

## Person inviting: watch the remote screen

While the chat runs on the remote PC, choose **Watch … screen** in the chat header to open a live view of that PC's desktop. Drag and resize it as you like, and point at it for **Expand** and **Stop watching**. The view is read-only.

## Person sharing: see who is using your PC

While your PC is paired, a bar at the top of the screen shows who is connected and what the agent is doing. While a tool runs, the edges of the screen light up. Click the bar to see each connected PC, disconnect one, or open its task folder. Use the pause button on the bar to stop every PC at once, and **Resume sharing** to let them back in.

While the other person watches your screen, the bar shows an eye.

## Person sharing: handle additional access

In Folder only mode, requests for additional access appear on the bar. Check who is asking and the exact path or command, then choose **Allow once** or **Decline**. Commands may affect files outside the task folder. A request nobody answers within two minutes is declined.

![The bar asking whether Ann may run a command, with Decline and Allow once](https://github.com/DotHarness/resources/raw/master/dotcraft/satellite/approve-command.png)

## Person sharing: review, pause, or revoke access

Right-click the Satellite tray icon and choose a PC to change its access mode, open its task folder, disconnect it, or revoke its access. **Pause sharing** in the same menu stops every PC until you resume.

If Desktop is also installed on this PC, **Settings → Satellites → Share this PC** shows who has access and how.

![Share this PC showing two paired computers with their access modes and task folders](https://github.com/DotHarness/resources/raw/master/dotcraft/satellite/manage-access.png)

The person inviting can also remove the PC from **Settings → Satellites**. Sharing again requires a new invitation.
