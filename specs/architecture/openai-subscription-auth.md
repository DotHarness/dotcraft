# OpenAI Subscription (Sign in with ChatGPT) Auth

| Field | Value |
|---|---|
| Version | 0.9.9 |
| Status | Living |
| Date | 2026-10-29 |

DotCraft can authenticate outgoing model requests against a user's ChatGPT subscription (Plus,
Pro, Team, Business, Enterprise, Edu) as an alternative to a pay-as-you-go OpenAI API key. This
document specifies the OAuth protocol, credential storage, request routing, and host boundaries.

## Status & risk

This auth path is not part of any published OpenAI SDK. It targets the
`chatgpt.com/backend-api/codex` surface consumed by ChatGPT's official clients, using a public
OAuth `client_id` that OpenAI's backend honours for third-party use. The protocol may change at
OpenAI's discretion; it is a best-effort capability.

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

The loopback listener accepts a callback only when its `state` matches the pending authorization.
It answers any other request with an error page and keeps waiting. A matching callback that
carries an OAuth `error` or no `code` ends the flow. The listener binds the host's `localhost`
resolution, which may be IPv6-only; port forwards to it target `localhost`, not a literal loopback
address.

## Credential refresh lifecycle

Token storage is host-owned: the local host uses `auth.json`; an embedded model service may use
encrypted database storage. Remote workers obtain only provider metadata and authentication
status. The model service owns token reads and refresh for their requests; workers never receive
credentials.

Token access is serialized within each process. A normal token read refreshes credentials when
`last_refresh` is absent or at least 8 days old, or the access-token JWT expires within 5 minutes.

A forced token read first reloads `auth.json` under the process auth lock. If the stored token
bundle changed and belongs to the same ChatGPT account, DotCraft adopts it without contacting the
token endpoint. A missing token bundle, a missing refresh token, or a known account-id mismatch
ends recovery as not signed in.

An authority refresh sends the refresh grant and persists the result atomically. An omitted
`id_token`, `access_token`, or `refresh_token` retains the current value. A returned `id_token`
may update the stored account id. Success sets `last_refresh` to the current time.

Refresh failures use the error code from `error.code`, a string `error`, or top-level `code`.
`refresh_token_expired`, `refresh_token_reused`, and `refresh_token_invalidated` are permanent
for every HTTP status; an HTTP 400 `invalid_grant` and an unclassified HTTP 401 are also
permanent. Before surfacing a permanent failure, DotCraft reloads `auth.json` once more so a
concurrent same-account rotation can satisfy the operation. Other HTTP and transport failures are
network failures and leave the current token bundle available.

## On-disk credential layout

`~/.craft/auth.json` (0600 on Unix; Windows ACL granting only the current user):

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

DotCraft reads (but does not verify) the JWT payloads to extract `chatgpt_account_id`,
`chatgpt_plan_type` (`free`, `plus`, `pro`, `business`, `enterprise`, `edu`), `chatgpt_user_id`,
`email`, and `exp`. The plan type is exposed to clients as the account's plan tier.

## Installation identifier

`~/.craft/installation_id` (plain text UUID v4, 0644 on Unix) is a per-machine id generated on
first launch and reused across processes and sessions. The ChatGPT backend uses it to bucket
prompt-cache shards. It is sent as the `x-codex-installation-id` header on every Responses-family
request and in `client_metadata` on create-response `/responses` requests (not on compact
requests). It is not an auth secret, is not used on the API-key path, and is never tied to an
account; switching accounts preserves it. A missing or invalid file is regenerated.

## Model API routing

| Auth method | Base URL | Path | Auth header | Extra headers |
|---|---|---|---|---|
| API key | `https://api.openai.com/v1` | `/responses`, `/chat/completions`, `/models` | `Authorization: Bearer <api-key>` | — |
| ChatGPT OAuth | `https://chatgpt.com/backend-api/codex` | `/responses` | `Authorization: Bearer <access_token>` | `chatgpt-account-id: <account_id>`, `originator: codex_cli_rs`, `x-codex-installation-id: <uuid>`, `session-id: <root_thread_id>`, `thread-id: <current_thread_id>`, `x-client-request-id: <current_thread_id>`, `x-codex-window-id: <window_id>`, `x-codex-turn-metadata: <json>`, `x-codex-turn-state: <state>` when established in the same logical turn |
| ChatGPT OAuth | `https://chatgpt.com/backend-api/codex` | `/models` | `Authorization: Bearer <access_token>` | `chatgpt-account-id: <account_id>`, `originator: codex_cli_rs` |

ChatGPT OAuth requests strip the OpenAI SDK's `X-Stainless-*` platform metadata headers; the OAuth
path owns its request identity through the headers above and the DotCraft user agent. API-key and
compatible endpoints keep SDK defaults.

`session-id` and the default `prompt_cache_key` use the root cache-session thread; `thread-id` and
`x-client-request-id` use the currently executing thread. They are equal for root threads and
ordinary forks; a subagent shares its root's cache session while keeping its own thread as the
execution identity. These per-thread hints give the backend a finer cache-affinity anchor than the
account id alone. Without an active thread id, DotCraft omits them and logs the degraded routing
shape once.

`chatgpt-account-id` is resolved per request from the signed-in token store.

Create-response `/responses` bodies carry a `client_metadata` map. `x-codex-turn-metadata` is the
canonical metadata envelope; flat fields expose the routing identities:

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

Augmentation applies only to request paths ending in `/responses`. Other caller-provided
`client_metadata` entries are preserved, but the reserved keys above are overwritten with
DotCraft's runtime values so header and body share one routing identity.

## Thread conversation and request identity

Session Core builds one immutable thread conversation identity at the model-invocation boundary:
current, root, parent, and fork-source thread ids; turn id; context-window id; request kind;
thread source; and subagent kind. Absent relationships stay absent. A new turn or context window
produces a new snapshot.

The Responses adapter derives one request identity from that snapshot:

| Request value | Source |
|---|---|
| `session-id` / `client_metadata.session_id` | Root thread id |
| `thread-id` / `client_metadata.thread_id` | Current thread id |
| `x-client-request-id` | Current thread id |
| Default `prompt_cache_key` | Root thread id |
| Turn, window, parent, fork, source, and subagent metadata | Corresponding snapshot fields |

For a native subagent created through thread spawn, provider metadata uses the provider taxonomy:

| Provider projection | Value |
|---|---|
| `x-openai-subagent` header | `collab_spawn` |
| `client_metadata["x-openai-subagent"]` | `collab_spawn` |
| `x-codex-turn-metadata.subagent_kind` | `thread_spawn` |

This projection does not replace the subagent's Session Core lineage and does not affect
`session-id`, `thread-id`, `x-client-request-id`, or `prompt_cache_key`.

An explicit caller `prompt_cache_key` overrides the derived default. Requests outside a Session
Core scope resolve the active session once into the request identity; headers, mapping, and
`client_metadata` consume that resolved identity rather than inferring it from tracing state.
Routing identity comes from Session lineage, never from Responses item history.

`x-codex-turn-state` is not part of the snapshot. It is turn-scoped provider state: DotCraft never
fabricates it, captures the first non-empty response header in a logical turn, replays it on later
`/responses` requests and 401 recovery attempts in that turn, and discards it when the turn's
runtime scope ends. It is never persisted.

## Responses request contract

ChatGPT OAuth model metadata selects between two HTTP dialects. A `/models` entry with
`use_responses_lite=true` marks a model as Responses Lite capable; `false`, missing, or unknown
selects standard Responses. The latest matching endpoint/account/client-version cache entry takes
precedence over the bundled catalog, which supplies the same metadata offline. An internal
developer gate keeps Lite disabled so eligible models retain parallel tool execution; it is not
user-configurable and never applies to API-key runtimes.

Both dialects share canonical Responses history, OAuth routing headers, `store=false`,
`reasoning.encrypted_content` inclusion, a `reasoning` object (empty or with configured effort and
summary), stable item ids, and prompt-cache identity. Standard uses top-level `tools` and the
model's default `parallel_tool_calls`. Lite:

- carries `x-openai-internal-codex-responses-lite: true`;
- moves tool definitions into a leading developer `additional_tools` input item before the
  base-instruction prefix and history;
- removes top-level `tools` and `max_output_tokens`, strips image `detail`, and sets
  `reasoning.context=all_turns`;
- always sends `parallel_tool_calls=false`, including requests without tools.

Tool choice maps `none`, `auto`, `required`, or a required function choice in both dialects.

All `openai-responses` requests, including API-key and compatible endpoints, carry effective base
instructions in one developer message before the original input and omit top-level
`instructions`. Instruction sources keep their merge order and caller overrides, including
explicit removal; whitespace-only instructions produce no prefix. The prefix identity stays stable
for unchanged instructions within the current thread across turns, retries, cold resume, and
compaction, and differs for changed instructions, child threads, and forks.

Every OAuth `/responses` request sends `x-codex-beta-features: remote_compaction_v2`. Sampling
requests use `stream=true`, `Accept: text/event-stream`, and Zstandard level 3 compression of the
complete JSON body; compact requests are not compressed.

Every outbound input item that supports an id has a stable id of the form `<prefix>_<suffix>`.
Valid provider ids are preserved. Missing ids are assigned before first send and retained in
model history so tool loops, retries, and resumed sessions replay the same ids. Invalid ids are
replaced or omitted without changing tool `call_id`. Local prefixes follow the item type (`msg`,
`rs`, `fc`, `fco`, `tsc`, `tso`, `ig`).

For threads using provider-history schema version 1, completed raw provider items and newly mapped
local input items are durably appended to the canonical Responses history, and later requests
consume that sequence directly. A missing capability version is a rollout error.

Request-shape diagnostics report serialized input size and aggregate item-id coverage only, never
item contents, credentials, or protected reasoning data.

The OAuth transport omits `max_output_tokens` because the backend rejects it; the value remains a
local budget (compaction still enforces its summary limit). API-key requests may send it.

When an OAuth stream completes with only a `server_error` frame and no delivered output, DotCraft
replays the sampling request under a bounded budget. Usage, error, and echoed tool-result updates
from failed attempts stay buffered and are neither surfaced nor executed. Exhausting the budget
surfaces the final provider message and request id. A stream that breaks after delivering output
falls under the turn-level retry contract in [Model Runtime](model-runtime.md).

Each completed stream attempt records a sanitized `ProviderResponseDiagnostic` trace event with
`eventType=stream_attempt`: request index, one-based attempt number, retry limit, outcome, retry
decision, normalized failure kind, duration, whether visible output was emitted, and, for OpenAI
Responses, final HTTP status and upstream request id. Routing values appear only as SHA-256 hashes
of `session-id`, `thread-id`, and `prompt_cache_key`; credentials, account ids, bodies, prompts,
and raw routing values are never persisted.

## Responses compaction transport

Server-managed compaction sends a streaming `POST /responses` with a trailing
`compaction_trigger`, using the same endpoint, dialect, body shaping, compression policy, client
metadata, routing, and turn-state rules as sampling. DotCraft consumes raw SSE events without
executing tools or appending intermediate output to live history. Exactly one raw compaction item
and `response.completed` are required. The retained-message window followed by that item becomes
the next canonical generation after the rollout commit.

## HTTP 401 recovery

OAuth `/responses` requests use a bounded sequence:

1. Send with the current access token.
2. On HTTP 401, reload `auth.json`. If a same-account token bundle changed, reapply all headers and
   retry.
3. If nothing changed, or that retry also returns 401, perform a forced token read (authority
   refresh only when no newer token exists), reapply headers, and retry once.
4. Return the final provider response; no request enters a second recovery cycle.

At most three provider requests are sent. Each attempt captures `x-codex-turn-state` for later
attempts in the same turn. Recovery failures preserve the most recent provider 401 for the caller.

The direct `/models`, usage, and image-edit clients send at most two requests: after a 401 they
perform one forced token read and retry once.

### Optional request profiles

By default OAuth requests use the `DotCraft/<version>` User-Agent and no `OpenAI-Beta` header. Two
opt-in environment variables exist for controlled A/B testing; their backend effects are not a
public contract:

| Variable | Effect |
|---|---|
| `DOTCRAFT_CHATGPT_OAUTH_UA_PROFILE=codex` | Use `codex_cli_rs/<version> (<os>; <arch>) dotcraft` as the User-Agent on OAuth-bound SDK requests |
| `DOTCRAFT_CHATGPT_OAUTH_OPENAI_BETA=<value>` | Send `OpenAI-Beta: <value>` on OAuth Responses-family requests |

### Provider normalization and model catalog

Session Core establishes the provider turn scope before invoking the model. Requests outside
Session Core still carry auth headers and installation metadata but may omit turn/window metadata.

When `AuthMethod = chatgptOAuth`, `Protocol` is normalized to `openai-responses` and the
provider's configured Endpoint and ApiKey are ignored. The model catalog comes from
`GET https://chatgpt.com/backend-api/codex/models?client_version=<accepted-version>` with the same
credentials, cached per account in `~/.craft/model-catalog-cache.json` for five minutes, with the
bundled catalog as the offline fallback. `client_version` is a fixed compatible client version, not
DotCraft's app version, because the backend uses it to decide model eligibility.

## Configuration shape

Per-provider entry in `~/.craft/config.json`:

```json
{
  "Providers": {
    "openai": {
      "DisplayName": "OpenAI (ChatGPT)",
      "Protocol": "openai-responses",
      "AuthMethod": "chatgptOAuth"
    }
  }
}
```

`AuthMethod` is `apiKey` (default) or `chatgptOAuth`, which requires an OpenAI protocol.
Configuration never stores account metadata; account id and plan come from the signed-in token
store.

## Sign-in

ChatGPT sign-in is the `chatgptOAuth` method of the built-in OpenAI provider in [provider
authentication](../protocols/provider-auth.md). A provider must be configured with this method, or
supplied as an unsaved draft to the CLI, before signing in. Login reports the authorize URL as an
`auth_url` event and completes on the loopback callback; logout revokes the refresh token and deletes
`auth.json`. Neither writes configuration. One sign-in serves every `chatgptOAuth` provider on the
host.

## Usage / rate-limit telemetry

DotCraft polls the ChatGPT rate-limit endpoint for the signed-in account and projects it as provider
usage for every `chatgptOAuth` provider:

| Item | Value |
|---|---|
| HTTP method | `GET` |
| URL | `https://chatgpt.com/backend-api/wham/usage` |
| Headers | same Bearer / `chatgpt-account-id` / `originator` triple as Responses |
| Polling | 5 min default; debounced 30 s for manual triggers |
| Fields read | `plan_type`, `rate_limit.primary_window.{used_percent,limit_window_seconds,reset_at}`, `rate_limit.secondary_window.*`, `credits.{has_credits,unlimited,balance}`, `rate_limit_reached_type.type` |

Polling runs while an account is signed in. Failures back off exponentially (10m, 20m, 40m, 60m
cap) without affecting other features.

`primary_window` and `secondary_window` are upstream slot names, not display semantics. Clients
classify a window as 5-hour or weekly from `limit_window_seconds` (18,000 or 604,800, ±5%). Either
slot may be absent, and a weekly-only promotion may put the weekly window in `primary_window`.
Absent windows are omitted rather than shown as empty placeholders; unknown durations use generic
primary/secondary labels.

## CLI commands

Sign-in uses the provider authentication commands, for example
`dotcraft auth login openai [--no-browser]`. `--no-browser` prints the URL only.

`dotcraft setup` and the setup wizard accept `--auth-method chatgptOAuth`. The wizard signs in
before completion with `auth login --stdin` and the unsaved provider draft: tokens are saved
immediately, provider configuration only when setup is submitted. Login and setup model discovery
use the same host-selected user data directory.

A containerized deployment keeps this user data in one deployment-local mount, shared by the
DotCraft container and a headless auth helper that uses a host-network loopback listener for
login. Separate deployments must not share the directory, since they refresh tokens independently.

## Host access boundary

Hosts expose sign-in, sign-out, status, and usage through the same credential owner. A caller may
explicitly request the current access token through `provider/auth/status` only from a host that
stores those credentials locally.
Remote model-service clients receive sign-in metadata but no token. Refresh failure omits the token;
clients must never persist or log an exposed token.

## Failure handling

| Source | Symptom | DotCraft behaviour |
|---|---|---|
| 401 from `chatgpt.com/backend-api/codex/responses` | Access token rejected | Same-account disk rotation, then authority refresh, at most two retries |
| `refresh_token_expired` / `_reused` / `_invalidated` | Refresh token permanently invalid and no newer same-account credentials | Auth failure with explicit reason; user must sign in again |
| Network error during refresh | Transient | Network failure; old access token stays in place |
| User cancels browser flow | Matching callback carries `error` or no `code` | Sign-in reported as not completed |
| Stray request on the callback port | Missing or mismatched `state` | Error page; listener keeps waiting |

## Limitations

- The `client_id` is shared with other third-party clients; the backend cannot distinguish
  DotCraft, and rate limits and usage caps apply to that identity.
- Model requests use only `/backend-api/codex`. Chat Completions, Assistants, Batch, and Files are
  unavailable on the ChatGPT backend.
- The bundled catalog is only an offline/setup fallback; signed-in providers use
  `/backend-api/codex/models` as the source of truth.
