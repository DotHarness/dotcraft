using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Handles provider, model catalog, and ChatGPT subscription auth wire methods.
/// </summary>
internal sealed class ProviderRequestHandler(
    IAppServerTransport transport,
    WorkspaceConfigEditor workspaceConfig,
    ConfigurationService? configuration,
    string? workspaceCraftPath,
    IAppConfigMonitor? appConfigMonitor,
    ModelProviderRegistry? modelProviderRegistry,
    bool supportsUltraReasoning) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.ProviderList, HandleProviderListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ProviderTest, HandleProviderTestAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ModelList, HandleModelListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AuthOpenAiStatus, HandleAuthOpenAiStatusAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AuthOpenAiLogin, HandleAuthOpenAiLoginAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AuthOpenAiLogout, HandleAuthOpenAiLogoutAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AuthOpenAiUsage, HandleAuthOpenAiUsageAsync);
    }

    private Task<object?> HandleProviderListAsync(AppServerTypedRequest<Contract.ProviderListParams> request, CancellationToken ct)
    {
        _ = request;
        _ = ct;
        EnsureProviderManagementAvailable();

        var config = workspaceConfig.LoadCurrentMergedConfig();
        return Task.FromResult<object?>(new Contract.ProviderListResult
        {
            ManagedBy = config.ModelService is null ? default : new Protocol.Optional<string>("modelService"),
            Providers = new Protocol.Optional<IReadOnlyList<Contract.ProviderInfo>>(
                ProviderContractMapper.BuildProviderInfos(config))
        });
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
            RequireProviderRegistry(),
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
                RequireProviderRegistry(),
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
                RequireProviderRegistry(),
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

    private async Task<object?> HandleAuthOpenAiStatusAsync(AppServerTypedRequest<Contract.AuthOpenAiStatusParams> request, CancellationToken ct)
    {
        if (appConfigMonitor?.Current is { ModelService: not null } remote)
        {
            var provider = remote.Providers.GetValueOrDefault(remote.ProviderId);
            return BuildAuthStatusResult(
                provider?.RemoteAuthentication ?? new ProviderAuthenticationStatus(false), remote.ProviderId);
        }
        var auth = GetOpenAIService<IProviderAuthentication>();
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        var p = request.Params;
        string? authToken = null;
        if (ValueOrDefault(p.IncludeToken) && auth.GetStatus().IsAuthenticated)
            authToken = await auth.TryGetAccessTokenAsync(ValueOrDefault(p.RefreshToken), ct).ConfigureAwait(false);

        return BuildAuthStatusResult(auth.GetStatus(), providerId: null, authToken);
    }

    private async Task<object?> HandleAuthOpenAiLoginAsync(AppServerTypedRequest<Contract.AuthOpenAiLoginParams> request, CancellationToken ct)
    {
        EnsureLocallyManaged();
        var auth = GetOpenAIService<IProviderAuthentication>();
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        var p = request.Params;
        var requestedProviderId = ValueOrDefault(p.ProviderId);
        var providerId = string.IsNullOrWhiteSpace(requestedProviderId) ? "openai" : requestedProviderId.Trim();
        var openBrowser = ValueOrDefault(p.OpenBrowser) ?? true;

        ProviderAuthenticationStatus status;
        try
        {
            status = await auth.LoginAsync(
                new ProviderLoginRequest(
                    openBrowser,
                    AuthorizationRequestAvailable: authorization =>
                {
                    _ = transport.NotifyContractAsync(
                        Protocol.AppServer.AppServerRpc.AuthOpenAiAuthorizeUrl,
                        new Contract.AuthOpenAiAuthorizeUrlNotification
                        {
                            Url = authorization.AuthorizationUrl.ToString(),
                            CallbackPort = authorization.CallbackPort
                        },
                        ct);
                    return ValueTask.CompletedTask;
                }),
                ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidRequest(ex.Message);
        }

        try
        {
            await WriteBindingAsync(
                user => OpenAIAuthBindingPersistence.BindEdits(user, providerId, status),
                Protocol.AppServer.AppServerMethodNames.AuthOpenAiLogin,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw AppServerErrors.InvalidRequest($"Login succeeded but provider config could not be updated: {ex.Message}");
        }

        return BuildAuthStatusResult(status, providerId);
    }

    private async Task<object?> HandleAuthOpenAiLogoutAsync(AppServerTypedRequest<Contract.AuthOpenAiLogoutParams> request, CancellationToken ct)
    {
        EnsureLocallyManaged();
        var auth = GetOpenAIService<IProviderAuthentication>();
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        var p = request.Params;
        var requestedProviderId = ValueOrDefault(p.ProviderId);
        var providerId = string.IsNullOrWhiteSpace(requestedProviderId) ? "openai" : requestedProviderId.Trim();

        await auth.LogoutAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteBindingAsync(
                user => OpenAIAuthBindingPersistence.UnbindEdits(user, providerId),
                Protocol.AppServer.AppServerMethodNames.AuthOpenAiLogout,
                ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort: OAuth tokens are already deleted, which is the user's primary intent.
        }

        return new Contract.AuthOpenAiStatusResult { LoggedIn = false, ProviderId = providerId };
    }

    private async Task<object?> HandleAuthOpenAiUsageAsync(AppServerTypedRequest<Protocol.RpcEmpty> request, CancellationToken ct)
    {
        _ = request;
        var usage = GetOpenAIService<IProviderUsageReader>();
        if (usage is null)
            throw AppServerErrors.InvalidRequest("ChatGPT usage telemetry is not available in this server build.");

        var snapshot = await usage.ReadAsync(ct).ConfigureAwait(false);

        return OpenAIUsageMapping.ToWire(snapshot);
    }

    private void EnsureProviderManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("provider/*");
    }

    private void EnsureLocallyManaged()
    {
        if (appConfigMonitor?.Current.ModelService is not null)
            throw AppServerErrors.InvalidRequest("Providers and subscription authentication are managed by the model service. Use its configuration or CLI.");
    }

    private ModelProviderRegistry RequireProviderRegistry() =>
        modelProviderRegistry
        ?? throw AppServerErrors.InvalidRequest("Model providers are not available in this server build.");

    private TService? GetOpenAIService<TService>() where TService : class
    {
        var registry = RequireProviderRegistry();
        return registry.TryResolve(ModelProviderProtocols.OpenAIResponses, out var provider)
            ? provider?.GetService(typeof(TService)) as TService
            : null;
    }

    private static Contract.AuthOpenAiStatusResult BuildAuthStatusResult(
        ProviderAuthenticationStatus status,
        string? providerId,
        string? authToken = null) =>
        new()
        {
            LoggedIn = status.IsAuthenticated,
            AccountId = status.AccountId,
            PlanType = status.PlanType,
            Email = status.Email,
            LastRefresh = status.LastRefresh,
            AccessTokenExpiresAt = status.AccessTokenExpiresAt,
            ProviderId = providerId,
            AuthToken = authToken is null ? default : new Protocol.Optional<string>(authToken)
        };

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private async Task WriteBindingAsync(
        Func<JsonObject, IReadOnlyList<ConfigEdit>> buildEdits,
        string source,
        CancellationToken ct)
    {
        var service = configuration ?? throw new InvalidOperationException("Workspace configuration is not available.");
        var user = service.Read(includeLayers: true).Layers![0];
        var edits = buildEdits(user.Config);
        if (edits.Count > 0)
            await service.WriteAsync(edits, service.UserConfigPath, user.Layer.Version, source, ct).ConfigureAwait(false);
    }

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
