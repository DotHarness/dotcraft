# DotCraft Model Runtime

| Field | Value |
|---|---|
| Version | 0.8 |
| Status | Approved |
| Date | 2026-08-06 |
| Parent Spec | [Session Core](session-core.md) |

## 1. Overview

DotCraft owns the complete lifecycle of a model-driven turn. The model runtime translates a
`Thread -> Turn -> Item` conversation into provider requests, consumes provider stream events,
dispatches tools, and projects the result back into Session events and durable rollout records.

Microsoft.Extensions.AI (MEAI) is DotCraft's provider-neutral content, tool, chat-client, and
streaming abstraction layer. DotCraft adds lifecycle and provider-native state only where MEAI's
generic aggregation cannot represent a required protocol invariant.

OpenAI Responses, OpenAI Chat Completions, and Anthropic continue to use MEAI-compatible clients
while retaining protocol-native request, response, history, and cache behavior.

This specification defines the finished architecture and its behavioral contract.

## 2. Goals

- Make Session Core's Thread, Turn, Item, and rollout model the only lifecycle authority.
- Preserve MEAI's `IChatClient`, `ChatMessage`, `AIContent`, `ChatOptions`, `ChatResponseUpdate`,
  `AITool`, and `AIFunction` abstractions wherever they faithfully express DotCraft behavior.
- Preserve provider-native information before any known-lossy MEAI aggregation boundary.
- Keep the tool loop structurally aligned with MEAI `FunctionInvokingChatClient`, adding only the
  DotCraft behavior required by tools, approvals, guidance, retry, and provider history.
- Keep provider request construction isolated by protocol.
- Preserve existing AppServer behavior, persisted conversations, tool execution semantics, and
  provider wire shape unless a protocol change is explicitly specified.
- Provide stable boundaries for Responses cache-session identity optimizations.

## 3. Scope

The runtime covers:

- MEAI model input, multimodal content, messages, options, tools, and streaming updates;
- ordered generic conversation history and optional provider-native history;
- provider response-item identity before lossy aggregation;
- tool-call assembly, policy, approval, dispatch, result projection, and iteration;
- retries, cancellation, guidance, compaction, and terminal failure;
- usage, tracing, and final transport-shape diagnostics;
- root, subagent, fork, rollback, resume, and context-window lifecycle integration;
- OpenAI Responses, OpenAI Chat Completions, and Anthropic transports;
- stability of rollout, model-history, provider-history, and AppServer contracts.

## 4. Non-goals

- Defining one lowest-common-denominator wire format for all providers.
- Replacing MEAI abstractions merely to make them DotCraft-owned.
- Removing `AIFunction`, `AIFunctionFactory`, delegating tool abstractions, or DotCraft's generated
  `AIFunction` wrappers and source generator.
- Introducing parallel DotCraft equivalents for `AIContent`, `ChatMessage`, `ChatOptions`,
  `ChatResponseUpdate`, `AITool`, `AIFunction`, or `IChatClient` without a demonstrated protocol or
  lifecycle conflict.
- Replacing provider SDKs when a maintained SDK exposes the required protocol faithfully.
- Changing model-visible tool names, schemas, ordering, or policy merely to fit a new runtime.
- Rewriting Session Core persistence or public AppServer protocols as part of runtime ownership.
- Treating UI-oriented Session Items as the source of provider request history.
- Persisting OAuth credentials, protected reasoning plaintext, or arbitrary provider runtime
  objects.

## 5. Authority and Boundaries

### 5.1 Session authority

Session Core owns:

- Thread lineage and configuration;
- Turn creation, status, cancellation, and queueing;
- Item lifecycle and AppServer event emission;
- approvals, user-input requests, hooks, goals, and subagent lifecycle;
- rollout, rollback, fork, compaction, and cold-resume semantics.

The model runtime receives an immutable turn execution snapshot and emits runtime events. It does
not create a second durable session object or independently infer thread lineage.

Session Core reconstructs a `List<ChatMessage>` directly from rollout records. That request-local
list is the MEAI model-history projection for the active turn; it is not serialized as an
independent session object. `ChatClientAgent` contains only the configured `IChatClient`, cloned
`ChatOptions`, the optional prompt context provider, and agent metadata.

### 5.2 Runtime authority

For one active Turn, DotCraft's MEAI-aligned execution layer owns:

- sampling attempts and their retry state;
- ordered provider input and output;
- partially assembled stream items;
- pending and completed tool invocations;
- the transition to another sampling request or terminal completion.

All mutable state belongs to one explicit turn execution. Async-local scopes may expose immutable
request context to adapters, but they are not authoritative storage. A scope is entered before the
dependent pipeline executes and is disposed on success, failure, or cancellation. Nested scopes
restore their parent value and turn-scoped state must not leak into another invocation.

`ChatClientAgent` invokes the configured `IChatClient` directly, streams
`ChatResponseUpdate` values without introducing a parallel event hierarchy, aggregates the
completed response with MEAI's standard helpers for generic model history, and appends the
request/response messages only after successful completion. Responses provider items continue to
be captured before this generic aggregation boundary.

### 5.3 Provider authority

Each provider-specific `IChatClient` adapter owns:

- protocol request and response serialization;
- authentication-independent request shaping;
- native tool projection;
- provider item and call identity validation;
- native reasoning and protected data;
- cache controls;
- conversion between native stream events and MEAI `ChatResponseUpdate` values.

Authentication and routing policies consume an already-resolved request identity. They do not
infer Session lifecycle from tracing state or ambient client instances.

## 6. MEAI Baseline and Extension Policy

MEAI is the default design and implementation reference. DotCraft does not introduce a competing
abstraction unless an accepted test demonstrates that the MEAI contract cannot preserve required
Session or provider behavior.

The exact implementation baselines for this design are:

- `Microsoft.Extensions.AI` and `Microsoft.Extensions.AI.OpenAI` 10.5.1:
  `dotnet/extensions@2d4d2df0ba38ee9aa0ed363ddab33d7ae7880b6d`;
- the resolved `Microsoft.Extensions.AI.Abstractions` 10.5.2:
  `dotnet/extensions@2a86d759c251eee39274c191bd9f8e14c58f875a`.

Later MEAI versions may be adopted through an explicit dependency upgrade, not silently mixed
into an architecture refactor.

### 6.1 Conversation items

Generic runtime content uses the MEAI `ChatMessage` and `AIContent` hierarchy. This includes:

- system/developer/user/assistant messages;
- visible text and multimodal data;
- reasoning summaries, protected reasoning, and provider item identity;
- function calls and function results;
- hosted/provider tool calls and outputs;
- usage and protocol error information;
- DotCraft `AIContent` subclasses for content kinds not yet represented by MEAI.

Provider item ID and tool `call_id` are distinct identities. Converting or aggregating content must
never silently substitute one for the other.

`AdditionalProperties` carries small, JSON-safe cross-layer metadata where MEAI explicitly
supports extension. `RawRepresentation` is request-local and is never a durable persistence
contract.

When a provider requires an item ordering, identity, or protected payload that MEAI aggregation
cannot preserve, that provider uses a separate native ledger. The ledger supplements rather than
replaces generic MEAI history.

### 6.2 Model request

Model requests continue to use `IChatClient.GetStreamingResponseAsync`, ordered
`IEnumerable<ChatMessage>`, and `ChatOptions`. DotCraft supplies:

- the immutable thread/turn/request identity;
- model and protocol selection;
- ordered MEAI conversation input;
- ordered `AITool` definitions;
- MEAI reasoning and output options;
- protocol-specific validated options;
- cancellation and observability context.

The request reaching the provider adapter is complete. `DelegatingChatClient` middleware and HTTP
policies may observe or serialize it but must not reconstruct lifecycle identity.

### 6.3 Stream events

Provider clients emit `ChatResponseUpdate` with typed `AIContent` for:

- response and output-item start/completion;
- text and reasoning deltas;
- tool-call argument deltas;
- completed tool calls and hosted items;
- usage;
- provider turn state;
- retryable and terminal failures.

Event order is part of the runtime contract. Provider-native identity must be attached before
`ToChatResponse`, message coalescing, history projection, or tool dispatch.

### 6.4 Extension criteria

DotCraft extends MEAI only for a documented Session or provider requirement:

- response coalescing cannot be the sole Responses wire-history authority because it cannot
  preserve every raw representation, item boundary, or content metadata field;
- function invocation requires streaming argument previews, concurrent execution, approval and
  mode policy, same-Turn guidance, bounded retry integration, and provider-history hooks;
- provider request identity comes from Thread lifecycle rather than ambient tracing or client
  middleware.

Any additional extension requires a failing characterization test and a spec update. The
preferred implementation is a narrow subclass, adapter, or MEAI-shaped extension rather than a
parallel content, tool, or chat-client model.

### 6.5 Agent facade

`ChatClientAgent` is the immutable DotCraft facade over one configured `IChatClient` pipeline. Its
options contain stable agent identity (`Id`, `Name`, and `Description`), default `ChatOptions`, and
an ordered snapshot of context providers. The facade also exposes provider metadata derived from
the configured chat client. Mutable option objects and collections are cloned at construction and
again for every invocation.

The facade exposes aggregated and streaming runs:

- `RunAsync` accepts a string, one `ChatMessage`, or an ordered message collection and returns the
  aggregated `ChatResponse`;
- `RunStreamingAsync` yields the provider's ordered `ChatResponseUpdate` sequence;
- both forms accept optional invocation-specific run options and caller-owned model history;
- both forms append the input plus completed response
  messages only after successful model completion;
- a failed or cancelled request does not append partial input or response messages;
- streaming cancellation is forwarded to the chat-client enumerator, whose disposal completes
  before the run exits;
- context providers receive terminal notification after success, failure, or cancellation;
- response messages and streaming updates receive the configured agent name only when they do not
  already identify an author.

Each invocation builds a new request list containing the supplied history followed by all current
input messages. String input is converted to a user-role message, and an omitted history starts
from an empty invocation-local list. The caller's list remains the durable history projection; the
request list and cloned options are invocation-local.

Invocation `ChatOptions` merge with agent defaults as follows:

- request scalar values win; unset request scalar values inherit agent defaults;
- agent instructions precede request instructions, separated by one newline;
- request additional properties win and missing keys inherit agent defaults;
- a request raw-representation factory runs first and falls back to the agent factory only when it
  returns `null`;
- request stop sequences precede agent stop sequences;
- request tools precede agent tools;
- run-level response format and additional properties override the merged chat options;
- no merge mutates agent defaults or caller-owned run options.

The supported run-options surface deliberately excludes continuation tokens, background
responses, and facade-owned sessions. A run may provide a request-local chat-client factory; its
result must be non-null and is used only for that invocation.

The facade does not add typed structured-output convenience overloads or external SDK metadata
projections without a first-party consumer. Callers may still use MEAI `ChatOptions.ResponseFormat`
through the existing run-options contract.

`ChatClientAgent` is not exposed as an `AIFunction`. Model-visible subagent invocation routes
through Session Core's child-Thread lifecycle so lineage, policy, persistence, cache identity, and
collaboration state remain authoritative.

`GetService` resolves, in order, the agent itself, agent metadata, a cloned options value, the
configured `IChatClient`, ordered context providers, and services provided by the underlying
chat-client pipeline. Service keys are forwarded and self-owned services resolve only for an
unkeyed request. `AsAIAgent` constructs this facade from an `IChatClient` without creating
lifecycle or persistence state.

`AddAIAgent` registers a keyed singleton `ChatClientAgent`. Registration resolves keyed `AITool`
instances with the same name, snapshots them into cloned options, and assigns the registration
name to the agent. Registration rejects a null builder, blank name, or null chat client.
`AgentFactory` constructs the provider middleware pipeline, immutable tool snapshot, prompt
context, and per-invocation options before creating the facade. Session Core remains responsible
for choosing the agent and supplying history for each Turn.

### 6.6 Context-provider lifecycle

An agent may have zero or more ordered context providers. Before each invocation, the first
provider receives an invocation-local context containing the complete request messages and merged
tools. Each provider returns the complete context observed by the next provider. Instructions,
messages, and tools therefore compose deterministically in registration order.

After the chat client terminates, every provider that participated in preparation is notified in
the same registration order. The terminal context contains the exact request sent to the chat
client and either the completed response messages or the exception. Notification is attempted for
success, failure, and cancellation; a failure notification must not replace the original model
exception. Context instructions, messages, and tools are request-local and are never appended to
Session-owned durable history merely because a provider supplied them.

`MemoryContextProvider` is one implementation of this contract. Its generated instruction bytes,
tool-name observation, and tracing point remain unchanged.

## 7. Turn Execution

The MEAI-aligned pipeline preserves this observable transition sequence:

1. Prepare the immutable execution and provider request identity.
2. Materialize the active conversation window and provider history.
3. Apply request-local sanitization without mutating durable history.
4. Send a sampling attempt.
5. Persist completed provider items according to the active protocol contract.
6. Assemble handleable tool calls.
7. Evaluate tool policy and approval.
8. Dispatch each authorized call at most once.
9. Emit tool lifecycle events and persist results.
10. Append protocol-correct tool outputs.
11. Repeat sampling or complete the Turn.

This sequence is a behavioral contract, not a requirement to introduce a second public state
machine or request/event type hierarchy. It remains implemented through an MEAI `IChatClient`
pipeline unless a narrower change is justified.

Tool-call sampling has no fixed iteration limit. It continues while the model emits handleable
calls and ends through normal model completion, cancellation, the consecutive-error policy, or
context compaction. Loop control must not issue a terminal sampling request with a reduced tool
surface.

Retries are sampling-attempt transitions, not recursive agent runs. An attempt that produced no
externally committed output may be retried under the configured bounded policy. Completed
provider items and tool effects are never executed again merely because transport streaming
failed.

## 8. Tool Contract

The runtime retains MEAI `AITool`, `AIFunction`, `AIFunctionDeclaration`,
`AIFunctionArguments`, delegating function wrappers, JSON schema utilities, and invocation
semantics. DotCraft's incremental `ToolFunctionGenerator` and generated `AIFunction` wrappers
remain the standard implementation for built-in tools.

The existing source-neutral `ToolDefinition`, planning snapshot, authority, approval, and
dispatcher contracts complement MEAI tool objects; they do not replace the MEAI invocation and
schema abstractions. Tool definitions remain ordered and immutable for a sampling request.

Tool execution results may contain MEAI `AIContent`. A provider adapter projects them into its
native tool-output format. Rich output intended only for clients or hosts is not sent to the model
unless explicitly marked model-visible.

Parallel tool calls may complete and emit UI lifecycle events in actual completion order. The next
provider request must use the deterministic ordering and grouping required by that provider.

## 9. Conversation and History

### 9.1 Provider-neutral history

The rollout remains the durable authority for Session behavior, display, protocol export,
rollback, and generic conversation reconstruction.

Provider-neutral history must preserve roles, multimodal content, reasoning, tool calls/results,
usage, and safe metadata without depending on runtime CLR object identity.

### 9.2 Provider-native history

Protocols that require byte- or item-faithful replay use a durable provider-native history as
defined by [Canonical OpenAI Responses Provider History](responses-provider-history.md).

Provider-native history:

- is the outbound authority for its provider/protocol;
- preserves item order, IDs, protected content, call IDs, and native item boundaries;
- survives tool iterations, Turn completion, cold resume, and compatible whole-Turn forks;
- establishes an explicit replacement boundary for partial forks, forks without native history,
  rollback, compaction, or protocol transitions that cannot preserve an exact native prefix;
- is replaced only at an explicit history/window boundary;
- is never reconstructed from UI Session Items when an exact native history exists.

### 9.3 Schema activation and recovery

Current rollouts require the provider-history schema version defined by the owning
provider-history specification. A compatible whole-Turn fork may preserve an exact native
snapshot; a partial fork or a fork without native history establishes the documented replacement
boundary. Unknown, missing, or corrupt active provider-native history fails with a stable error
rather than silently falling back to a lossy mapping.

## 10. Provider Isolation

OpenAI Responses, OpenAI Chat Completions, and Anthropic consume MEAI messages, options, and tools
through separate `IChatClient` adapters.

### 10.1 Assembly boundaries

The model runtime is split across four compile-time layers:

- `DotCraft.Agents` owns the provider-neutral agent facade, chat-client infrastructure, runtime
  request contracts, provider registry, optional provider capability contracts, and the MEAI
  foundation used by every model integration.
- `DotCraft.Core` owns product configuration, Session lifecycle, durable history, tool policy,
  compaction, observability, and the provider-neutral projection of native history as opaque JSON.
- `DotCraft.Agents.OpenAI` and `DotCraft.Agents.Anthropic` own their SDK clients, wire mappings,
  protocol-specific request adapters, and optional capabilities.
- Executable hosts are composition roots. The built-in DotCraft application references Core and
  both provider integrations and registers them explicitly.

`ModelProviderRegistry` rejects duplicate protocol ownership and reports an unsupported-provider
error when no registered provider owns the requested protocol. Core's `ChatClientRegistry` caches
clients and resolves product configuration from immutable provider-neutral runtime requests.

Provider-native conversation state crosses the boundary through an opaque history contract. The
provider owns MEAI-to-native mapping, entry identity, attempt bookkeeping, and native compaction.
Core owns append, replacement, abort persistence, replay filtering, and Thread lifecycle.

- Responses preserves native response items, reasoning identity, encrypted content, hosted items,
  and prompt-cache identity.
- Chat Completions preserves role/message grouping, reasoning fields used by compatible providers,
  and its existing cache-control behavior.
- Anthropic preserves content-block order, thinking signatures, native cache-control markers,
  deferred tool loading, and eager tool-input streaming.

A capability or optimization belonging to one transport cannot alter another transport's
history, tool schema, retry timing, or wire request.

### 10.2 Responses routing identity

The completed Responses transport distinguishes the cache-session/root identity from the current
execution Thread:

| Execution kind | `session-id` | `thread-id` | default `prompt_cache_key` | `x-client-request-id` |
|---|---|---|---|---|
| Root thread | root Thread ID | root Thread ID | root Thread ID | root Thread ID |
| Subagent | root Thread ID | child Thread ID | root Thread ID | child Thread ID |
| User fork | new fork Thread ID | new fork Thread ID | new fork Thread ID | new fork Thread ID |

An explicit caller cache key retains its documented precedence. Routing policies consume these
resolved values and do not derive them from tracing state.

## 11. Lifecycle Contract

| Lifecycle event | Runtime behavior |
|---|---|
| New root thread | Create a new conversation identity. |
| Next Turn | Reuse durable conversation history and create new turn-scoped state. |
| Tool iteration | Append completed provider items and tool outputs without rebuilding the existing prefix. |
| Cold resume | Restore rollout and protocol-compatible native history before sampling. |
| Subagent | Use the child Thread for execution while retaining explicit root lineage. |
| User fork | Create a new root conversation identity; preserve an exact compatible whole-Turn prefix or establish an explicit replacement boundary when the fork is partial or has no native prefix. |
| Rollback | Discard later Turn effects and rebuild from the surviving durable history. |
| Compaction | Replace the active window/history explicitly. |
| Protocol switch | Rebase from provider-neutral history and start protocol-local state. |
| Cancellation | Stop active sampling/tools according to their cancellation contract and persist only committed effects. |

## 12. Failure Behavior

- Provider authentication, rate-limit, invalid-request, and server failures retain stable provider
  request IDs and existing public error classification.
- Tool failures produce one deterministic result for their call and do not terminate unrelated
  parallel calls unless policy requires it.
- Retry never duplicates a completed local tool effect.
- Persistence failure before tool dispatch prevents that tool from executing.
- Corrupt durable history blocks only the incompatible execution path and reports a stable error.

## 13. Observability and Privacy

Diagnostics observe the final transport shape and may record:

- request identity source and request kind;
- hashes and byte counts for instructions, tools, reasoning options, and ordered input items;
- common-prefix length and provider item-ID coverage;
- history revision;
- token usage and cache coverage;
- retry, fallback, and invalidation reasons.

Diagnostics must not record request content, credentials, protected reasoning data, raw private
tool metadata, or user secrets. Diagnostic observers cannot assign identity or mutate history.

## 14. Stable Integration Boundaries

- AppServer request/response and Session event behavior remain backward compatible.
- Existing threads, forks, rollbacks, and compaction checkpoints remain usable.
- Model-visible tool schemas and ordering remain stable unless separately specified.
- Provider request JSON and headers are wire-identical during architecture-only changes, excluding
  explicitly normalized nondeterministic fields.
- Agent integrations use `ChatClientAgent`, `AsAIAgent`, `AddAIAgent`, `AgentFactory`, and the MEAI
  contracts defined by this specification.
- Executable composition roots register built-in provider integrations through
  `ModelProviderRegistry`.
- Session lifecycle and durable history integrations use Session Core contracts rather than
  serializing runtime agent objects.

## 15. Acceptance Checklist

- [ ] Thread/Turn/Item and rollout are the only durable lifecycle authority.
- [ ] Turn execution has one Session-owned lifecycle while retaining the MEAI `IChatClient`
      pipeline and content/tool contracts.
- [ ] Provider item and reasoning identity survive streaming, persistence, and resume.
- [ ] MEAI content, chat-client, streaming, tool, schema, and generated-function abstractions are
      preserved unless a documented conflict requires a narrow extension.
- [ ] DotCraft's generated `AIFunction` source path remains supported and wire-stable.
- [ ] Responses, Chat Completions, and Anthropic each use one MEAI-compatible provider path.
- [ ] Core and both provider integrations form the documented diamond dependency through
      `DotCraft.Agents`.
- [ ] Existing AppServer events, rollouts, and old-thread lifecycle behavior remain compatible.
- [ ] Architecture-only changes preserve sanitized request headers and complete wire JSON.
- [ ] Tools execute at most once across retry and transport fallback.
- [ ] Full automated tests and the required live provider smoke matrix pass.
- [ ] Prompt-cache coverage meets both the configured absolute floor and the accepted relative
      regression limit.

## 16. Open Questions

None.
