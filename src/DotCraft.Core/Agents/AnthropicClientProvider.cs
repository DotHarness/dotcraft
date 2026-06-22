using System.Collections.Concurrent;
using Anthropic;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Creates and caches Anthropic SDK clients for DotCraft runtime paths.
/// </summary>
public sealed class AnthropicClientProvider
{
    /// <summary>
    /// DotCraft's default Anthropic maximum output token budget when provider config does not override it.
    /// </summary>
    public const int DefaultMaxOutputTokens = 64_000;

    private readonly ConcurrentDictionary<AnthropicClientKey, AnthropicClient> _anthropicClients = new();

    /// <summary>
    /// Gets a cached Anthropic client for a resolved Anthropic runtime.
    /// </summary>
    public AnthropicClient GetAnthropicClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!string.Equals(runtime.Protocol, ModelProviderProtocols.Anthropic, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Provider '{runtime.ProviderId}' does not use the Anthropic protocol.", nameof(runtime));

        return GetAnthropicClient(AnthropicClientKey.From(runtime));
    }

    /// <summary>
    /// Gets a provider-neutral chat client for a resolved Anthropic runtime.
    /// </summary>
    public IChatClient GetChatClient(EffectiveModelRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return GetAnthropicClient(runtime).Beta.AsIChatClient(
            NormalizeRequiredModel(runtime.Model),
            defaultMaxOutputTokens: NormalizeMaxOutputTokens(runtime.MaxOutputTokens));
    }

    private AnthropicClient GetAnthropicClient(AnthropicClientKey key) =>
        _anthropicClients.GetOrAdd(key, static clientKey =>
            new AnthropicClient
            {
                ApiKey = clientKey.ApiKey,
                BaseUrl = clientKey.Endpoint.ToString().TrimEnd('/'),
                MaxRetries = 0,
                Timeout = TimeSpan.FromSeconds(clientKey.NetworkTimeoutSeconds)
            });

    private static string NormalizeRequiredModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model must be configured.", nameof(model));

        return model.Trim();
    }

    private readonly record struct AnthropicClientKey(Uri Endpoint, string ApiKey, int NetworkTimeoutSeconds)
    {
        public static AnthropicClientKey From(EffectiveModelRuntime runtime)
        {
            if (string.IsNullOrWhiteSpace(runtime.ApiKey))
                throw new ArgumentException("API key must be configured.", nameof(runtime));

            if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var endpoint))
                throw new ArgumentException("Endpoint must be an absolute URI.", nameof(runtime));

            return new AnthropicClientKey(endpoint, runtime.ApiKey, NormalizeNetworkTimeoutSeconds(runtime.NetworkTimeoutSeconds));
        }
    }

    private static int NormalizeNetworkTimeoutSeconds(int seconds) => Math.Max(1, seconds);

    private static int NormalizeMaxOutputTokens(int? value) =>
        value is > 0 ? value.Value : DefaultMaxOutputTokens;
}
