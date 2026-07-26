using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Protocol.AppServer;

internal sealed class SubAgentRequestHandler(
    ISessionService sessionService,
    IAppConfigMonitor? appConfigMonitor,
    string? workspaceCraftPath,
    string? hostWorkspacePath,
    AppServerRuntimeConfigRefresher runtimeConfig,
    Func<SessionThread, SessionWireThread, CancellationToken, Task<SessionWireThread>> enrichThreadAsync,
    Func<SessionThread, SubAgentCoordinator?>? subAgentCoordinatorFactory) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.SubAgentProfileList, HandleSubAgentProfileListAsync);
        table.Map(AppServerMethods.SubAgentSettingsUpdate, HandleSubAgentSettingsUpdateAsync);
        table.Map(AppServerMethods.SubAgentProfileSetEnabled, HandleSubAgentProfileSetEnabledAsync);
        table.Map(AppServerMethods.SubAgentProfileUpsert, HandleSubAgentProfileUpsertAsync);
        table.Map(AppServerMethods.SubAgentProfileRemove, HandleSubAgentProfileRemoveAsync);
        table.Map(AppServerMethods.SubAgentChildrenList, HandleSubAgentChildrenListAsync);
        table.Map(AppServerMethods.SubAgentSendMessage, HandleSubAgentSendMessageAsync);
        table.Map(AppServerMethods.SubAgentFollowupTask, HandleSubAgentFollowupTaskAsync);
        table.Map(AppServerMethods.SubAgentClose, HandleSubAgentCloseAsync);
    }

    private Task<object?> HandleSubAgentProfileListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;
        EnsureSubAgentManagementAvailable();

        var listResult = BuildSubAgentProfileListResult();
        return Task.FromResult<object?>(listResult);
    }

    private Task<object?> HandleSubAgentSettingsUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = AppServerParams.Get<SubAgentSettingsUpdateParams>(msg);
        JsonElement providerPreferencesEl = default;
        JsonElement minWaitTimeoutMsEl = default;
        JsonElement defaultWaitTimeoutMsEl = default;
        JsonElement maxWaitTimeoutMsEl = default;
        var hasProviderPreferences = msg.Params.HasValue
            && msg.Params.Value.ValueKind == JsonValueKind.Object
            && TryGetCaseInsensitiveProperty(msg.Params.Value, "providerPreferences", out providerPreferencesEl);
        if (msg.Params.HasValue
            && msg.Params.Value.ValueKind == JsonValueKind.Object
            && TryGetCaseInsensitiveProperty(msg.Params.Value, "providerModels", out _))
        {
            throw AppServerErrors.InvalidParams(
                "'providerModels' is no longer accepted. Use 'providerPreferences'.");
        }
        var hasMinWaitTimeoutMs = msg.Params.HasValue
            && msg.Params.Value.ValueKind == JsonValueKind.Object
            && TryGetCaseInsensitiveProperty(msg.Params.Value, "minWaitTimeoutMs", out minWaitTimeoutMsEl);
        var hasDefaultWaitTimeoutMs = msg.Params.HasValue
            && msg.Params.Value.ValueKind == JsonValueKind.Object
            && TryGetCaseInsensitiveProperty(msg.Params.Value, "defaultWaitTimeoutMs", out defaultWaitTimeoutMsEl);
        var hasMaxWaitTimeoutMs = msg.Params.HasValue
            && msg.Params.Value.ValueKind == JsonValueKind.Object
            && TryGetCaseInsensitiveProperty(msg.Params.Value, "maxWaitTimeoutMs", out maxWaitTimeoutMsEl);
        if (!p.ExternalCliSessionResumeEnabled.HasValue
            && !hasProviderPreferences
            && !hasMinWaitTimeoutMs
            && !hasDefaultWaitTimeoutMs
            && !hasMaxWaitTimeoutMs)
        {
            throw AppServerErrors.InvalidParams("At least one of 'externalCliSessionResumeEnabled', 'providerPreferences', 'minWaitTimeoutMs', 'defaultWaitTimeoutMs', or 'maxWaitTimeoutMs' is required.");
        }

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var nextResumeEnabled = p.ExternalCliSessionResumeEnabled ?? state.EnableExternalCliSessionResume;
        var nextProviderPreferences = hasProviderPreferences
            ? NormalizeProviderPreferences(
                ParseNullableProviderPreferences(providerPreferencesEl, "providerPreferences"))
            : state.ProviderPreferences;
        if (hasProviderPreferences)
        {
            var currentConfig = appConfigMonitor?.Current ?? AppConfig.Load(
                Path.Combine(workspaceCraftPath!, "config.json"));
            foreach (var (providerId, preference) in nextProviderPreferences)
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
        }
        var nextWaitAgentTimeouts = new SubAgentWaitAgentTimeoutOptions(
            hasMinWaitTimeoutMs ? ParseInteger(minWaitTimeoutMsEl, "minWaitTimeoutMs") : state.WaitAgentTimeouts.MinTimeoutMs,
            hasDefaultWaitTimeoutMs ? ParseInteger(defaultWaitTimeoutMsEl, "defaultWaitTimeoutMs") : state.WaitAgentTimeouts.DefaultTimeoutMs,
            hasMaxWaitTimeoutMs ? ParseInteger(maxWaitTimeoutMsEl, "maxWaitTimeoutMs") : state.WaitAgentTimeouts.MaxTimeoutMs);
        var waitAgentTimeoutErrors = SubAgentWaitAgentTimeoutOptions.Validate(nextWaitAgentTimeouts);
        if (waitAgentTimeoutErrors.Count > 0)
            throw AppServerErrors.InvalidParams(string.Join(" ", waitAgentTimeoutErrors));

        SubAgentProfilesPersistence.SaveWorkspaceState(
            workspaceCraftPath!,
            state.DisabledProfiles,
            nextResumeEnabled,
            nextWaitAgentTimeouts,
            state.Profiles,
            hasProviderPreferences ? nextProviderPreferences : null);
        runtimeConfig.RefreshCurrentSubAgentConfig();
        runtimeConfig.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SubAgentSettingsUpdate,
            [ConfigChangeRegions.SubAgent]);

        return Task.FromResult<object?>(new SubAgentSettingsUpdateResult
        {
            Settings = new SubAgentSettingsWire
            {
                ExternalCliSessionResumeEnabled = nextResumeEnabled,
                ProviderPreferences = nextProviderPreferences.Count == 0
                    ? null
                    : nextProviderPreferences.ToDictionary(
                        pair => pair.Key,
                        pair => ModelPreferenceRules.Clone(pair.Value),
                        StringComparer.Ordinal),
                MinWaitTimeoutMs = nextWaitAgentTimeouts.MinTimeoutMs,
                DefaultWaitTimeoutMs = nextWaitAgentTimeouts.DefaultTimeoutMs,
                MaxWaitTimeoutMs = nextWaitAgentTimeouts.MaxTimeoutMs
            }
        });
    }

    private Task<object?> HandleSubAgentProfileSetEnabledAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = AppServerParams.Get<SubAgentProfileSetEnabledParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        if (string.Equals(p.Name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase) && !p.Enabled)
            throw AppServerErrors.SubAgentProfileProtected($"'{SubAgentCoordinator.DefaultProfileName}' cannot be disabled.");

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var builtIns = SubAgentProfileRegistry.CreateBuiltInProfiles();
        var registry = new SubAgentProfileRegistry(
            state.Profiles,
            builtIns,
            SubAgentProfileRegistry.KnownRuntimeTypes,
            state.DisabledProfiles);
        if (!registry.TryGet(p.Name, out _))
            throw AppServerErrors.SubAgentProfileNotFound(p.Name);

        var disabled = state.DisabledProfiles.ToList();
        if (p.Enabled)
            disabled.RemoveAll(name => string.Equals(name, p.Name, StringComparison.OrdinalIgnoreCase));
        else if (!disabled.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            disabled.Add(p.Name);

        SubAgentProfilesPersistence.SaveWorkspaceState(
            workspaceCraftPath!,
            disabled,
            state.EnableExternalCliSessionResume,
            state.WaitAgentTimeouts,
            state.Profiles,
            state.ProviderPreferences);
        runtimeConfig.RefreshCurrentSubAgentConfig();
        runtimeConfig.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SubAgentProfileSetEnabled,
            [ConfigChangeRegions.SubAgent]);

        var updated = BuildSubAgentProfileListResult().Profiles
            .First(profile => string.Equals(profile.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<object?>(new SubAgentProfileSetEnabledResult { Profile = updated });
    }

    private Task<object?> HandleSubAgentProfileUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = AppServerParams.Get<SubAgentProfileUpsertParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var profile = MapWireToSubAgentProfile(p.Name, p.Definition);
        ValidateSubAgentProfileWire(profile);

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var profiles = state.Profiles.Select(existing => existing.Clone()).ToList();
        var existingIndex = profiles.FindIndex(existing => string.Equals(existing.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            profiles[existingIndex] = profile;
        else
            profiles.Add(profile);

        SubAgentProfilesPersistence.SaveWorkspaceState(
            workspaceCraftPath!,
            state.DisabledProfiles,
            state.EnableExternalCliSessionResume,
            state.WaitAgentTimeouts,
            profiles,
            state.ProviderPreferences);
        runtimeConfig.RefreshCurrentSubAgentConfig();
        runtimeConfig.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SubAgentProfileUpsert,
            [ConfigChangeRegions.SubAgent]);

        var updated = BuildSubAgentProfileListResult().Profiles
            .First(entry => string.Equals(entry.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<object?>(new SubAgentProfileUpsertResult { Profile = updated });
    }

    private Task<object?> HandleSubAgentProfileRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = AppServerParams.Get<SubAgentProfileRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var workspaceProfiles = state.Profiles.Select(profile => profile.Clone()).ToList();
        var existingIndex = workspaceProfiles.FindIndex(profile => string.Equals(profile.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
            throw AppServerErrors.SubAgentProfileNotFound(p.Name);

        workspaceProfiles.RemoveAt(existingIndex);
        var disabled = state.DisabledProfiles.ToList();
        var isBuiltIn = SubAgentProfileRegistry.CreateBuiltInProfiles()
            .Any(profile => string.Equals(profile.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (!isBuiltIn)
            disabled.RemoveAll(name => string.Equals(name, p.Name, StringComparison.OrdinalIgnoreCase));

        SubAgentProfilesPersistence.SaveWorkspaceState(
            workspaceCraftPath!,
            disabled,
            state.EnableExternalCliSessionResume,
            state.WaitAgentTimeouts,
            workspaceProfiles,
            state.ProviderPreferences);
        runtimeConfig.RefreshCurrentSubAgentConfig();
        runtimeConfig.InvalidateThreadAgents();
        appConfigMonitor?.NotifyChanged(
            AppServerMethods.SubAgentProfileRemove,
            [ConfigChangeRegions.SubAgent]);

        return Task.FromResult<object?>(new SubAgentProfileRemoveResult { Removed = true });
    }

    private async Task<object?> HandleSubAgentChildrenListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<SubAgentChildrenListParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ParentThreadId))
            throw AppServerErrors.InvalidParams("'parentThreadId' is required.");

        var edges = await sessionService.ListSubAgentChildrenAsync(
            p.ParentThreadId.Trim(),
            p.IncludeClosed ?? false,
            ct);
        var data = new List<SubAgentChildWire>();
        foreach (var edge in edges)
        {
            SessionWireThread? thread = null;
            if (p.IncludeThreads == true)
            {
                var child = await sessionService.GetThreadAsync(edge.ChildThreadId, ct);
                thread = await enrichThreadAsync(child, child.ToWire(includeTurns: false), ct);
            }

            data.Add(new SubAgentChildWire { Edge = edge, Thread = thread });
        }

        return new SubAgentChildrenListResult { Data = data };
    }

    private async Task<object?> HandleSubAgentSendMessageAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<SubAgentTargetMessageParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ParentThreadId) || string.IsNullOrWhiteSpace(p.Target) || string.IsNullOrWhiteSpace(p.Message))
            throw AppServerErrors.InvalidParams("'parentThreadId', 'target', and 'message' are required.");

        var context = await BuildSubAgentControlContextAsync(p.ParentThreadId.Trim(), ct);
        return await SubAgentSessionControl.SendMessageAsync(context, p.Target.Trim(), p.Message, ct);
    }

    private async Task<object?> HandleSubAgentFollowupTaskAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<SubAgentTargetMessageParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ParentThreadId) || string.IsNullOrWhiteSpace(p.Target) || string.IsNullOrWhiteSpace(p.Message))
            throw AppServerErrors.InvalidParams("'parentThreadId', 'target', and 'message' are required.");

        var context = await BuildSubAgentControlContextAsync(p.ParentThreadId.Trim(), ct);
        return await SubAgentSessionControl.FollowupTaskAsync(
            context,
            p.Target.Trim(),
            p.Message,
            CreateSubAgentCoordinator(context.ParentThread),
            ct,
            p.DeliveryMode);
    }

    private async Task<object?> HandleSubAgentCloseAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<SubAgentTargetParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ParentThreadId) || string.IsNullOrWhiteSpace(p.Target))
            throw AppServerErrors.InvalidParams("'parentThreadId' and 'target' are required.");

        var context = await BuildSubAgentControlContextAsync(p.ParentThreadId.Trim(), ct);
        return await SubAgentSessionControl.CloseAgentAsync(context, p.Target.Trim(), ct);
    }

    private void EnsureSubAgentManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("subagent/profiles/*");
    }

    private async Task<SubAgentSessionContext> BuildSubAgentControlContextAsync(string parentThreadId, CancellationToken ct)
    {
        var parent = await sessionService.GetThreadAsync(parentThreadId, ct);
        var source = parent.Source.SubAgent;
        return new SubAgentSessionContext
        {
            SessionService = sessionService,
            ParentThread = parent,
            ParentTurnId = string.Empty,
            RootThreadId = string.IsNullOrWhiteSpace(source?.RootThreadId) ? parent.Id : source.RootThreadId,
            Depth = source?.Depth ?? 0,
            LifecycleHook = sessionService is SessionService service
                ? service.RunSubAgentLifecycleHookAsync
                : null
        };
    }

    private SubAgentCoordinator? CreateSubAgentCoordinator(SessionThread thread)
    {
        if (subAgentCoordinatorFactory != null)
            return subAgentCoordinatorFactory(thread);

        var config = appConfigMonitor?.Current ?? new AppConfig();
        var workspaceRoot = !string.IsNullOrWhiteSpace(thread.WorkspacePath)
            ? thread.WorkspacePath
            : hostWorkspacePath ?? Environment.CurrentDirectory;
        return new SubAgentCoordinator(
            workspaceRoot,
            [new CliOneshotRuntime()],
            config.SubAgentProfiles,
            disabledProfiles: config.SubAgent.DisabledProfiles,
            externalCliSessionStore: new ThreadExternalCliSessionStore(thread),
            enableExternalCliSessionResume: config.SubAgent.EnableExternalCliSessionResume);
    }

    private static SubAgentProfileWriteWire MapSubAgentProfileToWire(SubAgentProfile profile) => new()
    {
        Runtime = profile.Runtime,
        Bin = profile.Bin,
        Args = profile.Args is { Count: > 0 } ? [.. profile.Args] : null,
        Env = profile.Env is { Count: > 0 } ? new Dictionary<string, string>(profile.Env, StringComparer.Ordinal) : null,
        EnvPassthrough = profile.EnvPassthrough is { Count: > 0 } ? [.. profile.EnvPassthrough] : null,
        WorkingDirectoryMode = profile.WorkingDirectoryMode,
        SupportsStreaming = profile.SupportsStreaming,
        SupportsResume = profile.SupportsResume,
        SupportsModelSelection = profile.SupportsModelSelection,
        InputFormat = profile.InputFormat,
        OutputFormat = profile.OutputFormat,
        InputMode = profile.InputMode,
        InputArgTemplate = profile.InputArgTemplate,
        InputEnvKey = profile.InputEnvKey,
        ResumeArgTemplate = profile.ResumeArgTemplate,
        ResumeSessionIdJsonPath = profile.ResumeSessionIdJsonPath,
        ResumeSessionIdRegex = profile.ResumeSessionIdRegex,
        OutputJsonPath = profile.OutputJsonPath,
        OutputInputTokensJsonPath = profile.OutputInputTokensJsonPath,
        OutputOutputTokensJsonPath = profile.OutputOutputTokensJsonPath,
        OutputTotalTokensJsonPath = profile.OutputTotalTokensJsonPath,
        OutputFileArgTemplate = profile.OutputFileArgTemplate,
        ReadOutputFile = profile.ReadOutputFile,
        DeleteOutputFileAfterRead = profile.DeleteOutputFileAfterRead,
        MaxOutputBytes = profile.MaxOutputBytes,
        Timeout = profile.Timeout,
        TrustLevel = profile.TrustLevel,
        PermissionModeMapping = profile.PermissionModeMapping is { Count: > 0 }
            ? new Dictionary<string, string>(profile.PermissionModeMapping, StringComparer.OrdinalIgnoreCase)
            : null,
        SanitizationRules = profile.SanitizationRules?.DeepClone() as JsonObject
    };

    private static SubAgentProfile MapWireToSubAgentProfile(string name, SubAgentProfileWriteWire wire) => new()
    {
        Name = name.Trim(),
        Runtime = wire.Runtime?.Trim() ?? string.Empty,
        Bin = string.IsNullOrWhiteSpace(wire.Bin) ? null : wire.Bin.Trim(),
        Args = wire.Args is { Count: > 0 } ? [.. wire.Args.Where(arg => !string.IsNullOrWhiteSpace(arg)).Select(arg => arg.Trim())] : null,
        Env = wire.Env is { Count: > 0 } ? new Dictionary<string, string>(wire.Env, StringComparer.Ordinal) : null,
        EnvPassthrough = wire.EnvPassthrough is { Count: > 0 }
            ? [.. wire.EnvPassthrough.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim())]
            : null,
        WorkingDirectoryMode = string.IsNullOrWhiteSpace(wire.WorkingDirectoryMode) ? "workspace" : wire.WorkingDirectoryMode.Trim(),
        SupportsStreaming = wire.SupportsStreaming,
        SupportsResume = wire.SupportsResume,
        SupportsModelSelection = wire.SupportsModelSelection,
        InputFormat = string.IsNullOrWhiteSpace(wire.InputFormat) ? null : wire.InputFormat.Trim(),
        OutputFormat = string.IsNullOrWhiteSpace(wire.OutputFormat) ? null : wire.OutputFormat.Trim(),
        InputMode = string.IsNullOrWhiteSpace(wire.InputMode) ? null : wire.InputMode.Trim(),
        InputArgTemplate = string.IsNullOrWhiteSpace(wire.InputArgTemplate) ? null : wire.InputArgTemplate,
        InputEnvKey = string.IsNullOrWhiteSpace(wire.InputEnvKey) ? null : wire.InputEnvKey.Trim(),
        ResumeArgTemplate = string.IsNullOrWhiteSpace(wire.ResumeArgTemplate) ? null : wire.ResumeArgTemplate,
        ResumeSessionIdJsonPath = string.IsNullOrWhiteSpace(wire.ResumeSessionIdJsonPath) ? null : wire.ResumeSessionIdJsonPath.Trim(),
        ResumeSessionIdRegex = string.IsNullOrWhiteSpace(wire.ResumeSessionIdRegex) ? null : wire.ResumeSessionIdRegex,
        OutputJsonPath = string.IsNullOrWhiteSpace(wire.OutputJsonPath) ? null : wire.OutputJsonPath.Trim(),
        OutputInputTokensJsonPath = string.IsNullOrWhiteSpace(wire.OutputInputTokensJsonPath) ? null : wire.OutputInputTokensJsonPath.Trim(),
        OutputOutputTokensJsonPath = string.IsNullOrWhiteSpace(wire.OutputOutputTokensJsonPath) ? null : wire.OutputOutputTokensJsonPath.Trim(),
        OutputTotalTokensJsonPath = string.IsNullOrWhiteSpace(wire.OutputTotalTokensJsonPath) ? null : wire.OutputTotalTokensJsonPath.Trim(),
        OutputFileArgTemplate = string.IsNullOrWhiteSpace(wire.OutputFileArgTemplate) ? null : wire.OutputFileArgTemplate,
        ReadOutputFile = wire.ReadOutputFile,
        DeleteOutputFileAfterRead = wire.DeleteOutputFileAfterRead,
        MaxOutputBytes = wire.MaxOutputBytes,
        Timeout = wire.Timeout,
        TrustLevel = string.IsNullOrWhiteSpace(wire.TrustLevel) ? null : wire.TrustLevel.Trim(),
        PermissionModeMapping = wire.PermissionModeMapping is { Count: > 0 }
            ? new Dictionary<string, string>(wire.PermissionModeMapping, StringComparer.OrdinalIgnoreCase)
            : null,
        SanitizationRules = wire.SanitizationRules?.DeepClone() as JsonObject
    };

    private static void ValidateSubAgentProfileWire(SubAgentProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw AppServerErrors.SubAgentProfileValidationFailed("'name' is required.");

        if (profile.SupportsResume == true)
        {
            if (string.IsNullOrWhiteSpace(profile.ResumeArgTemplate))
            {
                throw AppServerErrors.SubAgentProfileValidationFailed(
                    "resumeArgTemplate is required when supportsResume is true.");
            }

            if (string.IsNullOrWhiteSpace(profile.ResumeSessionIdJsonPath)
                && string.IsNullOrWhiteSpace(profile.ResumeSessionIdRegex))
            {
                throw AppServerErrors.SubAgentProfileValidationFailed(
                    "resumeSessionIdJsonPath or resumeSessionIdRegex is required when supportsResume is true.");
            }
        }

        var warnings = SubAgentProfileRegistry.ValidateProfiles(
            [profile],
            SubAgentProfileRegistry.KnownRuntimeTypes,
            []);
        if (warnings.Count > 0)
            throw AppServerErrors.SubAgentProfileValidationFailed(string.Join(" ", warnings));
    }

    private SubAgentProfileListResult BuildSubAgentProfileListResult()
    {
        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var workspaceOverrideNames = state.Profiles
            .Select(profile => profile.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var builtInProfiles = SubAgentProfileRegistry.CreateBuiltInProfiles();
        var builtInMap = builtInProfiles.ToDictionary(profile => profile.Name, profile => profile.Clone(), StringComparer.OrdinalIgnoreCase);
        var registry = new SubAgentProfileRegistry(
            state.Profiles,
            builtInProfiles,
            SubAgentProfileRegistry.KnownRuntimeTypes,
            state.DisabledProfiles);
        var diagnostics = BuildSubAgentDiagnostics(registry)
            .ToDictionary(diagnostic => diagnostic.Name, diagnostic => diagnostic, StringComparer.OrdinalIgnoreCase);

        var profiles = registry.Profiles
            .OrderBy(profile => string.Equals(profile.Name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile =>
            {
                var diagnostic = diagnostics[profile.Name];
                return new SubAgentProfileEntryWire
                {
                    Name = profile.Name,
                    IsBuiltIn = registry.IsBuiltInProfile(profile.Name),
                    IsTemplate = registry.IsTemplateProfile(profile.Name),
                    HasWorkspaceOverride = workspaceOverrideNames.Contains(profile.Name),
                    IsDefault = string.Equals(profile.Name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase),
                    Enabled = registry.IsEnabled(profile.Name),
                    Definition = MapSubAgentProfileToWire(profile),
                    BuiltInDefaults = builtInMap.TryGetValue(profile.Name, out var builtInDefault)
                        ? MapSubAgentProfileToWire(builtInDefault)
                        : null,
                    Diagnostic = new SubAgentProfileDiagnosticWire
                    {
                        Enabled = diagnostic.Enabled,
                        BinaryResolved = diagnostic.BinaryResolved,
                        HiddenFromPrompt = diagnostic.HiddenFromPrompt,
                        HiddenReason = diagnostic.HiddenReason,
                        Warnings = [.. diagnostic.Warnings]
                    }
                };
            })
            .ToList();

        return new SubAgentProfileListResult
        {
            DefaultName = SubAgentCoordinator.DefaultProfileName,
            Profiles = profiles,
            Settings = new SubAgentSettingsWire
            {
                ExternalCliSessionResumeEnabled = state.EnableExternalCliSessionResume,
                ProviderPreferences = state.ProviderPreferences.Count == 0
                    ? null
                    : state.ProviderPreferences.ToDictionary(
                        pair => pair.Key,
                        pair => ModelPreferenceRules.Clone(pair.Value),
                        StringComparer.Ordinal),
                MinWaitTimeoutMs = state.WaitAgentTimeouts.MinTimeoutMs,
                DefaultWaitTimeoutMs = state.WaitAgentTimeouts.DefaultTimeoutMs,
                MaxWaitTimeoutMs = state.WaitAgentTimeouts.MaxTimeoutMs
            }
        };
    }

    private static IReadOnlyList<SubAgentProfileDiagnostic> BuildSubAgentDiagnostics(SubAgentProfileRegistry registry)
    {
        var diagnostics = new List<SubAgentProfileDiagnostic>();
        foreach (var profile in registry.Profiles.OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            var warnings = registry.GetValidationWarningsForProfile(profile.Name);
            var enabled = registry.IsEnabled(profile.Name);
            var runtimeRegistered = SubAgentProfileRegistry.KnownRuntimeTypes.Contains(profile.Runtime, StringComparer.OrdinalIgnoreCase);

            string? resolvedBinary = null;
            var hiddenReasons = new List<string>();
            if (!enabled)
                hiddenReasons.Add("disabled by workspace configuration");

            if (!runtimeRegistered)
                hiddenReasons.Add("runtime not registered");

            if (registry.IsTemplateProfile(profile.Name))
                hiddenReasons.Add("template profile");

            if (warnings.Count > 0)
                hiddenReasons.Add("configuration warnings present");

            if (string.Equals(profile.Runtime, CliOneshotRuntime.RuntimeTypeName, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(profile.Bin))
                {
                    hiddenReasons.Add("missing required field 'bin'");
                }
                else if (runtimeRegistered)
                {
                    if (CliOneshotRuntime.TryResolveExecutablePath(profile.Bin, out var resolved))
                        resolvedBinary = resolved;
                    else
                        hiddenReasons.Add($"binary '{profile.Bin}' was not found on PATH");
                }
            }

            diagnostics.Add(new SubAgentProfileDiagnostic
            {
                Name = profile.Name,
                Runtime = profile.Runtime,
                WorkingDirectoryMode = profile.WorkingDirectoryMode,
                IsBuiltIn = registry.IsBuiltInProfile(profile.Name),
                Enabled = enabled,
                Bin = profile.Bin,
                ResolvedBinary = resolvedBinary,
                BinaryResolved = resolvedBinary != null,
                RuntimeRegistered = runtimeRegistered,
                HiddenFromPrompt = hiddenReasons.Count > 0,
                HiddenReason = hiddenReasons.Count > 0
                    ? string.Join("; ", hiddenReasons.Distinct(StringComparer.Ordinal))
                    : null,
                Warnings = warnings
            });
        }

        return diagnostics;
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

    private static int ParseInteger(JsonElement element, string fieldName)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            _ => throw AppServerErrors.InvalidParams($"'{fieldName}' must be an integer.")
        };
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
