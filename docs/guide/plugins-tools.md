# Plugins and tools

Use plugins and MCP servers to add tools, connect accounts, and share workflows with your agent.

![Built-in tools, plugins, and MCP servers reach the agent as one list of capabilities, and plugins and MCP servers are reviewed and trusted first](/capability-sources-overview.svg)

## Where capabilities come from

| Source | What it adds |
|---|---|
| **Built-in tools** | File editing, shell, web, search, planning, and other core actions |
| **Plugins** | Packaged skills, tools, workflows, apps, panels, and [lifecycle hooks](./hooks) |
| **MCP servers** | Tools provided by a local process or a remote service |

## Install a plugin

![Browsing a plugin, opening its details, and trying it in a DotCraft conversation](https://github.com/DotHarness/resources/raw/master/dotcraft/docs/plugin-registry.webp)

1. Open **Plugins** in DotCraft Desktop.
2. Search or browse the catalog.
3. Open a plugin and check its publisher, the capabilities it lists, and its links.
4. Select **Install**.
5. Review the confirmation, then select **Add to DotCraft**.
6. Complete any app setup shown in the installation dialog.
7. Select **Try in chat**, or start a chat and describe what you want done.

To install plugins from another catalog, see [Plugin marketplaces](./plugin-marketplaces). To connect a platform such as Telegram or WeChat, use the [Channels tab](./channels/#connect-a-channel).

## Manage installed plugins

Plugins you install are available to your workspaces on that host. Turn a plugin off in **Manage** for the current workspace, or select **Uninstall** to remove the shared installation.

Plugins in a project's `.craft/plugins` folder come with that project and appear under **From** followed by the project name.

Manage plugin app accounts in [App Settings](./connected-apps). Use the chat's app picker to choose which apps it can use.

## Remote plugin interfaces

Plugins install on the remote host. Authorize the workspace
to run its plugin interfaces on this computer. That permission covers future installs and
updates in the workspace. Use the Plugins page to stop or retry an interface.

## Install from disk

1. Open **Plugins** in a local workspace.
2. Open the menu beside **Create**, then select **Install from disk**.
3. Choose the plugin folder.
4. Review the plugin, then verify it with **Try in chat**.

## Create a plugin

Start with the built-in `$plugin-creator` skill:

```text
$plugin-creator Create a plugin that packages my project review workflow.
```

The skill creates the plugin and guides you through local testing.

For executable modules, start with [Build a Desktop extension](../extend/desktop-extensions) or [Build a .NET extension](../extend/dotnet-extensions). For packaging and distribution, see the [Plugin Market guide](../extend/plugin-market).

## Connect an MCP server

Open **Settings → MCP Servers**, then add one of these connections:

- **STDIO** for a server started by a local command.
- **Streamable HTTP** for a remote MCP endpoint.

Pass tokens and other secrets through environment variables. Select **Test connection** before relying on the server in a chat. The complete field list is in the [MCP server configuration](../configuration/plugins#mcp-servers).

## Review trust before installing

Check the publisher, source, and account permissions before installing a plugin or connecting a server.

Desktop extensions run on this computer. .NET extensions require authorization and run with the
DotCraft host's permissions. Review and trust plugin hooks in **Settings → Hooks** before running them.

## Related docs

- [Plugin marketplaces](./plugin-marketplaces) — add catalogs you trust and install plugins from them
- [Connected Apps](./connected-apps) — connect a plugin's app to your account
- [Security](../self-hosting/security) — bound tool calls with the workspace boundary, approvals, and command rules
