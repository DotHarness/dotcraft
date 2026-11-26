using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;

namespace DotCraft.Harness;

public static class RemoteModelConfiguration
{
    public static void Apply(AppConfig config, ModelServiceCatalog catalog)
    {
        if (config.ModelService is null)
            throw new InvalidOperationException("Configure the model service connection before applying its catalog.");
        config.ModelService.CallerId = catalog.CallerId;
        config.Providers = catalog.Providers.ToDictionary(provider => provider.Id, provider => new AppConfig.ModelProviderConfig
        {
            DisplayName = provider.DisplayName,
            BackendId = provider.BackendId,
            BackendSettings = provider.BackendId == OpenRouterBackend.Id && provider.OpenRouterRouting is { } routing
                ? JsonNode.Parse(routing.GetRawText()) as JsonObject ?? new() : new(),
            Protocol = provider.Protocol,
            EndPoint = provider.BackendId is null ? provider.Endpoint : string.Empty,
            AuthMethod = provider.AuthMethod,
            SupportsImageGeneration = provider.SupportsImageGeneration,
            SupportsFreeformTools = provider.SupportsFreeformTools,
            MaxOutputTokens = provider.MaxOutputTokens,
            NetworkTimeoutSeconds = provider.NetworkTimeoutSeconds,
            RemoteAuthentication = provider.Authentication,
            RemoteWebSearchBackend = provider.WebSearchBackend,
            RemoteModels = provider.Models,
            RemoteCatalogState = provider.CatalogState, RemoteUsage = provider.Usage
        }, StringComparer.OrdinalIgnoreCase);
    }
}
