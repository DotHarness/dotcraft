# Plugins, MCP, and LSP

## Plugins

| Field | Description | Default |
|-------|-------------|---------|
| `Plugins.DisabledPlugins` | Plugin ids turned off in this workspace | `[]` |
| `Plugins.PluginRoots` | Extra plugin root directories maintained outside `.craft/plugins/` | `[]` |
| `Plugins.PluginRegistries` | Plugin marketplace sources available for catalog discovery | `[]` |
| `Plugins.DisableDefaultPluginRegistry` | Ignore the host-provided default official plugin registry | `false` |

Official DotCraft Desktop and Docker hosts supply the official plugin marketplace as the default registry through `DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL`. Marketplace sources added through Desktop are stored in the global configuration; a workspace `PluginRegistries` value follows the normal workspace-over-global precedence. Docker Stack deployments persist the global configuration and marketplace cache under `state/dotcraft`.

`Plugins.PluginRegistries` entry fields:

| Field | Description | Default |
|---|---|---|
| `Name` | Marketplace identity. Required for manual entries and must match the marketplace document; maintained automatically when added through Desktop or AppServer | Empty |
| `SourceType` | Source kind: `git`, `local`, or `archive` | Inferred when omitted |
| `Url` | Git URL, local directory, archive URL, or archive file | Empty |
| `Ref` | Git branch, tag, or commit to check out | Source default |
| `SparsePaths` | Repository-relative paths included in a Git checkout | `[]` |
| `MarketplacePath` | Marketplace document path inside the source root | `.craft/plugins/marketplace.json` |
| `LastUpdated` | UTC timestamp of the last successful add or refresh | Empty |
| `LastRevision` | Resolved Git revision from the last successful fetch | Empty |

When `SourceType` is omitted, an existing directory or archive file is read locally; other values are treated as archive URLs. `Ref` and `SparsePaths` apply only to Git sources.

See [Plugin Market](../extend/plugin-market) for source syntax, the marketplace document, and lifecycle behavior.

Local plugin development override example:

```json
{
  "Plugins": {
    "PluginRoots": ["/path/to/local/plugins"]
  }
}
```

## Plugin settings files

Plugin-defined settings do not live under `Plugins` in the main `config.json`. A plugin declares `"settings": "./settings.schema.json"` in `.craft-plugin/plugin.json`, and the host reads two dedicated files:

| Scope | Path |
|---|---|
| Personal | `<UserDataPath>/plugin-config.json`; the official app uses `~/.craft/plugin-config.json` |
| Workspace | `<DataPath>/plugin-config.json`; the default is `<workspace>/.craft/plugin-config.json` |

The root object is keyed directly by canonical plugin id:

```json
{
  "acme.review-core": {
    "checklistLimit": 5,
    "tone": "concise"
  }
}
```

Effective settings resolve as schema defaults, then personal values, then workspace values. Objects merge recursively; arrays and scalar values replace the lower layer. A namespace is rejected as a whole when it contains an undeclared field or an invalid value. Removing a workspace value reveals the personal value or schema default below it.

These files are for small JSON configuration, not blobs, databases, or caches. Plugin data remains separate at `<UserDataPath>/plugins/<id>/data` when `UserDataPath` is configured, or `<DataPath>/plugin-data/<id>` otherwise. Disabling, removing, or reinstalling a plugin does not delete either its configuration namespace or data directory.

## MCP servers

| Field | Description | Default |
|-------|-------------|---------|
| `McpServers` | MCP server configuration map | `{}` |
| `Tools.DeferredLoading.Strategy` | Deferred tool loading strategy: `Off`, `Auto`, `Simulated`, or `Native` | `Auto` |
| `Tools.DeferredLoading.AlwaysLoadedTools` | MCP tool names always loaded upfront | `[]` |
| `Tools.DeferredLoading.DeferThreshold` | Minimum MCP tool count before MCP tools are deferred | `10` |
| `Tools.DeferredLoading.MaxSearchResults` | Maximum deferred tool search results per query | `5` |

Each `McpServers` entry accepts only the fields defined by its current schema. Unknown properties cause configuration parsing to fail.

MCP example:

```json
{
  "McpServers": {
    "everything": {
      "command": "npx",
      "arguments": ["-y", "@modelcontextprotocol/server-everything"]
    }
  },
  "Tools": {
    "DeferredLoading": {
      "Strategy": "Auto",
      "DeferThreshold": 10
    }
  }
}
```

With `Tools.DeferredLoading.Strategy = Auto`, all modes use the canonical `SearchTools` operation. OpenAI Responses maps it to the provider's client-executed `tool_search` wire type, Anthropic returns native tool references, and chat-completions injects the discovered schemas on the next model request.

### Pre-registered OAuth clients

For an HTTP MCP service that requires an existing OAuth application, open the server in Settings → MCP and set **OAuth → Client registration** to **Pre-registered**. Enter its client ID, choose a secret value or an environment variable, and set the callback URL registered with the service. You can also configure it in the file:

```json
{
  "McpServers": {
    "registered-service": {
      "transport": "http",
      "url": "https://mcp.example.com/mcp",
      "oauth": {
        "clientId": "your-registered-client-id",
        "clientSecretEnvVar": "MCP_CLIENT_SECRET",
        "callbackUrl": "http://127.0.0.1:8080/callback/"
      }
    }
  }
}
```

Set the referenced variable before starting the DotCraft host. `clientSecretEnvVar` takes precedence over `clientSecret`; a missing or empty referenced value fails instead of using the literal secret. `callbackPort` is optional when the URL already includes a port. A fixed callback port must be available. Leave OAuth configuration absent to use the existing automatic registration flow. Save the server, then use Authenticate to authorize it.

## LSP

| Field | Description | Default |
|-------|-------------|---------|
| `LspServers` | LSP server configuration map | `{}` |
| `Tools.Lsp.Enabled` | Enables built-in LSP tools | `false` |
| `Tools.Lsp.MaxFileSize` | Max LSP file size | `10485760` |

Each `LspServers` entry likewise accepts only the fields defined by its current schema.
