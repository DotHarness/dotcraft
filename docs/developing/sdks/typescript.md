# TypeScript SDK reference

`@dotcraft/sdk` provides generated contracts, a pure JSON-RPC client, high-level Thread and Run APIs, Hub management, and host profiles. For installation and a first run, start with the [Quickstart](./quickstart).

## Package

| | |
|---|---|
| Package | `@dotcraft/sdk` (source preview) |
| Module format | ESM (`"type": "module"`) |
| Runtime baseline | Node.js 20+ |
| Protocol metadata | `version` and `sdkContractVersion` exports |

The package is not currently published to npm. Build it from the repository and install the local directory as described in the [Quickstart](./quickstart).

## Entry points

| Entry point | Purpose |
|-------------|---------|
| `@dotcraft/sdk/contracts` | Generated DTOs, method maps, unions, and protocol metadata. It has no Node.js, WebSocket, or runtime I/O dependency. |
| `@dotcraft/sdk/wire` | `DotCraftWireClient`, JSON-RPC transports, lifecycle state, and typed and raw protocol APIs. |
| `@dotcraft/sdk` | `DotCraft`, `DotCraftThread`, Run APIs, callbacks, and `DotCraftAppServerClient`. |
| `@dotcraft/sdk/hub` | Hub discovery, management, process startup, structured errors, and event streaming. |
| `@dotcraft/sdk/channel` | Channel adapter and hosted module runtime. |
| `@dotcraft/sdk/testing` | Transport and conformance test helpers. |

DotCraft Desktop is the SDK's first full production host consumer. Electron Renderer code can safely import `@dotcraft/sdk/contracts`; runtime entry points belong in Node.js or Electron Main, and Renderer code should not create AppServer or Hub connections.

## Wire API

Known protocol methods are checked against generated method maps:

```ts
const wire = new DotCraftWireClient(transport, options);

const result = await wire.request("thread/list", params);
await wire.notify("initialized", {});
const dispose = wire.on("thread/started", (params) => {
  console.log(params.thread.id);
});
```

Use the explicitly named raw APIs only for third-party or not-yet-cataloged extensions:

```ts
const value = await wire.requestRaw("ext/example/read", { id: "42" });
await wire.notifyRaw("ext/example/changed", { id: "42" });
const dispose = wire.onRaw("ext/example/event", (params) => console.log(params));
```

`DotCraftAppServerClient` builds Thread, Turn, command, model, MCP, and event helpers on the pure Wire client. `DotCraft` adds the application-oriented Thread and Run model.

## Connection lifecycle

The Wire client reports `connecting`, `initializing`, `ready`, `disconnected`, `reconnecting`, `reconnectError`, and `closed`.

- Raw Wire connections do not reconnect unless `autoReconnect` is enabled. High-level and Channel profiles enable reconnect explicitly.
- The default RPC timeout is 30 seconds and includes time spent waiting in the reconnect queue.
- Reconnect uses exponential backoff from 1 to 30 seconds with jitter. Up to 1024 new requests are queued in call order.
- In-flight requests fail on disconnect and are never replayed. After a new transport is initialized, queued requests are released.
- Handler registrations survive reconnect. Thread subscriptions, active Runs, and Runtime Dynamic Tool resources are not reconstructed automatically.

## Hub API

The Hub client can read the lock file and default chat, query or ensure the live Hub, resolve a workspace AppServer, ensure/restart/stop/list AppServers, read status and events, and shut down the Hub.

Hub failures preserve structured `code`, `message`, and `details`. Process startup accepts an explicit executable and a binary mismatch policy:

- `ignore`
- `restartIfMismatch`
- `errorIfMismatch`

When no expected executable is supplied, the default policy is `ignore`.

## High-level exports

The main entry point exports `DotCraft`, `DotCraftThread`, `DotCraftRunResult`, `DotCraftRunEvent`, `DotCraftAppServerClient`, typed errors, input-part builders, approval decisions, Runtime Dynamic Tool types, and App Binding helpers.

## Channel modules

First-party TypeScript channel modules depend on the local SDK package: `@dotcraft/channel-feishu`, `@dotcraft/channel-weixin`, `@dotcraft/channel-telegram`, `@dotcraft/channel-qq`, and `@dotcraft/channel-wecom`. See [Channel adapters](./channels).

## Validation

```bash
cd sdk/typescript
npm run build
npm run typecheck:all
npm run test:all
```

## Related docs

- [Quickstart](./quickstart)
- [Threads & runs](./runs)
- [Tools & approvals](./tools)
- [Channel adapters](./channels)
- [AppServer Protocol](../protocols/appserver-protocol)
