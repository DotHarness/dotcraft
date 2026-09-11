using System.Text.Json.Nodes;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostClient
{
    private readonly Dictionary<string, ThreadRemoteSnapshot> _snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<(string ThreadId, string LeaseId), PreparedSnapshot> _preparedSnapshots = new();
    private readonly Dictionary<(string ThreadId, string LeaseId), PreparationFailure> _preparationFailures = new();

    public void UpdateRemoteToolSnapshot(string threadId, EffectiveToolSnapshot snapshot, string mode)
    {
        var visible = snapshot.ModelVisibleDefinitions.Concat(snapshot.DeferredDefinitions.Values.SelectMany(items => items))
            .Select(definition => definition.Id).ToHashSet();
        var registrations = snapshot.Registrations.Values
            .Where(item => visible.Contains(item.Definition.Id) && RemoteToolMetadata.IsRpcEligible(item)).ToArray();
        lock (_stateGate)
        {
            if (_snapshots.TryGetValue(threadId, out var previous) && previous.Revision >= snapshot.Revision) return;
            _snapshots[threadId] = new(snapshot.Revision, mode, registrations);
        }
    }

    public async ValueTask PrepareTurnAsync(string threadId, EffectiveToolSnapshot snapshot, string mode,
        CancellationToken cancellationToken = default)
    {
        await _routeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            UpdateRemoteToolSnapshot(threadId, snapshot, mode);
            if (!TryGetRoute(threadId, out var route)) return;
            await PreparePluginsAsync(threadId, RequireLease(route), cancellationToken, snapshot.Revision).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_stateGate)
                if (_routes.TryGetValue(threadId, out var current))
                {
                    var key = (threadId, current.LeaseId);
                    _preparedSnapshots.Remove(key);
                    _preparationFailures[key] = new(snapshot.Revision,
                        ex is RemoteToolHostException remote ? remote.Code : RemoteToolErrorCodes.RemoteToolUnavailable,
                        ex.Message);
                }
        }
        finally { _routeGate.Release(); }
    }

    private async Task PreparePluginsAsync(string threadId, SharedLease lease, CancellationToken ct, long? expectedRevision = null)
    {
        ThreadRemoteSnapshot? snapshot;
        lock (_stateGate)
        {
            _snapshots.TryGetValue(threadId, out snapshot);
            if (expectedRevision is not null && snapshot?.Revision != expectedRevision)
                throw new RemoteToolHostException(RemoteToolErrorCodes.RemoteToolUnavailable, "The captured tool snapshot is no longer current.");
            if (snapshot is null || (_preparedSnapshots.TryGetValue((threadId, lease.Route.LeaseId), out var prepared)
                && ReferenceEquals(prepared.Snapshot, snapshot))) return;
            _preparedSnapshots.Remove((threadId, lease.Route.LeaseId));
            _preparationFailures.Remove((threadId, lease.Route.LeaseId));
        }
        var sources = snapshot.Registrations.Select(RemoteToolMetadata.SourceBinding)
            .OfType<IRemoteToolSourceBinding>().DistinctBy(source => source.SourceId).ToArray();
        if (sources.Length == 0)
        {
            if (lease.SupportsPlugins)
                await SendAsync<PluginThreadRelease, JsonObject>(lease.Session.Client, RemotePluginProtocol.ReleaseThread,
                    new(lease.Route.LeaseId, lease.Route.WorkspaceId, threadId), ct).ConfigureAwait(false);
            return;
        }
        if (!lease.SupportsPlugins)
            throw new RemoteToolHostException(RemoteToolErrorCodes.ProtocolMismatch, "The remote Host does not support plugin preparation.");

        var exports = new List<RemoteToolSourceExport>();
        PluginPrepareResponse? pending = null;
        try
        {
            foreach (var source in sources) exports.Add(await source.ExportAsync(ct).ConfigureAwait(false));
            var bundles = exports.SelectMany(export => export.Bundles).GroupBy(files => files.Bundle.PluginId, StringComparer.Ordinal)
                .Select(group =>
                {
                    if (group.Select(files => files.Bundle.SourceGeneration).Distinct().Count() != 1)
                        throw new RemoteToolHostException(RemoteToolErrorCodes.RemoteToolUnavailable, "Plugin sources changed during preparation.");
                    return group.First();
                }).ToArray();
            var manifests = new List<PluginBundleTransfer>();
            foreach (var files in bundles)
                manifests.Add(new(files.Bundle, await TransferFileTree.DescribeAsync(files.RootPath,
                    new FileAccessGuard(files.RootPath), _maxTransferBytes, ct).ConfigureAwait(false)));
            var contracts = snapshot.Registrations.Select(RemoteToolMetadata.NativeDefinition)
                .Where(definition => definition.Id.Kind == ToolSourceKind.PluginNative)
                .Select(definition => new RemoteToolContractSummary(definition.Id.ToString(), definition.Name.ToString(),
                    RemoteToolContractHasher.Compute(definition))).ToArray();
            pending = await SendAsync<PluginPrepareRequest, PluginPrepareResponse>(lease.Session.Client,
                RemotePluginProtocol.Prepare, new(lease.Route.LeaseId, lease.Route.WorkspaceId, threadId,
                    snapshot.Revision, snapshot.Mode, manifests, contracts), ct).ConfigureAwait(false);
            foreach (var upload in pending.Uploads.Where(upload => upload.TransferId is not null))
            {
                var files = bundles.Single(files => files.Bundle.PluginId == upload.PluginId);
                var manifest = manifests.Single(item => item.Bundle.PluginId == upload.PluginId).Manifest;
                await UploadPluginAsync(lease, upload.TransferId!, files.RootPath, manifest, ct).ConfigureAwait(false);
            }
            var activated = await SendAsync<PluginActivateRequest, PluginActivateResponse>(lease.Session.Client,
                RemotePluginProtocol.Activate, new(pending.PreparationId), ct).ConfigureAwait(false);
            var bindings = activated.Bindings.ToDictionary(binding => binding.DefinitionId, StringComparer.Ordinal);
            foreach (var contract in contracts)
                if (!bindings.TryGetValue(contract.DefinitionId, out var binding) || binding.ContractHash != contract.ContractHash)
                    throw new RemoteToolHostException(RemoteToolErrorCodes.ToolContractMismatch, "Prepared plugin contracts do not match the captured snapshot.");
            RequireLease(lease.Route);
            lock (_stateGate)
                _preparedSnapshots[(threadId, lease.Route.LeaseId)] = new(snapshot, bindings);
        }
        finally
        {
            foreach (var export in exports) export.Dispose();
            if (pending is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await SendAsync<PluginActivateRequest, JsonObject>(lease.Session.Client, RemotePluginProtocol.Abort,
                        new(pending.PreparationId), timeout.Token).ConfigureAwait(false);
                }
                catch { }
            }
        }
    }

    private async Task UploadPluginAsync(SharedLease lease, string transferId, string root,
        TransferFileManifest manifest, CancellationToken ct)
    {
        for (var index = 0; index < manifest.Entries.Count; index++)
        {
            var entry = manifest.Entries[index];
            if (!entry.IsDirectory)
            {
                await using var file = TransferFileTree.OpenRead(TransferFileTree.ResolveEntry(root, entry.Path));
                long offset = 0;
                do
                {
                    RequireLease(lease.Route);
                    var bytes = new byte[(int)Math.Min(RemoteFileTransferProtocol.ChunkBytes, entry.Length - offset)];
                    await file.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
                    await SendAsync<FileTransferPart, JsonObject>(lease.Session.Client, RemoteFileTransferProtocol.Write,
                        new(transferId, index, offset, Convert.ToBase64String(bytes)), ct).ConfigureAwait(false);
                    offset += bytes.Length;
                } while (offset < entry.Length);
            }
            await SendAsync<FileTransferPart, JsonObject>(lease.Session.Client, RemoteFileTransferProtocol.Commit,
                new(transferId, index), ct).ConfigureAwait(false);
        }
    }

    private async Task ReleasePluginThreadAsync(string threadId, RemoteToolRoute route, CancellationToken ct)
    {
        try
        {
            var lease = RequireLease(route);
            if (lease.SupportsPlugins)
                await SendAsync<PluginThreadRelease, JsonObject>(lease.Session.Client, RemotePluginProtocol.ReleaseThread,
                    new(route.LeaseId, route.WorkspaceId, threadId), ct).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            lock (_stateGate)
            {
                _preparedSnapshots.Remove((threadId, route.LeaseId));
                _preparationFailures.Remove((threadId, route.LeaseId));
            }
        }
    }

    private sealed record ThreadRemoteSnapshot(long Revision, string Mode, IReadOnlyList<ToolRegistration> Registrations);
    private sealed record PreparedSnapshot(ThreadRemoteSnapshot Snapshot,
        IReadOnlyDictionary<string, PluginPreparedBinding> Bindings);
    private sealed record PreparationFailure(long Revision, string Code, string Message);
}
