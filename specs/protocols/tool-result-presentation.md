# DotCraft Tool Result Presentation Specification

| Field | Value |
|---|---|
| Version | 2.0 |
| Status | Normative |
| Date | 2026-07-14 |
| Parent | [Tool Architecture](../architecture/tools-architecture.md) |
| Wire protocol | [AppServer Protocol](appserver-protocol.md) |

## 1. Scope

This specification defines two independent enhancement paths for tool results:

- stable MCP Apps `io.modelcontextprotocol/ui`, version `2026-01-26`, for server-provided interactive resources;
- the trusted Desktop `ToolRendererRegistry` for local Core renderers.

Interactive presentation is optional. Every model-visible tool result MUST retain useful model/text fallback content. This specification does not grant tools or views additional execution authority.

## 2. Result audiences

| Field | Audience | Rule |
|---|---|---|
| `content` / `contentItems` | model and text clients | Normalized text/image fallback used for provider history. |
| `structuredContent` | client/view | Structured application data; never automatically inserted into model history. |
| `_meta` | host/view | Sanitized private metadata; never inserted into model history. |
| raw MCP result | trusted MCP App host | Preserved for a live view after limits and sanitization; not provider history. |

History reconstruction uses normalized model content only. Generic rendering, logs, traces, compaction, resume, and fork MUST NOT serialize `structuredContent`, `_meta`, or raw MCP data into provider context.

## 3. MCP Apps baseline and discovery

DotCraft implements stable MCP Apps `2026-01-26`. Desktop uses the official host-side `AppBridge` from exact package version `@modelcontextprotocol/ext-apps@1.7.4`. Core uses validated wrappers over raw MCP metadata and does not require a preview C# Apps package.

Every DotCraft MCP client initialization advertises:

```json
{
  "extensions": {
    "io.modelcontextprotocol/ui": {
      "mimeTypes": ["text/html;profile=mcp-app"]
    }
  }
}
```

This MCP capability belongs to the MCP session and does not change when Desktop connects or disconnects.

Tool linkage is read only from nested tool `_meta.ui`:

- `resourceUri` is an absolute `ui://` URI;
- omitted `visibility` means `model` and `app`;
- `[]` means neither audience;
- any unknown visibility value invalidates the declaration and exposes the tool to neither audience.

App-only tools remain registered but are not projected to the model. Model-only tools cannot be called by a view.

The host reads the declared resource from the exact owning MCP server generation. The returned resource MUST match the requested URI, use `text/html;profile=mcp-app`, and contain exactly one of text HTML or valid base64 blob content. Resource `_meta.ui` may declare CSP, permissions, domain, and border preference. Stable tool and resource metadata are not interchangeable.

## 4. Live-only View authority

Only a terminal `McpToolCall` delivered live to the same AppServer connection that advertised `mcpApps` may open an MCP App. AppServer attaches a non-persistent `mcpApp.available = true` projection only to that live `item/completed` delivery.

`thread/read`, replay, resume, navigation reload, Desktop restart, a new AppServer connection, and MCP generation replacement always use the generic frozen result. Neither eligibility nor `viewHandle` is persisted. A same-named reconnected server never inherits prior view authority.

`mcpApp/view/open` accepts only `threadId` and `itemId`. Core validates the live delivery, terminal item, current snapshot, definition, runtime binding, authority, App visibility, and MCP generation before atomically reading the UI resource and issuing a random opaque handle.

Each handle is connection-owned and binds immutable thread/item, server/origin, generation, definition/runtime binding, snapshot/authority revision, raw source tool id, and resource URI. Desktop and the iframe cannot supply or override those values. Disconnect, archive/delete, generation replacement, revoke, plugin disable, configuration replacement, or view close invalidates the handle immediately.

## 5. Host and AppBridge behavior

The host implements the stable initialize/initialized lifecycle, ping, teardown, tool-input and tool-result notifications, host-context updates, same-server tools/resources, logging, safe links, `ui/message`, and `ui/update-model-context`.

Supported display modes are inline and fullscreen. Fullscreen reuses the same view handle. Picture-in-picture, persisted widget state, historical reconstruction, dedicated domains, and iframe permissions are not supported.

Theme, locale, time zone, dimensions, display mode, and standard CSS variables are supplied through host context. Size and host-context updates are coalesced to at most ten per second.

The host renders through a trusted isolated-origin proxy and an inner sandboxed iframe. The inner document has no preload, Node, Electron, filesystem, shell, generic IPC, parent DOM, or undeclared network access. DotCraft applies a restrictive default CSP and only adds validated declared domains to their matching directives. Resource `domain` is attribution metadata and does not select a real origin.

M2 validates permission declarations but grants none. Camera, microphone, geolocation, and clipboard-write remain denied. `ui/open-link` permits HTTPS, `mailto`, and explicit loopback HTTP only; `file`, `data`, `javascript`, and custom schemes are rejected.

## 6. View actions

Same-server `tools/call` resolves the original MCP source identity from the handle and current snapshot. It uses `ToolInvocationAudience.App`, a server-generated `app_<guid>` call id, and the common schema, lease, policy, hook, approval, timeout, normalization, and result-limit dispatcher. Cross-server, model-only, stale, revoked, and unavailable calls are rejected.

An App call creates no Turn, Session tool item, or provider-history entry. Its bounded raw MCP result is returned only to the view. Safe tracing may record the invocation origin without recording private result audiences.

An accepted `ui/message` contains `role: "user"` and one text content block. It starts a source-marked MCP App Turn immediately when idle or enters the normal queued-input path while another Turn is active. The view cannot forge user/channel identity.

Each live view has one last-write-wins pending model context. Empty content clears it. An accepted `ui/message` consumes only its originating view's value; an ordinary user Turn atomically consumes all pending values for the thread. The value is injected once as bounded, untrusted transient context and is not an independent Session item or persistent configuration. Teardown, revoke, disconnect, or thread closure discards unconsumed context.

Unknown input content blocks reject the entire message/context update. Supported text/image blocks are safely materialized; structured content may be injected only as bounded JSON.

## 7. Limits and lifecycle

Normative M2 limits:

- four concurrent tool calls and 60 calls per view per minute;
- 16 KiB per `ui/message` or model-context update;
- 2 MiB per resource or raw result;
- 256 KiB per bridge JSON message;
- eight active views per thread and 32 per connection;
- 8 KiB per log entry and 60 logs per view per minute.

The live state sequence is unavailable → loading → initializing → ready-inline/fullscreen → tearing-down → closed. Resource/protocol failure produces generic fallback. Session loss produces offline; authority removal produces revoked. All terminal paths remove listeners, rate-limit state, and pending context.

## 8. Core-only local renderer registry

The local renderer registry is independent of MCP Apps. In M2 only trusted Core/Desktop renderers may register. Plugin and third-party code loading is deferred to a separate trust specification.

Selection uses an ordinal server-projected `PresentationId` plus matching safe Core provenance. Each renderer validates its bounded options. Duplicate ids fail registry construction. Unknown ids, invalid options, missing presentation, or provenance mismatch use the generic card. Remote MCP metadata, Dynamic declarations, plugin payloads, tool names, and results cannot select local code.

M2 migrates every existing special renderer family: CreatePlan, Cron, SkillManage, SkillView, all SubAgent operations, shell, WriteFile/EditFile and streaming diff, WebSearch/WebFetch, RequestUserInput, ReadFile, TodoWrite/UpdateTodos, deferred tool search, and generic fallback. Conversation cards, pinning, grouping, and labels consume registry render plans rather than branching on tool names.

Trusted historical Core Native items that lack a presentation descriptor MAY use a read-only provenance projector keyed by safe Core source identity. Non-Core or incomplete history is never guessed.

## 9. Legacy App Binding isolation

The private App Binding iframe protocol is a Legacy-only path until its scheduled removal. Its `interactiveToolUi`, `ui/resource/read`, `ui/tool/call`, widget-state APIs, custom bridge token/version, picture-in-picture, and private metadata MUST NOT be used by MCP Apps.

Only trusted Legacy App Binding provenance may activate that path. Runtime Dynamic tools and ordinary MCP `_meta` cannot trigger it. MCP Apps never accepts private widget fields, `_meta.ui` from Dynamic results, or Legacy resource authority.

## 10. Acceptance

- stable metadata, visibility, resource, and AppBridge fixtures pass;
- only live terminal MCP items on the owning capable connection can open a view;
- same-server authority, approval, audience separation, limits, and teardown are enforced;
- inline/fullscreen, generic fallback, safe links, messages, and one-shot context work;
- historical and reconnected items remain generic;
- all hard-coded Core renderer families resolve through the provenance-gated registry;
- MCP Apps and Legacy App Binding cannot activate or authorize each other.
