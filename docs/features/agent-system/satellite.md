# DotCraft Satellite

Satellite lets your agent use files and run tools on another Windows PC, such as a colleague's workstation with a repository and build environment already installed. The person inviting the PC continues the conversation in Desktop. The person sharing it chooses the access mode in Satellite and handles additional access requests.

![The inviting PC runs Desktop and the Hub; the shared PC runs Satellite, shares one folder, and dials out to reach it](/satellite-overview.svg)

## Person inviting: create an invitation

Open **Settings → Connections → Satellites** and choose **Invite**. Describe what you need the PC for, then create and copy the invitation link and send it to the other person.

If Windows asks whether to allow DotCraft through the firewall on your first invitation, allow it on a private network you trust. The shared PC must be able to reach the address in the invitation link.

## Person sharing: install and open Satellite

Open the invitation link on the PC you want to share, check the inviter and purpose, and choose **Download DotCraft Satellite**. Complete the installation to open **Share this PC**. Satellite installs for your Windows user without requesting administrator access.

If Satellite is already installed, use the invitation page's option to open it.

## Person sharing: choose an access mode

### Workspace preferred (recommended)

Keep **Workspace preferred** selected and choose **Allow connection** to use the suggested task folder. To use an existing project, choose **Change folder…** beneath this option before allowing the connection.

The agent can read and change files in the task folder. Other file access and local commands need your approval.

![Workspace preferred selected, with the task folder and Change folder button directly beneath it](https://github.com/DotHarness/resources/raw/master/dotcraft/satellite/authorize-workspace.png)

### Full access

Choose **Full access** only when you trust the other person to operate this PC. Read the warning, select the confirmation checkbox, and choose **Allow connection**. This mode does not show a task folder picker.

Full access allows file access and commands with your Windows permissions without asking each time. Operating system permissions and explicit host restrictions still apply.

![Full access selected, showing its warning and confirmation checkbox without a task folder picker](https://github.com/DotHarness/resources/raw/master/dotcraft/satellite/authorize-full.png)

Workspace preferred uses approval for additional access; it is not a strict filesystem sandbox. In either mode, **Decline** leaves the PC unpaired and does not create the suggested task folder.

## Person inviting: choose where to run

After the other person allows the connection, find the PC in **Settings → Connections → Satellites**. Open **Run on** below the conversation composer, select the remote PC and its workspace, and submit your task.

File, Shell, and language tools run on the selected PC. If the PC is offline or the workspace is in use, wait until it is available before continuing. A failed remote operation does not automatically run on your own PC. Select **This PC** when you want to switch back.

## Person sharing: handle additional access

In Workspace preferred mode, Satellite opens an approval window for additional access. Check the inviter, operation, and path or full command, then choose **Allow once** or **Decline**. Commands may affect files outside the task folder.

![Approval window showing Ann's request to run dotnet build Demo.sln, with Decline and Allow once buttons](https://github.com/DotHarness/resources/raw/master/dotcraft/satellite/approve-command.png)

If you decline, the agent receives that result. The person inviting your PC cannot approve this access on your behalf.

## Person sharing: review, pause, or revoke access

If Desktop is also installed on this PC, open **Settings → Connections → Share this PC** to see who has access, their access mode, and their task folder. Change permissions in Satellite.

![Share this PC showing two paired computers with their access modes and task folders](https://github.com/DotHarness/resources/raw/master/dotcraft/satellite/manage-access.png)

Right-click the Satellite tray icon, choose **Manage access**, and select a person to change their access mode. Choose **Pause sharing** to stop sharing and resume it later. Choose **Revoke** and select a person to remove their access.

The person inviting can also remove the PC from **Settings → Connections → Satellites**. Sharing again requires a new invitation.
