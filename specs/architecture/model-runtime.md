# DotCraft Model Runtime

| Field | Value |
|---|---|
| Version | 0.9.9 |
| Status | Living |
| Date | 2026-10-29 |
| Parent Spec | [Session Core](session-core.md) |

## 1. Overview

DotCraft owns the complete lifecycle of a model-driven turn. The model runtime translates a
`Thread -> Turn -> Item` conversation into provider requests, consumes provider stream events,
dispatches tools, and projects the result back into Session events and durable rollout records.

Microsoft.Extensions.AI (MEAI) is DotCraft's provider-neutral content, tool, chat-client, and
streaming abstraction layer. DotCraft adds lifecycle and provider-native state only where MEAI's
generic aggregation cannot represent a required protocol invariant.

OpenAI Responses, OpenAI Chat Completions, and Anthropic use MEAI-compatible clients while
retaining protocol-native request, response, history, and cache behavior.

Remote execution uses the same provider adapters and history contracts on the worker; a transport
substitution does not move model-loop or history ownership.

## 2. Non-goals

- Defining one lowest-common-denominator wire format for all providers.
- Parallel DotCraft equivalents for `AIContent`, `ChatMessage`, `ChatOptions`,
  `ChatResponseUpdate`, `AITool`, `AIFunction`, or `IChatClient` without a demonstrated protocol or
  lifecycle conflict.
- Replacing provider SDKs when a maintained SDK exposes the required protocol faithfully.
- Treating UI-oriented Session Items as the source of provider request history.
- Persisting OAuth credentials, protected reasoning plaintext, or arbitrary provider runtime
  objects.

## 3. Authority and Boundaries

### 3.1 Session authority

Session Core owns:

- Thread lineage and configuration;
- Turn creation, status, cancellation, and queueing;
- Item lifecycle and AppServer event emission;
- approvals, user-input requests, hooks, goals, and subagent lifecycle;
- rollout, rollback, fork, compaction, and cold-resume semantics.

The model runtime receives an immutable turn execution snapshot and emits runtime events. It does
not create a second durable session object or independently infer thread lineage.

Session Core reconstructs the MEAI model history for the active turn directly from rollout records.
That request-local list is not serialized as an independent session object.

### 3.2 Runtime authority

For one active Turn, the execution layer owns:

- sampling attempts and their retry state;
- ordered provider input and output;
- partially assembled stream items;
- pending and completed tool invocations;
- the transition to another sampling request or terminal completion.

All mutable state belongs to one explicit turn execution. Async-local scopes may expose immutable
request context to adapters, but they are not authoritative storage; they are disposed on success,
failure, or cancellation, nested scopes restore their parent value, and turn-scoped state does not
leak into another invocation.

### 3.3 Provider authority

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

## 4. MEAI Baseline and Extension Policy

MEAI is the default design reference. DotCraft does not introduce a competing abstraction unless
the MEAI contract demonstrably cannot preserve required Session or provider behavior; an extension
is a narrow subclass, adapter, or MEAI-shaped extension rather than a parallel content, tool, or
chat-client model.

### 4.1 Conversation items

Generic runtime content uses the MEAI `ChatMessage` and `AIContent` hierarchy: messages of every
role, visible text and multimodal data, reasoning summaries and protected reasoning, provider item
identity, function calls and results, hosted tool calls and outputs, usage, and protocol errors.
DotCraft adds `AIContent` subclasses only for content kinds MEAI does not represent.

Provider item ID and tool `call_id` are distinct identities. Converting or aggregating content
never substitutes one for the other.

`AdditionalProperties` carries small, JSON-safe cross-layer metadata where MEAI supports extension.
`RawRepresentation` is request-local and is never a durable persistence contract.

When a provider requires an item ordering, identity, or protected payload that MEAI aggregation
cannot preserve, that provider uses a separate native ledger that supplements generic MEAI history.

### 4.2 Model request

Model requests use `IChatClient` streaming with ordered `ChatMessage` input and `ChatOptions`.
DotCraft supplies the immutable thread/turn/request identity, model and protocol selection, ordered
conversation input, ordered `AITool` definitions, reasoning and output options, protocol-specific
validated options, and cancellation and observability context.

The request reaching the provider adapter is complete. Delegating middleware and HTTP policies may
observe or serialize it but do not reconstruct lifecycle identity.

### 4.3 Stream events

Provider clients emit `ChatResponseUpdate` with typed `AIContent` for response and output-item
start and completion, text and reasoning deltas, tool-call argument deltas, completed tool calls and
hosted items, usage, provider turn state, and retryable and terminal failures.

Event order is part of the runtime contract. Provider-native identity is attached before response
aggregation, history projection, or tool dispatch.

### 4.4 Documented extensions

DotCraft extends MEAI for these requirements:

- response coalescing cannot be the sole Responses wire-history authority because it cannot
  preserve every raw representation, item boundary, or content metadata field;
- function invocation requires streaming argument previews, concurrent execution, approval and
  mode policy, same-Turn guidance, bounded retry integration, and provider-history hooks;
- provider request identity comes from Thread lifecycle rather than ambient tracing or client
  middleware.

### 4.5 Agent facade

`ChatClientAgent` is the immutable facade over one configured `IChatClient` pipeline. Its options
contain stable agent identity (`Id`, `Name`, `Description`), default `ChatOptions`, and an ordered
snapshot of context providers. Mutable option objects and collections are cloned at construction
and for every invocation. `AsAIAgent` constructs the facade from an `IChatClient` without creating
lifecycle or persistence state.

The facade runs aggregated (`RunAsync`) or streaming (`RunStreamingAsync`):

- both accept a string, one message, or an ordered message collection, optional invocation-specific
  run options, and caller-owned model history;
- each invocation's request is the supplied history followed by the current input; the caller's
  list remains the durable history projection, and the request list and cloned options are
  invocation-local;
- input plus completed response messages are appended to the history only after successful model
  completion; a failed or cancelled request appends nothing;
- streaming cancellation is forwarded to the chat-client enumerator, whose disposal completes
  before the run exits;
- response messages and updates receive the agent name only when they do not already identify an
  author.

Invocation `ChatOptions` merge with agent defaults without mutating either: request scalars win
and unset ones inherit; agent instructions precede request instructions, separated by one newline;
request additional properties win per key; a request raw-representation factory runs first and
falls back to the agent's only when it returns `null`; request stop sequences and tools precede the
agent's; run-level response format and additional properties override the merged options.

Run options exclude continuation tokens, background responses, and facade-owned sessions. A run may
supply a request-local chat-client factory, whose non-null result is used only for that invocation.

`ChatClientAgent` is not exposed as an `AIFunction`. Model-visible subagent invocation routes
through Session Core's child-Thread lifecycle so lineage, policy, persistence, cache identity, and
collaboration state remain authoritative.

`GetService` resolves, in order, the agent itself, agent metadata, a cloned options value, the
configured `IChatClient`, ordered context providers, and services of the underlying chat-client
pipeline. Service keys are forwarded; self-owned services resolve only for an unkeyed request.

Agent construction builds the provider middleware pipeline, immutable tool snapshot, prompt
context, and per-invocation options. Session Core chooses the agent and supplies history for each
Turn.

### 4.6 Context-provider lifecycle

An agent may have zero or more ordered context providers. Before each invocation, the first
provider receives an invocation-local context containing the complete request messages and merged
tools, and each provider returns the complete context observed by the next. Instructions, messages,
and tools therefore compose deterministically in registration order.

After the chat client terminates, every provider that participated is notified in registration
order with the exact request sent and either the completed response messages or the exception.
Notification is attempted for success, failure, and cancellation; a notification failure does not
replace the original model exception. Context instructions, messages, and tools are request-local
and are never appended to Session-owned durable history.

## 5. Turn Execution

The pipeline preserves this observable transition sequence:

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

This sequence is a behavioral contract implemented through the MEAI `IChatClient` pipeline, not a
second public state machine or event hierarchy.

Tool-call sampling has no fixed iteration limit. It continues while the model emits handleable
calls and ends through normal model completion, cancellation, the consecutive-error policy, or
context compaction. Loop control never issues a terminal sampling request with a reduced tool
surface.

Retries are sampling-attempt transitions, not recursive agent runs. Two bounded budgets apply.
The transport budget covers failures raised before any update reaches the runtime and is available
to every sampling path. The turn budget covers a stream that breaks after updates have been
delivered: the runtime commits the truncated response to conversation history, settles the tool
calls that response carries, and issues a new sampling request built from the grown history.

Retry never replays a committed effect. Completed provider items and tool effects are never
executed again because transport streaming failed, and a truncated attempt's delivered output is
committed once and never withdrawn from the stream the caller already observed.

## 6. Tool Contract

The runtime uses MEAI `AITool`, `AIFunction`, delegating function wrappers, JSON schema utilities,
and invocation semantics; built-in tools are generated `AIFunction` wrappers. DotCraft's
source-neutral tool definitions, planning snapshot, authority, approval, and dispatcher contracts
complement these objects rather than replace them. Tool definitions are ordered and immutable for a
sampling request.

Tool execution results may contain MEAI `AIContent`. A provider adapter projects them into its
native tool-output format. Rich output intended only for clients or hosts is not sent to the model
unless explicitly marked model-visible.

Parallel tool calls may complete and emit UI lifecycle events in actual completion order. The next
provider request uses the deterministic ordering and grouping required by that provider.

## 7. Conversation and History

### 7.1 Provider-neutral history

The rollout is the durable authority for Session behavior, display, protocol export, rollback, and
generic conversation reconstruction. It preserves roles, multimodal content, reasoning, tool calls
and results, usage, and safe metadata without depending on runtime object identity.

### 7.2 Provider-native history

Protocols that require byte- or item-faithful replay use a durable provider-native history owned
by that provider adapter. Provider-native history:

- is the outbound authority for its provider/protocol;
- preserves item order, IDs, protected content, call IDs, and native item boundaries;
- survives tool iterations, Turn completion, cold resume, and compatible whole-Turn forks;
- is replaced only at an explicit history/window boundary, which partial forks, forks without native
  history, rollback, compaction, and protocol transitions that cannot preserve an exact native
  prefix establish;
- is never reconstructed from UI Session Items when an exact native history exists.

Unknown, missing, or corrupt active provider-native history fails with a stable error rather than
silently falling back to a lossy mapping.

## 8. Provider Isolation

OpenAI Responses, OpenAI Chat Completions, and Anthropic consume MEAI messages, options, and tools
through separate `IChatClient` adapters.

### 8.1 Assembly boundaries

- `DotCraft.Agents` owns the provider-neutral agent facade, chat-client infrastructure, runtime
  request contracts, provider registry, optional provider capability contracts, and the MEAI
  foundation used by every model integration.
- `DotCraft.Core` owns product configuration, Session lifecycle, durable history, tool policy,
  compaction, observability, and the provider-neutral projection of native history as opaque JSON.
- `DotCraft.Agents.OpenAI` and `DotCraft.Agents.Anthropic` own their SDK clients, wire mappings,
  protocol-specific request adapters, prompt-cache placement and marking, and optional capabilities.
- Executable hosts are composition roots that register provider integrations explicitly.

The provider registry rejects duplicate protocol ownership and reports an unsupported-provider
error when no registered provider owns the requested protocol. Core caches clients and resolves
product configuration from immutable provider-neutral runtime requests.

Provider-native conversation state crosses the boundary through an opaque history contract. The
provider owns MEAI-to-native mapping, entry identity, attempt bookkeeping, and native compaction.
Core owns append, replacement, abort persistence, replay filtering, and Thread lifecycle.

- Responses preserves native response items, reasoning identity, encrypted content, hosted items,
  and prompt-cache identity.
- Chat Completions preserves role/message grouping and reasoning fields used by compatible
  providers. It emits no cache-control markers; its prefix cache is automatic.
- Anthropic preserves content-block order, thinking signatures, native cache-control markers, and
  deferred tool loading. It does not request fine-grained tool-input streaming, which only
  first-party surfaces accept and compatible endpoints cannot advertise.

A capability or optimization belonging to one transport cannot alter another transport's
history, tool schema, retry timing, or wire request.

## 9. Lifecycle Contract

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

## 10. Failure Behavior

- Provider authentication, rate-limit, quota, overload, invalid-request, and server failures carry
  a stable classification and, when the provider supplies them, the upstream HTTP status, request
  ID, and a server-advised retry delay. Classification belongs to the provider integration; the
  provider-neutral layer does not re-derive it from message text.
- An invalid-request failure that a gateway wraps in a server error, such as a Bedrock
  `ValidationException` inside a 502, is classified as invalid-request and is not retried.
- A rejection naming an unknown model, such as OpenAI `model_not_found` or Anthropic
  `not_found_error`, is classified as invalid-request whatever its HTTP status and is not retried.
- A classified failure is the sole authority on whether it may be retried and after how long. A
  server-advised delay takes precedence over local backoff and becomes a process-local monotonic
  deadline when response headers are first observed, or when a provider exposes rate-limit text
  advice at failure capture. Text advice applies only to rate-limit failures and never overrides a
  valid header, including a zero delay. Later handling consumes the same deadline rather than
  restarting it, and both transport replay and tool-loop reissue wait only for the remaining
  duration. Zero or past-date advice means no wait; missing or malformed advice uses local backoff.
- Retry deadlines survive classification and exception wrapping. Monotonic timestamps are never
  serialized or sent across hosts; remote boundaries forward supported retry headers and establish
  a new local deadline on receipt.
- Tool failures produce one deterministic result for their call and do not terminate unrelated
  parallel calls unless policy requires it.
- Persistence failure before tool dispatch prevents that tool from executing.
- Corrupt durable history blocks only the incompatible execution path and reports a stable error.

## 11. Observability and Privacy

Diagnostics observe the final transport shape and may record:

- request identity source and request kind;
- hashes and byte counts for instructions, tools, reasoning options, and ordered input items;
- common-prefix length and provider item-ID coverage;
- history revision;
- token usage and cache coverage;
- retry, fallback, and invalidation reasons.

Diagnostics do not record request content, credentials, protected reasoning data, raw private tool
metadata, or user secrets. Diagnostic observers cannot assign identity or mutate history.
