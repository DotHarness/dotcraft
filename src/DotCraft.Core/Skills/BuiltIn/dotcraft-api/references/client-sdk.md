# Client SDK

For applications that connect to a workspace that already exists. Read `harness-hosting.md` instead when the application runs the agent in its own process.

## Connect and close

| Task | TypeScript | .NET |
| --- | --- | --- |
| Workspace by path | `DotCraft.local({ workspacePath })` | `DotCraftClient.ConnectLocalAsync(path, options)` |
| Default Chat workspace | `DotCraft.localChat()` | `ConnectLocalChatAsync(options)` |
| Remote AppServer | `DotCraft.remote({ url, token })` | `ConnectRemoteAsync(url, options)` |
| Release the connection | `dotcraft.close()` | `await client.DisposeAsync()` |

Pass the token separately from the URL so it is not copied into logs. Closing the client does **not** stop a Hub-managed AppServer; see `protocol.md`.

## Managers on the connected client

| Area | TypeScript | .NET |
| --- | --- | --- |
| Threads | `dotcraft.threads` | `client.Threads` |
| Turns | folded into `DotCraftThread` | `client.Turns` |
| Models | `dotcraft.models.list()` | `client.Models.GetCatalogAsync()` |
| MCP runtime | `dotcraft.mcpRuntime` | `client.McpRuntime` |
| App Binding | `dotcraft.appBindings` | `client.AppBindings` |
| Providers, agent profiles | not exposed at the high level | `client.Providers`, `client.AgentProfiles` |
| Typed / raw Wire | `dotcraft.wire`, `request()`, `requestRaw()` | `client.Wire`, `RequestAsync()`, `RequestRawAsync()` |

Thread objects carry `run`/`RunAsync`, `runStreamed`/`RunStreamedAsync`, `listTurns`/`ListTurnsAsync`, `listItems`/`ListItemsAsync`, `subscribe`, `unsubscribe`, `enqueue`, `interrupt`, `setMode`, `archive`, `delete`, `snapshot`, and `refresh`.

TypeScript adds `threads.getOrCreate()`; .NET adds `Threads.UpdateModelConfigurationAsync(...)`. Neither exists in the other language — do not port the name across.

## Input parts

Wire part types are `text`, `fileRef`, `image`, `localImage`, `skillRef`, `commandRef`. TypeScript ships one helper per type: `textPart`, `fileRefPart`, `imageDataUrlPart`, `localImagePart`, `skillRefPart`, `commandRefPart`. .NET constructs the generated `InputPart` directly.

Neither client parses `/command`, `$skill`, or `@file` out of plain text, and remote image URLs are rejected as `image` parts — download first, then send a data URL or a `localImage` path AppServer can read.

## Run events

The two languages name the same event differently. Take the name from the language you are writing, never from the other one.

- **.NET** uses the wire method string, available as constants on `DotCraftRunEventTypes` (`RunModels.cs`): `ThreadStarted`, `ThreadResumed`, `ThreadStatusChanged`, `ThreadRuntimeChanged`, `QueueUpdated`, `TurnStarted`, `ItemStarted`, `ItemCompleted`, `AgentMessageDelta`, `ReasoningDelta`, `ToolArgumentsDelta`, `ApprovalResolved`, `UsageDelta`, `SubagentProgress`, `PlanUpdated`, `SystemEvent`, `Completed`, `Failed`, `Cancelled`, `Raw`. Typed parameters arrive on `DotCraftRunEvent<TParams>.Params`.
- **TypeScript** normalizes to snake_case on `DotCraftRunEvent.type` — `turn_started`, `item_started`, `item_completed`, `agent_message_delta`, `reasoning_delta`, `tool_arguments_delta`, `approval_resolved`, `usage_delta`, `plan_updated`, `subagent_progress`, `system_event`, `queue_updated`, `completed`, `failed`, `cancelled`, `raw`. There is **no** exported constant list for these; the type is a plain `string`, so a typo compiles and then silently never matches.

For the underlying wire notification names, read the generated `ServerNotificationMethods` map rather than guessing from the normalized name.

## Reconnect contract

Reconnect restores the Wire transport, repeats initialization, and preserves local handler registrations. It does **not**:

- replay in-flight requests or a `turn/start` that lost its response;
- recreate thread subscriptions;
- resume an active run — an active .NET run fails with `RunDisconnectedException`;
- rebind runtime dynamic tools.

After reconnecting: resubscribe, refresh the thread header, read history from a **new** page rather than an old cursor, rebind runtime tools, then continue from server state. A request whose response was lost may still have been executed.

## Errors

TypeScript: `DotCraftError`, `InitializationError`, `ProtocolViolationError`, `RunDisconnectedError`, `TurnFailedError`, `TurnCancelledError`, `TurnInProgressError`, `ThreadNotFoundError`, `ThreadNotActiveError`, `ApprovalTimeoutError`. .NET uses the matching `*Exception` names. Branch on the type or the stable `code`, not on the message text.

## Live sources

- Signatures: `sdk/typescript/src/dotcraft.ts`, `sdk/dotnet/src/DotCraft.Sdk/AppServer/`, or the installed `.d.ts` / XML docs.
- Prose under `/developing/sdks/`: `quickstart`, `runs`, `tools`, `mcp-runtime`, `typescript`, `dotnet`.
- Samples: `sdk/typescript/samples/applications/` (`application.ts`, `continue-thread.ts`, `first-run.ts`, `models-and-mcp.ts`, `multimodal-input.ts`) and `sdk/dotnet/samples/` (`AgentProfileThreadSample`, `InteractiveToolSample`, `DotNetPluginSample`).
