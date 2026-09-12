using DotCraft.Plugins;
using DotCraft.Tools;

namespace DotCraft.Runtime;

internal sealed partial class DotNetPluginRuntimeManager
{
    private readonly object _exportGate = new();
    private int _exportCount;
    private TaskCompletionSource? _exportsDrained;

    internal async ValueTask<RemoteToolSourceExport> ExportAsync(
        string pluginId, string generationId, CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var files = new List<RemotePluginBundleFiles>();
        var pinned = false;
        try
        {
            ThrowIfDisposed();
            if (!_nodes.TryGetValue(pluginId, out var selected) || selected.GenerationId != generationId)
                throw new InvalidOperationException("The selected plugin generation is no longer active.");
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Visit(selected);
            lock (_exportGate)
            {
                if (_exportCount++ == 0) _exportsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
                pinned = true;
            }
            return new RemoteToolSourceExport(files.ToArray(), Release);

            void Visit(PluginRuntimeNode node)
            {
                if (!visited.Add(node.Snapshot.Manifest.Id)) return;
                if (node.State != PluginDotnetRuntimeState.Active || node.Generation is null
                    || !CallGates.IsCallable(node.Snapshot.Manifest.Id, node.GenerationId!))
                    throw new InvalidOperationException("The selected plugin generation is no longer active.");
                foreach (var dependency in node.Snapshot.Manifest.Dependencies.Keys)
                    Visit(_nodes[dependency]);
                var root = _bundleStore.CreateGenerationCopy(node.Snapshot, "export-" + Guid.NewGuid().ToString("N"));
                files.Add(new(new(node.Snapshot.Manifest.Id, node.GenerationId!, node.GenerationRevision,
                    node.Snapshot.ContentFingerprint, node.Snapshot.DotnetFingerprint, node.Settings!.Value.Clone()), root));
            }
        }
        catch { Release(); throw; }
        finally { _mutationLock.Release(); }

        void Release()
        {
            foreach (var file in files) _bundleStore.DeleteGeneration(file.RootPath);
            lock (_exportGate)
                if (pinned && --_exportCount == 0) _exportsDrained!.TrySetResult();
        }
    }

    private Task WaitForExportsAsync()
    {
        lock (_exportGate) return _exportCount == 0 ? Task.CompletedTask : _exportsDrained!.Task;
    }

    internal async Task PrepareExecutionAsync(
        IReadOnlyList<RemotePluginBundleFiles> bundles, CancellationToken cancellationToken)
    {
        if (!_executionOnly) throw new InvalidOperationException("This runtime does not accept execution bundles.");
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var accepted = new List<(RemotePluginBundle Bundle, PluginAcceptedSnapshot Snapshot)>();
        try
        {
            ThrowIfDisposed();
            foreach (var files in bundles)
            {
                var bundle = files.Bundle;
                var parsed = PluginManifestParser.Load(files.RootPath);
                if (parsed.Manifest?.Id != bundle.PluginId || parsed.Manifest.Dotnet is null)
                    throw new InvalidOperationException("The prepared bundle identity is invalid.");
                var snapshot = _bundleStore.Accept(new(parsed.Manifest, PluginDiscoverySourceKind.Explicit, files.RootPath, true));
                accepted.Add((bundle, snapshot));
                if (snapshot.ContentFingerprint != bundle.ContentFingerprint || snapshot.DotnetFingerprint != bundle.DotnetFingerprint
                    || PreflightBlockers(snapshot).Length != 0)
                    throw new InvalidOperationException("The prepared bundle failed fingerprint or ABI validation.");
            }

            foreach (var (bundle, snapshot) in accepted)
            {
                if (_nodes.TryGetValue(bundle.PluginId, out var previous))
                {
                    if (previous.Snapshot.ContentFingerprint == bundle.ContentFingerprint
                        && previous.ExecutionSourceGeneration == bundle.SourceGeneration
                        && previous.Settings?.GetRawText() == bundle.Settings.GetRawText()
                        && previous.State == PluginDotnetRuntimeState.Active)
                        continue;
                    if (!await StopClosureAsync(bundle.PluginId, previous, cancellationToken).ConfigureAwait(false))
                        throw new InvalidOperationException("The previous plugin generation is still draining.");
                    _bundleStore.DeleteAccepted(previous.Snapshot.ContentRoot);
                    _nodes.Remove(bundle.PluginId);
                }
                _executionQualifications[bundle.PluginId] = bundle.DotnetFingerprint;
                _nodes.Add(bundle.PluginId, new(snapshot, true, _paths.WorkspacePath)
                {
                    Settings = bundle.Settings.Clone(),
                    ExecutionSourceGeneration = bundle.SourceGeneration
                });
            }
            await ReplanAllAsync(_nodes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            PublishSnapshot();
            foreach (var (bundle, _) in accepted)
                if (_nodes[bundle.PluginId].State != PluginDotnetRuntimeState.Active)
                    throw new InvalidOperationException($"Plugin '{bundle.PluginId}' could not activate in the execution host. "
                        + string.Join(" ", _nodes[bundle.PluginId].Blockers.Select(blocker => $"{blocker.Code}: {blocker.Message}")));
        }
        catch (OperationCanceledException)
        {
            foreach (var (bundle, snapshot) in accepted)
                if (_nodes.TryGetValue(bundle.PluginId, out var node) && ReferenceEquals(node.Snapshot, snapshot))
                    await StopClosureAsync(bundle.PluginId, node, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            foreach (var (_, snapshot) in accepted)
                if (!_nodes.Values.Any(node => ReferenceEquals(node.Snapshot, snapshot)))
                    _bundleStore.DeleteAccepted(snapshot.ContentRoot);
            _mutationLock.Release();
        }
    }

    private readonly Dictionary<string, string> _executionQualifications = new(StringComparer.OrdinalIgnoreCase);
}
