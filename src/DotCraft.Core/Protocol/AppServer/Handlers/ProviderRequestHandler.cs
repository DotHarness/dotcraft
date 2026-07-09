using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles provider, model catalog, and ChatGPT subscription auth wire methods.
/// </summary>
internal sealed class ProviderRequestHandler(
    IAppServerTransport transport,
    WorkspaceConfigEditor workspaceConfig,
    AppServerRuntimeConfigRefresher runtimeConfig,
    string? workspaceCraftPath,
    IAppConfigMonitor? appConfigMonitor,
    OpenAIClientProvider? openAIClientProvider,
    IOpenAIAuthService? openAIAuthService,
    IOpenAIUsageService? openAIUsageService) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.ProviderList, HandleProviderListAsync);
        table.Map(AppServerMethods.ProviderCreate, HandleProviderCreateAsync);
        table.Map(AppServerMethods.ProviderUpdate, HandleProviderUpdateAsync);
        table.Map(AppServerMethods.ProviderDelete, HandleProviderDeleteAsync);
        table.Map(AppServerMethods.ProviderTest, HandleProviderTestAsync);
        table.Map(AppServerMethods.ModelList, HandleModelListAsync);
        table.Map(AppServerMethods.AuthOpenAiStatus, HandleAuthOpenAiStatusAsync);
        table.Map(AppServerMethods.AuthOpenAiLogin, HandleAuthOpenAiLoginAsync);
        table.Map(AppServerMethods.AuthOpenAiLogout, HandleAuthOpenAiLogoutAsync);
        table.Map(AppServerMethods.AuthOpenAiUsage, HandleAuthOpenAiUsageAsync);
    }

    private Task<object?> HandleProviderListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = AppServerParams.Get<ProviderListParams>(msg);
        _ = ct;
        EnsureProviderManagementAvailable();

        var config = workspaceConfig.LoadCurrentMergedConfig();
        return Task.FromResult<object?>(new ProviderListResult
        {
            Providers = ProviderWireMapper.BuildProviderInfos(config)
        });
    }

    private Task<object?> HandleProviderCreateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureProviderManagementAvailable();
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams("'id' is required.");
        var paramsElement = msg.Params.Value;
        var p = AppServerParams.Get<ProviderCreateParams>(msg);
        var id = NormalizeProviderId(p.Id);
        var protocol = NormalizeProviderProtocol(p.Protocol);
        ValidateProviderPayload(
            id,
            protocol,
            p.EndPoint,
            p.NetworkTimeoutSeconds,
            p.MaxOutputTokens,
            p.StreamMaxRetries,
            p.StreamIdleTimeoutMs);

        var configPath = workspaceConfig.PersonalConfigPath;
        var root = WorkspaceConfigEditor.LoadObject(configPath);
        var providers = GetOrCreateConfigSection(root, "Providers", createIfMissing: true)!;
        if (WorkspaceConfigEditor.FindCaseInsensitiveKey(providers, id) != null)
            throw AppServerErrors.InvalidParams($"Provider '{id}' already exists.");

        var provider = new JsonObject();
        providers[id] = provider;
        var createAuthMethod = ModelProviderAuthMethods.Normalize(p.AuthMethod);
        var supportsHostedImageGeneration = TryGetCaseInsensitiveProperty(paramsElement, "supportsHostedImageGeneration", out var supportsHostedImageGenerationEl)
            ? ParseBoolean(supportsHostedImageGenerationEl, "supportsHostedImageGeneration")
            : ModelProviderResolver.ResolveHostedImageGenerationSupport(new AppConfig.ModelProviderConfig
            {
                Protocol = protocol,
                EndPoint = p.EndPoint,
                AuthMethod = createAuthMethod
            });
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "DisplayName", NormalizeOptionalString(p.DisplayName) ?? id);
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "Protocol", protocol);
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "ApiKey", NormalizeOptionalString(p.ApiKey));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "EndPoint", NormalizeOptionalString(p.EndPoint));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "NetworkTimeoutSeconds", NormalizeNetworkTimeout(p.NetworkTimeoutSeconds));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "MaxOutputTokens", NormalizeMaxOutputTokens(p.MaxOutputTokens));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "StreamMaxRetries", NormalizeStreamMaxRetries(p.StreamMaxRetries));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "StreamIdleTimeoutMs", NormalizeStreamIdleTimeoutMs(p.StreamIdleTimeoutMs));
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "SupportsHostedImageGeneration", supportsHostedImageGeneration);
        WorkspaceConfigEditor.UpsertOrRemoveValue(provider, null, "AuthMethod",
            createAuthMethod == ModelProviderAuthMethods.ApiKey ? null : createAuthMethod);
        WorkspaceConfigEditor.WriteObject(configPath, root);

        runtimeConfig.RefreshCurrentLlmConfig();
        runtimeConfig.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(AppServerMethods.ProviderCreate, [ConfigChangeRegions.ProviderRegistry]);

        var current = workspaceConfig.LoadCurrentMergedConfig();
        return Task.FromResult<object?>(new ProviderMutationResult
        {
            Provider = ProviderWireMapper.BuildProviderInfo(id, current.Providers[id], isImplicit: false)
        });
    }

    private Task<object?> HandleProviderUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureProviderManagementAvailable();
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams("'id' is required.");

        var p = AppServerParams.Get<ProviderUpdateParams>(msg);
        var id = NormalizeProviderId(p.Id);

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
        appConfigMonitor?.NotifyChanged(AppServerMethods.ProviderUpdate, [ConfigChangeRegions.ProviderRegistry]);

        var current = workspaceConfig.LoadCurrentMergedConfig();
        return Task.FromResult<object?>(new ProviderMutationResult
        {
            Provider = ProviderWireMapper.BuildProviderInfo(existingKey, current.Providers[existingKey], isImplicit: false)
        });
    }

    private Task<object?> HandleProviderDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureProviderManagementAvailable();
        var p = AppServerParams.Get<ProviderDeleteParams>(msg);
        var id = NormalizeProviderId(p.Id);

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
            appConfigMonitor?.NotifyChanged(AppServerMethods.ProviderDelete, [ConfigChangeRegions.ProviderRegistry]);
        }

        return Task.FromResult<object?>(new ProviderDeleteResult { Deleted = removed });
    }

    private async Task<object?> HandleModelListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<ModelListParams>(msg);

        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.ModelList);

        var config = appConfigMonitor?.Current
            ?? AppConfig.LoadWithGlobalFallback(Path.Combine(workspaceCraftPath, "config.json"), workspaceConfig.EffectiveGlobalConfigPath);
        var result = await ModelProviderCatalog.FetchAsync(config, NormalizeOptionalString(p.ProviderId), ct, openAIClientProvider);

        return new ModelListResult
        {
            Success = result.Success,
            ProviderId = result.ProviderId,
            Protocol = result.Protocol,
            Models = [.. result.Models.Select(m => new ModelCatalogItem
            {
                Id = m.Id,
                OwnedBy = m.OwnedBy,
                CreatedAt = m.CreatedAt,
                Reasoning = ProviderWireMapper.MapReasoningCapability(ModelThinkingAdapterCatalog.ResolveReasoningCapability(
                    config,
                    result.Protocol,
                    result.EndPoint,
                    m.Id)),
                ContextWindow = ProviderWireMapper.MapContextWindowCapability(
                    ModelContextWindowCatalog.ResolveContextWindowCapability(config, m.Id))
            })],
            ErrorCode = result.Success ? null : result.ErrorCode.ToString(),
            ErrorMessage = result.Success ? null : ProviderWireMapper.FormatModelListErrorMessage(result.ErrorMessage, result.EndPoint)
        };
    }

    private async Task<object?> HandleProviderTestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        EnsureProviderManagementAvailable();
        var p = AppServerParams.Get<ProviderTestParams>(msg);
        var config = workspaceConfig.LoadCurrentMergedConfig();
        var providerId = NormalizeOptionalString(p.ProviderId);

        ModelCatalogResult result;
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            result = await ModelProviderCatalog.FetchAsync(config, providerId, ct, openAIClientProvider);
        }
        else
        {
            var protocol = NormalizeProviderProtocol(p.Protocol);
            ValidateProviderPayload(
                "__provider_test__",
                protocol,
                p.EndPoint,
                p.NetworkTimeoutSeconds,
                p.MaxOutputTokens,
                p.StreamMaxRetries,
                p.StreamIdleTimeoutMs);
            var draftProviderId = "__provider_test__";
            var draftConfig = new AppConfig
            {
                Model = string.IsNullOrWhiteSpace(config.Model) ? "gpt-4o-mini" : config.Model,
                NetworkTimeoutSeconds = config.NetworkTimeoutSeconds,
                Providers = new Dictionary<string, AppConfig.ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    [draftProviderId] = new()
                    {
                        DisplayName = "Provider Test",
                        Protocol = protocol,
                        ApiKey = NormalizeOptionalString(p.ApiKey) ?? string.Empty,
                        EndPoint = NormalizeOptionalString(p.EndPoint) ?? string.Empty,
                        NetworkTimeoutSeconds = NormalizeNetworkTimeout(p.NetworkTimeoutSeconds),
                        MaxOutputTokens = NormalizeMaxOutputTokens(p.MaxOutputTokens),
                        StreamMaxRetries = NormalizeStreamMaxRetries(p.StreamMaxRetries),
                        StreamIdleTimeoutMs = NormalizeStreamIdleTimeoutMs(p.StreamIdleTimeoutMs)
                    }
                }
            };
            result = await ModelProviderCatalog.FetchAsync(draftConfig, draftProviderId, ct, openAIClientProvider);
            result.ProviderId = null;
        }

        return new ProviderTestResult
        {
            Success = result.Success,
            ProviderId = result.ProviderId,
            Protocol = result.Protocol ?? NormalizeProviderProtocol(p.Protocol),
            Models = [.. result.Models.Select(m => new ModelCatalogItem
            {
                Id = m.Id,
                OwnedBy = m.OwnedBy,
                CreatedAt = m.CreatedAt,
                Reasoning = ProviderWireMapper.MapReasoningCapability(ModelThinkingAdapterCatalog.ResolveReasoningCapability(
                    config,
                    result.Protocol ?? NormalizeProviderProtocol(p.Protocol),
                    result.EndPoint,
                    m.Id)),
                ContextWindow = ProviderWireMapper.MapContextWindowCapability(
                    ModelContextWindowCatalog.ResolveContextWindowCapability(config, m.Id))
            })],
            ErrorCode = result.Success ? null : result.ErrorCode.ToString(),
            ErrorMessage = result.Success ? null : ProviderWireMapper.FormatModelListErrorMessage(result.ErrorMessage, result.EndPoint)
        };
    }

    private Task<object?> HandleAuthOpenAiStatusAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;
        var auth = openAIAuthService;
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        return Task.FromResult<object?>(BuildAuthStatusResult(auth.GetStatus(), providerId: null));
    }

    private async Task<object?> HandleAuthOpenAiLoginAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var auth = openAIAuthService;
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        var p = msg.Params is null ? new AuthOpenAiLoginParams() : AppServerParams.Get<AuthOpenAiLoginParams>(msg);
        var providerId = string.IsNullOrWhiteSpace(p.ProviderId) ? "openai" : p.ProviderId.Trim();
        var openBrowser = p.OpenBrowser ?? true;

        OpenAIAuthStatus status;
        try
        {
            status = await auth.LoginAsync(
                openBrowser,
                onAuthorizationUrl: url =>
                {
                    _ = transport.WriteMessageAsync(new
                    {
                        jsonrpc = "2.0",
                        method = AppServerMethods.AuthOpenAiAuthorizeUrl,
                        @params = new AuthOpenAiAuthorizeUrlNotification
                        {
                            Url = url,
                            CallbackPort = OpenAIAuthConstants.RedirectPortPrimary
                        }
                    }, ct);
                },
                ct).ConfigureAwait(false);
        }
        catch (OpenAIAuthException ex)
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
        appConfigMonitor?.NotifyChanged(AppServerMethods.AuthOpenAiLogin, [ConfigChangeRegions.ProviderRegistry]);

        return BuildAuthStatusResult(status, providerId);
    }

    private async Task<object?> HandleAuthOpenAiLogoutAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var auth = openAIAuthService;
        if (auth is null)
            throw AppServerErrors.InvalidRequest("ChatGPT authentication is not available in this server build.");

        var p = msg.Params is null ? new AuthOpenAiLogoutParams() : AppServerParams.Get<AuthOpenAiLogoutParams>(msg);
        var providerId = string.IsNullOrWhiteSpace(p.ProviderId) ? "openai" : p.ProviderId.Trim();

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
        appConfigMonitor?.NotifyChanged(AppServerMethods.AuthOpenAiLogout, [ConfigChangeRegions.ProviderRegistry]);

        return new AuthOpenAiStatusResult { LoggedIn = false, ProviderId = providerId };
    }

    private async Task<object?> HandleAuthOpenAiUsageAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        var usage = openAIUsageService;
        if (usage is null)
            throw AppServerErrors.InvalidRequest("ChatGPT usage telemetry is not available in this server build.");

        var snapshot = usage.CurrentSnapshot;
        if (snapshot is null)
            snapshot = await usage.RefreshAsync(ct).ConfigureAwait(false);

        return OpenAIUsageMapping.ToWire(snapshot);
    }

    private void EnsureProviderManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("provider/*");
    }

    private static AuthOpenAiStatusResult BuildAuthStatusResult(OpenAIAuthStatus status, string? providerId) =>
        new()
        {
            LoggedIn = status.LoggedIn,
            AccountId = status.AccountId,
            PlanType = status.PlanType,
            Email = status.Email,
            LastRefresh = status.LastRefresh,
            AccessTokenExpiresAt = status.AccessTokenExpiresAt,
            ProviderId = providerId
        };

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
}
