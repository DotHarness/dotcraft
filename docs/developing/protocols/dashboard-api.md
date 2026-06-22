# DotCraft Dashboard API

Dashboard API is intended for the debugging UI and internal tools. Most users should use the Dashboard pages directly; use this page when building integrations or debugging the frontend.

## Standalone Read-Only Viewer

Use Dashboard without starting AppServer, Desktop, TUI, channels, Dreams, Automations, MCP, or LSP:

```bash
dotcraft dashboard --workspace /path/to/workspace
dotcraft dashboard --workspace /path/to/workspace --host 127.0.0.1 --port 8081
```

`--workspace` accepts either the workspace root or its `.craft` directory. When omitted, the current directory is used. This mode ignores `DashBoard.Enabled`, but reuses `DashBoard.Host`, `DashBoard.Port`, `Username`, and `Password` from config unless `--host` or `--port` override them.

Read-only mode only exposes trace, session listing, token usage, tools, runtime metadata, and event stream endpoints. It does not register Settings write endpoints, Dreams endpoints, Automations endpoints, or session/thread deletion endpoints, and it opens existing `state.db` data without creating or migrating workspace state.

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
| `ContextCompaction` | Context compaction |
| `Thinking` | Model thinking content segment |
| `PromptCachePoint` | Prompt cache breakpoint summary |
| `PromptCacheDiagnostic` | Prompt cache hit/break diagnostic |
| `PromptCacheRequestShape` | OpenAI Responses request shape hashes for prompt-cache prefix diagnostics |
| `MaintenanceForkRequest` | Maintenance fork request |
| `MaintenanceForkResponse` | Maintenance fork response |

Dashboard records `Thinking` and `Response` trace events by contiguous streaming content segment, not per chunk, and does not collapse a full turn into one event. `ThinkingCount` and `ResponseCount` therefore count segments. The realtime event stream emits a segment event once that segment ends and is recorded.

Maintenance requests such as context compaction and memory consolidation also record `MaintenanceForkRequest` / `MaintenanceForkResponse` events. These events preserve snapshot/cache metadata, raw model text, tool-call-only responses, empty responses, and fallback reasons so Dashboard can diagnose issues such as `summary_unavailable`.

`DeferredToolLoading` is used for provider-native deferred tool loading, currently OpenAI Responses and Anthropic beta tool references. It records the tools newly activated by `tool_search`, the configured strategy, the effective mode, the provider protocol, and the provider wire shape; it does not mean top-level `tools` were injected and it is not marked as a prompt-cache tool extension.

`PromptCacheRequestShape` records SHA-256 hashes and counts for OpenAI Responses request components so adjacent requests can be compared for prefix stability.

## Endpoints

### `GET /DashBoard`

Returns the Dashboard page.

### `GET /DashBoard/api/summary`

Returns runtime summary, including session count, recent events, and module state.

### `GET /DashBoard/api/sessions`

Returns sessions visible to Dashboard.

### `GET /DashBoard/api/sessions/{sessionKey}/events`

Returns trace events for one session.

### `GET /dashboard/api/runtime`

Returns Dashboard host mode and capability flags. In standalone read-only mode, `mode` is `readOnly`, `readOnly` is `true`, and `settings`, `dreams`, `automations`, and `sessionDeletion` capabilities are `false`.

### `GET /dashboard/api/orchestrators/automations/state`

Returns Automations orchestrator state, including local tasks and Cron summaries.

### `POST /dashboard/api/orchestrators/automations/refresh`

Requests an Automations state refresh.

### `GET /dashboard/api/config/schema`

Returns the configuration schema used by the Dashboard Settings page.

### `GET /dashboard/api/dreams/status`

Returns current workspace Dreams config, run status, active store, and latest run.

### `GET /dashboard/api/dreams/runs`

Returns Dreams run records. Archived runs are omitted by default.

### `GET /dashboard/api/dreams/runs/{runId}`

Returns one Dreams run, active/output index preview, and topic paths for Dashboard review.

### `POST /dashboard/api/dreams/run`

Requests an immediate Dreams run.

### `POST /dashboard/api/dreams/runs/{runId}/{action}`

Runs a Dreams review action. `action` supports `apply`, `discard`, `archive`, and `cancel`.
`apply` also makes any succeeded, non-discarded, non-archived run the active store.

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
