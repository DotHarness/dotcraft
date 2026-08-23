using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>The coordinated quiesce, mutate, reconcile protocol AppServer drives bundle mutations through.</summary>
internal sealed partial class DotnetPluginRuntimeManager
{
    /// <inheritdoc />
    public async Task<PluginRuntimeMutationResult> QuiesceForMutationAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var canonicalId = PluginIds.Canonicalize(pluginId);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var affected = ReplanIdsFor(canonicalId);
            _nodes.TryGetValue(canonicalId, out var selected);
            var completed = await StopClosureAsync(
                    canonicalId,
                    selected,
                    cancellationToken)
                .ConfigureAwait(false);
            PublishSnapshot();
            if (!completed)
            {
                return NotApplied(
                    canonicalId,
                    "PluginOperationIncomplete",
                    "Plugin cleanup is still in progress.",
                    affected);
            }

            return Result(PluginRuntimeMutationOutcome.Applied, canonicalId, affected, []);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PluginRuntimeMutationResult> ReconcileAfterMutationAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var canonicalId = PluginIds.Canonicalize(pluginId);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var discovery = DiscoverCurrent();
            var current = discovery.Plugins.FirstOrDefault(plugin =>
                PluginIds.EqualsCanonical(plugin.Manifest.Id, canonicalId)
                && plugin.Installed
                && plugin.Manifest.Dotnet != null);
            return await AdoptCurrentAsync(canonicalId, current, discovery, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private async Task<PluginRuntimeMutationResult> AdoptCurrentAsync(
        string pluginId,
        DiscoveredPlugin? current,
        PluginDiscoveryResult discovery,
        CancellationToken cancellationToken)
    {
        var affected = ReplanIdsFor(pluginId);
        _nodes.TryGetValue(pluginId, out var oldNode);
        if (oldNode?.Generation != null
            || oldNode?.PendingActivation != null
            || oldNode?.PendingTeardown != null)
        {
            return NotApplied(
                pluginId,
                "PluginOperationIncomplete",
                "The old runtime generation must be quiesced before filesystem reconciliation.",
                affected);
        }

        PluginAcceptedSnapshot? candidate = null;
        if (current != null)
        {
            try
            {
                candidate = _bundleStore.Accept(current);
            }
            catch (Exception)
            {
                return NotApplied(
                    pluginId,
                    "PluginCandidateInvalid",
                    "The plugin bundle could not be copied and validated.",
                    affected);
            }
        }

        if (candidate != null
            && oldNode != null
            && string.Equals(candidate.Fingerprint, oldNode.Snapshot.Fingerprint, StringComparison.Ordinal))
        {
            _bundleStore.DeleteAccepted(candidate.ContentRoot);
            RefreshEffectivePlugins(discovery);
            await ReplanAllAsync(affected, cancellationToken).ConfigureAwait(false);
            PublishSnapshot();
            return Result(PluginRuntimeMutationOutcome.NoChange, pluginId, affected, []);
        }

        if (oldNode != null)
        {
            _bundleStore.DeleteAccepted(oldNode.Snapshot.ContentRoot);
            _nodes.Remove(pluginId);
        }

        if (candidate != null)
            _nodes[pluginId] = new PluginRuntimeNode(candidate, current!.Enabled);

        RefreshEffectivePlugins(discovery);
        await ReplanAllAsync(affected, cancellationToken).ConfigureAwait(false);
        PublishSnapshot();
        return Result(PluginRuntimeMutationOutcome.Applied, pluginId, affected, []);
    }

    private PluginDiscoveryResult DiscoverCurrent() =>
        _discovery.DiscoverAll(_config, _paths.WorkspacePath, _paths.Data.RootPath);

    private void RefreshEffectivePlugins(PluginDiscoveryResult discovery)
    {
        _effectivePlugins.Clear();
        foreach (var plugin in discovery.Plugins)
            _effectivePlugins[plugin.Manifest.Id] = plugin;
    }

    private PluginRuntimeMutationResult NotApplied(
        string pluginId,
        string code,
        string message,
        IReadOnlySet<string>? affected = null) =>
        Result(
            PluginRuntimeMutationOutcome.NotApplied,
            pluginId,
            affected ?? ReplanIdsFor(pluginId),
            [PluginDiagnostic.Error(code, message, pluginId)]);

    private PluginRuntimeMutationResult Result(
        PluginRuntimeMutationOutcome outcome,
        string pluginId,
        IEnumerable<string> affected,
        IReadOnlyList<PluginDiagnostic> diagnostics) =>
        new(
            outcome,
            affected.OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
            diagnostics);
}
