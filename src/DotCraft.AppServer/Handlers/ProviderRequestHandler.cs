using DotCraft.Agents;
using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Handles provider and model catalog wire methods.
/// </summary>
internal sealed class ProviderRequestHandler(
    WorkspaceConfigEditor workspaceConfig,
    string? workspaceCraftPath,
    IAppConfigMonitor? appConfigMonitor,
    ModelProviderRegistry? modelProviderRegistry,
    bool supportsUltraReasoning,
    IModelRuntimeResolver? runtimeResolver = null,
    ProviderAuthenticationCoordinator? authentication = null) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.ProviderList, HandleProviderListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ProviderTest, HandleProviderTestAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ModelList, HandleModelListAsync);
    }

    private async Task<object?> HandleProviderListAsync(AppServerTypedRequest<Contract.ProviderListParams> request, CancellationToken ct)
    {
        _ = request;
        _ = ct;
        EnsureProviderManagementAvailable();

        var config = workspaceConfig.LoadCurrentMergedConfig();
        return new Contract.ProviderListResult
        {
            ManagedBy = config.ModelService is null ? default : new Protocol.Optional<string>("modelService"),
            Providers = new Protocol.Optional<IReadOnlyList<Contract.ProviderInfo>>(
                await ProviderContractMapper.BuildProviderInfosAsync(config, runtimeResolver, authentication, ct).ConfigureAwait(false))
        };
    }

    private async Task<object?> HandleModelListAsync(AppServerTypedRequest<Contract.ModelListParams> request, CancellationToken ct)
    {
        var p = request.Params;

        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.ModelList);

        var config = appConfigMonitor?.Current
            ?? AppConfig.LoadWithGlobalFallback(Path.Combine(workspaceCraftPath, "config.json"), workspaceConfig.EffectiveGlobalConfigPath);
        var result = await ModelProviderCatalog.FetchAsync(
            config,
            RequireModelResolver(),
            NormalizeOptionalString(ValueOrDefault(p.ProviderId)),
            ModelCatalogRefreshStrategy.OnlineIfUncached,
            ct);

        return new Contract.ModelListResult
        {
            Success = result.Success,
            ProviderId = OmitIfNull(result.ProviderId),
            Protocol = result.Protocol,
            Models = new Protocol.Optional<IReadOnlyList<Contract.ModelCatalogItem>>(
                ProviderContractMapper.BuildModelCatalogItems(
                config,
                result.ProviderId,
                result.Protocol,
                result.EndPoint,
                result.Models,
                supportsUltraReasoning)),
            ErrorCode = result.Success ? default : OmitIfNull(result.ErrorCode.ToString()),
            ErrorMessage = result.Success
                ? default
                : OmitIfNull(ProviderContractMapper.FormatModelListErrorMessage(result.ErrorMessage, result.EndPoint))
        };
    }

    private async Task<object?> HandleProviderTestAsync(AppServerTypedRequest<Contract.ProviderTestParams> request, CancellationToken ct)
    {
        EnsureProviderManagementAvailable();
        var p = request.Params;
        var config = workspaceConfig.LoadCurrentMergedConfig();
        var providerId = NormalizeOptionalString(ValueOrDefault(p.ProviderId));

        ModelCatalogResult result;
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            result = await ModelProviderCatalog.FetchAsync(
                config,
                RequireModelResolver(),
                providerId,
                ModelCatalogRefreshStrategy.Online,
                ct);
        }
        else
        {
            EnsureLocallyManaged();
            var protocol = NormalizeProviderProtocol(ValueOrDefault(p.Protocol));
            ValidateProviderPayload(
                protocol,
                ValueOrDefault(p.EndPoint),
                ValueOrDefault(p.NetworkTimeoutSeconds),
                ValueOrDefault(p.MaxOutputTokens),
                ValueOrDefault(p.StreamMaxRetries),
                ValueOrDefault(p.StreamIdleTimeoutMs));
            var draftProviderId = "__provider_test__";
            var draftConfig = new AppConfig
            {
                NetworkTimeoutSeconds = config.NetworkTimeoutSeconds,
                Providers = new Dictionary<string, AppConfig.ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [draftProviderId] = new()
                    {
                        DisplayName = "Provider Test",
                        Protocol = protocol,
                        ApiKey = NormalizeOptionalString(ValueOrDefault(p.ApiKey)) ?? string.Empty,
                        EndPoint = NormalizeOptionalString(ValueOrDefault(p.EndPoint)) ?? string.Empty,
                        NetworkTimeoutSeconds = ValueOrDefault(p.NetworkTimeoutSeconds),
                        MaxOutputTokens = ValueOrDefault(p.MaxOutputTokens),
                        StreamMaxRetries = ValueOrDefault(p.StreamMaxRetries),
                        StreamIdleTimeoutMs = ValueOrDefault(p.StreamIdleTimeoutMs)
                    }
                }
            };
            result = await ModelProviderCatalog.FetchAsync(
                draftConfig,
                RequireModelResolver(),
                draftProviderId,
                ModelCatalogRefreshStrategy.Online,
                ct);
            result.ProviderId = null;
        }

        return new Contract.ProviderTestResult
        {
            Success = result.Success,
            ProviderId = OmitIfNull(result.ProviderId),
            Protocol = result.Protocol ?? NormalizeProviderProtocol(ValueOrDefault(p.Protocol)),
            Models = new Protocol.Optional<IReadOnlyList<Contract.ModelCatalogItem>>(
                ProviderContractMapper.BuildModelCatalogItems(
                config,
                result.ProviderId,
                result.Protocol ?? NormalizeProviderProtocol(ValueOrDefault(p.Protocol)),
                result.EndPoint,
                result.Models,
                supportsUltraReasoning)),
            ErrorCode = result.Success ? default : OmitIfNull(result.ErrorCode.ToString()),
            ErrorMessage = result.Success
                ? default
                : OmitIfNull(ProviderContractMapper.FormatModelListErrorMessage(result.ErrorMessage, result.EndPoint))
        };
    }

    private void EnsureProviderManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("provider/*");
    }

    private void EnsureLocallyManaged()
    {
        if (appConfigMonitor?.Current.ModelService is not null)
            throw AppServerErrors.InvalidRequest("Providers are managed by the model service. Use its configuration or CLI.");
    }

    private ModelProviderRegistry RequireProviderRegistry() =>
        modelProviderRegistry
        ?? throw AppServerErrors.InvalidRequest("Model providers are not available in this server build.");

    private IModelRuntimeResolver RequireModelResolver() => runtimeResolver ?? RequireProviderRegistry();

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static void ValidateProviderPayload(
        string protocol,
        string? endPoint,
        int? networkTimeoutSeconds,
        int? maxOutputTokens,
        int? streamMaxRetries,
        int? streamIdleTimeoutMs)
    {
        _ = NormalizeProviderProtocol(protocol);
        if (networkTimeoutSeconds.HasValue && networkTimeoutSeconds.Value < 1)
            throw AppServerErrors.InvalidParams("'networkTimeoutSeconds' must be greater than zero.");
        if (maxOutputTokens.HasValue && maxOutputTokens.Value < 1)
            throw AppServerErrors.InvalidParams("'maxOutputTokens' must be greater than zero.");
        if (streamMaxRetries.HasValue
            && (streamMaxRetries.Value < 0 || streamMaxRetries.Value > ModelProviderDefaults.MaxStreamMaxRetries))
            throw AppServerErrors.InvalidParams("'streamMaxRetries' must be between 0 and 100.");
        if (streamIdleTimeoutMs.HasValue && streamIdleTimeoutMs.Value < 1)
            throw AppServerErrors.InvalidParams("'streamIdleTimeoutMs' must be greater than zero.");
        if (!string.IsNullOrWhiteSpace(endPoint) && !Uri.TryCreate(endPoint, UriKind.Absolute, out _))
            throw AppServerErrors.InvalidParams("'endPoint' must be an absolute URI.");
    }

    private static string NormalizeProviderProtocol(string? protocol)
    {
        try
        {
            return ModelProviderProtocols.Normalize(protocol);
        }
        catch (ArgumentException)
        {
            throw AppServerErrors.InvalidParams($"Unsupported model provider protocol '{protocol}'.");
        }
    }

    private static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);
}
