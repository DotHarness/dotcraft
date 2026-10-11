# Public extension surfaces

Choose an extension surface by where the code runs and who owns its lifecycle.

| Surface | Contract source | Official documentation |
| --- | --- | --- |
| Channel adapter | `@dotcraft/channel` declarations | `https://www.dotcraft.net/extend/sdks/channels` |
| Channel module | `@dotcraft/channel` declarations and module manifest | `https://www.dotcraft.net/extend/channel-modules` |
| Desktop plugin | Installed `@dotcraft/plugin` declarations | `https://www.dotcraft.net/extend/desktop-plugins` and `https://www.dotcraft.net/extend/desktop-plugin-api` |
| Managed .NET plugin | Public types bundled with `DotCraft.Harness` | `https://www.dotcraft.net/extend/dotnet-plugins` and `https://www.dotcraft.net/extend/dotnet-plugin-api` |
| Runtime Dynamic Tools | Installed SDK declarations | `https://www.dotcraft.net/extend/sdks/tools` |
| App Binding | Installed SDK declarations and AppServer capabilities | `https://www.dotcraft.net/extend/app-binding` |
| MCP App | MCP tool and resource contracts | `https://www.dotcraft.net/extend/mcp-apps` |
| Marketplace listing | Marketplace document format | `https://www.dotcraft.net/extend/plugin-market` |

Plugin scaffolding is a separate workflow. When editing an existing extension, use only its published package declarations and the official documentation above.

A Channel module packages a Channel adapter with the metadata and lifecycle entry points a host needs. Runtime Dynamic Tools execute in a connected client process. App Binding connects an external application to a workspace. Desktop and managed plugins execute inside DotCraft. MCP Apps render UI resources returned by an MCP server. Do not substitute one surface's manifest or callback model for another.
