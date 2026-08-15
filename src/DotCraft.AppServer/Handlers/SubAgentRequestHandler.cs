using DotCraft.Agents;
using DotCraft.Configuration;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using SessionThread = DotCraft.Sessions.SessionThread;
using ModelPreference = DotCraft.Configuration.ModelPreference;

namespace DotCraft.AppServer;

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
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentProfileList, HandleSubAgentProfileListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentSettingsUpdate, HandleSubAgentSettingsUpdateAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentProfileSetEnabled, HandleSubAgentProfileSetEnabledAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentProfileUpsert, HandleSubAgentProfileUpsertAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentProfileRemove, HandleSubAgentProfileRemoveAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentChildrenList, HandleSubAgentChildrenListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentSendMessage, HandleSubAgentSendMessageAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentFollowupTask, HandleSubAgentFollowupTaskAsync);
        table.Map(Protocol.AppServer.AppServerRpc.SubAgentClose, HandleSubAgentCloseAsync);
    }

    private Task<AppServerTypedResult<Contract.SubAgentProfileListResult>> HandleSubAgentProfileListAsync(
        AppServerTypedRequest<Protocol.RpcEmpty> request,
        CancellationToken ct)
    {
        _ = request;
        _ = ct;
        EnsureSubAgentManagementAvailable();

        var listResult = BuildSubAgentProfileListResult();
        return Task.FromResult(AppServerTypedResult<Contract.SubAgentProfileListResult>.FromResult(listResult));
    }

    private Task<AppServerTypedResult<Contract.SubAgentSettingsUpdateResult>> HandleSubAgentSettingsUpdateAsync(
        AppServerTypedRequest<Contract.SubAgentSettingsUpdateParams> request,
        CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = request.Params;
        var hasResumeEnabled = p.ExternalCliSessionResumeEnabled.IsSet
            && p.ExternalCliSessionResumeEnabled.Value.HasValue;
        var hasProviderPreferences = p.ProviderPreferences.IsSet;
        var hasMinWaitTimeoutMs = p.MinWaitTimeoutMs.IsSet;
        var hasDefaultWaitTimeoutMs = p.DefaultWaitTimeoutMs.IsSet;
        var hasMaxWaitTimeoutMs = p.MaxWaitTimeoutMs.IsSet;
        if (!hasResumeEnabled
            && !hasProviderPreferences
            && !hasMinWaitTimeoutMs
            && !hasDefaultWaitTimeoutMs
            && !hasMaxWaitTimeoutMs)
        {
            throw AppServerErrors.InvalidParams("At least one of 'externalCliSessionResumeEnabled', 'providerPreferences', 'minWaitTimeoutMs', 'defaultWaitTimeoutMs', or 'maxWaitTimeoutMs' is required.");
        }

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var nextResumeEnabled = hasResumeEnabled
            ? p.ExternalCliSessionResumeEnabled.Value!.Value
            : state.EnableExternalCliSessionResume;
        var nextProviderPreferences = hasProviderPreferences
            ? NormalizeProviderPreferences(
                SubAgentContractMapper.FromContract(p.ProviderPreferences.Value))
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
            hasMinWaitTimeoutMs ? RequireInteger(p.MinWaitTimeoutMs, "minWaitTimeoutMs") : state.WaitAgentTimeouts.MinTimeoutMs,
            hasDefaultWaitTimeoutMs ? RequireInteger(p.DefaultWaitTimeoutMs, "defaultWaitTimeoutMs") : state.WaitAgentTimeouts.DefaultTimeoutMs,
            hasMaxWaitTimeoutMs ? RequireInteger(p.MaxWaitTimeoutMs, "maxWaitTimeoutMs") : state.WaitAgentTimeouts.MaxTimeoutMs);
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
            Protocol.AppServer.AppServerMethodNames.SubAgentSettingsUpdate,
            [ConfigChangeRegions.SubAgent]);

        return Task.FromResult(AppServerTypedResult<Contract.SubAgentSettingsUpdateResult>.FromResult(
            new Contract.SubAgentSettingsUpdateResult
            {
                Settings = SubAgentContractMapper.ToContract(
                    nextResumeEnabled,
                    nextProviderPreferences,
                    nextWaitAgentTimeouts)
            }));
    }

    private Task<AppServerTypedResult<Contract.SubAgentProfileSetEnabledResult>> HandleSubAgentProfileSetEnabledAsync(
        AppServerTypedRequest<Contract.SubAgentProfileSetEnabledParams> request,
        CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = request.Params;
        var name = SubAgentContractMapper.Read(p.Name);
        var enabled = SubAgentContractMapper.Read(p.Enabled);
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        if (string.Equals(name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase) && !enabled)
            throw AppServerErrors.SubAgentProfileProtected($"'{SubAgentCoordinator.DefaultProfileName}' cannot be disabled.");

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var builtIns = SubAgentProfileRegistry.CreateBuiltInProfiles();
        var registry = new SubAgentProfileRegistry(
            state.Profiles,
            builtIns,
            SubAgentProfileRegistry.KnownRuntimeTypes,
            state.DisabledProfiles);
        if (!registry.TryGet(name, out _))
            throw AppServerErrors.SubAgentProfileNotFound(name);

        var disabled = state.DisabledProfiles.ToList();
        if (enabled)
            disabled.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
        else if (!disabled.Contains(name, StringComparer.OrdinalIgnoreCase))
            disabled.Add(name);

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
            Protocol.AppServer.AppServerMethodNames.SubAgentProfileSetEnabled,
            [ConfigChangeRegions.SubAgent]);

        var updated = SubAgentContractMapper.Read(BuildSubAgentProfileListResult().Profiles)!
            .First(profile => string.Equals(SubAgentContractMapper.Read(profile.Name), name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(AppServerTypedResult<Contract.SubAgentProfileSetEnabledResult>.FromResult(
            new Contract.SubAgentProfileSetEnabledResult { Profile = updated }));
    }

    private Task<AppServerTypedResult<Contract.SubAgentProfileUpsertResult>> HandleSubAgentProfileUpsertAsync(
        AppServerTypedRequest<Contract.SubAgentProfileUpsertParams> request,
        CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = request.Params;
        var name = SubAgentContractMapper.Read(p.Name);
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var profile = SubAgentContractMapper.FromContract(
            name,
            SubAgentContractMapper.Read(p.Definition) ?? new Contract.SubAgentProfileWrite());
        ValidateSubAgentProfileWire(profile);

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var profiles = state.Profiles.Select(existing => existing.Clone()).ToList();
        var existingIndex = profiles.FindIndex(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
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
            Protocol.AppServer.AppServerMethodNames.SubAgentProfileUpsert,
            [ConfigChangeRegions.SubAgent]);

        var updated = SubAgentContractMapper.Read(BuildSubAgentProfileListResult().Profiles)!
            .First(entry => string.Equals(SubAgentContractMapper.Read(entry.Name), name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(AppServerTypedResult<Contract.SubAgentProfileUpsertResult>.FromResult(
            new Contract.SubAgentProfileUpsertResult { Profile = updated }));
    }

    private Task<AppServerTypedResult<Contract.SubAgentProfileRemoveResult>> HandleSubAgentProfileRemoveAsync(
        AppServerTypedRequest<Contract.SubAgentProfileRemoveParams> request,
        CancellationToken ct)
    {
        _ = ct;
        EnsureSubAgentManagementAvailable();
        var p = request.Params;
        var name = SubAgentContractMapper.Read(p.Name);
        if (string.IsNullOrWhiteSpace(name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var state = SubAgentProfilesPersistence.LoadWorkspaceState(workspaceCraftPath!);
        var workspaceProfiles = state.Profiles.Select(profile => profile.Clone()).ToList();
        var existingIndex = workspaceProfiles.FindIndex(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex < 0)
            throw AppServerErrors.SubAgentProfileNotFound(name);

        workspaceProfiles.RemoveAt(existingIndex);
        var disabled = state.DisabledProfiles.ToList();
        var isBuiltIn = SubAgentProfileRegistry.CreateBuiltInProfiles()
            .Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
        if (!isBuiltIn)
            disabled.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));

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
            Protocol.AppServer.AppServerMethodNames.SubAgentProfileRemove,
            [ConfigChangeRegions.SubAgent]);

        return Task.FromResult(AppServerTypedResult<Contract.SubAgentProfileRemoveResult>.FromResult(
            new Contract.SubAgentProfileRemoveResult { Removed = true }));
    }

    private async Task<AppServerTypedResult<Contract.SubAgentChildrenListResult>> HandleSubAgentChildrenListAsync(
        AppServerTypedRequest<Contract.SubAgentChildrenListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var parentThreadId = SubAgentContractMapper.Read(p.ParentThreadId);
        if (string.IsNullOrWhiteSpace(parentThreadId))
            throw AppServerErrors.InvalidParams("'parentThreadId' is required.");

        var edges = await sessionService.ListSubAgentChildrenAsync(
            parentThreadId.Trim(),
            SubAgentContractMapper.Read(p.IncludeClosed) ?? false,
            ct);
        var data = new List<Contract.SubAgentChild>();
        foreach (var edge in edges)
        {
            Contract.SessionThread? thread = null;
            if (SubAgentContractMapper.Read(p.IncludeThreads) == true)
            {
                var child = await sessionService.GetThreadAsync(edge.ChildThreadId, ct);
                var enriched = await enrichThreadAsync(child, child.ToWire(includeTurns: false), ct);
                thread = AppServerContractMapper.ToContract(enriched);
            }

            data.Add(new Contract.SubAgentChild
            {
                Edge = SubAgentContractMapper.ToContract(edge),
                Thread = thread is null
                    ? default
                    : Protocol.Optional<Contract.SessionThread?>.FromValue(thread)
            });
        }

        return AppServerTypedResult<Contract.SubAgentChildrenListResult>.FromResult(
            new Contract.SubAgentChildrenListResult { Data = data });
    }

    private async Task<AppServerTypedResult<Contract.SubAgentControlResult>> HandleSubAgentSendMessageAsync(
        AppServerTypedRequest<Contract.SubAgentTargetMessageParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var parentThreadId = SubAgentContractMapper.Read(p.ParentThreadId);
        var target = SubAgentContractMapper.Read(p.Target);
        var message = SubAgentContractMapper.Read(p.Message);
        if (string.IsNullOrWhiteSpace(parentThreadId) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(message))
            throw AppServerErrors.InvalidParams("'parentThreadId', 'target', and 'message' are required.");

        var context = await BuildSubAgentControlContextAsync(parentThreadId.Trim(), ct);
        var result = await SubAgentSessionControl.SendMessageAsync(context, target.Trim(), message, ct);
        return AppServerTypedResult<Contract.SubAgentControlResult>.FromResult(SubAgentContractMapper.ToContract(result));
    }

    private async Task<AppServerTypedResult<Contract.SubAgentControlResult>> HandleSubAgentFollowupTaskAsync(
        AppServerTypedRequest<Contract.SubAgentTargetMessageParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var parentThreadId = SubAgentContractMapper.Read(p.ParentThreadId);
        var target = SubAgentContractMapper.Read(p.Target);
        var message = SubAgentContractMapper.Read(p.Message);
        if (string.IsNullOrWhiteSpace(parentThreadId) || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(message))
            throw AppServerErrors.InvalidParams("'parentThreadId', 'target', and 'message' are required.");

        var context = await BuildSubAgentControlContextAsync(parentThreadId.Trim(), ct);
        var result = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            target.Trim(),
            message,
            CreateSubAgentCoordinator(context.ParentThread),
            ct,
            ParseDeliveryMode(SubAgentContractMapper.Read(p.DeliveryMode)));
        return AppServerTypedResult<Contract.SubAgentControlResult>.FromResult(SubAgentContractMapper.ToContract(result));
    }

    private async Task<AppServerTypedResult<Contract.SubAgentControlResult>> HandleSubAgentCloseAsync(
        AppServerTypedRequest<Contract.SubAgentTargetParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var parentThreadId = SubAgentContractMapper.Read(p.ParentThreadId);
        var target = SubAgentContractMapper.Read(p.Target);
        if (string.IsNullOrWhiteSpace(parentThreadId) || string.IsNullOrWhiteSpace(target))
            throw AppServerErrors.InvalidParams("'parentThreadId' and 'target' are required.");

        var context = await BuildSubAgentControlContextAsync(parentThreadId.Trim(), ct);
        var result = await SubAgentSessionControl.CloseAgentAsync(context, target.Trim(), ct);
        return AppServerTypedResult<Contract.SubAgentControlResult>.FromResult(SubAgentContractMapper.ToContract(result));
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

    private Contract.SubAgentProfileListResult BuildSubAgentProfileListResult()
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
                return new Contract.SubAgentProfileEntry
                {
                    Name = profile.Name,
                    IsBuiltIn = registry.IsBuiltInProfile(profile.Name),
                    IsTemplate = registry.IsTemplateProfile(profile.Name),
                    HasWorkspaceOverride = workspaceOverrideNames.Contains(profile.Name),
                    IsDefault = string.Equals(profile.Name, SubAgentCoordinator.DefaultProfileName, StringComparison.OrdinalIgnoreCase),
                    Enabled = registry.IsEnabled(profile.Name),
                    Definition = SubAgentContractMapper.ToContract(profile),
                    BuiltInDefaults = builtInMap.TryGetValue(profile.Name, out var builtInDefault)
                        ? SubAgentContractMapper.ToContract(builtInDefault)
                        : default,
                    Diagnostic = new Contract.SubAgentProfileDiagnostic
                    {
                        Enabled = diagnostic.Enabled,
                        BinaryResolved = diagnostic.BinaryResolved,
                        HiddenFromPrompt = diagnostic.HiddenFromPrompt,
                        HiddenReason = diagnostic.HiddenReason is null
                            ? default
                            : Protocol.Optional<string?>.FromValue(diagnostic.HiddenReason),
                        Warnings = diagnostic.Warnings.ToArray()
                    }
                };
            })
            .ToList();

        return new Contract.SubAgentProfileListResult
        {
            DefaultName = SubAgentCoordinator.DefaultProfileName,
            Profiles = profiles,
            Settings = SubAgentContractMapper.ToContract(
                state.EnableExternalCliSessionResume,
                state.ProviderPreferences,
                state.WaitAgentTimeouts)
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

    private static Dictionary<string, ModelPreference> NormalizeProviderPreferences(
        IReadOnlyDictionary<string, ModelPreference>? providerPreferences)
    {
        var result = new Dictionary<string, ModelPreference>(StringComparer.Ordinal);
        if (providerPreferences == null)
            return result;
        foreach (var (rawProviderId, rawPreference) in providerPreferences)
        {
            var providerId = NormalizeOptionalString(rawProviderId);
            if (providerId == null || rawPreference == null)
                continue;
            if (string.IsNullOrWhiteSpace(rawPreference.Model)
                || string.Equals(rawPreference.Model.Trim(), "default", StringComparison.OrdinalIgnoreCase))
            {
                throw AppServerErrors.InvalidParams($"'providerPreferences.{rawProviderId}.model' is required.");
            }
            var preference = ModelPreferenceRules.Clone(rawPreference);
            preference.Model = preference.Model.Trim();
            result[providerId] = preference;
        }

        return result;
    }

    private static int RequireInteger(
        Protocol.Optional<int?> value,
        string fieldName)
    {
        if (!value.IsSet || !value.Value.HasValue)
            throw AppServerErrors.InvalidParams($"'{fieldName}' must be an integer.");
        return value.Value.Value;
    }

    private static SubAgentFollowupDeliveryMode ParseDeliveryMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SubAgentFollowupDeliveryMode.Queue;
        if (Enum.TryParse<SubAgentFollowupDeliveryMode>(value, ignoreCase: true, out var parsed))
            return parsed;

        throw AppServerErrors.InvalidParams("'deliveryMode' must be 'queue' or 'steer'.");
    }
}
