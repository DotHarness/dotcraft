# DotCraft Dashboard API

Dashboard API is intended for the debugging UI and internal tools. Most users should use the Dashboard pages directly; use this page when building integrations or debugging the frontend.

## Standalone Read-Only Viewer

Use Dashboard without starting AppServer, Desktop, channels, Dreams, Automations, MCP, or LSP:

```bash
dotcraft dashboard --workspace /path/to/workspace
dotcraft dashboard --workspace /path/to/workspace --host 127.0.0.1 --port 8081
```

`--workspace` accepts either the workspace root or its `.craft` directory. When omitted, the current directory is used. This mode ignores `DashBoard.Enabled`, but reuses `DashBoard.Host`, `DashBoard.Port`, `Username`, and `Password` from config unless `--host` or `--port` override them.

Read-only mode only exposes trace, session listing, token usage, tools, runtime metadata, and event stream endpoints. It does not register Settings write endpoints, Dreams endpoints, Automations endpoints, or session/thread deletion endpoints, and it opens existing `state.db` data without creating or migrating workspace state. The command exits with an error when `.craft/state.db` does not exist.

## Trace Event Types

| Type | Description |
|------|-------------|
| `SessionMetadata` | Session system prompt and tool schema metadata |
| `Request` | User request |
| `Response` | Model response content segment |
| `ToolCallStarted` | Tool call started |
| `ToolCallCompleted` | Tool call completed |
| `ToolInjection` | Simulated deferred loading injected tool schemas into the next model request |
| `DeferredToolLoading` | Provider-native deferred loading activated deferred tools through `tool_search` |
| `TokenUsage` | Token usage for one LLM request |
| `Error` | Runtime error |
| `ResponseTerminal` | Terminal diagnostic for one streaming model request, even when no text was emitted |
| `ProviderError` | Non-fatal provider error content or provider stream error metadata |
| `ProviderResponseDiagnostic` | Sanitized provider terminal/status metadata, stream-attempt outcomes, and OpenAI request identifiers |
| `ContextCompaction` | Context compaction |
| `Thinking` | Model thinking content segment |
| `PromptCachePoint` | Prompt cache breakpoint summary |
| `PromptCacheDiagnostic` | Prompt cache hit/break diagnostic |
| `PromptCacheRequestShape` | OpenAI Responses request shape hashes for prompt-cache prefix diagnostics |
| `SubAgentPrefixDiagnostic` | One-time comparison between a native SubAgent's first Responses request and its direct parent's fork anchor |
| `MaintenanceForkRequest` | Maintenance fork request |
| `MaintenanceForkResponse` | Maintenance fork response |

Dashboard records `Thinking` and `Response` trace events by contiguous streaming content segment, not per chunk, and does not collapse a full turn into one event. `ThinkingCount` and `ResponseCount` therefore count segments. The realtime event stream emits a segment event once that segment ends and is recorded.

`ResponseTerminal`, `ProviderError`, and `ProviderResponseDiagnostic` are diagnostic-only events. They are not written into thread rollout history as assistant text. `ResponseTerminal` records finish reason and stream-shape metadata even for usage-only or empty terminal updates. Provider diagnostics record sanitized status, error, and incomplete reason fields only; they must not persist raw prompts, full request bodies, or large tool arguments.

The **Responses** filter includes `Response` and `ResponseTerminal`. The **Provider** filter includes `ProviderError` and `ProviderResponseDiagnostic`.

Each completed provider stream attempt emits a `ProviderResponseDiagnostic` with
`eventType=stream_attempt`. Its metadata includes `requestIndex`, `attemptNumber`, `retryLimit`,
`outcome`, `retryDecision`, `failureKind`, `durationMs`, and `visibleOutputEmitted`. OpenAI
Responses diagnostics also include the final HTTP status, upstream request ID, and SHA-256 hashes
of the effective session, thread, and prompt-cache identities. Raw routing identities, credentials,
request bodies, and response bodies are excluded.

Maintenance requests such as context compaction and memory consolidation also record `MaintenanceForkRequest` / `MaintenanceForkResponse` events. These events preserve snapshot/cache metadata, raw model text, tool-call-only responses, empty responses, and fallback reasons so Dashboard can diagnose issues such as `summary_unavailable`.

`DeferredToolLoading` is used for provider-native deferred tool loading, currently OpenAI Responses and Anthropic beta tool references. It records the tools newly activated by `tool_search`, the configured strategy, the effective mode, the provider protocol, and the provider wire shape; it does not mean top-level `tools` were injected and it is not marked as a prompt-cache tool extension.

`PromptCacheRequestShape` records SHA-256 hashes and counts for OpenAI Responses request components so adjacent requests can be compared for prefix stability. It also records sanitized effective option flags such as requested max output tokens, whether OAuth rewriting removes them before transport, reasoning effort, tool-choice kind, tool count, and streaming mode.

`SubAgentPrefixDiagnostic` compares a native SubAgent's first OpenAI Responses request with the direct parent's request captured at fork time. Its `status` is `compatible`, `diverged`, or `unavailable`. `compatible` requires equal cache identity and leading request components plus at least one retained parent input item; a later fork-specific suffix is expected. Metadata contains component hashes, request and attempt indexes, input counts, the matched prefix length, `exactParentInputPrefix`, the first zero-based divergence index, and `changedFields`; it contains no prompt text, tool schema, or input item content. Chat Completions and Anthropic sessions expose their parent relationship without inferring prefix equality.

## Endpoints

### `GET /DashBoard`

Returns the Dashboard page.

### `GET /DashBoard/api/summary`

Returns runtime summary, including session count, recent events, and module state.

### `GET /DashBoard/api/sessions`

Returns sessions visible to Dashboard. Child sessions include `parentSessionKey`. Their `parentPrefix` is either `null` when no diagnostic was recorded or a summary containing `status`, input counts, `matchedInputItemCount`, `exactParentInputPrefix`, `expectedSharedPrefix`, cache/static compatibility flags, `divergenceIndex`, and `changedFields`. `status` is `compatible` when the static prefix matches and an ordered input prefix was retained, `staticShared` when the static prefix matches but no input item was retained, `diverged` when a leading request component changed, or `unavailable` when the parent shape was missing. `expectedSharedPrefix` is true only when the child inherited parent turns, so a `staticShared` child that was spawned fresh is not a defect. Parent sessions expose their relationship through the child records; Dashboard derives the displayed child count from the returned list.

### `GET /DashBoard/api/sessions/{sessionKey}/events`

Returns trace events for one session.

### `GET /dashboard/api/runtime`

Returns the Dashboard host mode, full workspace path, and capability flags. In standalone read-only mode, `mode` is `readOnly`, `readOnly` is `true`, and `settings`, `dreams`, `automations`, and `sessionDeletion` capabilities are `false`.

### `GET /dashboard/api/orchestrators/automations/state`

Returns Automations orchestrator state, including local tasks and Cron summaries.

### `POST /dashboard/api/orchestrators/automations/refresh`

Requests an Automations state refresh.

### `GET /dashboard/api/config/schema`

Returns the configuration schema used by the Dashboard Settings page.

### `GET /dashboard/api/dreams/status`

Returns current workspace Dreams config, run status, active store, and latest run.

### `GET /dashboard/api/dreams/runs`

Returns all Dreams run records, including archived runs. Archive changes review state and does not physically delete run artifacts.

### `GET /dashboard/api/dreams/runs/{runId}`

Returns one Dreams run, active/output index preview, and topic paths for Dashboard review.

### `POST /dashboard/api/dreams/run`

Requests an immediate Dreams run.

### `POST /dashboard/api/dreams/runs/{runId}/{action}`

Runs a Dreams review action. `action` supports `apply`, `discard`, `archive`, and `cancel`.
`apply` also makes any succeeded, non-discarded, non-archived run the active store.
`archive` retains the run directory, input snapshot, output store, internal thread, and trace. Desktop uses this existing action for both Archive and Archive all; Archive all sends one request per eligible run.

### `DELETE /dashboard/api/dreams/runs/{runId}`

Permanently deletes one non-running Dreams run. Deletion removes the run directory and input snapshot, removes its output store unless that store is active, and cleans up the related internal thread and trace when present. The active store is preserved even when its producing run is deleted.

Returns `404 Not Found` when the run does not exist. Returns `409 Conflict` without deleting anything when the run is running.

### `DELETE /dashboard/api/dreams/runs`

Permanently deletes all Dreams runs. The request includes archived runs and uses the same cleanup rules as single-run deletion. If any run is running, the endpoint returns `409 Conflict` before deleting anything.

After either endpoint succeeds, Dashboard rebuilds the latest Dreams state from the newest remaining run or clears it when no runs remain.

A successful deletion returns:

```json
{
  "deletedRunIds": ["dream_20260511000000_abc123"],
  "deletedCount": 1,
  "activeDreamStoreId": "store_20260510000000_active",
  "partial": false,
  "traceCleanupFailures": []
}
```

Run/input and eligible output-store deletion is authoritative. Internal thread and trace cleanup is best-effort. If that cleanup fails after the Dreams artifacts are deleted, the endpoint still succeeds with `partial: true` and identifies each failure:

```json
{
  "deletedRunIds": ["dream_20260511000000_abc123"],
  "deletedCount": 1,
  "activeDreamStoreId": "store_20260510000000_active",
  "partial": true,
  "traceCleanupFailures": [
    {
      "runId": "dream_20260511000000_abc123",
      "threadId": "thread_20260511_abcd",
      "error": "Internal thread cleanup failed."
    }
  ]
}
```

### `DELETE /api/sessions/{sessionKey}`

Deletes one Dashboard session record.

### `DELETE /api/sessions`

Clears Dashboard session records.

### `GET /api/events/stream`

Returns the event stream used by Dashboard.

## Usage Notes

- API path casing follows the existing Dashboard routes.
- In standalone read-only mode, disabled feature and mutation endpoints return 404 or 405 because those routes are not registered.
- Prefer binding to `127.0.0.1` for local debugging.
- Do not expose an unprotected Dashboard in production or shared networks.

## Related docs

- [Observability](../../features/self-hosted/observability)
- [AppServer Protocol](./appserver-protocol)
- [Hub Protocol](./hub-protocol)
