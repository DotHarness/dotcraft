# Plugin Market

This page targets plugin publishers, integrators, and self-hosted operators. It defines the marketplace document, supported sources, and lifecycle exposed by DotCraft.

For instructions on adding and using a marketplace in Desktop, see [Plugin marketplaces](../../features/agent-system/plugin-marketplaces).

## Repository layout

A marketplace repository contains one catalog and one or more plugin directories:

```text
marketplace-root/
├── .craft/
│   └── plugins/
│       └── marketplace.json
└── plugins/
    └── example-plugin/
        └── .craft-plugin/
            └── plugin.json
```

The default catalog path is `.craft/plugins/marketplace.json`. Each listed plugin must resolve to a directory inside the marketplace root.

## Marketplace document

```json
{
  "name": "example-marketplace",
  "interface": {
    "displayName": "Example Plugins"
  },
  "plugins": [
    {
      "name": "example-plugin",
      "source": {
        "source": "local",
        "path": "./plugins/example-plugin"
      },
      "policy": {
        "installation": "AVAILABLE",
        "authentication": "ON_INSTALL"
      }
    }
  ]
}
```

| Field | Type | Required | Default |
|---|---|---|---|
| `name` | string | yes | — |
| `interface` | object | no | `{}` |
| `interface.displayName` | string | no | Marketplace `name` |
| `plugins` | array | yes | — |
| `plugins[].name` | string | yes | — |
| `plugins[].source` | object | yes | — |
| `plugins[].source.source` | `"local"` | yes | — |
| `plugins[].source.path` | string | yes | — |
| `plugins[].policy` | object | yes | — |
| `plugins[].policy.installation` | `"AVAILABLE"` | yes | — |
| `plugins[].policy.authentication` | `"ON_INSTALL"` | yes | — |

### Document rules

- `name` identifies the marketplace and must be a safe directory name.
- `interface.displayName` is the title shown in Desktop.
- Each plugin `name` must match the `id` in its `.craft-plugin/plugin.json`.
- `source.source` must be `local`.
- `source.path` must start with `./`, stay inside the marketplace root, and point to a valid plugin directory.
- `policy.installation` must be `AVAILABLE` for the plugin to appear as installable.
- `policy.authentication` is `ON_INSTALL` in the current publication format. App and account setup behavior is defined by the plugin's app descriptors.
- Plugin capabilities and install-page metadata belong in `.craft-plugin/plugin.json`, not in the marketplace document.

## Marketplace sources

| Source type | Value | Behavior |
|---|---|---|
| **`git`** | Repository shorthand or Git URL | DotCraft checks out and validates the repository |
| **`local`** | Existing directory | DotCraft validates and reads the directory in place |
| **`archive`** | HTTPS archive URL or local archive file | DotCraft uses a cached extracted snapshot |

The Desktop add dialog accepts Git sources. Local sources are available only for local workspaces. Archive sources are available to host-provided or manually configured registries.

### Git source syntax

| Input | Result |
|---|---|
| `owner/repo` | GitHub repository on its default branch |
| `owner/repo@release` | GitHub repository at `release` |
| `https://host/team/repo.git` | HTTPS Git repository |
| `https://host/team/repo.git#release` | HTTPS Git repository at `release` |
| `ssh://git@host/team/repo.git` | SSH Git repository |
| `git@host:team/repo.git` | SCP-style SSH repository |

An explicit `Ref` overrides a reference embedded in the source. `SparsePaths` accepts repository-relative paths for Git sources only; absolute paths and `..` are rejected.

DotCraft rejects `file://` sources and web URLs with embedded credentials. Git operations run non-interactively and use credentials already configured on the host.

## Configuration

Marketplace sources are stored in the global configuration by default. Plugin installation remains workspace-specific.

```json
{
  "Plugins": {
    "PluginRegistries": [
      {
        "Name": "example-marketplace",
        "SourceType": "git",
        "Url": "https://github.com/example/dotcraft-plugins.git",
        "Ref": "main",
        "SparsePaths": [
          ".craft/plugins",
          "plugins"
        ],
        "MarketplacePath": ".craft/plugins/marketplace.json"
      }
    ]
  }
}
```

When a source is added through Desktop or AppServer, `Name`, `LastUpdated`, and `LastRevision` are maintained from the validated source. For manual configuration, `Name` must match the marketplace document. See [Configuration](../configuration#plugins-mcp-and-lsp) for the complete field table and configuration precedence.

## Lifecycle

### Add

DotCraft fetches or opens the source, validates the marketplace document, records the source, and refreshes plugin discovery. Adding a marketplace does not install any plugin.

### Refresh

Refreshing fetches a Git source again, revalidates a local directory, or downloads a new archive snapshot. A failure in one marketplace does not prevent other marketplaces from refreshing.

Discovery reads only sources already available on disk. Listing plugins does not start a Git fetch.

### Install

Installing copies the selected plugin into the current workspace, validates its manifest, and loads its contributions through the normal plugin lifecycle. An uninstalled catalog entry contributes no skills, tools, apps, servers, hooks, or Desktop extensions.

### Remove

Removing a marketplace removes its configured source. Plugins already installed in workspaces remain installed.

## AppServer operations

Clients must negotiate `capabilities.pluginMarketplaces` before calling marketplace methods.

| Method | Purpose |
|---|---|
| `marketplace/add` | Add and validate a Git or local marketplace |
| `marketplace/refresh` | Refresh one marketplace or all configured marketplaces |
| `marketplace/remove` | Remove a configured marketplace source |
| `plugin/list` | Return plugins together with marketplace grouping metadata |

See [AppServer Protocol](../protocols/appserver-protocol#plugin-marketplaces) for request and response payloads.

## Security boundaries

- Adding a marketplace trusts its catalog source; it does not execute plugin code.
- A marketplace path cannot escape its marketplace root.
- DotCraft does not store source credentials.
- A plugin contributes runtime behavior only after it is installed in a workspace.
- Installed tools, apps, hooks, and Desktop extensions keep their existing approval and trust requirements.

## Related docs

- [Official DotCraft plugin marketplace](https://github.com/DotHarness/dotcraft-plugins)
- [Plugin marketplaces](../../features/agent-system/plugin-marketplaces)
- [Configuration](../configuration#plugins-mcp-and-lsp)
- [AppServer Protocol](../protocols/appserver-protocol#plugin-marketplaces)
- [Build an App](./build-an-app)
