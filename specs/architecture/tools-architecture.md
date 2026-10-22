# Tool Architecture

| Field | Value |
|---|---|
| Version | 0.9.2 |
| Status | Living |
| Date | 2026-10-22 |
| Scope | Agent tools, authority binding, execution, session projection, and interactive presentation |

## 1. Purpose

This specification defines the architecture for every tool that can be made available to a DotCraft agent. It is the source of truth for shared behavior across native tool providers, MCP, Runtime Dynamic Tools, App Binding, Plugin Functions, social channels, and client presentation boundaries.

The architecture has four goals:

1. one source-neutral registration and dispatch pipeline;
2. explicit authority, exposure, identity, and audience boundaries;
3. consistent AppServer tool semantics across server and clients;
4. standards-based interactive tool UI through MCP Apps, while retaining App Binding as a small authorization and connection control plane.

### 1.1 Specification ownership

| Specification | Owns |
|---|---|
| this document | cross-source identity, layer boundaries, exposure, authority, execution, result audiences, presentation invariants, and the semantics of built-in tools |
| [Session Core](session-core.md) | Thread/Turn/Item persistence, generic lifecycle, event ordering, archive/resume/fork semantics |
| the AppServer protocol | JSON-RPC method names, DTOs, capability negotiation, notifications, and transport serialization |
| the App Binding protocol | app discovery, principal/connection handoff, thread binding state, capability authorization, revoke/rebind/audit |
| MCP and MCP Apps standards | MCP capability, tool/resource/result, Apps metadata, and bridge wire contracts |

Consumers reference these semantics rather than redefining them.

## 2. Non-goals

This specification does not:

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
| **Tool Source** | A component that contributes tool registrations: Core native tools, a plugin, an MCP server, or a Runtime Dynamic client. |
| **Tool Definition** | An immutable source-qualified semantic definition: identity, model-facing name, description, schemas, hints, and presentation link. |
| **Tool Runtime Binding** | A live or stub binding from a definition to an executor, lifecycle lease, authority reference, availability, and revision. |
| **Tool Registration** | The resolved source-neutral join of a definition, runtime-binding reference, exposure defaults, and safe provenance for snapshot planning. |
| **Tool Projection Shape** | The source-declared Session lifecycle shape for an invocation: a standard call/result pair or one specialized lifecycle item. |
| **Tool Authority** | The server-authoritative decision that a registration is permitted for a thread and invocation context. |
| **Tool Exposure** | Whether and how a permitted tool is published to the model. |
| **Effective Tool Snapshot** | The immutable set of registrations and model-visible definitions selected for one Turn. |
| **Tool Invocation** | One model or host request to execute a registered tool. |
| **Tool Execution Result** | Source-neutral result containing model content, client-only structured content, host-only metadata, success, and stable error information. |
| **Presentation Descriptor** | Trusted metadata that selects or configures a local renderer. It is distinct from MCP Apps UI resource metadata. |
| **Runtime Dynamic Tool** | A thread-scoped callback implemented by the connected AppServer client. It is connection-owned and not a general external app integration mechanism. |
| **Binding MCP** | An MCP server connection authorized for one App Binding and added independently to one thread. |

## 5. Architectural layers

The architecture has five layers: Definition, Binding/Authority, Exposure, Execution, and Presentation. Source discovery and provenance belong to the Definition layer; the registration is the normalized boundary object passed from Definition into the remaining layers.

```text
Source -> Definition/Registration -> Binding/Authority -> Exposure -> Execution -> Presentation
```

### 5.1 Definition and source

The source discovers or declares a definition and owns source-specific lifecycle state. A source MUST NOT decide final model visibility by itself.

| Source kind | Lifecycle | Executor owner | Typical examples |
|---|---|---|---|
| Core Native | process/workspace | DotCraft server | file, web, subagent |
| Plugin Native | plugin enablement | trusted in-process plugin | plugin-contributed functions, managed channel tools |
| MCP | MCP connection/session | MCP server | workspace, thread, plugin, or binding MCP |
| Runtime Dynamic | AppServer connection + thread | connected AppServer client | client-owned thread management and run callbacks |

Core Native file and shell tools execute in the DotCraft process environment; for Docker Stack deployments, that is the DotCraft container.

### 5.2 Registration boundary

A source contribution separates durable semantic definition from live executability:

- a definition contains `ToolDefinitionId`, `ToolName`, `SourceToolId`, schemas, safe source provenance, approval/policy hints, and optional presentation link;
- a runtime binding contains `RuntimeBindingId`, definition reference, executor handle, lifecycle/connection lease, authority reference, availability, and binding revision;
- a registration is the resolved planning join and carries references, revisions, and the source-declared projection shape rather than a live executor.

This split MUST represent a stable definition with a replaced MCP connection, a durable grant with an offline executor, and persisted items with no current runtime. Source-specific opaque state and live executor handles are never model-visible and never stored in durable definition snapshots.

The runtime registry retains executable registrations that are hidden from the model; model-visible definitions are a projection of it. The source catalog, runtime registry, per-thread effective snapshot, local renderer registry, and MCP Apps resource broker are separate indexes with different owners; none is an alias for another.

### 5.3 Binding and authority

Authority determines whether a registration is usable by a thread and whether a particular invocation can dispatch. Inputs include thread configuration, plugin state, App Binding state, mode policy, approval policy, MCP annotations, and connection health. Source-owned business invariants remain the native service's execution-boundary responsibility.

Authorization MUST be server-authoritative. Arguments, renderer metadata, an iframe, or a remote source MUST NOT expand authority. A source whose execution authority has a live, independently revocable reference MUST fail closed when that reference cannot be resolved.

### 5.4 Exposure

Exposure determines publication to the model after authority is established:

| Value | Model publication |
|---|---|
| `Direct` | Included directly in the model tool list. |
| `Deferred` | Discoverable through the deferred-tool mechanism and not included directly. |
| `DirectModelOnly` | Directly visible as a normal model tool but excluded from any nested script tool surface. |
| `Hidden` | Not visible to the model; the executor MAY remain callable by an authorized host path. |

Exposure controls model publication only. Host/app invocation eligibility is a separate invocation-audience decision, and app visibility MUST NOT introduce additional exposure values. For MCP Apps, visibility maps independently to model-visible and app-callable decisions.

Deferred discovery is finalized from the effective snapshot. When the final snapshot contains searchable deferred registrations, planning adds a real Core Native search registration with canonical identity `ToolName(null, "SearchTools")` to the same registry; otherwise the search registration is absent. Provider adapters keep that canonical identity in runtime and Session history while translating provider-owned wire shapes: OpenAI Responses serializes it as client-executed `tool_search` and maps `tool_search_call` / `tool_search_output` to `SearchTools`. Schema sanitization preserves the search registration's deferred-search capability. Provider-native result content MAY be retained in the execution result and durable model history for replay; the invocation still uses the common dispatcher and Session projection.

On OpenAI Responses, discovered definitions stay provider-visible through historical `tool_search_output` items rather than later top-level tool lists. Every deferred registration in the current Turn snapshot remains locally resolvable whether or not the current Agent instance observed its search result. This lookup does not persist discovery state, re-add definitions to the tool list, or resolve registrations absent from the snapshot.

Anthropic native search MAY accept `select:` queries. Every selector MUST match an exact, case-sensitive provider flat name; canonical local names and case variants are not aliases. Keyword search MAY match canonical names and other metadata, but results expose provider flat names. Result limits apply before activation, and callbacks resolve through the snapshot's exact reverse index.

Before each Anthropic native deferred sampling request, the runtime provides a request-local, ordinally sorted, names-only inventory of every deferred provider-flat identity, regenerated from the current Turn snapshot. Exact tool references replayed from model history reactivate matching identities before tool schemas are projected, so rebuilding an Agent does not invalidate its history. The inventory and replayed activation are request shaping only: they are never persisted, summarized by compaction, added to shared instructions, or emitted on OpenAI paths.

### 5.5 Execution

All sources converge on one ordered dispatch pipeline. Planning selects an immutable snapshot but is not invocation identity: at the model callback boundary, Session execution creates an immutable `ToolInvocationContext` containing the live thread, Turn, call, audience, cancellation, approval, and authority inputs, and dispatch and recording use only that context. A host invocation without a Turn MAY execute when its audience is authorized, but creates no Session item.

1. resolve the provider callback identity to the exact canonical `ToolName` in the Turn snapshot (composite for namespace-capable protocols, the flat alias index otherwise);
2. atomically create or upsert the started projection from the resolved registration;
3. verify snapshot exposure and invocation audience;
4. check binding lease, live authority, and policy;
5. validate arguments at the owning boundary: host-owned sources against their declared schema; MCP arguments remain JSON objects validated by the MCP server;
6. apply mode/thread/native guards and MCP annotation policy;
7. run `PreToolUse` hooks;
8. resolve approval;
9. execute, classifying timeout and caller cancellation separately;
10. normalize result audiences, require model fallback where applicable, and enforce result limits;
11. project the terminal Session lifecycle;
12. run `PostToolUse` or `PostToolUseFailure` hooks.

A runtime may own source-specific execution approval; it uses the common approval identity and terminal-result contract without prompting twice for the same operation.

Every path after step 2 MUST terminalize the same projection, including validation, authority, policy, approval, cancellation, timeout, execution, and normalization failures. A separate execution signal MAY indicate when the approved runtime actually begins.

Host-owned tools that start external work pass the invocation cancellation token to the owner of that work. The owner distinguishes caller cancellation from its own timeout, stops and drains foreground resources before propagating cancellation, and leaves explicitly detached background resources under their own lifecycle. A tool MUST NOT report cancellation merely by abandoning a still-running foreground operation.

MCP input schemas are preserved as declared under the MCP JSON Schema contract; the Host MUST NOT apply the restricted plugin/Runtime Dynamic validator to MCP arguments. Server-reported input errors remain normal terminal MCP results. Oversized model-visible text is projected as a bounded preview without turning success into failure. Raw MCP content, structured content, and metadata use an independent bounded persistence projection.

#### 5.5.1 Image generation

Image generation is the Core Native tool `image_gen.imagegen`. The image provider is `Tools.ImageGeneration.Provider` when set (with `Tools.ImageGeneration.Model`), otherwise the conversation's effective provider. Planning publishes the tool only when `Tools.ImageGeneration.Enabled` is on and the image provider resolves, uses an OpenAI protocol, has `SupportsImageGeneration`, and has usable credentials (ChatGPT OAuth, model-service routing, or a non-empty API key). A configured provider that fails these checks withholds the tool; the conversation provider is never a fallback.

When the conversation provider signs in through ChatGPT OAuth, which reserves this name and schema, the definition carries `dotcraft/reservedSchema` and is sent verbatim with `strict: false`; otherwise the schema is sanitized normally. `SupportsImageGeneration` and `SupportsFreeformTools` (§6.1) default to on for ChatGPT OAuth and for API-key providers on the official OpenAI endpoint, and off elsewhere.

Arguments are `prompt`, optional `transparent_background`, and at most one of `referenced_image_paths` or `num_last_images_to_include`, each limited by `Tools.ImageGeneration.MaxReferenceImages`; unknown arguments are rejected. The runtime calls `images/generations`, or `images/edits` with reference images. Referenced paths are read through the file access guard, or on a remote route through the Host under its `ReadFile` authorization. Recent images are taken newest-first from user input, tool output images, and earlier generated images, then sent in conversation order. The backend request id and the image's `generation_id` are kept on the `ImageGeneration` item; the request id is kept on failures too.

A successful result saves the PNG under the thread's generated-images directory, or through the captured remote route, and returns the image plus a bounded saved-path hint as model content; the hint is omitted when saving fails. Request and API failures, including usage limits, are returned to the model as error text. Plan mode does not restrict the tool. Tool images entering model input use the shared image capability, which owns model size budgets, format selection, and failure placeholders.

#### 5.5.2 Web search

Generic web search is the built-in `WebSearch` tool; `WebFetch` is independent. Both return readable text to the model, never serialized JSON, and the same outcome as structured content for clients. `WebSearch` text opens with the query, follows the untrusted-content notice with one Markdown link per result, its date when known, and its excerpt on the next line, or states that nothing was found, and ends by asking the model to cite the sources it uses as Markdown links. Its structured content holds the query, the service, and each result's title, URL, excerpt, author, and date. `WebFetch` text names the fetched URL, its status, extractor, and any truncation, then the notice and the extracted text; its structured content holds those fields without the text. Failures return their message as error text. `Tools.Web.SearchProvider` defaults to `Auto`: on ChatGPT OAuth the subscription path below applies; otherwise explicit `Exa`, `Bing`, `DeepSeek`, or `ChatGPT` wins, and `Auto` uses DeepSeek only when the effective conversation provider supports official DeepSeek search and is authenticated, searching with that provider's credentials, else Exa. `Auto` never selects ChatGPT. The DeepSeek credential settings apply only when `DeepSeek` is selected. Once DeepSeek is selected, credential errors are reported rather than switching providers, and runtime failures never switch providers.

DeepSeek capability is identified by the official HTTPS `api.deepseek.com` endpoint (including OpenAI and Anthropic compatibility paths), never by provider or model name; remote providers additionally require the service-advertised `webSearchBackend: "deepseek"`. `Tools.Web.DeepSeekCredentialSource` defaults to `Provider`: `DeepSeekProvider` selects a provider, or an empty value prefers the effective conversation provider and then the only supported, authenticated candidate; multiple candidates require explicit selection. Choosing automatic reuse persists an empty string so it overrides an inherited provider id. `ApiKey` mode uses `DeepSeekApiKey`, then `DEEPSEEK_API_KEY`, without borrowing another provider's credentials. Keys are referenced, never copied, and changes rebuild thread agents. On remote providers the model service performs the search with its own credentials.

DeepSeek search keeps the 1–10 result limit, rejects queries longer than 128 Unicode scalar values without truncating, and preserves upstream order. Empty results succeed; invalid responses, HTTP failures, and timeouts return tool error text; caller cancellation propagates.

`ChatGPT` searches through the signed-in ChatGPT account whatever the conversation provider. It uses the first configured OpenAI-protocol provider that signs in through ChatGPT OAuth, with that provider's configured model; a missing provider or model is reported rather than switching services. Each `WebSearch` call posts one `search_query` command to the backend `alpha/search` operation used by subscription web search, through the model service on a model-service route, with a fresh request id and no conversation input, so only the query leaves DotCraft. The response `results` become the title, URL, and snippet results in upstream order up to the requested limit; the response `output` is not returned, because it carries page text and citation markers meant for subscription web search. Failures, including a signed-out account, return tool error text; caller cancellation propagates.

Subscription web search is the Core Native tool `web.run`, published only when the conversation provider resolves, uses an OpenAI protocol, and signs in through ChatGPT OAuth. On that runtime `WebSearch` is withheld so the model sees one search tool. The definition always carries `dotcraft/reservedSchema`. Arguments are batched commands (`search_query`, `image_query`, `open`, `click`, `find`, `screenshot`, `finance`, `weather`, `sports`, `time`, `response_length`); empty arguments mean no commands. The runtime posts one request to the backend `alpha/search` operation, through the model service on a model-service route. The request id is the DotCraft thread id, because the backend keeps browsing state per id and later commands refer to earlier results. The request input is a text-only tail of the conversation from the second-to-last user message, excluding system, developer, thread-context, tool, reasoning, and runtime-reminder content, with assistant text capped by dropping the oldest first. The response `output` returns to the model verbatim, including citation markers, and the response `results` become the `WebSearch` item's results. Failures return error text; caller cancellation propagates. Plan mode does not restrict the tool.

### 5.6 Presentation

Presentation is optional enhancement after correctness. Every model-visible tool result MUST have a usable model/text fallback. Presentation has two independent mechanisms:

- **MCP Apps**, for server-provided interactive resources and bidirectional host/view communication;
- **local renderer registry**, for trusted DotCraft renderers selected by server-controlled provenance and `PresentationId`.

Remote MCP metadata MUST NOT select an arbitrary local renderer.

## 6. Identity model

Tool identity is split into semantic, source-routing, runtime, and provider-projection identities:

| Identity | Purpose | Stability boundary |
|---|---|---|
| `SourceToolId` | Real identifier understood by the source/executor | Source connection or persisted source contract |
| `ToolDefinitionId` | Source-qualified semantic definition identity | Stable across reconnects while semantic identity is unchanged |
| `ToolName(namespace, name)` | Canonical model and router identifier | Effective snapshot and persisted invocation history |
| `RuntimeBindingId` | Live executor/authority lease identity | One binding/session generation |
| `PresentationId` | Trusted renderer selection | Core/client presentation contract |
| `ProviderFlatName` | Deterministic flat alias for providers that cannot represent namespaces | Effective snapshot and persisted invocation history |

`ToolName` is a composite value with ordinal, case-sensitive equality. Its optional `namespace` and required `name` are model-visible components, not encoded routing data. Each present component MUST be non-empty and match `^[A-Za-z0-9_]+$`; its flat form (`name`, or `namespace + "__" + name`) MUST fit within 64 ASCII bytes. Two tools MAY share a `name` in different namespaces. Deferred indexes, activated-tool sets, provider definitions, callbacks, and Session history preserve the full composite identity.

Source adapters SHOULD choose stable namespaces. Non-MCP declarations that violate the grammar are rejected or quarantined at registration, never silently rewritten.

MCP is normalized as one deterministic batch:

1. The namespace seed is `mcp__` plus the origin's declared server name, otherwise its collision-safe runtime name. The child seed is the raw MCP tool name.
2. Every Unicode scalar value outside ASCII letters, digits, and underscore becomes one `_` (hyphen included, so one identity works across providers). No Unicode normalization precedes sanitization or hashing.
3. A namespace is limited to 49 ASCII bytes; a longer one becomes its first 36 bytes, `_`, and the first 12 lowercase hex characters of SHA-1 over the UTF-8 unsanitized seed.
4. The child limit is `64 - namespaceLength - 2`; a longer child is truncated with the same `prefix + "_" + 12-hex` form.
5. Distinct source identities that sanitize to the same namespace each receive a suffix from SHA-1 of the full source identity; distinct raw tools in one source that collide receive a suffix from SHA-1 of `sourceIdentity + NUL + rawToolName`. Truncation is reapplied after suffixing. Ordinary catalogs scope source identity to the server source family; binding catalogs use the binding id.
6. Collision groups are computed from ordinally sorted full seeds, so results do not depend on enumeration order. A remaining duplicate is quarantined, not resolved by last-write-wins.

For MCP, `runtimeName` identifies the effective connection and `SourceToolId` is the exact raw name sent to `tools/call`; neither is a model namespace. The snapshot keeps the exact mapping `ToolName -> (ToolDefinitionId, RuntimeBindingId, runtimeName, SourceToolId)`. Clients, Views, and provider adapters MUST NOT reconstruct the MCP route by parsing or prefixing `ToolName`.

Ordinary and approved App Binding catalogs share this normalization. Binding definitions retain identity, descriptions, schemas, annotations, audiences, and exposure across connection loss; only executor and lease change. Candidate binding catalogs are withheld. Persisted MCP items and MCP App resource operations record the executor's routing name and generation separately from the binding's authority revision.

Direct publication, deferred indexes, native tool-search results, and provider callbacks all use the same canonical namespace. In a namespace-capable search result, the container name is the canonical namespace and each child is the canonical local name; a `ProviderFlatName` is never nested as a child. A namespace appears at most once per projection or search result. Raw MCP server, runtime, and source identities are restricted to routing, authority, generation lookup, and provenance. A deferred descriptor's namespace equals its definition's canonical namespace. Callbacks with an invalid or unknown namespace fail closed as an unresolved tool call and never fail the Turn.

The MCP initialize result's optional `instructions` value is the model-visible description of that server's namespace. It is untrusted tool metadata, not a system prompt or source of authority; it follows the normal description limits and stays attached to the server generation that returned it. A namespace without one uses a generic description. If registrations in one namespace carry distinct non-empty descriptions, projection uses the generic description, emits one `conflicting_namespace_description` snapshot diagnostic, and still emits exactly one namespace container. Binding MCP snapshots retain the approved description while offline and remove it on revocation. A description-only change follows the non-expanding capability-diff rule.

Provider projection follows the provider's identity shape:

- a namespace-capable protocol serializes a namespace definition plus local child name and returns the same tuple on its call;
- a flat-only protocol uses the snapshot's `ProviderFlatName`;
- the snapshot owns both directions of the flat index; distinct tuples that produce the same alias are truncated as needed and suffixed from SHA-1 over `namespace-or-empty + NUL + name`;
- dispatch MUST NOT parse a flat alias to recover a namespace, and namespace-capable protocols MUST NOT flatten before dispatch.

A provider that forwards a call elsewhere sends `ToolName(namespace, name)` and `ProviderFlatName` as data, and the far end presents the same identity to the real provider. A rebuilt declaration is never invoked where it was rebuilt; the tool runs where it was declared.

Provider call identifiers, canonical `ToolName`, `ProviderFlatName`, source-routing identities, and Session item identifiers are stored and projected separately and survive resume, fork, compaction, and history reconstruction without being substituted for, parsed from, or regenerated from one another.

### 6.1 Freeform input

A definition MAY declare a freeform input: a raw string constrained by a grammar (syntax and definition text) plus the name of the one required string parameter that carries it. Its input schema is an object with that parameter. Freeform input is a projection choice, not a separate identity or dispatch path: every call reaches the dispatcher as an object with that parameter.

- Support is the provider capability `SupportsFreeformTools`. An explicit provider value wins; otherwise it is on for ChatGPT OAuth and for API-key OpenAI-protocol providers on the official OpenAI endpoint, and off elsewhere, since compatible third-party endpoints may reject `custom` tools.
- OpenAI Responses with freeform support projects a `custom` tool whose `format` is `{ "type": "grammar", "syntax", "definition" }`, without `parameters` or `strict`, inside a namespace container when namespaced. A `custom_tool_call` becomes a call with arguments `{ <parameter>: input }` marked as a custom call; its result is sent as `custom_tool_call_output`.
- Every other protocol, and Responses without freeform support, projects the ordinary function.
- Dispatch marks a call as custom when its registration declares freeform input, whatever produced it. A standard `ToolCall` item persists that flag (never on the wire), and history rebuilt from items marks recorded freeform calls as custom.
- Replay on Responses with freeform support sends `custom_tool_call` / `custom_tool_call_output` only for calls marked custom, never consulting the request's declared tools. A request to a Responses provider without freeform support sends stored `custom_tool_call` items as `function_call` with arguments `{ <parameter>: input }` (the parameter from the request's freeform tools, `input` for an undeclared name) and their outputs as `function_call_output`.
- Schema sanitization never rewrites a freeform definition.

## 7. Core contracts

| Contract | Responsibility |
|---|---|
| `ToolName`, `ProviderFlatName`, `SourceToolId`, `ToolDefinitionId`, `RuntimeBindingId` | Typed identities. |
| `IToolSource` | Contribute definitions and runtime bindings for a planning context. |
| `IToolRuntime` | Execute one authorized invocation using an invocation context. |
| `IToolBindingLease` | Live availability/revocation/generation checks for a binding. |
| `IToolDispatcher` | The common invocation pipeline. |
| `ToolPlanningContext` | Immutable inputs for the next Turn snapshot, including `ToolPlanningThreadKind`; its Turn identity is not an execution identity. |
| `ToolInvocationContext` | Live thread, Turn, call, audience, cancellation, approval, and authority inputs captured at the execution boundary. |
| `ToolExecutionResult` / `ToolError` | Normalized result; stable error code, English fallback, and optional structured parameters. |
| `EffectiveToolSnapshot` | Per-Turn registration set plus canonical, composite-provider, and flat-alias indexes. |
| `ToolPresentationDescriptor` | Trusted `PresentationId` plus bounded renderer options; no free-form renderer selector. |

`ToolPlanningThreadKind` is a trusted Session-derived classification (`UserTopLevel`, `SubAgentChild`, `Unattended`, `Internal`, `Unknown`), derived once from persisted thread origin, source, visibility, and configuration. Sources treat `Unknown` as ineligible for privileged entrypoint tools and do not replace this classification with channel-name denylists.

Modules contribute tools only through tool sources and the typed contracts above.

### 7.1 Compile-time C# tool declarations

Every first-party tool whose model-visible contract is known at compile time derives its name, description, input schema, and output schema from the DotCraft source generator, including tools with a custom runtime or provider-specific wrapper. Declaration-only contracts use the typed declaration surface rather than JSON Schema strings or hand-built schema objects; conditional input relationships use an explicit discriminator plus runtime validation.

Generated parameter objects are closed by default, including nested DTOs; explicitly open JSON values remain open. CLR enum parameters are model-visible camelCase strings unless `JsonStringEnumMemberName` overrides a value; invocation deserializes with the same rules and rejects numeric enum values.

Schemas discovered or supplied at runtime are exempt: MCP servers, plugins, channel adapters, App Bindings, Runtime Dynamic tools, and provider translation layers.

### 7.2 Strongly typed tool implementations

Native and .NET plugin authors SHOULD implement ordinary tools as methods marked `[Tool]` or `[GeneratedTool]`, with typed business parameters and constructor-supplied services; a tool source supplies namespace, exposure, and policy, and the shared adapter owns registration and argument conversion. `[ToolDeclaration]` and custom runtimes remain available for specialized adapters. The generator also emits a per-type factory returning every executable tool on one instance.

A tool's trusted presentation is part of its declaration: `[ToolPresentation(id, Operation = ...)]` plus bounded `[ToolPresentationOption(key, value)]` options, validated against a 4,096-byte option budget. Sources MUST NOT select presentation by tool name. Provenance still decides which renderers may honor it, so the same declaration on a plugin tool selects only plugin renderers. Core may add one option derived from an invocation when recording its item, only for a fact Core alone resolves: `memory`, which marks a file tool call targeting the Thread's memory store.

A method MAY request one required, by-value `ToolInvocationContext` parameter for live invocation identity. That parameter and `CancellationToken` are excluded from the model schema and JSON binding, and model arguments cannot override them. Each invocation receives its own context; a function that requires context fails before its business method runs when none is supplied. Task isolation remains the business service's responsibility.

Methods returning `ToolExecutionResult` (directly or as a task) keep the result object; it has no generated output schema, and a null result is `tool_result_invalid`. Methods returning `AIContent` or a collection of it return model content, also without an output schema. Other return values keep their serialization. An explicit business result, including an uncertain external outcome, is never overwritten by the adapter, and returning an envelope cannot acquire host-private result authority.

Exception conversion preserves the category, supplies a nonempty fallback, and keeps the original exception and call identity in host diagnostics. Cancellation remains cancellation. Native and remote results preserve failure content and structured data alongside the success flag.

### 7.3 File tools

File mutation success reflects the disk write. A later diff-reporting failure produces a warning without turning a completed write into a failure; the [file-change payload](session-core.md#toolresult) records the write outcome independently of the diff. An unknown write outcome invalidates the local aggregate diff; a rejected write leaves it intact.

`ReadFile`, `WriteFile`, and `EditFile` decode a file as UTF-8 unless a UTF-8, UTF-16, or UTF-32 byte-order mark selects that encoding. They return an error for bytes that are not valid text in that encoding and never rewrite such a file; `GrepFiles` still searches it.

`GrepFiles` accepts a file or directory path; a file target reports its name and matching line numbers, directories recurse, and include patterns filter directory scans. No file is skipped for size. Path authorization, binary-file filtering, cancellation, the search timeout, and the match limit always apply. Missing targets are reported as missing paths.

`EditFile` treats `oldText` and `newText` as already-decoded strings. It matches in stages: non-overlapping exact substrings, then whole-line sequences ignoring trailing whitespace, then leading and trailing whitespace, then common Unicode punctuation and space differences. A stage with no candidates advances; a stage with several candidates rejects the edit without writing. A unique match is replaced at its source range; normalization never becomes replacement content. `replaceAll` uses exact matches only. Matching treats CRLF, CR, and LF line breaks alike. The file keeps its own line endings outside the edited region, and new lines in the edited region take the file's first line ending. Encoding policies apply; replacement text is not otherwise reformatted. A rejected edit quotes the `oldText` it searched for. A snippet ending in LF includes the trailing empty segment, which must match an empty or whitespace-only line or the file's final empty segment.

### 7.4 Shell execution

A returned command result is a successful tool invocation even when its exit code is nonzero; the exit code and `CommandExecution` status describe the command. Launch, authorization, infrastructure, timeout, and cancellation failures remain tool failures. Commands preserve their quoting; output is decoded as UTF-8 with replacement, never splitting characters at stream boundaries.

Background terminal output is retained in a 1 MiB buffer that keeps the beginning and end independently of the complete disk log. Truncated previews split the character budget between those excerpts and mark the omitted middle; truncated foreground results include the log path. Live output is limited to 8 KiB per delta, 10,000 deltas per terminal, and the configured live-byte budget; exhaustion stops notifications while logging and execution continue. Completion follows output drain and log flush, but once the process has exited, output that does not reach end of stream within 2 seconds is not awaited, so a descendant holding the pipes cannot keep the terminal open. An exited process releases its active entry even if metadata persistence fails, and its final snapshot is readable as soon as completion is notified.

The model reads a terminal through a per-terminal cursor. `Exec` returns the bounded preview of all output so far; `WriteStdin` returns only output after the cursor, bounded by `maxOutputChars` with the same head and tail excerpts, marking any gap the buffer no longer holds and naming the log. Both move the cursor to the end. Empty `WriteStdin` input waits for `yieldTimeMs` (30 seconds by default, capped by `Tools.Shell.Background.MaxYieldTimeMs`) and returns early on exit; it reads final output and exit code until retention expires. Nonempty input to an exited terminal fails. End-of-text (`\u0003`) is written like any other input and does not stop the process. The model stops a terminal with `TaskStop`, which stops its process tree. Client terminal reads and writes return the full bounded snapshot and do not move the model cursor.

`Exec` rejects a command whose first segment is only a delay of two seconds or more (`sleep N`, any seconds or milliseconds form of `Start-Sleep`, or `timeout /t N`) and directs the model to `runInBackground` and the completion notification. Shorter delays, delays inside a larger script, and `runInBackground` commands are allowed.

`Exec` and `WriteStdin` accept `waitFor`, a .NET regular expression matched against unread output as it arrives. A match ends the wait early: `Exec` returns the session ID of the still-running process, even for a foreground command, and `WriteStdin` returns the new output; the result names the matching line. Exit, the wait time, cancellation, and foreground hand-over still end the wait. A pattern that does not compile fails before a process starts or input is written. Each evaluation has a one-second timeout and sees only retained output.

A stall watchdog checks running terminals every 5 seconds. A terminal stalls when its output has not grown for `Tools.Shell.Background.StallWatchdogSeconds` (45 seconds; 0 disables) and its last nonempty line looks like an interactive prompt: a `(y/n)`, `[y/n]`, or `(yes/no)` choice, `Press Enter`, `Press any key`, `Continue?`, `Overwrite?`, or a question starting with "Do you", "Would you", "Shall I", "Are you sure", or "Ready to". A stall is reported once until more output arrives: it publishes `terminal/stalled` with the bounded snapshot, ends a waiting `WriteStdin` with a result saying the process appears to wait for input, and, when no model call is waiting and the terminal is armed for completion notification, delivers a stall task notification. The terminal stays armed for its completion notification.

### 7.5 Worktree tools

`EnterWorktree` and `ExitWorktree` let the agent move its own thread into and out of a managed worktree ([Session Core §4.1.1.2](session-core.md#41112-worktrees)). They are exposed to top-level threads whose state workspace is a local Git repository, so their presence does not change with mode or worktree state; Plan mode rejects them at execution time.

- `EnterWorktree` takes an optional `name` or an optional `path`. Without `path` it creates a worktree on a new branch from the current `HEAD`, named from `name` when given; uncommitted changes are not copied. `path` re-enters a managed worktree of this workspace that no other thread is bound to. It fails when the thread is already in a worktree.
- `ExitWorktree` takes `action` (`keep` or `remove`) and optional `discardChanges`. `keep` leaves the worktree and its branch on disk; `remove` deletes both and is refused, listing what would be lost, when the worktree has uncommitted changes or commits beyond its base unless `discardChanges` is true. Neither moves changes into the local checkout. `remove` is refused while a background terminal started in the worktree is running.
- Either tool runs alone: other tool calls of the same model response run before it, against the previous location. Calls that still overlap, such as concurrent calls from a Code Mode cell, move the thread one at a time.
- After success, later tool calls in the Turn resolve relative paths, default search roots, and the default shell working directory against the new location. File and shell boundaries keep the Turn's admitted roots plus the worktree. LSP, Code Mode cells already running, background terminals, and SubAgents keep the location they started with. The next Turn rebuilds the agent at the new location.
- The result states the new working directory and branch. The location change also reaches the model through the `environment` world-state section ([Prompt Composition §4b](prompt-composition.md#4b-thread-context-items)).

## 8. Snapshot and invalidation semantics

Each Turn executes against one immutable `EffectiveToolSnapshot`. Registration, schema, exposure, and presentation changes take effect on the next Turn, preserving prompt-cache and invocation consistency.

The following invalidate the next snapshot:

- workspace, thread, plugin, or binding MCP configuration changes;
- tool-source enablement changes;
- Runtime Dynamic declaration replacement;
- binding capability snapshot acceptance;
- external channel tool connection publication, disconnection, or replacement;
- mode or profile changes that truly alter the runtime surface.

Immediate safety checks are not frozen. Revocation, disconnect, expired authority, binding removal, and execution-policy invalidation block dispatch immediately, including for an invocation named in an older snapshot.

Adapter-declared channel tools are connection-bound: binding id, descriptor set, lease, and executor refer to the same adapter connection, and a call is never retargeted to a newer connection. When the connection changes, the current Turn keeps its snapshot but loses dispatch authority; the next Turn rebuilds.

Operational mode restrictions SHOULD keep stable tool schemas and enforce policy at execution time unless the mode is a genuinely different role or runtime surface.

## 9. Result and audience contract

| Field | Audience | Rule |
|---|---|---|
| `content` | model and text fallback clients | Text/image content for model history. A successful model-visible call MUST produce non-empty model content. |
| `structuredContent` | client/view only | Structured application data, never automatically inserted into model context. |
| `_meta` | host/view only | Private host or UI metadata that never enters model context. |

Failures include a concise textual fallback plus stable `errorCode`; `errorMessage` is an English fallback and MAY carry structured parameters.

`structuredContent` is never silently serialized into model content; a source returning structured data without useful model content violates the contract. Adapters MAY generate an explicit, bounded model summary when they know the semantic shape; ACP does so for structured-only successful results.

On the Dynamic wire, `contentItems` carries rich content and `structuredContent` carries client data. MCP results preserve standard `content`, `structuredContent`, and `_meta` semantics. Native and plugin results use the same audiences.

## 10. Session item projection

Each registration declares exactly one projection shape; the recorder never infers it from a call name or result payload.

| Invocation source | Target projection |
|---|---|
| Core or Plugin Native | standard `ToolCall` followed by `ToolResult` |
| MCP | `McpToolCall`, preserving raw MCP result and metadata under audience rules |
| Runtime Dynamic | one `DynamicToolCall` lifecycle item; no companion `ToolResult` |
| Core Native image generation | one `ImageGeneration` lifecycle item; no companion `ToolCall` or `ToolResult` |
| Core Native subscription web search | one `WebSearch` lifecycle item; no companion `ToolCall` or `ToolResult` |

Plugin provenance (`pluginId`, `functionId`, namespace) remains available on the standard projection.

Items record canonical `ToolName`, `ProviderFlatName`, definition identity, runtime-binding identity and revisions where applicable, snapshot revision, safe source provenance, trusted presentation, call identifier, arguments, status, duration, success, stable failure data, and audience-safe result fields. MCP items also record the runtime server name used for routing. Credentials and raw connection state are never persisted.

History reconstruction uses the persisted canonical tuple for namespace-capable protocols and the persisted flat alias for flat-only protocols, never the current inventory, alias parsing, or regenerated aliases. Replay is therefore independent of reconnects, renamed plugin runtimes, source ordering, and later tool-set changes.

Items of a call dispatched with an invocation origin persist that origin's kind, which is never projected to the wire. History reconstruction skips items whose origin is `codeMode`, because the model saw only the enclosing call's result.

Projection is atomic per Turn, call identifier, and projection shape. Streaming argument observation and dispatcher recording upsert the same item. A specialized lifecycle item transitions in place from started to exactly one terminal state; a standard projection creates or updates exactly one `ToolCall` and appends exactly one terminal `ToolResult`. Cancellation, timeout, rejection, and failure race through the same terminal guard; no path publishes a second terminal result or leaves an accepted call permanently started.

## 11. Runtime Dynamic Tools

Runtime Dynamic Tools are callbacks owned by the active AppServer client for a thread.

### 11.1 Declaration

The wire declaration is a tagged union:

- `Function`: `{ type: "function", name, description, inputSchema, deferLoading?, approval? }`; a top-level function is `ToolName(null, name)`;
- `Namespace`: `{ type: "namespace", name, description, tools: Function[] }`; contained functions inherit that namespace.

`approval` is the only DotCraft-specific field; `deferLoading` maps to Direct/Deferred and other exposure decisions stay server-owned. Any function with `deferLoading: true` MUST be inside a namespace. A top-level function's namespace is exactly `null`, never a source-owned default. Runtime Dynamic registrations are runtime-managed and stay available independently of Agent Profile tool allow/deny lists; mode policy, approval policy, connection leases, and Thread/Turn authority still apply.

### 11.2 Lifetime

Declarations and callbacks are connection-owned. Resume requires explicit rebinding:

- omitted declarations keep the bound set only when the request comes from the binding's current owning connection generation;
- an empty array clears Runtime Dynamic Tools;
- a non-empty array atomically replaces the set.

A new or non-owning connection cannot take over by omitting declarations; it submits a non-empty replacement and passes thread/connection authority. Whether a non-owner may clear with `[]` is decided by thread authority. Failed replacement leaves the previous binding unchanged.

Live executors are never persisted; a non-sensitive last-known declaration summary MAY be kept for diagnostics but is never a live executor. After disconnect, calls fail quickly with a stable disconnect category; timeout and protocol failures use distinct categories.

### 11.3 Dynamic content items

The Dynamic `contentItems` wire supports exactly:

- `{ "type": "text", "text": string }`, non-empty;
- `{ "type": "image", "mediaType": string, "url": string }`;
- `{ "type": "image", "mediaType": string, "dataBase64": string }`.

An image item provides exactly one of `url` or `dataBase64`; data URLs are not accepted in `url`. Sizes and counts are validated against limits owned by the AppServer protocol. Unknown or invalid items make the callback result invalid. A successful model-visible Dynamic call still includes at least one useful text item.

### 11.4 Result and lifecycle

`DynamicToolCall` uses `inProgress`, `completed`, or `failed`. At start `success` is null; completion includes `durationMs`, audience-separated result fields, and stable errors. `itemId` and provider `callId` stay separate. Runtime Dynamic metadata does not define iframe UI; interactive UI uses MCP Apps.

## 12. MCP architecture

MCP is the standard external tool and interactive app transport. DotCraft supports three independent MCP origins: workspace configuration (including enabled plugin-contributed servers), per-thread configuration, and App Binding MCP sessions.

### 12.1 Thread configuration semantics

`ThreadConfiguration.McpServers` has three states:

| Value | Meaning |
|---|---|
| `null` | inherit workspace/plugin MCP configuration |
| `[]` | disable user-configured workspace/plugin MCP for this thread |
| non-empty | replace workspace/plugin MCP with this thread list |

Binding MCP is additive and is never removed or overridden by the thread list.

#### 12.1.1 Policy boundary

MCP registrations are governed by the thread's MCP policy (server selection and namespaced tool selectors), not by the Agent Profile's tool allow/deny name lists: MCP origins are chosen by configuration, the thread's host, or an App Binding, which the Profile author does not enumerate, so a name list would withhold their tools silently. Mode policy, approval policy, connection leases, and Thread/Turn authority still apply.

### 12.2 Runtime and control surface

OAuth login and standard form and URL elicitation forwarding are generic MCP control-plane capabilities exposed through the AppServer protocol. Thread archive/disposal closes thread and binding MCP sessions. Status distinguishes workspace, thread, plugin, and binding origins, and a server that fails to start reports the server and the reason whatever its origin.

Streamable HTTP is only an OAuth candidate transport and does not by itself imply that authentication is supported or required. Runtime status, not transport shape or error text, is the authority for OAuth UX: clients offer authentication only when the server reports `authStatus: "notLoggedIn"`, as reauthentication when `failureReason: "reauthenticationRequired"`. Unknown discovery results fail closed. A connected server with usable OAuth credentials reports `authStatus: "oAuth"`.

Startup readiness depends on initialization and tool discovery. Optional resource and resource-template inventory never fails an otherwise usable server; `toolsAndAuthOnly` status reads do not enumerate resources, and full reads treat enumeration failure as an empty inventory.

### 12.3 Approval

MCP tool approval evaluates standard annotations (read-only, destructive, open-world) together with thread policy and DotCraft authority. Registration or App Binding approval does not bypass invocation approval. MCP App-initiated calls use the same policy.

## 13. MCP Apps host

DotCraft targets the stable MCP Apps extension `io.modelcontextprotocol/ui` dated 2026-01-26. Every DotCraft MCP session advertises support for `text/html;profile=mcp-app`; the capability belongs to the session lifecycle and does not change as clients connect or disconnect. View presentation behavior belongs to the client.

### 13.1 Supported capabilities

The host supports only capabilities it advertises:

- inline and fullscreen display modes (no picture-in-picture);
- same-server tool and resource access;
- logging and safe external-link requests;
- `ui/message`;
- `ui/update-model-context`.

`ui/message` submits a new source-marked Turn; it is accepted only from a live, visible view and is rate-limited.

`ui/update-model-context` stores one last-write-wins value per live view, injected once into the next user- or UI-message Turn and then consumed. It is not durable thread configuration and is not appended to history. An ordinary user Turn consumes all pending contexts for its thread; an accepted `ui/message` consumes only its view's context. Context is bounded, untrusted transient input and cannot alter authority, policy, or system instructions.

### 13.2 Visibility and authority

An omitted visibility value means model-and-app visibility. App-only tools stay in the registry but are excluded from model projection. A view may call only same-server tools visible to the app, and every call passes normal authority and approval.

Visibility controls invocation authority, not presentation eligibility. A terminal result may render its View when the persisted `ui://` association still matches the current tool definition and runtime, even if the originating tool is not app-visible; rendering does not make that tool app-callable.

Visibility is read only from nested `_meta.ui`. An empty array means neither audience; an unknown value invalidates the declaration and exposes the tool to neither audience.

UI linkage uses `_meta.ui.resourceUri`; `_meta["ui/resourceUri"]` is accepted only when the nested field is absent, and an invalid nested declaration fails closed rather than falling back to the alias. The URI MUST be absolute `ui://`. The response MUST match that URI, use `text/html;profile=mcp-app`, and contain exactly one text document or base64 blob.

MCP App presentation has three lifetimes. The normalized `ui://` association and bounded result are persisted with the terminal `McpToolCall`. Availability is decided only when a client opens the View, against the current definition, runtime, and authority; projections expose the persisted association and never inspect the live runtime. The View document, bridge connection, resource body, and opaque `viewHandle` exist only for one active View and are never persisted.

Core issues a new opaque `viewHandle` for every View, resolving to immutable server/session, authority revision, `SourceToolId`, and resource URI. The View sends only stable MCP Apps messages, allowed tool names/arguments, and the handle; it cannot select server, session, binding, source tool, or resource URI.

History reads and resume may advertise a new View only when the persisted association matches the current registration and authority; opening it fetches the current resource and binds a new handle to the current MCP generation, never restoring a previous View, handle, pending context, or permission. Offline, removed, changed, or revoked associations render the generic result. Rollback, archive, delete, generation replacement, binding revoke, plugin disable, configuration replacement, disconnect, and explicit close invalidate affected handles immediately.

App-initiated `tools/call` uses the common dispatcher with App audience and a server-generated call id; it creates no Turn, Session item, or provider-history entry.

### 13.3 Isolation

A View host isolates untrusted resources, enforces declared and host policy, and scopes every bridge operation to one live handle. A View gains no filesystem, shell, arbitrary network, cross-server tool, host-process, or unrelated client authority. Resource `domain` metadata does not choose a real origin. Safe links are limited to HTTPS, `mailto`, and explicit loopback HTTP.

## 14. Presentation boundary

Presentation is optional and preserves model/text fallback content. Local renderers and assistant visualizations are separate paths; neither grants tool execution authority.

### 14.1 Trusted local renderer registry

A trusted local renderer registry is independent of MCP Apps. Core renderers and installed, enabled client plugins register renderers selected by exact ordinal `PresentationId`. Active plugin entries are ordered by priority and stable plugin/contribution identity, followed by the Core renderer and generic fallback. Registrations are withdrawn when their owning generation ends, and bounded options are validated by the selected renderer.

The projected `PresentationId` selects only an already active local renderer; tool names, arguments, results, MCP metadata, and other payload data cannot supply module paths or code. Unknown or unavailable ids use the Core or generic fallback. Rendering families, grouping, and interaction belong to the client.

### 14.2 Assistant visualization boundary

Visualization is an assistant-message presentation path, not a tool-result payload. A completed `AgentMessage` may reference a View with a visualization content reference on its own line, in either form:

```text
visualize{"path":"<absolute-path>/example-name.html"}
visualize{"path":"<absolute-path>/example-name.html"}
```

The second form wraps the same JSON object in the private-use delimiters U+E200, U+E202, and U+E201. The object has a required absolute `path`, an optional non-empty `title` of at most 200 characters, and an optional `mode` whose only value is `"wide"`. Any other key, a malformed object, or an invalid value is not a reference. The file name MUST match `^[a-z0-9]+(?:-[a-z0-9]+)*\.html$` and the path MUST NOT contain `.` or `..` segments.

The reference remains ordinary persisted assistant text and introduces no Session item, delta, payload kind, or provider-history type; clients without the capability show it as text.

Only a completed `AgentMessage` containing the reference outside fenced code may authorize a View. The path MUST resolve, case-insensitively on Windows, to a file directly inside `<SessionThread.WorkspacePath>/.craft/visualizations/<threadId>/`, with no user-global fallback; execution and worktree overrides do not change ownership. Authoring uses ordinary file tools.

Opening a View always reads the current file, so a rewritten file shows its latest version in every message that references it. These files are transient workspace resources with no archive, fork, migration, or cross-device guarantee. Permanently deleting a thread deletes its visualization directory on a best-effort basis without following reparse points.

The host issues a connection-owned opaque handle after revalidating the connection/thread binding, completed source item, exact path, workspace boundary, reparse-point safety, and current file. The handle binds its source thread, Turn, Item, and path, which a View cannot override. View follow-up starts or queues a source-marked Turn and cannot forge user or channel identity.

A View may save a widget state snapshot `{ modelContent, privateContent }`, each any JSON value or `null`. The thread owns one snapshot per visualization, keyed by normalized path (case-insensitive on Windows) and stored with the thread's metadata, so every View of that path, on any client and after reload, opens with the latest snapshot. Each save replaces it; a snapshot with both fields `null` is removed. Snapshots are deleted with their thread and not copied by fork.

Saving never starts a Turn. `modelContent` reaches the model as the `visualization_state` [world-state section](world-state.md): before a model request, each visualization whose `modelContent` differs from what the model last saw is delivered keyed by path, and one that became `null` or was removed is delivered once as cleared. The section labels `modelContent` as untrusted data, not instructions. `privateContent` restores View presentation only and never reaches the model. A follow-up may carry explicit View context, appended to the user's message as a visible JSON block.

## 15. App Binding boundary

An application binding supplies independently revocable authority for its tool registrations. Binding identity, approved capability revision, live executor health, and model exposure are separate facts. Capability expansion requires accepted authority before dispatch; revocation blocks existing snapshots immediately. Offline definitions may remain visible as non-executable stubs.

Binding-scoped connections and credentials stay isolated from shared MCP connections. Credentials never enter model content, tool arguments, persisted definitions, or client-private result metadata.

## 16. Protocol consistency

Core, clients, and the SDKs use the same canonical tool identity, Runtime Dynamic declaration, MCP Apps, and App Binding contracts. Unsupported fields and method names are rejected rather than interpreted as alternate shapes.

## 17. Security invariants

1. A definition, View, remote server, or invocation argument cannot grant authority.
2. Revocation and expiry are enforced at dispatch time, not only at snapshot construction.
3. Model content, structured client content, and host-private metadata never cross audience boundaries implicitly.
4. Remote metadata cannot select trusted local code.
5. Binding MCP cannot launch local executables supplied by an app.
6. Conversation targets are server-derived and cannot be overridden by tool arguments.
7. Persisted diagnostics contain no bearer, credential, live executor, or sensitive `_meta`.
8. Interactive UI is optional; text/model fallback remains sufficient for correctness.

## 18. Observability and diagnostics

Diagnostics SHOULD identify canonical `ToolName` and safe provenance, provider projection shape and `ProviderFlatName` when used, source kind and MCP origin, snapshot revision, exposure and authority decision reason, approval decision, call/item identifiers, duration, outcome, stable error code, and connection/binding capability revision without secrets.

Status and audit views distinguish declaration availability, model exposure, live executor health, and authority.

A capability that policy keeps out of the model's tool list is recorded with its model-visible name, namespace, usage source, and the refusal that hid it, since it produces no call, result, or error that would otherwise show it was withheld.
