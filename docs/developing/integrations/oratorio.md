# Oratorio integration

This page targets DotCraft contributors. It explains the integration boundaries around the built-in Oratorio workflow without repeating its domain model or Server API.

![Oratorio integration boundaries: the Desktop Renderer and the bundled desktop plugin sit on one side of a typed IPC line and never receive the endpoint or credential, Desktop Main holds both on the other side, and only Main exchanges requests and events with the Oratorio Server, which Hub starts but never proxies](/oratorio-boundaries-topology.svg)

## Component boundaries

| Component | Responsibility |
| --- | --- |
| **Oratorio Server** | Owns tasks, runs, drafts, source synchronization, worktrees, decisions, settings, and realtime events. |
| **DotCraft Hub** | Starts and supervises the registered user-level `oratorio` managed service. It does not proxy Oratorio business requests. |
| **Desktop Main** | Resolves local or remote service access, injects the bearer, validates allowed routes, and owns the realtime connection. |
| **Desktop Renderer** | Renders the Board, task detail, and Settings through typed IPC without receiving the endpoint or bearer. |
| **Bundled Desktop Plugin** | Registers the Oratorio view and Settings page through the public Desktop Plugin activation contract. |

Local Desktop asks Hub to ensure the bundled Server on first use. Remote Desktop resolves the Oratorio service alongside the selected DotCraft Stack. In both modes, Renderer requests cross the same Main-process boundary.

App connection handoffs are inspected in Main and require explicit user approval. After the user enables Oratorio for a thread, Main delivers the bind handoff directly to the managed service as technical activation and returns activation failures to the initiating flow. Renderer receives only a request ID and redacted summary for connection consent.

## Develop and validate

Build the Server and run its focused tests from the repository root:

```bash
dotnet build src/DotCraft.Oratorio/DotCraft.Oratorio.csproj
dotnet test tests/DotCraft.Oratorio.Tests/DotCraft.Oratorio.Tests.csproj
```

Run Desktop checks from `desktop/`:

```bash
npm test
npm run build
```

The repository packaging scripts publish the self-contained Server to `build/oratorio/` and stage it in Desktop resources. Use `build.bat` on Windows or `build_linux.bat` for the Linux package flow.

Keep Oratorio domain behavior in the Server. Desktop view models may format data for display but must not recreate lifecycle, retry, recovery, Worktree, or decision rules.

## Related docs

- [Hub Protocol](../protocols/hub-protocol)
- [Desktop Plugins](./desktop-plugins)
- [DotCraft App](./app-binding)
- [Server Deployment](../../features/self-hosted/server-deployment)
- [Oratorio](../../features/oratorio)
