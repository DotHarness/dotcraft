# Plugins & Tools

Tools are what the agent can actually do — read a file, run a command, search the web. DotCraft draws them from three places: **built-in tools** (file, shell, web, search, plan, todo — shipped with DotCraft), **plugins** (extra tools and skills you or others package up), and **MCP servers**. You decide which ones are on.

![DotCraft tool surface topology](/tool-surface-topology.svg)

## Built-in Tools

DotCraft ships a default toolbelt that covers most coding agent needs:

| Category | Representative tools | Controlled by |
|---|---|---|
| File | `ReadFile` / `WriteFile` / `EditFile` / `GrepFiles` / `FindFiles` | Workspace boundary and security policy |
| Shell | `Exec` | Approval policy, timeout, and sandbox |
| Web | `WebSearch` / `WebFetch` | Network and fetch limits |
| LSP | Built-in LSP tools | Optional LSP tool settings |
| Images | Prompt-based image creation and reference image editing | Supported model services and image generation settings |
| Plan / Todo | `CreatePlan` / `UpdateTodos` / `TodoWrite` | Subagent role policies |

`ReadFile` reads text files and returns supported images as vision input. PDF and other binary files are rejected with a guidance message instead of being decoded as text.

When the current model service supports image creation, the agent can generate images from your description or refine images using references you provide. Results appear directly in the conversation, so you can review them, download them, or use them as references in the next step.

Tool switches, allow-lists, web limits, LSP settings, and image generation settings are listed in the [Configuration Reference](../../developing/configuration#tools-security-and-sandbox).

## Install & Use Plugins

A DotCraft plugin packages reusable workspace capabilities into an installable extension. A plugin can ship:

| Content | Description |
|---|---|
| Dynamic tool | Agent-callable tool, optionally executed by a local stdio process |
| Skill | Plugin-contained skill that joins the skill list when the plugin is enabled |
| Desktop extension | Trusted local UI bundle that contributes Desktop surfaces such as a sidebar main view |
| Lifecycle hook | Script hook that runs at lifecycle moments after you review and trust it |
| Metadata | Name, description, developer, category, icon, default prompt, related links |

Plugin-bundled skills follow plugin lifecycle: available when the plugin is enabled, hidden when disabled or removed.
Desktop extensions follow the same lifecycle: Desktop loads their local bundles only after the plugin is installed and enabled.
Plugin-bundled hooks are discovered when the plugin is enabled, then managed from **Settings -> Hooks**. They do not run until you trust them. See [Lifecycle Hooks](./hooks).

### Desktop Extensions

Some plugins ship a **Desktop extension** — a panel that opens right inside Desktop instead of only adding tools. The Oratorio plugin, for example, embeds its board as a full view you can open from the sidebar.

![Oratorio Desktop extension](https://github.com/DotHarness/resources/raw/master/dotcraft/whats-new/desktop-extensions.gif)

For what an extension can do and how to build one, see [Desktop Extensions](../../developing/integrations/desktop-extensions).

### Install in Desktop

![Plugin page](https://github.com/DotHarness/resources/raw/master/dotcraft/plugins.png)

1. Open DotCraft
2. Go to **Plugins**
3. Search or browse, open a plugin's detail page
4. Click **Install**
5. Click **Try in chat** when ready, or describe your task in any new conversation

### Enable / Disable / Remove

| Action | Meaning |
|---|---|
| Install | Add the plugin to the current workspace's available capabilities |
| Enable / Disable | Keep plugin files, only control whether they enter the agent context |
| Remove | Remove the plugin from the workspace; for plugins under `.craft/plugins/<plugin-id>` this deletes the directory |

The Plugins management page supports bulk enable/disable.

### Install a Local Plugin

When developing or testing a plugin, two options:

```text
.craft/plugins/<plugin-id>/.craft-plugin/plugin.json
```

Copy the plugin root into the workspace `.craft/plugins/<plugin-id>/`, open the Plugins page, and click **Refresh**. This install can be removed via the Desktop detail page, which deletes the directory.

Alternatively, point DotCraft at a directory you maintain externally. Desktop never deletes external plugin roots; remove the entry from config or manage the filesystem yourself. Full fields are in the [Configuration Reference](../../developing/configuration#plugins-mcp-and-lsp).

### Verify a Plugin

1. Click **Refresh** on the Plugins page
2. Search the plugin name or id
3. Open the detail page and confirm tools, skills, and links
4. If it does not appear, read the diagnostics shown on the page (usually the manifest path and error reason)

## Build a Plugin

The fastest path is the built-in `$plugin-creator` skill: let it scaffold first, then refine documentation, tool logic, and verification steps.

If your goal is just adding one workflow to a single project, prefer creating a plain skill. Use a plugin only when you want to distribute skills, dynamic tools, hooks, icon, and install-page metadata together.

### Bootstrap with plugin creator

Describe what you want in a chat:

```text
$plugin-creator Create a plugin named External Process Echo with one skill and one external-process tool.
```

Or specify runtime, language, and validation:

```text
$plugin-creator Create a local plugin that exposes an EchoText dynamic tool via a Python process, and produce install validation steps.
```

`plugin-creator` generates the plugin directory and manifest, a plugin-contained skill, optional MCP config, optional [hooks](./hooks), and an optional Desktop extension. After generation, usually three things remain:

1. Replace the placeholder text and sample copy
2. Implement or adjust the tool process logic
3. Install the plugin locally and verify via Plugins **Refresh**

### What a plugin can contribute

A single plugin can ship any mix of these content types, all driven by one manifest:

- **Dynamic tools** the agent can call, optionally backed by a local process.
- **Skills** that join the skill list whenever the plugin is enabled.
- **Desktop extensions** — local UI bundles that add their own surfaces to Desktop, such as a sidebar view, and can read (and, when authorized, write) their app's data through a sandboxed host bridge.
- **Lifecycle hooks** that run scripts at session, prompt, tool, or turn moments after the user trusts the plugin's current hooks.

Let `plugin-creator` scaffold the manifest and the matching files; the generated manifest is your working reference when you tune or distribute the plugin. For the wire-level contract behind dynamic tools and Desktop extensions — manifest fields, tool schemas, the host bridge, and write authorization — see [Build an App](../../developing/integrations/build-an-app) and [App Binding](../../developing/integrations/app-binding). For hook behavior and trust, see [Lifecycle Hooks](./hooks).

## MCP Servers

Beyond built-in tools and plugin dynamic tools, DotCraft also speaks MCP. In Desktop, open **Settings -> MCP Servers** to add servers for the current workspace without editing JSON.

- **STDIO** starts a local command and exposes the MCP tools it provides.
- **Streamable HTTP** connects to an HTTP MCP endpoint, usually ending in `/mcp`.
- **Secrets** should come from environment variables. Use bearer-token env vars or environment-backed headers for tokens instead of pasting secret values into literal headers.
- **Test connection** checks reachability and shows how many tools DotCraft discovered before you rely on the server in a session.

MCP server registration, deferred loading options, and the complete field list live in the [Configuration Reference](../../developing/configuration#plugins-mcp-and-lsp).

## Safety & Trust

Installing a plugin adds new tools and skills to the workspace's capability surface. Plugins with a `process` backend may launch a local stdio process declared in the manifest to execute dynamic tools. **Only install and enable plugins whose source, code, and dependencies you trust**.

- Plugin tool calls still pass through DotCraft's session, approvals, and tool-call records.
- Plugin hooks do not run until the plugin's current hooks are trusted in **Settings -> Hooks**; changed hook commands must be trusted again.
- Desktop extension bundles run inside the Desktop renderer as trusted local UI code. Descriptor-bound host capabilities are still enforced by Desktop's main process; extension v1 is not an untrusted code sandbox.
- Plugin detail pages link to website, privacy policy, and ToS for source verification.
- Blacklists, workspace boundary, sandbox, and other restrictions also apply to plugin tools. See [Security & Sandbox](../self-hosted/security).

## Related docs

- [Skills & Self-Learning](./skills) — relationship between skills and plugins
- [Lifecycle Hooks](./hooks) — plugin-bundled scripts that run at lifecycle moments
- [Desktop Extensions](../../developing/integrations/desktop-extensions) — plugins that embed their own view inside Desktop
- [Build an App](../../developing/integrations/build-an-app) — manifest fields, tool schemas, and Desktop extension authoring
- [Security & Sandbox](../self-hosted/security) — global constraints on tool capabilities
