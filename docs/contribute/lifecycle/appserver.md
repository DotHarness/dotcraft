# AppServer mode

This page targets integrators and contributors who manage AppServer directly; day-to-day Desktop and `dotcraft exec` go through [Hub local coordination](./hub). AppServer is an optional protocol and transport boundary over the host-owned [Session Core](../architecture/session-core): it projects the host's single `ISessionService` to external clients through JSON-RPC rather than creating a second session kernel. One AppServer process owns one Session Core, the stdio and WebSocket transports can be open at once, and Desktop, ACP, `dotcraft exec`, external channel adapters, and custom integrations all share the same session state when connected.

See [DotCraft SDKs](../../extend/sdks/) for client library APIs and [AppServer Protocol](../../extend/protocols/appserver) for wire messages.

![DotCraft AppServer mode topology: one host process serves stdio and WebSocket at the same time, and external clients share a single Session Core](/appserver-mode-topology.svg)

## Starting AppServer

```bash
# stdio (default, for subprocess communication)
dotcraft app-server

# Pure WebSocket (for remote, multi-client)
dotcraft app-server --listen ws://127.0.0.1:9100

# stdio + WebSocket dual mode
dotcraft app-server --listen ws+stdio://127.0.0.1:9100
```

The server listens on the bare `ws://host:port` address, and clients append the `/ws` path to connect, for example `ws://host:port/ws`. The examples below follow this rule.

The built-in listener does not terminate TLS, and `--listen wss://…` is rejected. For TLS, put a reverse proxy in front of AppServer to terminate it, and point clients at `wss://host/ws`.

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
| `dotcraft exec -` | Read the prompt from stdin and run a one-off task |
| `dotcraft exec resume <thread-id> <prompt>` | Add a task to an existing thread |
| `dotcraft exec resume --last <prompt>` | Add a task to the workspace's most recently active thread |
| `dotcraft exec interrupt <thread-id>` | Stop the running task of a thread |
| `dotcraft exec interrupt --last` | Stop the running task of the workspace's most recently active thread |
| `dotcraft app-server` | Start AppServer (defaults to stdio) |
| `--listen <URL>` | AppServer transport, used with `app-server` |
| `--remote <URL>` | Client connection to a remote AppServer, used with `exec` or ACP |
| `--token <VALUE>` | WebSocket auth token, used with `--listen` or `--remote` |

### `exec` options

| Option | Description |
|---|---|
| `-C, --cd <dir>` | Workspace directory to run in, which must contain `.craft`. Defaults to the current directory |
| `-m, --model <id>` | Model on the workspace's default provider. With `resume`, it also applies to the thread's later tasks |
| `-i, --image <file>` | Attach local images. Repeat the option or pass a comma-separated list. Relative paths resolve against the current directory, not `--cd` |
| `--auto-approve` | Approve every operation on the thread that needs approval. With `resume`, it also applies to the thread's later tasks |
| `--ephemeral` | Create a thread that is not persisted. `exec` only, not `resume` |
| `--json` | Write the event stream to stdout as JSON Lines instead of the final answer |
| `-o, --output-last-message <file>` | Also write the final answer to this file |

`exec interrupt` accepts `-C`, `--remote`, and `--token`.

When the task completes, its final answer goes to stdout. When it fails or is cancelled, stdout stays empty and the reason goes to stderr. Progress goes to stderr, including the `[CLI] Thread: <id>` line and one `[CLI] Generated image: <path>` line for each image the task saved. Image data is never printed. When you omit the prompt, exec reads it from stdin. When you pass a prompt and also pipe stdin, the piped text is appended to the prompt in a `<stdin>` block:

```bash
dotcraft exec "Fix the failing test"
dotcraft exec resume --last -m <model> "Now add a regression test"
dotcraft exec resume <thread-id> -i before.png,after.png "Compare these"
git diff | dotcraft exec "Review this change"
```

Exit codes: `0` when the task completed, `1` when it failed or exec could not run it, and `2` for invalid usage, before any thread is created.

`-o` writes the final answer of a completed task as UTF-8 without a byte-order mark, replacing the file. A completed task without a final answer writes an empty file and prints a warning. A failed or cancelled task leaves the file untouched.

Exec never prompts for approval. Operations that need approval are declined, the task continues, and the exit code follows the task's outcome. With `--auto-approve`, the thread's approval policy becomes `autoApprove` and those operations run, except commands that the safety rules forbid.

Exec waits for the task with no time limit of its own. Ctrl+C asks the AppServer to stop the task, waits until it stops, and exits `1`. A task whose exec was killed keeps running on the AppServer. `dotcraft exec interrupt` stops it and exits `0` whether it stopped a task or found none running, or `1` when the thread does not exist or the AppServer cannot be reached. A Hub that exec started keeps running after exec exits, even when exec's whole process tree is killed.

### `--json` events

With `--json`, stdout carries one JSON object per line, with `snake_case` fields:

| Event | Fields |
|---|---|
| `thread.started` | `thread_id` |
| `turn.started` | none |
| `item.started`, `item.updated`, `item.completed` | `item` |
| `turn.completed` | `usage`: `input_tokens`, `cached_input_tokens`, `cache_write_input_tokens`, `output_tokens`, `reasoning_output_tokens` |
| `turn.failed` | `error.message` |
| `error` | `message`, for failures outside a task |

A cancelled task ends without a `turn.*` event. Each `item` has an `id`, a `type`, and fields for that type:

| `type` | Fields |
|---|---|
| `agent_message` | `text` |
| `reasoning` | `text` |
| `command_execution` | `command`, `aggregated_output`, `exit_code`, `status` (`in_progress`, `completed`, `failed`) |
| `file_change` | `changes` (`path`, `kind`: `add`, `delete`, `update`), `status` |
| `mcp_tool_call` | `server`, `tool`, `arguments`, `result`, `error`, `status` |
| `web_search` | `query` |
| `todo_list` | `items` (`text`, `completed`). Updated with `item.updated` as the plan changes, completed when the task ends |
| `image_generation` | `status`, `saved_path`, `revised_prompt`, `error`. Never includes image data |

```bash
dotcraft exec --json "Fix the failing test" | jq -c 'select(.type == "item.completed") | .item.type'
dotcraft exec -o answer.md --auto-approve "Update the changelog"
dotcraft exec interrupt --last
```

### `--listen` URL schemes

| Scheme | Transport | stdout | Example |
|---|---|---|---|
| `stdio://` | Pure stdio (default) | Reserved for JSON-RPC | `--listen stdio://` |
| `ws://host:port` | Pure WebSocket | Normal console output | `--listen ws://127.0.0.1:9100` |
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

A token is required when AppServer listens on a non-loopback address (not `127.0.0.1` / `::1`). Without one, AppServer refuses to start rather than leaving an unauthenticated open port.

### Server

```bash
dotcraft app-server --listen ws://0.0.0.0:9100 --token my-secret
```

### Client

```bash
dotcraft exec --remote ws://server:9100/ws --token my-secret "Check status"
```

The token is passed via the WebSocket query: `ws://host:port/ws?token=<value>`. Once the server sets `--token`, every client — Desktop, ACP, `dotcraft exec`, and custom clients — must send the same token, and a missing or mismatched token is rejected with HTTP `401` before the WebSocket handshake completes. Token values must be URL-safe (alphanumeric plus `-`, `_`, `.`); otherwise the client percent-encodes them.

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

## Common scenarios

| Scenario | Approach |
|---|---|
| Run one task from a script | `dotcraft exec "..."` |
| Share one backend across Desktop / ACP / custom clients | `dotcraft app-server --listen ws://127.0.0.1:9100` |
| Connect to a remote workspace | Listen with WebSocket; clients connect to `/ws` |
| Build a custom raw client | Speak JSON-RPC 2.0 over stdio or WebSocket |

## Related docs

- [SDK quickstart](../../extend/sdks/quickstart) — the recommended client path, without implementing the protocol yourself
- [Entry points and services](../../configuration/services) — full descriptions of the `AppServer.*` / `CLI.*` fields
- [Architecture overview](../architecture/) — where AppServer sits among assembly ownership and dependency boundaries
