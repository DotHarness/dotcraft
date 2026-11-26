using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

internal sealed class RemoteHttpModelProvider(ModelProviderRegistry native) : IModelProvider, IModelCatalogProvider
{
    private static readonly IReadOnlyCollection<string> SupportedProtocols = Array.AsReadOnly([
        ModelProviderProtocols.Anthropic,
        ModelProviderProtocols.OpenAIResponses,
        ModelProviderProtocols.OpenAIChatCompletions
    ]);

    public IReadOnlyCollection<string> Protocols => SupportedProtocols;

    public static EffectiveModelRuntime Normalize(EffectiveModelRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(runtime.Model))
            return runtime;

        var adaptation = runtime.RemoteModels?.GetValueOrDefault(runtime.Model);
        if (adaptation?.Protocol is not { } protocol || !SupportedProtocols.Contains(protocol))
            throw new ArgumentException($"Model '{runtime.Model}' has no native route in the model service catalog. Refresh the catalog before selecting it.");

        var maxOutputTokens = (runtime.ConfiguredMaxOutputTokens, adaptation.MaxOutputTokens ?? adaptation.Model?.MaxOutputTokens) switch
        {
            (int configured, int limit) when configured > 0 && limit > 0 => Math.Min(configured, limit),
            (int configured, _) when configured > 0 => configured,
            (_, int limit) when limit > 0 => limit,
            _ => (int?)null
        };

        return runtime with
        {
            Protocol = protocol,
            EndPoint = protocol == ModelProviderProtocols.Anthropic
                ? ModelProviderResolver.RemoteBackendEndpoint : ModelProviderResolver.RemoteBackendEndpoint + "/v1",
            ApiKey = string.Empty,
            IsBuiltInBackend = true,
            RequestAdaptation = adaptation,
            ModelMetadata = adaptation.Model,
            MaxOutputTokens = maxOutputTokens,
            Capabilities = adaptation.Capabilities
                ?? ModelProviderCapabilities.ForProtocol(protocol) with { NativeDeferredToolLoading = false },
            SupportsImageGeneration = false,
            SupportsFreeformTools = false
        };
    }

    public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
    {
        var resolved = Normalize(runtime);
        return native.Resolve(resolved.Protocol).CreateChatClient(resolved with
        {
            BackendId = null,
            AuthMethod = ModelProviderAuthMethods.ApiKey
        });
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
            return null;
        if (serviceType == typeof(IProviderHistorySessionFactory)
            && native.TryResolve(ModelProviderProtocols.OpenAIResponses, out var provider))
            return provider?.GetService(serviceType);
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadCatalog(runtime));
    }

    internal static ModelCatalogResult ReadCatalog(EffectiveModelRuntime runtime)
    {
        var state = runtime.RemoteCatalogState;
        return new ModelCatalogResult
        {
            Success = state?.Success ?? true,
            Models = runtime.RemoteModels?.Select(pair => pair.Value.Model ?? new ModelCatalogEntry
            {
                Id = pair.Key,
                DefaultProtocol = pair.Value.Protocol
            }).ToList() ?? [],
            ErrorCode = state?.ErrorCode ?? ModelCatalogErrorCode.None,
            ErrorMessage = state?.ErrorMessage,
            ProviderId = runtime.ProviderId,
            Protocol = runtime.Protocol,
            EndPoint = runtime.EndPoint,
            Source = state?.Source ?? "modelService",
            RefreshedAt = state?.RefreshedAt,
            RefreshNotice = state?.RefreshNotice
        };
    }
}
