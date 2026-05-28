# DotCraft TUI

**[中文](./README_ZH.md) | English**

Rust-native terminal interface for DotCraft, built on [Ratatui](https://ratatui.rs/). Connects to the DotCraft AppServer over the Wire Protocol (JSON-RPC) and provides a full-featured AI Agent interaction experience in the terminal.

## Features

| Feature | Description |
|---------|-------------|
| **Streaming output** | Agent messages render incrementally with Markdown support (syntax-highlighted code blocks, tables, headings) |
| **Tool call display** | `• Called ReadFile("src/main.rs") (0.3s)` format with elapsed time and result preview |
| **StatusIndicator** | Shows `⠋ Working (Ns · esc to interrupt)` as the single busy spinner during active turns/system work |
| **Top status card** | Compact startup/default card with model, workspace, thread, and connection state |
| **Content-flow composer** | Input follows the latest transcript content for short chats and settles near the bottom for long chats |
| **FooterLine** | On-demand composer hint row for slash navigation, draft/send hints, connection errors, and active-turn tokens |
| **Inline SubAgent progress** | Live SubAgent status rendered inline; collapses to a summary when all complete |
| **Inline Plan view** | Agent todo list rendered inline in the chat flow |
| **Session management** | `/sessions` opens the session picker (resume / archive / delete) |
| **Approval flow** | `ApprovalOverlay` for tool calls that require human approval |
| **i18n** | Built-in Chinese / English (`--lang zh` / `--lang en`) |
| **Theme customization** | TOML-based color overrides |
| **Clipboard** | `y` key copies the last agent message (requires `clipboard` feature) |
| **WebSocket mode** | Connect to a remote AppServer (requires `websocket` feature) |

## Building

**Prerequisites**: Rust stable toolchain — install via [rustup](https://rustup.rs/).

```bash
# Enter the tui directory
cd tui

# Standard build (includes WebSocket support)
cargo build --release

# Without WebSocket (does not support local Hub or remote mode)
cargo build --release --no-default-features

# With system clipboard support
cargo build --release --features clipboard
```

Output binary: `target/release/dotcraft-tui` (Windows: `dotcraft-tui.exe`).

## Launching

### Mode 1: Hub-managed local mode (default)

The TUI starts or discovers DotCraft Hub, asks Hub to ensure the workspace AppServer, then connects to the returned AppServer WebSocket endpoint. `--server-bin` points to the `dotcraft` binary used to start Hub. If omitted, TUI first looks for `dotcraft` next to `dotcraft-tui`, then falls back to `dotcraft` on PATH.

The terminal UI appears immediately with the top status card and composer visible. You can edit a draft while Hub/AppServer connection is still in progress; pressing `Enter` before connection succeeds keeps the draft intact and shows the connection status near the composer.

```bash
# Launch in the current project directory
dotcraft-tui

# Specify workspace path
dotcraft-tui --workspace /path/to/project

# Specify the dotcraft binary used to start Hub
dotcraft-tui --server-bin /usr/local/bin/dotcraft

# Via environment variable
DOTCRAFT_BIN=/usr/local/bin/dotcraft dotcraft-tui
```

### Mode 2: Remote WebSocket mode

Connect to a running AppServer (requires `websocket` feature).

```bash
# Connect to a local AppServer
dotcraft-tui --remote ws://localhost:3000/ws

# Connect to a remote AppServer with authentication
dotcraft-tui --remote "ws://host:3000/ws?token=your-secret"

# With explicit workspace path
dotcraft-tui --remote ws://host:3000/ws --workspace /path/to/project
```

Starting the AppServer for remote mode:

```bash
# Start AppServer in WebSocket mode
dotcraft app-server --listen ws://0.0.0.0:3000
```

### Language and Theme

```bash
# Chinese UI (default)
dotcraft-tui --lang zh

# English UI
dotcraft-tui --lang en

# Custom theme
dotcraft-tui --theme /path/to/theme.toml
```

### CLI Reference

| Flag | Description | Default |
|------|-------------|---------|
| `--remote <URL>` | Connect to a remote AppServer (WebSocket URL) | — |
| `--server-bin <PATH>` | `dotcraft` binary used to start Hub | sibling `dotcraft`, then PATH |
| `--workspace <PATH>` | Workspace directory path | current directory |
| `--theme <PATH>` | Custom theme TOML file path | built-in dark theme |
| `--lang <LANG>` | UI language (`zh` / `en`) | `zh` |

## Key Bindings

| Key | Action |
|-----|--------|
| `Enter` | Send message when connected; while connecting, keep the draft intact |
| `Shift+Enter` | Insert newline in input |
| `Tab` | While running: queue message; idle: slash or `$skill` completion |
| `Ctrl+C` | While running: interrupt agent; idle: first press flags quit, second press exits |
| `Shift+Tab` | Toggle Agent / Plan mode |
| `Esc` | Running: interrupt; otherwise enter transcript browse mode |
| `↑` / `↓` | Input mode: history (empty draft); browse mode: scroll chat by line |
| `PageUp` / `PageDown` | Enter/continue transcript browse by page (no `Esc` required first) |
| `Home` / `End` | Enter/continue transcript browse and jump top / bottom |
| `Mouse wheel` | Scroll transcript history directly (input or browse mode) |
| `i` (in browse mode) | Return to input editor |
| `↑` / `↓` in popup | Navigate slash command or `$skill` suggestions |
| `Ctrl+A` / `Ctrl+E` | Move to line start / end in input editor |
| `y` | Copy last agent message to clipboard (requires `clipboard` feature) |
| `s` | When SubAgents are done: toggle detail / collapsed view |
| `Ctrl+L` | Force terminal redraw |

## Slash Commands

| Command | Description |
|---------|-------------|
| `/sessions` | Open session manager |
| `/new` | Start a new session |
| `/clear` | Clear current conversation history |
| `/load <thread-id>` | Load a specific session by ID |
| `/agent` | Switch to Agent mode |
| `/plan` | Switch to Plan mode |
| `/model [name\|default]` | Open the model picker or set the model |
| `/skills` | Enable, disable, or inspect skills |
| `/permissions` | Choose the approval/permission preset for the current or next thread |
| `/cron` | List cron jobs |
| `/quit` | Exit the TUI |

## Skill Mentions

Type `$` in the composer to open a skill picker below the input. `↑/↓` navigates, `Tab` or `Enter` inserts the selected skill, and `Esc` closes the picker. Submitted drafts send recognized enabled skills as native `skillRef` input parts while preserving the visible `$skill` text in conversation history.

## Theme Configuration

In the TOML file passed to `--theme` (colors accept Ratatui color names or `#RRGGBB`):

```toml
[colors]
brand = "#7C3AED"           # brand color (logo, mode indicator)
user_message = "white"
agent_message = "white"
reasoning = "cyan"
tool_active = "yellow"
tool_completed = "gray"
error = "red"
success = "green"
dim = "dark_gray"
mode_agent = "green"
mode_plan = "blue"
status_indicator = "yellow" # "Working" label and spinner color

[footer]
foreground = "dark_gray"    # on-demand composer hint text
context_color = "dark_gray" # active-turn context such as token counts

[code]
syntect_theme = "base16-ocean.dark"  # code block syntax highlight theme
```

## Logging

Set `DOTCRAFT_TUI_LOG` to enable log output (logs are written to stderr):

```bash
DOTCRAFT_TUI_LOG=debug dotcraft-tui 2>tui.log
```
