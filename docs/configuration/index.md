# Configuration

Each page in this section lists the fields, defaults, and JSON examples for one area. For first-time setup, read [Getting started](../start/). For what a feature does and when to reach for it, start from its feature page and come back here for the exact fields.

## Configuration files

DotCraft reads global `~/.craft/config.json` first, then overlays workspace `.craft/config.json`. Workspace fields win. String values support `$VAR` and `${VAR}` environment variable placeholders. An unset variable keeps its placeholder unchanged.

Some settings have a file of their own in the same two places: global under `~/.craft/`, workspace under `.craft/`, with the workspace winning.

| File | Holds | Reference |
|---|---|---|
| `config.json` | Most settings | The pages in this section |
| `hooks.json` | Lifecycle hooks | [Hooks](./hooks) |
| `models.json` | Model context windows and Fast Mode support | [Models and providers](./models#model-capability-catalog) |
| `model-thinking-adapters.json` | Reasoning request shapes | [Models and providers](./models#reasoning-and-prompt-caching) |
| `plugin-config.json` | Settings that plugins declare | [Plugins, MCP, and LSP](./plugins#plugin-settings-files) |
| `commands/` | Custom `/` commands | [Conversations](./conversations#custom-commands) |
| `<channel>.json` | Channel adapters, workspace only | [Channels](./channels) |

For when a changed setting takes effect, see [Settings lifecycle](./settings-lifecycle).

## Inspect configuration from the CLI

`dotcraft config schema` prints the sections and fields included in the configuration schema, with their types, defaults, sensitivity, and reload behavior. `dotcraft config show` prints the merged configuration of one workspace with `ApiKey`, `Password`, and `Token` values masked as `***`.

```bash
dotcraft config schema --section Tools.Web
dotcraft config schema --json
dotcraft config show --json
```

`--section` accepts a section's display name or its JSON path. `--json` writes machine-readable output, and `config show` is indented JSON either way. `config show` reads the workspace in the current directory; pass `--workspace` for another one.

## Change configuration from a client

AppServer clients change settings by key path instead of editing files. `config/read` returns the effective configuration with sensitive values masked, and can include the user and workspace layers. `config/value/write` changes one key path, such as `Tools.CodeMode.Mode`, and `config/batchWrite` applies several edits to one layer at once. A write targets the workspace layer unless it names the user layer's file. Sensitive values such as API keys, tokens, and passwords are write-only: a write stores the new value, `config/read` shows it as `***`, and sending `***` back keeps the stored value. Model provider definitions are still changed through the provider methods. The [AppServer protocol](../extend/protocols/appserver) lists the parameters and error codes.

The server validates a write before saving it: the result must still load as a configuration, and every changed field must satisfy its type, range, and allowed values. A rejected write leaves the file untouched. An accepted write updates the running AppServer according to each field's reload behavior and is announced through `config/changed` with the changed key paths.

The Dashboard Settings page saves a whole layer file through the same path, so its saves are validated, applied, and announced the same way. A masked `***` value in a saved document keeps the stored secret.
