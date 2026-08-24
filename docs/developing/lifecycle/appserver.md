# AppServer mode

This page targets integrators and contributors who manage AppServer directly. AppServer is an optional protocol and transport boundary over the host-owned Session Core. It projects the host's single `ISessionService` to external clients through JSON-RPC rather than creating a second session kernel. Desktop, ACP, `dotcraft exec`, external channel adapters, and custom integrations can all connect to the same AppServer.

Use cases:

- Custom IDE / editor integrations
- Remote development (clients connecting to a remote AppServer)
- Multiple clients sharing the same workspace
- Building non-C# clients (any language with WebSocket / stdio support)

> [!NOTE]
> Day-to-day Desktop and `dotcraft exec` go through [Hub local coordination](./hub). This page is for manual AppServer management.

This page covers AppServer process startup, transport modes, configuration, lifecycle, and security. See [DotCraft SDKs](../sdks/) for client library APIs and [AppServer Protocol](../protocols/appserver-protocol) for wire messages.

## Starting AppServer

```bash
# stdio (default, for subprocess communication)
dotcraft app-server

# Pure WebSocket (for remote, multi-client)
dotcraft app-server --listen ws://127.0.0.1:9100

# stdio + WebSocket dual mode
dotcraft app-server --listen ws+stdio://127.0.0.1:9100
```

The server listens on the bare `ws://host:port` (or `wss://host:port`) address; clients append the `/ws` path to connect, for example `ws://host:port/ws`. The examples below follow this rule.

## Connecting from the command line

```bash
# One-shot task
dotcraft exec --remote ws://127.0.0.1:9100/ws "Summarize this workspace"

# With token auth
dotcraft exec --remote ws://server:9100/ws --token my-secret "Summarize this workspace"
```

## Command-line reference

### Subcommands and global options

| Command / Option | Description |
|---|---|
| `dotcraft exec <prompt>` | Run a one-off command-line Agent task |
| `dotcraft exec -` | Read input from stdin and run a one-off task |
| `dotcraft app-server` | Start AppServer (defaults to stdio) |
| `--listen <URL>` | AppServer transport, used with `app-server` |
| `--remote <URL>` | Client connection to a remote AppServer, used with `exec` or ACP |
| `--token <VALUE>` | WebSocket auth token, used with `--listen` or `--remote` |

### `--listen` URL schemes

| Scheme | Transport | stdout | Example |
|---|---|---|---|
| `stdio://` | Pure stdio (default) | Reserved for JSON-RPC | `--listen stdio://` |
| `ws://host:port` | Pure WebSocket | Normal console output | `--listen ws://127.0.0.1:9100` |
| `wss://host:port` | Pure WebSocket (TLS) | Normal console output | `--listen wss://0.0.0.0:9100` |
| `ws+stdio://host:port` | stdio + WebSocket | Reserved for JSON-RPC | `--listen ws+stdio://127.0.0.1:9100` |

## Transport modes

### stdio (default)

AppServer communicates over stdin/stdout using newline-delimited JSON (JSONL). This is the local subprocess communication method commonly used by ACP and custom clients.

```
Client (stdin) → JSON-RPC Request → AppServer
AppServer → JSON-RPC Response/Notification → Client (stdout)
AppServer → Diagnostic logs → stderr
```

**Properties**:

- 1:1 communication (one client per server process)
- stdout is reserved for the wire protocol; console logs go to stderr
- No network configuration; ideal for local development

### WebSocket

AppServer starts a WebSocket listener on the given address. Each text frame carries a complete JSON-RPC message.

```bash
dotcraft app-server --listen ws://127.0.0.1:9100
```

**Properties**:

- Multiple concurrent client connections (each with independent init state and thread subscriptions)
- stdout is free; console output works normally
- Supports remote connections and network authentication

### stdio + WebSocket dual mode

```bash
dotcraft app-server --listen ws+stdio://127.0.0.1:9100
```

For deployments that need both subprocess and remote connections.

## Security authentication

When AppServer listens on a non-loopback address (not `127.0.0.1` / `::1`), **strongly** set up token authentication.

### Server

```bash
dotcraft app-server --listen ws://0.0.0.0:9100 --token my-secret
```

### Client

```bash
dotcraft exec --remote ws://server:9100/ws --token my-secret "Check status"
```

The token is passed via the WebSocket query: `ws://host:port/ws?token=<value>`. Once the server sets `--token`, every client — Desktop, ACP, `dotcraft exec`, and custom clients — must send the same token; an empty token is rejected.

> [!CAUTION]
> Binding to `0.0.0.0` without a token leaves AppServer fully open.

## Configuration

### Command line (recommended)

CLI arguments override config-file values:

```bash
dotcraft app-server --listen ws://127.0.0.1:9100 --token my-secret
```

### config.json (alternative)

Suitable for fixed deployments. `ExternalChannels` tells DotCraft how to launch an external adapter; structured delivery capabilities and `channelTools` are not in config — adapters declare them dynamically during `initialize`.

**AppServer config**

| Field | Description | Default |
|---|---|---|
| `AppServer.Mode` | Transport: `Disabled` / `Stdio` / `WebSocket` / `StdioAndWebSocket` | `Disabled` |
| `AppServer.WebSocket.Host` | WebSocket bind address | `127.0.0.1` |
| `AppServer.WebSocket.Port` | WebSocket bind port | `9100` |
| `AppServer.WebSocket.Token` | WebSocket auth token | empty |

**CLI client config**

| Field | Description | Default |
|---|---|---|
| `CLI.AppServerUrl` | Remote AppServer URL used by `dotcraft exec` | empty |
| `CLI.AppServerToken` | Remote auth token used by `dotcraft exec` | empty |
| `CLI.AppServerBin` | Custom executable used when `dotcraft exec` starts a local Hub/AppServer | empty (current process) |

**Examples**

```json
{
    "AppServer": {
        "Mode": "WebSocket",
        "WebSocket": {
            "Host": "0.0.0.0",
            "Port": 9100,
            "Token": "my-secret"
        }
    }
}
```

```json
{
    "CLI": {
        "AppServerUrl": "ws://server:9100/ws",
        "AppServerToken": "my-secret"
    }
}
```

## How it works

![DotCraft AppServer mode topology](/appserver-mode-topology.svg)

| Scenario | Approach |
|---|---|
| Run one task from a script | `dotcraft exec "..."` |
| Share one backend across Desktop / ACP / custom clients | `dotcraft app-server --listen ws://127.0.0.1:9100` |
| Connect to a remote workspace | Listen with WebSocket; clients connect to `/ws` |
| Build a custom raw client | Speak JSON-RPC 2.0 over stdio or WebSocket |

## Related docs

- [Architecture overview](../architecture/overview) — assembly ownership and dependency boundaries
- [SDK quickstart](../sdks/quickstart) — the recommended client path
- [Configuration reference](../configuration) — `AppServer.*` / `CLI.*` fields
- [AppServer Protocol](../protocols/appserver-protocol) — raw client protocol
- [Hub local coordination](./hub) — the path Desktop and CLI take by default
- [Unified Session Core](../architecture/session-core) — Thread / Turn / Item model
