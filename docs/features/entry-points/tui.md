# TUI

The TUI is DotCraft's native terminal interface, built in Rust. If you live in the terminal — or work over SSH — it gives you the full DotCraft experience right there: the same workspace, sessions, and approvals you'd get in Desktop, without leaving the command line.

## Build

```bash
cd tui
cargo build --release
```

Outputs land in `target/release/`. On Windows the binary is `dotcraft-tui.exe`.

## How to Start

### Hub-Managed Local Mode (default)

From a project directory:

```bash
dotcraft-tui
```

TUI starts or discovers DotCraft Hub, asks Hub to ensure the workspace AppServer, then connects to the returned WebSocket endpoint.

### Specify Workspace or Binary

```bash
dotcraft-tui --workspace /path/to/project
dotcraft-tui --server-bin /usr/local/bin/dotcraft
DOTCRAFT_BIN=/usr/local/bin/dotcraft dotcraft-tui
```

### Remote Mode

```bash
dotcraft app-server --listen ws://127.0.0.1:9100
dotcraft-tui --remote ws://127.0.0.1:9100/ws
```

With authentication:

```bash
dotcraft-tui --remote ws://server:9100/ws --token my-secret
```

## Command-Line Arguments

| Argument | Description |
|---|---|
| `--workspace` | Workspace directory |
| `--server-bin` | `dotcraft` binary used to start Hub |
| `--remote` | Connect to an existing WebSocket AppServer |
| `--token` | Remote AppServer auth token |
| `--theme` | Custom TOML theme |

## Common Slash Commands

`/new`, `/model`, `/provider`, `/clear`, `/quit`.

- `/provider` lists configured personal providers
- `/provider <id>` selects the workspace provider
- Provider creation, editing, testing, and deletion live in Desktop or any AppServer-capable client

For full key bindings and theme details, see [`tui/README.md`](https://github.com/DotHarness/dotcraft/blob/master/tui/README.md).

## Advanced

- Default mode is Hub-managed local mode; remote mode targets explicitly hosted AppServers.
- Enable debug logs: `DOTCRAFT_TUI_LOG=debug dotcraft-tui 2>tui.log`
- Enable system clipboard: `cargo build --release --features clipboard`

## Related docs

- [Desktop](./desktop)
- [AppServer Mode](../../developing/lifecycle/appserver)
- [Settings Lifecycle](../../developing/lifecycle/settings-lifecycle)
