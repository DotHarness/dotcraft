# Plugins and tools

Plugins and tools let DotCraft work with files, run commands, connect to services, and follow reusable workflows.

## Where capabilities come from

| Source | What it adds |
|---|---|
| **Built-in tools** | File editing, shell commands, web access, search, planning, and other core actions |
| **Plugins** | Packaged skills, tools, apps, panels, and lifecycle hooks |
| **MCP servers** | Tools provided by a local process or remote service |

DotCraft applies workspace boundaries, approvals, and security settings when the agent uses these capabilities.

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

For marketplace packaging and distribution, see [Plugin Market](../../developing/integrations/plugin-market).

## Connect an MCP server

Open **Settings → MCP Servers**, then add one of these connections:

- **STDIO** for a server started by a local command.
- **Streamable HTTP** for a remote MCP endpoint.

Use environment variables for tokens and other secrets. Select **Test connection** before relying on the server in a conversation.

See [Configuration](../../developing/configuration#plugins-mcp-and-lsp) for the complete MCP field reference.

## Review trust before installation

Only install plugins and connect servers that you trust. Review the publisher, requested capabilities, source links, and account permissions first.

Plugins may start local processes, connect to remote services, add hooks, or load a Desktop panel. Calls still follow DotCraft approvals and workspace security settings. Plugin hooks remain inactive until you review and trust them in **Settings → Hooks**.

## Related docs

- [Plugin marketplaces](./plugin-marketplaces)
- [Connected Apps](./connected-apps)
- [Lifecycle Hooks](./hooks)
- [Security & Sandbox](../self-hosted/security)
- [DotCraft App](../../developing/integrations/app-binding)
