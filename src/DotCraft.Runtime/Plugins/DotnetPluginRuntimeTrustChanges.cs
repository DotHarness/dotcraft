using DotCraft.Plugins;
using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

internal sealed partial class DotnetPluginRuntimeManager
{
    private readonly object _trustChangeSync = new();
    private readonly HashSet<string> _pendingTrustChanges = new(StringComparer.OrdinalIgnoreCase);
    private bool _trustChangeWorkerRunning;
    private Task? _trustChangeWorker;

    private void OnTrustChanged(object? sender, PluginDotnetTrustChangedEventArgs args)
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
                foreach (var pluginId in pluginIds)
                {
                    if (!_nodes.TryGetValue(pluginId, out var node))
                        continue;

                    affected.UnionWith(ReplanIdsFor(pluginId));
                    if (TrustStatusOf(node) != PluginDotnetTrustStatus.Trusted
                        && (node.Generation != null || node.PendingActivation != null))
                    {
                        await StopClosureAsync(pluginId, node, CancellationToken.None).ConfigureAwait(false);
                    }
                }

                await ReplanAllAsync(affected, CancellationToken.None).ConfigureAwait(false);
                PublishSnapshot();
            }
            catch (Exception exception)
            {
                _logger?.LogError(
                    "Failed to converge .NET plugin trust changes for {PluginIds}. FailureType: {FailureType}.",
                    string.Join(",", pluginIds),
                    exception.GetType().Name);
            }
            finally
            {
                _mutationLock.Release();
            }
        }
    }

    private Task WaitForTrustChangeWorkerAsync()
    {
        lock (_trustChangeSync)
            return _trustChangeWorker ?? Task.CompletedTask;
    }
}
