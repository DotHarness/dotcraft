# Plugins and tools

Plugins and tools let DotCraft work with files, run commands, connect to services, and follow reusable workflows.

![Built-in tools, plugins, and MCP servers reach the agent as one list of capabilities; plugins and MCP servers are reviewed and trusted first](/capability-sources-overview.svg)

## Where capabilities come from

| Source | What it adds |
|---|---|
| **Built-in tools** | File editing, shell commands, web access, search, planning, and other core actions |
| **Plugins** | Packaged skills, tools, workflows, apps, panels, and lifecycle hooks |
| **MCP servers** | Tools provided by a local process or remote service |

Agent tool calls still follow DotCraft workspace boundaries, approvals, and security settings. Executable Desktop and .NET modules have a different trust boundary, described below.

## Install a plugin

1. Open **Plugins** in DotCraft Desktop.
2. Search or browse the catalog.
3. Open a plugin to review its publisher, capabilities, and links.
4. Select **Install**.
5. Review the confirmation, then select **Add to DotCraft**.
6. Complete any app setup shown in the installation dialog.
7. Select **Try in chat**, or start a conversation and describe your task.

To add plugins from another catalog, see [Plugin marketplaces](./plugin-marketplaces).

## Manage installed plugins

Open **Plugins**, then select **Manage**.

- Turn a plugin off to keep it installed without making its capabilities available to the agent.
- Turn it on when you want to use it again.
- Open the plugin and select **Uninstall** to remove it from the current workspace.

If a plugin includes an app, **App Settings** manages its account connection. Choosing an app in a conversation controls whether that conversation can use it. See [Connected Apps](./connected-apps).

## Install from disk

Use this option for a plugin you are developing or received as a folder:

This option is available only for local workspaces.

1. Open **Plugins**.
2. Open the menu beside **Create**, then select **Install from disk**.
3. Choose the plugin folder.
4. Review the plugin, then use **Try in chat** to verify it.

DotCraft copies the plugin into the current workspace. Uninstalling it removes that installed copy.

## Create a plugin

Start with the built-in `$plugin-creator` skill:

```text
$plugin-creator Create a plugin that packages my project review workflow.
```

The skill creates the plugin structure and guides you through testing it. Use a plugin when you want to distribute a reusable capability; use a plain skill for a workflow that only belongs to one project.

For executable modules, start with [Build a Desktop Plugin](../../developing/integrations/desktop-plugins) or [Build a .NET plugin](../../developing/integrations/dotnet-plugins). For packaging and distribution, see [Plugin Market](../../developing/integrations/plugin-market).

## Connect an MCP server

Open **Settings → MCP Servers**, then add one of these connections:

- **STDIO** for a server started by a local command.
- **Streamable HTTP** for a remote MCP endpoint.

Use environment variables for tokens and other secrets. Select **Test connection** before relying on the server in a conversation.

See [Configuration](../../developing/configuration#plugins-mcp-and-lsp) for the complete MCP field reference.

## Review trust before installation

Only install plugins and connect servers that you trust. Review the publisher, described capabilities, source links, and account permissions first. Manifest capability labels describe a plugin; they do not grant or restrict executable permissions.

Agent tool calls continue through tool policy and approvals. Desktop Plugin code runs as trusted code in the renderer when the plugin is enabled. .NET Plugin code runs inside the DotCraft host with its filesystem, network, credential, native interop, and OS authority; it requires an explicit grant for the accepted plugin id and fingerprint and is not contained by the ordinary tool sandbox. Use MCP when executable code needs a process boundary. Plugin hooks remain inactive until you review and trust them in **Settings → Hooks**.

## Related docs

- [Plugin marketplaces](./plugin-marketplaces)
- [Dynamic Workflows](./dynamic-workflows)
- [Connected Apps](./connected-apps)
- [Lifecycle Hooks](./hooks)
- [Security & Sandbox](../self-hosted/security)
- [DotCraft App](../../developing/integrations/app-binding)
