# Oratorio integration

This page targets DotCraft contributors. It explains the integration boundaries around the built-in [Oratorio](../../guide/oratorio/) workflow.

![Oratorio integration boundaries: the Desktop Renderer and the bundled Desktop extension sit on one side of a typed IPC line and never receive the endpoint or credential. Desktop Main holds both on the other side, and only Main exchanges requests and events with the Oratorio Server, which Hub starts but never proxies](/oratorio-boundaries-topology.svg)

## Component boundaries

| Component | Responsibility |
| --- | --- |
| **Oratorio Server** | Owns tasks, runs, drafts, source synchronization, worktrees, decisions, settings, and realtime events. |
| **DotCraft Hub** | Starts and supervises the registered user-level `oratorio` managed service. It does not proxy Oratorio business requests. |
| **Desktop Main** | Resolves local or remote service access, injects the bearer, validates allowed routes, and owns the realtime connection. |
| **Desktop Renderer** | Renders the Board, task detail, and Settings through typed IPC without receiving the endpoint or bearer. |
| **Bundled Desktop extension** | Registers the Oratorio view and Settings page through the public Desktop extension activation contract. |

Local Desktop asks Hub to ensure Oratorio on first use. Remote Desktop resolves the service alongside the selected [DotCraft Stack](../../self-hosting/server-deployment). In both modes, Renderer requests cross the same Main-process boundary.

App connection handoffs are inspected in Main and require explicit user approval. After the user enables Oratorio for a thread, Main delivers the bind handoff directly to the managed service as technical activation and returns activation failures to the initiating flow. Renderer receives only a request ID and redacted summary for connection consent.

## Service hosting and command line

`DotCraft.App` composes the Oratorio web application from `DotCraft.Oratorio`. Oratorio dispatches Agent work to workspace AppServers through the .NET SDK.

```bash
dotcraft oratorio serve --state /path/to/oratorio-state --listen http://127.0.0.1:5087
```

The default state directory is `~/.craft/oratorio`, and the default listener is `http://127.0.0.1:5087`.

Hub runs this command as one user-level process shared by local workspaces. The Stack runs AppServer and Oratorio in separate containers using the DotCraft image.

## GitHub discussion execution

Repository comment scans and optional webhooks share one mention intake. Command audit records identify the target kind and number. Dispatch refreshes the exact issue or pull request before creating a Discussion Turn, including importing an issue that has not reached the Board yet. Replies reuse the source write log and hidden reply markers for retry deduplication.

An independent issue discussion queries GitHub for the default branch, fetches that branch with the App installation credentials, and prepares the item's managed worktree at the fetched SHA. Fetch and SHA resolution run under the repository lock. Resuming an idle independent discussion refreshes this target. Active conversations and implementation threads keep their execution context. Source synchronization updates tracker data and does not switch or pull the base checkout.

The question includes the issue title, body, URL and discussion history, plus the actual branch, SHA and execution path when preparing an independent discussion. It creates no round, run or implementation delivery. The [GitHub guide](../../guide/oratorio/github) describes how collaborators invoke it.

## Develop and validate

Build the application and run the service and host tests from the repository root:

```bash
dotnet build src/DotCraft.App/DotCraft.App.csproj
dotnet test tests/DotCraft.Oratorio.Tests/DotCraft.Oratorio.Tests.csproj
dotnet test tests/DotCraft.App.Tests/DotCraft.App.Tests.csproj
```

Run Desktop checks from `desktop/`:

```bash
npm test
npm run build
```

Packaging stages the DotCraft publish output from `build/dotcraft/` in Desktop resources. Run `build.bat` for a local Windows package. The repository's `build-multiplatform` workflow produces the same layout for every shipped platform.

Keep Oratorio domain behavior in the Server. Desktop view models may format data for display but must not recreate lifecycle, retry, recovery, Worktree, or decision rules.

## Related docs

- [Hub protocol](../../extend/protocols/hub) — the interface Desktop uses to ensure the `oratorio` managed service is running.
- [Build a Desktop extension](../../extend/desktop-extensions) — the activation contract the bundled plugin uses to register the Oratorio view.
- [DotCraft App](../../extend/app-binding) — the authority model behind the connect and bind handoffs Main forwards.
