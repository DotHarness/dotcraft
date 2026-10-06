using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;
using DotCraft.Memory;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;

namespace DotCraft.AppServer;

internal sealed class WorkspaceRequestHandler(
    ICommitMessageSuggester? commitMessageSuggest,
    IWelcomeSuggester? welcomeSuggestionService,
    IReadOnlyList<ConfigSchemaSection> configSchema,
    MemoryStore? memoryStore,
    DreamStore? dreamStore,
    IAppConfigMonitor? appConfigMonitor,
    string? hostWorkspacePath,
    IContextPageManager? contextPageManager) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.WorkspaceCommitMessageSuggest, HandleWorkspaceCommitMessageSuggestAsync);
        table.Map(Protocol.AppServer.AppServerRpc.WelcomeSuggestions, HandleWelcomeSuggestionsAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ConfigSchema, HandleConfigSchemaAsync);
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

    private Task<object?> HandleConfigSchemaAsync(
        AppServerTypedRequest<Contract.ConfigSchemaParams> request,
        CancellationToken ct)
    {
        _ = ct;
        _ = request.Params;
        if (configSchema.Count == 0)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.ConfigSchema);

        return Task.FromResult<object?>(new Contract.ConfigSchemaResult
        {
            Sections = configSchema.Select(ConfigSchemaContractMapper.ToContract).ToArray()
        });
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

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;
}
