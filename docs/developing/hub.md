# Hub Local Coordination

Hub is DotCraft's local runtime coordinator. It runs per OS user and discovers, starts, reuses, and stops the AppServer process for each workspace. Desktop / TUI use Hub by default — most users **do not interact with Hub directly**.

> [!NOTE]
> Remote, CI, bots, or explicit AppServer protocol debugging go through [AppServer Mode](./appserver.md).

## Key Properties

- One Hub per OS user
- One AppServer per workspace
- Hub does not handle conversation traffic or proxy the AppServer protocol
- A client only asks Hub during bootstrap: "make sure this workspace's AppServer is available"
- After bootstrap, the client connects **directly** to the returned AppServer WebSocket URL

![DotCraft Hub local coordination topology](/hub-coordination-topology.svg)

## When to Start Manually

You usually do not need to run `dotcraft hub` manually. For coordination debugging:

```bash
dotcraft hub
```

Hub starts a loopback management API and writes discovery metadata to `~/.craft/hub/hub.lock`.

## Local State

```text
~/.craft/hub/
├── hub.lock          # current Hub discovery: API URL, PID, start time, local token, binary path
└── appservers.json   # Hub-tracked AppServer state (display & recovery)
```

Each workspace also has:

```text
<workspace>/.craft/appserver.lock
```

It records which AppServer process owns the workspace and prevents multiple local AppServers from running against the same workspace.

When Hub or AppServer finds a stale `appserver.lock` left by a dead process, it removes the lock and continues. If the lock points to a still-running AppServer with a healthy WebSocket endpoint, Hub reuses that endpoint instead of starting a duplicate process.

## Desktop and the Tray

Desktop is the visual layer; Hub itself is a headless background coordinator. Desktop can:

- Open or switch workspaces
- See recent and running workspaces
- Open Desktop or Dashboard
- Restart or stop Hub-managed workspace runtimes
- Receive system notifications forwarded through Hub (task completion, approvals, runtime state)

When the tray exits, Desktop can ask Hub to stop the workspace AppServers Hub manages.

## Troubleshooting

### Desktop cannot open a workspace

Make sure `dotcraft` / `dotcraft.exe` is on `PATH`, or configure the AppServer executable path in Desktop settings.

### The workspace is locked

`<workspace>/.craft/appserver.lock` points to another live AppServer that Hub could not safely reuse. Close the Desktop / TUI / CLI using that workspace, or stop the workspace runtime from the tray, then try again. Locks left by dead processes are recovered automatically.

### Local port conflicts

Hub allocates local ports automatically. Failure is usually a busy port, permission issue, or security software blocking loopback. Restarting Hub or Desktop normally reallocates ports.

## Building a Client

Read [Hub Protocol](./hub-protocol.md) to discover Hub, call `ensure`, and subscribe to lifecycle events.

## Related

- [AppServer Mode](./appserver.md) — remote / multi-client / CI
- [Hub Protocol](./hub-protocol.md) — client protocol overview
- [Unified Session Core](../features/session-core.md) — where Hub and AppServer sit in the bigger picture
