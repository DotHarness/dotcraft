# IDE / Editors (ACP)

DotCraft can run right inside your editor as a coding assistant — JetBrains, Obsidian, Unity, and more — with no cloud subscription, no proprietary plugin, and no vendor lock-in. It does this by speaking [Agent Client Protocol (ACP)](https://agentclientprotocol.com/), an open standard for connecting coding agents to editors (think LSP, but for AI agents). Any ACP-compatible editor can talk to any ACP-compatible agent, and DotCraft speaks ACP natively.

The editor launches DotCraft and talks to it; DotCraft bridges that conversation to its [AppServer](../../developing/lifecycle/appserver), which runs the agent. So an ACP session shares the same workspace, sessions, memory, and tools as Desktop and channels — the editor is just another window onto the same agent. By default DotCraft starts a local AppServer for you; point it at a remote one when you need to.

## Supported Editors

ACP is an open standard with a growing ecosystem. DotCraft runs in:

| Editor | Plugin / integration |
|---|---|
| **JetBrains IDEs** | Built-in AI Assistant agent support |
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

Follow the prompts for provider / model / api-key. See [Configuration Reference](../../developing/configuration) for full fields, or run `dotcraft setup --help` for supported options.

Once setup completes, the workspace is ready for ACP, Desktop, or automation entries.

### 2. Configure ACP in the Editor

In the editor's agent configuration:

- **Command**: `dotcraft`
- **Arguments**: `-acp`
- **Working directory**: the project root from step 1

DotCraft activates ACP mode automatically when launched with `-acp` — no config-file changes required.

### 3. Remote Workspace (optional)

If a DotCraft AppServer is already running (via `dotcraft app-server` or the desktop app), point the ACP bridge at it instead of starting a new one:

```text
dotcraft -acp --remote ws://<host>:<port>/ws
```

The AppServer listens on a bare `ws://host:port` address; clients always append the `/ws` path, as shown above. Add `--token <token>` if the AppServer requires authentication. With a remote AppServer, sessions you create in the editor are visible in real time to every connected client.

---

## JetBrains IDEs

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

Once connected, the agent can query the Unity scene, the selected object, the console, and project info — those tools are provided and maintained by the `dotcraft-unity` plugin.

## What you get in the editor

Running inside the editor gives DotCraft abilities a plain CLI agent cannot offer:

- **Read unsaved buffers** — the agent sees your current edits, not just what's on disk.
- **Inline diffs before applying** — review and approve each change in the editor's own diff view.
- **Editor-managed terminal** — commands run in a terminal the editor owns, with its working directory and environment.
- **Native approvals** — before a file write or shell command, the editor shows an approve/deny prompt.
- **Slash commands and model switching** — your `.craft/commands/` show up in the editor's command picker, and you can switch model right from the editor.

Because the agent runs in AppServer, your work outlives the editor: sessions persist, and another client can pick up the same thread after you close the editor.

For the full list of ACP methods DotCraft implements and how the bridge maps them to AppServer, see the [AppServer Protocol](../../developing/protocols/appserver-protocol).

## Sessions shared across clients

An ACP session is a full workspace session — it lives in the same store as your Desktop and bot sessions, and they all share the same long-term memory. Knowledge captured in an ACP session is available in a Desktop or QQ bot session in the same workspace, and vice versa.

With `--remote`, several clients connect to one AppServer at once. A session you open in Obsidian is visible and continuable in the desktop app in real time. See [Unified Session Core](../../developing/architecture/session-core) for the model behind this.

## Usage Examples

| Scenario | Approach |
|---|---|
| Local IDE | Configure the editor to launch `dotcraft -acp` |
| Remote workspace | Start AppServer WebSocket first, then add `--remote` to the ACP arguments |
| Share sessions with Desktop | Point at the same workspace / AppServer |
| Let the editor own file and terminal access | Use an ACP client that handles file and terminal requests natively |

## Related docs

- [Desktop](./desktop) — GUI client over the same backend
- [AppServer Mode](../../developing/lifecycle/appserver) — remote or multi-client
- [Unified Session Core](../../developing/architecture/session-core) — Thread / Turn / Item model and ACP bridging
