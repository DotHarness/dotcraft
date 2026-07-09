# DotCraft SDK Specification

| Field | Value |
|-------|-------|
| **Version** | 0.3.0 |
| **Status** | Living |
| **Date** | 2026-06-03 |
| **Related Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Hub Architecture](../architecture/hub-architecture.md), [App Binding](../protocols/app-binding.md), [External Channel Adapter](../protocols/external-channel-adapter.md), [Session Core](../architecture/session-core.md), [TypeScript SDK Binding](typescript.md), [.NET SDK Binding](dotnet.md), [Python SDK Binding](python.md) |

Purpose: define the shared SDK design contract for DotCraft across languages while allowing each language binding to keep idiomatic package structure, runtime constraints, publishing rules, and environment-specific helpers.

This document is the canonical cross-language SDK spec. Language binding specs refine this contract; they must not redefine shared protocol semantics.

---

## 1. Scope

This specification defines:

- Shared SDK design principles.
- Required SDK capability profiles.
- Optional or language-specific profiles.
- Cross-language capability parity expectations.
- Shared testing, security, and versioning rules.

This specification does not define:

- AppServer JSON-RPC wire semantics. See [AppServer Protocol](../protocols/appserver-protocol.md).
- Hub HTTP endpoint semantics. See [Hub Architecture](../architecture/hub-architecture.md).
- App Binding product semantics. See [App Binding](../protocols/app-binding.md).
- Session persistence, turn lifecycle, item payload semantics, or agent internals. See [Session Core](../architecture/session-core.md).
- Exact package exports, class names, namespace layout, or publishing workflows. Those live in the language binding specs.

## 2. Design Principles

### 2.1 Parallel Surface, Idiomatic Casing

Every general-purpose SDK exposes the same DotCraft capabilities through the same core nouns and verbs, differing only by each language's casing and idiom. A developer fluent in one binding should read another without a translation table: `connectLocal` / `ConnectLocalAsync` / `connect_local` are the same operation; `thread.run()` / `thread.RunAsync()` / `thread.run()` are the same operation.

This is the design center of the cross-language contract. Bindings stay idiomatic in *form* — async suffixes, casing, iteration primitives, error types, packaging — but the surface is *parallel in shape*: same entry points, same method names, same option keys, same event model, same capability coverage. Divergent names or object models for the same capability are parity debt, tracked in the capability matrix (§5), not an accepted outcome.

The canonical surface below is the spine all general-purpose bindings converge on:

| Concept | TypeScript | .NET | Python |
|---------|-----------|------|--------|
| Connect (local Hub) | `DotCraft.local()` | `DotCraftClient.ConnectLocalAsync()` | `DotCraft.connect_local()` |
| Connect (default Chat) | `DotCraft.localChat()` | `DotCraftClient.ConnectLocalChatAsync()` | `DotCraft.connect_local_chat()` |
| Connect (remote WebSocket) | `DotCraft.remote()` | `DotCraftClient.ConnectRemoteAsync()` | `DotCraft.connect_remote()` |
| Raw request escape hatch | `request()` | `RequestAsync()` | `request()` |
| Thread manager | `dotcraft.threads` | `client.Threads` | `dotcraft.threads` |
| Active thread handle | `DotCraftThread` | `DotCraftThread` | `Thread` |
| Run, buffered | `thread.run()` | `thread.RunAsync()` | `thread.run()` |
| Run, streamed | `thread.runStreamed()` | `thread.RunStreamedAsync()` | `thread.run_streamed()` |
| Normalized run event | `DotCraftRunEvent` | `DotCraftRunEvent` | `RunEvent` |
| Approval handler | `approvalHandler` | `ApprovalHandler` | `approval_handler` |
| User-input handler | `userInputHandler` | `UserInputHandler` | `user_input_handler` |
| Dynamic tool handler | `thread.onToolCall()` | `thread.OnToolCall()` | `thread.on_tool_call()` |
| App Binding helpers | `dotcraft.appBindings` | `client.AppBindings` | `dotcraft.app_bindings` |

Streaming is a variant of one verb (`run` vs `runStreamed`), not a separate object model. Both return the same normalized event type, exposed over each language's native iteration primitive: `for await … of` (TypeScript), `await foreach` over `IAsyncEnumerable<T>` (.NET), `async for` (Python).

### 2.2 AppServer And Hub Are Authoritative

SDKs are clients. They must not duplicate server-side thread state machines, queue semantics, approval policy, App Binding validation, model catalog resolution, or persistence rules.

### 2.3 Raw Escape Hatch Is Required

Every general-purpose SDK must expose a raw AppServer request API and a raw notification stream or registration API so callers can use newly added protocol methods before typed wrappers exist.

### 2.4 Typed Wrappers Are Traceable

Every typed SDK wrapper must map to one or more rows in this spec's capability matrix and to the owning protocol spec. If a language adds a typed wrapper first, this shared spec must be updated in the same change.

### 2.5 Language-Specific Profiles Are Allowed

A few SDK surfaces are naturally language-specific. Examples:

- The first-party **hosted** channel module runtime (manifests, module lifecycle, Desktop-managed startup) is owned by TypeScript, because hosted channel modules are Node packages. The channel **adapter** base class itself is a shared capability that any SDK building external channels may provide; TypeScript and Python both ship one today.
- Publishing, packaging, and runtime baselines remain language binding concerns.

Everything else — including App Binding helpers, the Run profile, and approval/user-input callbacks — is a shared capability with parallel surfaces across every general-purpose SDK, not a single-language feature. Language-specific profiles must still use the shared protocol semantics and stable wire shapes.

## 3. Capability Profiles

### 3.1 Core Profile

The Core profile is required for every general-purpose SDK:

- Initialize and send `initialized`.
- Connect over AppServer WebSocket.
- Support a custom or low-level transport path for tests and advanced clients.
- Send raw JSON-RPC requests and notifications.
- Correlate JSON-RPC responses and convert JSON-RPC errors.
- Dispatch server-initiated JSON-RPC requests.
- Expose raw AppServer notifications.
- Close or dispose the SDK connection without stopping Hub-managed AppServers.

### 3.2 Hub Bootstrap Profile

The Hub Bootstrap profile is required for local-workspace SDK clients:

- Resolve `~/.craft/hub/hub.lock`.
- Parse Hub lock metadata.
- Reject stale process ids and non-loopback Hub URLs.
- Probe `GET /v1/status`.
- Start `dotcraft hub` when configured.
- Call `POST /v1/appservers/ensure`.
- Require `endpoints.appServerWebSocket` for local AppServer connections.
- Avoid logging Hub tokens or AppServer WebSocket tokens.

Default Chat local bootstrap is the same profile after resolving and ensuring the concrete workspace path `~/.craft/workspaces/chats`. It must not use an empty `workspacePath` or a separate Hub endpoint.

Additional Hub management APIs, such as appserver lookup and event subscription, are optional typed wrappers when the language binding needs them.

### 3.3 Application Profile

The Application profile is the normal application-developer surface:

- High-level local and remote connection helpers.
- Thread start, resume, read, subscribe, and list.
- Turn start, enqueue, and interrupt.
- Input part helpers or typed input models.
- Runtime Dynamic Tool declaration and callback registration.
- Approval and user-input callback support when the SDK advertises those initialize capabilities.
- Raw request escape hatch on the high-level client.

### 3.4 Run Profile

The Run profile is the high-level one-turn application API and is **required for every general-purpose SDK**:

- Subscribe to turn events before starting the turn.
- Expose a blocking run helper that waits for terminal turn state.
- Expose a streaming run helper or async event stream.
- Normalize common thread, turn, item, plan, subagent, and system notifications.
- Merge agent-message deltas and final snapshots without duplicating text.
- Surface failed and cancelled turns as stable SDK errors.
- Support enqueue-on-busy when implemented by the language binding.

### 3.5 App Binding Profile

The App Binding profile covers app-side and application-side helpers over [App Binding](../protocols/app-binding.md):

- Parse or represent App Binding handoffs when the runtime participates in native-app flows.
- Inspect connection and binding requests.
- Complete or revoke app connections.
- Create, cancel, inspect, accept, refresh, revoke, and list thread bindings when a typed wrapper exists.
- Attach runtime Dynamic Tools to accepted bindings.
- Keep app-bound tool channels alive while the app is running.
- Return standard App Binding tool error shapes.

SDKs may expose App Binding methods as generic typed requests first, then add stable DTOs later.

### 3.6 Channel Adapter Profile

The Channel Adapter profile is required only for languages that ship first-party external channel adapters. TypeScript and Python both provide a channel adapter base class today; .NET does not. The hosted-module runtime (manifests, config descriptors, module lifecycle, conformance helpers) is a TypeScript-only sub-profile.

- Channel adapter initialize capability declaration.
- Per-identity message queueing.
- Thread resolution, cache recovery, resume, and fresh-thread reset.
- Slash command routing through AppServer command methods.
- Turn stream reduction with segment boundaries.
- Delivery, channel tool, approval, and heartbeat dispatch.
- Media source normalization for upload-capable channel tools, when the SDK profile exposes such helpers.
- Hosted module manifests, config descriptors, workspace context, lifecycle state, and conformance helpers (TypeScript hosted-module sub-profile).

## 4. Shared API Families

### 4.1 Connection

All SDKs should expose these concepts using language-idiomatic names:

- Local Hub-managed connection from `workspacePath`.
- Local Hub-managed connection to the default Chat workspace.
- Remote AppServer WebSocket connection from `url` plus optional token.
- Low-level transport construction for protocol tests or embedded hosts.
- Client identity fields: name, version, and optional title.
- Forward-compatible capability extension fields.

High-level local connection must not stop Hub or the Hub-managed AppServer when the SDK client closes.

### 4.2 Wire Client

Wire clients must:

- Generate request ids.
- Serialize JSON-RPC 2.0 messages.
- Correlate responses.
- Preserve `error.code`, `error.message`, and optional `error.data`.
- Dispatch notifications by method or expose an async stream.
- Dispatch server requests by method.
- Return `Method not handled` for unregistered server requests unless the protocol defines a default response.

### 4.3 Thread And Turn

SDK thread and turn helpers should preserve AppServer naming and payload semantics:

- `thread/start`
- `thread/resume`
- `thread/read`
- `thread/list`
- `thread/subscribe`
- `thread/unsubscribe`
- `thread/archive`
- `thread/delete`
- `thread/mode/set`
- `turn/start`
- `turn/enqueue`
- `turn/interrupt`

Language bindings may expose only a subset as typed wrappers, but raw request access must cover the rest.

### 4.4 Runtime Dynamic Tools

Runtime Dynamic Tools use the same model in every SDK:

- Tool specs are declared on `thread/start.dynamicTools` or `thread/resume.dynamicTools`.
- SDK-local handlers are not sent over the wire.
- `item/tool/call` is dispatched by `threadId`, optional `namespace`, and `tool`.
- Missing handlers return `UnsupportedTool`.
- Handler exceptions return `AdapterToolCallFailed`.
- Tool handlers are responsible for argument validation and app-level authorization.

### 4.5 Callbacks

When an SDK advertises callback support, it must be able to answer the matching server request:

- `approvalSupport` requires `item/approval/request` handling.
- `requestUserInputSupport` requires `item/tool/requestUserInput` handling.
- Channel adapters must answer `ext/channel/heartbeat` with `{}`.

Non-interactive SDK clients may provide documented fallback behavior, but production examples must encourage explicit handlers for approval-sensitive flows.

### 4.6 Error Model

SDKs should expose stable error codes for common AppServer and SDK cases:

- initialization failure;
- transport closed;
- thread not found;
- thread not active;
- turn already in progress;
- turn failed;
- turn cancelled;
- approval timeout;
- Hub discovery or request failure.

Exact class names and inheritance are language binding concerns.

### 4.7 Media Source Handling

Media source handling is an SDK-local normalization layer for channel tools and SDK helpers that need to send user-provided media to an external platform.

It does not change the AppServer wire shape, including `ext/channel/toolCall`, and it does not require model-visible tool schemas to change. Existing tool arguments may continue to use channel-specific names such as a path, URL, or base64 field when those names are already part of the tool contract.

The shared media source semantics are:

- A host path means a file path readable by the SDK process handling the tool call.
- A base64 source is decoded by the SDK before platform upload.
- A URL source is passed through only when the channel tool and platform allow URL input.
- Preparation resolves file name, media type, byte size, and readable bytes when bytes are required.
- Preparation fails with a stable SDK/tool error when the source is missing, unreadable, invalid, not allowed, or larger than the applicable channel limit.
- Platform-specific upload forms are produced after normalization. Examples include bytes/form-data, temporary files, platform URLs, or platform-specific inline data URIs.

SDKs must not assume that a downstream messaging platform, gateway, or helper process can read the same filesystem path as the SDK process. Channel tool descriptions should describe the expected source argument from the agent's perspective and avoid exposing adapter-internal deployment details.

## 5. Capability Matrix

Status values:

- **Typed**: language binding has a named high-level wrapper.
- **Generic**: helper exists, but request/response shape is caller-provided.
- **Raw**: available through raw request or raw notification APIs only.
- **Callback**: SDK dispatches the server-initiated request.
- **Profile**: supported by an optional language-specific profile.
- **Partial**: some methods in the capability family are typed or generic, while others remain raw or unsupported.
- **Gap**: no support beyond what the lower layer incidentally exposes.

Parity Target applies to every general-purpose SDK (TypeScript, .NET, Python) unless the row names a single-language profile. Cells record the current status per language; cells below the target are tracked parity debt closed by the SDK alignment milestones.

| Capability | Owning Spec | TypeScript | .NET | Python | Parity Target |
|------------|-------------|------------|------|--------|---------------|
| Initialize / initialized | AppServer | Typed | Typed | Typed | Required |
| Raw AppServer request | AppServer | Typed | Typed | Typed | Required |
| Raw notification consumption | AppServer | Typed | Typed | Typed | Required |
| Server request dispatch | AppServer | Typed | Typed | Typed | Required |
| Stdio or stream JSON-RPC transport | AppServer | Typed | Typed | Typed | Required low-level |
| WebSocket JSON-RPC transport | AppServer | Typed | Typed | Typed | Required |
| Custom transport injection | SDK | Raw constructor | Typed high-level | Typed | Required low-level |
| Hub lock discovery and validation | Hub | Typed | Typed | Typed | Required local |
| Hub startup | Hub | Typed | Typed | Typed | Required local |
| AppServer ensure | Hub | Typed | Typed | Typed | Required local |
| Default Chat AppServer ensure | Hub | Typed | Typed | Typed | Required local |
| AppServer lookup by workspace | Hub | Gap | Typed | Gap | Optional typed |
| Hub status | Hub | Typed | Gap | Gap | Optional typed |
| Hub SSE events | Hub | Typed | Gap | Gap | Optional typed |
| Thread start | AppServer | Typed | Typed | Typed | Required |
| Thread resume | AppServer | Typed | Typed | Typed | Required |
| Thread read | AppServer | Typed | Typed | Typed | Required |
| Thread subscribe | AppServer | Typed | Typed | Typed | Required |
| Thread list | AppServer | Typed | Typed | Typed | Required application |
| Thread unsubscribe | AppServer | Typed | Typed | Typed | Required application |
| Thread archive/delete | AppServer | Typed | Typed | Typed | Optional typed |
| Thread mode set | AppServer | Typed | Typed | Typed | Optional typed |
| Turn start | AppServer | Typed | Typed | Typed | Required |
| Turn enqueue | AppServer | Typed | Typed | Typed | Required |
| Turn interrupt | AppServer | Typed | Typed | Typed | Required |
| High-level run | SDK | Typed | Typed | Typed | Required Run profile |
| Streaming run events | SDK/AppServer | Typed | Typed | Typed | Required Run profile |
| Delta/snapshot text merge | SDK/Session | Typed | Typed | Typed | Required Run profile |
| Approval callback | AppServer | Callback | Callback | Callback | Required when advertised |
| User-input callback | AppServer | Callback | Callback | Callback | Required when advertised |
| Runtime Dynamic Tool declaration | AppServer | Typed | Typed | Typed | Required |
| Runtime Dynamic Tool callback | AppServer | Callback | Callback | Callback | Required |
| Model list | AppServer | Typed | Typed | Typed | Optional typed |
| App Binding handoff parse | App Binding | Typed | Typed | Typed | App Binding profile |
| App Binding request inspect | App Binding | Raw | Typed | Typed | App Binding profile |
| App Binding accept | App Binding | Typed | Typed | Typed | App Binding profile |
| App Binding attach tools | App Binding | Typed | Typed | Typed | App Binding profile |
| App Binding app list/view | App Binding | Typed | Typed | Typed | Optional typed |
| App Binding connection start/revoke/status | App Binding | Typed | Typed | Typed | Optional typed |
| Thread app bindings list/revoke/refresh | App Binding | Typed | Typed | Typed | Optional typed |
| App Binding tool error shape | App Binding | Typed | Typed | Typed | Required App Binding profile |
| Channel adapter base class | External Channel Adapter | Profile | Gap | Profile | TypeScript + Python profile |
| Channel runtime reducers/dispatchers | External Channel Adapter | Profile | Gap | Profile | TypeScript + Python profile |
| Media source normalization for channel tools | SDK | Profile | Gap | Gap | TypeScript profile |
| Hosted channel module manifest | External Channel Adapter | Profile | Gap | Gap | TypeScript profile |
| Module conformance helper | SDK | Profile | Gap | Gap | TypeScript profile |
| SDK conformance fixtures | SDK | Typed | Typed | Typed | Required for new wrappers |

When a status changes, update this table and the relevant language binding spec in the same change.

## 6. Language Binding Specs

Each language binding spec must document:

- Package identity and versioning.
- Runtime baseline.
- Public entry points, exports, or namespaces.
- Idiomatic high-level client shape.
- Language-specific profiles and explicit gaps.
- Validation commands.
- Publishing policy when applicable.

Current binding specs:

- [TypeScript SDK Binding](typescript.md)
- [.NET SDK Binding](dotnet.md)
- [Python SDK Binding](python.md)

All three are general-purpose SDKs and must satisfy the Core, Hub Bootstrap, Application, Run, and App Binding profiles at the parity targets in §5. TypeScript and Python additionally provide the Channel Adapter profile.

## 7. Testing And Conformance

Shared conformance expectations:

- Initialize request shape and `initialized` notification.
- JSON-RPC response correlation and error conversion.
- Transport framing for each supported transport.
- Hub lock validation and bearer authorization.
- Thread and turn request shapes for typed wrappers.
- Runtime Dynamic Tool declaration, dispatch, missing-handler fallback, and handler exception fallback.
- App Binding method shape for typed or generic helpers.
- Run profile event order, text merge, failure, cancellation, abort, and enqueue behavior.
- Channel profile queueing, thread resolution, command routing, delivery/tool/approval dispatch, lifecycle, and conformance helpers.
- Media source normalization for channel tools where the profile is implemented.

Language binding specs own the exact commands.

## 8. Security

SDKs must:

- Treat Hub tokens, AppServer WebSocket tokens, handoff tokens, and request tokens as secrets.
- Validate Hub locks before trusting them.
- Avoid logging full token-bearing URLs.
- Document explicit approval handlers for production use.
- Avoid sandboxing claims for dynamic tool handlers. Tool handlers execute in the application process.
- Preserve App Binding authority boundaries. DotCraft validation is not a substitute for app-side authorization.

## 9. Versioning And Compatibility

SDK changes are classified by the shared semantic contract, not by identical language syntax.

Breaking changes include:

- Changing a stable SDK error code.
- Removing a required Core, Hub, Application, Run, or App Binding capability from a language that had advertised the profile.
- Changing a typed wrapper's wire method or payload shape incompatibly.
- Changing callback fallback behavior in a way that can block non-interactive clients.

Non-breaking changes include:

- Adding typed wrappers for raw-only rows.
- Adding optional DTO fields that pass through server data.
- Adding language-specific helpers that preserve existing wire behavior.

## 10. Acceptance Contract

A complete shared SDK specification state satisfies:

- `specs/sdk/sdk.md` defines the shared semantic SDK contract.
- Language binding specs live under `specs/sdk/`.
- Existing root SDK spec paths are compatibility pointers only.
- Cross-spec links point to the new SDK directory.
- Capability parity is tracked in this document's matrix.
- Language-specific SDK designs remain documented without duplicating shared protocol semantics.
