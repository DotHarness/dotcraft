# Models and providers

Model providers, per-provider model preferences, reasoning and prompt caching, and the model capability catalog.

## Providers and model selection

| Field | Description | Default |
|-------|-------------|---------|
| `Providers` | Personal model provider dictionary, usually stored in `~/.craft/config.json` | Empty |
| `ProviderId` | Current personal provider id. Empty means no provider is selected | Empty |
| `ProviderPreferences` | Complete MainAgent preferences keyed by provider id. The selected provider must have an effective entry | `{}` |
| `NetworkTimeoutSeconds` | Global model request timeout in seconds; providers can override it | `600` |

Personal provider example:

```json
{
  "Providers": {
    "anthropic": {
      "DisplayName": "Anthropic",
      "Protocol": "anthropic",
      "ApiKey": "${ANTHROPIC_API_KEY}"
    },
    "openrouter": {
      "DisplayName": "OpenRouter",
      "Protocol": "openai-chat-completions",
      "ApiKey": "${OPENROUTER_API_KEY}",
      "EndPoint": "https://openrouter.ai/api/v1"
    }
  }
}
```

Workspace model selection example:

```json
{
  "ProviderId": "anthropic",
  "ProviderPreferences": {
    "anthropic": {
      "Model": "claude-sonnet-4-5",
      "Reasoning": {
        "Enabled": true,
        "Effort": "High",
        "Output": "Full"
      },
      "Speed": "Fast"
    }
  }
}
```

`ProviderPreferences` merges per provider id, never field by field. When global and workspace config both define the same provider id, the workspace record replaces the global one in full.

| Preference field | Values | Description |
|------------------|--------|-------------|
| **`Model`** | Non-empty model id | Model used for new MainAgent threads |
| **`Reasoning.Enabled`** | `true`, `false` | Enables reasoning when the model supports the choice |
| **`Reasoning.Effort`** | `Low`, `Medium`, `High`, `ExtraHigh`, `Max`, `Ultra` | Requested reasoning effort |
| **`Reasoning.Output`** | `None`, `Summary`, `Full` | Requested reasoning output |
| **`Speed`** | `Standard`, `Fast` | Requested inference speed; unsupported Fast runs as Standard |

Max requests native maximum reasoning effort. Ultra uses the same Max effort and adds proactive Dynamic Workflow orchestration.

Provider object fields:

| Field | Description | Default |
|-------|-------------|---------|
| `DisplayName` | User-facing provider name; falls back to the provider id when empty | Empty |
| `Protocol` | Provider protocol: `anthropic`, `openai-chat-completions`, or `openai-responses`. Empty values default to `openai-chat-completions`. | `openai-chat-completions` |
| `ApiKey` | Provider API key; prefer `${ENV_NAME}` environment variable references | Empty |
| `AuthMethod` | Authentication method: `apiKey` uses the static `ApiKey`; `chatgptOAuth` authenticates with a ChatGPT subscription account (OpenAI protocols only, see below). Unrecognized values fall back to `apiKey` | `apiKey` |
| `EndPoint` | Provider base URL; empty values use the protocol default endpoint | OpenAI protocols: `https://api.openai.com/v1`; `anthropic`: `https://api.anthropic.com` |
| `NetworkTimeoutSeconds` | Per-provider request timeout, overriding the global `NetworkTimeoutSeconds` | Empty |
| `MaxOutputTokens` | Per-provider default maximum output tokens, applied when a request does not set its own | Empty |
| `StreamMaxRetries` | Per-provider streaming reconnection attempts for dropped or idle provider streams; `0` disables stream retry | `5` |
| `StreamIdleTimeoutMs` | Per-provider idle timeout for streaming responses, in milliseconds | `300000` |
| `SupportsImageGeneration` | Whether this provider serves the OpenAI Images API. When omitted, ChatGPT OAuth and API-key providers on the official OpenAI endpoint default to `true`; other endpoints default to `false`. | Provider default |
| `SupportsFreeformTools` | Whether this provider accepts grammar-constrained (`custom`) tools on the Responses protocol. When `false`, tools such as code mode's `CodeMode` are sent as ordinary function tools. When omitted, `chatgptOAuth` providers and API-key providers on the official OpenAI endpoint default to `true`; other endpoints default to `false`. | Provider default |

## Sign in with ChatGPT

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

A `chatgptOAuth` provider authenticates with a ChatGPT subscription instead of an API key. Save the provider entry above, then run `dotcraft auth login openai` to sign in — it opens the browser and stores the OAuth token bundle as `auth.json` in the user data directory. Signing in does not change configuration, and one sign-in serves every `chatgptOAuth` provider on the machine. In this mode `ApiKey` and `EndPoint` are ignored and the effective protocol is always `openai-responses`; the resolution rules live in [Configure model providers](../extend/harness/model-providers). `dotcraft auth status openai` shows the signed-in account, and `dotcraft auth logout openai` deletes the tokens, signing out every `chatgptOAuth` provider.

## Reasoning and prompt caching

| Field | Description | Default |
|-------|-------------|---------|
| `Reasoning.Enabled` | Requests provider reasoning support | `false` |
| `Reasoning.Effort` | Reasoning depth: `None` / `Low` / `Medium` / `High` / `ExtraHigh` / `Max` / `Ultra` | `Medium` |
| `Reasoning.Output` | Reasoning visibility: `None` / `Summary` / `Full` | `Full` |
| `PromptCaching.Enabled` | Inject prompt cache markers for matching models | `true` |
| `PromptCaching.ModelPatterns` | Case-insensitive model name fragments. Empty matches no models | `["claude"]` |
| `PromptCaching.Placement` | Marker placement strategy. Currently only `ConversationTail` is supported | `ConversationTail` |
| `PromptCaching.Ttl` | Anthropic cache TTL. Empty uses the default 5 minutes; `1h` requests the long cache | Empty |
| `PromptCaching.Warming` | Refresh the Anthropic prompt cache while a turn waits on long tool runs or approvals, so the next request reuses the cache instead of writing it again | `true` |

Deep-thinking adapter catalog files:

- Global: `~/.craft/model-thinking-adapters.json`
- Workspace: `.craft/model-thinking-adapters.json`

The built-in catalog exposes full reasoning choices for unlisted Anthropic-protocol models, but does not assume they support Anthropic `adaptive` request shaping. Add `anthropicThinking` entries for models or endpoints that explicitly support that shape.

For Anthropic-compatible providers, `anthropicMessageContent` can declare how DotCraft reasoning history should be represented. The built-in DeepSeek Anthropic adapter maps historical `TextReasoningContent` to Anthropic-compatible `thinking` blocks before sending history; it is not a generic unsupported-block filter.

```json
{
  "deepThinking": {
    "models": ["deepseek", "mimo", "my-thinking-model-"],
    "endpoints": ["deepseek", "my-thinking-gateway"]
  },
  "anthropicThinking": {
    "adapters": [
      {
        "models": ["my-adaptive-anthropic-model-"],
        "thinking": { "type": "adaptive", "display": "fromReasoningOutput" },
        "outputConfig": { "effort": "fromReasoningEffort" }
      }
    ]
  },
  "anthropicMessageContent": {
    "adapters": [
      {
        "models": ["deepseek"],
        "endpoints": ["deepseek"],
        "reasoningHistory": { "blockType": "thinking" }
      }
    ]
  }
}
```

## Model capability catalog

DotCraft ships a built-in catalog for model context windows and Fast Mode support. Extend or override it with:

- Global: `~/.craft/models.json`
- Workspace: `.craft/models.json`

```json
{
  "defaultContextWindow": 256000,
  "models": {
    "my-256k-model": {
      "contextWindow": 256000
    },
    "my-reserved-output-model": {
      "contextWindow": 400000,
      "maxInputTokens": 272000
    },
    "custom-fast-model": {
      "contextWindow": 1048576,
      "fast": {
        "protocols": ["openai-responses"]
      }
    },
    "custom-anthropic-model": {
      "fast": {
        "protocols": ["anthropic"]
      }
    }
  }
}
```

Workspace entries override global entries, which override the built-in catalog. Fields merge
independently for each model pattern. Set `fast` to `null` to disable an inherited Fast capability.
Model patterns use case-insensitive longest-prefix matching and also match namespaced suffixes such
as `provider/custom-fast-model`.

Built-in context-window values are synchronized from the provider-agnostic
[models.dev](https://models.dev/) catalog for models that support tool calls, include text output,
and declare a context window of at least 1,000 tokens. The final segment of the canonical model id is
used as its lowercase key. If a provider serves a different limit, set an override in the global or
workspace catalog. More-specific keys win over family prefixes, so a concrete model can safely carry
a different window from its family.

Model context capacity comes directly from the merged catalog. Unknown models use
`defaultContextWindow`, falling back to 256,000 tokens. Summary reserves and safety buffers
are applied by compaction after the optional client budget.

Some models accept less input than their context window because part of the window is reserved
for output. `maxInputTokens` declares that input limit, and automatic compaction triggers below it
instead of below the full window. Built-in values come from the model's own provider on models.dev.
