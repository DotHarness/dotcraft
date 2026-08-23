using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;
using DotCraft.Lsp;
using DotCraft.Memory;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using ModelPreferenceContextWindow = DotCraft.Configuration.ModelPreferenceContextWindow;

namespace DotCraft.AppServer;

internal sealed class WorkspaceRequestHandler(
    ICommitMessageSuggester? commitMessageSuggest,
    IWelcomeSuggester? welcomeSuggestionService,
    IReadOnlyList<ConfigSchemaSection> configSchema,
    MemoryStore? memoryStore,
    DreamStore? dreamStore,
    DreamsService? dreamsService,
    LspServerManager? lspServerManager,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    string? hostWorkspacePath,
    IContextPageManager? contextPageManager,
    WorkspaceConfigEditor workspaceConfig,
    AppServerRuntimeConfigRefresher runtimeConfig) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.WorkspaceCommitMessageSuggest, HandleWorkspaceCommitMessageSuggestAsync);
        table.Map(Protocol.AppServer.AppServerRpc.WelcomeSuggestions, HandleWelcomeSuggestionsAsync);
        table.Map(Protocol.AppServer.AppServerRpc.WorkspaceConfigSchema, HandleWorkspaceConfigSchemaAsync);
        table.Map(Protocol.AppServer.AppServerRpc.WorkspaceConfigUpdate, HandleWorkspaceConfigUpdateAsync);
        table.Map(Protocol.AppServer.AppServerRpc.MemoryReset, HandleMemoryResetAsync);
    }

    private async Task<object?> HandleWorkspaceCommitMessageSuggestAsync(
        AppServerTypedRequest<Contract.WorkspaceCommitMessageSuggestParams> request,
        CancellationToken ct)
    {
        if (commitMessageSuggest == null)
            throw AppServerErrors.InvalidRequest("Commit message suggestion is not available on this connection.");

        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId);
        var paths = ValueOrDefault(p.Paths);
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (paths is not { Count: > 0 })
            throw AppServerErrors.InvalidParams("'paths' must contain at least one file path.");

        try
        {
            var result = await commitMessageSuggest.SuggestAsync(new CommitMessageSuggestionRequest
            {
                ThreadId = threadId,
                Paths = paths.ToArray(),
                Provider = ValueOrDefault(p.Provider),
                MaxDiffChars = ValueOrDefault(p.MaxDiffChars)
            }, ct);
            return new Contract.WorkspaceCommitMessageSuggestResult { Message = result.Message };
        }
        catch (KeyNotFoundException ex)
        {
            throw AppServerErrors.ThreadNotFound(AppServerExceptionMapper.ExtractQuotedId(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidRequest(ex.Message);
        }
    }

    private async Task<object?> HandleWelcomeSuggestionsAsync(
        AppServerTypedRequest<Contract.WelcomeSuggestionsParams> request,
        CancellationToken ct)
    {
        if (welcomeSuggestionService == null)
            throw AppServerErrors.InvalidRequest("Welcome suggestions are not available on this connection.");

        var p = request.Params;
        var identity = NormalizeIdentityWorkspace(ToDomain(ValueOrDefault(p.Identity)));
        if (string.IsNullOrWhiteSpace(identity.WorkspacePath))
            throw AppServerErrors.InvalidParams("'identity.workspacePath' is required.");

        try
        {
            var result = await welcomeSuggestionService.SuggestAsync(new WelcomeSuggestionRequest
            {
                Identity = identity,
                MaxItems = ValueOrDefault(p.MaxItems)
            }, ct);
            return ToContract(result);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidRequest(ex.Message);
        }
    }

    private Task<object?> HandleWorkspaceConfigSchemaAsync(
        AppServerTypedRequest<Contract.WorkspaceConfigSchemaParams> request,
        CancellationToken ct)
    {
        _ = ct;
        _ = request.Params;
        if (configSchema.Count == 0)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.WorkspaceConfigSchema);

        return Task.FromResult<object?>(new Contract.WorkspaceConfigSchemaResult
        {
            Sections = configSchema.Select(ToContract).ToArray()
        });
    }

    private async Task<object?> HandleWorkspaceConfigUpdateAsync(
        AppServerTypedRequest<Contract.WorkspaceConfigUpdateParams> request,
        CancellationToken ct)
    {
        const string requiredFieldMessage =
            "At least one of 'providerId', 'providerPreferences', 'welcomeSuggestionsEnabled', " +
            "'skillsSelfLearningEnabled', 'memoryAutoConsolidateEnabled', 'dreamsEnabled', 'dreamsInterval', " +
            "'dreamsThreadLookbackCount', 'dreamsAutoApply', 'defaultApprovalPolicy', 'toolsLspEnabled', " +
            "is required.";

        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.WorkspaceConfigUpdate);
        if (!request.Message.Params.HasValue || request.Message.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams(requiredFieldMessage);

        var paramsElement = request.Message.Params.Value;
        var hasProviderId = TryGetCaseInsensitiveProperty(paramsElement, "providerId", out var providerIdEl);
        var hasProviderPreferences = TryGetCaseInsensitiveProperty(
            paramsElement,
            "providerPreferences",
            out var providerPreferencesEl);
        var hasWelcomeSuggestionsEnabled = TryGetCaseInsensitiveProperty(
            paramsElement,
            "welcomeSuggestionsEnabled",
            out var welcomeSuggestionsEnabledEl);
        var hasSkillsSelfLearningEnabled = TryGetCaseInsensitiveProperty(
            paramsElement,
            "skillsSelfLearningEnabled",
            out var skillsSelfLearningEnabledEl);
        var hasMemoryAutoConsolidateEnabled = TryGetCaseInsensitiveProperty(
            paramsElement,
            "memoryAutoConsolidateEnabled",
            out var memoryAutoConsolidateEnabledEl);
        var hasDreamsEnabled = TryGetCaseInsensitiveProperty(
            paramsElement,
            "dreamsEnabled",
            out var dreamsEnabledEl);
        var hasDreamsInterval = TryGetCaseInsensitiveProperty(
            paramsElement,
            "dreamsInterval",
            out var dreamsIntervalEl);
        var hasDreamsThreadLookbackCount = TryGetCaseInsensitiveProperty(
            paramsElement,
            "dreamsThreadLookbackCount",
            out var dreamsThreadLookbackCountEl);
        var hasDreamsAutoApply = TryGetCaseInsensitiveProperty(
            paramsElement,
            "dreamsAutoApply",
            out var dreamsAutoApplyEl);
        var hasDefaultApprovalPolicy = TryGetCaseInsensitiveProperty(
            paramsElement,
            "defaultApprovalPolicy",
            out var defaultApprovalPolicyEl);
        var hasToolsLspEnabled = TryGetCaseInsensitiveProperty(
            paramsElement,
            "toolsLspEnabled",
            out var toolsLspEnabledEl);
        if (!hasProviderId
            && !hasProviderPreferences
            && !hasWelcomeSuggestionsEnabled
            && !hasSkillsSelfLearningEnabled
            && !hasMemoryAutoConsolidateEnabled
            && !hasDreamsEnabled
            && !hasDreamsInterval
            && !hasDreamsThreadLookbackCount
            && !hasDreamsAutoApply
            && !hasDefaultApprovalPolicy
            && !hasToolsLspEnabled)
        {
            throw AppServerErrors.InvalidParams(requiredFieldMessage);
        }

        var currentConfig = appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig();
        var providerId = hasProviderId ? NormalizeOptionalString(ParseNullableString(providerIdEl, "providerId")) : null;
        var providerPreferences = hasProviderPreferences
            ? ParseNullableProviderPreferences(providerPreferencesEl, "providerPreferences")
            : null;
        var welcomeSuggestionsEnabled = hasWelcomeSuggestionsEnabled
            ? ParseNullableBoolean(welcomeSuggestionsEnabledEl, "welcomeSuggestionsEnabled")
            : null;
        var skillsSelfLearningEnabled = hasSkillsSelfLearningEnabled
            ? ParseNullableBoolean(skillsSelfLearningEnabledEl, "skillsSelfLearningEnabled")
            : null;
        var memoryAutoConsolidateEnabled = hasMemoryAutoConsolidateEnabled
            ? ParseNullableBoolean(memoryAutoConsolidateEnabledEl, "memoryAutoConsolidateEnabled")
            : null;
        var dreamsEnabled = hasDreamsEnabled
            ? ParseNullableBoolean(dreamsEnabledEl, "dreamsEnabled")
            : null;
        var dreamsInterval = hasDreamsInterval
            ? ParseNullablePositiveTimeSpan(dreamsIntervalEl, "dreamsInterval")
            : null;
        var dreamsThreadLookbackCount = hasDreamsThreadLookbackCount
            ? ParseNullablePositiveInt32(dreamsThreadLookbackCountEl, "dreamsThreadLookbackCount")
            : null;
        var dreamsAutoApply = hasDreamsAutoApply
            ? ParseNullableBoolean(dreamsAutoApplyEl, "dreamsAutoApply")
            : null;
        var defaultApprovalPolicy = hasDefaultApprovalPolicy
            ? ParseNullableString(defaultApprovalPolicyEl, "defaultApprovalPolicy")
            : null;
        var toolsLspEnabled = hasToolsLspEnabled
            ? ParseNullableBoolean(toolsLspEnabledEl, "toolsLspEnabled")
            : null;
        var prospectiveProviderId = hasProviderId ? providerId : currentConfig.ProviderId;
        if (hasProviderPreferences && providerPreferences != null)
        {
            foreach (var (preferenceProviderId, preference) in providerPreferences)
                ValidatePreferenceForRuntime(currentConfig, preferenceProviderId, preference);
        }

        var saveResult = SaveWorkspaceCoreConfig(
            workspaceCraftPath!,
            hasProviderId ? providerId : null,
            hasProviderPreferences ? providerPreferences : null,
            welcomeSuggestionsEnabled,
            skillsSelfLearningEnabled,
            memoryAutoConsolidateEnabled,
            dreamsEnabled,
            dreamsInterval,
            dreamsThreadLookbackCount,
            dreamsAutoApply,
            hasDefaultApprovalPolicy ? NormalizeDefaultApprovalPolicy(defaultApprovalPolicy) : null,
            toolsLspEnabled,
            hasProviderId,
            hasProviderPreferences,
            hasWelcomeSuggestionsEnabled,
            hasSkillsSelfLearningEnabled,
            hasMemoryAutoConsolidateEnabled,
            hasDreamsEnabled,
            hasDreamsInterval,
            hasDreamsThreadLookbackCount,
            hasDreamsAutoApply,
            hasDefaultApprovalPolicy,
            hasToolsLspEnabled);

        var changedRegions = new List<string>();
        if (saveResult.ProviderIdChanged)
            changedRegions.Add(ConfigChangeRegions.WorkspaceProvider);
        if (saveResult.ProviderPreferencesChanged)
            changedRegions.Add(ConfigChangeRegions.WorkspaceProviderPreferences);
        if (saveResult.ProviderIdChanged
            || saveResult.ProviderPreferencesChanged)
        {
            runtimeConfig.RefreshCurrentLlmConfig();
            runtimeConfig.InvalidateThreadAgents();
        }
        if (saveResult.WelcomeSuggestionsChanged)
            changedRegions.Add(ConfigChangeRegions.WelcomeSuggestions);
        if (saveResult.SkillsSelfLearningChanged)
        {
            changedRegions.Add(ConfigChangeRegions.Skills);
            AppServerContextInvalidation.MarkSkills(contextPageManager);
        }
        if (saveResult.MemoryAutoConsolidateChanged)
        {
            changedRegions.Add(ConfigChangeRegions.Memory);
            runtimeConfig.RefreshCurrentMemoryConfig();
        }
        if (saveResult.DreamsChanged)
        {
            changedRegions.Add(ConfigChangeRegions.Memory);
            runtimeConfig.RefreshCurrentDreamsConfig();
            if (dreamsService != null)
            {
                if (appConfigMonitor?.Current.Dreams.Enabled == true)
                    await dreamsService.StartAsync(ct);
                else
                    await dreamsService.StopAsync(ct);
            }
        }
        if (saveResult.DefaultApprovalPolicyChanged)
        {
            changedRegions.Add(ConfigChangeRegions.WorkspaceDefaultApprovalPolicy);
            runtimeConfig.RefreshCurrentPermissionsConfig(saveResult.DefaultApprovalPolicy);
        }
        if (saveResult.ToolsLspEnabledChanged)
        {
            changedRegions.Add(ConfigChangeRegions.Lsp);
            runtimeConfig.RefreshCurrentLspConfig(saveResult.ToolsLspEnabled);
            await ReconnectEffectiveLspRuntimeAsync(ct);
        }
        if (changedRegions.Count > 0)
        {
            appConfigMonitor?.NotifyChanged(
                Protocol.AppServer.AppServerMethodNames.WorkspaceConfigUpdate,
                changedRegions);
        }

        return new Contract.WorkspaceConfigUpdateResult
        {
            ProviderId = saveResult.ProviderId,
            ProviderPreferences = saveResult.ProviderPreferences is null
                ? default
                : new Protocol.Optional<IReadOnlyDictionary<string, Contract.ModelPreference>?>(
                    saveResult.ProviderPreferences.ToDictionary(
                        static pair => pair.Key,
                        static pair => ThreadConfigurationContractMapper.ToContract(pair.Value),
                        StringComparer.Ordinal)),
            WelcomeSuggestionsEnabled = saveResult.WelcomeSuggestionsEnabled,
            SkillsSelfLearningEnabled = saveResult.SkillsSelfLearningEnabled,
            MemoryAutoConsolidateEnabled = saveResult.MemoryAutoConsolidateEnabled,
            DreamsEnabled = saveResult.DreamsEnabled,
            DreamsInterval = saveResult.DreamsInterval,
            DreamsThreadLookbackCount = saveResult.DreamsThreadLookbackCount,
            DreamsAutoApply = saveResult.DreamsAutoApply,
            DefaultApprovalPolicy = saveResult.DefaultApprovalPolicy,
            ToolsLspEnabled = saveResult.ToolsLspEnabled
        };
    }

    private Task<object?> HandleMemoryResetAsync(
        AppServerTypedRequest<Protocol.RpcEmpty> request,
        CancellationToken ct)
    {
        _ = ct;
        if (memoryStore == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.MemoryReset);
        if (request.Message.Params.HasValue
            && request.Message.Params.Value.ValueKind is not JsonValueKind.Null
                and not JsonValueKind.Object
                and not JsonValueKind.Undefined)
        {
            throw AppServerErrors.InvalidParams("memory/reset accepts omitted, null, or empty-object params.");
        }

        try
        {
            memoryStore.ClearAll();
            dreamStore?.ClearAll();
            AppServerContextInvalidation.MarkMemory(contextPageManager);
            if (!string.IsNullOrWhiteSpace(hostWorkspacePath))
                welcomeSuggestionService?.ClearWorkspaceCache(hostWorkspacePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw AppServerErrors.InternalError($"Failed to reset memory: {ex.Message}");
        }

        appConfigMonitor?.NotifyChanged(
            Protocol.AppServer.AppServerMethodNames.MemoryReset,
            [ConfigChangeRegions.Memory]);

        return Task.FromResult<object?>(new Contract.MemoryResetResult());
    }

    private SessionIdentity NormalizeIdentityWorkspace(SessionIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.WorkspacePath) && !string.IsNullOrEmpty(hostWorkspacePath))
            return identity with { WorkspacePath = hostWorkspacePath };
        return identity;
    }

    private static SessionIdentity ToDomain(Contract.SessionIdentity? identity)
    {
        if (identity is null)
            throw AppServerErrors.InvalidParams("'identity' is required.");
        return new SessionIdentity
        {
            ChannelName = identity.ChannelName,
            UserId = identity.UserId,
            WorkspacePath = identity.WorkspacePath ?? string.Empty,
            ChannelContext = identity.ChannelContext
        };
    }

    private static Contract.WelcomeSuggestionsResult ToContract(WelcomeSuggestionSnapshot value) => new()
    {
        Items = value.Items.Select(static item => new Contract.WelcomeSuggestionItem
        {
            Title = item.Title,
            Prompt = item.Prompt,
            Reason = item.Reason
        }).ToArray(),
        Source = value.Source,
        GeneratedAt = value.GeneratedAt,
        Fingerprint = value.Fingerprint
    };

    private static Contract.ConfigSchemaSection ToContract(ConfigSchemaSection value) => new()
    {
        Section = value.Section,
        Order = value.Order,
        Path = OmitIfNull<IReadOnlyList<string>>(value.Path),
        RootKey = OmitIfNull(value.RootKey),
        ItemFields = value.ItemFields is null
            ? default
            : new Protocol.Optional<IReadOnlyList<Contract.ConfigSchemaField>?>(
                value.ItemFields.Select(ToContract).ToArray()),
        Fields = value.Fields.Select(ToContract).ToArray()
    };

    private static Contract.ConfigSchemaField ToContract(ConfigSchemaField value) => new()
    {
        Key = value.Key,
        DisplayName = OmitIfNull(value.DisplayName),
        Type = value.Type,
        Sensitive = value.Sensitive,
        Options = OmitIfNull<IReadOnlyList<string>>(value.Options),
        Min = OmitIfNull(value.Min),
        Max = OmitIfNull(value.Max),
        Hint = OmitIfNull(value.Hint),
        Reload = JsonNamingPolicy.CamelCase.ConvertName(value.Reload.ToString()),
        SubsystemKey = OmitIfNull(value.SubsystemKey),
        DefaultValue = ToOptionalJson(value.DefaultValue)
    };

    private static Protocol.Optional<JsonElement?> ToOptionalJson(object? value)
    {
        if (value is null)
            return default;
        if (value is JsonElement element)
            return element.ValueKind == JsonValueKind.Undefined
                ? default
                : Protocol.Optional<JsonElement?>.FromValue(element.Clone());

        return Protocol.Optional<JsonElement?>.FromValue(
            JsonSerializer.SerializeToElement(value, AppConfig.SerializerOptions));
    }

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : new Protocol.Optional<T?>(value);

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private async Task ReconnectEffectiveLspRuntimeAsync(CancellationToken ct)
    {
        if (lspServerManager == null)
            return;

        ct.ThrowIfCancellationRequested();
        await lspServerManager.InitializeAsync(ct);
    }

    private static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static Dictionary<string, ModelPreference>? ParseNullableProviderPreferences(
        JsonElement element,
        string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be an object or null.");

        var result = new Dictionary<string, ModelPreference>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject())
        {
            var providerId = NormalizeOptionalString(prop.Name);
            if (providerId == null)
                continue;
            if (prop.Value.ValueKind == JsonValueKind.Null)
                continue;
            ModelPreference? preference;
            try
            {
                preference = prop.Value.Deserialize<ModelPreference>(AppConfig.SerializerOptions);
            }
            catch (JsonException ex)
            {
                throw AppServerErrors.InvalidParams($"'{fieldName}.{prop.Name}' is invalid: {ex.Message}");
            }

            if (preference == null || string.IsNullOrWhiteSpace(preference.Model)
                || string.Equals(preference.Model.Trim(), "default", StringComparison.OrdinalIgnoreCase))
            {
                throw AppServerErrors.InvalidParams($"'{fieldName}.{prop.Name}.model' is required.");
            }

            preference.Model = preference.Model.Trim();
            preference.Reasoning ??= new AppConfig.ReasoningConfig();
            preference.ContextWindow ??= new ModelPreferenceContextWindow();
            result[providerId] = preference;
        }

        return result;
    }

    private static Dictionary<string, ModelPreference> NormalizeProviderPreferences(
        IReadOnlyDictionary<string, ModelPreference>? providerPreferences)
    {
        var result = new Dictionary<string, ModelPreference>(StringComparer.Ordinal);
        if (providerPreferences == null)
            return result;
        foreach (var (rawProviderId, rawPreference) in providerPreferences)
        {
            var providerId = NormalizeOptionalString(rawProviderId);
            if (providerId == null || rawPreference == null || string.IsNullOrWhiteSpace(rawPreference.Model))
                continue;
            var preference = ModelPreferenceRules.Clone(rawPreference);
            preference.Model = preference.Model.Trim();
            result[providerId] = preference;
        }
        return result;
    }

    private static Dictionary<string, ModelPreference> ReadConfigProviderPreferences(JsonObject root)
    {
        var key = FindCaseInsensitiveKey(root, "ProviderPreferences");
        if (key == null || root[key] is not JsonObject obj)
            return new Dictionary<string, ModelPreference>(StringComparer.Ordinal);
        try
        {
            return NormalizeProviderPreferences(
                obj.Deserialize<Dictionary<string, ModelPreference>>(AppConfig.SerializerOptions));
        }
        catch (JsonException)
        {
            return new Dictionary<string, ModelPreference>(StringComparer.Ordinal);
        }
    }

    private static bool ProviderPreferencesEqual(
        IReadOnlyDictionary<string, ModelPreference> left,
        IReadOnlyDictionary<string, ModelPreference> right)
    {
        if (left.Count != right.Count)
            return false;
        foreach (var (providerId, preference) in left)
        {
            var other = right.FirstOrDefault(
                pair => string.Equals(pair.Key, providerId, StringComparison.OrdinalIgnoreCase)).Value;
            if (!ModelPreferenceRules.ValueEquals(preference, other))
                return false;
        }
        return true;
    }

    private static void WriteProviderPreferences(
        JsonObject root,
        IReadOnlyDictionary<string, ModelPreference> providerPreferences)
    {
        var existingKey = FindCaseInsensitiveKey(root, "ProviderPreferences");
        if (providerPreferences.Count == 0)
        {
            if (existingKey != null)
                root.Remove(existingKey);
            return;
        }
        var obj = new JsonObject();
        foreach (var (providerId, preference) in providerPreferences.OrderBy(
                     pair => pair.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            obj[providerId] = JsonSerializer.SerializeToNode(preference, AppConfig.SerializerOptions);
        }
        root[existingKey ?? "ProviderPreferences"] = obj;
    }

    private static void ValidatePreferenceForRuntime(
        AppConfig currentConfig,
        string providerId,
        ModelPreference preference)
    {
        AppServerRuntimeRequestValidator.ValidateReasoningForRuntime(
            currentConfig,
            providerId,
            preference.Model,
            preference.Reasoning);
        AppServerRuntimeRequestValidator.ValidateContextWindowForRuntime(
            currentConfig,
            providerId,
            preference.Model,
            new ThreadContextWindowConfig { Mode = preference.ContextWindow.Mode });
    }

    private static string? NormalizeDefaultApprovalPolicy(string? rawPolicy)
    {
        var trimmed = rawPolicy?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        return trimmed switch
        {
            "default" or "Default" => "default",
            "autoApprove" or "AutoApprove" => "autoApprove",
            _ => throw AppServerErrors.InvalidParams("'defaultApprovalPolicy' must be 'default', 'autoApprove', or null.")
        };
    }

    private static WorkspaceConfigSaveResult SaveWorkspaceCoreConfig(
        string workspaceCraftPath,
        string? providerId,
        Dictionary<string, ModelPreference>? providerPreferences,
        bool? welcomeSuggestionsEnabled,
        bool? skillsSelfLearningEnabled,
        bool? memoryAutoConsolidateEnabled,
        bool? dreamsEnabled,
        TimeSpan? dreamsInterval,
        int? dreamsThreadLookbackCount,
        bool? dreamsAutoApply,
        string? defaultApprovalPolicy,
        bool? toolsLspEnabled,
        bool updateProviderId,
        bool updateProviderPreferences,
        bool updateWelcomeSuggestionsEnabled,
        bool updateSkillsSelfLearningEnabled,
        bool updateMemoryAutoConsolidateEnabled,
        bool updateDreamsEnabled,
        bool updateDreamsInterval,
        bool updateDreamsThreadLookbackCount,
        bool updateDreamsAutoApply,
        bool updateDefaultApprovalPolicy,
        bool updateToolsLspEnabled)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);

        var providerIdKey = FindCaseInsensitiveKey(root, "ProviderId");
        var welcomeSection = GetOrCreateConfigSection(root, "WelcomeSuggestions", createIfMissing: updateWelcomeSuggestionsEnabled);
        var welcomeEnabledKey = welcomeSection == null ? null : FindCaseInsensitiveKey(welcomeSection, "Enabled");
        var skillsSection = GetOrCreateConfigSection(root, "Skills", createIfMissing: updateSkillsSelfLearningEnabled);
        var selfLearningSection = skillsSection == null
            ? null
            : GetOrCreateConfigSection(skillsSection, "SelfLearning", createIfMissing: updateSkillsSelfLearningEnabled);
        var selfLearningEnabledKey = selfLearningSection == null ? null : FindCaseInsensitiveKey(selfLearningSection, "Enabled");
        var memorySection = GetOrCreateConfigSection(root, "Memory", createIfMissing: updateMemoryAutoConsolidateEnabled);
        var memoryAutoConsolidateEnabledKey = memorySection == null ? null : FindCaseInsensitiveKey(memorySection, "AutoConsolidateEnabled");
        var dreamsSection = GetOrCreateConfigSection(
            root,
            "Dreams",
            createIfMissing: updateDreamsEnabled || updateDreamsInterval || updateDreamsThreadLookbackCount || updateDreamsAutoApply);
        var dreamsEnabledKey = dreamsSection == null ? null : FindCaseInsensitiveKey(dreamsSection, "Enabled");
        var dreamsIntervalKey = dreamsSection == null ? null : FindCaseInsensitiveKey(dreamsSection, "Interval");
        var dreamsThreadLookbackCountKey = dreamsSection == null ? null : FindCaseInsensitiveKey(dreamsSection, "ThreadLookbackCount");
        var dreamsAutoApplyKey = dreamsSection == null ? null : FindCaseInsensitiveKey(dreamsSection, "AutoApply");
        var permissionsSection = GetOrCreateConfigSection(root, "Permissions", createIfMissing: updateDefaultApprovalPolicy);
        var defaultApprovalPolicyKey = permissionsSection == null ? null : FindCaseInsensitiveKey(permissionsSection, "DefaultApprovalPolicy");
        var toolsSection = GetOrCreateConfigSection(root, "Tools", createIfMissing: updateToolsLspEnabled);
        var lspSection = toolsSection == null
            ? null
            : GetOrCreateConfigSection(toolsSection, "Lsp", createIfMissing: updateToolsLspEnabled);
        var toolsLspEnabledKey = lspSection == null ? null : FindCaseInsensitiveKey(lspSection, "Enabled");
        var existingProviderId = NormalizeOptionalString(ReadConfigStringValue(root, providerIdKey));
        var existingProviderPreferences = ReadConfigProviderPreferences(root);
        var existingWelcomeSuggestionsEnabled = ReadConfigBooleanValue(welcomeSection, welcomeEnabledKey);
        var existingSkillsSelfLearningEnabled = ReadConfigBooleanValue(selfLearningSection, selfLearningEnabledKey);
        var existingMemoryAutoConsolidateEnabled = ReadConfigBooleanValue(memorySection, memoryAutoConsolidateEnabledKey);
        var existingDreamsEnabled = ReadConfigBooleanValue(dreamsSection, dreamsEnabledKey);
        var existingDreamsInterval = ReadConfigTimeSpanValue(dreamsSection, dreamsIntervalKey);
        var existingDreamsThreadLookbackCount = ReadConfigIntegerValue(dreamsSection, dreamsThreadLookbackCountKey);
        var existingDreamsAutoApply = ReadConfigBooleanValue(dreamsSection, dreamsAutoApplyKey);
        var existingDefaultApprovalPolicy = NormalizeDefaultApprovalPolicy(ReadConfigStringValue(permissionsSection, defaultApprovalPolicyKey));
        var existingToolsLspEnabled = ReadConfigBooleanValue(lspSection, toolsLspEnabledKey);
        var providerIdChanged = updateProviderId && !string.Equals(existingProviderId, providerId, StringComparison.Ordinal);
        var normalizedProviderPreferences = updateProviderPreferences
            ? NormalizeProviderPreferences(providerPreferences)
            : existingProviderPreferences;
        var providerPreferencesChanged = updateProviderPreferences
            && !ProviderPreferencesEqual(existingProviderPreferences, normalizedProviderPreferences);
        var welcomeSuggestionsChanged = updateWelcomeSuggestionsEnabled
            && existingWelcomeSuggestionsEnabled != welcomeSuggestionsEnabled;
        var skillsSelfLearningChanged = updateSkillsSelfLearningEnabled
            && existingSkillsSelfLearningEnabled != skillsSelfLearningEnabled;
        var memoryAutoConsolidateChanged = updateMemoryAutoConsolidateEnabled
            && existingMemoryAutoConsolidateEnabled != memoryAutoConsolidateEnabled;
        var dreamsEnabledChanged = updateDreamsEnabled
            && existingDreamsEnabled != dreamsEnabled;
        var dreamsIntervalChanged = updateDreamsInterval
            && existingDreamsInterval != dreamsInterval;
        var dreamsThreadLookbackCountChanged = updateDreamsThreadLookbackCount
            && existingDreamsThreadLookbackCount != dreamsThreadLookbackCount;
        var dreamsAutoApplyChanged = updateDreamsAutoApply
            && existingDreamsAutoApply != dreamsAutoApply;
        var defaultApprovalPolicyChanged = updateDefaultApprovalPolicy
            && !string.Equals(existingDefaultApprovalPolicy, defaultApprovalPolicy, StringComparison.Ordinal);
        var toolsLspEnabledChanged = updateToolsLspEnabled
            && existingToolsLspEnabled != toolsLspEnabled;
        if (updateProviderId)
            UpsertOrRemoveConfigValue(root, providerIdKey, "ProviderId", providerId);
        if (updateProviderPreferences)
            WriteProviderPreferences(root, normalizedProviderPreferences);
        if (updateWelcomeSuggestionsEnabled)
        {
            var section = GetOrCreateConfigSection(root, "WelcomeSuggestions", createIfMissing: true)!;
            var sectionEnabledKey = FindCaseInsensitiveKey(section, "Enabled");
            UpsertOrRemoveConfigValue(section, sectionEnabledKey, "Enabled", welcomeSuggestionsEnabled);
            RemoveConfigSectionIfEmpty(root, "WelcomeSuggestions");
        }
        if (updateSkillsSelfLearningEnabled)
        {
            var skills = GetOrCreateConfigSection(root, "Skills", createIfMissing: true)!;
            var selfLearning = GetOrCreateConfigSection(skills, "SelfLearning", createIfMissing: true)!;
            var selfLearningEnabledExistingKey = FindCaseInsensitiveKey(selfLearning, "Enabled");
            UpsertOrRemoveConfigValue(selfLearning, selfLearningEnabledExistingKey, "Enabled", skillsSelfLearningEnabled);
            RemoveConfigSectionIfEmpty(skills, "SelfLearning");
            RemoveConfigSectionIfEmpty(root, "Skills");
        }
        if (updateMemoryAutoConsolidateEnabled)
        {
            var memory = GetOrCreateConfigSection(root, "Memory", createIfMissing: true)!;
            var autoConsolidateExistingKey = FindCaseInsensitiveKey(memory, "AutoConsolidateEnabled");
            UpsertOrRemoveConfigValue(memory, autoConsolidateExistingKey, "AutoConsolidateEnabled", memoryAutoConsolidateEnabled);
            RemoveConfigSectionIfEmpty(root, "Memory");
        }
        if (updateDreamsEnabled || updateDreamsInterval || updateDreamsThreadLookbackCount || updateDreamsAutoApply)
        {
            var dreams = GetOrCreateConfigSection(root, "Dreams", createIfMissing: true)!;
            if (updateDreamsEnabled)
            {
                var enabledExistingKey = FindCaseInsensitiveKey(dreams, "Enabled");
                UpsertOrRemoveConfigValue(dreams, enabledExistingKey, "Enabled", dreamsEnabled);
            }
            if (updateDreamsInterval)
            {
                var intervalExistingKey = FindCaseInsensitiveKey(dreams, "Interval");
                UpsertOrRemoveConfigValue(dreams, intervalExistingKey, "Interval", FormatTimeSpanForConfig(dreamsInterval));
            }
            if (updateDreamsThreadLookbackCount)
            {
                var threadLookbackExistingKey = FindCaseInsensitiveKey(dreams, "ThreadLookbackCount");
                UpsertOrRemoveConfigValue(dreams, threadLookbackExistingKey, "ThreadLookbackCount", dreamsThreadLookbackCount);
            }
            if (updateDreamsAutoApply)
            {
                var autoApplyExistingKey = FindCaseInsensitiveKey(dreams, "AutoApply");
                UpsertOrRemoveConfigValue(dreams, autoApplyExistingKey, "AutoApply", dreamsAutoApply);
            }
            RemoveConfigSectionIfEmpty(root, "Dreams");
        }
        if (updateDefaultApprovalPolicy)
        {
            var permissions = GetOrCreateConfigSection(root, "Permissions", createIfMissing: true)!;
            var defaultApprovalPolicyExistingKey = FindCaseInsensitiveKey(permissions, "DefaultApprovalPolicy");
            UpsertOrRemoveConfigValue(permissions, defaultApprovalPolicyExistingKey, "DefaultApprovalPolicy", defaultApprovalPolicy);
            RemoveConfigSectionIfEmpty(root, "Permissions");
        }
        if (updateToolsLspEnabled)
        {
            var tools = GetOrCreateConfigSection(root, "Tools", createIfMissing: true)!;
            var lsp = GetOrCreateConfigSection(tools, "Lsp", createIfMissing: true)!;
            var enabledExistingKey = FindCaseInsensitiveKey(lsp, "Enabled");
            UpsertOrRemoveConfigValue(lsp, enabledExistingKey, "Enabled", toolsLspEnabled);
            RemoveConfigSectionIfEmpty(tools, "Lsp");
            RemoveConfigSectionIfEmpty(root, "Tools");
        }
        if (providerIdChanged
            || providerPreferencesChanged
            || welcomeSuggestionsChanged
            || skillsSelfLearningChanged
            || memoryAutoConsolidateChanged
            || dreamsEnabledChanged
            || dreamsIntervalChanged
            || dreamsThreadLookbackCountChanged
            || dreamsAutoApplyChanged
            || defaultApprovalPolicyChanged
            || toolsLspEnabledChanged)
        {
            WriteConfigObject(configPath, root);
        }

        return new WorkspaceConfigSaveResult
        {
            ProviderId = updateProviderId ? providerId : existingProviderId,
            ProviderPreferences = normalizedProviderPreferences.Count == 0
                ? null
                : normalizedProviderPreferences.ToDictionary(
                    pair => pair.Key,
                    pair => ModelPreferenceRules.Clone(pair.Value),
                    StringComparer.Ordinal),
            WelcomeSuggestionsEnabled = updateWelcomeSuggestionsEnabled
                ? welcomeSuggestionsEnabled
                : existingWelcomeSuggestionsEnabled,
            SkillsSelfLearningEnabled = updateSkillsSelfLearningEnabled
                ? skillsSelfLearningEnabled
                : existingSkillsSelfLearningEnabled,
            MemoryAutoConsolidateEnabled = updateMemoryAutoConsolidateEnabled
                ? memoryAutoConsolidateEnabled
                : existingMemoryAutoConsolidateEnabled,
            DreamsEnabled = updateDreamsEnabled
                ? dreamsEnabled
                : existingDreamsEnabled,
            DreamsInterval = updateDreamsInterval
                ? FormatTimeSpanForConfig(dreamsInterval)
                : FormatTimeSpanForConfig(existingDreamsInterval),
            DreamsThreadLookbackCount = updateDreamsThreadLookbackCount
                ? dreamsThreadLookbackCount
                : existingDreamsThreadLookbackCount,
            DreamsAutoApply = updateDreamsAutoApply
                ? dreamsAutoApply
                : existingDreamsAutoApply,
            DefaultApprovalPolicy = updateDefaultApprovalPolicy
                ? defaultApprovalPolicy
                : existingDefaultApprovalPolicy,
            ToolsLspEnabled = updateToolsLspEnabled
                ? toolsLspEnabled
                : existingToolsLspEnabled,
            ProviderIdChanged = providerIdChanged,
            ProviderPreferencesChanged = providerPreferencesChanged,
            WelcomeSuggestionsChanged = welcomeSuggestionsChanged,
            SkillsSelfLearningChanged = skillsSelfLearningChanged,
            MemoryAutoConsolidateChanged = memoryAutoConsolidateChanged,
            DreamsChanged = dreamsEnabledChanged || dreamsIntervalChanged || dreamsThreadLookbackCountChanged || dreamsAutoApplyChanged,
            DefaultApprovalPolicyChanged = defaultApprovalPolicyChanged,
            ToolsLspEnabledChanged = toolsLspEnabledChanged
        };
    }

    private static JsonObject LoadWorkspaceConfigObject(string configPath)
        => WorkspaceConfigEditor.LoadObject(configPath);

    private static string? FindCaseInsensitiveKey(JsonObject obj, string expectedKey)
        => WorkspaceConfigEditor.FindCaseInsensitiveKey(obj, expectedKey);

    private static void UpsertOrRemoveConfigValue(
        JsonObject root,
        string? existingKey,
        string canonicalKey,
        string? value)
        => WorkspaceConfigEditor.UpsertOrRemoveValue(root, existingKey, canonicalKey, value);

    private static void UpsertOrRemoveConfigValue(
        JsonObject root,
        string? existingKey,
        string canonicalKey,
        bool? value)
        => WorkspaceConfigEditor.UpsertOrRemoveValue(root, existingKey, canonicalKey, value);

    private static void UpsertOrRemoveConfigValue(
        JsonObject root,
        string? existingKey,
        string canonicalKey,
        int? value)
        => WorkspaceConfigEditor.UpsertOrRemoveValue(root, existingKey, canonicalKey, value);

    private static void WriteConfigObject(string configPath, JsonObject root)
        => WorkspaceConfigEditor.WriteObject(configPath, root);

    private static string? ParseNullableString(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be a string or null.")
        };
    }

    private static bool? ParseNullableBoolean(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be a boolean or null.")
        };
    }

    private static int? ParseNullablePositiveInt32(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out var value)
            || value <= 0)
        {
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a positive integer or null.");
        }

        return value;
    }

    private static TimeSpan? ParseNullablePositiveTimeSpan(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.String)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a positive TimeSpan string or null.");

        var raw = element.GetString();
        if (string.IsNullOrWhiteSpace(raw)
            || !TryParseWireTimeSpan(raw.Trim(), out var value)
            || value <= TimeSpan.Zero)
        {
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a positive TimeSpan string or null.");
        }

        return value;
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

    private static bool? ReadConfigBooleanValue(JsonObject? root, string? key)
    {
        if (root == null || string.IsNullOrEmpty(key))
            return null;
        if (!root.TryGetPropertyValue(key, out var node) || node == null)
            return null;
        if (node is not JsonValue value)
            return null;
        return value.TryGetValue<bool>(out var result) ? result : null;
    }

    private static int? ReadConfigIntegerValue(JsonObject? root, string? key)
    {
        if (root == null || string.IsNullOrEmpty(key))
            return null;
        if (!root.TryGetPropertyValue(key, out var node) || node == null)
            return null;
        if (node is not JsonValue value)
            return null;
        return value.TryGetValue<int>(out var result) ? result : null;
    }

    private static TimeSpan? ReadConfigTimeSpanValue(JsonObject? root, string? key)
    {
        var raw = ReadConfigStringValue(root, key);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return TryParseWireTimeSpan(raw.Trim(), out var result) && result > TimeSpan.Zero
            ? result
            : null;
    }

    private static string? FormatTimeSpanForConfig(TimeSpan? value) =>
        value.HasValue ? value.Value.ToString("c", CultureInfo.InvariantCulture) : null;

    private static bool TryParseWireTimeSpan(string raw, out TimeSpan value)
    {
        if (TryParseTotalHoursTimeSpan(raw, out value))
            return true;
        return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseTotalHoursTimeSpan(string raw, out TimeSpan value)
    {
        value = default;
        var parts = raw.Split(':');
        if (parts.Length != 3)
            return false;

        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || hours < 0
            || minutes is < 0 or > 59
            || seconds is < 0 or > 59)
        {
            return false;
        }

        value = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static JsonObject? GetOrCreateConfigSection(JsonObject root, string canonicalKey, bool createIfMissing)
    {
        var existingKey = FindCaseInsensitiveKey(root, canonicalKey);
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

    private static void RemoveConfigSectionIfEmpty(JsonObject root, string canonicalKey)
    {
        var existingKey = FindCaseInsensitiveKey(root, canonicalKey);
        if (existingKey == null)
            return;

        if (root[existingKey] is JsonObject obj && obj.Count == 0)
            root.Remove(existingKey);
    }

    private static void RemoveConfigSection(JsonObject root, string canonicalKey)
    {
        var existingKey = FindCaseInsensitiveKey(root, canonicalKey);
        if (existingKey != null)
            root.Remove(existingKey);
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

    private sealed class WorkspaceConfigSaveResult
    {
        public string? ProviderId { get; init; }

        public Dictionary<string, ModelPreference>? ProviderPreferences { get; init; }

        public bool? WelcomeSuggestionsEnabled { get; init; }

        public bool? SkillsSelfLearningEnabled { get; init; }

        public bool? MemoryAutoConsolidateEnabled { get; init; }

        public bool? DreamsEnabled { get; init; }

        public string? DreamsInterval { get; init; }

        public int? DreamsThreadLookbackCount { get; init; }

        public bool? DreamsAutoApply { get; init; }

        public string? DefaultApprovalPolicy { get; init; }

        public bool? ToolsLspEnabled { get; init; }

        public bool ProviderIdChanged { get; init; }

        public bool ProviderPreferencesChanged { get; init; }

        public bool WelcomeSuggestionsChanged { get; init; }

        public bool SkillsSelfLearningChanged { get; init; }

        public bool MemoryAutoConsolidateChanged { get; init; }

        public bool DreamsChanged { get; init; }

        public bool DefaultApprovalPolicyChanged { get; init; }

        public bool ToolsLspEnabledChanged { get; init; }
    }
}
