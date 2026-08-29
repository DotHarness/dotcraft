# Configure model providers

DotCraft Harness ships two built-in provider integrations, OpenAI and Anthropic. The host selects a provider and model through `AppConfig`, then passes the effective configuration to [`AddDotCraftHarness`](./).

## Configure OpenAI

Define a provider record and a model preference under the same stable provider ID:

```csharp
using DotCraft.Configuration;

const string providerId = "openai";

var appConfig = new AppConfig
{
    ProviderId = providerId,
    ProviderPreferences =
    {
        [providerId] = new ModelPreference
        {
            Model = "gpt-5.1"
        }
    },
    Providers =
    {
        [providerId] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "OpenAI",
            Protocol = ModelProviderProtocols.OpenAIResponses,
            ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException("Set OPENAI_API_KEY.")
        }
    }
};
```

Use `ModelProviderProtocols.OpenAIChatCompletions` for endpoints that implement the Chat Completions protocol, and `ModelProviderProtocols.OpenAIResponses` for the Responses protocol.

## Authenticate with a ChatGPT subscription

OpenAI providers accept a second authentication method. `AuthMethod = "chatgptOAuth"` replaces the static API key with the OpenAI Sign in with ChatGPT flow, authenticating requests with a ChatGPT subscription account:

```csharp
appConfig.Providers["openai"] = new AppConfig.ModelProviderConfig
{
    DisplayName = "OpenAI (ChatGPT)",
    Protocol = ModelProviderProtocols.OpenAIResponses,
    AuthMethod = ModelProviderAuthMethods.ChatGptOAuth
};
```

In this mode the provider resolves differently:

- The protocol must be an OpenAI protocol. `anthropic` combined with `chatgptOAuth` throws a `ModelProviderConfigurationException` when the provider is resolved, and the effective protocol is always `openai-responses`.
- `ApiKey` and `EndPoint` are ignored. Requests go to the ChatGPT backend at `https://chatgpt.com/backend-api/codex`.
- Every request attaches a fresh access token from the token bundle stored as `auth.json` in the user data directory. Tokens refresh automatically, and a `401` first adopts credentials rotated by another process before refreshing at the issuer.

Sign-in happens once, outside provider configuration. `IOpenAIAuthService`, registered together with the OpenAI provider, runs the PKCE browser login through `LoginAsync` and persists the tokens. On a machine with the DotCraft app, `dotcraft auth openai login` does the same and also writes the provider entry into the global configuration. `ChatGptAccountId` and `ChatGptPlanType` are written by that flow — treat them as read-only metadata. Signing out with `LogoutAsync` or `dotcraft auth openai logout` deletes the tokens and reverts the provider to `apiKey`.

## Configure Anthropic

Anthropic uses the same provider registry and preference structure:

```csharp
const string providerId = "anthropic";

var appConfig = new AppConfig
{
    ProviderId = providerId,
    ProviderPreferences =
    {
        [providerId] = new ModelPreference
        {
            Model = "claude-sonnet-4-5"
        }
    },
    Providers =
    {
        [providerId] = new AppConfig.ModelProviderConfig
        {
            DisplayName = "Anthropic",
            Protocol = ModelProviderProtocols.Anthropic,
            ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                ?? throw new InvalidOperationException("Set ANTHROPIC_API_KEY.")
        }
    }
};
```

An empty `EndPoint` selects the built-in default for the chosen protocol.

## Use a compatible endpoint

Set `EndPoint` when routing an OpenAI-compatible or Anthropic-compatible provider through another service:

```csharp
appConfig.Providers["company-models"] = new AppConfig.ModelProviderConfig
{
    DisplayName = "Company models",
    Protocol = ModelProviderProtocols.OpenAIChatCompletions,
    ApiKey = secretStore.Get("company-models"),
    EndPoint = "https://models.example.com/v1"
};

appConfig.ProviderId = "company-models";
appConfig.ProviderPreferences["company-models"] = new ModelPreference
{
    Model = "engineering-agent"
};
```

The endpoint must implement the protocol selected by `Protocol`. A custom provider ID names a configuration record. It does not define a new wire protocol — the three above are the only choices.

## Select a model per Thread

A new Thread captures the effective workspace provider and model at creation time. Override them for that one Thread with `ThreadConfiguration`:

```csharp
var thread = await sessions.CreateThreadAsync(
    identity,
    new ThreadConfiguration
    {
        ProviderId = "anthropic",
        Model = "claude-sonnet-4-5"
    },
    ct: cancellationToken);
```

Later changes to the host configuration do not rewrite the model snapshot of existing Threads.

> [!TIP]
> Keep API keys outside workspace configuration. Resolve secrets in the host and construct the effective `AppConfig` immediately before composition.

## Provider registration

`AddDotCraftHarness` registers both built-in provider implementations idempotently, so repeated registration produces no duplicates.

A protocol can have only one `IModelProvider`. Multiple implementations for the same protocol throw during Runtime composition and the Host fails to start.

## Related docs

- [Configuration and paths](./configuration-paths) — how the application prepares the effective `AppConfig` before registration.
- [Threads and Turns](./threads-turns) — where a Thread captures its provider and model snapshot.
