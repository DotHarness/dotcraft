# Remote tool calls

Remote tool calls let an Agent use file, Shell, LSP, and opted-in .NET plugin tools on another machine. Remote Tool Host is the execution component on that machine. This page covers command-line setup and integration outside DotCraft Desktop.

![The Agent machine routes tool calls through the Hub to the execution host on the shared machine](/remote-tool-host-topology.svg)


## Responsibility split

The Agent machine owns the model loop, approvals, hooks, and the Session history. The workspace machine owns the real workspace, the local tool policy, and the execution audit. A remotely executed tool keeps its existing tool identity, schema, and Session projection — remote execution replaces only the runtime route behind a stable registration, so the model never sees a second, remote copy of a tool.

The Hub on the Agent machine is the rendezvous point. The execution host connects to it and never listens for inbound connections, so the workspace machine needs no inbound firewall rule, port forward, or TLS identity. The Hub relays the bytes between the two sides without interpreting them.

Both machines run as the signed-in user, not as a service, so both must stay signed in.

## Pair from the command line

The Agent machine runs DotCraft Hub (`dotcraft hub`), and the workspace machine must be able to reach it on port 47600. Run this on the Agent machine to mint an invitation:

```powershell
dotcraft tool-host invite --name "Ann's workstation"
```

The command prints the invitation link plus the exact command to run on the other machine. An invitation names this device by its host name; if the other machine cannot resolve it, mint the invitation with the address to dial instead:

```powershell
dotcraft tool-host invite --host 192.168.1.20 --expires 4
```

`--expires` sets the validity in hours. An invitation is single-use.

Run this on the machine that owns the workspace, replacing the link and the folder:

```powershell
dotcraft tool-host setup --name "Ann's workstation"
dotcraft tool-host join http://ann-pc:47600/i/inv_x1y2z3 --workspace C:\workspaces\sample-project
dotcraft tool-host serve
```

`join` stores a long-lived credential in the operating-system credential store and prints the workspace id that Agents will use. `serve` keeps the control connection open and reconnects on its own after a Hub restart. To start it at sign-in:

```powershell
dotcraft tool-host autostart install
```

[DotCraft Satellite](../../features/agent-system/satellite) is the tray client that replaces `setup`, `join`, `serve`, and `autostart install` with an installer and an on-screen approval, for a machine owner who does not want a terminal.

## Inspect and route

On the Agent machine, `dotcraft tool-host list` prints the machines paired with this Hub and their ids, and `dotcraft tool-host test <machine-id>` checks whether one of them is online. On the workspace machine, `dotcraft tool-host workspace list` prints the folders it exports, and the policy commands show and change what it will run:

```powershell
dotcraft tool-host policy list
dotcraft tool-host policy set Exec needs-approval
```

A policy is `allow`, `deny`, or `needs-approval`, set per canonical tool name.

Policies are enforced on the execution host. An Agent cannot weaken a `deny` rule or create a permanent approval on the remote machine.

Outside Desktop, an Agent routes a conversation with the `RemoteToolHost.List`, `RemoteToolHost.Connect`, and `RemoteToolHost.Disconnect` model tools:

```text
Call RemoteToolHost.List, then connect this conversation to workspace
sample-project on <machine-id> with RemoteToolHost.Connect.
```

A workspace serves one Agent Host at a time. If it is already held, disconnect it there or wait for it to be released — there is no queue and no takeover. A remote failure is reported as a remote failure; DotCraft never silently retries the call against the local binding.

## Local access and file transfer

Tools that support remote calls accept `target: "local"` or `target: "remote"`. Omit it to follow the conversation's connection. For example, `ReadFile({ "path": "scripts/check.py", "target": "local" })` reads the Agent's workspace without disconnecting. Use the same target for `WriteStdin` as for the `Exec` that started its terminal.

`RemoteToolHost.Transfer` copies files or directories directly between machines:

```json
{
  "direction": "upload",
  "localPath": "tools/checker",
  "remotePath": "tools/checker",
  "overwrite": false
}
```

Use `download` to copy from the remote machine. Paths name exact destinations, relative to each machine's workspace or absolute. Directories merge, preserving extra files; replacing existing files requires `overwrite: true`. The result reports completed files and bytes if a transfer stops partway through. Incomplete files are discarded. Plan mode does not allow explicit transfers.

Transfers use the existing connection and enforce both machines' file policies. They reject filesystem links, check SHA-256, and commit each file atomically. The execution host's `Tools.File.MaxTransferBytes` defaults to 10 GiB, independently of the text read limit. A lost commit response is reported as an unknown outcome and is not automatically retried.

## Skill and plugin resources

.NET plugin tools that support remote calls use the Agent's accepted plugin bundles and effective settings. Connect prepares the complete bundles and their .NET dependencies on the remote workspace automatically, including deferred tools. No separate plugin installation is needed there. Changes are prepared before the next Turn uses its tool snapshot. MCP and runtime dynamic tools remain local.

Under workspace-preferred authorization, the remote owner approves the exact plugin fingerprints before any plugin code loads. Full access permits automatic activation. Disconnect releases the thread's plugin state, and the last workspace lease drains the plugin runtime. Verified bundle files remain available for reuse.

Skills stay on the Agent machine. `SkillView` reads local instructions, and the Skill catalog gives the effective local path, including variants. Use `ReadFile` with `target: "local"` for supporting files while connected.

The Agent uses `Transfer` to copy scripts, directories, or CLI files needed remotely, preserving their relative dependencies and using the effective Skill variant where applicable. Transferred files remain after disconnect, with destinations and overwrites chosen explicitly. Executables must support the remote OS.

Connect negotiates tools and file transfer support before publishing the route. If connection setup fails, the previous route remains. Upgrade Satellites that lack file transfer support before connecting.

## End a pairing

```powershell
dotcraft tool-host revoke <machine-id>
```

On the Agent machine this removes the peer from the Hub and closes its connections. On the workspace machine it deletes the local pairing and stops `serve`. Either side is enough — the stored credential goes with it.

## Related docs

- [DotCraft Satellite](../../features/agent-system/satellite) — the same pairing driven from Desktop
- [Architecture overview](./overview) — where the Hub and the Agent Host sit in the wider runtime
