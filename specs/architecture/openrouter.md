# OpenRouter integration

| Field | Value |
|---|---|
| Status | Living |
| Date | 2026-11-01 |

## Routing and ownership

OpenRouter is the host-composed built-in backend `openrouter`. Its configured protocol is `auto`,
`anthropic`, `openai-chat-completions`, or `openai-responses`. Resolution precedes orchestration and
client caching, supplying actual protocol, endpoint, model metadata and catalog revision. Original
model IDs are never rewritten. Collectible plugins cannot replace built-in registrations.

Automatic routing selects only advertised protocols: Anthropic prefers Messages, Chat Completions,
then Responses; OpenAI prefers Responses, Chat Completions, then Messages; other models prefer Chat
Completions, Responses, then Messages. Explicit protocols are binding. Unsupported choices and unknown
models fail without an inference probe or runtime protocol fallback. Responses uses full history and
`store: false`, without inheriting ChatGPT authentication, hosted tools or native compaction.
ModelService filters its published models by an explicitly configured protocol before resolving
their adaptations, so models requiring another protocol do not invalidate the provider catalog.

## Models and refresh

A single built-in `models.json` combines generic model defaults with provider-specific records.
OpenRouter's `/models` listing is normalized into the shared model format under `providers.openrouter`,
with source URL and capture time. The shared resource belongs to Agents and is deserialized once;
Core reads generic defaults and OpenRouter reads its provider records. No separate OpenRouter snapshot
is embedded. The agent catalog contains
text-output models with tools, preserving image input, endpoint protocols, context/output limits,
reasoning metadata and prices. `/models/user` personalizes online refresh for the key. Cache identity
includes instance, endpoint and credential fingerprint. Successful refreshes atomically replace the
catalog. Offline reads use the last successful cache or bundled snapshot with explicit refresh state.
Authentication failures cannot make connection tests succeed. Unsupported personalization reports a
notice rather than claiming that the public listing is personalized.
Desktop clears refresh metadata when a new result omits it, when loading another provider, and on
reset. A successful refresh clears the previous cached-catalog warning.

Fixed API responses and routing preferences use typed DTOs. Preferences are parsed and validated once
per provider instance. JSON mutation remains limited to SDK request extensions and opaque reasoning
payloads, preserving fields that the protocol clients do not own.

The backend composes the existing protocol clients. Request extensions use their native SDK options
and message mapping; response metadata comes from SDK response and stream-event objects. The HTTP
transport only supplies authentication, session headers and remote forwarding, leaving bodies and
response streams untouched. Responses storage and encrypted reasoning settings belong to the shared
Responses client. Anthropic thinking enablement and effort support belong to its thinking client.

OpenRouter records own runtime and UI capabilities, independently of generic `models.json` prefix
matches. Official fields win; verified compatibility policies fill absent fields. Generic Anthropic
matching recognizes dotted/dashed Claude versions, namespaces and context annotations locally while
preserving wire IDs. Opus 5.5 uses 1M context, adaptive thinking, medium default effort and no Off.

## Requests, authentication and presentation

The Opus 5.5 product default is medium when supported, independently of the gateway's own default.
The model record retains `UpstreamDefaultEffort` so this preference does not overwrite the source fact.

Requests use Bearer authentication and stable session affinity. The adapter owns SDK URL differences,
provider preferences, prompt cache shaping and usage. Messages preserves signatures, Chat Completions
preserves `reasoning_details`, and Responses preserves reasoning items and encrypted content. Stream
and non-stream behavior agree. Chat Completions exposes plaintext `reasoning` and text/summary details
as reasoning content, without repeating text already exposed by the protocol client. Encrypted
details remain opaque replay metadata. Configurable provider preferences are `order`, `only`, `ignore`,
`sort`, `allow_fallbacks`, and `require_parameters`; unset fields keep upstream defaults.
The supplier picker reads names and slugs from `/providers`, supports search and multiple selections,
and preserves preference order. Unlimited keys show spending rather than an empty quota bar. Missing
request costs remain absent, and an unlimited key limit is distinct from account credit availability.

API keys and PKCE S256 authorization use shared instance-bound credential storage. Browser callbacks
and manually entered callback URLs use the same verification. Cancellation, expiry and invalid
callbacks do not commit credentials. OAuth-issued API keys are not refresh-token subscriptions.

Actual request costs are optional USD values, never zero placeholders or estimated prices. `/key`
supplies current-key usage and limits, not account balances. Missing values and permissions remain
unavailable without requiring a management key. Catalog prices are estimates only.
Desktop refreshes current-key spending and request costs on turn completion through the shared usage
subscription, including when the usage popover is already open.

Desktop, CLI, subagents, maintenance and ModelService use the same resolved model record. ModelService
permits explicitly composed built-in backends, rejects local plugin backends, retains credentials on
the service host, and validates operations against the selected model. Existing configurations are
not automatically migrated. Cross-model fallback and image generation are out of scope.
