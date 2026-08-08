# TypeScript SDK reference

`@dotcraft/sdk` is the Node.js SDK for AppServer applications. Start with the [Quickstart](./quickstart) for installation and a first run.

## Package

| Field | Value |
| --- | --- |
| Package | `@dotcraft/sdk` (source preview) |
| Module format | ESM |
| Runtime | Node.js 20+ |

The package is private and is not published to npm. Runtime entry points belong in Node.js or Electron Main, not browser or Electron Renderer code.

## Entry points

| Entry point | Public surface |
| --- | --- |
| `@dotcraft/sdk` | `DotCraft`, threads, runs, callbacks, input helpers, approval decisions, and high-level errors. |
| `@dotcraft/sdk/contracts` | Generated DTOs, method maps, registries, and protocol metadata. |
| `@dotcraft/sdk/wire` | `DotCraftWireClient`, transports, lifecycle state, timeouts, typed methods, and raw extension APIs. |
| `@dotcraft/sdk/hub` | Hub discovery, AppServer management, process policy, events, and structured errors. |
| `@dotcraft/sdk/app-binding` | App Binding handoff and result helpers. |
| `@dotcraft/sdk/dynamic-tools` | Runtime Dynamic Tool authoring helpers. |
| `@dotcraft/sdk/testing` | Transport test helpers. |
| `@dotcraft/sdk/meta` | SDK, contract, protocol, and contract-hash metadata. |

Contracts has no Node.js, WebSocket, or runtime I/O dependency, so Renderer code may import it for types.

## High-level API

| Task | API |
| --- | --- |
| Connect | `DotCraft.local()`, `DotCraft.localChat()`, `DotCraft.remote()` |
| Close | `dotcraft.close()` |
| Threads | `threads.getOrCreate()`, `start()`, `resume()`, `list()`, `listPage()`, `read()`, `listTurns()`, `listItems()` |
| Run | `run()`, `runStreamed()`, `enqueue()`, `interrupt()` |
| Thread state | `snapshot()`, `refresh()`, `subscribe()`, `unsubscribe()`, `setMode()`, `archive()`, `delete()` |
| Models | `models.list()` |
| MCP runtime | `mcpRuntime.listStatus()`, `readResource()`, `callTool()`, `loginOAuth()`, `reload()` |
| App Binding | `appBindings` |
| Runtime tools | `onToolCall()` |

Configure `approvalHandler` and `userInputHandler` in local or remote connection options. See [Threads & runs](./runs) and [Tools & approvals](./tools) for task flows.

## Connect

| Method | Required option | Connection ownership |
| --- | --- | --- |
| `DotCraft.local(options)` | `workspacePath` | Uses Hub to ensure the workspace AppServer, then connects to it. |
| `DotCraft.localChat(options?)` | None | Uses Hub to ensure the default Chat workspace AppServer. |
| `DotCraft.remote(options)` | `url`; optional `token` | Connects directly to an existing AppServer WebSocket. |

All three option types accept client identity, approval and user-input handlers, and additional capabilities. Local options also accept executable selection, binary-match policy, Hub timeout, and home-directory overrides.

| Option type | Fields |
| --- | --- |
| `DotCraftLocalOptions` | Required `workspacePath`; optional `clientName`, `clientVersion`, `clientTitle`, `executable`, `expectedExecutable`, `binaryMatchPolicy`, `hubStartupTimeoutMs`, `homeDir`, handlers, and `capabilities`. |
| `DotCraftLocalChatOptions` | The local fields except `workspacePath`. |
| `DotCraftRemoteOptions` | Required `url`; optional `token`, client identity, handlers, and `capabilities`. |

## Threads and runs

`ThreadManager` has these exact high-level operations:

```ts
getOrCreate(options?: GetOrCreateThreadOptions): Promise<DotCraftThread>;
start(options?: StartThreadOptions): Promise<DotCraftThread>;
resume(threadId: string, options?: ResumeThreadOptions): Promise<DotCraftThread>;
list(options?: ListThreadOptions): Promise<ThreadSummary[]>;
listPage(options?: ListThreadOptions): Promise<ThreadListResult>;
read(threadId: string): Promise<SessionThread>;
listTurns(threadId: string, options?: ThreadHistoryPageOptions): Promise<ThreadTurnsListResult>;
listItems(threadId: string, options?: ThreadItemPageOptions): Promise<ThreadItemsListResult>;
```

Start options contain identity fields, display name, history mode, configuration, Runtime Dynamic Tools, and additional context. Resume options only rebind dynamic tools and additional context. List options add identity/workspace scope, archived filtering, text query, limit, and cursor.

`read()` and a Thread handle's `refresh()` return the current Thread header without persisted Turns or Items. `listTurns()` reads Turn metadata; `listItems()` reads Items across the Thread or for the optional `turnId`. Both accept `cursor`, `limit`, and `sortDirection`, and return `data` plus an opaque `nextCursor`. Thread handles expose the same two pagination methods without the `threadId` argument.

`run()` and `runStreamed()` accept text, `InputPart[]`, or `{ input, sender }`. Run options are `sender`, `collectRawEvents`, `abortSignal`, and `enqueueIfBusy`. A buffered result contains `thread`, optional terminal `turn`, merged `text`, `items`, optional `usage`, optional raw events, and any queued-input result.

## Models, MCP, and App Binding

| Manager | Operations |
| --- | --- |
| `models` | `list()` returns the model catalog visible to this AppServer. |
| `mcpRuntime` | `listStatus()`, `readResource()`, `callTool()`, `loginOAuth()`, `reload()`. |
| `appBindings` | App discovery, connection, surfaces, thread bindings, social bindings, and principal operations. |

The TypeScript high-level surface lists models but does not currently provide a model-configuration convenience method. Use the typed Wire request map for `thread/config/update` when an application must change the complete thread configuration, and preserve fields it does not own. See [MCP runtime](./mcp-runtime) and [DotCraft App](../integrations/app-binding) for task-oriented flows.

The MCP manager signatures are:

```ts
listStatus(params?: McpServerStatusListParams): Promise<McpServerStatusListResult>;
readResource(params: McpServerResourceReadParams): Promise<McpServerResourceReadResult>;
callTool(params: McpServerToolCallParams): Promise<McpServerToolCallResult>;
loginOAuth(params: McpServerOAuthLoginParams): Promise<McpServerOAuthLoginResult>;
reload(): Promise<McpServerReloadResult>;
```

## Callbacks and Runtime Dynamic Tools

```ts
type ApprovalHandler =
  (request: Record<string, unknown>) => Promise<ApprovalDecision> | ApprovalDecision;
type UserInputHandler =
  (request: Record<string, unknown>) => Promise<Record<string, unknown>> | Record<string, unknown>;
type DynamicToolHandler =
  (request: DynamicToolCallRequest) => Promise<DynamicToolCallResult> | DynamicToolCallResult;

thread.onToolCall(namespace: string | null, name: string, handler: DynamicToolHandler): Unsubscribe;
```

Handlers execute in the application process. Register them before starting work that can call the tool, validate arguments in the handler, and dispose registrations when their owning scope ends.

## Typed and raw Wire API

Use the typed method map for cataloged AppServer methods:

```ts
const result = await wire.request("thread/list", params);
const dispose = wire.on("thread/started", ({ thread }) => console.log(thread.id));
```

Use raw APIs only for third-party or not-yet-cataloged extensions:

```ts
const value = await wire.requestRaw("ext/example/read", { id: "42" });
const dispose = wire.onRaw("ext/example/changed", console.log);
```

`DotCraftWireClient` owns JSON-RPC and connection state. It does not approve requests, answer user input, or rebuild thread and tool resources.

## Connection lifecycle

Wire state is `connecting`, `initializing`, `ready`, `disconnected`, `reconnecting`, `reconnectError`, or `closed`.

- Raw Wire connections do not reconnect unless `autoReconnect` is enabled.
- Ordinary requests default to a 30-second timeout.
- Reconnect uses exponential backoff and queues at most 1024 new calls.
- In-flight calls fail and are never replayed.
- Initialization completes before queued calls are released.
- Handler registrations survive reconnect. Thread subscriptions, active runs, and runtime tool resources do not.

Closing a local high-level client closes its WebSocket connection. It does not stop Hub or the Hub-managed AppServer.

## Errors

All SDK errors derive from `DotCraftError` and carry a stable `code`.

| Error | Condition |
| --- | --- |
| `JsonRpcError` | AppServer returned a JSON-RPC error. Preserves `rpcCode` and data. |
| `InitializationError` | Connection initialization failed. |
| `TurnInProgressError` | The thread already has an active turn. |
| `ThreadNotFoundError` / `ThreadNotActiveError` | The target thread is missing or cannot run. |
| `TurnFailedError` / `TurnCancelledError` | A buffered run reached a failed or cancelled terminal state. |
| `ApprovalTimeoutError` | AppServer reports approval timeout. |
| `ProtocolViolationError` | A known message does not match its contract. |

The Wire entry point also exports `TransportError`, `TransportClosed`, `RequestTimeoutError`, and `ReconnectQueueFullError`.

## Hub API

`HubClient` discovers or starts Hub, validates the local lock, resolves a workspace AppServer, and supports ensure, restart, stop, list, status, events, and shutdown operations.

Hub errors preserve `code`, `message`, and `details`. Do not log Hub tokens or full token-bearing WebSocket URLs.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| npm cannot resolve `@dotcraft/sdk` | The package is a source preview. Build it from the repository checkout as shown in the [Quickstart](./quickstart). |
| Local connection requires a workspace | Pass `workspacePath`, or use `DotCraft.localChat()` for the default Chat workspace. |
| Remote initialization fails | Verify the AppServer WebSocket URL ends in `/ws` and the token matches that AppServer. Do not print either value in logs. |
| A run ends during reconnect | In-flight work is not replayed. Read or resume the thread, subscribe again, and re-register runtime tool handlers before continuing. |

## Related docs

- [SDK quickstart](./quickstart)
- [Threads & runs](./runs)
- [Tools & approvals](./tools)
- [MCP runtime](./mcp-runtime)
- [Channel adapters](./channels)
- [AppServer Protocol](../protocols/appserver-protocol)
