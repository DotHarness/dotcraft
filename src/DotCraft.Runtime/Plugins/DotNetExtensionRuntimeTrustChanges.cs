using DotCraft.Plugins;
using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

internal sealed partial class DotNetExtensionRuntimeManager
{
    private readonly object _trustChangeSync = new();
    private readonly HashSet<string> _pendingTrustChanges = new(StringComparer.OrdinalIgnoreCase);
    private bool _trustChangeWorkerRunning;
    private Task? _trustChangeWorker;

    private void OnTrustChanged(object? sender, DotNetExtensionTrustChangedEventArgs args)
    {
        lock (_trustChangeSync)
        {
            if (_disposed)
                return;
            _pendingTrustChanges.UnionWith(args.PluginIds);
            if (_trustChangeWorkerRunning)
                return;

            _trustChangeWorkerRunning = true;
            using (ExecutionContext.SuppressFlow())
                _trustChangeWorker = Task.Run(ProcessTrustChangesAsync);
        }
    }

    private async Task ProcessTrustChangesAsync()
    {
        while (true)
        {
            string[] pluginIds;
            lock (_trustChangeSync)
            {
                if (_pendingTrustChanges.Count == 0 || _disposed)
                {
                    _pendingTrustChanges.Clear();
                    _trustChangeWorkerRunning = false;
                    return;
                }

                pluginIds = _pendingTrustChanges
                    .OrderBy(static pluginId => pluginId, StringComparer.Ordinal)
                    .ToArray();
                _pendingTrustChanges.Clear();
            }

            await _mutationLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_disposed)
                    continue;

                var affected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var revoked = new List<(string PluginId, PluginRuntimeNode Node)>();
                var withdrawn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pluginId in pluginIds)
                {
                    if (!_nodes.TryGetValue(pluginId, out var node))
                        continue;

                    var closure = ReplanIdsFor(pluginId);
                    affected.UnionWith(closure);
                    if (!HasExecutionQualification(node)
                        && (node.Generation != null || node.PendingActivation != null))
                    {
                        revoked.Add((pluginId, node));
                        withdrawn.UnionWith(closure);
                    }
                }

                WithdrawTrustRevokedRoutes(withdrawn);
                foreach (var (pluginId, node) in revoked)
                    await StopClosureAsync(pluginId, node, CancellationToken.None).ConfigureAwait(false);

                await ReplanAllAsync(affected, CancellationToken.None).ConfigureAwait(false);
                PublishSnapshot();
            }
            catch (Exception exception)
            {
                _logger?.LogError(
                    "Failed to converge .NET extension trust changes for {PluginIds}. FailureType: {FailureType}.",
                    string.Join(",", pluginIds),
                    exception.GetType().Name);
            }
            finally
            {
                _mutationLock.Release();
            }
        }
    }

    private void WithdrawTrustRevokedRoutes(IReadOnlySet<string> affected)
    {
        using var batch = _contributions.BeginBatch();
        foreach (var pluginId in affected)
        {
            if (!_nodes.TryGetValue(pluginId, out var node) || node.Generation is not { } generation)
                continue;

            generation.WithdrawRouting();
            if (node.State == DotNetExtensionRuntimeState.Active)
            {
                node.State = DotNetExtensionRuntimeState.Deactivating;
                node.Blockers = [];
            }
        }
    }

    private Task WaitForTrustChangeWorkerAsync()
    {
        lock (_trustChangeSync)
            return _trustChangeWorker ?? Task.CompletedTask;
    }
}
