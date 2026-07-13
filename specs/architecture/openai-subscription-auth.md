# OpenAI Subscription (Sign in with ChatGPT) Auth

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
| Refresh cadence | every 8 hours; on-demand on HTTP 401 |

The authorize URL additionally carries `id_token_add_organizations=true` and
`codex_cli_simplified_flow=true`, both required by the upstream backend.

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
`x-codex-installation-id` HTTP header **and** inside the request body's `client_metadata` map on
every Responses request issued through an OAuth-bound client. The id is not an auth secret and is
not shared with the API-key code path.

If the file is missing or contains an invalid value, DotCraft regenerates a fresh UUID v4 and
overwrites the file. The id is *never* tied to a specific ChatGPT account; switching accounts
preserves the installation id.

## Model API routing

| Auth method | Base URL | Path | Auth header | Extra headers |
|---|---|---|---|---|
| API key (existing) | `https://api.openai.com/v1` | `/responses`, `/chat/completions`, `/models` | `Authorization: Bearer <api-key>` | — |
| ChatGPT OAuth | `https://chatgpt.com/backend-api/codex` | `/responses` | `Authorization: Bearer <access_token>` | `chatgpt-account-id: <account_id>`, `originator: codex_cli_rs`, `x-codex-installation-id: <uuid>`, `session-id: <thread_id>`, `thread-id: <thread_id>`, `session_id: <thread_id>`, `conversation_id: <thread_id>`, `x-codex-window-id: <window_id>`, `x-codex-turn-metadata: <json>`, `x-codex-turn-state: <state>` when established in the same logical turn |
| ChatGPT OAuth | `https://chatgpt.com/backend-api/codex` | `/models` | `Authorization: Bearer <access_token>` | `chatgpt-account-id: <account_id>`, `originator: codex_cli_rs` |

For HTTP Responses, the upstream HTTP client baseline is the hyphenated
`session-id` / `thread-id` pair plus the body-level `prompt_cache_key`. DotCraft populates those
headers per request from the active `TracingChatClient.CurrentSessionKey` (the DotCraft thread id).
It also emits `session_id` and `conversation_id` as compatibility sticky-routing hints for gateways
and clients that key on the snake_case spellings; these are DotCraft compatibility headers, not
treated as required baseline headers.

Each thread/session header gives the ChatGPT backend's prompt-cache shards a finer-grained anchor
than `chatgpt-account-id` alone so that requests on the same thread tend to land on the cache node
that already has the prefix warm. If there is no active thread id, DotCraft sends the request
without these thread-scoped hints and logs the degraded sticky-routing shape once for diagnosis.

`chatgpt-account-id` is resolved per request from the signed-in token/account store via
`IOpenAIAuthService.GetAccountId()`, then falls back to the runtime/config value. If the runtime
value is stale and differs from the signed-in token account, DotCraft logs a warning once and uses
the token/account-store value for request routing.

On the ChatGPT OAuth path, outgoing `/responses` request bodies are additionally augmented with
a `client_metadata` map carrying provider-compatible request metadata. `x-codex-turn-metadata` is the
canonical metadata envelope for the Responses API; flat fields remain as compatibility projections:

```json
{
  "client_metadata": {
    "x-codex-installation-id": "<uuid>",
    "session_id": "<thread_id>",
    "thread_id": "<thread_id>",
    "turn_id": "<turn_id>",
    "x-codex-window-id": "<window_id>",
    "x-codex-turn-metadata": "{\"installation_id\":\"<uuid>\",\"session_id\":\"<thread_id>\",\"thread_id\":\"<thread_id>\",\"turn_id\":\"<turn_id>\",\"window_id\":\"<window_id>\",\"request_kind\":\"turn\",\"turn_started_at_unix_ms\":1778544000000}"
  }
}
```

The augmentation is performed by `OpenAIResponsesClientMetadataPipelinePolicy` and only fires for
URIs whose path ends in `/responses`. Other caller-provided `client_metadata` entries are
preserved, but provider-reserved keys are authoritative runtime state. Caller-provided values for
`x-codex-installation-id`, `session_id`, `thread_id`, `turn_id`, `x-codex-window-id`, and
`x-codex-turn-metadata` are overwritten when they differ from DotCraft's active runtime context so
the header and body do not split sticky-routing identity.

The OAuth `/responses` transport omits the top-level `max_output_tokens` request field even when a
caller sets `ChatOptions.MaxOutputTokens`, because this backend path rejects that parameter. The
value remains available to DotCraft runtime code as a local budget, and compaction summaries still
enforce `SummaryMaxOutputTokens` after the response is received. API-key Responses clients are not
affected by this OAuth-only compatibility rule.

`x-codex-turn-state` is request-scoped provider state. DotCraft never fabricates it. The OAuth
pipeline captures the first non-empty `x-codex-turn-state` response header observed during a
logical Session Core turn and replays that value on subsequent `/responses` requests in the same
turn, including the one-shot 401 retry path. The stored value is discarded when the logical turn's
runtime scope ends and is not persisted to thread history, trace events, or the next user turn.

### Experimental ChatGPT OAuth compatibility switches

DotCraft defaults to its normal `DotCraft/<version>` User-Agent and does not send `OpenAI-Beta`.
Two opt-in environment variables allow controlled A/B testing against the ChatGPT OAuth path:

| Variable | Effect |
|---|---|
| `DOTCRAFT_CHATGPT_OAUTH_UA_PROFILE=codex` | Use the alternate User-Agent profile (`codex_cli_rs/<version> (<os>; <arch>) dotcraft`) on OAuth-bound SDK requests |
| `DOTCRAFT_CHATGPT_OAUTH_OPENAI_BETA=<value>` | Send `OpenAI-Beta: <value>` on OAuth `/responses` requests |

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
falls back to the bundled model catalog (`src/DotCraft.Core/Resources/chatgpt-codex-models.json`)
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

## Core integration points

| Component | File | Responsibility |
|---|---|---|
| Auth manager | `src/DotCraft.Core/Auth/OpenAI/OpenAIAuthManager.cs` | Login, refresh, logout, status; thread-safe; raises `LoggedIn` / `LoggedOut` events |
| Token store | `src/DotCraft.Core/Auth/OpenAI/OpenAITokenStore.cs` | Reads/writes `auth.json` with locked-down permissions |
| Installation id provider | `src/DotCraft.Core/Auth/OpenAI/OpenAIInstallationIdProvider.cs` | Resolves and persists the `~/.craft/installation_id` UUID v4 |
| Auth pipeline policy | `src/DotCraft.Core/Agents/OpenAIOAuthPipelinePolicy.cs` | Sets OAuth auth headers, resolves account id from auth service before config, adds Responses sticky headers and provider turn/window headers, captures and replays same-turn `x-codex-turn-state`, applies opt-in experimental headers, refreshes on HTTP 401 |
| Responses metadata policy | `src/DotCraft.Core/Agents/OpenAIResponsesClientMetadataPipelinePolicy.cs` | Adds/normalizes provider-compatible `client_metadata` into outgoing `/responses` request bodies on OAuth clients |
| Provider resolver | `src/DotCraft.Core/Configuration/ModelProviderRuntime.cs` | Forces `chatgpt.com/backend-api/codex` endpoint + `openai-responses` protocol in OAuth mode |
| Binding helper | `src/DotCraft.Core/Auth/OpenAI/OpenAIAuthBindingPersistence.cs` | Shared CLI/AppServer helper that writes `AuthMethod` / `ChatGptAccountId` into the global config |
| Usage client | `src/DotCraft.Core/Auth/OpenAI/OpenAIUsageClient.cs` | One-shot `GET wham/usage`; reuses the same headers; 401 → force-refresh + retry once |
| Usage poller | `src/DotCraft.Core/Auth/OpenAI/OpenAIUsagePoller.cs` | Singleton; 5-min cadence, 30 s manual debounce, exponential backoff to 1 h on failures; broadcasts `SnapshotChanged` |

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
| 401 from `chatgpt.com/backend-api/codex/responses` | Token expired | Pipeline policy calls `ForceRefresh`, retries once |
| `refresh_token_expired` / `_reused` / `_invalidated` from token endpoint | Refresh token permanently invalid | `OpenAIAuthException` with explicit reason; user must re-login |
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
