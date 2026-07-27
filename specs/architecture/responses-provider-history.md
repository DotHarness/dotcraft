# Canonical OpenAI Responses Provider History

| Field | Value |
|---|---|
| **Version** | 0.3.0 |
| **Status** | Living |
| **Date** | 2026-07-26 |
| **Parent Specs** | [Session Core](session-core.md), [Prompt Cache](prompt-cache.md), [OpenAI Subscription Auth](openai-subscription-auth.md) |

## Overview

Microsoft.Extensions.AI (MEAI) `ChatMessage` history remains DotCraft's provider-neutral agent
history. OpenAI Responses additionally requires the exact ordered response-item sequence that was
sent to and returned by the provider. Reconstructing that sequence from turn-aggregated MEAI
messages changes item grouping across turns and invalidates otherwise reusable prompt-cache
prefixes.

For opted-in threads, DotCraft therefore maintains a second, Responses-native history. It is the
source of truth for the Responses `input` array only. MEAI history remains the source of truth for
request-local model execution, UI projection, provider-neutral recovery, token estimation, and
every non-Responses protocol. Session Core owns that history directly and persists it through the
rollout contracts defined in [Session Core](session-core.md).

## Activation

The canonical provider-history capability is selected when the thread is created and persisted in
the canonical `thread_opened` baseline as `providerHistorySchemaVersion = 1`.

- Every current thread and fork uses version 1.
- A missing or unsupported capability version is a rollout error and is not inferred or migrated.
- The capability is internal rollout state and is not part of the public SessionThread/AppServer
  schema.
- Both API-key and ChatGPT OAuth `openai-responses` runtimes consume the capability.
- Version 1 is consumed only for server-managed history. Client-managed history uses direct
  per-request mapping because the caller, rather than the rollout, owns its preceding prefix.
- Anthropic and OpenAI Chat Completions never consume or mutate Responses provider history.

## Canonical history

A provider-history generation is an ordered list of provider-visible JSON input items associated
with one context window. Items are stored as JSON objects rather than MEAI CLR content so item
type, property order, provider ID, `call_id`, reasoning summary, and `encrypted_content` survive
without aggregation or reinterpretation.

The thread rollout supports:

- `provider_history_items_appended`: ordered local-input or provider-output entries for a turn;
- `provider_history_replaced`: a complete baseline created by compaction, protocol return, or fork
  materialization;
- `provider_history_attempt_aborted`: a tombstone for provider-output entries from an attempt that
  the stream retry layer deliberately discards.

Every record carries schema version, thread id, protocol, generation id, and context-window id.
Append entries additionally carry turn id, source, optional attempt id, a stable ledger entry id,
and the exact item object. Provider-output entries are captured from completed raw Responses
output items before tool-search normalization and MEAI conversion.

Local items are appended before transport. Provider-output items are appended as soon as the raw
`response.output_item.done` event is consumed, so completed output survives a later cancellation or
terminal stream failure. A persistent append failure fails the model request before the
corresponding local tool is executed.

## Request construction

For a version-1 thread, the Responses request input is:

1. the current canonical generation;
2. plus only the current MEAI sampling tail not already represented by that generation.

The turn runtime captures a fingerprinted MEAI baseline before the current user input enters the
agent. The function-invocation wrapper marks MEAI response projections as covered after it updates
its augmented sampling history. This lets the next tool-loop request map only newly appended tool
results and guidance.

Request-local history sanitization is not a conversation-history replacement. A sanitizer may
repair an incomplete tool pair or remove content that is invalid for a role in the current request,
but that projection must not replace, truncate, reorder, or regenerate the canonical Responses
generation. Object identity or collection shape changes produced by sanitization are never evidence
that canonical history changed.

The existing Responses mapper remains authoritative for converting local MEAI content, assigning
locally generated item IDs, and sanitizing invalid IDs. A correlation index derived from canonical
calls is supplied when mapping a tail so a result for a native tool-search call remains a
`tool_search_output`.

Instructions, tools, reasoning configuration, `prompt_cache_key`, OAuth body shaping, and headers
continue to use their existing request paths. Canonical history changes only the `input` array.

When tools are present, `ChatOptions.ToolMode` maps to the final Responses `tool_choice` field:

- `None` maps to `none`;
- `Auto` maps to `auto`;
- `RequireAny` maps to `required`;
- a required function name maps to a function choice for that exact name.

An explicitly configured provider-native tool choice takes precedence. No `tool_choice` is added
when the request has no tools.

Before transport, request-local normalization supplies a deterministic `aborted` output for a
client function/tool-search call that has no output and removes orphan client outputs. Synthetic
items are not persisted; their IDs are derived from the source call item ID so repeated sampling
has the same byte shape.

## Lifecycle

One `OpenAIResponsesProviderHistoryContext` belongs to one active Thread/Turn sampling chain and has
one consumer. Preparing input, beginning or ending an attempt, appending provider output, marking
MEAI projection coverage, replacing history, aborting, and snapshotting are transitions of that
single state machine. Supporting concurrent consumers would require serializing the complete
transition set; adding an isolated lock to one method is not sufficient.

- **Retry:** provider-output entries carry an attempt id. Immediately before an attempt is
  deliberately retried, DotCraft appends an abort tombstone and removes that attempt from live
  history. If a streaming consumer cancels, disposes, or stops enumeration before normal
  completion, its attempt is aborted even when no retry is issued. A normally completed attempt
  becomes part of the canonical prefix.
- **Cold resume:** replay selects the newest valid replacement and appends surviving, non-aborted
  entries in rollout order.
- **Rollback:** entries for removed turns are excluded. A replacement whose covered turn no longer
  survives is invalid and replay continues to an earlier baseline.
- **Compaction:** successful auto or manual replacement maps the final compacted MEAI history once,
  starts a new provider-history generation, and shares the existing context-window advance.
- **Protocol change:** leaving Responses leaves the generation untouched. Returning after
  non-Responses turns creates a replacement from current MEAI history and advances the context
  window before the next Responses request.
- **Fork:** a whole-turn fork copies an exact canonical prefix when available. Partial or legacy
  forks create a new snapshot from the fork materialization. Fork state is copied, never linked.
- **Ephemeral threads:** use the same runtime state in memory and persist nothing until normal
  thread-promotion behavior makes the thread durable.

Canonical replacement is an explicit lifecycle transition. Successful compaction, protocol return,
and fork materialization may create `provider_history_replaced`; request-local sanitization,
provider adapter normalization, retry preparation, and ordinary tool-loop projection may not.

## MEAI projection boundaries

Responses reasoning output is assistant-authored content even when the provider SDK leaves the
corresponding MEAI streaming update role unset. Before an update enters a cross-service-call
aggregate, the Responses adapter assigns it `Assistant` role so a reasoning item following a local
tool-result update starts a new MEAI message instead of inheriting the `Tool` role.

The provider response-item ID remains content-scoped metadata and is not promoted to
`ChatMessage.MessageId`. A valid provider ID is preserved on every streamed reasoning fragment and
on its encrypted completion fragment so MEAI content coalescing, model-history persistence, and
outbound mapping retain one identity. Missing or invalid item IDs do not suppress the Assistant
message boundary.

## Recovery and privacy

Unknown capability versions or malformed records required by the active generation produce the
stable internal error `responses_provider_history_corrupt` for Responses sampling. They do not
prevent the domain thread or generic MEAI history from being read or used by another protocol.

Provider-history records are internal recovery state. Context search, previews, and ordinary
context export must not index or expose their item JSON. Encrypted reasoning is intentionally
durable but must never be copied into diagnostics.

## Acceptance checklist

- Consecutive tool-loop and cross-turn Responses requests keep the previous request input as a
  byte-identical prefix and append only completed provider items and new local tail items.
- Request-local sanitization never emits `provider_history_replaced`; successful compaction emits
  exactly one replacement for the new context window.
- Provider IDs, `call_id`, item ordering, and encrypted reasoning bytes survive turn completion,
  cold resume, rollback, and compatible fork.
- Reasoning emitted after a tool result is projected as Assistant content, while the Tool message
  contains only the corresponding tool results.
- Compaction and protocol return establish an explicit, diagnosable prefix boundary.
- Legacy threads retain their existing Responses wire shape.
- Anthropic and OpenAI Chat Completions produce the same transport shapes and persistence behavior
  as before this capability.
- The complete test suite passes and the ChatGPT OAuth prompt-cache smoke median does not regress
  by more than five percentage points from its pre-change baseline.
