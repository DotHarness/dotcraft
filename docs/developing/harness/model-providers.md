# Configure model providers

DotCraft Harness includes OpenAI and Anthropic provider integrations. The host selects a provider and model through `AppConfig`, then passes the effective configuration to `AddDotCraftHarness`.

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

Use `ModelProviderProtocols.OpenAIChatCompletions` for endpoints that implement the Chat Completions protocol. Use `ModelProviderProtocols.OpenAIResponses` for the Responses protocol.

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

The endpoint must implement the protocol selected by `Protocol`. A custom provider ID names a configuration record. It does not define a new wire protocol.

## Select a model per Thread

New Threads capture the effective workspace provider and model. Override them for one Thread with `ThreadConfiguration`:

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

Changes to the host configuration do not silently rewrite the model snapshot of existing Threads.

> [!TIP]
> Keep API keys outside workspace configuration. Resolve secrets in the host and construct the effective `AppConfig` immediately before composition.

## Provider registration

`AddDotCraftHarness` registers both built-in Provider implementations idempotently.

A protocol can have only one registered `IModelProvider`. Registering multiple implementations for the same protocol is rejected during Runtime composition.

## Related docs

- [Harness overview](./)
- [Configuration and paths](./configuration-paths)
- [Threads and Turns](./threads-turns)
