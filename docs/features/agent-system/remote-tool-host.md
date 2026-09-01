# Remote Tool Host

Remote Tool Host lets an Agent on one device run eligible file, Shell, and LSP tools in a workspace on another device. The Agent device still owns the model, conversation, approvals, and tool history; the Tool Host owns the real workspace and executes the tools there.

![The Agent Runtime keeps the model loop and tool identities while Remote Tool Host executes eligible Core file, Shell, and LSP tools beside the target workspace](/remote-tool-host-topology.svg)

## When to use it

Use Remote Tool Host when the Agent device should not contain the project checkout or its local toolchain. A common setup is an Agent device for conversation and model access, plus a developer workstation that already has the repository, build tools, Shell environment, and language servers.

Remote Tool Host changes where eligible built-in tools execute without moving the Agent session or changing ordinary .NET method calls.

## Before you start

You need:

- DotCraft on both Windows devices, with compatible Remote Tool Host protocol and tool contracts.
- An HTTPS endpoint on the Tool Host that the Agent device can reach, including its explicit port.
- An existing absolute workspace directory on the Tool Host.
- A secure way to transfer one pairing file from the Tool Host to the Agent device.

The v1 autostart command runs for the current Windows user. The user must be signed in for the Tool Host to stay available.

## Configure the Tool Host device

Run setup once on the device that owns the workspace. Replace the endpoint and path with values for that device:

```powershell
dotcraft tool-host setup https://tool-host.example:7443 --output .\tool-host.pairing.json
dotcraft tool-host workspace add sample-project C:\workspaces\sample-project
dotcraft tool-host status
```

The endpoint is required because it determines the address advertised to Agent devices and the identity in the generated TLS certificate. The workspace id, `sample-project` here, is the stable name Agents use; it is not inferred from the directory name.

To start the Host automatically at the next sign-in:

```powershell
dotcraft tool-host autostart install
```

For an immediate test, keep this command running in a terminal:

```powershell
dotcraft tool-host serve
```

Use `dotcraft tool-host policy list` to inspect local policy. A Tool Host administrator can change one eligible tool with:

```powershell
dotcraft tool-host policy set Exec needs-approval
```

Policies are enforced on the Tool Host. The Agent cannot weaken a `deny` rule or create a permanent approval.

## Pair the Agent device

Transfer `tool-host.pairing.json` through a secure channel, then register it on the device that runs the Agent:

```powershell
dotcraft tool-host register .\tool-host.pairing.json
dotcraft tool-host list
dotcraft tool-host test <host-id>
```

Setup prints the host id, and `tool-host list` shows it again. The pairing file contains a bearer token. Delete every transferred copy after registration; the Agent stores the token in the operating-system credential store.

If no output path is supplied to `setup` or `token rotate`, DotCraft writes a pairing file named from the generated host id in the current directory. Rotating the token immediately revokes all previous registrations, so distribute and register the new pairing file before relying on the Host again.

## Connect a conversation

Ask the Agent to list registered Hosts and connect the current conversation to the workspace:

```text
Call RemoteToolHost.List, then connect this conversation to workspace
sample-project on <host-id> with RemoteToolHost.Connect.
```

After `Connect` succeeds, the existing file, Shell, and LSP tool names route to that workspace. The model does not see duplicate local and remote tools. The route applies only to the current conversation and is not restored after DotCraft restarts.

Verify the target reported by `Connect`, then ask the Agent to read a known file or run a harmless workspace command. To return to local execution, ask it to call `RemoteToolHost.Disconnect`. A network failure does not silently retry the operation locally.

## Troubleshooting

### Host is offline

On the Tool Host, run `dotcraft tool-host status`, then start `dotcraft tool-host serve`. Confirm that the configured HTTPS hostname and port are reachable through the local firewall. If you installed autostart, confirm the configured user is signed in.

### Workspace is busy

Another Agent Host currently holds that workspace. Disconnect it there, or wait for its lost lease to expire. Remote Tool Host does not queue or take over a workspace owned by another Agent Host.

### Certificate mismatch

Do not bypass the warning. Compare the fingerprint shown by `dotcraft tool-host status` on the Tool Host with the expected pairing information. If the Host was intentionally set up again, unregister the old host id and register a newly transferred pairing file.

## Related docs

- [Plugins and tools](./plugins-tools) — understand the other sources of Agent capabilities
- [Security & Sandbox](../self-hosted/security) — review workspace boundaries, approvals, and execution policy
