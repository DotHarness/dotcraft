using DotCraft.Agents;

namespace DotCraft.Protocol.AppServer;

internal sealed class AgentProfileRequestHandler(
    ISessionService sessionService,
    string? workspaceCraftPath,
    string? hostWorkspacePath) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(AppServerMethods.AgentProfileList, HandleListAsync);
        table.Map(AppServerMethods.AgentProfileRead, HandleReadAsync);
        table.Map(AppServerMethods.AgentProfileValidate, HandleValidateAsync);
        table.Map(AppServerMethods.AgentProfileUpsert, HandleUpsertAsync);
        table.Map(AppServerMethods.AgentProfileRemove, HandleRemoveAsync);
        table.Map(AppServerMethods.AgentProfileRefreshThread, HandleRefreshThreadAsync);
    }

    private async Task<object?> HandleListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<AgentProfileListParams>(msg);
        try
        {
            var store = CreateStore();
            var profiles = store
                .List(p.Source, p.IncludeInvalid ?? true)
                .Select(profile => ToWire(profile))
                .ToList();
            await AnnotateStaleThreadsAsync(store, profiles, ct);
            return new AgentProfileListResult
            {
                Profiles = profiles
            };
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private async Task<object?> HandleReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<AgentProfileReadParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");

        try
        {
            var profile = ToWire(CreateStore().Read(p.Id, p.Source), includeRawContent: true, includeCompiledConfig: true);
            await AnnotateStaleThreadsAsync(CreateStore(), [profile], ct);
            return new AgentProfileReadResult
            {
                Profile = profile
            };
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private Task<object?> HandleValidateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = AppServerParams.Get<AgentProfileValidateParams>(msg);
        if (p.RawContent == null)
            throw AppServerErrors.InvalidParams("'rawContent' is required.");

        try
        {
            var validation = CreateStore().ValidateRaw(p.RawContent, p.Source);
            return Task.FromResult<object?>(new AgentProfileValidateResult
            {
                Valid = validation.Valid,
                Diagnostics = validation.Diagnostics.Select(ToWire).ToList(),
                Summary = new AgentProfileSummaryWire
                {
                    Id = validation.Id,
                    Description = validation.Description
                },
                LockedFields = validation.LockedFields,
                RestrictedFields = validation.RestrictedFields,
                CompiledConfig = validation.CompiledConfiguration
            });
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private Task<object?> HandleUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = AppServerParams.Get<AgentProfileUpsertParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");
        if (string.IsNullOrWhiteSpace(p.Source))
            throw AppServerErrors.InvalidParams("'source' is required.");
        if (p.RawContent == null)
            throw AppServerErrors.InvalidParams("'rawContent' is required.");

        try
        {
            var profile = CreateStore().Upsert(p.Id, p.Source, p.RawContent);
            return Task.FromResult<object?>(new AgentProfileUpsertResult
            {
                Profile = ToWire(profile, includeRawContent: true, includeCompiledConfig: true)
            });
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private Task<object?> HandleRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = AppServerParams.Get<AgentProfileRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");
        if (string.IsNullOrWhiteSpace(p.Source))
            throw AppServerErrors.InvalidParams("'source' is required.");

        try
        {
            return Task.FromResult<object?>(new AgentProfileRemoveResult
            {
                Removed = CreateStore().Remove(p.Id, p.Source)
            });
        }
        catch (AgentProfileException ex)
        {
            throw MapError(ex);
        }
    }

    private async Task<object?> HandleRefreshThreadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = AppServerParams.Get<AgentProfileRefreshThreadParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var store = CreateStore();
        string? requestedProfileId = null;
        try
        {
            var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
            var profileId = string.IsNullOrWhiteSpace(p.ProfileId)
                ? thread.Configuration?.AgentProfileId
                : p.ProfileId.Trim();
            if (string.IsNullOrWhiteSpace(profileId))
                throw AppServerErrors.InvalidParams("Thread does not have an agent profile id.");

            requestedProfileId = profileId;
            var profile = store.Read(profileId);
            if (!profile.Valid || profile.CompiledConfiguration == null)
                throw new AgentProfileException(AgentProfileErrorKind.ValidationFailed, "Agent profile validation failed.", profile.Diagnostics);

            var beforeFingerprint = thread.Configuration?.AgentProfileFingerprint;
            var refreshed = store.ResolveProfileConfiguration(profileId);
            PreserveUnrelatedThreadFields(thread.Configuration, refreshed);
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

            return new AgentProfileRefreshThreadResult
            {
                ThreadId = thread.Id,
                Profile = ToWire(profile, includeRawContent: true, includeCompiledConfig: true),
                Config = refreshed,
                WasStale = !string.Equals(beforeFingerprint, profile.Fingerprint, StringComparison.Ordinal),
                Audit = ToWire(audit)
            };
        }
        catch (AgentProfileException ex)
        {
            var failure = new AgentProfileAuditRecord
            {
                Event = "agentProfile.thread.refresh",
                Code = "AgentProfileThreadRefreshFailed",
                ProfileId = requestedProfileId ?? p.ProfileId,
                ThreadId = p.ThreadId,
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

    private AgentProfileStore CreateStore() =>
        new(ResolveWorkspaceCraftPath());

    private string? ResolveWorkspaceCraftPath()
    {
        if (!string.IsNullOrWhiteSpace(workspaceCraftPath))
            return workspaceCraftPath;
        if (!string.IsNullOrWhiteSpace(hostWorkspacePath))
            return Path.Combine(hostWorkspacePath, ".craft");
        return null;
    }

    internal static AgentProfileEntryWire ToWire(
        AgentProfileEntry profile,
        bool includeRawContent = false,
        bool includeCompiledConfig = false) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Description = profile.Description,
        Source = profile.Source,
        Path = profile.Path,
        PluginId = profile.PluginId,
        Fingerprint = profile.Fingerprint,
        Valid = profile.Valid,
        IsBuiltIn = profile.IsBuiltIn,
        ReadOnly = profile.ReadOnly,
        Shadowed = profile.Shadowed,
        ShadowedBy = profile.ShadowedBy,
        SourceStack = profile.SourceStack,
        LockedFields = profile.LockedFields,
        RestrictedFields = profile.RestrictedFields,
        TrustRestricted = profile.TrustRestricted,
        Diagnostics = profile.Diagnostics.Select(ToWire).ToList(),
        RawContent = includeRawContent ? profile.RawContent : null,
        CompiledConfig = includeCompiledConfig ? profile.CompiledConfiguration : null
    };

    internal static AgentProfileDiagnosticWire ToWire(AgentProfileDiagnostic diagnostic) => new()
    {
        Severity = diagnostic.Severity,
        Code = diagnostic.Code,
        Message = diagnostic.Message
    };

    private async Task AnnotateStaleThreadsAsync(
        AgentProfileStore store,
        IReadOnlyList<AgentProfileEntryWire> profiles,
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
            .Where(profile => profile.Valid && !profile.Shadowed)
            .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
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

            if (!string.Equals(config.AgentProfileFingerprint, profile.Fingerprint, StringComparison.Ordinal))
                profile.StaleThreadIds.Add(thread.Id);
        }
    }

    private static void PreserveUnrelatedThreadFields(ThreadConfiguration? current, ThreadConfiguration refreshed)
    {
        if (current == null)
            return;

        refreshed.ProviderId ??= current.ProviderId;
        refreshed.Model ??= current.Model;
        refreshed.Reasoning ??= current.Reasoning;
        refreshed.WorkspaceOverride = current.WorkspaceOverride;
        refreshed.ExecutionWorkspaceOverride = current.ExecutionWorkspaceOverride;
        refreshed.Extensions = current.Extensions;
        refreshed.CustomTools = current.CustomTools;
        refreshed.AutomationTaskDirectory = current.AutomationTaskDirectory;
    }

    private static AgentProfileAuditWire ToWire(AgentProfileAuditRecord audit) => new()
    {
        Event = audit.Event,
        Code = audit.Code,
        Timestamp = audit.Timestamp,
        ProfileId = audit.ProfileId,
        Source = audit.Source,
        ThreadId = audit.ThreadId,
        Status = audit.Status,
        Fields = audit.Fields
    };

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
