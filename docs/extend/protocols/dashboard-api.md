# Dashboard API

Dashboard API is intended for the debugging UI and internal tools. For everyday troubleshooting the Dashboard pages are enough — see [Observability](../../self-hosting/observability). This page is for building an integration or debugging the frontend.

Every route lives under `/dashboard/`, and the page itself is `GET /dashboard/`.

## Standalone read-only viewer

Use Dashboard without starting AppServer, Desktop, channels, Dreams, Automations, MCP, or LSP:

```bash
dotcraft dashboard --workspace /path/to/workspace
dotcraft dashboard --workspace /path/to/workspace --host 127.0.0.1 --port 8081
```

`--workspace` accepts either the workspace root or its `.craft` directory. When omitted, the current directory is used. This mode ignores `DashBoard.Enabled`, but reuses `DashBoard.Host`, `DashBoard.Port`, `Username`, and `Password` from config unless `--host` or `--port` override them.

Read-only mode only exposes trace, session listing, token usage, tools, runtime metadata, and event stream endpoints. It does not register Settings write endpoints, Dreams endpoints, Automations endpoints, or session/thread deletion endpoints, and it opens existing `state.db` data without creating or migrating workspace state. The command exits with an error when `.craft/state.db` does not exist.

## Trace event types

| Type | Description |
|------|-------------|
| `SessionMetadata` | Session system prompt and tool schema metadata |
| `AgentInstructions` | Effective `AGENTS.md` instruction snapshot selected for the session |
| `Request` | User request |
| `Response` | Model response content segment |
| `ToolCallStarted` | Tool call started |
| `ToolCallCompleted` | Tool call completed |
| `ToolInjection` | Simulated deferred loading injected tool schemas into the next model request |
| `DeferredToolLoading` | Provider-native deferred loading activated deferred tools through `SearchTools` |
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
| `SubAgentPrefixDiagnostic` | One-time comparison between a native subagent's first Responses request and its direct parent's fork anchor |
| `MaintenanceForkRequest` | Maintenance fork request |
| `MaintenanceForkResponse` | Maintenance fork response |
| `WorldStateDiagnostic` | What the world state told the model for one sampling step, including the steps that sent nothing |

Dashboard records `Thinking` and `Response` trace events by contiguous streaming content segment, not per chunk, and does not collapse a full turn into one event. `ThinkingCount` and `ResponseCount` therefore count segments. The realtime event stream emits a segment event once that segment ends and is recorded.

`ResponseTerminal`, `ProviderError`, and `ProviderResponseDiagnostic` are diagnostic-only events. They are not written into thread rollout history as assistant text. `ResponseTerminal` records finish reason and stream-shape metadata even for usage-only or empty terminal updates. Provider diagnostics record sanitized status, error, and incomplete reason fields only; they must not persist raw prompts, full request bodies, or large tool arguments.

The **Responses** filter includes `Response` and `ResponseTerminal`. The **Provider** filter includes `ProviderError` and `ProviderResponseDiagnostic`.

The **Instructions** filter includes `AgentInstructions`. Its `content` field contains the exact rendered instruction text, including an empty string when no instructions are loaded. Its `metadataJson` has this shape:

```json
{
  "schemaVersion": 1,
  "kind": "agents_md.instructions",
  "role": "user",
  "fingerprint": "sha256:...",
  "sources": ["/path/to/AGENTS.md"]
}
```

The fingerprint covers both content and ordered sources. Equivalent snapshots are de-duplicated. This diagnostic is not a system prompt, ordinary request, or model-history item. Dashboard reads the persisted snapshot in live and standalone read-only modes instead of loading instruction files.

Each completed provider stream attempt emits a `ProviderResponseDiagnostic` with `eventType=stream_attempt`. Its metadata includes `requestIndex`, `attemptNumber`, `retryLimit`, `outcome`, `retryDecision`, `failureKind`, `durationMs`, and `visibleOutputEmitted`. OpenAI Responses diagnostics also include the final HTTP status, upstream request ID, and SHA-256 hashes of the effective session, thread, and prompt-cache identities. Raw routing identities, credentials, request bodies, and response bodies are excluded.

Maintenance requests such as context compaction also record `MaintenanceForkRequest` / `MaintenanceForkResponse` events. These events preserve snapshot/cache metadata, raw model text, tool-call-only responses, empty responses, and fallback reasons so Dashboard can diagnose issues such as `summary_unavailable`.

`DeferredToolLoading` is used for provider-native deferred tool loading, currently OpenAI Responses and Anthropic beta tool references. It records the tools newly activated by `SearchTools`, the configured strategy, the effective mode, the provider protocol, and the provider wire shape; it does not mean top-level `tools` were injected and it is not marked as a prompt-cache tool extension.

`PromptCacheRequestShape` records SHA-256 hashes and counts for OpenAI Responses request components so adjacent requests can be compared for prefix stability. It also records sanitized effective option flags such as requested max output tokens, whether OAuth rewriting removes them before transport, reasoning effort, tool-choice kind, tool count, and streaming mode.

`SubAgentPrefixDiagnostic` compares a native subagent's first OpenAI Responses request with the direct parent's request captured at fork time. Its `status` is `compatible`, `staticShared`, `diverged`, or `unavailable`. `compatible` requires equal cache identity and leading request components plus at least one retained parent input item; a later fork-specific suffix is expected. `staticShared` means the static prefix matched but no input item was retained. Metadata contains component hashes, request and attempt indexes, input counts, the matched prefix length, `exactParentInputPrefix`, the first zero-based divergence index, and `changedFields`; it contains no prompt text, tool schema, or input item content. Chat Completions and Anthropic sessions expose their parent relationship without inferring prefix equality.

The **World State** filter includes `WorldStateDiagnostic`. One event is recorded for every sampling
step, including a step that sent nothing, because "the model was told nothing" and "the section
never ran" are otherwise indistinguishable after the fact. Its `status` is `full`, `emitted`, or
`unchanged`, and `baselineSource` is `none`, `memory`, `rollout`, or `fork`. Its `metadataJson` has
this shape:

```json
{
  "schemaVersion": 1,
  "status": "unchanged",
  "turnId": "turn_002",
  "baselineSource": "memory",
  "changedSections": [],
  "unchangedSections": ["environment", "mode"],
  "suppressedSections": [{ "id": "acme_review", "reason": "render_failed" }]
}
```

Section ids are recorded; rendered section text is not.

## Endpoints

### `GET /dashboard/`

Returns the Dashboard page.

### `GET /dashboard/api/summary`

Returns runtime summary, including session count, recent events, and module state.

### `GET /dashboard/api/sessions`

Returns sessions visible to Dashboard. Child sessions include `parentSessionKey`. Their `parentPrefix` is either `null` when no diagnostic was recorded or a summary containing `status`, input counts, `matchedInputItemCount`, `exactParentInputPrefix`, `expectedSharedPrefix`, cache/static compatibility flags, `divergenceIndex`, and `changedFields`. `status` is `compatible` when the static prefix matches and an ordered input prefix was retained, `staticShared` when the static prefix matches but no input item was retained, `diverged` when a leading request component changed, or `unavailable` when the parent shape was missing. `expectedSharedPrefix` is true only when the child inherited parent turns, so a `staticShared` child that was spawned fresh is not a defect. Parent sessions expose their relationship through the child records; Dashboard derives the displayed child count from the returned list.

### `GET /dashboard/api/sessions/{sessionKey}/events`

Returns trace events for one session.

### `GET /dashboard/api/runtime`

Returns the Dashboard host mode, full workspace path, and capability flags. In standalone read-only mode, `mode` is `readOnly`, `readOnly` is `true`, and `settings`, `dreams`, `automations`, and `sessionDeletion` capabilities are `false`.

### `GET /dashboard/api/orchestrators/automations/state`

Returns `{ automations, countsByStatus, generatedAt }` from the unified automation service. Counts describe definitions (`active`, `paused`, `completed`); per-run results are available through `automation/runs/list`.

### `POST /dashboard/api/orchestrators/automations/refresh`

Requests an Automations state refresh.

### `GET /dashboard/api/config/schema`

Returns the configuration schema used by the Dashboard Settings page.

### `GET /dashboard/api/config/edit`

Returns the user layer as `global`, the workspace layer as `workspace`, and the effective configuration as `merged`, with sensitive values masked as `***`, plus each layer's file path.

### `POST /dashboard/api/config/workspace`

Replaces the workspace configuration file with the posted JSON object. A `***` value keeps the stored secret at that path. The document must load as a valid configuration and every changed field must satisfy its type, range, and allowed values. An accepted save is applied to the running AppServer and announced through `config/changed` with the changed key paths. An invalid document returns `400` with an English `error` and leaves the file unchanged.

### `GET /dashboard/api/dreams/status`

Returns the workspace's Dreams settings (`enabled`, `interval`, `autoApply`, `minSessionsSinceLastRun`), `running`, `nextRunAt`, the latest run as `lastRun` (`null` when Dreams has never run), and `pendingCount`, the number of runs waiting for review.

A run carries `id`, `scope` (omitted for the workspace memory store, otherwise the scope of an Agent Profile's own store), `status` (`running`, `succeeded`, `skipped`, `failed`, or `canceled`), `trigger` (`manual` or `scheduled`), timestamps, `changes` (the memory files the run added, updated, and deleted, and whether `MEMORY.md` changed), `reviewStatus` (`pending`, `applied`, `discarded`, or `archived`), `autoApplied`, `summary`, `errorType` and `message`, and the internal `threadId` and `turnId`. The [AppServer protocol specification](https://github.com/DotHarness/dotcraft/blob/main/specs/protocols/appserver-protocol.md#271-run-object) lists every field.

### `GET /dashboard/api/dreams/runs`

Returns `{ runs }` with every run, archived runs included.

### `GET /dashboard/api/dreams/runs/{runId}`

Returns one run with its review material as `{ run, files, manifest }`. `files` lists each file the run changed, `MEMORY.md` included, with its `name`, `change` (`added`, `updated`, or `deleted`), the `baseline` text from when the run started, and the proposed `candidate` text. An added file has no `baseline` and a deleted file has no `candidate`. `manifest` is the run's input manifest as Markdown, or an empty string when there is none. Returns `404 Not Found` for an unknown run.

### `POST /dashboard/api/dreams/run`

Requests a manual run for every store Dreams consolidates that holds memory files or has sessions since its last consolidation. Those stores are the workspace memory store and the store of each Agent Profile that declares `memory: own`. The runs execute one at a time in the background and skip `Dreams.Interval` and `Dreams.MinSessionsSinceLastRun`.

Returns `{ run, status }`. `run` is the first requested run, or the active run when one is already running, and is `null` when memory or Dreams is disabled. `status` has the shape of the status endpoint.

### `POST /dashboard/api/dreams/runs/{runId}/{action}`

Runs a review action on one run and returns `{ run }` with its updated state.

| `action` | Accepted for | Effect |
|----------|--------------|--------|
| `apply` | A pending run | Replaces the store's memory files and `MEMORY.md` with the run's candidate, only while the store still matches the run's baseline |
| `restore` | An applied run | Writes the run's baseline back, only while the store still matches the run's candidate. The run becomes `discarded` |
| `discard` | A pending run | Marks the run `discarded` and leaves the store unchanged |
| `archive` | A run that is not running | Marks the run `archived`, which hides it from default AppServer lists. Its files, internal thread, and trace stay |
| `cancel` | A running run | Cancels the run, best effort |

When the store changed after the run started, `apply` returns `409 Conflict` with `code: "storeChanged"`, records that error on the run, and leaves the run pending and the store untouched. `restore` returns the same `409 Conflict` when the store no longer matches the run's result. Errors return `{ error, code }`. An unknown run returns `404 Not Found`, and an unknown action or a run in the wrong state returns `400 Bad Request`.

### `DELETE /dashboard/api/dreams/runs/{runId}`

Permanently deletes one run. Returns `404 Not Found` when the run does not exist and `409 Conflict` when it is running.

### `DELETE /dashboard/api/dreams/runs`

Permanently deletes every run, archived runs included. Returns `409 Conflict` before deleting anything when a run is active.

Deletion removes each run's directory and, best effort, its internal thread and trace. It never changes a memory store. Afterwards the latest Dreams state is rebuilt from the newest remaining run, or cleared when no run remains. Both endpoints return:

```json
{
  "deletedRunIds": ["dream_20261013000000_abc123"],
  "deletedCount": 1,
  "partial": false,
  "traceCleanupFailures": []
}
```

When thread or trace cleanup fails, the request still succeeds with `partial: true`, and each `traceCleanupFailures` entry carries `runId`, `threadId`, and `error`.

### `DELETE /dashboard/api/sessions/{sessionKey}`

Deletes one Dashboard session record.

### `DELETE /dashboard/api/sessions`

Clears Dashboard session records.

### `GET /dashboard/api/events/stream`

Returns the event stream used by Dashboard.

## Usage notes

- In standalone read-only mode, disabled feature and mutation endpoints return 404 or 405 because those routes are not registered.
- Prefer binding to `127.0.0.1` for local debugging.
- Do not expose an unprotected Dashboard in production or shared networks.

## Related docs

- [AppServer Protocol](./appserver) — the JSON-RPC surface that produces these traces.
- [Hub Protocol](./hub) — where the local Dashboard URL is returned alongside the AppServer endpoint.
