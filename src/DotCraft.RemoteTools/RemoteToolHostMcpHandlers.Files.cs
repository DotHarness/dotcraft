using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Security;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostMcpHandlers
{
    private readonly ConcurrentDictionary<string, HostTransfer> _transfers = new(StringComparer.Ordinal);

    private async ValueTask<JsonNode?> OpenFileTransferAsync(JsonRpcRequest request, string peerId, CancellationToken ct)
    {
        var input = Deserialize<FileTransferOpen>(request);
        var root = _leases.Validate(input.LeaseId, input.WorkspaceId);
        var peer = RequirePeer(RequireState(), peerId, input.WorkspaceId);
        var config = HostWorkspaceRuntime.LoadWorkspaceConfig(_storage.GlobalConfigPath, root);
        var invocationId = "transfer_" + Guid.NewGuid().ToString("N");
        var approval = new HostInvocationApprovalService.Invocation(peer, invocationId, root, _approvalPresenter, ct);
        using var scope = HostInvocationApprovalService.Begin(approval);
        var guard = new FileAccessGuard(root, approvalService: new HostInvocationApprovalService(),
            blacklist: new PathBlacklist(config.Security.BlacklistedPaths));
        var path = guard.ResolvePath(input.Path);
        var limit = config.Tools.File.MaxTransferBytes;
        async Task Authorize(string p, string operation, CancellationToken token)
        {
            var args = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement(p) };
            await AuthorizeAsync(operation == "write" ? "WriteFile" : "ReadFile", args, RequireState(), approval, root, token)
                .ConfigureAwait(false);
        }
        await Authorize(path, input.Write ? "write" : "read", ct).ConfigureAwait(false);
        var manifest = input.Write ? input.Manifest ?? throw new IOException("A write manifest is required.")
            : await TransferFileTree.DescribeAsync(path, guard, limit, ct: ct).ConfigureAwait(false);
        var session = new FileTransferSession(path, manifest, input.Write, input.Overwrite);
        try
        {
            await session.PrepareAsync(limit, Authorize, ct).ConfigureAwait(false);
            if (RequirePeer(RequireState(), peerId, input.WorkspaceId).AuthorizationRevision != peer.AuthorizationRevision)
                throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Authorization changed.");
            _leases.CommitArtifact(input.LeaseId, input.WorkspaceId, () =>
            {
                if (_transfers.Count >= 32) throw new IOException("Too many active file transfers.");
                _transfers[invocationId] = new(input, peerId, peer.AuthorizationRevision, session);
            });
            return JsonSerializer.SerializeToNode(new FileTransferOpened(invocationId, path, manifest), RemoteToolHostProtocol.JsonOptions);
        }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async ValueTask<JsonNode?> FileTransferPartAsync(JsonRpcRequest request, string peerId, string operation, CancellationToken ct)
    {
        var input = Deserialize<FileTransferPart>(request);
        if (!_transfers.TryGetValue(input.TransferId, out var transfer) || transfer.PeerId != peerId)
            throw new RemoteToolHostException(RemoteToolErrorCodes.LeaseLost, "File transfer is unavailable.");
        if (operation == RemoteFileTransferProtocol.Close)
        {
            if (_transfers.TryRemove(input.TransferId, out _)) await transfer.Session.DisposeAsync().ConfigureAwait(false);
            return new JsonObject();
        }
        var session = transfer.Session;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Stopping.Token);
        var token = linked.Token;
        await session.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ValidateTransfer(transfer);
            var entry = session.Entry(input.Entry, operation != RemoteFileTransferProtocol.Read);
            var path = TransferFileTree.ResolveEntry(session.Root, entry.Path);
            var config = HostWorkspaceRuntime.LoadWorkspaceConfig(_storage.GlobalConfigPath,
                _leases.Validate(transfer.Open.LeaseId, transfer.Open.WorkspaceId));
            if (new PathBlacklist(config.Security.BlacklistedPaths).IsBlacklisted(path))
                throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Transfer path is blacklisted.");
            TransferFileTree.RejectLinks(path);
            using var activity = _activity?.Begin(peerId, "RemoteToolHost.Transfer", operation);
            if (operation == RemoteFileTransferProtocol.Read)
            {
                var bytes = await session.ReadAsync(input.Entry, input.Offset, token).ConfigureAwait(false);
                return JsonSerializer.SerializeToNode(new FileTransferChunk(Convert.ToBase64String(bytes)), RemoteToolHostProtocol.JsonOptions);
            }
            if (operation == RemoteFileTransferProtocol.Write)
            {
                if (input.Base64 is null || input.Base64.Length > (RemoteFileTransferProtocol.ChunkBytes + 2) / 3 * 4)
                    throw new IOException("Invalid transfer chunk size.");
                await session.WriteAsync(input.Entry, input.Offset, Convert.FromBase64String(input.Base64), token).ConfigureAwait(false);
            }
            else
            {
                await session.CommitAsync(input.Entry, commit =>
                {
                    ValidateTransfer(transfer);
                    _leases.CommitArtifact(transfer.Open.LeaseId, transfer.Open.WorkspaceId, commit);
                }, token).ConfigureAwait(false);
                _storage.AppendAudit(new(DateTimeOffset.UtcNow, null, null, transfer.Open.WorkspaceId,
                    "RemoteToolHost.Transfer", input.TransferId, "ok", 0, false));
            }
            return new JsonObject();
        }
        finally { session.Gate.Release(); }
    }

    private void ValidateTransfer(HostTransfer transfer)
    {
        _leases.Validate(transfer.Open.LeaseId, transfer.Open.WorkspaceId);
        var state = RequireState();
        var peer = RequirePeer(state, transfer.PeerId, transfer.Open.WorkspaceId);
        var tool = transfer.Session.Write ? "WriteFile" : "ReadFile";
        if (peer.AuthorizationRevision != transfer.Revision || state.ToolPolicies.GetValueOrDefault(tool) == "deny")
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Transfer authorization changed.");
    }

    internal void ReleaseFileTransfers(string leaseId)
    {
        foreach (var (id, transfer) in _transfers)
            if (transfer.Open.LeaseId == leaseId && _transfers.TryRemove(id, out _))
                _ = transfer.Session.DisposeAsync().AsTask();
    }

    private sealed record HostTransfer(FileTransferOpen Open, string PeerId, long Revision, FileTransferSession Session);
}
