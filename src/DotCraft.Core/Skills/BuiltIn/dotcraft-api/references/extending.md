# Extension surfaces

Code that runs *inside* DotCraft rather than connecting to it from outside. Scaffolding a plugin directory is `$plugin-creator`'s job; this file explains what each surface is and where its contract lives.

## Pick the surface

| Surface | Runs in | Entry contract | Docs | Repository |
| --- | --- | --- | --- | --- |
| Desktop Plugin | The Desktop renderer, fully trusted | Named `activate` export typed `DesktopPluginActivate`, from `@dotcraft/plugin` | `/developing/integrations/desktop-plugins` and `/developing/integrations/desktop-plugin-api` | `sdk/typescript/packages/plugin/` |
| Managed .NET plugin | The DotCraft process | `IDotCraftPlugin` on one public type with a public parameterless constructor; contributes through `context.Contributions.Add<T>(...)` | `/developing/integrations/dotnet-plugins` and `/developing/integrations/dotnet-plugin-reference` | `sdk/dotnet/samples/DotNetPluginSample/` |
| Runtime Dynamic Tools | The **client's** process, called back over the Wire | `dynamicTools` on thread start/resume; `@dotcraft/sdk/dynamic-tools` or `DotCraft.Sdk.DynamicTools` | `/developing/sdks/tools` | `sdk/typescript/src/dynamic-tools.ts`, `sdk/dotnet/src/DotCraft.Sdk/AppServer/RuntimeDynamicToolDeclarationBuilder.cs` |
| App Binding | An external app authenticated as a workspace principal | `app/connection/*` and `thread/appBindings/*`; tools come from the app's own binding-scoped Streamable HTTP MCP server | `/developing/integrations/app-binding` | `sdk/typescript/src/app-binding.ts` |
| MCP App | An MCP server's tool result, rendered sandboxed in Desktop | `_meta.ui.resourceUri` pointing at a `ui://` resource, plus `visibility` | `/developing/integrations/mcp-apps` | — |
| TypeScript channel module | A host process (Desktop, CLI, supervisor) | `@dotcraft/channel` module contract; subclass `ChannelAdapter` | `/developing/sdks/channels` and `/developing/integrations/typescript-module` | `sdk/typescript/packages/channel*/` |
| Marketplace listing | Distribution, not runtime | Marketplace document | `/developing/integrations/plugin-market` | — |

## Two rules that get broken constantly

- The plugin schema has no `tools`, `functions`, or `processes` manifest fields. Out-of-process executable capability is exposed through MCP.
- A thread-scoped callback that the AppServer invokes in a client process is a **Runtime Dynamic Tool**, not a plugin manifest entry. If someone is reaching for a manifest field to register a callback, they want dynamic tools.

## Details worth knowing before you write code

- A plugin manifest is `.craft-plugin/plugin.json` at `schemaVersion: 1`. Manifest-relative paths must start with `./`, stay inside the plugin root, and never contain `..`. `version` is required in `MAJOR.MINOR.PATCH` form.
- A Desktop module is declared inline as one `desktop` entry. `entry` must name an existing `.mjs` under `./desktop/dist/`, and every `styles` entry an existing `.css` in the same directory; imported chunks and assets must stay there too. `capabilities` labels neither grant nor restrict renderer access.
- `@dotcraft/plugin` declares `react` and `react-dom` as peer dependencies and ships a `dotcraft-plugin` build command.
- One bundle can carry both a Desktop module and a managed .NET plugin under one plugin id.
- Plugin hook commands can use `${DOTCRAFT_PLUGIN_ROOT}` and `${DOTCRAFT_PLUGIN_DATA}`. First run still requires user trust through Desktop Hooks settings or the `hooks/setState` method.
- The `@dotcraft/channel` family is `"private": true` and never published. Build it from `sdk/typescript/packages/` and install the local directories; only `@dotcraft/sdk` and `@dotcraft/plugin` come from a registry. Channel adapters are TypeScript-only — the .NET SDK ships none.
- `mcpRuntime` / `McpRuntime` is a control API over *configured* MCP servers. It does not define a tool in your process; that is what Runtime Dynamic Tools are for.

## Hand off

- Creating or scaffolding a plugin bundle: `$plugin-creator`.
- Turning on or configuring an existing plugin, MCP server, or channel in this installation: `$dotcraft-guide`.
- Changing DotCraft's own source for these surfaces: `dotcraft-dev-guide`.
