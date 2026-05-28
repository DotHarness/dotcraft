# DotCraft SDK Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-05-21 |
| **Related Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Hub Architecture](../runtime/hub-architecture.md), [App Binding](../protocols/app-binding.md), [External Channel Adapter](../protocols/external-channel-adapter.md), [Session Core](../core/session-core.md), [TypeScript SDK Binding](typescript.md), [.NET SDK Binding](dotnet.md) |

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
- Hub HTTP endpoint semantics. See [Hub Architecture](../runtime/hub-architecture.md).
- App Binding product semantics. See [App Binding](../protocols/app-binding.md).
- Session persistence, turn lifecycle, item payload semantics, or agent internals. See [Session Core](../core/session-core.md).
- Exact package exports, class names, namespace layout, or publishing workflows. Those live in the language binding specs.

## 2. Design Principles

### 2.1 One Semantic Contract, Idiomatic Bindings

SDKs should expose the same DotCraft capabilities and behavior where the host language and runtime make that useful. They do not need identical names or object models. A .NET API should feel like .NET; a TypeScript API should feel like TypeScript.

### 2.2 AppServer And Hub Are Authoritative

SDKs are clients. They must not duplicate server-side thread state machines, queue semantics, approval policy, App Binding validation, model catalog resolution, or persistence rules.

### 2.3 Raw Escape Hatch Is Required

Every general-purpose SDK must expose a raw AppServer request API and a raw notification stream or registration API so callers can use newly added protocol methods before typed wrappers exist.

### 2.4 Typed Wrappers Are Traceable

Every typed SDK wrapper must map to one or more rows in this spec's capability matrix and to the owning protocol spec. If a language adds a typed wrapper first, this shared spec must be updated in the same change.

### 2.5 Language-Specific Profiles Are Allowed

Some SDK surfaces are naturally language-specific. Examples:

- TypeScript owns the first-party external channel module runtime because current channel modules are Node packages.
- .NET owns native App Binding handoff helpers because first validating native-app integrations are .NET desktop apps.
- Publishing, packaging, and runtime baselines remain language binding concerns.

Language-specific profiles must still use the shared protocol semantics and stable wire shapes.

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

The Run profile is required for SDKs that advertise a high-level one-turn application API:

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

The Channel Adapter profile is required only for languages that ship first-party external channel modules:

- Channel adapter initialize capability declaration.
- Per-identity message queueing.
- Thread resolution, cache recovery, resume, and fresh-thread reset.
- Slash command routing through AppServer command methods.
- Turn stream reduction with segment boundaries.
- Delivery, channel tool, approval, and heartbeat dispatch.
- Hosted module manifests, config descriptors, workspace context, lifecycle state, and conformance helpers.

The current required language for this profile is TypeScript.

## 4. Shared API Families

### 4.1 Connection

All SDKs should expose these concepts using language-idiomatic names:

- Local Hub-managed connection from `workspacePath`.
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

## 5. Capability Matrix

Status values:

- **Typed**: language binding has a named high-level wrapper.
- **Generic**: helper exists, but request/response shape is caller-provided.
- **Raw**: available through raw request or raw notification APIs only.
- **Callback**: SDK dispatches the server-initiated request.
- **Profile**: supported by an optional language-specific profile.
- **Partial**: some methods in the capability family are typed or generic, while others remain raw or unsupported.
- **Gap**: no support beyond what the lower layer incidentally exposes.

| Capability | Owning Spec | TypeScript | .NET | Parity Target |
|------------|-------------|------------|------|---------------|
| Initialize / initialized | AppServer | Typed | Typed | Required |
| Raw AppServer request | AppServer | Typed | Typed | Required |
| Raw notification consumption | AppServer | Typed | Typed | Required |
| Server request dispatch | AppServer | Typed | Typed | Required |
| Stdio or stream JSON-RPC transport | AppServer | Typed | Typed | Required low-level |
| WebSocket JSON-RPC transport | AppServer | Typed | Typed | Required |
| Custom transport injection | SDK | Raw constructor | Typed high-level | Required low-level |
| Hub lock discovery and validation | Hub | Typed | Typed | Required local |
| Hub startup | Hub | Typed | Typed | Required local |
| AppServer ensure | Hub | Typed | Typed | Required local |
| AppServer lookup by workspace | Hub | Gap | Typed | Optional typed |
| Hub status | Hub | Typed | Gap | Optional typed |
| Hub SSE events | Hub | Typed | Gap | Optional typed |
| Thread start | AppServer | Typed | Typed | Required |
| Thread resume | AppServer | Typed | Typed | Required |
| Thread read | AppServer | Typed | Typed | Required |
| Thread subscribe | AppServer | Typed | Typed | Required |
| Thread list | AppServer | Typed | Raw | Required application |
| Thread unsubscribe | AppServer | Typed | Raw | Required application |
| Thread archive/delete | AppServer | Typed | Raw | Optional typed |
| Thread mode set | AppServer | Typed | Raw | Optional typed |
| Turn start | AppServer | Typed | Typed | Required |
| Turn enqueue | AppServer | Typed | Typed | Required |
| Turn interrupt | AppServer | Typed | Typed | Required |
| High-level run | SDK | Typed | Gap | Required Run profile |
| Streaming run events | SDK/AppServer | Typed | Raw | Required Run profile |
| Delta/snapshot text merge | SDK/Session | Typed | Gap | Required Run profile |
| Approval callback | AppServer | Callback | Gap | Required when advertised |
| User-input callback | AppServer | Callback | Gap | Required when advertised |
| Runtime Dynamic Tool declaration | AppServer | Typed | Typed | Required |
| Runtime Dynamic Tool callback | AppServer | Callback | Callback | Required |
| Model list | AppServer | Raw | Typed | Optional typed |
| App Binding handoff parse | App Binding | Gap | Typed | Language-specific native helper |
| App Binding request inspect | App Binding | Raw | Generic | App Binding profile |
| App Binding accept | App Binding | Typed | Generic | App Binding profile |
| App Binding attach tools | App Binding | Typed | Generic | App Binding profile |
| App Binding app list/view | App Binding | Typed | Raw | Optional typed |
| App Binding connection start/revoke/status | App Binding | Typed | Partial | Optional typed |
| Thread app bindings list/revoke/refresh | App Binding | Typed | Raw | Optional typed |
| App Binding tool error shape | App Binding | Typed | Typed | Required App Binding profile |
| Channel adapter base class | External Channel Adapter | Profile | Gap | TypeScript profile |
| Channel runtime reducers/dispatchers | External Channel Adapter | Profile | Gap | TypeScript profile |
| Hosted channel module manifest | External Channel Adapter | Profile | Gap | TypeScript profile |
| Module conformance helper | SDK | Profile | Gap | TypeScript profile |
| SDK conformance fixtures | SDK | Typed | Typed | Required for new wrappers |

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
