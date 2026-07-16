using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;
using DotCraft.Lsp;
using DotCraft.Memory;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;

internal sealed class WorkspaceRequestHandler(
    ICommitMessageSuggestService? commitMessageSuggest,
    IWelcomeSuggestionService? welcomeSuggestionService,
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
        table.Map(AppServerMethods.WorkspaceCommitMessageSuggest, HandleWorkspaceCommitMessageSuggestAsync);
        table.Map(AppServerMethods.WelcomeSuggestions, HandleWelcomeSuggestionsAsync);
        table.Map(AppServerMethods.WorkspaceConfigSchema, HandleWorkspaceConfigSchemaAsync);
        table.Map(AppServerMethods.WorkspaceConfigUpdate, HandleWorkspaceConfigUpdateAsync);
        table.Map(AppServerMethods.MemoryReset, HandleMemoryResetAsync);
    }

    private async Task<object?> HandleWorkspaceCommitMessageSuggestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (commitMessageSuggest == null)
            throw AppServerErrors.InvalidRequest("Commit message suggestion is not available on this connection.");

        var p = AppServerParams.Get<WorkspaceCommitMessageSuggestParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (p.Paths is not { Length: > 0 })
            throw AppServerErrors.InvalidParams("'paths' must contain at least one file path.");

        try
        {
            return await commitMessageSuggest.SuggestAsync(p, ct);
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

    private async Task<object?> HandleWelcomeSuggestionsAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (welcomeSuggestionService == null)
            throw AppServerErrors.InvalidRequest("Welcome suggestions are not available on this connection.");

        var p = AppServerParams.Get<WelcomeSuggestionsParams>(msg);
        p.Identity = NormalizeIdentityWorkspace(p.Identity);
        if (string.IsNullOrWhiteSpace(p.Identity.WorkspacePath))
            throw AppServerErrors.InvalidParams("'identity.workspacePath' is required.");

        try
        {
            return await welcomeSuggestionService.SuggestAsync(p, ct);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidRequest(ex.Message);
        }
    }

    private Task<object?> HandleWorkspaceConfigSchemaAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        _ = AppServerParams.Get<WorkspaceConfigSchemaParams>(msg);
        if (configSchema.Count == 0)
            throw AppServerErrors.MethodNotFound(AppServerMethods.WorkspaceConfigSchema);

        return Task.FromResult<object?>(new WorkspaceConfigSchemaResult
        {
            Sections = [.. configSchema]
        });
    }

    private async Task<object?> HandleWorkspaceConfigUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        const string requiredFieldMessage =
            "At least one of 'providerId', 'providerModels', 'welcomeSuggestionsEnabled', " +
            "'skillsSelfLearningEnabled', 'memoryAutoConsolidateEnabled', 'dreamsEnabled', 'dreamsInterval', " +
            "'dreamsThreadLookbackCount', 'dreamsAutoApply', 'defaultApprovalPolicy', 'toolsLspEnabled', " +
            "'reasoning', 'speed', or 'contextWindow' is required.";

        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.WorkspaceConfigUpdate);
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams(requiredFieldMessage);

        var hasProviderId = TryGetCaseInsensitiveProperty(msg.Params.Value, "providerId", out var providerIdEl);
        var hasModel = TryGetCaseInsensitiveProperty(msg.Params.Value, "model", out _);
        var hasProviderModels = TryGetCaseInsensitiveProperty(msg.Params.Value, "providerModels", out var providerModelsEl);
        var hasApiKey = TryGetCaseInsensitiveProperty(msg.Params.Value, "apiKey", out var apiKeyEl);
        var hasEndPoint = TryGetCaseInsensitiveProperty(msg.Params.Value, "endPoint", out var endPointEl);
        if (hasApiKey || hasEndPoint)
            throw AppServerErrors.InvalidParams(
                "'apiKey' and 'endPoint' are no longer accepted by workspace/config/update. Use provider/create or provider/update.");
        if (hasModel)
            throw AppServerErrors.InvalidParams(
                "'model' is no longer accepted by workspace/config/update. Set the selected provider's entry in 'providerModels'.");
        var hasWelcomeSuggestionsEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "welcomeSuggestionsEnabled",
            out var welcomeSuggestionsEnabledEl);
        var hasSkillsSelfLearningEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "skillsSelfLearningEnabled",
            out var skillsSelfLearningEnabledEl);
        var hasMemoryAutoConsolidateEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "memoryAutoConsolidateEnabled",
            out var memoryAutoConsolidateEnabledEl);
        var hasDreamsEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "dreamsEnabled",
            out var dreamsEnabledEl);
        var hasDreamsInterval = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "dreamsInterval",
            out var dreamsIntervalEl);
        var hasDreamsThreadLookbackCount = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "dreamsThreadLookbackCount",
            out var dreamsThreadLookbackCountEl);
        var hasDreamsAutoApply = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "dreamsAutoApply",
            out var dreamsAutoApplyEl);
        var hasDefaultApprovalPolicy = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "defaultApprovalPolicy",
            out var defaultApprovalPolicyEl);
        var hasToolsLspEnabled = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "toolsLspEnabled",
            out var toolsLspEnabledEl);
        var hasReasoning = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "reasoning",
            out var reasoningEl);
        var hasSpeed = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "speed",
            out var speedEl);
        var hasContextWindow = TryGetCaseInsensitiveProperty(
            msg.Params.Value,
            "contextWindow",
            out var contextWindowEl);
        if (!hasProviderId
            && !hasProviderModels
            && !hasWelcomeSuggestionsEnabled
            && !hasSkillsSelfLearningEnabled
            && !hasMemoryAutoConsolidateEnabled
            && !hasDreamsEnabled
            && !hasDreamsInterval
            && !hasDreamsThreadLookbackCount
            && !hasDreamsAutoApply
            && !hasDefaultApprovalPolicy
            && !hasToolsLspEnabled
            && !hasReasoning
            && !hasSpeed
            && !hasContextWindow)
        {
            throw AppServerErrors.InvalidParams(requiredFieldMessage);
        }

        var currentConfig = appConfigMonitor?.Current ?? workspaceConfig.LoadCurrentMergedConfig();
        var providerId = hasProviderId ? NormalizeOptionalString(ParseNullableString(providerIdEl, "providerId")) : null;
        var providerModels = hasProviderModels ? ParseNullableProviderModels(providerModelsEl, "providerModels") : null;
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
        var reasoning = hasReasoning
            ? ParseNullableReasoningConfig(reasoningEl, "reasoning", currentConfig.Reasoning)
            : null;
        var speed = hasSpeed
            ? ParseInferenceSpeed(speedEl, "speed")
            : currentConfig.Speed;
        var contextWindow = hasContextWindow
            ? ParseNullableWorkspaceContextWindowConfig(contextWindowEl, "contextWindow")
            : null;

        var prospectiveProviderId = hasProviderId ? providerId : currentConfig.ProviderId;
        var prospectiveModel = ResolveProspectiveWorkspaceModel(
            currentConfig,
            workspaceConfig.PersonalConfigPath,
            prospectiveProviderId,
            hasProviderModels ? providerModels : null,
            hasProviderModels);
        if (hasReasoning && reasoning != null)
        {
            AppServerRuntimeRequestValidator.ValidateReasoningForRuntime(
                currentConfig,
                prospectiveProviderId,
                prospectiveModel,
                reasoning);
        }
        if (hasContextWindow && contextWindow != null)
        {
            AppServerRuntimeRequestValidator.ValidateContextWindowForRuntime(
                currentConfig,
                prospectiveProviderId,
                prospectiveModel,
                contextWindow);
        }

        var saveResult = SaveWorkspaceCoreConfig(
            workspaceCraftPath!,
            hasProviderId ? providerId : null,
            hasProviderModels ? providerModels : null,
            welcomeSuggestionsEnabled,
            skillsSelfLearningEnabled,
            memoryAutoConsolidateEnabled,
            dreamsEnabled,
            dreamsInterval,
            dreamsThreadLookbackCount,
            dreamsAutoApply,
            hasDefaultApprovalPolicy ? NormalizeDefaultApprovalPolicy(defaultApprovalPolicy) : null,
            toolsLspEnabled,
            reasoning,
            contextWindow,
            hasProviderId,
            hasProviderModels,
            hasWelcomeSuggestionsEnabled,
            hasSkillsSelfLearningEnabled,
            hasMemoryAutoConsolidateEnabled,
            hasDreamsEnabled,
            hasDreamsInterval,
            hasDreamsThreadLookbackCount,
            hasDreamsAutoApply,
            hasDefaultApprovalPolicy,
            hasToolsLspEnabled,
            hasReasoning,
            hasContextWindow);
        var speedChanged = hasSpeed && SaveWorkspaceSpeed(workspaceCraftPath!, speed);

        var changedRegions = new List<string>();
        if (saveResult.ProviderIdChanged)
            changedRegions.Add(ConfigChangeRegions.WorkspaceProvider);
        if (saveResult.ProviderModelsChanged)
            changedRegions.Add(ConfigChangeRegions.WorkspaceModel);
        if (saveResult.ProviderIdChanged
            || saveResult.ProviderModelsChanged)
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
        if (saveResult.ReasoningChanged)
        {
            changedRegions.Add(ConfigChangeRegions.WorkspaceReasoning);
            runtimeConfig.RefreshCurrentReasoningConfig();
            runtimeConfig.InvalidateThreadAgents();
        }
        if (speedChanged)
        {
            changedRegions.Add(ConfigChangeRegions.WorkspaceSpeed);
            runtimeConfig.RefreshCurrentSpeedConfig();
            runtimeConfig.InvalidateThreadAgents();
        }
        if (saveResult.ContextWindowChanged)
        {
            changedRegions.Add(ConfigChangeRegions.WorkspaceContextWindow);
            runtimeConfig.RefreshCurrentContextWindowConfig();
        }
        if (changedRegions.Count > 0)
        {
            appConfigMonitor?.NotifyChanged(
                AppServerMethods.WorkspaceConfigUpdate,
                changedRegions);
        }

        return new WorkspaceConfigUpdateResult
        {
            ProviderId = saveResult.ProviderId,
            ProviderModels = saveResult.ProviderModels,
            WelcomeSuggestionsEnabled = saveResult.WelcomeSuggestionsEnabled,
            SkillsSelfLearningEnabled = saveResult.SkillsSelfLearningEnabled,
            MemoryAutoConsolidateEnabled = saveResult.MemoryAutoConsolidateEnabled,
            DreamsEnabled = saveResult.DreamsEnabled,
            DreamsInterval = saveResult.DreamsInterval,
            DreamsThreadLookbackCount = saveResult.DreamsThreadLookbackCount,
            DreamsAutoApply = saveResult.DreamsAutoApply,
            DefaultApprovalPolicy = saveResult.DefaultApprovalPolicy,
            ToolsLspEnabled = saveResult.ToolsLspEnabled,
            Reasoning = CloneNullableReasoningConfig(saveResult.Reasoning),
            Speed = hasSpeed ? speed : currentConfig.Speed,
            ContextWindow = CloneNullableContextWindowConfig(saveResult.ContextWindow)
        };
    }

    private static InferenceSpeed ParseInferenceSpeed(JsonElement element, string fieldName)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be 'standard' or 'fast'.");
        return element.GetString()?.Trim().ToLowerInvariant() switch
        {
            "standard" => InferenceSpeed.Standard,
            "fast" => InferenceSpeed.Fast,
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be 'standard' or 'fast'.")
        };
    }

    private static bool SaveWorkspaceSpeed(string workspaceCraftPath, InferenceSpeed speed)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);
        var key = FindCaseInsensitiveKey(root, "Speed");
        var existing = ReadConfigStringValue(root, key);
        var canonical = speed == InferenceSpeed.Fast ? "Fast" : "Standard";
        if (string.Equals(existing, canonical, StringComparison.OrdinalIgnoreCase))
            return false;
        UpsertOrRemoveConfigValue(root, key, "Speed", canonical);
        WriteConfigObject(configPath, root);
        return true;
    }

    private Task<object?> HandleMemoryResetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        if (memoryStore == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.MemoryReset);
        if (msg.Params.HasValue
            && msg.Params.Value.ValueKind is not JsonValueKind.Null
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
            AppServerMethods.MemoryReset,
            [ConfigChangeRegions.Memory]);

        return Task.FromResult<object?>(new MemoryResetResult());
    }

    private SessionIdentity NormalizeIdentityWorkspace(SessionIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.WorkspacePath) && !string.IsNullOrEmpty(hostWorkspacePath))
            return identity with { WorkspacePath = hostWorkspacePath };
        return identity;
    }

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

    private static string? NormalizeWorkspaceModel(string? rawModel)
    {
        var trimmed = rawModel?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static Dictionary<string, string>? ParseNullableProviderModels(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be an object or null.");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject())
        {
            var providerId = NormalizeOptionalString(prop.Name);
            if (providerId == null)
                continue;
            if (prop.Value.ValueKind != JsonValueKind.String && prop.Value.ValueKind != JsonValueKind.Null)
                throw AppServerErrors.InvalidParams($"'{fieldName}.{prop.Name}' must be a string or null.");
            var model = prop.Value.ValueKind == JsonValueKind.String
                ? NormalizeWorkspaceModel(prop.Value.GetString())
                : null;
            if (model == null)
                continue;
            result[providerId] = model;
        }

        return result;
    }

    private static Dictionary<string, string> NormalizeProviderModels(Dictionary<string, string>? providerModels)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (providerModels == null)
            return result;
        foreach (var kv in providerModels)
        {
            var providerId = NormalizeOptionalString(kv.Key);
            if (providerId == null)
                continue;
            var model = NormalizeWorkspaceModel(kv.Value);
            if (model == null)
                continue;
            result[providerId] = model;
        }

        return result;
    }

    private static string? ResolveProspectiveWorkspaceModel(
        AppConfig currentConfig,
        string personalConfigPath,
        string? providerId,
        Dictionary<string, string>? workspaceProviderModels,
        bool updateProviderModels)
    {
        if (!updateProviderModels)
            return ModelProviderResolver.ResolveConfiguredModel(currentConfig, providerId);

        var effectiveModels = new Dictionary<string, string>(
            AppConfig.Load(personalConfigPath).ProviderModels,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in NormalizeProviderModels(workspaceProviderModels))
            effectiveModels[key] = value;

        return ModelProviderResolver.ResolveConfiguredModel(
            new AppConfig
            {
                ProviderId = providerId ?? string.Empty,
                ProviderModels = effectiveModels
            },
            providerId);
    }

    private static Dictionary<string, string> ReadConfigProviderModels(JsonObject root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var key = FindCaseInsensitiveKey(root, "ProviderModels");
        if (key == null || root[key] is not JsonObject obj)
            return result;

        foreach (var kv in obj)
        {
            var providerId = NormalizeOptionalString(kv.Key);
            if (providerId == null)
                continue;
            if (kv.Value is not JsonValue value || !value.TryGetValue<string>(out var rawModel))
                continue;
            var model = NormalizeWorkspaceModel(rawModel);
            if (model == null)
                continue;
            result[providerId] = model;
        }

        return result;
    }

    private static bool ProviderModelsEqual(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a.Count != b.Count)
            return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var other) || !string.Equals(other, kv.Value, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static void WriteProviderModels(JsonObject root, Dictionary<string, string> providerModels)
    {
        var existingKey = FindCaseInsensitiveKey(root, "ProviderModels");
        if (providerModels.Count == 0)
        {
            if (existingKey != null)
                root.Remove(existingKey);
            return;
        }

        var obj = new JsonObject();
        foreach (var kv in providerModels)
            obj[kv.Key] = JsonValue.Create(kv.Value);
        root[existingKey ?? "ProviderModels"] = obj;
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
        Dictionary<string, string>? providerModels,
        bool? welcomeSuggestionsEnabled,
        bool? skillsSelfLearningEnabled,
        bool? memoryAutoConsolidateEnabled,
        bool? dreamsEnabled,
        TimeSpan? dreamsInterval,
        int? dreamsThreadLookbackCount,
        bool? dreamsAutoApply,
        string? defaultApprovalPolicy,
        bool? toolsLspEnabled,
        AppConfig.ReasoningConfig? reasoning,
        ThreadContextWindowConfig? contextWindow,
        bool updateProviderId,
        bool updateProviderModels,
        bool updateWelcomeSuggestionsEnabled,
        bool updateSkillsSelfLearningEnabled,
        bool updateMemoryAutoConsolidateEnabled,
        bool updateDreamsEnabled,
        bool updateDreamsInterval,
        bool updateDreamsThreadLookbackCount,
        bool updateDreamsAutoApply,
        bool updateDefaultApprovalPolicy,
        bool updateToolsLspEnabled,
        bool updateReasoning,
        bool updateContextWindow)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);
        var legacyLanguageKey = FindCaseInsensitiveKey(root, "Language");
        var legacyLanguageRemoved = legacyLanguageKey != null && root.Remove(legacyLanguageKey);
        var legacyModelKey = FindCaseInsensitiveKey(root, "Model");
        var legacyModelRemoved = legacyModelKey != null && root.Remove(legacyModelKey);

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
        var reasoningSection = GetOrCreateConfigSection(root, "Reasoning", createIfMissing: updateReasoning && reasoning != null);
        var compactionSection = GetOrCreateConfigSection(root, "Compaction", createIfMissing: updateContextWindow && contextWindow != null);
        var contextWindowModeKey = compactionSection == null ? null : FindCaseInsensitiveKey(compactionSection, "ContextWindowMode");

        var existingProviderId = NormalizeOptionalString(ReadConfigStringValue(root, providerIdKey));
        var existingProviderModels = ReadConfigProviderModels(root);
        var existingWelcomeSuggestionsEnabled = ReadConfigBooleanValue(welcomeSection, welcomeEnabledKey);
        var existingSkillsSelfLearningEnabled = ReadConfigBooleanValue(selfLearningSection, selfLearningEnabledKey);
        var existingMemoryAutoConsolidateEnabled = ReadConfigBooleanValue(memorySection, memoryAutoConsolidateEnabledKey);
        var existingDreamsEnabled = ReadConfigBooleanValue(dreamsSection, dreamsEnabledKey);
        var existingDreamsInterval = ReadConfigTimeSpanValue(dreamsSection, dreamsIntervalKey);
        var existingDreamsThreadLookbackCount = ReadConfigIntegerValue(dreamsSection, dreamsThreadLookbackCountKey);
        var existingDreamsAutoApply = ReadConfigBooleanValue(dreamsSection, dreamsAutoApplyKey);
        var existingDefaultApprovalPolicy = NormalizeDefaultApprovalPolicy(ReadConfigStringValue(permissionsSection, defaultApprovalPolicyKey));
        var existingToolsLspEnabled = ReadConfigBooleanValue(lspSection, toolsLspEnabledKey);
        var existingReasoning = ReadConfigReasoningValue(reasoningSection);
        var existingContextWindow = ReadConfigContextWindowValue(compactionSection, contextWindowModeKey);

        var providerIdChanged = updateProviderId && !string.Equals(existingProviderId, providerId, StringComparison.Ordinal);
        var normalizedProviderModels = updateProviderModels ? NormalizeProviderModels(providerModels) : existingProviderModels;
        var providerModelsChanged = updateProviderModels && !ProviderModelsEqual(existingProviderModels, normalizedProviderModels);
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
        var reasoningChanged = updateReasoning
            && !ReasoningConfigEquals(existingReasoning, reasoning);
        var contextWindowChanged = updateContextWindow
            && !ContextWindowConfigEquals(existingContextWindow, contextWindow);

        if (updateProviderId)
            UpsertOrRemoveConfigValue(root, providerIdKey, "ProviderId", providerId);
        if (updateProviderModels)
            WriteProviderModels(root, normalizedProviderModels);
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
        if (updateReasoning)
        {
            if (reasoning == null)
            {
                RemoveConfigSection(root, "Reasoning");
            }
            else
            {
                var section = GetOrCreateConfigSection(root, "Reasoning", createIfMissing: true)!;
                UpsertOrRemoveConfigValue(section, FindCaseInsensitiveKey(section, "Enabled"), "Enabled", reasoning.Enabled);
                UpsertOrRemoveConfigValue(section, FindCaseInsensitiveKey(section, "Effort"), "Effort", reasoning.Effort.ToString());
                UpsertOrRemoveConfigValue(section, FindCaseInsensitiveKey(section, "Output"), "Output", reasoning.Output.ToString());
            }
        }
        if (updateContextWindow)
        {
            if (contextWindow == null)
            {
                var section = GetOrCreateConfigSection(root, "Compaction", createIfMissing: false);
                if (section != null)
                {
                    var existingKey = FindCaseInsensitiveKey(section, "ContextWindowMode");
                    UpsertOrRemoveConfigValue(section, existingKey, "ContextWindowMode", (string?)null);
                    RemoveConfigSectionIfEmpty(root, "Compaction");
                }
            }
            else
            {
                var section = GetOrCreateConfigSection(root, "Compaction", createIfMissing: true)!;
                UpsertOrRemoveConfigValue(
                    section,
                    FindCaseInsensitiveKey(section, "ContextWindowMode"),
                    "ContextWindowMode",
                    contextWindow.Mode.ToString());
            }
        }

        if (providerIdChanged
            || providerModelsChanged
            || welcomeSuggestionsChanged
            || skillsSelfLearningChanged
            || memoryAutoConsolidateChanged
            || dreamsEnabledChanged
            || dreamsIntervalChanged
            || dreamsThreadLookbackCountChanged
            || dreamsAutoApplyChanged
            || defaultApprovalPolicyChanged
            || toolsLspEnabledChanged
            || reasoningChanged
            || contextWindowChanged
            || legacyLanguageRemoved
            || legacyModelRemoved)
        {
            WriteConfigObject(configPath, root);
        }

        return new WorkspaceConfigSaveResult
        {
            ProviderId = updateProviderId ? providerId : existingProviderId,
            ProviderModels = normalizedProviderModels.Count == 0 ? null : new Dictionary<string, string>(normalizedProviderModels, StringComparer.Ordinal),
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
            Reasoning = updateReasoning
                ? CloneNullableReasoningConfig(reasoning)
                : CloneNullableReasoningConfig(existingReasoning),
            ContextWindow = updateContextWindow
                ? CloneNullableContextWindowConfig(contextWindow)
                : CloneNullableContextWindowConfig(existingContextWindow),
            ProviderIdChanged = providerIdChanged,
            ProviderModelsChanged = providerModelsChanged,
            WelcomeSuggestionsChanged = welcomeSuggestionsChanged,
            SkillsSelfLearningChanged = skillsSelfLearningChanged,
            MemoryAutoConsolidateChanged = memoryAutoConsolidateChanged,
            DreamsChanged = dreamsEnabledChanged || dreamsIntervalChanged || dreamsThreadLookbackCountChanged || dreamsAutoApplyChanged,
            DefaultApprovalPolicyChanged = defaultApprovalPolicyChanged,
            ToolsLspEnabledChanged = toolsLspEnabledChanged,
            ReasoningChanged = reasoningChanged,
            ContextWindowChanged = contextWindowChanged
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

    private static AppConfig.ReasoningConfig? ParseNullableReasoningConfig(
        JsonElement element,
        string fieldName,
        AppConfig.ReasoningConfig fallback)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        if (element.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be an object or null.");

        var hasEnabled = TryGetCaseInsensitiveProperty(element, "enabled", out var enabledEl);
        var hasEffort = TryGetCaseInsensitiveProperty(element, "effort", out var effortEl);
        var hasOutput = TryGetCaseInsensitiveProperty(element, "output", out var outputEl);
        if (!hasEnabled && !hasEffort && !hasOutput)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must contain at least one of 'enabled', 'effort', or 'output'.");

        var enabled = hasEnabled ? ParseNullableBoolean(enabledEl, $"{fieldName}.enabled") : null;
        var effort = hasEffort ? ParseNullableReasoningEffort(effortEl, $"{fieldName}.effort") : null;
        var output = hasOutput ? ParseNullableReasoningOutput(outputEl, $"{fieldName}.output") : null;

        return new AppConfig.ReasoningConfig
        {
            Enabled = enabled ?? (hasEffort ? true : fallback.Enabled),
            Effort = effort ?? fallback.Effort,
            Output = output ?? fallback.Output
        };
    }

    private static ThreadContextWindowConfig? ParseNullableWorkspaceContextWindowConfig(
        JsonElement element,
        string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        if (element.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be an object or null.");

        if (!TryGetCaseInsensitiveProperty(element, "mode", out var modeEl))
            throw AppServerErrors.InvalidParams($"'{fieldName}' must contain 'mode'.");
        if (modeEl.ValueKind != JsonValueKind.String)
            throw AppServerErrors.InvalidParams($"'{fieldName}.mode' must be 'default' or 'max'.");

        if (!ContextWindowModeJsonConverter.TryParse(modeEl.GetString(), out var mode))
            throw AppServerErrors.InvalidParams($"'{fieldName}.mode' must be 'default' or 'max'.");

        return mode == ContextWindowMode.Default
            ? null
            : new ThreadContextWindowConfig
            {
                Mode = mode
            };
    }

    private static ReasoningEffort? ParseNullableReasoningEffort(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.String)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a reasoning effort string or null.");

        var raw = element.GetString();
        if (!ModelThinkingAdapterCatalog.TryParseReasoningEffort(raw, out var effort))
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be one of 'low', 'medium', 'high', or 'extraHigh'.");

        return effort;
    }

    private static ReasoningOutput? ParseNullableReasoningOutput(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.String)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be a reasoning output string or null.");

        var raw = element.GetString();
        if (!ModelThinkingAdapterCatalog.TryParseReasoningOutput(raw, out var output))
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be one of 'none', 'summary', or 'full'.");

        return output;
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

    private static AppConfig.ReasoningConfig? ReadConfigReasoningValue(JsonObject? root)
    {
        if (root == null)
            return null;

        var enabled = ReadConfigBooleanValue(root, FindCaseInsensitiveKey(root, "Enabled"));
        var effortRaw = ReadConfigStringValue(root, FindCaseInsensitiveKey(root, "Effort"));
        var outputRaw = ReadConfigStringValue(root, FindCaseInsensitiveKey(root, "Output"));
        var hasEffort = ModelThinkingAdapterCatalog.TryParseReasoningEffort(effortRaw, out var effort);
        var hasOutput = ModelThinkingAdapterCatalog.TryParseReasoningOutput(outputRaw, out var output);
        if (!enabled.HasValue && !hasEffort && !hasOutput)
            return null;

        return new AppConfig.ReasoningConfig
        {
            Enabled = enabled ?? false,
            Effort = hasEffort ? effort : ReasoningEffort.Medium,
            Output = hasOutput ? output : ReasoningOutput.Full
        };
    }

    private static ThreadContextWindowConfig? ReadConfigContextWindowValue(JsonObject? root, string? key)
    {
        var raw = ReadConfigStringValue(root, key);
        if (!ContextWindowModeJsonConverter.TryParse(raw, out var mode) || mode == ContextWindowMode.Default)
            return null;

        return new ThreadContextWindowConfig
        {
            Mode = mode
        };
    }

    private static AppConfig.ReasoningConfig CloneReasoningConfig(AppConfig.ReasoningConfig source) => new()
    {
        Enabled = source.Enabled,
        Effort = source.Effort,
        Output = source.Output
    };

    private static AppConfig.ReasoningConfig? CloneNullableReasoningConfig(AppConfig.ReasoningConfig? source) =>
        source == null ? null : CloneReasoningConfig(source);

    private static ThreadContextWindowConfig? CloneNullableContextWindowConfig(ThreadContextWindowConfig? source) =>
        source == null
            ? null
            : new ThreadContextWindowConfig
            {
                Mode = source.Mode
            };

    private static bool ReasoningConfigEquals(AppConfig.ReasoningConfig? left, AppConfig.ReasoningConfig? right)
    {
        if (left == null || right == null)
            return left == null && right == null;

        return left.Enabled == right.Enabled
               && left.Effort == right.Effort
               && left.Output == right.Output;
    }

    private static bool ContextWindowConfigEquals(ThreadContextWindowConfig? left, ThreadContextWindowConfig? right)
    {
        if (left == null || right == null)
            return left == null && right == null;

        return left.Mode == right.Mode;
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

        public Dictionary<string, string>? ProviderModels { get; init; }

        public bool? WelcomeSuggestionsEnabled { get; init; }

        public bool? SkillsSelfLearningEnabled { get; init; }

        public bool? MemoryAutoConsolidateEnabled { get; init; }

        public bool? DreamsEnabled { get; init; }

        public string? DreamsInterval { get; init; }

        public int? DreamsThreadLookbackCount { get; init; }

        public bool? DreamsAutoApply { get; init; }

        public string? DefaultApprovalPolicy { get; init; }

        public bool? ToolsLspEnabled { get; init; }

        public AppConfig.ReasoningConfig? Reasoning { get; init; }

        public ThreadContextWindowConfig? ContextWindow { get; init; }

        public bool ProviderIdChanged { get; init; }

        public bool ProviderModelsChanged { get; init; }

        public bool WelcomeSuggestionsChanged { get; init; }

        public bool SkillsSelfLearningChanged { get; init; }

        public bool MemoryAutoConsolidateChanged { get; init; }

        public bool DreamsChanged { get; init; }

        public bool DefaultApprovalPolicyChanged { get; init; }

        public bool ToolsLspEnabledChanged { get; init; }

        public bool ReasoningChanged { get; init; }

        public bool ContextWindowChanged { get; init; }
    }
}
