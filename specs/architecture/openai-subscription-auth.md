# OpenAI Subscription (Sign in with ChatGPT) Auth

| Field | Value |
|---|---|
| Version | 1.0.0 |
| Status | Living |
| Date | 2026-09-05 |

DotCraft natively supports authenticating outgoing model requests against a user's ChatGPT
subscription (Plus, Pro, Team, Business, Enterprise, Edu) as an alternative to the standard
pay-as-you-go OpenAI API key. This document specifies the protocol, on-disk shape, and the touch
points across Core, CLI, AppServer, and Desktop.

## Status & risk

This auth path is not part of any published OpenAI SDK. It targets the same
`chatgpt.com/backend-api/codex` surface that ChatGPT's official desktop clients consume, using a
public OAuth `client_id` that OpenAI's backend currently honours for third-party use. The protocol
is subject to change at OpenAI's discretion. Treat this as a best-effort capability: if OpenAI
rotates the `client_id` or changes the `originator` allow-list, DotCraft will need a corresponding
update.

## Authentication mechanism

OAuth 2.0 Authorization Code Flow with PKCE (S256).

| Setting | Value |
|---|---|
| Issuer | `https://auth.openai.com` |
| Authorize URL | `https://auth.openai.com/oauth/authorize` |
| Token / refresh URL | `https://auth.openai.com/oauth/token` |
| Revoke URL | `https://auth.openai.com/oauth/revoke` |
| `client_id` | `app_EMoamEEZ73f0CkXaXp7hrann` |
| Scopes | `openid profile email offline_access api.connectors.read api.connectors.invoke` |
| `originator` query / header | `codex_cli_rs` |
| Redirect URI | `http://localhost:1455/auth/callback`, fallback `1457` |
| Refresh grant | `grant_type=refresh_token` (JSON body) |
| Refresh cadence | before expiry, after 8 days without refresh, or on demand after HTTP 401 |

The authorize URL additionally carries `id_token_add_organizations=true` and
`codex_cli_simplified_flow=true`, both required by the upstream backend.

## Credential refresh lifecycle

Token access is serialized within each process. A normal token read refreshes credentials when any
of these conditions is true:

- `last_refresh` is absent;
- `last_refresh` is at least 8 days old;
- the access-token JWT expires within 5 minutes.

A forced token read first reloads `auth.json` while holding the process auth lock. If the stored
token bundle changed and belongs to the same ChatGPT account, DotCraft adopts it without contacting
the token endpoint. A missing token bundle, a missing refresh token, or a known account-id mismatch
terminates recovery with `OpenAIAuthFailureReason.NotSignedIn`.

When authority refresh is required, DotCraft sends the configured refresh grant and persists the
result atomically. An omitted `id_token`, `access_token`, or `refresh_token` retains the
corresponding current value. A returned `id_token` may update the stored account id. A successful
refresh sets `last_refresh` to the current time.

Refresh failures use the error code from `error.code`, a string `error`, or top-level `code`.
`refresh_token_expired`, `refresh_token_reused`, and `refresh_token_invalidated` are permanent
failures for every HTTP status. An unclassified HTTP 401 is also permanent. Before surfacing a
permanent failure, DotCraft reloads `auth.json` once more so a same-account token rotation completed
during the authority request can satisfy the operation. Other HTTP failures and transport failures
produce `OpenAIAuthFailureReason.Network` and leave the current token bundle available.

## On-disk credential layout

`~/.craft/auth.json` (chmod 0600 on Unix; Windows ACL granting only the current user):

```json
{
  "OPENAI_API_KEY": null,
  "tokens": {
    "id_token": "<JWT>",
    "access_token": "<JWT>",
    "refresh_token": "<opaque>",
    "account_id": "<chatgpt_account_id from id_token>"
  },
  "last_refresh": "2026-05-25T10:30:00Z"
}
```

`access_token` and `id_token` are JWTs. DotCraft reads (but does not verify) the payload to
extract `chatgpt_account_id`, `chatgpt_plan_type`, `chatgpt_user_id`, `email`, and `exp`. The
`chatgpt_plan_type` claim drives the plan tier shown in the Desktop composer footer
(`free`, `plus`, `pro`, `business`, `enterprise`, `edu`).

## Installation identifier

`~/.craft/installation_id` (plain text, 0644 on Unix):

```
11111111-2222-4333-8444-555555555555
```

The installation id is a per-machine UUID v4 generated on first launch and reused across processes
and login sessions. ChatGPT's `/backend-api/codex` surface uses the id to bucket prompt-cache
shards; including it materially improves `prompt_cache_key` hit rate on
`chatgpt.com/backend-api/codex/responses`. The id is therefore sent both as the
`x-codex-installation-id` HTTP header on every Responses-family request and inside the request
body's `client_metadata` map on create-response `/responses` requests. Compact requests do not
carry `client_metadata`. The id is not an auth secret and is not shared with the API-key code path.

If the file is missing or contains an invalid value, DotCraft regenerates a fresh UUID v4 and
overwrites the file. The id is *never* tied to a specific ChatGPT account; switching accounts
preserves the installation id.

## Model API routing

| Auth method | Base URL | Path | Auth header | Extra headers |
|---|---|---|---|---|
| API key | `https://api.openai.com/v1` | `/responses`, `/chat/completions`, `/models` | `Authorization: Bearer <api-key>` | — |
| ChatGPT OAuth | `https://chatgpt.com/backend-api/codex` | `/responses`, `/responses/compact` | `Authorization: Bearer <access_token>` | `chatgpt-account-id: <account_id>`, `originator: codex_cli_rs`, `x-codex-installation-id: <uuid>`, `session-id: <root_thread_id>`, `thread-id: <current_thread_id>`, `x-client-request-id: <current_thread_id>`, `x-codex-window-id: <window_id>`, `x-codex-turn-metadata: <json>`, `x-codex-turn-state: <state>` when established in the same logical turn |
| ChatGPT OAuth | `https://chatgpt.com/backend-api/codex` | `/models` | `Authorization: Bearer <access_token>` | `chatgpt-account-id: <account_id>`, `originator: codex_cli_rs` |

ChatGPT OAuth requests intentionally remove the OpenAI .NET SDK's `X-Stainless-*` platform
metadata headers before transport. The Codex-compatible path owns its request identity through the
headers above and the DotCraft/Codex user agent; this keeps its wire shape aligned with the Codex
client under `references/codex` without changing the SDK defaults used by API-key or compatible
OpenAI endpoints.

For HTTP Responses, DotCraft sends `session-id`, `thread-id`, and `x-client-request-id` plus the
body-level `prompt_cache_key`. `session-id` and the default cache key use the root cache-session
identity; `thread-id` and `x-client-request-id` use the currently executing DotCraft thread.
They are equal for root threads and ordinary user forks. A subagent shares its root thread's cache
session while retaining its child thread as the execution and correlation identity. On
create-response requests, `session_id` and `thread_id` are also body-level `client_metadata` keys.
Compact requests carry only the corresponding headers and compact body fields.

Each thread/session header gives the ChatGPT backend's prompt-cache shards a finer-grained anchor
than `chatgpt-account-id` alone so that requests on the same thread tend to land on the cache node
that already has the prefix warm. If there is no active thread id, DotCraft sends the request
without these thread-scoped hints and logs the degraded sticky-routing shape once for diagnosis.

`chatgpt-account-id` is resolved per request from the signed-in token/account store via
`IOpenAIAuthService.GetAccountId()`, then falls back to the runtime/config value. If the runtime
value is stale and differs from the signed-in token account, DotCraft logs a warning once and uses
the token/account-store value for request routing.

On the ChatGPT OAuth path, outgoing `/responses` request bodies are additionally augmented with
a `client_metadata` map carrying request metadata. `x-codex-turn-metadata` is the canonical metadata
envelope for the Responses API, and flat fields expose the routing identities used by the provider:

```json
{
  "client_metadata": {
    "x-codex-installation-id": "<uuid>",
    "session_id": "<root_thread_id>",
    "thread_id": "<current_thread_id>",
    "turn_id": "<turn_id>",
    "x-codex-window-id": "<window_id>",
    "x-codex-turn-metadata": "{\"installation_id\":\"<uuid>\",\"session_id\":\"<root_thread_id>\",\"thread_id\":\"<current_thread_id>\",\"turn_id\":\"<turn_id>\",\"window_id\":\"<window_id>\",\"request_kind\":\"turn\",\"turn_started_at_unix_ms\":1778544000000}"
  }
}
```

The augmentation is performed by `OpenAIResponsesClientMetadataPipelinePolicy` and only fires for
URIs whose path ends in `/responses`. Other caller-provided `client_metadata` entries are
preserved, but provider-reserved keys are authoritative runtime state. Caller-provided values for
`x-codex-installation-id`, `session_id`, `thread_id`, `turn_id`, `x-codex-window-id`, and
`x-codex-turn-metadata` are overwritten when they differ from DotCraft's active runtime context so
the header and body use one sticky-routing identity.

## Thread conversation and request identity

Session Core constructs one immutable `ThreadConversationIdentity` at the model-invocation
lifecycle boundary. It contains the current, root, parent, and fork-source thread ids; turn id;
context-window id; request kind; thread source; and subagent kind. Optional lineage fields remain
absent when the corresponding relationship does not exist. Starting another turn or replacing the
context window creates a new snapshot instead of mutating the active snapshot.

The OpenAI Responses adapter derives one request identity from that snapshot:

| Request value | Source |
|---|---|
| `session-id` / `client_metadata.session_id` | `ThreadConversationIdentity.RootThreadId` |
| `thread-id` / `client_metadata.thread_id` | `ThreadConversationIdentity.CurrentThreadId` |
| `x-client-request-id` | `ThreadConversationIdentity.CurrentThreadId` |
| Default `prompt_cache_key` | `ThreadConversationIdentity.RootThreadId` |
| Turn, window, parent, fork, source, and subagent metadata | Corresponding immutable lifecycle fields |

For a native Session Core subagent created through the thread-spawn lifecycle, provider
compatibility metadata uses the provider taxonomy rather than DotCraft's internal runtime
taxonomy:

| Provider projection | Value |
|---|---|
| `x-openai-subagent` header | `collab_spawn` |
| `client_metadata["x-openai-subagent"]` | `collab_spawn` |
| `x-codex-turn-metadata.subagent_kind` | `thread_spawn` |

The subagent's DotCraft runtime type, profile, role, parent, and root lineage remain on its Session
Core thread source and spawn edge. They are not replaced by these provider-facing compatibility
values. This projection does not participate in root/current thread resolution and therefore does
not change `session-id`, `thread-id`, `x-client-request-id`, or `prompt_cache_key`.

An explicit caller-provided `prompt_cache_key` remains authoritative over the derived default.
Requests made outside a Session Core scope retain the existing active-session compatibility
fallback. The adapter resolves that fallback once into the request identity. Request mapping,
OAuth headers, and `client_metadata` consume the resolved identity and do not independently infer
thread or source values from tracing state.

Provider state that can change within a logical turn is not part of
`ThreadConversationIdentity`. In particular, `x-codex-turn-state` remains a separately synchronized
turn-scoped value: responses may establish it, retries and later requests in the same turn may
reuse it, and ending the runtime scope discards it.

Thread/request identity remains independent from the canonical Responses item history defined in
[Canonical OpenAI Responses Provider History](responses-provider-history.md). The provider-history
capability changes only the Responses `input` source; routing identity is derived from Session
lineage and never inferred from item history.

## Responses request contract

ChatGPT OAuth model metadata describes eligibility for two internal HTTP dialects. A cached
`/models` entry with `use_responses_lite=true` marks a model as Responses Lite capable; `false`, a
missing field, or an unknown model selects the standard Responses SDK path. The latest matching
endpoint/account/client-version cache entry takes precedence over the bundled catalog, and catalog
refresh remains outside the sampling boundary. The bundled catalog marks GPT-6 Astra and GPT-5.6
Sol, Terra, and Luna as Lite capable. An internal developer gate currently keeps Lite disabled so
these models retain parallel tool execution through standard Responses. Enabling that gate makes the
metadata selection effective. This is not a user-configurable setting and never enables Lite for
API-key Responses runtimes.

Both dialects share the canonical Responses history, OAuth routing headers, `store=false`, encrypted
reasoning inclusion, stable item IDs, and prompt-cache identity described below. The standard dialect
uses the SDK's top-level `instructions` and `tools` fields. Model metadata supplies the default
`parallel_tool_calls` value; the Lite dialect always forces that value to `false`. The standard
dialect does not send the Responses Lite header or apply Lite body mapping.

Every OAuth `/responses` and `/responses/compact` request advertises
`x-codex-beta-features: remote_compaction_v2`. The Lite dialect additionally carries
`x-openai-internal-codex-responses-lite: true`. Sampling requests carry
`Accept: text/event-stream` and serialize the complete JSON body before applying Zstandard level 3
compression, independently of the selected dialect. Compact requests are not compressed. The Lite
body moves tool definitions into a leading developer
`additional_tools` input item and moves non-empty base instructions into the following developer
message. It removes the top-level `instructions`, `tools`, and `max_output_tokens` fields, strips
image `detail` values, and sets `reasoning.context=all_turns`. Sampling requests use `store=false`
and `stream=true`.

Tool choice retains the value resolved by the standard Responses mapper:
`ChatOptions.ToolMode` maps to `none`, `auto`, `required`, or a required function choice. The
Responses Lite endpoint does not support parallel tool execution and rejects
`parallel_tool_calls=true`, so the Lite mapper forces an emitted `parallel_tool_calls` field to
`false` while preserving its omission when the standard mapper does not emit it. Compact requests
apply the same restriction and omit sampling-only fields such as `tool_choice`, `stream`, `store`,
`include`, and `client_metadata`.

Every OAuth `/responses` request uses `store=false`, includes
`reasoning.encrypted_content`, and contains a `reasoning` object. The object may be empty or carry
the configured effort and summary fields. The default `prompt_cache_key` is the root cache-session
thread id; an explicit caller value remains authoritative.
Installation, window, Turn, parent-thread, subagent, and same-Turn provider state are projected only
when the corresponding runtime values exist.

Every outbound Responses input item that supports an item ID has a stable ID with a non-empty
type prefix and suffix separated by `_`. Provider-returned valid IDs are preserved. Missing IDs
are assigned before the item first enters a provider request and are retained in model-history
metadata so subsequent tool loops, rollback retries, and resumed sessions replay the same IDs.
DotCraft replaces or omits invalid IDs without changing tool `call_id`; item IDs and
tool-call correlation IDs are separate identities. Locally generated IDs use the corresponding
Responses item prefix (`msg`, `rs`, `fc`, `fco`, `tsc`, `tso`, or `ig`).

For a thread whose rollout uses the required provider-history schema version 1, completed raw provider
items and newly mapped local input items are durably appended to the canonical Responses history.
Later requests consume that item sequence directly instead of reconstructing prior turns from
aggregated MEAI messages. A missing capability version is a rollout error.

`PromptCacheRequestShape` diagnostics report the serialized input byte count and only aggregate
item-ID coverage: eligible, present, generated, missing, and invalid-source counts. They never
record item contents, credentials, or reasoning protected data.

The OAuth `/responses` transport omits the top-level `max_output_tokens` request field even when a
caller sets `ChatOptions.MaxOutputTokens`, because this backend path rejects that parameter. The
value remains available to DotCraft runtime code as a local budget, and compaction summaries still
enforce `SummaryMaxOutputTokens` after the response is received. API-key Responses requests may
send the field.

`x-codex-turn-state` is request-scoped provider state. DotCraft never fabricates it. The OAuth
pipeline captures the first non-empty `x-codex-turn-state` response header observed during a
logical Session Core turn and replays that value on subsequent `/responses` requests in the same
turn, including every bounded 401 recovery attempt. The stored value is discarded when the logical
turn's runtime scope ends and is not persisted to thread history, trace events, or the next user
turn.

When the ChatGPT OAuth stream terminates with `server_error` before emitting text, reasoning text,
or a tool call, DotCraft retries that sampling request once. Usage, error, and echoed tool-result
updates from the failed attempt remain buffered and are not surfaced or executed. A second
`server_error`, or any failure after visible model output, is surfaced immediately with the final
provider message and request ID.

Every completed stream attempt records a sanitized `ProviderResponseDiagnostic` trace event with
`eventType=stream_attempt`. The event identifies the logical request by its existing request index
and records the one-based attempt number, retry limit, outcome, retry decision, normalized failure
kind, duration, and whether visible output was emitted. OpenAI Responses attempts additionally
record the final HTTP status and upstream request ID when available. Routing values are recorded
only as SHA-256 hashes of `session-id`, `thread-id`, and `prompt_cache_key`; credentials, account
identifiers, request or response bodies, prompts, and raw routing values are never persisted.

## Responses compaction transport

ChatGPT OAuth server-managed Responses threads use the provider-native backend defined in
[Context Compaction](context-compaction.md). The backend sends
`POST https://chatgpt.com/backend-api/codex/responses/compact`; it does not send the public OpenAI
API compact contract to `api.openai.com`.

The configured OpenAI .NET `ResponsesClient` may supply the raw protocol transport because its base
endpoint and client pipeline already belong to the ChatGPT OAuth runtime. DotCraft constructs and
validates the ChatGPT-compatible JSON itself. SDK response-item objects and MEAI
`RawRepresentation` are not persistence formats.

`/responses/compact` is a Responses-family request for OAuth headers, sticky routing, Turn metadata,
and `x-codex-turn-state`. It is not a create-response request: the OAuth body policy must not inject
`client_metadata`, streaming fields, or other `/responses`-only body rewrites. The complete raw
`output` array is persisted as the next canonical Responses generation. Compaction uses the same
model metadata decision as sampling: standard models use the standard OAuth SDK transport, while
Lite models use the Lite header, body mapper, and parallel-tool restriction. Neither compact dialect
uses request-body compression.
Sampling and compaction for one runtime must not mix dialects.

## HTTP 401 recovery

OAuth SDK `/responses` requests use a bounded recovery sequence:

1. Send the request with the current access token.
2. On HTTP 401, reload `auth.json`. If a same-account token bundle changed, reapply all request
   headers and retry with that access token.
3. If no token changed, or the disk-token retry also returns HTTP 401, perform a forced token read.
   This reloads disk state again and contacts the token endpoint only when no newer token is
   available. Reapply all request headers and retry once.
4. Return the final provider response. No request can enter another 401 recovery cycle.

The sequence sends at most three provider requests. Each attempt captures response
`x-codex-turn-state`, and subsequent attempts replay the state within the same logical Turn.
Authentication recovery failures preserve the most recent provider HTTP 401 for the caller.

The direct `/models`, usage, and image-edit clients send at most two provider requests. After the
initial HTTP 401 they perform one forced token read, which adopts a same-account disk rotation or
refreshes at the authority, then retry once.

### Optional ChatGPT OAuth request profiles

DotCraft defaults to its normal `DotCraft/<version>` User-Agent and does not send `OpenAI-Beta`.
Two opt-in environment variables allow controlled A/B testing against the ChatGPT OAuth path:

| Variable | Effect |
|---|---|
| `DOTCRAFT_CHATGPT_OAUTH_UA_PROFILE=codex` | Use the alternate User-Agent profile (`codex_cli_rs/<version> (<os>; <arch>) dotcraft`) on OAuth-bound SDK requests |
| `DOTCRAFT_CHATGPT_OAUTH_OPENAI_BETA=<value>` | Send `OpenAI-Beta: <value>` on OAuth Responses-family requests |

These switches are intentionally not enabled by default because their backend effects are not part
of a public contract and must be evaluated with live token A/B data (`cached_input_tokens`,
401/403 rate, first-token latency, and rate-limit headers).

Session Core establishes the provider turn runtime scope before invoking the model. Requests outside
Session Core still receive OAuth auth headers and installation metadata, but may omit turn/window
metadata when no active thread context is available.

When `AuthMethod = chatgptOAuth`, `Protocol` is normalized to `openai-responses`. The Endpoint and
ApiKey fields configured on the provider are ignored at runtime in favour of the values above. The
model catalog is loaded from the ChatGPT backend with the same OAuth credentials:
`GET https://chatgpt.com/backend-api/codex/models?client_version=<accepted-version>`. DotCraft
caches the account-scoped response under `~/.craft/model-catalog-cache.json` for five minutes and
falls back to the bundled model catalog (`src/DotCraft.Agents.OpenAI/Resources/chatgpt-codex-models.json`)
when the network is unavailable. The `client_version` query uses the highest
`minimal_client_version` present in the bundled fallback catalog rather than DotCraft's app
version, because the ChatGPT backend uses that value to decide which models are eligible for the
client.

## Configuration shape

Per-provider entry in `~/.craft/config.json`:

```json
{
  "Providers": {
    "openai": {
      "DisplayName": "OpenAI (ChatGPT)",
      "Protocol": "openai-responses",
      "AuthMethod": "chatgptOAuth",
      "ChatGptAccountId": "acct_...",
      "ChatGptPlanType": "pro"
    }
  }
}
```

`AuthMethod` is either `apiKey` (default) or `chatgptOAuth`. `ChatGptAccountId` /
`ChatGptPlanType` are read-only metadata populated by the login flow and shown in the UI; users
should not edit them by hand.

## Integration points

| Component | File | Responsibility |
|---|---|---|
| Auth manager | `src/DotCraft.Agents.OpenAI/Auth/OpenAI/OpenAIAuthManager.cs` | Login, same-account disk reload, authority refresh, token rotation, logout, and status; thread-safe; raises `LoggedIn` / `LoggedOut` events |
| Token store | `src/DotCraft.Agents.OpenAI/Auth/OpenAI/OpenAITokenStore.cs` | Reads/writes `auth.json` with locked-down permissions |
| Installation id provider | `src/DotCraft.Agents.OpenAI/Auth/OpenAI/OpenAIInstallationIdProvider.cs` | Resolves and persists the `~/.craft/installation_id` UUID v4 |
| Auth pipeline policy | `src/DotCraft.Agents.OpenAI/Agents/Providers/OpenAI/OpenAIOAuthPipelinePolicy.cs` | Sets OAuth auth headers, resolves account id from auth service before config, adds Responses sticky headers and provider turn/window headers, captures and replays same-turn `x-codex-turn-state`, applies opt-in request profiles, and runs bounded HTTP 401 recovery |
| Responses metadata policy | `src/DotCraft.Agents.OpenAI/Agents/Providers/OpenAI/OpenAIResponsesClientMetadataPipelinePolicy.cs` | Adds/normalizes provider-compatible `client_metadata` into outgoing `/responses` request bodies on OAuth clients |
| Provider resolver | `src/DotCraft.Core/Configuration/ModelProviderRuntime.cs` | Forces `chatgpt.com/backend-api/codex` endpoint + `openai-responses` protocol in OAuth mode |
| Binding helper | `src/DotCraft.Core/Auth/OpenAI/OpenAIAuthBindingPersistence.cs` | Shared CLI/AppServer helper that writes `AuthMethod` / `ChatGptAccountId` into the global config |
| Usage client | `src/DotCraft.Agents.OpenAI/Auth/OpenAI/OpenAIUsageClient.cs` | One-shot `GET wham/usage`; reuses the same headers; 401 → force-refresh + retry once |
| Usage poller | `src/DotCraft.Agents.OpenAI/Auth/OpenAI/OpenAIUsagePoller.cs` | Singleton; 5-min cadence, 30 s manual debounce, exponential backoff to 1 h on failures; broadcasts `SnapshotChanged` |

## Usage / rate-limit telemetry

DotCraft polls the ChatGPT rate-limit endpoint for the signed-in account so the Desktop composer
can show plan-tier usage:

| Item | Value |
|---|---|
| HTTP method | `GET` |
| URL | `https://chatgpt.com/backend-api/wham/usage` |
| Headers | same Bearer / `chatgpt-account-id` / `originator` triple as Responses |
| Polling | 5 min default; debounced 30 s for manual triggers |
| Fields read | `plan_type`, `rate_limit.primary_window.{used_percent,limit_window_seconds,reset_at}`, `rate_limit.secondary_window.*`, `credits.{has_credits,unlimited,balance}`, `rate_limit_reached_type.type` |

The poller starts automatically when an account is signed in and shuts down on logout. Failures
back off exponentially (10m → 20m → 40m → 60m cap) without affecting other DotCraft features.

`primary_window` and `secondary_window` are upstream slot names, not stable display semantics.
Clients determine whether a window is the 5-hour or weekly limit from `limit_window_seconds`
(18,000 seconds and 604,800 seconds respectively, with a ±5% tolerance). Either slot may be absent,
and a weekly-only promotion may return the weekly window in `primary_window`. Presentation surfaces
must omit absent windows rather than rendering an empty 5-hour or weekly placeholder. Unknown
durations remain visible under generic primary/secondary usage labels instead of being mislabeled as
5-hour or weekly limits.

## CLI commands

```
dotcraft auth openai login   [--provider-id <id>] [--no-browser]
dotcraft auth openai logout  [--provider-id <id>]
dotcraft auth openai status
```

`login` opens the system browser to the authorize URL, waits on the loopback callback, persists
tokens, and updates the global provider registry. `--no-browser` prints the URL only (useful for
headless setups). `logout` revokes the refresh token at OpenAI and deletes the local `auth.json`.

`dotcraft setup` and the Desktop wizard accept `--auth-method chatgptOAuth` on the bootstrapped
provider; the wizard records the preference but actual sign-in happens afterward in
Settings → Providers.

## AppServer JSON-RPC

| Method | Direction | Purpose |
|---|---|---|
| `auth/openai/status` | request | Returns logged-in account metadata or `loggedIn: false` |
| `auth/openai/login` | request | Starts a login flow; blocks until the user completes the browser step |
| `auth/openai/logout` | request | Revokes + clears local tokens; unbinds the provider |
| `auth/openai/usage` | request | Returns the cached usage snapshot; triggers an inline fetch when none is cached |
| `auth/openai/authorizeUrl` | notification | Sent mid-`login` request with the browser URL (used by the desktop "Copy URL" affordance) |
| `auth/openai/usageChanged` | notification | Broadcast every time the cached usage snapshot changes (new poll, login, logout) |

`auth/openai/login` is intentionally blocking. The desktop renderer shows a "Waiting for browser
authorization..." spinner alongside the URL while the JSON-RPC call is pending. The server-side
timeout should be high (≥ 15 minutes) because the user may take a while to complete the flow on a
different device.

The capability flags `authOpenAiOAuth` and `authOpenAiUsage` (in the `initialize` response)
advertise whether the auth and usage surfaces are available.

## Desktop UX

Workspace setup wizard:
- The "OpenAI" provider template card surfaces a two-option authentication selector.
- "Sign in with ChatGPT" hides the API-key field and shows a hint that sign-in happens after setup
  completes.
- The model picker is preseeded from the bundled ChatGPT fallback catalog before sign-in. After
  ChatGPT sign-in, AppServer `model/list` refreshes the account-scoped catalog from
  `/backend-api/codex/models`.

Settings → Providers:
- The OpenAI provider editor renders the same authentication selector.
- In OAuth mode the API-key + endpoint fields are replaced by a Sign in / Sign out panel.
- A live notification stream shows the authorization URL with a "Copy URL" button while a
  sign-in request is pending.

Composer footer:
- When the active provider's `AuthMethod` is `chatgptOAuth`, a compact icon-only usage control is
  shown adjacent to the model picker on both the active conversation composer and the welcome
  composer.
- The control shows the OpenAI mark plus one mini progress rail for the most pressured remaining
  headroom window. It does not show inline numbers in the composer; green / yellow / red breakpoints
  remain 40% / 20% remaining.
- Clicking the pill opens a popover with each available usage window as a remaining-headroom
  progress bar + reset countdown + optional credits row + limit-reached warning. Known 5-hour and
  weekly windows are ordered by duration semantics rather than their upstream slot; absent windows
  are omitted.

## Failure handling

| Source | Symptom | DotCraft behaviour |
|---|---|---|
| 401 from `chatgpt.com/backend-api/codex/responses` | Access token rejected | Pipeline policy tries a same-account disk rotation, then authority refresh, with at most two retries |
| `refresh_token_expired` / `_reused` / `_invalidated` from token endpoint | Refresh token permanently invalid and no newer same-account credentials exist | `OpenAIAuthException` with explicit reason; user must re-login |
| Network error during refresh | Transient | Caller sees `OpenAIAuthFailureReason.Network`; old access token is left in place |
| User cancels browser flow | Loopback returns no `code` | `OpenAIAuthException(Unknown, "Sign-in was not completed")` |

## Limitations

- The `client_id` is a public identifier that several third-party clients also use; OpenAI's
  backend cannot distinguish DotCraft from any other client sharing that id on this code path.
  Rate limits and account-level usage caps apply globally to that identity.
- Only the `/backend-api/codex` Responses surface is supported. Chat-Completions, Assistants,
  Batch, and Files endpoints are not available on the ChatGPT backend.
- The bundled fallback catalog is only an offline/setup fallback. Signed-in ChatGPT OAuth providers
  use `/backend-api/codex/models` as the source of truth, including newly enabled account-specific
  models.
