# Hub local coordination

This page targets integrators and contributors; most users never touch Hub directly. Hub is DotCraft's local runtime coordinator. It runs per OS user and discovers, starts, reuses, and stops the AppServer process for each workspace. Desktop and CLI use Hub by default.

> [!NOTE]
> Remote, CI, bots, or explicit AppServer protocol debugging go through [AppServer Mode](./appserver).

## Key properties

- One Hub per OS user
- One AppServer per workspace
- Hub does not handle conversation traffic or proxy the AppServer protocol
- A client only asks Hub during bootstrap: "make sure this workspace's AppServer is available"
- After bootstrap, the client connects **directly** to the returned AppServer WebSocket URL

![DotCraft Hub local coordination topology](/hub-coordination-topology.svg)

## When to start manually

You usually do not need to run `dotcraft hub` manually. For coordination debugging:

```bash
dotcraft hub
```

Hub starts a loopback management API and writes discovery metadata to `~/.craft/hub/hub.lock`. It allocates local ports automatically; if startup fails because a port is busy, a permission is denied, or security software blocks loopback, restart Hub or Desktop to reallocate.

## Local state

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

When Hub or AppServer finds a stale `appserver.lock` left by a dead process, it removes the lock and continues. If the lock points to a still-running AppServer with a healthy WebSocket endpoint, Hub reuses that endpoint instead of starting a duplicate process. When the lock points to a live AppServer that Hub cannot safely reuse, close the Desktop or CLI process holding that workspace, or stop the workspace runtime from the tray, then reopen it.

## Desktop and the tray

Desktop is the visual layer; Hub itself is a headless background coordinator. Desktop can:

- Open or switch workspaces
- See recent and running workspaces
- Open Desktop or Dashboard
- Restart or stop Hub-managed workspace runtimes
- Receive system notifications forwarded through Hub (task completion, approvals, runtime state)

When the tray exits, Desktop can ask Hub to stop the workspace AppServers Hub manages.

For Desktop to open a workspace, `dotcraft` / `dotcraft.exe` must be on `PATH`, or the AppServer executable path must be set in Desktop settings.

## Building a client

Use a [DotCraft SDK](../sdks/) for normal local clients. Its Hub API discovers or starts Hub, ensures the workspace AppServer, preserves structured errors, and then opens the AppServer connection. Implement [Hub Protocol](../protocols/hub-protocol) directly only for a custom transport, an unsupported language, or protocol debugging.

## Related docs

- [SDK quickstart](../sdks/quickstart) — the recommended client path
- [AppServer mode](./appserver) — remote / multi-client / CI
- [Hub Protocol](../protocols/hub-protocol) — client protocol overview
- [Unified Session Core](../architecture/session-core) — where Hub and AppServer sit in the bigger picture
