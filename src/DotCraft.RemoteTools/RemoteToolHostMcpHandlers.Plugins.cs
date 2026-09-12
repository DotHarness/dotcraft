using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostMcpHandlers
{
    private readonly ConcurrentDictionary<string, PluginPreparation> _pluginPreparations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _pluginApprovals = new(StringComparer.Ordinal);

    private async ValueTask<JsonNode?> PreparePluginsAsync(JsonRpcRequest request, string peerId, CancellationToken ct)
    {
        var input = Deserialize<PluginPrepareRequest>(request);
        using var call = EnterCall(input.LeaseId, input.WorkspaceId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, call.Token);
        ct = linked.Token;
        var workspace = ValidateLease(input.LeaseId, input.WorkspaceId);
        var peer = RequirePeer(RequireState(), peerId, input.WorkspaceId);
        if (string.IsNullOrWhiteSpace(input.ThreadId) || string.IsNullOrWhiteSpace(input.Mode)
            || input.Bundles.Count is 0 or > 128 || input.Tools.Count is 0 or > 10000
            || input.Bundles.Select(item => item.Bundle.PluginId).Distinct(StringComparer.Ordinal).Count() != input.Bundles.Count)
            throw new RemoteToolHostException(ToolErrorCodes.InputInvalid, "Invalid plugin preparation manifest.");
        input = input with { ThreadId = ScopedThread(input.ThreadId) };
        using var threadCall = EnterThread(input.ThreadId, ct);
        ct = threadCall.Token;
        var id = "prepare_" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(_storage.RootPath, "workspaces", input.WorkspaceId, "plugins");
        var stagedRoot = Path.Combine(_storage.RootPath, "workspaces", input.WorkspaceId, "sessions", SessionId, "plugin-staging", id);
        var files = new List<RemotePluginBundleFiles>();
        var uploads = new List<PluginUpload>();
        var preparation = new PluginPreparation(input, peerId, peer.AuthorizationRevision, files, uploads, stagedRoot, root);
        try
        {
            var limit = HostWorkspaceRuntime.LoadWorkspaceConfig(_storage.GlobalConfigPath, workspace).Tools.File.MaxTransferBytes;
            long total = 0;
            foreach (var item in input.Bundles)
            {
                var bundle = item.Bundle;
                if (PluginIds.Canonicalize(bundle.PluginId) != bundle.PluginId
                    || bundle.Settings.ValueKind != JsonValueKind.Object || bundle.SourceRevision < 1
                    || string.IsNullOrWhiteSpace(bundle.SourceGeneration))
                    throw new RemoteToolHostException(ToolErrorCodes.InputInvalid, "Invalid plugin bundle identity or settings.");
                var destination = TransferFileTree.ResolveEntry(root, bundle.PluginId);
                TransferFileTree.RejectLinks(destination);
                if (Directory.Exists(destination) && PluginExecutionHost.MatchesBundle(destination, bundle))
                {
                    files.Add(new(bundle, destination));
                    uploads.Add(new(bundle.PluginId, null));
                    continue;
                }
                var staged = TransferFileTree.ResolveEntry(stagedRoot, bundle.PluginId);
                var session = new FileTransferSession(staged, item.Manifest, true, false);
                try
                {
                    await session.PrepareAsync(limit - total, (_, _, _) => Task.CompletedTask, ct).ConfigureAwait(false);
                    total += item.Manifest.Entries.Sum(entry => entry.Length);
                    var transferId = "plugin_" + Guid.NewGuid().ToString("N");
                    var open = new FileTransferOpen(input.LeaseId, input.WorkspaceId, staged, true, false, item.Manifest);
                    CommitArtifact(input.LeaseId, input.WorkspaceId, () =>
                    {
                        _transfers[transferId] = new(open, peerId, peer.AuthorizationRevision, session, PluginBundle: true);
                    });
                    uploads.Add(new(bundle.PluginId, transferId));
                    files.Add(new(bundle, staged));
                }
                catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
            }
            CommitArtifact(input.LeaseId, input.WorkspaceId, () =>
            {
                if (_pluginPreparations.Count >= 32) throw new IOException("Too many plugin preparations are pending.");
                _pluginPreparations[id] = preparation;
            });
            return JsonSerializer.SerializeToNode(new PluginPrepareResponse(id, uploads), RemoteToolHostProtocol.JsonOptions);
        }
        catch
        {
            await CleanupPreparationAsync(preparation).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<JsonNode?> ActivatePluginsAsync(JsonRpcRequest request, string peerId, CancellationToken ct)
    {
        var input = Deserialize<PluginActivateRequest>(request);
        if (!_pluginPreparations.TryGetValue(input.PreparationId, out var pending) || pending.PeerId != peerId)
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemoteToolUnavailable, "The plugin preparation is unavailable.");
        using var threadCall = EnterThread(pending.Input.ThreadId, ct);
        ct = threadCall.Token;
        using var call = EnterCall(pending.Input.LeaseId, pending.Input.WorkspaceId);
        if (!pending.TryStart())
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemoteToolUnavailable, "The plugin preparation is unavailable.");
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, call.Token, pending.Stopping.Token);
            ct = linked.Token;
            var workspace = ValidateLease(pending.Input.LeaseId, pending.Input.WorkspaceId);
            var peer = RequirePeer(RequireState(), peerId, pending.Input.WorkspaceId);
            if (peer.AuthorizationRevision != pending.AuthorizationRevision)
                throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Authorization changed.");
            foreach (var upload in pending.Uploads.Where(upload => upload.TransferId is not null))
            {
                if (!_transfers.TryRemove(upload.TransferId!, out var transfer))
                    throw new RemoteToolHostException(RemoteToolErrorCodes.RemoteToolUnavailable, "A plugin upload is incomplete.");
                await transfer.Session.DisposeAsync().ConfigureAwait(false);
                if (!transfer.Session.IsComplete)
                    throw new RemoteToolHostException(RemoteToolErrorCodes.RemoteToolUnavailable, "A plugin upload is incomplete.");
            }
            foreach (var files in pending.Files)
                if (!PluginExecutionHost.MatchesBundle(files.RootPath, files.Bundle))
                    throw new RemoteToolHostException(ToolErrorCodes.InputInvalid, "Plugin bundle fingerprint mismatch.");
            var approval = new HostInvocationApprovalService.Invocation(peer, input.PreparationId, workspace, _approvalPresenter, ct);
            using var approvalScope = HostInvocationApprovalService.Begin(approval);
            var required = pending.Files.Where(file => !IsPluginApproved(pending.Input.LeaseId, file.Bundle)).ToArray();
            if (required.Length > 0)
                await approval.RequestAsync("plugin", "activate", string.Join("\n", required.Select(file =>
                    file.Bundle.PluginId + " " + file.Bundle.ContentFingerprint))).ConfigureAwait(false);
            if (RequirePeer(RequireState(), peerId, pending.Input.WorkspaceId).AuthorizationRevision != peer.AuthorizationRevision)
                throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Authorization changed.");
            ct.ThrowIfCancellationRequested();
            var runtime = await GetRuntimeAsync(pending.Input.LeaseId, pending.Input.WorkspaceId, workspace, RequireState(), ct).ConfigureAwait(false);
            using var terminalScope = ExecutionSessionTerminals.Enter(runtime.Terminals, pending.Input.ThreadId);
            var result = await runtime.Plugins.PrepareAsync(pending.Input, pending.Files, ct,
                () => CommitArtifact(pending.Input.LeaseId, pending.Input.WorkspaceId, () =>
                {
                    if (RequirePeer(RequireState(), peerId, pending.Input.WorkspaceId).AuthorizationRevision != peer.AuthorizationRevision)
                        throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied, "Authorization changed.");
                    StorePreparedBundles(pending);
                })).ConfigureAwait(false);
            CommitArtifact(pending.Input.LeaseId, pending.Input.WorkspaceId, () =>
            {
                lock (_gate)
                {
                    if (!_pluginApprovals.TryGetValue(pending.Input.LeaseId, out var grants))
                        _pluginApprovals[pending.Input.LeaseId] = grants = new(StringComparer.Ordinal);
                    foreach (var file in required) grants.Add(ApprovalKey(file.Bundle));
                }
            });
            return JsonSerializer.SerializeToNode(result, RemoteToolHostProtocol.JsonOptions);
        }
        finally
        {
            _pluginPreparations.TryRemove(input.PreparationId, out _);
            await CleanupPreparationAsync(pending).ConfigureAwait(false);
        }
    }

    private bool IsPluginApproved(string leaseId, RemotePluginBundle bundle)
    {
        lock (_gate) return _pluginApprovals.TryGetValue(leaseId, out var grants) && grants.Contains(ApprovalKey(bundle));
    }

    private static string ApprovalKey(RemotePluginBundle bundle) => bundle.PluginId + ":" + bundle.ContentFingerprint;

    private static void StorePreparedBundles(PluginPreparation preparation)
    {
        Directory.CreateDirectory(preparation.InstallRoot);
        foreach (var upload in preparation.Uploads.Where(upload => upload.TransferId is not null))
        {
            var source = preparation.Files.Single(file => file.Bundle.PluginId == upload.PluginId).RootPath;
            var destination = TransferFileTree.ResolveEntry(preparation.InstallRoot, upload.PluginId);
            TransferFileTree.RejectLinks(destination);
            var backup = Path.Combine(preparation.StagedRoot, Guid.NewGuid().ToString("N"));
            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            try { Directory.Move(source, destination); }
            catch
            {
                if (Directory.Exists(backup)) Directory.Move(backup, destination);
                throw;
            }
        }
    }

    private async Task CleanupPreparationAsync(PluginPreparation preparation)
    {
        try
        {
            foreach (var upload in preparation.Uploads.Where(upload => upload.TransferId is not null))
                if (_transfers.TryRemove(upload.TransferId!, out var transfer))
                    await transfer.Session.DisposeAsync().ConfigureAwait(false);
            if (Directory.Exists(preparation.StagedRoot)) Directory.Delete(preparation.StagedRoot, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        finally
        {
            preparation.Stopping.Dispose();
            preparation.Completed.TrySetResult();
        }
    }

    private async ValueTask<JsonNode?> AbortPluginsAsync(JsonRpcRequest request, string peerId, CancellationToken ct)
    {
        var input = Deserialize<PluginActivateRequest>(request);
        if (_pluginPreparations.TryGetValue(input.PreparationId, out var pending) && pending.PeerId == peerId)
        {
            using var call = EnterCall(pending.Input.LeaseId, pending.Input.WorkspaceId);
            if (pending.TryAbortPending())
            {
                if (_pluginPreparations.TryRemove(input.PreparationId, out _))
                    await CleanupPreparationAsync(pending).ConfigureAwait(false);
            }
            else
            {
                await pending.Stopping.CancelAsync().ConfigureAwait(false);
                await pending.Completed.Task.WaitAsync(ct).ConfigureAwait(false);
            }
        }
        return new JsonObject();
    }

    private async ValueTask<JsonNode?> ReleasePluginThreadAsync(JsonRpcRequest request, string peerId, CancellationToken ct)
    {
        var input = Deserialize<PluginThreadRelease>(request);
        using var call = EnterCall(input.LeaseId, input.WorkspaceId);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, call.Token);
        var workspace = ValidateLease(input.LeaseId, input.WorkspaceId);
        RequirePeer(RequireState(), peerId, input.WorkspaceId);
        var runtime = await GetRuntimeAsync(input.LeaseId, input.WorkspaceId, workspace, RequireState(), linked.Token).ConfigureAwait(false);
        var threadId = ScopedThread(input.ThreadId);
        using var terminalScope = ExecutionSessionTerminals.Enter(runtime.Terminals, threadId);
        await runtime.ReleasePluginThreadAsync(threadId, linked.Token).ConfigureAwait(false);
        return new JsonObject();
    }

    private sealed record PluginPreparation(PluginPrepareRequest Input, string PeerId, long AuthorizationRevision,
        IReadOnlyList<RemotePluginBundleFiles> Files, IReadOnlyList<PluginUpload> Uploads, string StagedRoot, string InstallRoot)
    {
        private const int Pending = 0;
        private const int Activating = 1;
        private const int Aborted = 2;
        private int _state;
        internal CancellationTokenSource Stopping { get; } = new();
        internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool TryStart() => Interlocked.CompareExchange(ref _state, Activating, Pending) == Pending;
        internal bool TryAbortPending() => Interlocked.CompareExchange(ref _state, Aborted, Pending) == Pending;
    }
}
