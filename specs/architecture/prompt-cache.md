# DotCraft Prompt Cache Strategy

| Field | Value |
|-------|-------|
| **Version** | 0.1.2 |
| **Status** | Living |
| **Date** | 2026-07-20 |
| **Parent Specs** | [Session Core](session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md), [OpenAI Subscription Auth](openai-subscription-auth.md) |

Purpose: define the per-protocol contract DotCraft must satisfy for the provider's prompt cache to hit, and the empirical hit-rate envelope each protocol is expected to deliver. This is a design document — it constrains what the runtime emits on the wire, not how it builds the request internally.

---

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

---

## 2. Per-protocol contract

### 2.1 `openai-chat-completions`

| Aspect | Setting |
|--------|---------|
| Cache routing | Prefix cache, automatic on supported models |
| Threshold | Backend-side ≥ 1024 prefix tokens (provider default; gateway-dependent) |
| Required client work | None beyond a byte-stable prefix |

DotCraft does not set any cache-control field on this protocol. Cache works as long as the message array, tools array, and system prompt are byte-identical between requests.

**Empirical envelope:** ~80% aggregate hit rate on the `prompt-cache-baseline` workload.

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
- **Native tool search returns the same composite definitions used by direct projection**. A deferred result describes one namespace once and returns its children under their local names. Discovery and activation are keyed by the full canonical `ToolName`, so equal child names in different namespaces remain independent. The local search tool itself is the top-level canonical `tool_search` function.
- **No volatile content in system / assistant turns**. Timestamps, randomised tool ordering, in-place mutation of caller options — all forbidden inside the cached prefix. Volatile content belongs only at the tail of the latest user turn.

**Empirical envelope:** ~60% on a light baseline and ~40% on a heavy baseline. Exact numbers vary by gateway because each implements its own prefix-cache layer.

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

Prompt-cache routing uses these fields on
`chatgpt.com/backend-api/codex/responses`:

| Hint | Location | Value |
|------|----------|-------|
| `Authorization: Bearer <access_token>` | HTTP header | OAuth access token |
| `chatgpt-account-id` | HTTP header | ChatGPT account id, resolved from the signed-in token/account store before runtime config |
| `originator` | HTTP header | Fixed identifier the backend recognises (see [openai-subscription-auth](openai-subscription-auth.md)) |
| `x-codex-installation-id` | HTTP header **and** request body `client_metadata` | Per-machine UUID v4, stable across processes and accounts |
| `session-id` | HTTP header | Root cache-session thread id |
| `thread-id` | HTTP header | Current executing thread id |
| `x-client-request-id` | HTTP header | Current executing thread id |
| `session_id` | Request body `client_metadata` | Root cache-session thread id |
| `thread_id` | Request body `client_metadata` | Current executing thread id |
| `x-codex-window-id` | HTTP header and request body `client_metadata` | Stable internal context-window id for the active thread |
| `x-codex-turn-metadata` | HTTP header and request body `client_metadata` | Canonical provider metadata JSON envelope for Responses routing |
| `x-codex-turn-state` | HTTP header | Provider-returned state replayed only within the same logical turn |

Each header is a stickiness signal at a different granularity:

```
chatgpt-account-id  ⊂  x-codex-installation-id  ⊂  session/thread headers
    account                install / machine             thread / conversation
```

Finer-grained signals let the load balancer park thread-scoped traffic on the cache shard that
already holds the prefix. Coarser signals remain available when the load balancer cannot honour a
finer signal. Optional runtime values are omitted when their scope is unavailable.

If the request carries caller-provided `client_metadata`, DotCraft preserves unrelated keys but
treats provider-reserved keys as authoritative runtime state. A mismatched reserved value is
overwritten to match the active OAuth/runtime context; otherwise the header/body pair could route
the same request under split identities. The body-level `x-codex-turn-metadata` string is the
canonical metadata envelope. Flat `client_metadata` keys expose the same routing identities.

Provider metadata and same-turn `x-codex-turn-state` are routing/runtime metadata. They are
not model-visible prompt content and MUST NOT be considered part of DotCraft's prompt-prefix
identity. Prompt-cache diagnostics may record separate metadata fingerprints for debugging, but
changes in `turn_id`, `turn_started_at_unix_ms`, `x-codex-turn-state`, or other provider runtime
metadata MUST NOT be classified as prompt/input/tool drift. Omitting OAuth-unsupported transport
parameters such as `max_output_tokens` likewise MUST NOT be reported as prompt-prefix drift.

Backend-specific thresholds — the public OpenAI thresholds do **not** apply here:

- Single-call input below ~14 000 tokens: cache is rarely written; coverage stays at 0%.
- 14 000 – 30 000 tokens: partial coverage, growing with prefix size and routing stickiness.
- Above 30 000 tokens with all routing hints set: up to ~75% coverage on a single call; aggregate session coverage tops out around 50% because the LB occasionally re-routes mid-thread.

**Empirical envelope:** the `prompt-cache-baseline` heavy workload targets an aggregate hit rate of
at least 35%. Backend routing may vary between runs, so the measurement contract in §5 evaluates
aggregate coverage and per-call evidence together.

### 2.4 `anthropic`

| Aspect | Setting |
|--------|---------|
| Cache routing | Explicit `cache_control: { type: "ephemeral", ttl: "5m" }` breakpoints |
| Threshold | Provider-side per-segment minimum; below the minimum the marker is ignored |
| Required client work | Place breakpoints on the system prompt and on stable snapshot prefixes |

Breakpoint placement contract:

1. **System prompt** — marked at the end of the system message so the entire system prompt is cached as one segment.
2. **Snapshot prefix** — marked at the last message of a captured snapshot so successive maintenance forks reuse the snapshot segment.
3. **Maintenance fork cache mode** — one-shot maintenance forks with no tool execution use `readOnlyPrefix`: they mark only the reusable system / snapshot prefix and do not mark the appended maintenance task tail. Tool-executing maintenance forks use `writeThrough`: they keep an internal cache-state path separate from the main conversation so tool-loop tails can advance their own remembered breakpoints without overwriting the main thread's remembered points.

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

Native deferred tool loading uses Anthropic's beta tool-reference path without changing this cache-control contract:

- DotCraft sends the `anthropic-beta: advanced-tool-use-2025-11-20` request header when Anthropic native deferred loading is active.
- The top-level tool list contains ordinary always-loaded tools, the local `tool_search` tool, and only deferred tools that were already discovered. Discovered deferred tools are serialized with `defer_loading: true`.
- `tool_search` returns Anthropic `tool_reference` content blocks. DotCraft does not reuse the OpenAI Responses `tool_search_output` wire shape on this protocol.
- Undiscovered deferred tools are not injected as ordinary complete schemas, so discovering a new tool does not perturb the cached prefix the way simulated deferred loading does.

Anthropic's tool surface is flat. DotCraft therefore emits and replays the persisted `providerFlatName` selected by the Turn snapshot, while dispatch resolves that alias through the snapshot's exact reverse index. It does not flatten or parse namespace components at the provider callback boundary.

**Empirical envelope:** ~82% aggregate hit rate on the `prompt-cache-baseline` workload.

### 2.5 Trace diagnostics

For `openai-responses`, each provider request records a `PromptCacheRequestShape` trace event before transport. The event records SHA-256 hashes and counts for provider-visible byte shapes: instructions, tools, reasoning configuration, the full input array, and each ordered input item. It also records the serialized input byte count, the one-based stream attempt number, and aggregate response-item ID coverage (eligible, present, generated, missing, and invalid-source counts) without storing request content.

Prompt-cache investigations compare adjacent `PromptCacheRequestShape` events by `inputItemHashes` from the start of the array. A long common prefix with only appended tail items means the provider-visible prefix stayed stable; an early hash mismatch means the prefix changed at or before that input item. Retries share the existing request index and increment `attemptNumber`, so their request shapes can be compared directly. If `inputItemHashes` stay stable but `promptCacheKeyHash` changes, the cache identity changed. If both stay stable but cached reads drop, the trace should classify the evidence as provider/cache-routing-side rather than a DotCraft prefix mutation.

When a native SubAgent session is bound to its direct parent, tracing captures the parent's latest `PromptCacheRequestShape` as an immutable fork anchor. The child's first request shape produces exactly one `SubAgentPrefixDiagnostic`. `compatible` means protocol, model, prompt-cache key, instructions, tools, and reasoning hashes are equal and the child retains a non-empty ordered input prefix from the parent. The parent request may legitimately have a different suffix because full-history forks discard transient reasoning, commentary, and tool traffic before appending child guidance and the initial task. `diverged` means a leading request component changed or no ordered input item was retained; a missing parent shape is `unavailable`. The event records only component hashes, request/attempt indexes, item counts, the matched prefix length, whether the complete parent input remains a prefix, the first divergence index, and changed-field names. It never records request content or compares the complete `inputHash`.

This cross-session comparison is exact only for `openai-responses`, where canonical request-shape tracing exists. Other protocols retain the parent/child trace binding but do not infer prefix equality from incomplete generic hashes. Nested SubAgents compare against their direct parent, and later turns, tool loops, retries, or cold resumes do not select a new fork anchor or emit another diagnostic.

The request-shape event intentionally excludes OAuth/runtime metadata from the prefix hashes.
When emitted, a metadata diagnostic hash is informational only and is not used to increment prompt
drift counters.

---

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
8. **Internal cache state may be narrower than provider identity.** DotCraft may track remembered prompt-cache breakpoints under an internal state key such as `thread:<id>:maintenance:<kind>:<run>` so maintenance forks and the main conversation do not overwrite each other's breakpoint history. One-shot maintenance forks may use that state key in `readOnlyPrefix` mode without committing new remembered breakpoints. This internal state key MUST NOT replace provider-visible cache-session or current-thread routing identity.
9. **Tool identity shape is cache state.** Canonical namespace/name pairs, flat aliases, namespace grouping, and child ordering come from the immutable Turn snapshot. Provider adapters must not re-sanitize names, derive namespaces from runtime source names, or enumerate collision groups in discovery order. History replay uses persisted canonical tuples for namespace-capable providers and persisted flat aliases for flat-only providers.
10. **Responses SubAgent guidance is fork-boundary history.** On `openai-responses`, native SubAgent role instructions are materialized once as a developer message after inherited history and before the child task. Later turns append after that message; request adapters must not relocate it before the latest user message. Changing or clearing the instructions establishes an explicit replacement boundary. Other protocols keep native SubAgent role instructions in their generated system prompt.

---

## 4. Failure modes the runtime must guard against

| Symptom | Required guard |
|---------|----------------|
| Cache-control field set in caller options never reaches the wire | Runtime MUST verify the field survives any chat-client wrapper layer; tests must assert wire-level presence, not just in-memory presence on the option object |
| Duplicate top-level JSON keys in the wire body | Runtime MUST emit a canonical body. If the underlying serializer cannot be coerced, a pipeline-level deduplicator MUST run before transport |
| Volatile content leaks into the cached prefix | Prompt construction MUST keep timestamps, runtime context, and any other request-local data confined to the latest user turn |
| Reasoning encrypted content mutated between turns | Conversion layers MUST pass `encrypted_content` through unchanged; round-trip tests cover the case |
| Cache-read drop after reasoning settings change | Prompt-cache diagnostics MUST include a reasoning/thinking fingerprint and classify the drop as a request-shape change instead of likely server-side routing |
| One-shot maintenance fork writes an unneeded tail breakpoint | Fork cache shaping MUST use `readOnlyPrefix` when no tool execution is enabled, mark only the reusable prefix, and skip committing fork-local remembered breakpoints |
| Provider sticky-routing flap (ChatGPT OAuth) | Recognised as an upstream limitation. The runtime reports observed coverage faithfully and does not retry just to chase a higher hit rate |
| Provider returns an empty post-tool response | After at least one tool result has been returned to the model, a normally completed response with no assistant content, reasoning output, or tool call ends the turn successfully without retrying or emitting `agent_empty_response`. An interrupted stream or explicit provider error still fails the turn |
| A tool identity cannot be represented by the target provider | Request serialization fails locally with stable `invalid_provider_tool_identity` diagnostics before HTTP transport; the runtime must not send a request known to violate the provider's name/length grammar |

---

## 5. Measurement contract

Every prompt-cache-relevant change MUST be validated with AppServerTestClient's
`prompt-cache-smoke` command, which runs the `prompt-cache-baseline` scenario with
context compaction disabled:

- Workload: a deterministic large file (~30k characters) read three times by the agent.
- Reporting: aggregate hit rate (cached_input_tokens / input_tokens), the enforced
  `minimumCacheHitRate`, and per-call breakdown.
- Guardrail: any `ContextCompaction` trace event fails the baseline run because compaction
  rewrites the provider-visible prefix and contaminates cache-hit comparisons.
- Pass criterion: the aggregate hit rate MUST stay above the configured floor. Matrix rows MAY
  set `minimumCacheHitRate`; otherwise AppServerTestClient uses conservative defaults below the
  empirical envelopes in §2: `openai-chat-completions` 0.50, `openai-responses` 0.30,
  ChatGPT OAuth Responses 0.35, and `anthropic` 0.50.

Each protocol's envelope is a calibration baseline, not a contract. Provider routing instability can swing any single call by tens of percentage points; multi-run trends matter more than single numbers.

---

## 6. Future work

- Verify each routing signal in isolation against the ChatGPT backend to measure the effect of installation id, `session-id`, `thread-id`, `x-codex-window-id`, `x-codex-turn-state`, and body-level `client_metadata` identities.
- Run opt-in A/B profiles for the alternate User-Agent profile and explicit `OpenAI-Beta` values, measuring cache coverage, 401/403 rate, first-token latency, and rate-limit headers before considering any default change.
- Surface per-call cache coverage in the desktop dashboard so drift from the expected pattern is visible without trace-database inspection.
- Audit cache-marker placement on the anthropic protocol: the current marks cover system prompt and snapshot prefix; additional marks on large stable always-loaded tool definitions or the first user instruction block may raise the envelope.
