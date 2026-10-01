# Tool Architecture

| Field | Value |
|---|---|
| Version | 0.7.10 |
| Status | Living |
| Date | 2026-10-02 |
| Scope | Agent tools, authority binding, execution, session projection, and interactive presentation |

## 1. Purpose

This specification defines the architecture for every tool that can be made available to a DotCraft agent. It is the source of truth for shared behavior across native tool providers, MCP, Runtime Dynamic Tools, App Binding, Plugin Functions, social channels, and client presentation boundaries.

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
| Plugin Native | plugin enablement | trusted in-process plugin | plugin-contributed functions, managed channel tools |
| MCP | MCP connection/session | MCP server | workspace, thread, plugin, or binding MCP |
| Runtime Dynamic | AppServer connection + thread | connected AppServer client | Desktop thread management, client-owned run callbacks |

Core Native file and shell tools execute in the DotCraft process environment.
For Docker Stack deployments, that environment is the DotCraft container.

### 5.2 Registration boundary

A source contribution MUST separate durable semantic definition from live executability:

- `ToolDefinition` contains `ToolDefinitionId`, `ToolName`, `SourceToolId`, schemas, safe source provenance, approval/policy hints, and optional presentation link;
- `ToolRuntimeBinding` contains `RuntimeBindingId`, definition reference, executor handle, lifecycle/connection lease, `AuthorityRef`, availability, and binding revision;
- `ToolRegistration` is the resolved planning join and contains references/revisions plus the source-declared projection shape rather than persisting a live executor inside the definition.

This split MUST represent a stable definition with a replaced MCP connection, a durable grant with an offline executor, and persisted items with no current runtime. Source-specific opaque state and live executor handles are never model-visible and never stored in durable definition snapshots.

The runtime registry MUST retain executable registrations that are hidden from the model. Model-visible definition generation is a projection of the runtime registry, not the registry itself. The source catalog, runtime registry, per-thread effective snapshot, local renderer registry, and MCP Apps resource broker are separate indexes with different owners; none may be used as an alias for another.

### 5.3 Binding and authority

Authority determines whether a registration is usable by a thread and whether a particular invocation can dispatch. Authority inputs include thread configuration, plugin state, App Binding state, mode policy, approval policy, MCP annotations, and connection health. Source-owned business invariants remain the native service's execution-boundary responsibility and are not required to become a generic authority record.

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

Deferred discovery MUST be finalized from the effective snapshot rather than from an independent provider-only surface. When the final snapshot contains searchable deferred registrations, planning MUST add a real Core Native search registration and runtime to the same registry with canonical identity `ToolName(null, "SearchTools")`. Provider adapters preserve that canonical identity in runtime and Session history while translating provider-owned wire shapes; OpenAI Responses serializes the tool as client-executed `tool_search` and maps its `tool_search_call` and `tool_search_output` callbacks to `SearchTools`. When no searchable deferred registration remains, the search registration MUST be absent. Schema sanitization that replaces a search registration MUST preserve its deferred-search registry capability for downstream provider projection. Provider-native result content MAY be retained by the normalized execution result and durable model history when required for replay, but the search invocation still uses the common dispatcher and Session projection pipeline.

OpenAI Responses native search keeps discovered definitions provider-visible through historical `tool_search_output` items rather than adding them to later top-level tool lists. Every deferred registration in the current Turn snapshot MUST remain locally resolvable regardless of whether the current Agent instance observed its search result. This local lookup MUST NOT persist discovery state, re-add deferred definitions to the provider tool list, or resolve a registration absent from the current snapshot.

Anthropic native search MAY accept `select:` queries for deterministic discovery. Every selector MUST match an exact, case-sensitive provider flat name; canonical local names and case variants are not aliases. Keyword search MAY match canonical names and other searchable metadata, but every result MUST expose the selected registration's provider flat name. Result limits MUST be applied before activation. Provider callbacks resolve through the snapshot's exact reverse index using that same identity.

Before each Anthropic native deferred sampling request, the runtime MUST provide a request-local names-only inventory of every deferred provider-flat identity. The inventory is ordinally sorted, remains unchanged when a tool is activated, and is regenerated from the current Turn snapshot after history replacement. Exact tool references replayed from model history reactivate matching identities in the current deferred index before tool schemas are projected, so rebuilding an Agent does not invalidate its history. The inventory and replayed activation are Anthropic request shaping: they MUST NOT be persisted as additional history, summarized by compaction, added to shared base instructions, or emitted on OpenAI protocol paths.

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

A runtime may own source-specific execution approval. It must use the common approval identity
and terminal-result contract without prompting twice for the same operation.

Every path after step 2 MUST terminalize the same projection, including validation, authority, policy, approval, cancellation, timeout, execution, and normalization failures. `ToolExecution` MAY separately indicate when the approved runtime actually begins. Source adapters MAY add transport-specific lifecycle behavior, but MUST NOT duplicate common approval, result audience, or error normalization rules.

Host-owned tools that start external work MUST pass the invocation cancellation token to the component that owns that work. That owner MUST distinguish caller cancellation from its own timeout, stop and drain foreground resources before propagating caller cancellation, and leave explicitly detached background resources under their separate control-plane lifecycle. A tool MUST NOT report cancellation merely by abandoning a still-running foreground operation.

MCP input schemas are preserved as declared and follow the MCP JSON Schema contract. The Host MUST NOT apply the restricted Plugin/Runtime Dynamic schema validator to MCP arguments or reject valid composition and reference keywords before dispatch. Server-reported protocol and tool-execution input errors remain normal terminal MCP results. Oversized model-visible text is projected as a bounded preview without changing a successful source result into a failure. Raw MCP content, structured content, and metadata use an independent bounded persistence projection.

Image generation is the Core Native tool `image_gen.imagegen`, dispatched through the common pipeline like any other local tool. Planning publishes it only when `Tools.ImageGeneration.Enabled` is on and the effective provider uses an OpenAI protocol, has `SupportsImageGeneration`, and has usable credentials: ChatGPT OAuth, model-service routing, or a non-empty API key. `SupportsImageGeneration` defaults to on for ChatGPT OAuth and for API-key providers on the official OpenAI endpoint, and to off for other endpoints. The model-visible arguments are `prompt`, optional `transparent_background`, and at most one of `referenced_image_paths` or `num_last_images_to_include`, each limited by `Tools.ImageGeneration.MaxReferenceImages`; unknown arguments are rejected. The runtime sends one JSON request to the provider's base endpoint: `images/generations`, or `images/edits` with the reference images as data URLs. Every request carries the image Turn id header and the `originator` header; the backend image request id from the response headers and the image's `generation_id` are kept on the `ImageGeneration` item, and the request id is kept on failures too. Paths are read through the file access guard; on a remote route they are read through `dotcraft/remoteToolHost/images/read`, which applies the Host's `ReadFile` authorization. Recent images are taken newest-first from user input, tool output images, and earlier generated images, then sent in conversation order. A successful result saves the PNG under the thread's generated-images directory, or through the captured remote route, and returns the image plus a saved-path hint of at most 1024 bytes as model content; the hint is omitted when saving fails. Request and API failures, including usage limits, are returned to the model as error text. Plan mode does not restrict the tool.

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

A provider that forwards a call elsewhere projects nothing itself, so the composite identity has to travel as data. The declaration it sends carries `ToolName(namespace, name)` and `ProviderFlatName`, and whatever rebuilds it at the far end presents the same identity to the real provider. A rebuilt declaration is never invoked where it was rebuilt; the tool runs where it was declared.

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

`ToolPlanningThreadKind` is a trusted Session-derived classification with values `UserTopLevel`, `SubAgentChild`, `Unattended`, `Internal`, and `Unknown`. It is derived once when constructing `ToolPlanningContext` from persisted thread origin/source/visibility/configuration. Sources MUST treat `Unknown` as ineligible for privileged entrypoint tools and MUST NOT replace this classification with source-local channel-name denylists.

Modules contribute tools through `GetToolSources()` and the typed source, definition, binding, registration, and runtime contracts. Production modules MUST NOT use `IAgentToolProvider` or a source-local dispatcher.

### 7.1 Compile-time C# tool declarations

Every first-party tool whose model-visible contract is known at C# compile time MUST derive its name, description, input schema, and output schema from `DotCraft.Generators`. This applies both to ordinary generated `AIFunction` tools and to tools that retain a custom `IToolRuntime` or provider-specific wrapper. Generated declarations are immutable and may be consumed independently from the generated executable function.

Declaration-only contracts MUST use the typed declaration surface rather than embedding JSON Schema strings or constructing static schema objects by hand. Conditional input relationships SHOULD use an explicit discriminator plus runtime validation when they cannot be represented by the supported typed schema attributes. Production declarations MUST NOT embed raw JSON Schema fragments as an escape hatch.

Generated tool parameter objects are closed by default, including statically known nested DTOs; explicitly open JSON values such as `JsonObject` and `JsonNode` remain open. CLR enum parameters are model-visible strings whose default wire names are camelCase. `JsonStringEnumMemberName` is reserved for values that cannot be derived by that convention. Generated invocation wrappers deserialize enum inputs with the same naming rules and reject numeric enum values without changing result serialization.

Schemas discovered or supplied at runtime are exempt from this rule. Exempt sources include MCP servers, plugins, channel adapters, App Bindings, runtime dynamic tools, and provider translation layers that preserve or transform a schema owned by another boundary.

### 7.2 Strongly typed tool implementations

Native and .NET plugin authors SHOULD implement ordinary tools as methods marked with `[Tool]`
or `[GeneratedTool]`, with typed business parameters and constructor-supplied services. An
`AIFunctionToolSource` selects generated functions and supplies namespace, exposure and policy
configuration; its shared adapter owns registration and JSON argument conversion. Custom
`IToolRuntime` implementations and `[ToolDeclaration]` remain available for specialized adapters.

A method MAY request one required, non-nullable, by-value `ToolInvocationContext` parameter when it
needs live invocation identity. The generator MUST exclude this parameter and `CancellationToken`
from model schema and JSON binding. Model arguments MUST NOT override injected context.

`AIFunctionToolRuntime` MUST carry its received context in a fresh `AIFunctionArguments.Context`
for each invocation. Generated functions read that separate context through the host-owned base
class, never from model arguments, mutable shared state or a planning identity. Invoking a function
that requires context without supplying it fails before its business method runs. Independent and
concurrent calls MUST receive their own thread, Turn, call, workspace and execution-location values.
Task isolation remains the business service's responsibility using that live identity.

Methods declared to return `ToolExecutionResult`, `Task<ToolExecutionResult>` or
`ValueTask<ToolExecutionResult>` retain the result object through generated marshalling. The
runtime envelope has no generated output schema, and a null envelope is `tool_result_invalid`.
Ordinary return values retain their existing serialization behavior. An explicit business result,
including an uncertain external outcome, MUST NOT be overwritten by the adapter.

`WriteFile`, `EditFile`, `Exec`, `WriteStdin`, and `Transfer` return `ToolExecutionResult` directly.
Success, errors, model text and structured content belong to that result.
Ordinary-value and rich-content tools retain their own return contracts.

File mutation success reflects the disk write. Subsequent diff reporting failures produce warnings
without changing a completed write into a failure; the [file-change payload](session-core.md#toolresult)
records the write outcome independently of the available diff.
An unknown write outcome invalidates the local aggregate diff; a rejected write leaves it intact.

Exception conversion preserves the category, supplies a nonempty fallback, and retains the original
exception and call identity in host diagnostics. Cancellation remains cancellation. Native and remote
results preserve failure content and structured data alongside the normalized success flag.

`ReadFile`, `WriteFile`, and `EditFile` decode an existing file as UTF-8 unless a UTF-8, UTF-16, or
UTF-32 byte-order mark selects that encoding. They return an error instead of decoding bytes that are
not valid text in that encoding and never rewrite such a file; `GrepFiles` still searches it.

`GrepFiles` accepts a file or directory path. A file target searches only that file and reports its
name and matching line numbers; directory targets recurse. Include patterns filter directory scans.
Neither search backend skips files based on size. Both preserve path authorization, binary-file
filtering, cancellation, the search timeout, and the match limit. The managed backend scans lines
incrementally rather than loading the entire file. Missing targets are reported as missing paths.

`EditFile` treats `oldText` and `newText` as already JSON-decoded strings and does not reinterpret
literal escape sequences. It first counts non-overlapping exact substring matches, then matches whole-line sequences ignoring
trailing whitespace, leading and trailing whitespace, and finally common Unicode punctuation
and space differences, in that order. A stage with no candidates advances to the next stage;
a stage with multiple candidates rejects the edit without writing. A unique match is replaced
at its recorded source range. Comparison normalization never becomes replacement content.
`replaceAll` uses exact substring matches only. Existing line-ending and encoding policies still
apply; editing does not automatically format or reindent replacement text.
Whole-line matching includes the trailing empty segment when a snippet ends in LF, so that segment
must match an empty or whitespace-only line (or the file's final empty segment).

Result forwarding preserves source containment and audience normalization; a source cannot acquire
host-private result authority by returning an envelope.

Generated declarations own reusable input/output serializer options with independent resolvers so
schema and serialization metadata do not pin a retired plugin load context.

### 7.3 Shell execution

Background terminal output uses bounded process-read and live-notification queues. A 1 MiB UTF-8
buffer retains the beginning and end independently of the complete disk log. Truncated previews
split the requested character budget between those excerpts, mark the omitted middle, and preserve
character boundaries. Foreground command results include the complete log path when truncated.
Real-time output is limited to 8 KiB per delta, 10,000 deltas per terminal, and the configured
live-byte budget. Exhaustion stops data
notifications while logging and process execution continue. Both running and recovered previews
remain bounded; completion follows output drain and log flush.

Empty `WriteStdin` input reads the terminal snapshot, including final output and exit code after
completion or recovery, until retention expires. Nonempty input to an exited terminal fails.
An exited process releases its active entry even if metadata persistence fails. Its in-memory final
snapshot remains readable; metadata failures are diagnosed without changing the known exit code.
Completion notifications must allow observers to read that final snapshot immediately.
Shell commands preserve their quoting. Output uses UTF-8 replacement decoding without encoding
detection; stream boundaries must not split characters. Logs and events use UTF-8.

A returned command result is a successful tool invocation even when its exit code is nonzero.
The exit code and CommandExecution status describe the command outcome. Launch, authorization,
execution infrastructure, timeout and cancellation failures remain tool failures.

## 8. Snapshot and invalidation semantics

Each Turn MUST execute against one immutable `EffectiveToolSnapshot`. Registration, schema, exposure, and presentation changes take effect on the next Turn. This preserves prompt-cache and invocation consistency.

The following changes invalidate the next snapshot:

- workspace, thread, plugin, or binding MCP configuration changes;
- tool-source enablement changes;
- Runtime Dynamic declaration replacement;
- binding capability snapshot acceptance;
- external channel tool connection publication, disconnection, or replacement;
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
| Core Native image generation | one `ImageGeneration` lifecycle item; no companion `ToolCall` or `ToolResult` |

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

`approval` is the only DotCraft-specific declaration field. Generic exposure and output schema are not Dynamic wire fields: `deferLoading` maps to Direct/Deferred and other policy/exposure decisions remain server-owned. Namespacing is semantic, not a string-prefix convention. Namespace functions may be direct or deferred, but any function with `deferLoading: true` MUST be contained by a namespace. The normalized runtime identity is the composite `ToolName(namespace, name)`; for a top-level Function, `namespace` is exactly `null` and MUST NOT be replaced with a source-owned default. Runtime Dynamic registrations are runtime-managed operational capabilities and MUST remain available independently of Agent Profile tool allow/deny policy; mode policy, approval policy, connection leases, and Thread/Turn authority still apply.

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

### 12.1.1 Policy boundary

MCP registrations are governed by the thread's MCP policy — server selection and namespaced tool selectors — and MUST NOT participate in the Agent Profile's ordinary tool allow/deny name lists. An MCP origin is chosen by workspace configuration, by the host that owns the thread, or by an App Binding, none of which the Profile author enumerates; a name list written without that origin in view would withhold its tools silently. Mode policy, approval policy, connection leases, and Thread/Turn authority still apply.

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

The `mcp/*` methods remain DotCraft's workspace configuration-management surface and MUST NOT be reused as aliases for these runtime methods. OAuth plus standard form and URL elicitation forwarding are generic MCP control-plane capabilities. Desktop MUST provide a generic interaction for those flows. MCP Apps resource rendering and AppBridge follow the presentation contract in Section 13.

Thread archive/disposal MUST close thread and binding MCP sessions. Configuration changes invalidate the next snapshot. Status output MUST distinguish workspace, thread, plugin, and binding origins. A server that fails to start MUST report the server and the reason, whatever origin it came from; a thread-origin server is not exempt because its lifetime is one Thread.

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

A capable View host MUST isolate untrusted resources, enforce declared and host policy, and scope every bridge operation to one live handle. A View MUST NOT gain filesystem, shell, arbitrary network, cross-server tool, host-process, or unrelated client authority. Resource `domain` metadata does not choose a real origin. Safe links are limited to HTTPS, `mailto`, and explicit loopback HTTP.

## 14. Presentation boundary

Presentation is optional and MUST preserve useful model/text fallback content. Local renderers and assistant inline visualizations are separate presentation paths; neither grants additional tool execution authority.

### 14.1 Trusted local renderer registry

A trusted local renderer registry is independent of MCP Apps. Core renderers and installed, enabled Desktop Plugins may register renderers selected by an exact ordinal `PresentationId`. Active Desktop Plugin entries are ordered by priority and stable plugin/contribution identity, followed by the optimized Core renderer and generic fallback. Renderer registrations are withdrawn when their owning generation ends. Renderer-specific bounded options are validated by the selected renderer.

The projected `PresentationId` selects only an already active local renderer; tool names, arguments, results, MCP metadata, and other payload data cannot provide module paths or executable code. Unknown or unavailable presentation ids use the Core or generic fallback. Client rendering families, grouping, and interaction behavior belong to the applicable client specification.

### 14.2 Assistant inline visualization boundary

Inline visualization is an assistant-message presentation path, not a tool-result payload. A completed `AgentMessage` may reference a View with an exact standalone directive:

```text
::dotcraft-inline-vis{file="example-name.html"}
```

The directive remains ordinary persisted assistant text. It introduces no Session Item, delta, payload kind, snapshot, metadata record, or provider-history type. Clients without the capability retain the directive as text.

Only a completed `AgentMessage` containing the directive outside fenced code may authorize a View. The file name MUST match `^[a-z0-9]+(?:-[a-z0-9]+)*\.html$`. Authoring uses ordinary file tools in `<SessionThread.WorkspacePath>/.craft/visualizations/<threadId>/`; execution and worktree overrides do not change ownership. These files are transient workspace resources with no archive, fork, migration, reload, or cross-device guarantee. Implementations MUST NOT fall back to a user-global directory. Ordinary file-tool execution, history, trace, and result semantics remain unchanged.

The host issues a connection-owned opaque handle after revalidating the active connection/thread binding, completed source item, exact directive, safe file name, workspace boundary, and current file. The handle binds its source thread, Turn, Item, and file; a View cannot choose or override those identities. View follow-up starts or queues a source-marked Turn and cannot forge user or channel identity.

## 15. App Binding boundary

An application binding supplies independently revocable authority for its tool registrations.
Binding identity, approved capability revision, live executor health, and model exposure remain
separate facts. Capability expansion requires accepted authority before dispatch; revocation blocks
existing snapshots immediately. Offline definitions may remain visible as non-executable stubs.

Binding-scoped connections and credentials remain isolated from shared MCP connections. Credentials
never enter model content, tool arguments, persisted definitions, or client-private result metadata.

## 18. Protocol consistency

Core, Desktop, and the .NET and TypeScript SDKs use the same canonical tool identity, Runtime Dynamic declaration, MCP Apps, and App Binding contracts. Unsupported fields and method names are rejected rather than interpreted as alternate protocol shapes.

## 19. Security invariants

1. A definition, View, remote server, or invocation argument cannot grant authority.
2. Revocation and expiry are enforced at dispatch time, not only snapshot construction.
3. Model content, structured client content, and host-private metadata never cross audience boundaries implicitly.
4. Remote metadata cannot select trusted local code.
5. Binding MCP cannot launch local executables supplied by an app.
6. Conversation targets are server-derived and cannot be overridden by tool arguments.
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

A capability that policy keeps out of the model's tool list MUST be recorded with its model-visible name, namespace, usage source, and the refusal that hid it. Such a capability produces no call, no result, and no error, so this record is the only evidence that it was withheld rather than absent.
