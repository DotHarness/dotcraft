# Public extension surfaces

Choose an extension surface by where the code runs and who owns its lifecycle.

| Surface | Contract source | Official documentation |
| --- | --- | --- |
| Channel adapter | `@dotcraft/channel` declarations | `https://www.dotcraft.net/developing/sdks/channels` |
| Channel module | `@dotcraft/channel` declarations and module manifest | `https://www.dotcraft.net/developing/integrations/typescript-module` |
| Desktop plugin | Installed `@dotcraft/plugin` declarations | `https://www.dotcraft.net/developing/integrations/desktop-plugins` and `https://www.dotcraft.net/developing/integrations/desktop-plugin-api` |
| Managed .NET plugin | Public types bundled with `DotCraft.Harness` | `https://www.dotcraft.net/developing/integrations/dotnet-plugins` and `https://www.dotcraft.net/developing/integrations/dotnet-plugin-reference` |
| Runtime Dynamic Tools | Installed SDK declarations | `https://www.dotcraft.net/developing/sdks/tools` |
| App Binding | Installed SDK declarations and AppServer capabilities | `https://www.dotcraft.net/developing/integrations/app-binding` |
| MCP App | MCP tool and resource contracts | `https://www.dotcraft.net/developing/integrations/mcp-apps` |
| Marketplace listing | Marketplace document format | `https://www.dotcraft.net/developing/integrations/plugin-market` |

Plugin scaffolding is a separate workflow. When editing an existing extension, use only its published package declarations and the official documentation above.

A Channel module packages a Channel adapter with the metadata and lifecycle entry points a host needs. Runtime Dynamic Tools execute in a connected client process. App Binding connects an external application to a workspace. Desktop and managed plugins execute inside DotCraft. MCP Apps render UI resources returned by an MCP server. Do not substitute one surface's manifest or callback model for another.
