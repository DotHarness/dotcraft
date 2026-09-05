# Remote Tool Host

Remote Tool Host lets an Agent on one device run eligible file, Shell, and LSP tools in a workspace on another device. The Agent device still owns the model, conversation, approvals, and tool history; the Tool Host owns the real workspace and executes the tools there.

![The Agent Runtime keeps the model loop and tool identities while Remote Tool Host executes eligible Core file, Shell, and LSP tools beside the target workspace](/remote-tool-host-topology.svg)

## When to use it

Use Remote Tool Host when the Agent device should not contain the project checkout or its local toolchain. A common setup is an Agent device for conversation and model access, plus a colleague's workstation that already has the repository, build tools, Shell environment, and language servers.

The workspace machine only dials out. Nobody opens an inbound port, moves a certificate, or edits a configuration file there.

## Before you start

You need:

- DotCraft on both Windows devices, with compatible Remote Tool Host protocol and tool contracts.
- DotCraft Hub running on the Agent device (`dotcraft hub`). It is the meeting point for both machines.
- Network access from the workspace machine to the Agent device on port 47600.
- An existing folder on the workspace machine to share.

Both machines must stay signed in: Remote Tool Host runs as the signed-in user, not as a service.

## Invite the machine that owns the workspace

In Desktop, open **Settings → Connections → Satellites** and choose **Invite**. A dialog asks what you need the machine for and which folder you would like to work in on it — both are optional, and both are shown to the person you invite. Create the link, copy it from the same dialog and send it to them, then choose **Done**. To invite a second machine, choose **Create another** without leaving the dialog. The link works once and expires after 24 hours.

Outside Desktop, run this on the Agent device:

```powershell
dotcraft tool-host invite --name "Ann's workstation"
```

The command prints the same link plus the exact command to run on the other machine.

The first invitation opens the pairing port, so Windows asks once whether to allow DotCraft through the firewall — allow it on your private network.

An invitation names this device by its host name. If the other machine cannot resolve that name, mint it with the address to dial instead: `dotcraft tool-host invite --host 192.168.1.20`.

## Join and stay available

Run this on the machine that owns the workspace, replacing the link and the folder:

```powershell
dotcraft tool-host setup --name "Ann's workstation"
dotcraft tool-host join http://ann-pc:47600/i/inv_x1y2z3 --workspace C:\workspaces\sample-project
dotcraft tool-host serve
```

`join` stores a long-lived credential in the operating-system credential store and prints the workspace id that Agents will use. `serve` keeps the connection open; leave it running, or start it at sign-in:

```powershell
dotcraft tool-host autostart install
```

Restarting the Hub on the Agent device does not need any action here — the connection comes back within seconds.

Use `dotcraft tool-host policy list` to inspect local policy. A Tool Host administrator can change one eligible tool with:

```powershell
dotcraft tool-host policy set Exec needs-approval
```

Policies are enforced on the Tool Host. The Agent cannot weaken a `deny` rule or create a permanent approval.

The colleague can skip these commands entirely: [DotCraft Satellite](./satellite) installs from the invitation link and replaces `join`, `serve`, and `autostart install` with one approval window and a tray icon.

## See the machines you can use

**Settings → Connections → Satellites** lists every machine that has joined, each marked **Ready**, **In use**, or **Offline**. Open one for the folders it shares and what has happened on it recently. Outside Desktop, `dotcraft tool-host list` prints the same machines and their ids.

## Choose where a conversation runs

The **Run on** control in the composer says where this conversation's tools run. It offers **This PC** and one entry per paired machine and folder, marking the folders someone else is using and the machines that are offline. Choose one and the existing file, Shell, and LSP tool names route there — the model does not see duplicate local and remote tools. Desktop remembers the choice for that conversation and puts it back the next time you open it, when the machine is online and the folder is free.

Outside Desktop, ask the Agent to route the conversation:

```text
Call RemoteToolHost.List, then connect this conversation to workspace
sample-project on <machine-id> with RemoteToolHost.Connect.
```

A workspace serves one Agent Host at a time. If the folder is already in use, another Agent Host holds it; disconnect it there, or wait for its lease to expire. There is no queue and no takeover.

To go back to local execution, choose **This PC**, or ask the Agent to call `RemoteToolHost.Disconnect`. A network failure does not silently retry the operation locally.

## End the pairing

Either side can end it, and the stored credential goes with it. In Desktop, open the machine under **Settings → Connections → Satellites** and choose **Remove** from its status menu. Outside Desktop:

```powershell
dotcraft tool-host revoke <machine-id>
```

On the Agent device this removes the machine from the Hub and closes its connections. On the workspace machine it deletes the local pairing and stops `serve`.

## Related docs

- [Plugins and tools](./plugins-tools) — understand the other sources of Agent capabilities
- [Security & Sandbox](../self-hosted/security) — review workspace boundaries, approvals, and execution policy
