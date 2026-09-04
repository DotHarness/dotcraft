# AppServer protocol

JSON-RPC 2.0 projecting the host-owned `ISessionService` to out-of-process clients. Use it directly only for a custom transport, an unsupported language, or protocol debugging; otherwise a client SDK already wraps it.

## Transport

| Transport | Framing | Status |
| --- | --- | --- |
| stdio | One complete JSON-RPC message per line, UTF-8, requests on stdin and responses on stdout; logs go to stderr. One client per process | Primary |
| WebSocket | One JSON-RPC message per text frame at `ws://HOST:PORT/ws`; connections are independent, each with its own initialization state and subscriptions | Experimental |

## Handshake

`initialize` must be the first message on a connection. Anything earlier is rejected with `-32002` ("Not initialized"); a second `initialize` on the same connection is rejected with `-32003` ("Already initialized").

After the `initialize` response the client sends the `initialized` notification (params `{}`, no response). Until it arrives the connection is initialized but not client-ready and ordinary requests are rejected as invalid. The server starts sending notifications only after `initialized`.

## Serialization

- Property names are camelCase; enums are camelCase strings.
- Timestamps are ISO 8601 UTC.
- Null fields are omitted unless the spec says otherwise.
- JSON-RPC `id` may be a string or an integer; the server echoes the type and value.
- Wire DTOs are not the on-disk persistence models. Do not read persisted thread JSON as if it were the wire contract.

A client that falls behind on notifications is buffered only to a limit, after which AppServer drops the connection. Consume the stream promptly.

## What the two artifacts contain

`appserver.manifest.json` is the complete contract: every method, type, item payload, and module. `openrpc.json` lists **requests** only, in both directions. Notifications exist only in the manifest and in the generated `ServerNotificationMethods` / `ClientNotificationMethods` maps, so searching `openrpc.json` for a notification name and finding nothing proves nothing.

Read the module list and its counts from the manifest's `modules` and `methods` entries.

## Narrowing a search in openrpc.json

Every request carries the same ten `x-dotcraft-*` keys. Only three of them actually discriminate:

| Key | Values present | Useful for |
| --- | --- | --- |
| `x-dotcraft-module` | the eight module names | Finding a feature's methods |
| `x-dotcraft-direction` | `clientToServer`, `serverToClient` | Separating calls you make from calls the server makes to you |
| `x-dotcraft-scope` | `connection`, `workspace`, `thread` | Knowing what a method needs in scope |
| `x-dotcraft-capability` | a capability name such as `mcpRuntime`, `approvalSupport`, `threadManagement`, or `null` | Knowing which negotiated capability gates the method |
| `x-dotcraft-errors` | error-code arrays such as `["InvalidParams","InvalidRequest","MethodNotFound"]` | The declared failures for that method |

`x-dotcraft-kind` is `request` on every entry, `x-dotcraft-stability` is `stable` on every entry, `x-dotcraft-since` is `1` on every entry, and `x-dotcraft-notification-opt-out` is `false` on every entry. Filtering on those four returns everything or nothing. `x-dotcraft-spec-ref` is the spec file path, not a section anchor.

## Three ways to reach the contract

1. **In this repository**: `src/DotCraft.Protocol/Artifacts/AppServer/` holds `openrpc.json`, `appserver.manifest.json` (types and fields), `contract.sha256`, and `schemas/<module>/*.schema.json`.
2. **Outside it, SDK installed**: `node_modules/@dotcraft/sdk/dist/generated/appserver/*.generated.d.ts`, or the `DotCraft.Protocol.AppServer` types shipped inside the `DotCraft.Sdk` package. `@dotcraft/sdk/meta` re-exports `SDK_VERSION`, `CONTRACT_VERSION`, `APPSERVER_PROTOCOL_VERSION`, and `CONTRACT_SHA256`.
3. **Neither**: fetch `openrpc.json` from `raw.githubusercontent.com/DotHarness/dotcraft/main/`. Treat this as the least trustworthy source — it is the default branch, not the user's build.

Compare `CONTRACT_SHA256` against the running build's `contract.sha256` before concluding a method is missing.

## Hub's role

Hub is a local coordinator, not a proxy. One Hub per OS user, one AppServer per workspace. A client asks Hub during bootstrap only — "make this workspace's AppServer available" — then connects **directly** to the returned WebSocket URL. Hub never carries conversation traffic.

Consequences for an application:

- Closing the SDK connection does not stop a Hub-managed AppServer. To actually stop a workspace runtime the user goes through the Desktop tray or exits Hub.
- Hub discovery lives in `~/.craft/hub/hub.lock` and `~/.craft/hub/appservers.json`; workspace ownership lives in `<workspace>/.craft/appserver.lock`. A stale lock from a dead process is removed automatically; a live one is reused when its endpoint is healthy.
- Implement Hub Protocol yourself only for a custom transport or an unsupported language.

## Live sources

`specs/protocols/appserver-protocol.md` is the normative spec and is very large; navigate it by its table of contents to the numbered section for the method family instead of reading it through. Prose lives at `/developing/protocols/appserver-protocol`, `/developing/protocols/hub-protocol`, and `/developing/lifecycle/hub`.
