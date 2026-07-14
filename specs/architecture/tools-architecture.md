# Tool Architecture

| Field | Value |
|---|---|
| Version | 0.1.0 |
| Status | Proposed |
| Date | 2026-07-14 |
| Scope | Agent tools, authority binding, execution, session projection, and interactive presentation |
| Related | [Session Core](session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md), [App Binding](../protocols/app-binding.md), [Tool Result Presentation](../protocols/tool-result-presentation.md), [Plugin Architecture](plugin-architecture.md) |

## 1. Purpose

This specification defines the target architecture for every tool that can be made available to a DotCraft agent. It is the architectural source of truth for the tool refactor milestones. It separates concerns that are currently coupled across native tool providers, MCP, Runtime Dynamic Tools, App Binding, Plugin Functions, Teams, social channels, and Desktop rendering.

The architecture has four goals:

1. one source-neutral registration and dispatch pipeline;
2. explicit authority, exposure, identity, and audience boundaries;
3. consistent AppServer tool semantics across server and clients;
4. standards-based interactive tool UI through MCP Apps, while retaining DotCraft-specific App Binding as a small authorization and connection control plane.

This document describes the target state. Existing protocol documents continue to describe released behavior until their owning milestone updates them. When a milestone adopts a conflicting rule, this document is authoritative for the intended replacement and the affected protocol specification MUST be updated in the same implementation change.

### 1.1 Specification ownership

| Specification | Owns |
|---|---|
| this document | cross-source identity, layer boundaries, exposure, authority, execution, result audiences, and presentation invariants |
| `session-core.md` | Thread/Turn/Item persistence, generic lifecycle, event ordering, archive/resume/fork semantics |
| `appserver-protocol.md` | JSON-RPC method names, DTOs, capability negotiation, notifications, and transport serialization |
| `app-binding.md` | app discovery, principal/connection handoff, thread binding state, capability authorization, revoke/rebind/audit |
| MCP and MCP Apps standards | MCP capability, tool/resource/result, Apps metadata, and bridge wire contracts |
| plugin/SDK/client specifications | source authoring, language APIs, and UI mapping of the architecture/protocol contracts |

Downstream specifications MUST reference these semantics rather than copy and locally redefine them. Wire field details shown here or in milestone plans are design requirements; the owning protocol specification becomes authoritative for their final serialization when that milestone is implemented.

## 2. Non-goals

This specification does not:

- replace the complete AppServer or Session Core architecture;
- define general app discovery, marketplace, or plugin installation UX;
- make every tool source use the same transport or session item type;
- require interactive UI for a tool to be correct;
- expose App Binding credentials or external app executables to the agent runtime;
- preserve wire compatibility with the pre-refactor Dynamic Tool or App Binding tool-attachment protocols.

## 3. Normative language

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are normative.

## 4. Canonical terminology

| Term | Definition |
|---|---|
| **Tool Source** | A component that contributes tool registrations. Examples: Core native tools, a plugin, an MCP server, or a Runtime Dynamic client. |
| **Tool Definition** | An immutable source-qualified semantic definition: identity, model-facing name, description, schemas, hints, and presentation link. |
| **Tool Runtime Binding** | A live or stub binding from a definition to an executor, lifecycle lease, authority reference, availability, and revision. |
| **Tool Registration** | The resolved source-neutral join of a definition, runtime-binding reference, exposure defaults, and safe provenance for snapshot planning. |
| **Tool Runtime** | The executor implementation used by a live runtime binding. |
| **Tool Authority** | The server-authoritative decision that a registration is permitted for a thread and invocation context. |
| **Tool Exposure** | Whether and how a permitted tool is published to the model. |
| **Effective Tool Snapshot** | The immutable set of registrations and model-visible definitions selected for one Turn. |
| **Tool Invocation** | One model or host request to execute a registered tool. |
| **Tool Execution Result** | Source-neutral result containing model content, client-only structured content, host-only metadata, success, and stable error information. |
| **Presentation Descriptor** | Trusted metadata that selects or configures a local renderer. It is distinct from MCP Apps UI resource metadata. |
| **Runtime Dynamic Tool** | A thread-scoped callback implemented by the connected AppServer client. It is connection-owned and not a general external app integration mechanism. |
| **Binding MCP** | An MCP server connection authorized for one App Binding and added independently to one thread. |

The term **Dynamic Tool** without the **Runtime** qualifier SHOULD be avoided in new design and code because App Binding tools are no longer transported through that mechanism.

## 5. Architectural layers

The architecture has five layers: Definition, Binding/Authority, Exposure, Execution, and Presentation. Source discovery and provenance belong to the Definition layer; `ToolRegistration` is the normalized boundary object passed from Definition into the remaining layers. Implementations MAY combine classes, but MUST preserve these semantic boundaries and invariants.

```text
Source -> Definition/Registration -> Binding/Authority -> Exposure -> Execution -> Presentation
```

### 5.1 Definition and source

The source discovers or declares a definition and owns source-specific lifecycle state. A source MUST NOT decide final model visibility solely by itself.

Canonical sources are:

| Source kind | Lifecycle | Executor owner | Typical examples |
|---|---|---|---|
| Core Native | process/workspace | DotCraft server | file, web, subagent |
| Plugin Native | plugin enablement | trusted in-process plugin | Agent Teams, plugin-contributed functions, managed social tools |
| MCP | MCP connection/session | MCP server | workspace, thread, plugin, or binding MCP |
| Runtime Dynamic | AppServer connection + thread | connected AppServer client | Desktop thread management, client-owned run callbacks |

### 5.2 Registration boundary

A source contribution MUST separate durable semantic definition from live executability:

- `ToolDefinition` contains `ToolDefinitionId`, `ToolName`, `SourceToolId`, schemas, safe source provenance, approval/policy hints, and optional presentation link;
- `ToolRuntimeBinding` contains `RuntimeBindingId`, definition reference, executor handle, lifecycle/connection lease, `AuthorityRef`, availability, and binding revision;
- `ToolRegistration` is the resolved planning join and contains references/revisions rather than persisting a live executor inside the definition.

This split MUST represent a stable definition with a replaced MCP connection, a durable grant with an offline executor, and readable historical items with no current runtime. Source-specific opaque state and live executor handles are never model-visible and never stored in durable definition snapshots.

The runtime registry MUST retain executable registrations that are hidden from the model. Model-visible definition generation is a projection of the runtime registry, not the registry itself. The source catalog, runtime registry, per-thread effective snapshot, local renderer registry, and MCP Apps resource broker are separate indexes with different owners; none may be used as an alias for another.

### 5.3 Binding and authority

Authority determines whether a registration is usable by a thread and whether a particular invocation can dispatch. Authority inputs include thread configuration, plugin state, App Binding state, mode policy, approval policy, MCP annotations, and connection health. Source-owned business invariants, such as Teams Mission membership and task assignment, remain the native service's execution-boundary responsibility and are not required to become a generic authority record.

Authorization MUST be server-authoritative. Arguments, renderer metadata, an iframe, or a remote source MUST NOT expand authority.

`IToolAuthorityEvaluator` is required when execution authority has a live, independently revocable reference or revision that is not fully owned by the source service or binding lease. A source that declares such authority MUST fail closed when it cannot be resolved. Native services may own execution-boundary validation for their own business state.

### 5.4 Exposure

Exposure determines publication to the model after authority is established. The canonical values are:

| Value | Model publication |
|---|---|
| `Direct` | Definition is included directly in the model tool list. |
| `Deferred` | Definition is discoverable through the deferred-tool mechanism and not included directly. |
| `DirectModelOnly` | Definition is directly visible as a normal model tool but excluded from the nested code-mode tool surface. |
| `Hidden` | Definition is not visible to the model; the executor MAY remain callable by an authorized host path. |

`ToolExposure` controls model and code-mode publication only. Host/app invocation eligibility is a separate invocation-audience/capability decision. App visibility MUST NOT introduce additional `ToolExposure` values. For MCP Apps, the stable visibility contract maps independently to model-visible and app-callable decisions.

### 5.5 Execution

All sources MUST converge on this ordered source-neutral dispatch pipeline:

1. resolve the provider call name to exact canonical identity in the Turn snapshot;
2. verify snapshot exposure and invocation audience;
3. check binding lease, live authority, and policy;
4. validate arguments against the input schema;
5. apply mode/thread/native guards and MCP annotation policy;
6. run `PreToolUse` hooks;
7. resolve approval;
8. project the source-appropriate started lifecycle;
9. execute through `IToolRuntime`, classifying timeout and caller cancellation separately;
10. normalize result audiences, require model fallback where applicable, and enforce result limits;
11. project the terminal Session lifecycle;
12. run `PostToolUse` or `PostToolUseFailure` hooks.

Source adapters MAY add transport-specific lifecycle behavior, but MUST NOT duplicate common approval, result audience, or error normalization rules.

Provider-hosted capabilities such as provider-native image generation are not local tools and MUST NOT be represented by `IToolRuntime` or dispatched through the local tool pipeline. Snapshot planning records them in a separate provider-capability plan, and the provider adapter projects their declarations and specialized Session items. Their result still obeys the audience and persistence rules applicable to their specialized item type.

### 5.6 Presentation

Presentation is optional enhancement after correctness. Every model-visible tool result MUST have a usable model/text fallback. Presentation has two independent mechanisms:

- **MCP Apps**, for server-provided interactive resources and bidirectional host/view communication;
- **local renderer registry**, for trusted DotCraft/Desktop renderers selected by server-controlled provenance and `PresentationId`.

Remote MCP metadata MUST NOT select an arbitrary local renderer.

## 6. Identity model

Tool identity is intentionally split:

| Identity | Purpose | Stability boundary |
|---|---|---|
| `SourceToolId` | Real identifier understood by the source/executor | Source connection or persisted source contract |
| `ToolDefinitionId` | DotCraft source-qualified semantic definition identity | Stable across reconnects while semantic identity is unchanged |
| `ToolName(namespace, name)` | Canonical model and router identifier | Effective snapshot and persisted invocation history |
| `RuntimeBindingId` | Live executor/authority lease identity | One binding/session generation |
| `PresentationId` | Trusted renderer selection | Core/Desktop presentation contract |

`ToolName` MUST be a true composite value with ordinal, case-sensitive equality. Two tools MAY have the same `name` in different namespaces. Deferred lookup MUST preserve the namespace.

Source adapters SHOULD choose stable namespaces. MCP canonical identity is `ToolName("mcp__" + declaredServerName, rawToolName)`; the provider projection uses the normalized `mcp__server__tool` form after 64-byte normalization, legal-character cleanup, and deterministic SHA-1 12-character suffixing for truncation/collision cases. The declared server name and raw tool name remain available as source identity and provenance even when the provider projection is sanitized. The effective snapshot keeps the exact mapping `ToolName -> (ToolDefinitionId, RuntimeBindingId, SourceToolId)`. MCP `tools/call` always receives the original `SourceToolId`; neither Desktop nor an iframe may reconstruct it from a canonical name. The generic registry MUST quarantine every registration participating in a remaining duplicate canonical `ToolName`, retain non-conflicting registrations, and emit a diagnostic containing both safe provenances; it MUST NOT use source order or last-write-wins replacement.

Provider/model call identifiers and Session item identifiers are different identities. They MUST be stored and projected separately and MUST survive resume, fork, compaction, and history reconstruction without being substituted for one another.

## 7. Core contracts

Milestone 1 establishes the following conceptual contracts. Exact C# record members may evolve during implementation, but their responsibilities MUST remain separate.

| Contract | Responsibility |
|---|---|
| `ToolName`, `SourceToolId`, `ToolDefinitionId`, `RuntimeBindingId` | Typed canonical, source, semantic-definition, and live-binding identities. |
| `IToolSource` | Contribute definitions and runtime bindings for a planning context. |
| `ToolDefinition` | Immutable source-qualified semantic definition. |
| `ToolRuntimeBinding` | Executor, lifecycle lease, authority, availability, and revision. |
| `ToolRegistration` | Resolve a definition and binding reference for planning. |
| `IToolRuntime` | Execute one authorized invocation using an invocation context. |
| `IToolBindingLease` | Perform live availability/revocation/generation checks for a binding. |
| `IToolAuthorityEvaluator` | Evaluate a source-declared live authority reference when the source has independently revocable authority. It is optional only when the source service or lease owns all live validation. |
| `IToolDispatcher` | Apply the common invocation pipeline and dispatch to the selected runtime. |
| `ToolPlanningContext` | Immutable inputs used to assemble the next Turn snapshot, including trusted `ToolPlanningThreadKind`. |
| `ToolInvocationContext` | Thread, Turn, call, cancellation, approval, and authority inputs for dispatch. |
| `ToolExecutionResult` | Normalized result and stable failure information. |
| `ToolError` | Stable error code, English fallback, and optional structured parameters. |
| `EffectiveToolSnapshot` | Immutable per-Turn registration/index/model-definition set. |
| `ToolPresentationDescriptor` | Trusted local `PresentationId` plus bounded renderer options. It contains no free-form renderer selector. |
| `ProviderHostedCapabilityPlan` | Provider-adapter declarations that are not local `IToolRuntime` tools. |

`ToolPlanningThreadKind` is a trusted Session-derived classification with values `UserTopLevel`, `ModuleManaged`, `SubAgentChild`, `Unattended`, `Internal`, and `Unknown`. It is derived once when constructing `ToolPlanningContext` from persisted thread origin/source/visibility/configuration. Sources MUST treat `Unknown` as ineligible for privileged entrypoint tools and MUST NOT replace this classification with source-local channel-name denylists.

Modules contribute tools through `GetToolSources()` and the typed source, definition, binding, registration, and runtime contracts. Production modules MUST NOT use `IAgentToolProvider` or a source-local dispatcher.

## 8. Snapshot and invalidation semantics

Each Turn MUST execute against one immutable `EffectiveToolSnapshot`. Registration, schema, exposure, and presentation changes take effect on the next Turn. This preserves prompt-cache and invocation consistency.

The following changes invalidate the next snapshot:

- workspace, thread, plugin, or binding MCP configuration changes;
- tool-source enablement changes;
- Runtime Dynamic declaration replacement;
- binding capability snapshot acceptance;
- Teams mission-thread role-surface changes derived from Teams state;
- mode or profile changes that truly alter the runtime surface.

Immediate safety checks are not frozen. Revocation, disconnect, expired authority, binding removal, and execution-policy invalidation MUST block dispatch immediately, including an invocation named in an older snapshot.

Operational mode restrictions SHOULD keep stable tool schemas and enforce policy at execution time unless the mode represents a genuinely different role or runtime surface.

## 9. Result and audience contract

The normalized result has three audience-separated payloads:

| Field | Audience | Rule |
|---|---|---|
| `content` | model and text fallback clients | Text/image content appropriate for model history. A successful model-visible call MUST produce non-empty model content. |
| `structuredContent` | client/view only | Structured application data. It MUST NOT be automatically inserted into model context. |
| `_meta` | host/view only | Private host or UI metadata. It MUST never enter model context. |

Failures MUST include a concise textual fallback plus stable `errorCode`; `errorMessage` is an English fallback and MAY be accompanied by structured parameters.

`structuredContent` MUST NOT be silently serialized into model content. A source that returns structured data without useful model content violates the contract. Adapters MAY generate an explicit, bounded model summary when they know the semantic shape; ACP is required to do so for structured-only successful results.

On the Dynamic wire, `contentItems` remains the transport spelling for rich content and legacy `structuredResult` is replaced by `structuredContent`. MCP results preserve standard `content`, `structuredContent`, and `_meta` semantics. Native/plugin results use the same normalized internal audiences.

## 10. Session item projection

The common runtime does not require a single Session item type. Projection communicates source and transport semantics:

| Invocation source | Target projection |
|---|---|
| Core or Plugin Native | standard `ToolCall` followed by `ToolResult` |
| MCP | `McpToolCall`, preserving raw MCP result and metadata under audience rules |
| Runtime Dynamic | one `DynamicToolCall` lifecycle item; no companion `ToolResult` |

The specialized `PluginFunctionCall` item is removed. Plugin provenance (`pluginId`, `functionId`, namespace) MUST remain available on the standard invocation/result projection.

Items MUST record canonical `ToolName`, `SourceToolId` or source provenance where safe, call identifier, arguments, status, duration, success, stable failure data, and audience-safe result fields. Sensitive credentials and raw connection state MUST NOT be persisted.

## 11. Runtime Dynamic Tools v2

Runtime Dynamic Tools are restricted to callbacks owned by the active AppServer client for a thread.

### 11.1 Declaration

The wire declaration is a tagged union:

- `Function`: `{ type: "function", name, description, inputSchema, deferLoading?, approval? }`; a top-level function is normalized to `ToolName(null, name)` and therefore has no namespace;
- `Namespace`: `{ type: "namespace", name, description, tools: Function[] }`; contained functions inherit that namespace.

`approval` is the only DotCraft-specific declaration field in v2. Generic exposure and output schema are not Dynamic wire fields: `deferLoading` maps to Direct/Deferred and other policy/exposure decisions remain server-owned. Namespacing is semantic, not a string-prefix convention. Namespace functions may be direct or deferred, but any function with `deferLoading: true` MUST be contained by a namespace. The normalized runtime identity is the composite `ToolName(namespace, name)`; for a top-level Function, `namespace` is exactly `null` and MUST NOT be replaced with a source-owned default.

### 11.2 Lifetime

Declarations and callbacks are connection-owned. Resume requires explicit rebinding:

- omitted declarations: keep the currently bound declaration set only when the request comes from that binding's current owning connection generation;
- empty array: clear/unbind Runtime Dynamic Tools;
- non-empty array: atomically replace the declaration set.

A new or non-owning connection cannot take over by omitting declarations; it MUST submit a non-empty replacement and pass thread/connection authority. Whether a non-owner may clear with `[]` is likewise decided by thread authority, never by payload possession. Every binding has a connection-generation/lease identity. Failed replacement leaves the previous valid live binding unchanged.

Live executors are never persisted. DotCraft MAY persist a non-sensitive last-known declaration summary for diagnostics. After disconnect the summary is not exposed as a live executor. Calls fail quickly with a stable disconnect category. Timeout and protocol failures use distinct stable error categories.

### 11.3 Dynamic content items

The Dynamic v2 `contentItems` wire supports exactly:

- `{ "type": "text", "text": string }`, where text is non-empty after validation;
- `{ "type": "image", "mediaType": string, "url": string }`;
- `{ "type": "image", "mediaType": string, "dataBase64": string }`.

An image item MUST provide exactly one of `url` or `dataBase64`; data URLs are not accepted in `url`. URLs, media types, decoded sizes, item counts, and total result size are validated against limits owned by the AppServer protocol. Unknown item types or invalid shapes make the callback result invalid rather than being inserted into model history. A successful model-visible Dynamic call MUST still include at least one useful text item; images are additive, not the only fallback.

### 11.4 Result and lifecycle

`DynamicToolCall` uses `inProgress`, `completed`, or `failed`. At start, `success` is absent/null; completion includes `durationMs`, audience-separated result fields, and stable errors. `itemId` and provider/model `callId` MUST remain separate.

Runtime Dynamic metadata for private iframe UI is removed. Interactive UI moves to MCP Apps.

## 12. MCP architecture

MCP is the standard external tool and interactive app transport. DotCraft supports four independent MCP origins:

1. workspace configuration, including enabled plugin-contributed servers;
2. per-thread configuration;
3. App Binding MCP sessions;
4. future server-managed origins explicitly described by another spec.

### 12.1 Thread configuration semantics

`ThreadConfiguration.McpServers` has three states:

| Value | Meaning |
|---|---|
| `null` | inherit workspace/plugin MCP configuration |
| `[]` | disable user-configured workspace/plugin MCP for this thread |
| non-empty | replace workspace/plugin MCP with this thread list |

Binding MCP is additive and independent. It is never removed or overridden by the three-state thread list.

### 12.2 AppServer MCP surface

DotCraft uses the following fixed method names for the MCP runtime/control surface:

- `mcpServerStatus/list`;
- `mcpServer/resource/read`;
- `mcpServer/tool/call`;
- `mcpServer/oauth/login`;
- `config/mcpServer/reload`;
- `mcpServer/startupStatus/updated`;
- `mcpServer/oauthLogin/completed`;
- `mcpServer/elicitation/request`.

The existing `mcp/*` methods remain DotCraft's workspace configuration-management surface and MUST NOT be reused as aliases for these runtime methods. M1 includes OAuth plus standard form and URL elicitation forwarding as generic MCP control-plane capabilities. Desktop MUST provide a generic interaction for those flows. MCP Apps resource rendering, AppBridge, and tool-result iframes remain M2 work.

Thread archive/disposal MUST close thread and binding MCP sessions. Configuration changes invalidate the next snapshot. Status output MUST distinguish workspace, thread, plugin, and binding origins.

### 12.3 Approval

MCP tool approval evaluates standard annotations such as read-only, destructive, and open-world behavior together with thread policy and DotCraft authority. Registration or App Binding approval does not bypass invocation approval. MCP App-initiated tool calls use the same policy.

## 13. MCP Apps host

DotCraft targets the stable MCP Apps extension `io.modelcontextprotocol/ui` dated 2026-01-26. Desktop pins the official TypeScript host package at `@modelcontextprotocol/ext-apps` version `1.7.4` and uses `AppBridge`. Core uses validated wrappers over raw MCP metadata and does not require a preview C# Apps package.

Every DotCraft MCP session advertises support for `text/html;profile=mcp-app`. The MCP capability belongs to the session lifecycle and does not change when Desktop connects or disconnects.

### 13.1 Required first-version capabilities

The host supports only capabilities it advertises. The first version includes:

- inline and fullscreen display modes; picture-in-picture is excluded;
- same-server tool and resource access;
- logging and safe external-link requests;
- `ui/message`;
- `ui/update-model-context`.

`ui/message` submits a new source-marked Turn. It is accepted only from a live, visible view and is rate-limited.

`ui/update-model-context` stores one last-write-wins value per live view. That value is injected once into the next user- or UI-message Turn and then consumed. It is not durable thread configuration and is not independently appended to history.

An ordinary user Turn atomically consumes all pending contexts for its thread. An accepted `ui/message` consumes only the originating view's context. Context is injected as bounded, untrusted transient input and cannot alter authority, policy, or system instructions.

### 13.2 Visibility and authority

An omitted MCP Apps visibility value means model-and-app visibility. App-only tools are hidden from the model. A view may call only tools from the same MCP server that are visible to the app, and every call passes normal authority and approval.

Visibility is read only from nested `_meta.ui`. An empty array means neither audience. A declaration containing an unknown visibility value is invalid and exposes the tool to neither audience. App-only tools remain in the canonical registry but are excluded from model projection. UI linkage requires an absolute `ui://` URI. The resource response MUST match that URI, use `text/html;profile=mcp-app`, and contain exactly one text document or base64 blob.

Tool results preserve the audience contract: model `content`, view-only `structuredContent`, and host/view-only `_meta`.

Core/AppServer issues an opaque `viewHandle` for each live interactive view. The trusted host resolves it to immutable server/session, snapshot revision, authority revision, `SourceToolId`, and resource URI. The iframe may send only stable MCP Apps messages, tool names/arguments allowed by its advertised capability, and the opaque handle through the host-controlled channel; it cannot select or override server id, session id, binding id, source tool id, or resource URI. Desktop and the iframe never construct an MCP source name by prefixing a canonical `ToolName`.

View authority is connection-owned and live-only. Only a terminal `McpToolCall` delivered live to the same AppServer connection may receive eligibility. Eligibility and handles are not persisted. History reads, replay, resume, navigation reload, Desktop restart, a new connection, and MCP generation replacement render the generic result and never reconstruct a live view.

App-initiated `tools/call` uses the common dispatcher with App audience and a server-generated call id. It does not create a Turn, Session tool item, or provider-history entry. `ui/message` is the only view action that submits or queues a source-marked Turn.

### 13.3 Isolation

Desktop MUST render untrusted resources through an isolated-origin sandbox proxy/inner iframe design. It MUST enforce resource size limits, CSP, navigation restrictions, teardown, and per-view capability scoping. A view MUST NOT gain Electron, filesystem, shell, arbitrary network, or cross-server tool access. M2 validates declared permissions but grants none; camera, microphone, geolocation, and clipboard-write are denied. Resource `domain` metadata does not choose a real iframe origin. Safe links are limited to HTTPS, `mailto`, and explicit loopback HTTP.

## 14. Local presentation registry

Desktop maintains a local `ToolRendererRegistry` independent of MCP Apps. In M2, only trusted Core/Desktop renderers may register; plugin and third-party registration is deferred to a separate trust and code-loading specification. Entry selection requires an ordinal `PresentationId` and matching safe Core provenance. Duplicate ids are rejected. Renderer-specific bounded options are validated by the selected renderer.

Remote tool descriptions, MCP `_meta`, Dynamic declarations, plugin data, or result data MUST NOT name arbitrary local code. Unknown, unavailable, invalid, or provenance-mismatched renderers fall back to the generic tool card.

M2 migrates all existing hard-coded renderer families into the registry: CreatePlan, Cron, SkillManage, SkillView, all SubAgent operations, shell, WriteFile/EditFile and streaming diff, WebSearch/WebFetch, RequestUserInput, ReadFile, TodoWrite/UpdateTodos, deferred tool search, and generic fallback. Conversation cards, pinning, grouping, and labels MUST consume registry render plans and MUST NOT select a special renderer by tool name.

## 15. App Binding v2 boundary

App Binding remains a DotCraft-specific control plane because DotCraft must bind an installed or connected application, account/conversation authority, and one thread. It is no longer a tool declaration, execution, or interactive UI protocol.

App Binding v2 owns:

- app identity and installed/connection state;
- one-click thread enablement;
- connection credential handoff and rotation;
- binding MCP endpoint/session establishment;
- approved capability snapshot, revision, confirmation, revoke, rebind, and audit;
- social conversation target and routing authority where applicable.

App Binding v2 does not own:

- Dynamic Tool attachment;
- executable static tool catalogs or per-tool scope pickers;
- private iframe resource protocols;
- model result audience semantics;
- Teams runtime roles.

### 15.1 Enablement and capability changes

Enabling an already connected app is one thread-level authorization action. If the app is not connected, the handoff MAY combine connection/login/account selection and then automatically enable the requesting thread. DotCraft MUST NOT require a routine second confirmation after successful app-side connection.

The first MCP initialization snapshot is approved by the original enable action. Later capability expansion requires a thread-side confirmation. Expansion includes a new tool, widened schema/visibility/risk, or widened UI CSP domain/permission. Removal, title/description changes, endpoint or token rotation, and capability narrowing are auto-accepted.

The grant is the whole app for one thread. App Binding v2 does not expose a per-scope tool picker.

### 15.2 Transport and credentials

External binding MCP uses Streamable HTTP only:

- loopback HTTP or remote HTTPS is allowed;
- stdio, app-supplied executables/commands, and remote plaintext HTTP are forbidden;
- every binding has an independent bearer and MCP session;
- the app owns its app-connection credential;
- DotCraft persists only a salted hash/identifier, expiry, and principal for that credential;
- the raw binding MCP bearer is memory-only.

After a DotCraft restart, a binding is an offline stub until the app rebinds and rotates its token. Rebind does not require reauthorization if persisted authority remains valid. Revocation deletes the credential verifier and closes the session; a stale app connection cannot resurrect it.

Offline bindings retain only a non-sensitive last-known approved capability snapshot for display and fast failure. Revocation removes dispatch authority immediately.

## 16. Product-specific mappings

### 16.1 Agent Teams

Agent Teams is a Plugin Native tool source (`sourceId = agent-teams`). Plugin enablement is the workspace product switch. When enabled, direct `teams.CreateTeam` is available only to trusted `UserTopLevel` planning contexts. Module-managed, SubAgent, unattended, internal, ephemeral, and unknown contexts do not receive it.

Mission/member threads receive role-specific direct native tools selected from the current `MissionThreadRecord`; `MemberId == "leader"` selects the Leader surface. `TeamsService` owns live membership, role, assignee, reference, and mission-lifecycle validation. Scheduling invokes `ISessionService` directly. Immutable mission context is supplied through the stable `teams/mission` context page. Branding uses generic channel/presentation metadata.

### 16.2 Social channels

App Binding retains conversation identity, bind-code lifecycle, routing authority, revoke, and audit. Social tool registrations and execution become managed native sources/runtimes. The server injects `socialTarget`/`deliveryTarget`; model arguments MUST NOT override the bound address. The runtime MAY delegate actual delivery through the external channel adapter.

Origin-channel tools remain independent from a Desktop thread's optional social binding.

Social binding uses a dedicated channel-principal resolve/accept/rebind flow, not ordinary app Binding MCP activation. The verified channel/account/conversation target is the authority input to the managed native runtime.

### 16.3 External application integrations

Long-lived application tools migrate to binding MCP and interactive UI migrates to MCP Apps. Run-specific submission callbacks remain Runtime Dynamic Tools v2 because they are ephemeral callbacks owned by the active run/client connection. Integrations that already host Streamable HTTP MCP add a binding-scoped authenticated endpoint or strictly separated authentication mode, independent per-binding session state, capability revision, and rebind. Binding authentication MUST NOT break existing shared MCP clients.

## 17. Baseline and intentional extensions

The common AppServer and MCP contracts are the baseline. Product-specific behavior MUST be expressed as an explicit extension rather than an incidental protocol divergence.

| Area | Decision |
|---|---|
| composite `ToolName(namespace, name)` | common baseline |
| Direct/Deferred/DirectModelOnly/Hidden exposure | common baseline |
| registered executors separated from model-visible specs | common baseline |
| namespaced deferred discovery and exact identity routing | common baseline |
| Dynamic Function/Namespace tagged union | common baseline plus optional DotCraft approval metadata |
| Dynamic callback lifetime | DotCraft extension: explicit AppServer connection ownership and rebind/clear semantics |
| Dynamic result | DotCraft extension: structured client content, stable error codes, and explicit lifecycle/duration |
| Dynamic content items | DotCraft extension: strict `text`/`image` items and bounded URL/base64 image payloads |
| multiple native/plugin/MCP/Dynamic sources | DotCraft extension through the unified registry/runtime |
| App Binding | DotCraft-specific authorization/control plane; never treated as a Runtime Dynamic Tool feature |
| MCP Apps | standards-based extension host shared by all MCP sources |
| Session projection | DotCraft-specific source-aware items while preserving common call identity/result semantics |

Any new divergence in tool identity, exposure, deferred discovery, or Dynamic declaration semantics MUST be justified in the owning specification rather than introduced incidentally in code.

## 18. Compatibility and rollout

The refactor is a coordinated hard cut across DotCraft Core, Desktop, .NET/TypeScript/Python SDKs, and maintained external integrations. Released builds do not carry a long-lived legacy parser or dual protocol.

The hard cut is applied in place on the current development AppServer contract. It does not increment the AppServer protocol version and does not add version negotiation. Core and every in-repository client/SDK MUST switch atomically; a client that sends the removed Dynamic v1 shape is rejected as invalid input.

Implementation MAY use temporary internal adapters within a milestone. They MUST be removed before that milestone's completion criteria are met. Old App Binding v1 tool/grant state is not migrated. Users re-enable affected apps after the cutover.

M1–M5 are implementation/review boundaries, not independently releasable compatibility stages. Because M1 hard-cuts Dynamic v2 and M2 removes the private UI target before external integrations move in M5, the complete series MUST pass one coordinated release gate. Intermediate work remains unreleased or behind an internal integration boundary so released server, Desktop, SDK, and integration builds never disagree about Dynamic v2, MCP Apps, or App Binding v2.

## 19. Security invariants

1. A definition, iframe, remote server, or invocation argument cannot grant authority.
2. Revocation and expiry are enforced at dispatch time, not only snapshot construction.
3. Model content, structured client content, and host-private metadata never cross audience boundaries implicitly.
4. Remote metadata cannot select trusted local code.
5. Binding MCP cannot launch local executables supplied by an app.
6. Conversation targets and Teams mission/member/thread identity are server-derived and cannot be overridden by tool arguments.
7. Persisted diagnostics contain no bearer, credential, live executor, or sensitive `_meta`.
8. Interactive UI is optional; text/model fallback remains sufficient for correctness.

## 20. Observability and diagnostics

Diagnostics SHOULD identify:

- canonical `ToolName` and safe source provenance;
- source kind and MCP origin;
- snapshot revision;
- exposure and authority decision reason;
- approval decision;
- call/item identifiers;
- duration, outcome, and stable error code;
- connection/binding capability revision without secrets.

Status and audit views MUST distinguish declaration availability, model exposure, live executor health, and authority. These states are not interchangeable.

## 21. Conformance requirements

Each implementation milestone MUST add behavior-level tests for its observable contracts. At minimum, the completed architecture requires coverage for:

- canonical identity collision and namespace behavior;
- per-Turn snapshot consistency plus immediate revocation;
- result audience isolation and non-empty model fallback;
- resume/fork/compaction call identifier preservation;
- Dynamic v2 declaration replacement and disconnect behavior;
- MCP three-state configuration and source-aware status;
- MCP Apps visibility, approval, isolation, and one-shot model context;
- Teams role-specific native snapshots plus live `TeamsService` business validation without App Binding;
- App Binding enable/rebind/revoke/capability-expansion state transitions;
- managed social target injection;
- coordinated SDK and first-party wire conformance.

## 22. Milestone map

| Milestone | Outcome |
|---|---|
| M1 | Unified tool core, Dynamic v2, MCP backend, Session/AppServer projection |
| M2 | MCP Apps host and trusted Desktop presentation registry |
| M3 | Teams native runtime and removal of Teams App Binding dependency |
| M4 | App Binding v2 control plane and managed social runtime |
| M5 | External integrations, SDK, and Desktop coordinated cutover; legacy removal |

No milestone may weaken the security or audience invariants to reduce migration work.
