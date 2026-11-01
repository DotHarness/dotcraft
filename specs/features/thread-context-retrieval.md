# Thread context retrieval

| Field | Value |
|-------|-------|
| **Version** | 0.9.10 |
| **Status** | Living |
| **Date** | 2026-11-01 |
| **Parent Spec** | [AppServer Protocol](../protocols/appserver-protocol.md) |
| **Related Specs** | [Session Core](../architecture/session-core.md), [Desktop Client](../clients/desktop-client.md), [Thread History Loading](thread-history-loading.md) |

## Purpose

Agents locate relevant persisted messages before reading their complete content. Overview, Item discovery and exact reads have separate contracts, so inspecting one result does not require assembling a complete conversation in the client.

## Protocol and ownership

Session Core queries the existing persisted history projection. AppServer owns opaque cursor encoding and connection-scoped visibility. Desktop owns model-facing tool arguments, safe field projection and bounded previews. Runtime Dynamic Tools keep their existing declarations and JSON text result contract.

### Message occurrences

`thread/searchOccurrences` accepts `threadId`, non-empty trimmed `searchTerm`, optional `cursor` and `limit` (default 20, 1–100). Search is a case-insensitive literal substring match in stored visible user and assistant text, ordered by Item rollout ordinal and then match offset. Tool Items, reasoning and provider-native recovery records are not searched.

The result is `{ data, nextCursor }`. Each occurrence contains `turnId`, `itemId`, `snippet`, `snippetMatchRange: { start, end }`, and `turnCursor`. Match ranges are UTF-16 offsets within the snippet, end-exclusive, and snippet boundaries do not split surrogate pairs. The Turn cursor includes the matching Turn and is accepted by `thread/turns/list` in either direction.

Search continuation is exclusive after the last returned occurrence, and is bound to the thread and exact trimmed query. Multiple occurrences in one Item can span pages without duplication or omission. Malformed, cross-thread or cross-query cursors fail rather than restarting. Updates and rollback use the current projection, not obsolete rollout versions. Matching within one message advances beyond each full match.

The query scans eligible projected messages incrementally and stops after enough occurrences to establish continuation.

### Exact Items

`thread/items/read` accepts `threadId`, `turnId` and 1–100 `itemIds`. It returns `{ data: [{ turnId, item }] }` in stored order. Duplicate IDs do not duplicate output; absent IDs are omitted. Visibility and presentation filtering match `thread/items/list`.

Reads of unloaded threads do not load execution state, recover a Turn or start background work. Projection repair remains permitted. Ephemeral histories are unsupported. Precise reads materialize the selected persisted Items.

## Desktop tools

All tools operate in the current server workspace, without selecting, resuming or subscribing to the target thread.

| Tool | Behavior |
|------|----------|
| `ReadThread` | Reads the header and a newest-first Turn page using `itemsView: summary`. `includeItems: false` requests metadata only. It never loops over all Item pages. |
| `SearchThreadMessages` | Calls `thread/searchOccurrences` with the same query and cursor semantics. |
| `ListThreadItems` | Reads one ascending page of a specified Turn's Items, returning IDs, kinds, call metadata and bounded previews. |
| `ReadThreadItem` | Calls `thread/items/read` for one ID and returns its complete model-safe text, arguments and output. A missing visible Item is an explicit failure. |

ReadThread retains `threadId/cursor/turnLimit` (default 1, 1–10), `includeOutputs` (default false), and `maxOutputCharsPerItem` (default 2,000, 0–20,000). It adds `includeItems` (default true) and `maxCharsPerItem` (default 2,000, 1–20,000).

ListThreadItems takes `threadId/turnId`, optional `cursor`, `limit` (default 20, 1–100), `includeOutputs`, `maxCharsPerItem` and `maxOutputCharsPerItem` with the same preview defaults. SearchThreadMessages uses the protocol search defaults. ReadThreadItem takes only `threadId/turnId/itemId`.

### Preview and complete values

- Summary results carry `itemsView/itemCount`; page and text truncation states remain separate.
- Body and materialized input context share the Item's character allowance. Identical body text is not repeated in multiple fields.
- Large arguments, errors and context fields cannot bypass preview limits. Shortened non-text values are explicit preview fields, never partial JSON presented as complete arguments.
- Message body previews retain `text: string` and add `textTruncated` and `textOriginalChars` when shortened. Other preview fields identify truncation and original size. Identity fields remain intact. Media is represented by references, not inline binary.
- Precise reads use the same safe field policy without preview truncation. Host-only metadata and private interaction responses are not exposed to the model.
- A per-Item limit is not an aggregate byte guarantee. Model-output limits and the CodeMode program-result limit still apply; an oversized exact read fails explicitly without automatic retry or altered data types.

The existing server summary implementation may still materialize all Items of a Turn before selecting its summary. The direct search-to-exact-read path does not depend on summary hydration. Client overview changes do not alter UI summary selection or the default AppServer `itemsView`.

## Parameter and error feedback

Input descriptions state defaults, valid ranges and cursor use using the same constants as validation. Optional cursors are omitted for the first page; invalid field names, nulls and out-of-range values are not silently corrected.

Client `InvalidArguments` failures map to `tool_input_invalid`. CodeMode error text includes the source error code, not private error parameters. MCP error delivery is unchanged.

## Data boundaries

Search follows stored Item types. Imported tool notes that were persisted as assistant text remain assistant text; output filtering does not infer their original type from marker strings. Import conversion, original-history restoration and cross-workspace discovery are outside this contract.

## Acceptance

- Search occurrences, Turn navigation and exact Item reads form a complete path without client-wide history hydration.
- Turn Item discovery is paged and can locate tool results when their Item IDs are initially unknown.
- Search continuation handles multiple matches, Unicode, query mismatches, updates and rollback correctly.
- Passive reads preserve unloaded execution state; precise queries do not first fetch the whole Turn.
- Long input, context and tool data produce valid, explicitly bounded previews and complete exact values when within existing limits.
- Invalid arguments remain actionable through the real dynamic-tool and CodeMode error path.
