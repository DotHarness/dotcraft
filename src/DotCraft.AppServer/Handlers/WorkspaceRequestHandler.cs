using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Memory;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;

namespace DotCraft.AppServer;

internal sealed class WorkspaceRequestHandler(
    ICommitMessageSuggester? commitMessageSuggest,
    IReadOnlyList<ConfigSchemaSection> configSchema,
    MemoryStore? memoryStore,
    DreamsService? dreamsService,
    IAppConfigMonitor? appConfigMonitor) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.WorkspaceCommitMessageSuggest, HandleWorkspaceCommitMessageSuggestAsync);
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
        catch (ThreadNotFoundException ex)
        {
            throw AppServerErrors.ThreadNotFound(ex.ThreadId);
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

    private async Task<object?> HandleMemoryResetAsync(
        AppServerTypedRequest<Protocol.RpcEmpty> request,
        CancellationToken ct)
    {
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
            if (dreamsService != null)
            {
                await dreamsService.ResetAsync(() => MemoryScopes.ClearAll(memoryStore), ct).ConfigureAwait(false);
            }
            else
            {
                MemoryScopes.ClearAll(memoryStore);
                new DreamsArtifacts(Path.GetDirectoryName(memoryStore.MemoryDirectoryPath)!).ClearAll();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw AppServerErrors.InternalError($"Failed to reset memory: {ex.Message}");
        }

        appConfigMonitor?.NotifyChanged(
            Protocol.AppServer.AppServerMethodNames.MemoryReset,
            [ConfigChangeRegions.Memory]);

        return new Contract.MemoryResetResult();
    }

    private static T? ValueOrDefault<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;
}
