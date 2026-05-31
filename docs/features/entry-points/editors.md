# IDE / Editors (ACP)

[Agent Client Protocol (ACP)](https://agentclientprotocol.com/) is an open protocol that standardizes communication between coding agents and editors/IDEs — much like LSP standardizes language servers, but for AI agents. Any ACP-compatible editor can connect to any ACP-compatible agent. DotCraft natively speaks ACP, so it runs as a first-class coding assistant inside the editor with no cloud subscription, no proprietary plugin, and no vendor-specific config.

From the editor's view, communication is **stdio plus JSON-RPC 2.0**: the editor launches DotCraft as a subprocess and exchanges messages over standard streams. Internally, the DotCraft ACP process is a **protocol bridge** that connects the editor (ACP) to an AppServer instance (Wire Protocol). All session state, agent execution, and tool calls are handled by AppServer and shared with TUI, Desktop, and external channels. The bridge auto-launches a local AppServer subprocess or connects to a remote AppServer you specify.

## Supported Editors

ACP is an open standard with a growing ecosystem. DotCraft runs in:

| Editor | Plugin / integration |
|---|---|
| **JetBrains Rider** (and other JetBrains IDEs) | Built-in AI Assistant agent support |
| **Obsidian** | [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client) |
| **Unity Editor** | [dotcraft-unity](https://github.com/DotHarness/dotcraft-unity) |

Any other ACP-capable editor can connect with the same configuration shape.

## Quick Start

### 1. Initialize a DotCraft Workspace

Before wiring up the editor, run a one-time non-interactive setup in your project directory:

```bash
cd <your project directory>
dotcraft setup
```

Follow the prompts for provider / model / api-key. See [Configuration Reference](../../developing/configuration.md) for full fields, or run `dotcraft setup --help` for supported options.

Once setup completes, the workspace is ready for ACP, TUI, Desktop, or automation entries.

### 2. Configure ACP in the Editor

In the editor's agent configuration:

- **Command**: `dotcraft`
- **Arguments**: `-acp`
- **Working directory**: the project root from step 1

DotCraft activates ACP mode automatically when launched with `-acp` — no config-file changes required.

### 3. Remote Workspace (optional)

If a DotCraft AppServer is already running (via `dotcraft app-server` or the desktop app), point the ACP bridge at it instead of starting a new subprocess:

```text
dotcraft -acp --remote ws://<host>:<port>/ws
```

Append `--token <token>` if the AppServer requires authentication. With a remote AppServer, sessions created in the editor are visible in real time to every connected client.

---

## JetBrains Rider (and other JetBrains IDEs)

JetBrains IDEs with the AI Assistant plugin can register an ACP agent directly. Open **AI Chat → Add Custom Agents** and create:

```json
{
    "agent_servers": {
        "DotCraft": {
            "command": "dotcraft",
            "args": ["-acp"]
        }
    }
}
```

Save and pick DotCraft in the AI chat panel's agent selector. The IDE owns process lifecycle: DotCraft starts when you open a session and exits when you close it.

## Obsidian

Install [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client) (via BRAT or manually), then add a Custom agent in plugin settings:

| Field | Value |
|---|---|
| **AgentID** | DotCraft |
| **Display name** | DotCraft |
| **Path** | `dotcraft.exe` |
| **Arguments** | `-acp` |

Once configured, DotCraft appears in the plugin's chat UI. It can answer questions and read/write notes directly — one agent, both coding assistant and knowledge-base assistant.

## Unity Editor

The Unity editor client lives in a separate repository: [DotHarness/dotcraft-unity](https://github.com/DotHarness/dotcraft-unity).

DotCraft itself is the agent harness Unity launches via ACP. Install and configure DotCraft from this repo first, then add `dotcraft-unity` to your Unity project:

```text
https://github.com/DotHarness/dotcraft-unity.git
```

Unity scene, selected-object, console, and project-info tools are declared by `dotcraft-unity` as runtime dynamic tools during ACP initialize. `_unity/*` are Unity client-internal ACP extension methods, implemented and maintained by the client plugin.

## How It Works

When the editor launches DotCraft in ACP mode:

1. **Initialize** — The editor and ACP bridge exchange protocol versions and capabilities (`initialize`). The bridge connects to AppServer (or auto-launches a local subprocess if `--remote` is not set) and forwards the handshake over Wire Protocol. Clients with DotCraft extensions may declare client-implemented runtime tools in `_meta.dotcraft.runtimeTools`.
2. **Create session** — The editor sends `session/new`. The bridge forwards it; AppServer creates the session and relays the response (slash commands, config options, etc.) back to the editor UI. Client-declared runtime tools are bound to the connection as AppServer `dynamicTools`.
3. **Prompt interaction** — The editor sends `session/prompt`. AppServer runs the agent and streams back visible replies, thinking, tool-call state, and tool results. The bridge forwards them via `session/update` notifications as `agent_message_chunk` for visible content and `agent_thought_chunk` for thinking.
4. **Config switching** — DotCraft exposes mode and model selectors through ACP `configOptions`. Capable clients call `session/set_config_option` to switch models; the bridge updates the active thread and workspace default in lockstep.
5. **Permission requests** — Before file writes or shell commands, AppServer raises an approval over Wire Protocol. The bridge translates it to an ACP `requestPermission`, and the editor renders an approve/deny prompt.
6. **File and terminal access** — When AppServer wants editor-native file or terminal access, the request flows back to the editor (`fs/readTextFile`, `fs/writeTextFile`, `terminal/*`) and uses the editor's own APIs.
7. **Client runtime tools** — Clients like Unity can declare scene queries, selected-object reads, console logs, etc. DotCraft only validates and bridges the tool descriptors; implementations stay in the client plugin.

The result: DotCraft can read unsaved buffer content, render inline diffs before applying changes, and run commands inside the editor-managed terminal — capabilities a plain CLI agent cannot offer. Meanwhile, all agent state lives in AppServer, sessions persist, and other clients can pick up the same thread even after the editor closes.

## Supported Protocol Features

| Feature | Description |
|---|---|
| `initialize` | Protocol version negotiation and capability exchange |
| `session/new` | Create a new session |
| `session/load` | Load an existing session and replay history |
| `session/list` | List all ACP sessions |
| `session/prompt` | Send a prompt and stream the reply |
| `session/update` | DotCraft pushes visible chunks, thought chunks, and tool-call state |
| `session/set_config_option` | Switch session config such as mode and model |
| `session/cancel` | Cancel an in-progress operation |
| `requestPermission` | DotCraft asks for permission on sensitive operations |
| `fs/readTextFile` | Read files (including unsaved buffers) through the editor |
| `fs/writeTextFile` | Write files (with diff preview) through the editor |
| `terminal/*` | Create and manage terminals through the editor |
| Runtime Dynamic Tools | Clients declare thread-scoped tools via `_meta.dotcraft.runtimeTools`; the bridge calls back via AppServer `item/tool/call` |
| Slash Commands | Custom commands in `.craft/commands/` are broadcast to the editor's command picker |
| Config Options | Optional configs (mode, model) exposed in the editor UI; model selector uses ACP `category: "model"` |

## Sessions & Workspace Behavior

ACP works as a full AppServer client. Sessions created in the editor land in the same session store:

- **Session ID format**: `acp_{sessionId}` (id allocated by the editor and forwarded to AppServer)
- **Session storage**: `<workspace>/.craft/sessions/` next to TUI, Desktop, and bot sessions
- **Shared memory**: `memory/MEMORY.md` and `memory/HISTORY.md` are shared across every channel in the same workspace — knowledge captured in an ACP session is available in TUI, Desktop, or QQ bot sessions, and vice versa
- **Multi-client concurrency**: with `--remote`, multiple clients connect to the same AppServer. An ACP session opened in Obsidian is visible / continuable in the desktop app in real time

## Usage Examples

| Scenario | Approach |
|---|---|
| Local IDE | Configure the editor to launch `dotcraft -acp` |
| Remote workspace | Start AppServer WebSocket first, then add `--remote` to the ACP arguments |
| Share sessions with Desktop | Point at the same workspace / AppServer |
| Let the editor own file and terminal access | Use an ACP client that supports `fs/*` and `terminal/*` |

## Troubleshooting

### DotCraft does not show up in the editor

Confirm the command path resolves to `dotcraft`, the argument is `-acp`, and the editor plugin supports Agent Client Protocol.

### Cannot read unsaved files

Only file access routed through the editor's ACP client sees unsaved buffers. Other entries usually read from disk.

### Remote mode fails to connect

Confirm AppServer is started in WebSocket mode, the URL contains `/ws`, and the token matches.

## Related

- [Desktop](./desktop.md) — GUI client over the same backend
- [AppServer Mode](../../developing/appserver.md) — remote or multi-client
- [Unified Session Core](../session-core.md) — Thread / Turn / Item model and ACP bridging
