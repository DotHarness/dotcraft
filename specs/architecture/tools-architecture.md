# Tool Architecture

| Field | Value |
|---|---|
| Version | 0.2.3 |
| Status | Normative |
| Date | 2026-07-18 |
| Scope | Agent tools, authority binding, execution, session projection, and interactive presentation |
| Related | [Session Core](session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md), [App Binding](../protocols/app-binding.md), [Desktop Client](../clients/desktop-client.md), [Plugin Architecture](plugin-architecture.md) |

## 1. Purpose

This specification defines the architecture for every tool that can be made available to a DotCraft agent. It is the source of truth for shared behavior across native tool providers, MCP, Runtime Dynamic Tools, App Binding, Plugin Functions, Teams, social channels, and client presentation boundaries.

The architecture has four goals:

1. one source-neutral registration and dispatch pipeline;
2. explicit authority, exposure, identity, and audience boundaries;
3. consistent AppServer tool semantics across server and clients;
4. standards-based interactive tool UI through MCP Apps, while retaining DotCraft-specific App Binding as a small authorization and connection control plane.

The affected protocol specification MUST be updated in the same implementation change whenever one of these shared rules changes.

### 1.1 Specification ownership

| Specification | Owns |
|---|---|
| this document | cross-source identity, layer boundaries, exposure, authority, execution, result audiences, and presentation invariants |
| `session-core.md` | Thread/Turn/Item persistence, generic lifecycle, event ordering, archive/resume/fork semantics |
| `appserver-protocol.md` | JSON-RPC method names, DTOs, capability negotiation, notifications, and transport serialization |
| `app-binding.md` | app discovery, principal/connection handoff, thread binding state, capability authorization, revoke/rebind/audit |
| MCP and MCP Apps standards | MCP capability, tool/resource/result, Apps metadata, and bridge wire contracts |
| plugin/SDK/client specifications | source authoring, language APIs, and UI mapping of the architecture/protocol contracts |

Downstream specifications MUST reference these semantics rather than copy and locally redefine them. The owning protocol specification is authoritative for serialization details.

## 2. Non-goals

This specification does not:

- replace the complete AppServer or Session Core architecture;
- define general app discovery, marketplace, or plugin installation UX;
- make every tool source use the same transport or session item type;
- require interactive UI for a tool to be correct;
- expose App Binding credentials or external app executables to the agent runtime;
- provide aliases for Runtime Dynamic Tool or App Binding execution protocols.

## 3. Normative language

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are normative.

## 4. Canonical terminology

| Term | Definition |
|---|---|
| **Tool Source** | A component that contributes tool registrations. Examples: Core native tools, a plugin, an MCP server, or a Runtime Dynamic client. |
| **Tool Definition** | An immutable source-qualified semantic definition: identity, model-facing name, description, schemas, hints, and presentation link. |
| **Tool Runtime Binding** | A live or stub binding from a definition to an executor, lifecycle lease, authority reference, availability, and revision. |
| **Tool Registration** | The resolved source-neutral join of a definition, runtime-binding reference, exposure defaults, and safe provenance for snapshot planning. |
| **Tool Projection Shape** | The source-declared Session lifecycle shape for an invocation: a standard call/result pair or one specialized lifecycle item. |
| **Tool Runtime** | The executor implementation used by a live runtime binding. |
| **Tool Authority** | The server-authoritative decision that a registration is permitted for a thread and invocation context. |
| **Tool Exposure** | Whether and how a permitted tool is published to the model. |
| **Effective Tool Snapshot** | The immutable set of registrations and model-visible definitions selected for one Turn. |
| **Tool Invocation** | One model or host request to execute a registered tool. |
| **Tool Execution Result** | Source-neutral result containing model content, client-only structured content, host-only metadata, success, and stable error information. |
| **Presentation Descriptor** | Trusted metadata that selects or configures a local renderer. It is distinct from MCP Apps UI resource metadata. |
| **Runtime Dynamic Tool** | A thread-scoped callback implemented by the connected AppServer client. It is connection-owned and not a general external app integration mechanism. |
| **Binding MCP** | An MCP server connection authorized for one App Binding and added independently to one thread. |

**Runtime Dynamic Tool** is the canonical term for client-owned callbacks. App Binding tools use binding-scoped MCP sessions.

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

Core owns the Sandbox execution semantics, tool definitions, configuration, workspace synchronization,
and thread-scoped lifecycle. A concrete sandbox backend is an infrastructure adapter: it implements the
Core sandbox provider contracts and owns its vendor SDK dependency, protocol mapping, and provider-specific
error translation. The default application composes the OpenSandbox adapter explicitly; this boundary does
not make Sandbox a Plugin Native source or change its stable Core Native tool identities.

### 5.2 Registration boundary

A source contribution MUST separate durable semantic definition from live executability:

- `ToolDefinition` contains `ToolDefinitionId`, `ToolName`, `SourceToolId`, schemas, safe source provenance, approval/policy hints, and optional presentation link;
- `ToolRuntimeBinding` contains `RuntimeBindingId`, definition reference, executor handle, lifecycle/connection lease, `AuthorityRef`, availability, and binding revision;
- `ToolRegistration` is the resolved planning join and contains references/revisions plus the source-declared projection shape rather than persisting a live executor inside the definition.

This split MUST represent a stable definition with a replaced MCP connection, a durable grant with an offline executor, and persisted items with no current runtime. Source-specific opaque state and live executor handles are never model-visible and never stored in durable definition snapshots.

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

Deferred discovery MUST be finalized from the effective snapshot rather than from an independent provider-only surface. When the final snapshot contains searchable deferred registrations, planning MUST add a real Core Native search registration and runtime to the same registry with canonical identity `ToolName(null, "tool_search")`. Every provider projection uses that same identity; an adapter MUST NOT introduce a second semantic name such as `SearchTools`. When no searchable deferred registration remains, the search registration MUST be absent. Provider-native result content MAY be retained transiently by the normalized execution result, but the search invocation still uses the common dispatcher and Session projection pipeline.

### 5.5 Execution

All sources MUST converge on this ordered source-neutral dispatch pipeline. Planning context selects an immutable snapshot, but it is not invocation identity. At the model callback boundary, Session execution MUST create an immutable `ToolInvocationContext` containing the live thread, Turn, call, audience, cancellation, approval, and authority inputs. A planning Turn id MUST NOT be used as a substitute for the live Turn id. Dispatch and recording MUST use only that explicit invocation context after the boundary. A host invocation without a Turn MAY execute when its audience is authorized, but it MUST NOT create a Session Turn item.

1. resolve the provider callback identity to the exact canonical `ToolName` in the Turn snapshot, using the composite namespace/name for namespace-capable protocols and the snapshot's flat alias index for flat-only protocols;
2. atomically create or upsert the source-appropriate started projection from the resolved registration;
3. verify snapshot exposure and invocation audience;
4. check binding lease, live authority, and policy;
5. validate arguments at the owning boundary: Host-owned sources validate against their declared schema, while MCP arguments remain JSON objects and are validated by the owning MCP server;
6. apply mode/thread/native guards and MCP annotation policy;
7. run `PreToolUse` hooks;
8. resolve approval;
9. execute through `IToolRuntime`, classifying timeout and caller cancellation separately;
10. normalize result audiences, require model fallback where applicable, and enforce result limits;
11. project the terminal Session lifecycle;
12. run `PostToolUse` or `PostToolUseFailure` hooks.

Every path after step 2 MUST terminalize the same projection, including validation, authority, policy, approval, cancellation, timeout, execution, and normalization failures. `ToolExecution` MAY separately indicate when the approved runtime actually begins. Source adapters MAY add transport-specific lifecycle behavior, but MUST NOT duplicate common approval, result audience, or error normalization rules.

Host-owned tools that start external work MUST pass the invocation cancellation token to the component that owns that work. That owner MUST distinguish caller cancellation from its own timeout, stop and drain foreground resources before propagating caller cancellation, and leave explicitly detached background resources under their separate control-plane lifecycle. A tool MUST NOT report cancellation merely by abandoning a still-running foreground operation.

MCP input schemas are preserved as declared and follow the MCP JSON Schema contract. The Host MUST NOT apply the restricted Plugin/Runtime Dynamic schema validator to MCP arguments or reject valid composition and reference keywords before dispatch. Server-reported protocol and tool-execution input errors remain normal terminal MCP results. Oversized model-visible text is projected as a bounded preview without changing a successful source result into a failure. Raw MCP content, structured content, and metadata use an independent bounded persistence projection.

Provider-hosted capabilities such as provider-native image generation are not local tools and MUST NOT be represented by `IToolRuntime` or dispatched through the local tool pipeline. Snapshot planning records them in a separate provider-capability plan, and the provider adapter projects their declarations and specialized Session items. Their result still obeys the audience and persistence rules applicable to their specialized item type.

### 5.6 Presentation

Presentation is optional enhancement after correctness. Every model-visible tool result MUST have a usable model/text fallback. Presentation has two independent mechanisms:

- **MCP Apps**, for server-provided interactive resources and bidirectional host/view communication;
- **local renderer registry**, for trusted DotCraft/Desktop renderers selected by server-controlled provenance and `PresentationId`.

Remote MCP metadata MUST NOT select an arbitrary local renderer.

## 6. Identity model

Tool identity is intentionally split into semantic, source-routing, runtime, and provider-projection identities:

| Identity | Purpose | Stability boundary |
|---|---|---|
| `SourceToolId` | Real identifier understood by the source/executor | Source connection or persisted source contract |
| `ToolDefinitionId` | DotCraft source-qualified semantic definition identity | Stable across reconnects while semantic identity is unchanged |
| `ToolName(namespace, name)` | Canonical model and router identifier | Effective snapshot and persisted invocation history |
| `RuntimeBindingId` | Live executor/authority lease identity | One binding/session generation |
| `PresentationId` | Trusted renderer selection | Core/Desktop presentation contract |
| `ProviderFlatName` | Deterministic flat alias for providers that cannot represent namespaces | Effective snapshot and persisted invocation history |

`ToolName` MUST be a true composite value with ordinal, case-sensitive equality. Its optional `namespace` and required `name` are model-visible components, not encoded source-routing data. Each present component MUST be non-empty and match `^[A-Za-z0-9_]+$`; its deterministic flat form (`name`, or `namespace + "__" + name`) MUST fit within 64 ASCII bytes. Two tools MAY have the same `name` in different namespaces. Deferred indexes, activated-tool sets, provider definitions, callbacks, and Session history MUST preserve the full composite identity.

Source adapters SHOULD choose stable namespaces. Controlled non-MCP declarations that violate the component grammar MUST be rejected or quarantined at registration; they MUST NOT be silently rewritten into a different semantic identity.

MCP is normalized as one deterministic batch because an individual tool cannot detect sanitization collisions:

1. The namespace seed is `mcp__` plus the origin's declared server name when present, otherwise its collision-safe runtime name. The child seed is the raw MCP tool name.
2. Every Unicode scalar value outside ASCII letters, digits, and underscore is replaced with one `_`; hyphen is deliberately normalized even when a particular provider accepts it so one identity works across all supported providers. The source string is not Unicode-normalized before sanitization or hashing.
3. A namespace is limited to 49 ASCII bytes. A longer namespace becomes its first 36 bytes, `_`, and the first 12 lowercase hexadecimal characters of SHA-1 over the UTF-8 bytes of the unsanitized seed.
4. The child limit is `64 - namespaceLength - 2`, reserving `__` for a flat alias. A longer child is truncated using the same `prefix + "_" + 12-character SHA-1` form within that limit.
5. If distinct runtime servers sanitize to the same namespace, each conflicting namespace receives a suffix derived from SHA-1 of the full runtime name. If distinct raw tools in one runtime sanitize to the same child, each conflicting child receives a suffix derived from SHA-1 of `runtimeName + NUL + rawToolName`. Truncation is reapplied after suffixing.
6. Collision groups and suffixes are computed from ordinally sorted full seeds, so results do not depend on MCP enumeration or source discovery order. Any duplicate that remains after this algorithm is quarantined rather than resolved by last-write-wins.

For MCP, `runtimeName` identifies the effective connection and may contain plugin/source delimiters; `SourceToolId` is the exact raw name sent to MCP `tools/call`; neither is a model namespace. The effective snapshot keeps the exact mapping `ToolName -> (ToolDefinitionId, RuntimeBindingId, runtimeName, SourceToolId)`. Desktop, an iframe, and provider adapters MUST NOT reconstruct the MCP route by parsing or prefixing `ToolName`.

Direct publication, deferred indexes, native tool-search results, and provider callbacks MUST all use the same canonical `ToolName` namespace. In a namespace-capable tool-search result, the outer container name is the canonical namespace and every child name is the canonical local name; a `ProviderFlatName` MUST NOT be nested as a child name. A canonical namespace appears at most once in one provider projection or search result. Raw MCP server, runtime, and source identities are restricted to routing, authority, generation lookup, and provenance. A deferred descriptor is searchable metadata, not an identity authority, and its namespace MUST exactly equal its definition's canonical namespace. Provider callbacks containing an invalid or unknown namespace fail closed as an unresolved tool call; constructing a `ToolName` from untrusted callback data MUST NOT throw an exception that fails the Turn.

The MCP initialize result's optional `instructions` value is the model-visible description of that server's canonical tool namespace. It is untrusted tool metadata, not a system prompt, role instruction, App Binding context block, or source of authority. The description follows the same normalization and size limits as other model-visible descriptions and MUST remain attached to the exact MCP server generation that returned it. Direct and deferred namespace-capable provider projections use the same normalized description. A namespace with no description uses the provider-neutral generic description. If model-visible registrations in one canonical namespace contain multiple distinct non-empty descriptions, projection uses the generic description, emits one safe `conflicting_namespace_description` snapshot diagnostic, and still emits exactly one namespace container. Reconnecting one server MUST NOT reuse another server's description. Binding MCP snapshots retain the approved description while offline and remove it on revocation. A description-only change follows the ordinary non-expanding capability-diff rule.

Provider projection follows the provider's native identity shape:

- a namespace-capable protocol serializes `ToolName(namespace, name)` as a namespace definition plus local child name and returns the same tuple on its function call;
- a flat-only protocol uses the snapshot's `ProviderFlatName`, which is `name` for a top-level tool and `namespace + "__" + name` for a namespaced tool after the normalization above;
- the snapshot owns both `ToolName -> ProviderFlatName` and `ProviderFlatName -> ToolName` indexes; if distinct canonical tuples produce the same flat alias, every conflicting alias is truncated as needed and suffixed from SHA-1 over the UTF-8 bytes of `namespace-or-empty + NUL + name`;
- dispatch MUST NOT parse a flat alias to recover a namespace, and namespace-capable protocols MUST NOT flatten a composite identity before dispatch.

Provider/model call identifiers, canonical `ToolName`, `ProviderFlatName`, source-routing identities, and Session item identifiers are different identities. They MUST be stored and projected separately and MUST survive resume, fork, compaction, and history reconstruction without being substituted for, parsed from, or regenerated from one another.

## 7. Core contracts

The following conceptual contracts have separate responsibilities:

| Contract | Responsibility |
|---|---|
| `ToolName`, `ProviderFlatName`, `SourceToolId`, `ToolDefinitionId`, `RuntimeBindingId` | Typed canonical, flat-provider, source, semantic-definition, and live-binding identities. |
| `IToolSource` | Contribute definitions and runtime bindings for a planning context. |
| `ToolDefinition` | Immutable source-qualified semantic definition. |
| `ToolRuntimeBinding` | Executor, lifecycle lease, authority, availability, and revision. |
| `ToolRegistration` | Resolve a definition and binding reference for planning. |
| `IToolRuntime` | Execute one authorized invocation using an invocation context. |
| `IToolBindingLease` | Perform live availability/revocation/generation checks for a binding. |
| `IToolAuthorityEvaluator` | Evaluate a source-declared live authority reference when the source has independently revocable authority. It is optional only when the source service or lease owns all live validation. |
| `IToolDispatcher` | Apply the common invocation pipeline and dispatch to the selected runtime. |
| `ToolPlanningContext` | Immutable inputs used to assemble the next Turn snapshot, including trusted `ToolPlanningThreadKind`; its Turn identity is not an execution identity. |
| `ToolInvocationContext` | Immutable live thread, Turn, call, audience, cancellation, approval, and authority inputs captured at the execution boundary. |
| `ToolExecutionResult` | Normalized result and stable failure information. |
| `ToolError` | Stable error code, English fallback, and optional structured parameters. |
| `EffectiveToolSnapshot` | Immutable per-Turn registration set plus canonical, composite-provider, and flat-alias indexes/model definitions. |
| `ToolPresentationDescriptor` | Trusted local `PresentationId` plus bounded renderer options. It contains no free-form renderer selector. |
| `ProviderHostedCapabilityPlan` | Provider-adapter declarations that are not local `IToolRuntime` tools. |

`ToolPlanningThreadKind` is a trusted Session-derived classification with values `UserTopLevel`, `ModuleManaged`, `SubAgentChild`, `Unattended`, `Internal`, and `Unknown`. It is derived once when constructing `ToolPlanningContext` from persisted thread origin/source/visibility/configuration. Sources MUST treat `Unknown` as ineligible for privileged entrypoint tools and MUST NOT replace this classification with source-local channel-name denylists.

Modules contribute tools through `GetToolSources()` and the typed source, definition, binding, registration, and runtime contracts. Production modules MUST NOT use `IAgentToolProvider` or a source-local dispatcher.

### 7.1 Compile-time C# tool declarations

Every first-party tool whose model-visible contract is known at C# compile time MUST derive its name, description, input schema, and output schema from `DotCraft.Generators`. This applies both to ordinary generated `AIFunction` tools and to tools that retain a custom `IToolRuntime` or provider-specific wrapper. Generated declarations are immutable and may be consumed independently from the generated executable function.

Declaration-only contracts MUST use the typed declaration surface rather than embedding JSON Schema strings or constructing static schema objects by hand. Conditional input relationships SHOULD use an explicit discriminator plus runtime validation when they cannot be represented by the supported typed schema attributes. Production declarations MUST NOT embed raw JSON Schema fragments as an escape hatch.

Schemas discovered or supplied at runtime are exempt from this rule. Exempt sources include MCP servers, plugins, channel adapters, App Bindings, runtime dynamic tools, and provider translation layers that preserve or transform a schema owned by another boundary.

## 8. Snapshot and invalidation semantics

Each Turn MUST execute against one immutable `EffectiveToolSnapshot`. Registration, schema, exposure, and presentation changes take effect on the next Turn. This preserves prompt-cache and invocation consistency.

The following changes invalidate the next snapshot:

- workspace, thread, plugin, or binding MCP configuration changes;
- tool-source enablement changes;
- Runtime Dynamic declaration replacement;
- binding capability snapshot acceptance;
- external channel tool connection publication, disconnection, or replacement;
- Teams mission-thread role-surface changes derived from Teams state;
- mode or profile changes that truly alter the runtime surface.

Immediate safety checks are not frozen. Revocation, disconnect, expired authority, binding removal, and execution-policy invalidation MUST block dispatch immediately, including an invocation named in an older snapshot.

Adapter-declared channel tools are connection-bound. Their `RuntimeBindingId`, descriptor set, lease, and executor must refer to the same initialized adapter connection. A lease check followed by invocation must not retarget the call to a newer connection. When the connection changes, the current Turn keeps its immutable snapshot but loses dispatch authority; the next Turn rebuilds against the new connection.

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

On the Dynamic wire, `contentItems` is the transport spelling for rich content and structured client data uses `structuredContent`. MCP results preserve standard `content`, `structuredContent`, and `_meta` semantics. Native/plugin results use the same normalized internal audiences.

## 10. Session item projection

The common runtime does not require a single Session item type. Each registration MUST declare exactly one projection shape; the common recorder MUST NOT infer that shape from a provider call name or result payload. Projection communicates source and transport semantics:

| Invocation source | Target projection |
|---|---|
| Core or Plugin Native | standard `ToolCall` followed by `ToolResult` |
| MCP | `McpToolCall`, preserving raw MCP result and metadata under audience rules |
| Runtime Dynamic | one `DynamicToolCall` lifecycle item; no companion `ToolResult` |

Plugin invocations use the standard `ToolCall` and `ToolResult` items. Plugin provenance (`pluginId`, `functionId`, namespace) MUST remain available on that projection.

Items MUST record canonical `ToolName`, deterministic `ProviderFlatName`, definition identity, runtime-binding identity and revisions where applicable, snapshot revision, `SourceToolId` or source provenance where safe, trusted presentation, call identifier, arguments, status, duration, success, stable failure data, and audience-safe result fields. MCP items additionally record the exact runtime server name used for routing. Sensitive credentials and raw connection state MUST NOT be persisted.

History reconstruction MUST use the persisted canonical tuple for namespace-capable protocols and the persisted flat alias for flat-only protocols. It MUST NOT consult the current tool inventory, parse a flat alias, or regenerate an alias from current normalization rules. This makes replay independent of reconnects, renamed plugin runtimes, source ordering, and later tool-set changes.

Session projection MUST be atomic per Turn, call identifier, and projection shape. Streaming argument observation and dispatcher lifecycle recording MUST upsert the same call item rather than create competing items. A specialized lifecycle item transitions in place from started to exactly one terminal state. A standard projection creates or updates exactly one `ToolCall` and appends exactly one terminal `ToolResult`. Cancellation, timeout, rejection, and execution failure race through the same terminal guard; no path may publish a second terminal result or leave an accepted registered call permanently started.

## 11. Runtime Dynamic Tools

Runtime Dynamic Tools are restricted to callbacks owned by the active AppServer client for a thread.

### 11.1 Declaration

The wire declaration is a tagged union:

- `Function`: `{ type: "function", name, description, inputSchema, deferLoading?, approval? }`; a top-level function is normalized to `ToolName(null, name)` and therefore has no namespace;
- `Namespace`: `{ type: "namespace", name, description, tools: Function[] }`; contained functions inherit that namespace.

`approval` is the only DotCraft-specific declaration field. Generic exposure and output schema are not Dynamic wire fields: `deferLoading` maps to Direct/Deferred and other policy/exposure decisions remain server-owned. Namespacing is semantic, not a string-prefix convention. Namespace functions may be direct or deferred, but any function with `deferLoading: true` MUST be contained by a namespace. The normalized runtime identity is the composite `ToolName(namespace, name)`; for a top-level Function, `namespace` is exactly `null` and MUST NOT be replaced with a source-owned default.

### 11.2 Lifetime

Declarations and callbacks are connection-owned. Resume requires explicit rebinding:

- omitted declarations: keep the currently bound declaration set only when the request comes from that binding's current owning connection generation;
- empty array: clear/unbind Runtime Dynamic Tools;
- non-empty array: atomically replace the declaration set.

A new or non-owning connection cannot take over by omitting declarations; it MUST submit a non-empty replacement and pass thread/connection authority. Whether a non-owner may clear with `[]` is likewise decided by thread authority, never by payload possession. Every binding has a connection-generation/lease identity. Failed replacement leaves the previous valid live binding unchanged.

Live executors are never persisted. DotCraft MAY persist a non-sensitive last-known declaration summary for diagnostics. After disconnect the summary is not exposed as a live executor. Calls fail quickly with a stable disconnect category. Timeout and protocol failures use distinct stable error categories.

### 11.3 Dynamic content items

The Dynamic `contentItems` wire supports exactly:

- `{ "type": "text", "text": string }`, where text is non-empty after validation;
- `{ "type": "image", "mediaType": string, "url": string }`;
- `{ "type": "image", "mediaType": string, "dataBase64": string }`.

An image item MUST provide exactly one of `url` or `dataBase64`; data URLs are not accepted in `url`. URLs, media types, decoded sizes, item counts, and total result size are validated against limits owned by the AppServer protocol. Unknown item types or invalid shapes make the callback result invalid rather than being inserted into model history. A successful model-visible Dynamic call MUST still include at least one useful text item; images are additive, not the only fallback.

### 11.4 Result and lifecycle

`DynamicToolCall` uses `inProgress`, `completed`, or `failed`. At start, `success` is absent/null; completion includes `durationMs`, audience-separated result fields, and stable errors. `itemId` and provider/model `callId` MUST remain separate.

Runtime Dynamic metadata does not define iframe UI. Interactive UI uses MCP Apps.

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

The `mcp/*` methods remain DotCraft's workspace configuration-management surface and MUST NOT be reused as aliases for these runtime methods. OAuth plus standard form and URL elicitation forwarding are generic MCP control-plane capabilities. Desktop MUST provide a generic interaction for those flows. MCP Apps resource rendering and AppBridge follow the presentation contract in Section 13 and the client behavior contract in the [Desktop Client specification](../clients/desktop-client.md#582-mcp-apps-interactive-tool-views).

Thread archive/disposal MUST close thread and binding MCP sessions. Configuration changes invalidate the next snapshot. Status output MUST distinguish workspace, thread, plugin, and binding origins.

Streamable HTTP is only an OAuth candidate transport; it MUST NOT by itself imply that authentication is supported or required. Runtime status, rather than transport shape or error-text matching, is the authority for OAuth UX. Desktop exposes an authentication action only when the effective server reports `authStatus: "notLoggedIn"`; `failureReason: "reauthenticationRequired"` changes that action to reauthentication. Unknown discovery results fail closed and do not expose an OAuth action. A connected server with usable OAuth credentials reports `authStatus: "oAuth"` but does not show a primary authentication action.

MCP startup readiness depends on initialization and tool discovery. Optional resource and resource-template inventory MUST NOT cause an otherwise usable server to fail startup. Lightweight `toolsAndAuthOnly` status reads do not enumerate resources; full status reads may enumerate them independently and treat enumeration failure as an empty optional inventory.

### 12.3 Approval

MCP tool approval evaluates standard annotations such as read-only, destructive, and open-world behavior together with thread policy and DotCraft authority. Registration or App Binding approval does not bypass invocation approval. MCP App-initiated tool calls use the same policy.

## 13. MCP Apps host

DotCraft targets the stable MCP Apps extension `io.modelcontextprotocol/ui` dated 2026-01-26. Core uses validated wrappers over raw MCP metadata. Client package selection and View presentation behavior belong to the applicable client specification.

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

Tool visibility controls invocation authority, not presentation eligibility. A terminal result may render its associated View when the persisted `ui://` association still matches the current tool definition and runtime even when the originating tool is not app-visible. Such a View may call only the same-server tools that independently grant app visibility; rendering the View does not make its originating tool app-callable.

Visibility is read only from nested `_meta.ui`. An empty array means neither audience. A declaration containing an unknown visibility value is invalid and exposes the tool to neither audience. App-only tools remain in the canonical registry but are excluded from model projection.

UI linkage uses `_meta.ui.resourceUri` as the canonical declaration. `_meta["ui/resourceUri"]` is accepted only when the nested field is absent. An invalid present nested declaration fails closed and MUST NOT be replaced by the alias. The resource URI MUST be absolute and use `ui://`. The response MUST match that URI, use `text/html;profile=mcp-app`, and contain exactly one text document or base64 blob.

Tool results preserve the audience contract: model `content`, view-only `structuredContent`, and host/view-only `_meta`.

MCP App presentation has three distinct lifetimes. The normalized `ui://` resource association and the bounded tool result are persisted with the terminal `McpToolCall`. Availability is derived from the current tool definition, runtime, and authority whenever a client projects that item. AppServer may project non-persistent `mcpApp.available = true` as advisory current availability evidence; it is not reusable authority. The View document, bridge connection, resource body, and opaque `viewHandle` exist only for one active View and are never persisted.

Core/AppServer issues a new opaque `viewHandle` for every interactive View. The trusted host resolves it to immutable server/session, authority revision, `SourceToolId`, and resource URI. The View may send only stable MCP Apps messages, tool names/arguments allowed by its advertised capability, and the opaque handle through the host-controlled channel; it cannot select or override server id, session id, binding id, source tool id, or resource URI. Client hosts and Views never construct an MCP source name by prefixing a canonical `ToolName`.

History reads and resume may advertise a new View only when the persisted association still matches the current MCP registration and current authority. Opening that item fetches the current resource and creates a new handle bound to the current MCP generation. It never restores a previous View, handle, pending context, or permission. Offline, removed, changed, or revoked associations render the generic result. Rollback, archive, delete, runtime generation replacement, binding revoke, plugin disable, configuration replacement, disconnect, and explicit close invalidate affected live handles immediately.

App-initiated `tools/call` uses the common dispatcher with App audience and a server-generated call id. It does not create a Turn, Session tool item, or provider-history entry. `ui/message` is the only view action that submits or queues a source-marked Turn.

### 13.3 Isolation

A capable View host MUST isolate untrusted resources, enforce declared and host policy, and scope every bridge operation to one live handle. A View MUST NOT gain filesystem, shell, arbitrary network, cross-server tool, host-process, or unrelated client authority. Resource `domain` metadata does not choose a real origin. Safe links are limited to HTTPS, `mailto`, and explicit loopback HTTP. Exact methods, limits, and stable errors are owned by [AppServer Protocol Section 22.10](../protocols/appserver-protocol.md#2210-mcp-apps-opaque-view-methods); Desktop sandbox and recovery behavior is owned by the [Desktop Client specification](../clients/desktop-client.md#582-mcp-apps-interactive-tool-views).

## 14. Presentation boundary

Presentation is optional and MUST preserve useful model/text fallback content. Local renderers and assistant inline visualizations are separate presentation paths; neither grants additional tool execution authority.

### 14.1 Trusted local renderer registry

A trusted local renderer registry is independent of MCP Apps. Only trusted Core/client renderers may register; plugin and third-party registration is deferred to a separate trust and code-loading specification. Entry selection requires an ordinal `PresentationId` and matching safe Core provenance. Duplicate ids are rejected. Renderer-specific bounded options are validated by the selected renderer.

Remote tool descriptions, MCP `_meta`, Dynamic declarations, plugin data, tool names, arguments, and result data MUST NOT name or select arbitrary local code. Unknown, unavailable, invalid, or provenance-mismatched renderers use generic fallback presentation. Client rendering families, grouping, and interaction behavior belong to the applicable client specification.

### 14.2 Assistant inline visualization boundary

Inline visualization is an assistant-message presentation path, not a tool-result payload. A completed `AgentMessage` may reference a View with an exact standalone directive:

```text
::dotcraft-inline-vis{file="example-name.html"}
```

The directive remains ordinary persisted assistant text. It introduces no Session Item, delta, payload kind, snapshot, metadata record, or provider-history type. Clients without the capability retain the directive as text.

Only a completed `AgentMessage` containing the directive outside fenced code may authorize a View. The file name MUST match `^[a-z0-9]+(?:-[a-z0-9]+)*\.html$`. Authoring uses ordinary file tools in `<SessionThread.WorkspacePath>/.craft/visualizations/<threadId>/`; execution and worktree overrides do not change ownership. These files are transient workspace resources with no archive, fork, migration, reload, or cross-device guarantee. Implementations MUST NOT fall back to a user-global directory. Ordinary file-tool execution, history, trace, and result semantics remain unchanged.

The host issues a connection-owned opaque handle after revalidating the active connection/thread binding, completed source item, exact directive, safe file name, workspace boundary, and current file. The handle binds its source thread, Turn, Item, and file; a View cannot choose or override those identities. View follow-up starts or queues a source-marked Turn and cannot forge user or channel identity. Exact capability, method, result, and error contracts are owned by [AppServer Protocol Section 22.10A](../protocols/appserver-protocol.md#2210a-inline-visualization-views). Desktop parsing, loading, sandbox, confirmation, and fallback behavior is owned by the [Desktop Client specification](../clients/desktop-client.md#583-inline-assistant-visualizations).

## 15. App Binding boundary

App Binding is DotCraft's control plane for binding an installed or connected application, account/conversation authority, and one thread. Tool declaration, execution, and interactive UI use MCP and MCP Apps.

App Binding owns:

- app identity and installed/connection state;
- one-click thread enablement;
- connection credential handoff and rotation;
- binding MCP endpoint/session establishment;
- approved capability snapshot, revision, confirmation, revoke, rebind, and audit;
- social conversation target and routing authority where applicable.

App Binding does not own:

- Dynamic Tool attachment;
- executable static tool catalogs or per-tool scope pickers;
- private iframe resource protocols;
- model result audience semantics;
- Teams runtime roles.

### 15.1 Enablement and capability changes

Enabling an already connected app is one thread-level authorization action. If the app is not connected, the handoff MAY combine connection/login/account selection and then automatically enable the requesting thread. DotCraft MUST NOT require a routine second confirmation after successful app-side connection.

The first MCP initialization snapshot is approved by the original enable action. Later capability expansion requires a thread-side confirmation. Expansion includes a new tool, widened schema/visibility/risk, or widened UI CSP domain/permission. Removal, title/description changes, endpoint or token rotation, and capability narrowing are auto-accepted. Rejecting an expansion discards the candidate and moves the binding offline; the previous approved snapshot remains only as the offline registration baseline and cannot dispatch until a compatible authenticated rebind succeeds.

The grant is the whole app for one thread. App Binding does not expose a per-scope tool picker.

### 15.2 Transport and credentials

DotCraft MCP clients use the initialize-handshake lifecycle with `2025-06-18` as the
default compatibility baseline across stdio and Streamable HTTP transports. They MUST
NOT probe or negotiate the `2026-07-28` discovery lifecycle unless a future explicit
product capability enables it. A server MAY negotiate another compatible
initialize-era revision through the standard lifecycle.

External binding MCP uses Streamable HTTP only:

- loopback HTTP or remote HTTPS is allowed;
- stdio, app-supplied executables/commands, and remote plaintext HTTP are forbidden;
- every binding has an independent bearer and MCP session;
- the app owns its app-connection credential;
- DotCraft persists only a salted hash/identifier, expiry, and principal for that credential;
- the raw binding MCP bearer is memory-only.

After a DotCraft restart, a binding is an offline stub until the app rebinds and rotates its token. Rebind does not require reauthorization if persisted authority remains valid. Revocation deletes the credential verifier and closes the session; a stale app connection cannot resurrect it.

Offline bindings retain a non-sensitive last-known approved capability snapshot and expose schema-stable model-visible stubs for prompt-cache stability. Stub invocation fails with `AppBindingOffline` before remote dispatch. Revocation removes the registrations and dispatch authority immediately.

App Binding requests contain connection and authority data only. Executable catalogs, Dynamic attachments, context blocks, private UI methods, and managed social Dynamic execution are invalid.

## 16. Product-specific mappings

### 16.1 Agent Teams

Agent Teams is a Plugin Native tool source (`sourceId = agent-teams`). Plugin enablement is the workspace product switch. When enabled, direct `teams.CreateTeam` is available only to trusted `UserTopLevel` planning contexts. Module-managed, SubAgent, unattended, internal, ephemeral, and unknown contexts do not receive it.

Mission/member threads receive role-specific direct native tools selected from the current `MissionThreadRecord`; `MemberId == "leader"` selects the Leader surface. `TeamsService` owns live membership, role, assignee, reference, and mission-lifecycle validation. Scheduling invokes `ISessionService` directly. Immutable mission context is supplied through the stable `teams/mission` context page. Branding uses generic channel/presentation metadata.

### 16.2 Social channels

App Binding retains conversation identity, bind-code lifecycle, routing authority, revoke, and audit. Social tool registrations and execution become managed native sources/runtimes. The server injects `socialTarget`/`deliveryTarget`; model arguments MUST NOT override the bound address. The runtime MAY delegate actual delivery through the external channel adapter.

Origin-channel tools remain independent from a Desktop thread's optional social binding.

Social binding uses a dedicated channel-principal resolve/accept/rebind flow, not ordinary app Binding MCP activation. The verified channel/account/conversation target is the authority input to the managed native runtime.

### 16.3 External application integrations

Ordinary external integrations MAY expose tools through standard workspace, thread, or plugin MCP without App Binding. App Binding is used only when a product needs per-thread application authorization, connection handoff, capability confirmation, revoke, or rebind; those authorized tools use an independent binding MCP session. Interactive UI uses MCP Apps. Run-specific submission callbacks use Runtime Dynamic Tools because they are ephemeral callbacks owned by the active run/client connection. When one integration supports both shared and binding MCP, binding authentication and per-binding session state MUST remain isolated from shared MCP clients.

A connection-owned Channel tool MAY start a bounded companion executable when the adapter owns the executable, command policy, credentials, and child-process lifecycle. The model-facing input MUST be structured rather than a shell command, credentials MUST be scoped to the child process, common approval MUST complete before adapter dispatch, and the adapter MUST revalidate its business invariants at execution time. The companion remains part of the declaring adapter generation; it is not a host-native provider or an independently registered workspace service. Product-specific behavior requires an owning feature specification; see [Feishu CLI Capabilities](../features/feishu-cli-capabilities.md).

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

## 18. Protocol consistency

Core, Desktop, and the .NET, TypeScript, and Python SDKs use the same canonical tool identity, Runtime Dynamic declaration, MCP Apps, and App Binding contracts. Unsupported fields and method names are rejected rather than interpreted as alternate protocol shapes.

## 19. Security invariants

1. A definition, View, remote server, or invocation argument cannot grant authority.
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
- provider projection shape and `ProviderFlatName` when a flat alias is used;
- source kind and MCP origin;
- snapshot revision;
- exposure and authority decision reason;
- approval decision;
- call/item identifiers;
- duration, outcome, and stable error code;
- connection/binding capability revision without secrets.

Status and audit views MUST distinguish declaration availability, model exposure, live executor health, and authority. These states are not interchangeable.

## 21. Conformance requirements

The architecture requires behavior-level coverage for:

- canonical identity normalization, truncation, collision, enumeration-order independence, and namespace behavior;
- exact composite dispatch for namespace-capable providers and flat-alias dispatch for flat-only providers;
- persisted composite/flat history replay without current-inventory lookup or alias parsing;
- per-Turn snapshot consistency plus immediate revocation;
- result audience isolation and non-empty model fallback;
- resume/fork/compaction call identifier preservation;
- Runtime Dynamic declaration replacement and disconnect behavior;
- MCP three-state configuration and source-aware status;
- MCP Apps visibility, approval, isolation, and one-shot model context;
- inline visualization directive authorization, workspace isolation, transient-file semantics, and handle-scoped follow-up identity;
- Teams role-specific native snapshots plus live `TeamsService` business validation without App Binding;
- App Binding enable/rebind/revoke/capability-expansion state transitions;
- managed social target injection;
- cross-SDK and first-party conformance for supported wire contracts.
