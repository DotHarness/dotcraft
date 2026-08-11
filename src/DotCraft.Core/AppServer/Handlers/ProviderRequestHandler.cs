using System.Text.Json;
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
    AppServerRuntimeConfigRefresher runtimeConfig,
    string? workspaceCraftPath,
    IAppConfigMonitor? appConfigMonitor,
    ModelProviderRegistry? modelProviderRegistry,
    bool supportsUltraReasoning) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.ProviderList, HandleProviderListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ProviderCreate, HandleProviderCreateAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ProviderUpdate, HandleProviderUpdateAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ProviderDelete, HandleProviderDeleteAsync);
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
            Providers = new Protocol.Optional<IReadOnlyList<Contract.ProviderInfo>>(
                ProviderContractMapper.BuildProviderInfos(config))
        });
    }

    private Task<object?> HandleProviderCreateAsync(AppServerTypedRequest<Contract.ProviderCreateParams> request, CancellationToken ct)
    {
        _ = ct;
        EnsureProviderManagementAvailable();
        var msg = request.Message;
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams("'id' is required.");
        var paramsElement = msg.Params.Value;
        var p = request.Params;
        var id = NormalizeProviderId(ValueOrDefault(p.Id));
        var protocol = NormalizeProviderProtocol(ValueOrDefault(p.Protocol));
        ValidateProviderPayload(
            id,
            protocol,
            ValueOrDefault(p.EndPoint),
            ValueOrDefault(p.NetworkTimeoutSeconds),
            ValueOrDefault(p.MaxOutputTokens),
            ValueOrDefault(p.StreamMaxRetries),
            ValueOrDefault(p.StreamIdleTimeoutMs));

        var configPath = workspaceConfig.PersonalConfigPath;
        var root = WorkspaceConfigEditor.LoadObject(configPath);
        var providers = GetOrCreateConfigSection(root, "Providers", createIfMissing: true)!;
        if (WorkspaceConfigEditor.FindCaseInsensitiveKey(providers, id) != null)
            throw AppServerErrors.InvalidParams($"Provider '{id}' already exists.");

        var provider = new JsonObject();
        providers[id] = provider;
        var createAuthMethod = ModelProviderAuthMethods.Normalize(ValueOrDefault(p.AuthMethod));
        var supportsHostedImageGeneration = TryGetCaseInsensitiveProperty(paramsElement, "supportsHostedImageGeneration", out var supportsHostedImageGenerationEl)
            ? ParseBoolean(supportsHostedImageGenerationEl, "supportsHostedImageGeneration")
            : ModelProviderResolver.ResolveHostedImageGenerationSupport(new AppConfig.ModelProviderConfig
            {
                Protocol = protocol,
                EndPoint = ValueOrDefault(p.EndPoint) ?? string.Empty,
                AuthMethod = createAuthMethod
            });
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "DisplayName", NormalizeOptionalString(ValueOrDefault(p.DisplayName)) ?? id);
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "Protocol", protocol);
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "ApiKey", NormalizeOptionalString(ValueOrDefault(p.ApiKey)));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "EndPoint", NormalizeOptionalString(ValueOrDefault(p.EndPoint)));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "NetworkTimeoutSeconds", NormalizeNetworkTimeout(ValueOrDefault(p.NetworkTimeoutSeconds)));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "MaxOutputTokens", NormalizeMaxOutputTokens(ValueOrDefault(p.MaxOutputTokens)));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "StreamMaxRetries", NormalizeStreamMaxRetries(ValueOrDefault(p.StreamMaxRetries)));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "StreamIdleTimeoutMs", NormalizeStreamIdleTimeoutMs(ValueOrDefault(p.StreamIdleTimeoutMs)));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "SupportsHostedImageGeneration", supportsHostedImageGeneration);
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "AuthMethod",
            createAuthMethod == ModelProviderAuthMethods.ApiKey ? null : createAuthMethod);
        WorkspaceConfigEditor.WriteObject(configPath, root);

        runtimeConfig.RefreshCurrentLlmConfig();
        runtimeConfig.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(Protocol.AppServer.AppServerMethodNames.ProviderCreate, [ConfigChangeRegions.ProviderRegistry]);

        var current = workspaceConfig.LoadCurrentMergedConfig();
        return Task.FromResult<object?>(new Contract.ProviderMutationResult
        {
            Provider = ProviderContractMapper.BuildProviderInfo(id, current.Providers[id], isImplicit: false)
        });
    }

    private Task<object?> HandleProviderUpdateAsync(AppServerTypedRequest<Contract.ProviderUpdateParams> request, CancellationToken ct)
    {
        _ = ct;
        EnsureProviderManagementAvailable();
        var msg = request.Message;
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams("'id' is required.");

        var p = request.Params;
        var id = NormalizeProviderId(ValueOrDefault(p.Id));

        var configPath = workspaceConfig.PersonalConfigPath;
        var root = WorkspaceConfigEditor.LoadObject(configPath);
        var providers = GetOrCreateConfigSection(root, "Providers", createIfMissing: false)
            ?? throw AppServerErrors.InvalidParams($"Provider '{id}' is not configured.");
        var existingKey = WorkspaceConfigEditor.FindCaseInsensitiveKey(providers, id)
            ?? throw AppServerErrors.InvalidParams($"Provider '{id}' is not configured.");
        if (providers[existingKey] is not JsonObject provider)
            throw AppServerErrors.InvalidParams($"Provider '{id}' is not an object.");

        var protocolKey = WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "Protocol");
        var persistedProtocol = ReadConfigStringValue(provider, protocolKey);
        var currentProtocol = NormalizeProviderProtocol(persistedProtocol ?? ModelProviderProtocols.OpenAI);
        var protocol = currentProtocol;
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "protocol", out var protocolEl))
            protocol = NormalizeProviderProtocol(ParseNullableString(protocolEl, "protocol"));
        var endPoint = ReadConfigStringValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "EndPoint"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "endPoint", out var endPointEl))
            endPoint = NormalizeOptionalString(ParseNullableString(endPointEl, "endPoint"));
        int? timeout = ReadConfigIntegerValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "NetworkTimeoutSeconds"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "networkTimeoutSeconds", out var timeoutEl))
            timeout = ParseNullableInteger(timeoutEl, "networkTimeoutSeconds");
        int? maxOutputTokens = ReadConfigIntegerValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "MaxOutputTokens"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "maxOutputTokens", out var maxOutputTokensEl))
            maxOutputTokens = ParseNullableInteger(maxOutputTokensEl, "maxOutputTokens");
        int? streamMaxRetries = ReadConfigIntegerValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "StreamMaxRetries"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "streamMaxRetries", out var streamMaxRetriesEl))
            streamMaxRetries = ParseNullableInteger(streamMaxRetriesEl, "streamMaxRetries");
        int? streamIdleTimeoutMs = ReadConfigIntegerValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "StreamIdleTimeoutMs"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "streamIdleTimeoutMs", out var streamIdleTimeoutMsEl))
            streamIdleTimeoutMs = ParseNullableInteger(streamIdleTimeoutMsEl, "streamIdleTimeoutMs");

        ValidateProviderPayload(id, protocol, endPoint, timeout, maxOutputTokens, streamMaxRetries, streamIdleTimeoutMs);

        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "displayName", out var displayNameEl))
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "DisplayName"), "DisplayName",
                NormalizeOptionalString(ParseNullableString(displayNameEl, "displayName")) ?? id);
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "protocol", out _)
            || !string.Equals(persistedProtocol, protocol, StringComparison.Ordinal))
        {
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, protocolKey, "Protocol", protocol);
        }
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "apiKey", out var apiKeyEl))
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "ApiKey"), "ApiKey",
                NormalizeOptionalString(ParseNullableString(apiKeyEl, "apiKey")));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "endPoint", out _))
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "EndPoint"), "EndPoint", endPoint);
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "networkTimeoutSeconds", out _))
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "NetworkTimeoutSeconds"), "NetworkTimeoutSeconds",
                NormalizeNetworkTimeout(timeout));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "maxOutputTokens", out _))
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "MaxOutputTokens"), "MaxOutputTokens",
                NormalizeMaxOutputTokens(maxOutputTokens));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "streamMaxRetries", out _))
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "StreamMaxRetries"), "StreamMaxRetries",
                NormalizeStreamMaxRetries(streamMaxRetries));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "streamIdleTimeoutMs", out _))
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "StreamIdleTimeoutMs"), "StreamIdleTimeoutMs",
                NormalizeStreamIdleTimeoutMs(streamIdleTimeoutMs));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "supportsHostedImageGeneration", out var supportsHostedImageGenerationEl))
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "SupportsHostedImageGeneration"), "SupportsHostedImageGeneration",
                ParseBoolean(supportsHostedImageGenerationEl, "supportsHostedImageGeneration"));
        if (TryGetCaseInsensitiveProperty(msg.Params.Value, "authMethod", out var authMethodEl))
        {
            var updatedAuthMethod = ModelProviderAuthMethods.Normalize(ParseNullableString(authMethodEl, "authMethod"));
            WorkspaceConfigEditor.UpsertOrRemoveValue(provider, WorkspaceConfigEditor.FindCaseInsensitiveKey(provider, "AuthMethod"), "AuthMethod",
                updatedAuthMethod == ModelProviderAuthMethods.ApiKey ? null : updatedAuthMethod);
        }

        WorkspaceConfigEditor.WriteObject(configPath, root);

        runtimeConfig.RefreshCurrentLlmConfig();
        runtimeConfig.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(Protocol.AppServer.AppServerMethodNames.ProviderUpdate, [ConfigChangeRegions.ProviderRegistry]);

        var current = workspaceConfig.LoadCurrentMergedConfig();
        return Task.FromResult<object?>(new Contract.ProviderMutationResult
        {
            Provider = ProviderContractMapper.BuildProviderInfo(existingKey, current.Providers[existingKey], isImplicit: false)
        });
    }

    private Task<object?> HandleProviderDeleteAsync(AppServerTypedRequest<Contract.ProviderDeleteParams> request, CancellationToken ct)
    {
        _ = ct;
        EnsureProviderManagementAvailable();
        var p = request.Params;
        var id = NormalizeProviderId(ValueOrDefault(p.Id));

        var current = workspaceConfig.LoadCurrentMergedConfig();
        if (string.Equals(current.ProviderId, id, StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams($"Provider '{id}' is selected by the active workspace.");

        var configPath = workspaceConfig.PersonalConfigPath;
        var root = WorkspaceConfigEditor.LoadObject(configPath);
        var providers = GetOrCreateConfigSection(root, "Providers", createIfMissing: false);
        var removed = false;
        if (providers != null && WorkspaceConfigEditor.FindCaseInsensitiveKey(providers, id) is { } existingKey)
            removed = providers.Remove(existingKey);

        if (providers is { Count: 0 })
            root.Remove(WorkspaceConfigEditor.FindCaseInsensitiveKey(root, "Providers") ?? "Providers");
        if (removed)
            WorkspaceConfigEditor.WriteObject(configPath, root);

        if (removed)
        {
            runtimeConfig.RefreshCurrentLlmConfig();
            runtimeConfig.InvalidateThreadAgents();
            appConfigMonitor?.NotifyChanged(Protocol.AppServer.AppServerMethodNames.ProviderDelete, [ConfigChangeRegions.ProviderRegistry]);
        }

        return Task.FromResult<object?>(new Contract.ProviderDeleteResult { Deleted = removed });
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
            ct);

        return new Contract.ModelListResult
        {
            Success = result.Success,
            ProviderId = OmitIfNull(result.ProviderId),
            Protocol = result.Protocol,
            Models = new Protocol.Optional<IReadOnlyList<Contract.ModelCatalogItem>>(
                result.Models.Select(m => ProviderContractMapper.BuildModelCatalogItem(
                config,
                result.Protocol,
                result.EndPoint,
                m,
                supportsUltraReasoning)).ToArray()),
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
            result = await ModelProviderCatalog.FetchAsync(config, RequireProviderRegistry(), providerId, ct);
        }
        else
        {
            var protocol = NormalizeProviderProtocol(ValueOrDefault(p.Protocol));
            ValidateProviderPayload(
                "__provider_test__",
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
                        NetworkTimeoutSeconds = NormalizeNetworkTimeout(ValueOrDefault(p.NetworkTimeoutSeconds)),
                        MaxOutputTokens = NormalizeMaxOutputTokens(ValueOrDefault(p.MaxOutputTokens)),
                        StreamMaxRetries = NormalizeStreamMaxRetries(ValueOrDefault(p.StreamMaxRetries)),
                        StreamIdleTimeoutMs = NormalizeStreamIdleTimeoutMs(ValueOrDefault(p.StreamIdleTimeoutMs))
                    }
                }
            };
            result = await ModelProviderCatalog.FetchAsync(draftConfig, RequireProviderRegistry(), draftProviderId, ct);
            result.ProviderId = null;
        }

        return new Contract.ProviderTestResult
        {
            Success = result.Success,
            ProviderId = OmitIfNull(result.ProviderId),
            Protocol = result.Protocol ?? NormalizeProviderProtocol(ValueOrDefault(p.Protocol)),
            Models = new Protocol.Optional<IReadOnlyList<Contract.ModelCatalogItem>>(
                result.Models.Select(m => ProviderContractMapper.BuildModelCatalogItem(
                config,
                result.Protocol ?? NormalizeProviderProtocol(ValueOrDefault(p.Protocol)),
                result.EndPoint,
                m,
                supportsUltraReasoning)).ToArray()),
            ErrorCode = result.Success ? default : OmitIfNull(result.ErrorCode.ToString()),
            ErrorMessage = result.Success
                ? default
                : OmitIfNull(ProviderContractMapper.FormatModelListErrorMessage(result.ErrorMessage, result.EndPoint))
        };
    }

    private Task<object?> HandleAuthOpenAiStatusAsync(AppServerTypedRequest<Protocol.RpcEmpty> request, CancellationToken ct)
    {
        _ = request;
        _ = ct;
        var auth = GetOpenAIService<IProviderAuthentication>();
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        return Task.FromResult<object?>(BuildAuthStatusResult(auth.GetStatus(), providerId: null));
    }

    private async Task<object?> HandleAuthOpenAiLoginAsync(AppServerTypedRequest<Contract.AuthOpenAiLoginParams> request, CancellationToken ct)
    {
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
            OpenAIAuthBindingPersistence.BindProviderToOAuth(providerId, status, workspaceConfig.EffectiveGlobalConfigPath);
        }
        catch (Exception ex)
        {
            throw AppServerErrors.InvalidRequest($"Login succeeded but provider config could not be updated: {ex.Message}");
        }

        runtimeConfig.RefreshCurrentLlmConfig();
        runtimeConfig.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(Protocol.AppServer.AppServerMethodNames.AuthOpenAiLogin, [ConfigChangeRegions.ProviderRegistry]);

        return BuildAuthStatusResult(status, providerId);
    }

    private async Task<object?> HandleAuthOpenAiLogoutAsync(AppServerTypedRequest<Contract.AuthOpenAiLogoutParams> request, CancellationToken ct)
    {
        var auth = GetOpenAIService<IProviderAuthentication>();
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        var p = request.Params;
        var requestedProviderId = ValueOrDefault(p.ProviderId);
        var providerId = string.IsNullOrWhiteSpace(requestedProviderId) ? "openai" : requestedProviderId.Trim();

        await auth.LogoutAsync(ct).ConfigureAwait(false);
        try
        {
            OpenAIAuthBindingPersistence.UnbindProvider(providerId, workspaceConfig.EffectiveGlobalConfigPath);
        }
        catch (Exception)
        {
            // Best-effort: OAuth tokens are already deleted, which is the user's primary intent.
        }

        runtimeConfig.RefreshCurrentLlmConfig();
        runtimeConfig.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(Protocol.AppServer.AppServerMethodNames.AuthOpenAiLogout, [ConfigChangeRegions.ProviderRegistry]);

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

    private static Contract.AuthOpenAiStatusResult BuildAuthStatusResult(ProviderAuthenticationStatus status, string? providerId) =>
        new()
        {
            LoggedIn = status.IsAuthenticated,
            AccountId = status.AccountId,
            PlanType = status.PlanType,
            Email = status.Email,
            LastRefresh = status.LastRefresh,
            AccessTokenExpiresAt = status.AccessTokenExpiresAt,
            ProviderId = providerId
        };

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static string NormalizeProviderId(string? id)
    {
        var normalized = NormalizeOptionalString(id);
        if (string.IsNullOrWhiteSpace(normalized))
            throw AppServerErrors.InvalidParams("'id' is required.");
        if (normalized.IndexOfAny(['/', '\\', ':']) >= 0)
            throw AppServerErrors.InvalidParams("'id' must not contain '/', '\\', or ':'.");
        return normalized;
    }

    private static void ValidateProviderPayload(
        string id,
        string protocol,
        string? endPoint,
        int? networkTimeoutSeconds,
        int? maxOutputTokens,
        int? streamMaxRetries,
        int? streamIdleTimeoutMs)
    {
        _ = id;
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

    private static int? NormalizeNetworkTimeout(int? value) => value.HasValue ? Math.Max(1, value.Value) : null;

    private static int? NormalizeMaxOutputTokens(int? value) => value.HasValue ? Math.Max(1, value.Value) : null;

    private static int? NormalizeStreamMaxRetries(int? value) =>
        value.HasValue ? Math.Clamp(value.Value, 0, ModelProviderDefaults.MaxStreamMaxRetries) : null;

    private static int? NormalizeStreamIdleTimeoutMs(int? value) => value.HasValue ? Math.Max(1, value.Value) : null;

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

    private static string? ParseNullableString(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be a string or null.")
        };
    }

    private static int? ParseNullableInteger(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be an integer or null.")
        };
    }

    private static bool ParseBoolean(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be a boolean.")
        };
    }

    private static string? ReadConfigStringValue(JsonObject? root, string? key)
    {
        if (root == null || string.IsNullOrEmpty(key))
            return null;
        if (!root.TryGetPropertyValue(key, out var node) || node == null)
            return null;
        if (node is not JsonValue value)
            return null;
        return value.TryGetValue<string>(out var result) ? result : null;
    }

    private static int? ReadConfigIntegerValue(JsonObject? root, string? key)
    {
        if (root == null || string.IsNullOrEmpty(key))
            return null;
        if (!root.TryGetPropertyValue(key, out var node) || node == null)
            return null;
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue<int>(out var result))
            return result;
        if (value.TryGetValue<long>(out var longResult) && longResult is >= int.MinValue and <= int.MaxValue)
            return (int)longResult;
        return null;
    }

    private static JsonObject? GetOrCreateConfigSection(JsonObject root, string canonicalKey, bool createIfMissing)
    {
        var existingKey = WorkspaceConfigEditor.FindCaseInsensitiveKey(root, canonicalKey);
        if (existingKey != null)
        {
            if (root[existingKey] is JsonObject existingObject)
                return existingObject;
            if (!createIfMissing)
                return null;

            var replacement = new JsonObject();
            root[existingKey] = replacement;
            return replacement;
        }

        if (!createIfMissing)
            return null;

        var section = new JsonObject();
        root[canonicalKey] = section;
        return section;
    }

    private static bool TryGetCaseInsensitiveProperty(JsonElement obj, string expectedName, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);
}
