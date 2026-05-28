# @dotcraft/sdk (TypeScript)

The DotCraft TypeScript SDK serves three audiences:

- Application developers: connect with `DotCraft.local()` / `DotCraft.remote()` and run threads.
- Protocol clients: use `@dotcraft/sdk/wire` for raw AppServer JSON-RPC access.
- Channel authors: use `@dotcraft/sdk/channel` to build external social channel modules.

The TypeScript SDK follows the shared AppServer and Hub model while providing TypeScript-specific packages and helpers. The Python SDK keeps its `dotcraft_wire` package name.

## Packages and Entry Points

```typescript
import { DotCraft, textPart, skillRefPart } from "@dotcraft/sdk";
import { DotCraftWireClient, WebSocketTransport } from "@dotcraft/sdk/wire";
import { HubClient } from "@dotcraft/sdk/hub";
import { ModuleChannelAdapter } from "@dotcraft/sdk/channel";
import { runModuleConformanceSuite } from "@dotcraft/sdk/testing";
```

First-party channel packages:

- `@dotcraft/channel-feishu`
- `@dotcraft/channel-weixin`
- `@dotcraft/channel-telegram`
- `@dotcraft/channel-qq`
- `@dotcraft/channel-wecom`

## Install

```bash
cd sdk/typescript
npm install
npm run build
```

Repository-local package dependency:

```json
{
  "dependencies": {
    "@dotcraft/sdk": "*"
  }
}
```

## Local Hub Quickstart

`DotCraft.local()` discovers or starts the local Hub, ensures the workspace AppServer, connects to its WebSocket endpoint, and performs `initialize` / `initialized`.

```typescript
import { DotCraft } from "@dotcraft/sdk";

const dotcraft = await DotCraft.local({
  workspacePath: "E:/Git/dotcraft",
  approvalHandler: async (request) => {
    console.log("approval requested", request);
    return "decline";
  },
});

const thread = await dotcraft.threads.getOrCreate({ userId: "me" });
const result = await thread.run("Summarize this workspace.");

console.log(result.text);
await dotcraft.close();
```

Production applications should always provide an explicit `approvalHandler`. If omitted, the SDK keeps the compatibility default and returns `accept`, which is useful for tests and non-interactive scripts but should not be treated as a production approval policy.

## Remote WebSocket

```typescript
import { DotCraft } from "@dotcraft/sdk";

const dotcraft = await DotCraft.remote({
  url: "ws://127.0.0.1:9100/ws",
  token: "",
});
```

If the URL already includes a `token` query parameter, the SDK does not append another one.

## Threads and Runs

```typescript
const thread = await dotcraft.threads.start({
  userId: "alice",
  displayName: "Build review",
});

for await (const event of thread.runStreamed("Review the current diff.")) {
  if (event.type === "agent_message_delta") process.stdout.write(event.delta ?? "");
  if (event.type === "completed") console.log(event.result?.text);
}
```

`run()` returns merged final text. `runStreamed()` yields normalized events and preserves raw JSON-RPC messages. The SDK does not automatically parse `/command`, `$skill`, or `@file` text; callers should explicitly use `commandRefPart()`, `skillRefPart()`, `fileRefPart()`, or raw command APIs. For the shared SDK event topology, see [SDKs](./sdk.md#event-topology).

## Dynamic Tools

Runtime dynamic tools are declared and bound together: each item includes both the wire descriptor and a local `handler`. The SDK registers handlers before `thread/start` or `thread/resume`, and strips handlers from the descriptor sent to the server.

```typescript
const thread = await dotcraft.threads.start({
  userId: "alice",
  dynamicTools: [
    {
      namespace: "local",
      name: "Echo",
      description: "Echo input.",
      inputSchema: { type: "object" },
      handler: async (request) => ({
        success: true,
        contentItems: [{ type: "text", text: JSON.stringify(request.arguments) }],
      }),
    },
  ],
});
```

## Raw Wire API

```typescript
import { DotCraftWireClient, WebSocketTransport } from "@dotcraft/sdk/wire";

const client = new DotCraftWireClient(new WebSocketTransport({ url: "ws://127.0.0.1:9100/ws" }));
await client.connect();
await client.initialize({ clientName: "raw-client", clientVersion: "0.1.0" });

const threads = await client.threadList({ channelName: "sdk", userId: "me" });
const raw = await client.request("thread/read", { threadId: threads[0]?.id });
```

## Channel Modules

For host-side module loading and lifecycle integration, see [TypeScript Module Integration](./typescript-module.md). First-party channel packages depend on `@dotcraft/sdk` and import from subpaths:

```typescript
import { textPart } from "@dotcraft/sdk";
import { WebSocketTransport } from "@dotcraft/sdk/wire";
import { ModuleChannelAdapter } from "@dotcraft/sdk/channel";
```

`@dotcraft/sdk/channel` also exports stable channel runtime components: `ThreadResolver`, `ChannelMessageQueue`, `CommandRouter`, `TurnStreamReducer`, `DeliveryDispatcher`, `ChannelToolDispatcher`, `ApprovalDispatcher`, `ModuleConfigLoader`, and `ModuleLifecycleState`. First-party channel packages use them through `ChannelAdapter` by default; new channels can reach for them directly when they need custom thread resolution, command routing, segmented streaming, or module lifecycle behavior.

## Debugging

- Pass `debugStream: true` in `ChannelAdapter` options. Logs use `[dotcraft-sdk:adapter-stream]`.
- Call `configureTextMergeDebug(true)`. Merge traces use `[dotcraft-sdk:text-merge]`.

## Validation

```bash
cd sdk/typescript
npm run typecheck:all
npm run test:all
npm run pack:verify
```

MIT
