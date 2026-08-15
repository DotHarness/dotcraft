using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tools;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.AppServer;

internal sealed class AgentProfileRequestHandler(
    ISessionService sessionService,
    string? workspaceCraftPath,
    string? hostWorkspacePath,
    IAppConfigMonitor? appConfigMonitor) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.AgentProfileList, HandleListAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AgentProfileRead, HandleReadAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AgentProfileValidate, HandleValidateAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AgentProfileUpsert, HandleUpsertAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AgentProfileRemove, HandleRemoveAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AgentProfileRefreshThread, HandleRefreshThreadAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AgentProfileBuilderDraftRead, HandleBuilderDraftReadAsync);
        table.Map(Protocol.AppServer.AppServerRpc.AgentProfileBuilderDraftUpdate, HandleBuilderDraftUpdateAsync);
    }

    private async Task<object?> HandleListAsync(
        AppServerTypedRequest<Contract.AgentProfileListParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        try
        {
            var store = CreateStore();
            var profiles = store
                .List(ValueOrDefault(p.Source), ValueOrDefault(p.IncludeInvalid) ?? true)
                .Select(profile => ToContract(profile))
                .ToList();
            await AnnotateStaleThreadsAsync(store, profiles, ct);
            return new Contract.AgentProfileListResult
            {
                Profiles = profiles
            };
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private async Task<object?> HandleReadAsync(
        AppServerTypedRequest<Contract.AgentProfileReadParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var id = ValueOrDefault(p.Id);
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        try
        {
            var profile = ToContract(CreateStore().Read(id, ValueOrDefault(p.Source)), includeRawContent: true, includeCompiledConfig: true);
            await AnnotateStaleThreadsAsync(CreateStore(), [profile], ct);
            return new Contract.AgentProfileReadResult
            {
                Profile = profile
            };
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private Task<object?> HandleValidateAsync(
        AppServerTypedRequest<Contract.AgentProfileValidateParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var p = request.Params;
        var rawContent = ValueOrDefault(p.RawContent);
        if (rawContent == null)
            throw AppServerErrors.InvalidParams("'rawContent' is required.");

        try
        {
            var validation = CreateStore().ValidateRaw(rawContent, ValueOrDefault(p.Source));
            return Task.FromResult<object?>(new Contract.AgentProfileValidateResult
            {
                Valid = validation.Valid,
                Diagnostics = ToContractDiagnostics(
                    validation.Diagnostics,
                    validation.ProviderPreference),
                Summary = new Contract.AgentProfileSummary
                {
                    Id = OmitIfNull(validation.Id),
                    Description = OmitIfNull(validation.Description)
                },
                LockedFields = validation.LockedFields,
                RestrictedFields = validation.RestrictedFields,
                CompiledConfig = validation.CompiledConfiguration is null
                    ? default
                    : new Protocol.Optional<Contract.ThreadConfiguration?>(
                        ThreadConfigurationContractMapper.ToContract(validation.CompiledConfiguration)),
                ProviderPreference = validation.ProviderPreference is null
                    ? default
                    : new Protocol.Optional<Contract.AgentProfileProviderPreference?>(
                        ToContract(validation.ProviderPreference))
            });
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private Task<object?> HandleUpsertAsync(
        AppServerTypedRequest<Contract.AgentProfileUpsertParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var p = request.Params;
        var id = ValueOrDefault(p.Id);
        var source = ValueOrDefault(p.Source);
        var rawContent = ValueOrDefault(p.RawContent);
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");
        if (string.IsNullOrWhiteSpace(source))
            throw AppServerErrors.InvalidParams("'source' is required.");
        if (rawContent == null)
            throw AppServerErrors.InvalidParams("'rawContent' is required.");

        try
        {
            var profile = CreateStore().Upsert(id, source, rawContent);
            return Task.FromResult<object?>(new Contract.AgentProfileUpsertResult
            {
                Profile = ToContract(profile, includeRawContent: true, includeCompiledConfig: true)
            });
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private Task<object?> HandleRemoveAsync(
        AppServerTypedRequest<Contract.AgentProfileRemoveParams> request,
        CancellationToken ct)
    {
        _ = ct;
        var p = request.Params;
        var id = ValueOrDefault(p.Id);
        var source = ValueOrDefault(p.Source);
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");
        if (string.IsNullOrWhiteSpace(source))
            throw AppServerErrors.InvalidParams("'source' is required.");

        try
        {
            return Task.FromResult<object?>(new Contract.AgentProfileRemoveResult
            {
                Removed = CreateStore().Remove(id, source)
            });
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private async Task<object?> HandleRefreshThreadAsync(
        AppServerTypedRequest<Contract.AgentProfileRefreshThreadParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var threadId = ValueOrDefault(p.ThreadId);
        var requestedParameterProfileId = ValueOrDefault(p.ProfileId);
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var store = CreateStore();
        string? requestedProfileId = null;
        try
        {
            var thread = await sessionService.GetThreadAsync(threadId, ct);
            var profileId = string.IsNullOrWhiteSpace(requestedParameterProfileId)
                ? thread.Configuration?.AgentProfileId
                : requestedParameterProfileId.Trim();
            if (string.IsNullOrWhiteSpace(profileId))
                throw AppServerErrors.InvalidParams("Thread does not have an agent profile id.");

            requestedProfileId = profileId;
            var profile = store.Read(profileId);
            if (!profile.Valid || profile.CompiledConfiguration == null)
                throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, "Agent profile validation failed.", profile.Diagnostics);

            var beforeFingerprint = thread.Configuration?.AgentProfileFingerprint;
            var currentConfig = appConfigMonitor?.Current;
            if (profile.ProviderPreference != null && currentConfig == null)
                throw new AgentProfileException(
                    AgentProfileErrorKind.ValidationFailed,
                    "Provider configuration is unavailable for the fixed Agent Profile model preset.");
            var refreshed = currentConfig == null
                ? store.ResolveProfileConfiguration(profileId)
                : store.ResolveProfileConfiguration(profileId, currentConfig);
            PreserveUnrelatedThreadFields(
                thread.Configuration,
                refreshed,
                profile.ProviderPreference);
            ValidateRuntimeConfiguration(refreshed);
            await sessionService.UpdateThreadConfigurationAsync(thread.Id, refreshed, ct);
            var audit = new AgentProfileAuditRecord
            {
                Event = "agentProfile.thread.refresh",
                Code = "AgentProfileThreadRefreshed",
                ProfileId = profile.Id,
                Source = profile.Source,
                ThreadId = thread.Id,
                Fields =
                {
                    ["fingerprint"] = profile.Fingerprint
                }
            };
            store.AppendAudit(audit);

            return new Contract.AgentProfileRefreshThreadResult
            {
                ThreadId = thread.Id,
                Profile = ToContract(profile, includeRawContent: true, includeCompiledConfig: true),
                Config = ThreadConfigurationContractMapper.ToContract(refreshed),
                WasStale = !string.Equals(beforeFingerprint, profile.Fingerprint, StringComparison.Ordinal),
                Audit = ToContract(audit)
            };
        }
        catch (AgentProfileException ex)
        {
            var failure = new AgentProfileAuditRecord
            {
                Event = "agentProfile.thread.refresh",
                Code = "AgentProfileThreadRefreshFailed",
                ProfileId = requestedProfileId ?? requestedParameterProfileId,
                ThreadId = threadId,
                Status = "failed",
                Fields =
                {
                    ["error"] = ex.Kind.ToString()
                }
            };
            store.AppendAudit(failure);
            throw MapError(ex);
        }
    }

    private async Task<object?> HandleBuilderDraftReadAsync(
        AppServerTypedRequest<Contract.AgentProfileBuilderDraftReadParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var binding = await RequireBuilderThreadAsync(ValueOrDefault(p.ThreadId), ct);
        var entry = EnsureBuilderDraft(binding.ThreadId, binding.TargetId, binding.TargetSource);
        return new Contract.AgentProfileBuilderDraftResult
        {
            ThreadId = binding.ThreadId,
            TargetId = entry.TargetId,
            TargetSource = entry.TargetSource,
            RawContent = entry.Markdown
        };
    }

    private async Task<object?> HandleBuilderDraftUpdateAsync(
        AppServerTypedRequest<Contract.AgentProfileBuilderDraftUpdateParams> request,
        CancellationToken ct)
    {
        var p = request.Params;
        var rawContent = ValueOrDefault(p.RawContent);
        if (rawContent == null)
            throw AppServerErrors.InvalidParams("'rawContent' is required.");

        var binding = await RequireBuilderThreadAsync(ValueOrDefault(p.ThreadId), ct);
        var existing = ProfileBuilderDraftStore.TryGet(binding.ThreadId);
        if (existing != null
            && (!string.Equals(existing.TargetId, binding.TargetId, StringComparison.Ordinal)
                || !string.Equals(existing.TargetSource, binding.TargetSource, StringComparison.Ordinal)))
        {
            ProfileBuilderDraftStore.Remove(binding.ThreadId);
            existing = null;
        }

        var entry = existing == null
            ? ProfileBuilderDraftStore.Seed(binding.ThreadId, binding.TargetId, binding.TargetSource, rawContent)
            : ProfileBuilderDraftStore.Update(binding.ThreadId, rawContent) ?? existing;

        return new Contract.AgentProfileBuilderDraftResult
        {
            ThreadId = binding.ThreadId,
            TargetId = entry.TargetId,
            TargetSource = entry.TargetSource,
            RawContent = entry.Markdown
        };
    }

    private AgentProfileStore CreateStore() =>
        new(ResolveWorkspaceDataPath());

    private async Task<(string ThreadId, string TargetId, string TargetSource)> RequireBuilderThreadAsync(
        string? threadId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var thread = await sessionService.GetThreadAsync(threadId.Trim(), ct);
        var targetId = thread.Configuration?.AgentBuilderTargetId?.Trim();
        if (string.IsNullOrWhiteSpace(targetId))
            throw AppServerErrors.InvalidParams("Thread is not an Agent Builder session.");

        var targetSource = string.IsNullOrWhiteSpace(thread.Configuration?.AgentBuilderTargetSource)
            ? AgentProfileSources.Workspace
            : thread.Configuration.AgentBuilderTargetSource!.Trim();

        return (thread.Id, targetId, targetSource);
    }

    private ProfileBuilderDraftEntry EnsureBuilderDraft(string threadId, string targetId, string targetSource)
    {
        var entry = ProfileBuilderDraftStore.TryGet(threadId);
        if (entry != null
            && string.Equals(entry.TargetId, targetId, StringComparison.Ordinal)
            && string.Equals(entry.TargetSource, targetSource, StringComparison.Ordinal))
        {
            return entry;
        }

        if (entry != null)
            ProfileBuilderDraftStore.Remove(threadId);

        return ProfileBuilderDraftStore.Seed(threadId, targetId, targetSource, SeedBuilderDraftMarkdown(targetId, targetSource));
    }

    private string SeedBuilderDraftMarkdown(string targetId, string targetSource)
    {
        try
        {
            return CreateStore().Read(targetId, targetSource).RawContent ?? string.Empty;
        }
        catch (AgentProfileException ex) when (ex.Kind == AgentProfileErrorKind.NotFound)
        {
            return string.Empty;
        }
    }

    private string? ResolveWorkspaceDataPath() =>
        string.IsNullOrWhiteSpace(workspaceCraftPath) ? null : workspaceCraftPath;

    private Contract.AgentProfileEntry ToContract(
        AgentProfileEntry profile,
        bool includeRawContent = false,
        bool includeCompiledConfig = false) => new()
        {
            Id = profile.Id,
            Name = OmitIfNull(profile.Name),
            Description = OmitIfNull(profile.Description),
            Avatar = OmitIfNull(profile.Avatar),
            Source = profile.Source,
            Path = OmitIfNull(profile.Path),
            UpdatedAt = OmitIfNull(profile.UpdatedAt),
            PluginId = OmitIfNull(profile.PluginId),
            Fingerprint = profile.Fingerprint,
            Valid = profile.Valid,
            IsBuiltIn = profile.IsBuiltIn,
            ReadOnly = profile.ReadOnly,
            Shadowed = profile.Shadowed,
            ShadowedBy = OmitIfNull(profile.ShadowedBy),
            SourceStack = profile.SourceStack,
            LockedFields = profile.LockedFields,
            RestrictedFields = profile.RestrictedFields,
            TrustRestricted = profile.TrustRestricted,
            StaleThreadIds = new List<string>(),
            Diagnostics = ToContractDiagnostics(profile.Diagnostics, profile.ProviderPreference),
            ProviderPreference = profile.ProviderPreference is null
                ? default
                : new Protocol.Optional<Contract.AgentProfileProviderPreference?>(
                    ToContract(profile.ProviderPreference)),
            RawContent = includeRawContent ? OmitIfNull(profile.RawContent) : default,
            CompiledConfig = includeCompiledConfig && profile.CompiledConfiguration is not null
                ? new Protocol.Optional<Contract.ThreadConfiguration?>(
                    ThreadConfigurationContractMapper.ToContract(profile.CompiledConfiguration))
                : default
        };

    private List<Contract.AgentProfileDiagnostic> ToContractDiagnostics(
        IEnumerable<AgentProfileDiagnostic> diagnostics,
        AgentProfileProviderPreference? providerPreference)
    {
        var result = diagnostics.Select(ToContract).ToList();
        var currentConfig = appConfigMonitor?.Current;
        if (providerPreference == null
            || string.IsNullOrWhiteSpace(providerPreference.ProviderId)
            || currentConfig == null)
        {
            return result;
        }

        try
        {
            _ = ModelProviderResolver.ResolveMain(
                currentConfig,
                providerPreference.ProviderId,
                providerPreference.Model);
        }
        catch (Exception ex) when (ex is ArgumentException or ModelProviderConfigurationException)
        {
            result.Add(new Contract.AgentProfileDiagnostic
            {
                Severity = "warning",
                Code = "PinnedProviderUnavailable",
                Message = $"Pinned provider '{providerPreference.ProviderId}' is not runnable in the current workspace."
            });
        }

        return result;
    }

    private static Contract.AgentProfileProviderPreference? ToContract(
        AgentProfileProviderPreference? providerPreference)
    {
        if (providerPreference == null)
            return null;

        return new Contract.AgentProfileProviderPreference
        {
            ProviderId = providerPreference.ProviderId,
            Model = providerPreference.Model,
            Reasoning = new Contract.AgentProfileReasoningPreference
            {
                Enabled = providerPreference.Reasoning.Enabled,
                Effort = JsonNamingPolicy.CamelCase.ConvertName(providerPreference.Reasoning.Effort.ToString())
            },
            Speed = JsonNamingPolicy.CamelCase.ConvertName(providerPreference.Speed.ToString()),
            ContextWindow = ThreadConfigurationContractMapper.ToContract(providerPreference.ContextWindow)
        };
    }

    internal static Contract.AgentProfileDiagnostic ToContract(AgentProfileDiagnostic diagnostic) => new()
    {
        Severity = diagnostic.Severity,
        Code = diagnostic.Code,
        Message = diagnostic.Message
    };

    private async Task AnnotateStaleThreadsAsync(
        AgentProfileStore store,
        IReadOnlyList<Contract.AgentProfileEntry> profiles,
        CancellationToken ct)
    {
        if (profiles.Count == 0 || string.IsNullOrWhiteSpace(hostWorkspacePath))
            return;

        IReadOnlyList<ThreadSummary> summaries;
        try
        {
            summaries = await sessionService.FindThreadsAsync(
                new SessionIdentity { WorkspacePath = hostWorkspacePath },
                includeArchived: false,
                includeSubAgents: true,
                ct: ct);
        }
        catch
        {
            return;
        }

        var profileById = profiles
            .Where(profile => ValueOrDefault(profile.Valid) && !ValueOrDefault(profile.Shadowed))
            .GroupBy(profile => ValueOrDefault(profile.Id) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var summary in summaries)
        {
            SessionThread thread;
            try
            {
                thread = await sessionService.GetThreadAsync(summary.Id, ct);
            }
            catch
            {
                continue;
            }

            var config = thread.Configuration;
            if (string.IsNullOrWhiteSpace(config?.AgentProfileId)
                || !profileById.TryGetValue(config.AgentProfileId, out var profile))
            {
                continue;
            }

            if (!string.Equals(config.AgentProfileFingerprint, ValueOrDefault(profile.Fingerprint), StringComparison.Ordinal)
                && ValueOrDefault(profile.StaleThreadIds) is List<string> staleThreadIds)
            {
                staleThreadIds.Add(thread.Id);
            }
        }
    }

    private static void PreserveUnrelatedThreadFields(
        ThreadConfiguration? current,
        ThreadConfiguration refreshed,
        AgentProfileProviderPreference? providerPreference)
    {
        if (current == null)
            return;

        if (providerPreference == null)
        {
            refreshed.ProviderId = current.ProviderId;
            refreshed.Model = current.Model;
            refreshed.Reasoning = current.Reasoning;
            refreshed.Speed = current.Speed;
            refreshed.ContextWindow = current.ContextWindow;
        }
        refreshed.WorkspaceOverride = current.WorkspaceOverride;
        refreshed.Cwd = current.Cwd;
        refreshed.RuntimeWorkspaceRoots = current.RuntimeWorkspaceRoots;
        refreshed.ExecutionWorkspaceOverride = current.ExecutionWorkspaceOverride;
        refreshed.Extensions = current.Extensions;
        refreshed.CustomTools = current.CustomTools;
        refreshed.AutomationTaskDirectory = current.AutomationTaskDirectory;
    }

    private void ValidateRuntimeConfiguration(ThreadConfiguration config)
    {
        var currentConfig = appConfigMonitor?.Current;
        if (currentConfig == null)
            return;

        AppServerRuntimeRequestValidator.NormalizeCompleteModelConfiguration(currentConfig, config);

        if (config.Reasoning != null)
        {
            AppServerRuntimeRequestValidator.ValidateReasoningForRuntime(
                currentConfig,
                config.ProviderId,
                config.Model,
                config.Reasoning);
        }

        if (config.ContextWindow != null)
        {
            AppServerRuntimeRequestValidator.ValidateContextWindowForRuntime(
                currentConfig,
                config.ProviderId,
                config.Model,
                config.ContextWindow);
        }
    }

    private static Contract.AgentProfileAudit ToContract(AgentProfileAuditRecord audit) => new()
    {
        Event = audit.Event,
        Code = audit.Code,
        Timestamp = audit.Timestamp,
        ProfileId = OmitIfNull(audit.ProfileId),
        Source = OmitIfNull(audit.Source),
        ThreadId = OmitIfNull(audit.ThreadId),
        Status = audit.Status,
        Fields = audit.Fields
    };

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : new Protocol.Optional<T?>(value);

    internal static AppServerException MapError(AgentProfileException ex) =>
        ex.Kind switch
        {
            AgentProfileErrorKind.NotFound => AppServerErrors.AgentProfileNotFound(ex.Message),
            AgentProfileErrorKind.ValidationFailed => AppServerErrors.AgentProfileValidationFailed(ex.Message, ex.Diagnostics),
            AgentProfileErrorKind.Protected => AppServerErrors.AgentProfileProtected(ex.Message),
            AgentProfileErrorKind.SourceUnavailable => AppServerErrors.AgentProfileSourceUnavailable(ex.Message),
            AgentProfileErrorKind.Conflict => AppServerErrors.AgentProfileConflict(ex.Message),
            _ => AppServerErrors.AgentProfileValidationFailed(ex.Message, ex.Diagnostics)
        };
}
