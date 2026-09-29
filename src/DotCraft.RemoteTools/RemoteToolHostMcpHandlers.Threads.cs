using System.Text.Json.Nodes;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostMcpHandlers
{
    private RemoteOperationScope.Call EnterThread(string threadId, CancellationToken ct)
    {
        lock (_gate)
            return _threads[threadId].TryEnter(ct) ?? throw Lost();
    }

    private async ValueTask<JsonNode?> ReleaseExecutionThreadAsync(JsonRpcRequest request, string peerId, CancellationToken ct)
    {
        var input = Deserialize<PluginThreadRelease>(request);
        ValidateLease(input.LeaseId, input.WorkspaceId);
        RequirePeer(RequireState(), peerId, input.WorkspaceId);
        var threadId = ScopedThread(input.ThreadId);
        RemoteOperationScope operations;
        lock (_gate) operations = _threads[threadId];
        await operations.DisposeAsync().ConfigureAwait(false);
        _activity?.ForgetThread(threadId);
        foreach (var pending in _pluginPreparations.Where(pair => pair.Value.Input.ThreadId == threadId).ToArray())
            if (_pluginPreparations.TryRemove(pending.Key, out var preparation))
                await CleanupPreparationAsync(preparation).ConfigureAwait(false);
        HostWorkspaceRuntime? runtime;
        lock (_gate) runtime = _runtime;
        if (runtime is not null)
        {
            using var terminalScope = ExecutionSessionTerminals.Enter(runtime.Terminals, threadId);
            await runtime.ReleasePluginThreadAsync(threadId, ct).ConfigureAwait(false);
            await runtime.Terminals.CleanThreadAsync(threadId, ct).ConfigureAwait(false);
            await runtime.Terminals.DeleteThreadArtifactsAsync(threadId, ct).ConfigureAwait(false);
        }
        return new JsonObject();
    }

    private ValueTask<JsonNode?> ReportTurn(JsonRpcRequest request, string peerId, CancellationToken ct)
    {
        var input = Deserialize<ExecutionTurnReport>(request);
        ValidateLease(input.LeaseId, input.WorkspaceId);
        RequirePeer(RequireState(), peerId, input.WorkspaceId);
        var status = RemoteToolHostProtocol.ParseTurnStatus(input.Status);
        if (status is null || string.IsNullOrWhiteSpace(input.TurnId))
            throw new RemoteToolHostException(ToolErrorCodes.InputInvalid, "A Turn report needs a Turn id and a known status.");
        var threadId = ScopedThread(input.ThreadId);
        using var call = EnterThread(threadId, ct);
        _activity?.ReportTurn(SessionId, peerId, threadId, input.TurnId, status.Value);
        return ValueTask.FromResult<JsonNode?>(new JsonObject());
    }
}
