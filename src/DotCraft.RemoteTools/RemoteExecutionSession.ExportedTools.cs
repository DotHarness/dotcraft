using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

public sealed partial class RemoteExecutionSession
{
    public async ValueTask<IReadOnlyList<RemoteExportedTool>> ListExportedToolsAsync(
        string threadId, CancellationToken cancellationToken = default)
    {
        using var operation = _operations.Enter(cancellationToken);
        var catalog = await ReadCatalogAsync(threadId, Route, operation.Token).ConfigureAwait(false);
        return catalog.Tools.Select(tool =>
        {
            var contract = ParseRemoteContract(tool);
            if (contract is null || contract.ProfileVersion != RemoteToolHostProtocol.ProfileVersion
                || string.IsNullOrWhiteSpace(contract.DefinitionId) || string.IsNullOrWhiteSpace(contract.ContractHash)
                || (contract.PreparedBinding is null) != (contract.SnapshotRevision is null))
                throw new RemoteToolHostException(RemoteToolErrorCodes.ProtocolMismatch,
                    $"The remote tool '{tool.Name}' returned incomplete or unsupported contract metadata.");
            return new RemoteExportedTool(this, threadId, tool, contract.DefinitionId,
                contract.ContractHash, contract.PreparedBinding, contract.SnapshotRevision);
        }).ToArray();
    }

    public async ValueTask<ToolExecutionResult> InvokeAsync(
        RemoteExportedTool tool, string threadId, JsonObject arguments, string? turnId = null,
        CancellationToken cancellationToken = default)
    {
        using var operation = _operations.TryEnter(cancellationToken);
        if (operation is null || !IsAvailable || !ReferenceEquals(tool.Session, this))
            return Failure(RemoteToolErrorCodes.LeaseLost, "The exported tool belongs to another or expired execution session.");
        if (tool.ThreadId != threadId)
            return Failure(RemoteToolErrorCodes.RemoteToolUnavailable, "The exported tool belongs to another thread.");

        var invocationId = "rti_" + Guid.NewGuid().ToString("N");
        var meta = new RemoteInvocationMeta(Route.LeaseId, Route.WorkspaceId, invocationId,
            tool.DefinitionId, tool.ContractHash, threadId, turnId, ResolveRemoteResultLimit(null),
            Math.Clamp(_spillPreviewLines, 1, 500), tool.PreparedBinding, tool.SnapshotRevision);
        return await InvokeRemoteAsync(Route, tool.Name, meta, invocationId, arguments,
            ThreadReleases(threadId), operation.Token).ConfigureAwait(false);
    }

    private async Task<ListToolsResult> ReadCatalogAsync(
        string threadId, RemoteToolRoute route, CancellationToken cancellationToken)
    {
        var lease = RequireLease(route);
        return await lease.Session.Client.ListToolsAsync(new ListToolsRequestParams
        {
            Meta = new JsonObject
            {
                ["dotcraft"] = JsonSerializer.SerializeToNode(
                    new RemoteCatalogScope(route.LeaseId, route.WorkspaceId, threadId),
                    RemoteToolHostProtocol.JsonOptions)
            }
        }, cancellationToken).ConfigureAwait(false);
    }
}
