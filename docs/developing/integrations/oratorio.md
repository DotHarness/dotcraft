# Oratorio integration

This page targets DotCraft contributors. It explains the integration boundaries around the built-in Oratorio workflow without repeating its domain model or Server API.

## Component boundaries

| Component | Responsibility |
| --- | --- |
| **Oratorio Server** | Owns tasks, runs, drafts, source synchronization, worktrees, decisions, settings, and realtime events. |
| **DotCraft Hub** | Starts and supervises the registered user-level `oratorio` managed service. It does not proxy Oratorio business requests. |
| **Desktop Main** | Resolves local or remote service access, injects the bearer, validates allowed routes, and owns the realtime connection. |
| **Desktop Renderer** | Renders the Board, task detail, and Settings through typed IPC without receiving the endpoint or bearer. |
| **Bundled descriptor** | Registers the built-in Oratorio view and Settings surface from the bundled plugin directory. |

Local Desktop asks Hub to ensure the bundled Server on first use. Remote Desktop resolves the Oratorio service alongside the selected DotCraft Stack. In both modes, Renderer requests cross the same Main-process boundary.

App Binding handoffs are inspected in Main and require explicit user approval. Renderer receives only a request ID and a redacted summary.

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
- [Desktop Extensions](./desktop-extensions)
- [DotCraft App](./app-binding)
- [Deploy the DotCraft Stack](../../features/self-hosted/server-deployment)
- [Oratorio](../../features/oratorio)
