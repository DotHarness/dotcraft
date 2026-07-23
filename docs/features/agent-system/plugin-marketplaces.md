# Plugin marketplaces

Plugin marketplaces add catalogs from sources you choose, so you can browse and install their plugins from the Plugins page.

![A marketplace makes plugins available to install in each workspace](/plugin-marketplace-flow.svg)

A marketplace is added once to DotCraft. Plugins from that marketplace are installed separately in each workspace, so every project keeps only the capabilities it needs.

## Add a marketplace

1. Open **Plugins** in DotCraft Desktop.
2. Open the menu beside **Create**, then select **Add marketplace**.
3. Enter the marketplace source.
4. Select **Add marketplace**.

DotCraft loads the catalog and adds its plugins to the Plugins page.

### Choose a source

| Source | Example | When to use it |
|---|---|---|
| **GitHub repository** | `owner/repo` | The shortest way to add a repository hosted on GitHub |
| **Git URL** | `https://host/team/plugins.git` | Public, private, or self-hosted Git repositories |
| **Local folder** | Select **Browse** | A marketplace you are developing or maintaining on this computer |

**Browse** is available only when Desktop is using a local workspace.

For a Git source, leave **Git ref** empty to follow its default branch. Set a branch, tag, or commit only when you need a fixed version.

Use **Sparse paths** when the repository owner tells you to download only specific folders. Enter one repository path per line. Most marketplaces do not require this setting.

## Install a plugin

1. Open **Plugins**.
2. Search for a plugin, or choose **Marketplaces** from the publisher filter.
3. Open the plugin.
4. Select **Install**.
5. Review the confirmation, then select **Add to DotCraft**.
6. Complete any app setup shown in the installation dialog.
7. Select **Try in chat**, or start a conversation and describe what you want to do.

Installing a plugin adds it to the current workspace. Use **Manage** to enable or disable installed plugins without uninstalling them.

## Refresh a marketplace

Refresh a marketplace when its publisher adds or updates plugins:

1. Choose **Marketplaces** from the publisher filter.
2. Find the marketplace heading.
3. Open **Marketplace actions**.
4. Select **Refresh**.

Refreshing updates the marketplace catalog.

## Remove a marketplace

Open **Marketplace actions** beside the marketplace, then select **Remove**.

Removing a marketplace hides its catalog from DotCraft. Plugins already installed in your workspaces stay installed until you uninstall them.

> [!CAUTION]
> Add marketplaces only from sources you trust. Review a plugin's publisher, permissions, and links before installing it.

## Related docs

- [Plugins & Tools](./plugins-tools)
- [Connected Apps](./connected-apps)
- [Security & Sandbox](../self-hosted/security)
- [Plugin Market](../../developing/integrations/plugin-market)
