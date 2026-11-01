# OpenRouter integration

| Field | Value |
|---|---|
| Status | Living |
| Date | 2026-11-02 |

## Provider and protocol routing

`BackendId: openrouter` identifies the provider. `Protocol` accepts `auto`, `anthropic`,
`openai-chat-completions`, or `openai-responses`. Model IDs remain unchanged in configuration,
requests and history.

Automatic routing selects the first protocol advertised by the model in this order:

| Model family | Protocol order |
|---|---|
| Anthropic | Messages → Chat Completions → Responses |
| OpenAI | Responses → Chat Completions → Messages |
| Other | Chat Completions → Responses → Messages |

An explicit protocol overrides automatic selection. Model resolution supplies the protocol,
endpoint, capabilities and catalog revision before a request starts. Desktop, CLI, subagents,
maintenance tasks and ModelService use the same resolved description.

## Catalog lifecycle

The shared built-in catalog includes text-output models with tool support, retaining image inputs,
protocols, context and output limits, reasoning options and prices. Model records govern routing
and capabilities. Official metadata takes precedence over local compatibility rules.

Online refresh reads `/models` and applies the key's available models through `/models/user`.
Successful refreshes replace the catalog. The last successful cache or built-in catalog supplies
models while refresh status reports the latest outcome. Catalog caches are scoped by provider
instance, endpoint and credential identity.

ModelService publishes models supported by the configured protocol. Its credentials remain on
the service host, and upstream sign-in uses the service's CLI management entry point.

## Requests and authentication

The integration reuses the existing protocol clients for inference, tools and history. Requests use
Bearer authentication and a stable session identifier. Responses sends complete history with
`store: false`.

Messages retains thinking signatures, Chat Completions retains `reasoning_details`, and Responses
retains reasoning items and encrypted content across tool calls. Plaintext reasoning is exposed
once as reasoning content in streamed and complete responses. Encrypted data remains opaque.

API keys and PKCE S256 browser login use provider-instance credentials. Browser callbacks and pasted
callback URLs complete the same authorization flow. Credentials are saved after successful authorization.

## Routing preferences and usage

Upstream supplier preferences are `order`, `only`, `ignore`, `sort`, `allow_fallbacks`, and
`require_parameters`. Supplier names and slugs come from `/providers`. Preferred suppliers retain
their configured priority. Unset preferences use OpenRouter defaults.

`/key` supplies current-key spending and remaining limits. Request costs use the optional amount
returned by the API. Catalog prices are estimates. Unlimited keys display spending, and unavailable
amounts stay unspecified. Desktop refreshes usage after a turn completes.
