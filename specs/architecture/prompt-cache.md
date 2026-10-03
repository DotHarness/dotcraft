# DotCraft Prompt Cache Strategy

| Field | Value |
|-------|-------|
| **Version** | 0.8.1 |
| **Status** | Living |
| **Date** | 2026-10-03 |
| **Parent Specs** | [Session Core](session-core.md), [Prompt Composition](prompt-composition.md), [OpenAI Subscription Auth](openai-subscription-auth.md) |

Purpose: define the per-protocol contract DotCraft must satisfy for the provider's prompt cache to hit, and the empirical hit-rate envelope each protocol is expected to deliver. This is a design document — it constrains what the runtime emits on the wire, not how it builds the request internally.

The stable-prefix/cache-point selection algorithm lives in `DotCraft.Agents`. Session Core supplies
policy and diagnostics. Placement and marking belong to the provider integration that builds the
request: the integration composes the caching middleware inside its own client chain and writes its
native marker directly, so no provider-neutral marker representation crosses the boundary. A provider
that forwards inference elsewhere therefore carries only the policy, and placement happens wherever
the provider request is finalized.

## 1. Concepts

Prompt cache exists because providers can skip prefill compute for tokens that match a stored prefix. DotCraft cares about two metrics:

- **Coverage** — `cached_input_tokens / input_tokens` per call.
- **Stability** — whether the same nominal workload reliably produces the same coverage. Unstable cache turns "cheap turn" into a lottery.

Two cache-routing models are in play:

| Model | How a cache hit is decided |
|-------|----------------------------|
| **Prefix cache** | Backend hashes the input prefix and looks it up in a per-shard KV store. Hit depends on byte-stable prefix AND the request landing on a shard that already holds it. |
| **Explicit cache control** | Caller marks specific spans of the prompt with cache breakpoints. Backend writes named cache segments and reuses them by name. |

DotCraft must build a byte-stable prefix for the first model and place the right cache markers for the second.

## 2. Per-protocol contract

### 2.1 `openai-chat-completions`

| Aspect | Setting |
|--------|---------|
| Cache routing | Prefix cache, automatic on supported models |
| Threshold | Backend-side ≥ 1024 prefix tokens (provider default; gateway-dependent) |
| Required client work | None beyond a byte-stable prefix |

DotCraft does not set any cache-control field on this protocol. This holds for every model reached
through it, including Claude models served by an OpenAI-compatible gateway. Cache works as long as the
message array, tools array, and system prompt are byte-identical between requests.

### 2.2 `openai-responses` — API-key path

| Aspect | Setting |
|--------|---------|
| Cache routing | Prefix cache, hinted by `prompt_cache_key` |
| Threshold | Provider-dependent. Public OpenAI is low; third-party OpenAI-compatible gateways are usually higher and vary per gateway |
| Required client work | Send `prompt_cache_key` plus a byte-stable prefix |

The wire body emitted on every Responses request:

```json
{
  "model": "<model>",
  "instructions": "<system prompt>",
  "input": [ /* message / function_call / function_call_output / reasoning items */ ],
  "tools": [ /* function / namespace / tool_search definitions */ ],
  "store": false,
  "stream": true,
  "include": ["reasoning.encrypted_content"],
  "prompt_cache_key": "<root_cache_session_id>",
  "reasoning": { /* effort and summary fields are optional */ },
  "parallel_tool_calls": <optional bool>,
  "max_output_tokens": <optional int>
}
```

Invariants the runtime must uphold:

- **`prompt_cache_key` equals the root cache-session thread id**. Root requests use their own thread
  id. Subagent requests use their root thread id while retaining the child id in `thread-id` and
  `x-client-request-id`. An ordinary user fork starts a new root cache session and uses its new
  fork thread id.
- **`store=false`**. The backend MUST be treated as stateless; conversation state lives in DotCraft. Reasoning items round-trip through `include: ["reasoning.encrypted_content"]`.
- **`reasoning` is always an object**. Effort and summary fields are present only when configured, but the object itself remains part of the stable request shape.
- **Input is rebuilt deterministically each turn**. Reasoning items keep their original `encrypted_content` blob byte-for-byte. Re-encrypting or stripping them breaks prefix equality.
- **Canonical Responses history is append-only between explicit replacement boundaries**.
  Version-1 threads reuse completed provider items directly and map only the new local MEAI tail;
  compaction, protocol return, and incompatible fork materialization establish a new prefix
  generation as specified by
  [Canonical OpenAI Responses Provider History](responses-provider-history.md).
- **Namespaced tools keep their provider-visible namespace shape**. Runtime tools with a namespace are serialized as Responses `namespace` tool definitions that wrap local child `function` definitions. The namespace is the canonical `ToolName.namespace`; a child definition's name is only `ToolName.name`, never a flattened `namespace__name`. Namespaced `function_call` input items retain that same namespace and local name. Matching `function_call_output` input items are correlated by `call_id` and must not include `namespace`; prompt-cache request-shape hashes must reflect only the legal provider-visible request shape so flat and namespaced tools cannot share the same tool-schema hash.
- **Native tool search returns the same composite definitions used by direct projection**. A deferred result describes one namespace once and returns its children under their local names. Results are keyed by the full canonical `ToolName`, so equal child names in different namespaces remain independent. Historical `tool_search_output` items keep discovered schemas provider-visible, while local execution resolves against the current Turn snapshot without re-injecting those schemas after an Agent rebuild. The local search tool has canonical identity `SearchTools`; the OpenAI Responses adapter serializes it with the provider-owned `tool_search` wire shape.
- **No volatile content in system / assistant turns**. Timestamps, randomised tool ordering, in-place mutation of caller options — all forbidden inside the cached prefix. Volatile content belongs only at the tail of the latest user turn.

### 2.3 `openai-responses` — ChatGPT OAuth path

The [OpenAI Subscription Auth specification](openai-subscription-auth.md#responses-request-contract)
owns the complete OAuth Responses wire contract. Model metadata and the internal Lite developer gate
select the standard or Lite wire dialect; the gate currently defaults to standard Responses.
Standard OAuth Responses follows §2.2 with top-level `instructions` and `tools`. Responses
Lite projects those stable values into leading developer input items, sets
`reasoning.context=all_turns`, and disables parallel tool execution. Both dialects consume the same
canonical provider history and preserve the same append-only input prefix between explicit
replacement boundaries. Both omit the top-level `max_output_tokens` field; that value remains a
local DotCraft budget, with compaction summary length enforced after the provider returns.
OAuth sampling applies Zstandard transport encoding after the logical request body is finalized;
compression does not change the canonical input sequence or prompt-cache generation. Provider-native
compact requests remain uncompressed.

OAuth routing fields and authoritative header/body identity come from the authentication contract.

Provider metadata and same-turn `x-codex-turn-state` are routing/runtime metadata. They are
not model-visible prompt content and MUST NOT be considered part of DotCraft's prompt-prefix
identity. Prompt-cache diagnostics may record separate metadata fingerprints for debugging, but
changes in `turn_id`, `turn_started_at_unix_ms`, `x-codex-turn-state`, or other provider runtime
metadata MUST NOT be classified as prompt/input/tool drift. Omitting OAuth-unsupported transport
parameters such as `max_output_tokens` likewise MUST NOT be reported as prompt-prefix drift.

### 2.4 `anthropic`

| Aspect | Setting |
|--------|---------|
| Cache routing | Explicit `cache_control: { type: "ephemeral", ttl: "5m" }` breakpoints |
| Threshold | Provider-side per-segment minimum; below the minimum the marker is ignored |
| Required client work | Place breakpoints on the system prompt and on stable snapshot prefixes |

Breakpoint placement contract:

1. **System prompt** — marked at the end of the system message so the entire system prompt is cached as one segment.
2. **Snapshot prefix** — marked at the last message of a captured snapshot so successive maintenance forks reuse the snapshot segment.
3. **Maintenance fork cache mode** — maintenance forks are one-shot and execute no tools. They use `readOnlyPrefix`: they mark only the reusable system / snapshot prefix, do not mark the appended maintenance task tail, and do not commit remembered breakpoints.

Anthropic cache markers are provider-visible content-block annotations. They MUST NOT be implemented
by splitting, duplicating, reordering, or otherwise changing the semantic shape of messages. In
particular, tool results for a single assistant tool-use turn remain one grouped tool-result message:
if an assistant message emits multiple `tool_use` blocks, the immediately following provider-visible
user/tool-result message contains one `tool_result` block per `tool_use`, each `tool_use_id` appears
exactly once, and the `tool_result` blocks stay at the front of that message's content array. When a
cache breakpoint lands on one result inside such a grouped message, DotCraft marks that existing
result block in place and preserves the surrounding result blocks and message boundary. Prompt-cache
shaping is allowed to clone selected content blocks request-locally, but it is not allowed to mutate
the persisted thread rollout or rewrite the tool-use/tool-result pairing.

The cache write that produced a segment counts as `cache_write_input_tokens` on that call and as `cached_input_tokens` on subsequent calls; both fields surface in trace.

#### Cache keepalive during a turn

A turn can sit between sampling requests for longer than the cache TTL: a long tool run, a SubAgent
or workflow wait, or a pending approval. The next request then pays a full cache write for the whole
prefix. While `PromptCaching.Warming` is enabled (default `true`), DotCraft refreshes the cache by
replaying the turn's latest sampling request before the TTL runs out.

1. **Eligibility.** Only a main agent sampling request on the `anthropic` protocol whose model
   receives cache markers is armed. Maintenance forks, title generation, and other auxiliary requests
   are never armed. A request whose total prompt (`input + cache read + cache write` from its usage)
   is below 16,000 tokens is not worth refreshing and stops warming.
2. **Replay.** The keepalive resends the latest sampling request through the same client chain below
   tool invocation, with only `max_tokens` set to 1 and no retries. It reuses the breakpoints that
   request committed instead of selecting new ones and commits nothing, so its provider-visible bytes
   match the original request up to the last breakpoint.
3. **Timing.** The keepalive fires at 90% of the TTL after the request it replays, leaving at least 10
   seconds of margin, and re-arms after each successful refresh. If it fires later than halfway
   between its due time and expiry, it stops, because a late refresh is a full-price cache write.
4. **Lifetime.** Each new sampling request cancels the pending keepalive and any refresh in flight.
   Warming ends when the turn ends, fails, or is cancelled, and after 60 minutes from the request that
   armed it. It never runs while the thread is idle.
5. **Accounting.** A keepalive is an auxiliary request of kind `cacheWarm`. Its usage is traced as its
   own event and never counts toward the turn's token usage or context estimate. A failed keepalive
   is traced and stops warming; it never fails the turn.

Deferred discovery and provider-flat identity follow [Tool Architecture](tools-architecture.md#54-exposure).
Anthropic native discovery adds `anthropic-beta: advanced-tool-use-2025-11-20`; discovered definitions
use `defer_loading: true` and results use `tool_reference` blocks. Optional fields stay absent unless
enabled, including `strict` for non-strict tools. The names-only inventory enters request-local
history before cache-point selection, while undiscovered tools do not enter the full schema prefix.

The names-only inventory is part of the Anthropic message prefix rather than the stable system instructions. An unchanged inventory is byte-stable across sampling rounds. When the available deferred pool changes, cache points after the inventory form a new prefix while earlier stable system cache points remain eligible for reuse. DotCraft does not persist inventory deltas or add a compaction-specific history type.

### 2.5 Trace diagnostics

For `openai-responses`, each provider request records a `PromptCacheRequestShape` trace event before transport. The event records SHA-256 hashes and counts for provider-visible byte shapes: instructions, tools, reasoning configuration, the full input array, and each ordered input item. It also records the serialized input byte count, the one-based stream attempt number, and aggregate response-item ID coverage (eligible, present, generated, missing, and invalid-source counts) without storing request content.

Prompt-cache investigations compare adjacent `PromptCacheRequestShape` events by `inputItemHashes` from the start of the array. A long common prefix with only appended tail items means the provider-visible prefix stayed stable; an early hash mismatch means the prefix changed at or before that input item. Retries share the existing request index and increment `attemptNumber`, so their request shapes can be compared directly. If `inputItemHashes` stay stable but `promptCacheKeyHash` changes, the cache identity changed. If both stay stable but cached reads drop, the trace should classify the evidence as provider/cache-routing-side rather than a DotCraft prefix mutation.

When a full-history native SubAgent session is bound to its direct parent, tracing captures the parent's latest `PromptCacheRequestShape` as an immutable fork anchor. The child's first request shape produces exactly one `SubAgentPrefixDiagnostic` with one of three statuses:

| Status | Meaning |
|--------|---------|
| `compatible` | The static prefix matches — protocol, model, prompt-cache key, instructions, tools, and reasoning hashes are all equal — and the child retains a non-empty ordered input prefix from the parent. |
| `staticShared` | The static prefix matches but the full-history child retained no ordered parent input, indicating that shared history was not materialized as expected. |
| `diverged` | A leading request component changed, so the static prefix is broken. A defect when the child is required to inherit that prefix. |

A missing parent shape is `unavailable`.

A reused history snapshot must preserve its inherited static prefix. Diagnostics distinguish
static-prefix compatibility from the length of inherited input; a short inherited input prefix
is not by itself a cache defect.

The event records only component hashes, request/attempt indexes, item counts, the matched prefix length, whether the complete parent input remains a prefix, whether a shared input prefix was expected for this spawn, the first divergence index, and changed-field names. It never records request content or compares the complete `inputHash`.

This cross-session comparison is exact only for `openai-responses`, where canonical request-shape tracing exists. Other protocols retain the parent/child trace binding but do not infer prefix equality from incomplete generic hashes. Nested SubAgents compare against their direct parent, and later turns, tool loops, retries, or cold resumes do not select a new fork anchor or emit another diagnostic.

The request-shape event intentionally excludes OAuth/runtime metadata from the prefix hashes.
When emitted, a metadata diagnostic hash is informational only and is not used to increment prompt
drift counters.

## 3. Cross-cutting design rules

These rules apply to every protocol unless the protocol contract above explicitly overrides them.

1. **Byte-stable prefix.** Anything inside the cacheable prefix — system prompt, tools, prior turns — must be identical byte-for-byte between requests on the same thread. Adding a single character anywhere in the prefix invalidates the cache for the whole prefix.
2. **Volatile content only at the tail.** Timestamps, runtime context, mode banners and any other request-local state must live in the user message of the current turn, never in system content, tools, or prior assistant turns.
3. **Tool order is part of the prefix.** Tools must be enumerated in a stable order across requests on the same thread. Re-sorting tools (alphabetically, by category, etc.) between turns is a cache-break. Ordinary tool-loop control must not remove or reorder provider-visible tools to manufacture a terminal sampling request.
4. **Reasoning items round-trip verbatim.** When a model emits a reasoning item with `encrypted_content`, the next request must pass that exact blob back. Decrypting and re-encrypting, dropping the field, or normalising whitespace inside it all break the cache.
5. **Reasoning configuration is part of the cache key.** Provider-visible thinking / reasoning settings must be treated as a cache-key dimension for diagnostics. A change in reasoning effort, reasoning output visibility, or provider thinking mode can explain a cache read drop even when messages and tools are unchanged.
6. **Root lineage is the Responses cache identity.** `prompt_cache_key`, `session-id`, and
   `client_metadata.session_id` MUST use the root cache-session thread id. `thread-id`,
   `x-client-request-id`, and `client_metadata.thread_id` MUST use the current executing thread id.
   Root threads use the same value for both roles; subagents share the root cache key while keeping
   child-scoped execution identity; ordinary user forks start a new root identity.
   A shared key influences cache routing but does not require every request in the lineage to have
   the same complete input. Cache reads remain limited to exact prefixes present in both requests.
7. **One canonical body per request.** Wire bodies must not contain duplicate top-level JSON keys. Downstream policies and inspectors are allowed to assume the body parses cleanly into a flat object.
8. **Internal cache state may be narrower than provider identity.** DotCraft may track remembered prompt-cache breakpoints under an internal state key such as `thread:<id>:maintenance:<kind>:<run>` so maintenance forks and the main conversation do not overwrite each other's breakpoint history. Maintenance forks use that state key in `readOnlyPrefix` mode without committing new remembered breakpoints. This internal state key MUST NOT replace provider-visible cache-session or current-thread routing identity.
9. **Tool identity shape is cache state.** Provider projection and replay preserve the immutable
   identities in [Tool Architecture](tools-architecture.md#6-identity-model); cache shaping does not
   rename, regroup, or reorder definitions.

10. **Thread-scoped context is history, not prefix.** Content that depends on the running thread or on an attached client connection MUST NOT reach the system prompt / `instructions` channel on any protocol. It travels as a thread context item, placed and carried as specified in [Prompt Composition](prompt-composition.md).
11. **Thread context items append; they do not mutate.** Rewriting an already-sent item, or rebuilding the system prompt because a binding or capability changed, invalidates the whole cached prefix and is forbidden. Replacing native SubAgent role instructions is the one exception and establishes an explicit replacement boundary.

## 4. Failure modes the runtime must guard against

| Symptom | Required guard |
|---------|----------------|
| Cache-control field set in caller options never reaches the wire | Runtime MUST verify the field survives any chat-client wrapper layer; tests must assert wire-level presence, not just in-memory presence on the option object |
| Duplicate top-level JSON keys in the wire body | Runtime MUST emit a canonical body. If the underlying serializer cannot be coerced, a pipeline-level deduplicator MUST run before transport |
| Volatile content leaks into the cached prefix | Prompt construction MUST keep timestamps, runtime context, and any other request-local data confined to the latest user turn |
| Reasoning encrypted content mutated between turns | Conversion layers MUST pass `encrypted_content` through unchanged; round-trip tests cover the case |
| Cache-read drop after reasoning settings change | Prompt-cache diagnostics MUST include a reasoning/thinking fingerprint and classify the drop as a request-shape change instead of likely server-side routing |
| Maintenance fork writes an unneeded tail breakpoint | Fork cache shaping MUST use `readOnlyPrefix`, mark only the reusable prefix, and skip committing fork-local remembered breakpoints |
| Provider sticky-routing flap (ChatGPT OAuth) | Recognised as an upstream limitation. The runtime reports observed coverage faithfully and does not retry just to chase a higher hit rate |
| Provider returns an empty post-tool response | After at least one tool result has been returned to the model, a normally completed response with no assistant content, reasoning output, or tool call ends the turn successfully without retrying or emitting `agent_empty_response`. An interrupted stream or explicit provider error still fails the turn |
| A tool identity cannot be represented by the target provider | Request serialization fails locally with stable `invalid_provider_tool_identity` diagnostics before HTTP transport; the runtime must not send a request known to violate the provider's name/length grammar |

## 5. Measurement contract

Compare cache coverage using repeated identical workloads with compaction disabled. Report aggregate
cached-input/input ratio and per-request evidence, and separate request-shape changes from routing
variation. Provider routing can vary widely; workload thresholds are test configuration, not a
provider guarantee or protocol contract.
