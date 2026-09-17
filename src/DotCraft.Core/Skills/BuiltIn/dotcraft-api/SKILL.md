---
name: dotcraft-api
description: Build applications with DotCraft's public SDKs, Harness, AppServer protocol, Channel adapters and modules, App Binding, dynamic tools, Desktop or .NET plugins, and MCP Apps. Use when connecting to, embedding, or publicly extending DotCraft; not for changing DotCraft itself.
---

# DotCraft API

Build against the latest public DotCraft surface. Official documentation explains current behavior; installed package types confirm the exact names available to the project being edited.

## Start from official documentation

Search `https://www.dotcraft.net` for the exact SDK, Harness, protocol, or extension topic and open the matching page before answering from memory. Use these section indexes when search is unavailable:

| Task | Documentation |
| --- | --- |
| Connect an application | `https://www.dotcraft.net/developing/sdks/` |
| Connect a messaging platform | `https://www.dotcraft.net/developing/sdks/channels` |
| Package a Channel for DotCraft hosts | `https://www.dotcraft.net/developing/integrations/typescript-module` |
| Embed the runtime in .NET | `https://www.dotcraft.net/developing/harness/` |
| Implement AppServer JSON-RPC | `https://www.dotcraft.net/developing/protocols/appserver-protocol` |
| Build App Binding or a plugin | `https://www.dotcraft.net/developing/integrations/app-binding` |

The site describes the latest public release. Do not use a repository checkout, internal specification, test fixture, or default branch as an API reference.

## Confirm code against the project

When writing code, inspect the project's package manifest and the installed package declarations:

- TypeScript: `@dotcraft/sdk` and its exported `.d.ts` files; `@dotcraft/plugin` for Desktop plugins.
- .NET client: `DotCraft.Sdk` and the bundled `DotCraft.Protocol` assembly.
- In-process hosting and managed plugins: `DotCraft.Harness` and its bundled public assemblies.

Do not invent a method, DTO, event, option, or manifest field. If neither the official documentation nor the installed public types confirms it, state that it could not be verified.

## Pick the integration shape

| Shape | Use for |
| --- | --- |
| Client SDK | An Electron app, CLI, service, or bot that connects to an existing workspace. |
| Harness | A .NET application that owns and runs the agent runtime in-process. |
| AppServer protocol | A custom client or unsupported language that implements JSON-RPC directly. |
| Channel adapter or module | A TypeScript integration that connects a messaging platform, optionally packaged for discovery and lifecycle management by DotCraft hosts. |
| Public extension | App Binding, Runtime Dynamic Tools, Desktop plugins, managed .NET plugins, or MCP Apps. |

Configuring an installed DotCraft workspace is a product-usage task rather than API development. Plugin scaffolding belongs to the plugin-creation workflow; this skill explains the public contracts used by the result.

## Read only the relevant reference

- [references/client-sdk.md](references/client-sdk.md) — SDK layers, connection choices, and current documentation routes.
- [references/harness-hosting.md](references/harness-hosting.md) — in-process .NET ownership and hosting routes.
- [references/protocol.md](references/protocol.md) — direct AppServer transport and handshake routes.
- [references/extending.md](references/extending.md) — public extension surfaces and their contract locations.

## Verify before finishing

- Build or typecheck the external project.
- Confirm every API name against its installed public types.
- Check runtime capabilities before using an optional AppServer surface.
- Link the official page used for behavioral guidance.
