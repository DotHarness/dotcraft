---
name: dotcraft-api
description: Build applications on DotCraft's public surface: the @dotcraft/sdk and DotCraft.Sdk clients, in-process hosting with DotCraft.Harness, the AppServer JSON-RPC protocol and its generated contracts, App Binding, dynamic tools, plugin APIs, and channel modules. Use when writing or debugging code that connects to, embeds, or extends DotCraft.
---

# DotCraft API

Build applications that connect to, embed, or extend DotCraft from outside the agent loop.

Package names, entry types, method names, event names, and versions come from the pinned facts below and the generated contract, because DotCraft's surface changes between releases. When neither confirms a name, say so instead of supplying a plausible one.

## Pick the integration shape first

A client connects to a workspace, Harness is the workspace, an extension runs inside one.

| Shape | Use for | Entry point |
| --- | --- | --- |
| Client app | Electron, CLI, service, or bot driving an existing workspace | TS `DotCraft.local()` / `localChat()` / `remote()`; .NET `DotCraftClient.ConnectLocalAsync` / `ConnectLocalChatAsync` / `ConnectRemoteAsync` |
| In-process host | WPF, WinUI, console, service, or test that runs the agent itself | `AddDotCraftHarness(appConfig, options)` on an `IServiceCollection` |
| Extension | Desktop panel, managed .NET plugin, application-owned tools, MCP App, App Binding | `@dotcraft/plugin`, `IDotCraftPlugin` contributions, Runtime Dynamic Tools |
| Channel adapter | A new messaging platform as a first-class DotCraft channel | `@dotcraft/channel`, TypeScript only, repository-local |

`/developing/sdks/quickstart` is the one page carrying install commands and a first run for both languages; do not invent install steps elsewhere.

Configuring an existing installation belongs to `$dotcraft-guide`. Scaffolding a plugin directory belongs to `$plugin-creator`; this skill explains APIs and does not scaffold.

## Packages and surfaces

| Subject | What it is |
| --- | --- |
| `@dotcraft/sdk` | ESM npm package. Subpaths: `.` `./contracts` `./wire` `./hub` `./app-binding` `./dynamic-tools` `./testing` `./meta` |
| `DotCraft.Sdk` | NuGet client package; ships `DotCraft.Sdk.dll` and `DotCraft.Protocol.dll` together |
| `DotCraft.Harness` | NuGet package for in-process hosting; it is not a client |
| `@dotcraft/plugin` | npm package for Desktop plugins; `react` and `react-dom` are peer dependencies |
| `@dotcraft/channel*` | `"private": true`, never published. Build from `sdk/typescript/packages/` |
| Transport | JSON-RPC 2.0 over stdio (one JSON message per line) or WebSocket at `/ws` |
| Contract | `appserver.manifest.json` carries `protocolVersion`, `contractVersion`, and the module list; `contract.sha256` fingerprints it |
| Docs and source | `https://www.dotcraft.net` with clean URLs and a `/zh/` mirror. `/developing/sdks/` and `/developing/harness/` are index pages; `integrations`, `protocols`, and `lifecycle` have leaf pages only, and `/developing/` itself is not a page. Source: `https://github.com/DotHarness/dotcraft` |

Versions, target frameworks, and engine ranges are read from the user's lockfile, `.csproj`, `@dotcraft/sdk/meta`, or the package manifests, never quoted from this file.

## Find the protocol ground truth

Stop at the first source that exists, and prefer the build the user actually runs.

1. Inside this repository: `ReadFile src/DotCraft.Protocol/Artifacts/AppServer/openrpc.json`, `appserver.manifest.json`, `contract.sha256`, and `schemas/<module>/`. Prose lives in `specs/protocols/appserver-protocol.md` and `specs/sdk/`.
2. Outside it with the SDK installed: `node_modules/@dotcraft/sdk/dist/generated/appserver/*.generated.d.ts`. `method-groups` lists every method name, `models` holds the DTOs, and `ClientRequestMethods`, `ClientNotificationMethods`, `ServerRequestMethods`, `ServerNotificationMethods`, and `SessionItemPayloadMap` are the typed maps. The .NET equivalents are the `DotCraft.Protocol.AppServer` types.
3. Neither: `WebFetch("https://raw.githubusercontent.com/DotHarness/dotcraft/main/src/DotCraft.Protocol/Artifacts/AppServer/openrpc.json")`.

`openrpc.json` lists requests only; server notifications live in `appserver.manifest.json` and in the generated `ServerNotificationMethods` map. Narrow a `GrepFiles` sweep with `x-dotcraft-module`, `x-dotcraft-direction`, `x-dotcraft-scope`, or `x-dotcraft-capability` — the other extension keys hold the same value on every method.

## Version compatibility

`initialize` must be the first message on a connection, and the client must send the `initialized` notification before ordinary requests are accepted.

When a call fails with "method not found", a field is missing, or `initialize` is rejected, compare `APPSERVER_PROTOCOL_VERSION` with the handshake value and `CONTRACT_SHA256` with the running build's `contract.sha256` before blaming the user's code.

## References

Read the one that matches the task. They live in this skill's own directory, the `<location>` listed for `dotcraft-api` in the skills catalog, normally `.craft/skills/dotcraft-api/`.

- [references/client-sdk.md](references/client-sdk.md) — TypeScript and .NET client surface, run events, reconnect contract.
- [references/harness-hosting.md](references/harness-hosting.md) — embedding the runtime in a .NET process.
- [references/protocol.md](references/protocol.md) — transport, handshake, modules, and Hub's role.
- [references/extending.md](references/extending.md) — plugins, App Binding, dynamic tools, MCP Apps, channel modules.

## Verify before finishing

- Build or typecheck what you changed with `Exec`.
- TypeScript: every `@dotcraft/sdk` import uses one of the subpaths above. .NET: the project targets the framework the package declares.
- Every method, notification, event name, and DTO you used appears in the generated contract or type declarations.
