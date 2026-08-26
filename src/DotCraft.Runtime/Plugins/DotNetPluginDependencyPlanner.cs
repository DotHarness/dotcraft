using DotCraft.Plugins;

namespace DotCraft.Runtime;

/// <summary>The dependency half of the runtime manager: activation ordering, teardown ordering, and blockers.</summary>
internal sealed partial class DotNetPluginRuntimeManager
{
    private async Task HandleWorkFailureAsync(string pluginId, string generationId, string message)
    {
        await _mutationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_nodes.TryGetValue(pluginId, out var node)
                || node.Generation == null
                || !string.Equals(node.GenerationId, generationId, StringComparison.Ordinal))
                return;

            await StopClosureAsync(pluginId, node, CancellationToken.None).ConfigureAwait(false);
            if (node.PendingTeardown != null)
                node.PendingTeardownCompletedState = PluginDotnetRuntimeState.Faulted;
            if (_pendingStopClosures.ContainsKey(pluginId))
                _pendingStopClosures[pluginId] = PluginDotnetRuntimeState.Faulted;
            node.PendingRemnant = null;
            node.State = PluginDotnetRuntimeState.Faulted;
            node.Blockers = [Blocker(PluginActivationBlockers.ActivationFailed, message, ("phase", "backgroundWork"))];
            PublishSnapshot();
            await ReplanAllAsync(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <summary>Re-evaluates every node, activating providers before the consumers that need them.</summary>
    private async Task ReplanAllAsync(
        IReadOnlySet<string> retryFaulted,
        CancellationToken cancellationToken)
    {
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var cycleMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in _nodes.Values.OrderBy(static item => item.Snapshot.Manifest.Id, StringComparer.Ordinal))
            await VisitAsync(node).ConfigureAwait(false);

        async Task<bool> VisitAsync(PluginRuntimeNode node)
        {
            var pluginId = node.Snapshot.Manifest.Id;
            if (visited.Contains(pluginId))
                return node.State == PluginDotnetRuntimeState.Active;
            if (!visiting.Add(pluginId))
            {
                MarkCycle(pluginId, stack, cycleMembers);
                return false;
            }

            stack.Add(pluginId);
            if (node.PendingActivation != null || node.PendingTeardown != null)
            {
                if (node.Enabled && retryFaulted.Contains(pluginId))
                    node.RetryAfterPendingTeardown = true;
                CompleteVisit(pluginId, visiting, visited, stack);
                return false;
            }

            if (!node.Enabled)
            {
                // A disabled plugin whose last generation has not come back keeps reporting the reclaim.
                if (node.Generation == null && node.State != PluginDotnetRuntimeState.Reclaiming)
                    node.State = PluginDotnetRuntimeState.Stopped;
                node.Blockers = [];
                CompleteVisit(pluginId, visiting, visited, stack);
                return false;
            }

            if (node.State == PluginDotnetRuntimeState.Active)
            {
                CompleteVisit(pluginId, visiting, visited, stack);
                return true;
            }

            var blockers = PreflightBlockers(node.Snapshot).Concat(TrustBlockers(node)).ToList();
            foreach (var dependency in node.Snapshot.Manifest.Dependencies
                         .OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                if (!_nodes.TryGetValue(dependency.Key, out var provider))
                {
                    blockers.Add(BuildAbsentProviderBlocker(dependency.Key, dependency.Value));
                    continue;
                }

                var providerVersion = provider.Snapshot.Manifest.Version;
                if (!PluginDotnetManifestAdmission.SatisfiesMinimum(dependency.Value, providerVersion))
                {
                    blockers.Add(DependencyUnsatisfied(
                        dependency.Key,
                        dependency.Value,
                        providerVersion,
                        "versionUnsatisfied"));
                    continue;
                }

                if (!provider.Enabled)
                {
                    blockers.Add(DependencyUnsatisfied(
                        dependency.Key,
                        dependency.Value,
                        providerVersion,
                        "disabled"));
                    continue;
                }

                await VisitAsync(provider).ConfigureAwait(false);
                if (cycleMembers.Contains(pluginId))
                    continue;
                if (provider.State != PluginDotnetRuntimeState.Active)
                {
                    blockers.Add(DependencyUnsatisfied(
                        dependency.Key,
                        dependency.Value,
                        providerVersion,
                        provider.State.ToString().ToLowerInvariant()));
                }
            }

            if (cycleMembers.Contains(pluginId))
            {
                CompleteVisit(pluginId, visiting, visited, stack);
                return false;
            }

            if (blockers.Count > 0)
            {
                node.PendingRemnant = null;
                node.State = PluginDotnetRuntimeState.Blocked;
                node.Blockers = blockers;
                CompleteVisit(pluginId, visiting, visited, stack);
                return false;
            }

            if (node.State == PluginDotnetRuntimeState.Faulted && !retryFaulted.Contains(pluginId))
            {
                CompleteVisit(pluginId, visiting, visited, stack);
                return false;
            }

            await ActivateNodeAsync(node, cancellationToken).ConfigureAwait(false);
            CompleteVisit(pluginId, visiting, visited, stack);
            return node.State == PluginDotnetRuntimeState.Active;
        }
    }

    /// <summary>Stops one plugin together with everything that depends on it, consumers first.</summary>
    private async Task<bool> StopClosureAsync(
        string selectedPluginId,
        PluginRuntimeNode? selected,
        CancellationToken cancellationToken,
        PluginDotnetRuntimeState selectedCompletedState = PluginDotnetRuntimeState.Stopped)
    {
        var closureIds = ReplanIdsFor(selectedPluginId);
        var ordered = StopOrder(_nodes.Values.Where(node => closureIds.Contains(node.Snapshot.Manifest.Id)));
        foreach (var node in ordered)
        {
            var completedState = ReferenceEquals(node, selected)
                ? selectedCompletedState
                : PluginDotnetRuntimeState.Blocked;
            if (!await StopNodeAsync(node, completedState, cancellationToken).ConfigureAwait(false))
            {
                _pendingStopClosures[selectedPluginId] = selectedCompletedState;
                PublishSnapshot();
                return false;
            }
        }

        _pendingStopClosures.Remove(selectedPluginId);
        PublishSnapshot();
        return true;
    }

    /// <summary>Continues dependency-ordered stops after an earlier generation finishes cleanup.</summary>
    private async Task ResumePendingStopClosuresAsync()
    {
        var shouldReplan = false;
        foreach (var request in _pendingStopClosures.ToArray())
        {
            _nodes.TryGetValue(request.Key, out var selected);
            if (!await StopClosureAsync(
                    request.Key,
                    selected,
                    CancellationToken.None,
                    request.Value)
                .ConfigureAwait(false))
            {
                continue;
            }

            shouldReplan = true;
        }

        if (shouldReplan && !_disposed)
        {
            await ReplanAllAsync(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private IReadOnlyDictionary<string, PluginGeneration> GetDirectProviderGenerations(
        PluginRuntimeNode consumer)
    {
        var result = new Dictionary<string, PluginGeneration>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerId in consumer.Snapshot.Manifest.Dependencies.Keys)
        {
            if (!_nodes.TryGetValue(providerId, out var provider)
                || provider.State != PluginDotnetRuntimeState.Active
                || provider.Generation == null)
            {
                throw PluginServiceBindingException.Missing(
                    providerId,
                    "<activation>",
                    $"Direct provider '{providerId}' is not active for '{consumer.Snapshot.Manifest.Id}'.");
            }

            result.Add(providerId, provider.Generation);
        }

        return result;
    }

    /// <summary>Returns one plugin together with the reverse closure of its consumers.</summary>
    private HashSet<string> ReplanIdsFor(string selectedPluginId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { selectedPluginId };
        var queue = new Queue<string>();
        queue.Enqueue(selectedPluginId);
        while (queue.TryDequeue(out var providerId))
        {
            foreach (var consumer in _nodes.Values.Where(node =>
                         node.Snapshot.Manifest.Dependencies.ContainsKey(providerId)))
            {
                if (result.Add(consumer.Snapshot.Manifest.Id))
                    queue.Enqueue(consumer.Snapshot.Manifest.Id);
            }
        }

        return result;
    }

    /// <summary>Orders nodes so that every consumer is torn down before its providers.</summary>
    private static IReadOnlyList<PluginRuntimeNode> StopOrder(IEnumerable<PluginRuntimeNode> nodes)
    {
        var map = nodes.ToDictionary(
            static node => node.Snapshot.Manifest.Id,
            StringComparer.OrdinalIgnoreCase);
        var result = new List<PluginRuntimeNode>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in map.Values.OrderBy(static item => item.Snapshot.Manifest.Id, StringComparer.Ordinal))
            Visit(node);
        result.Reverse();
        return result;

        void Visit(PluginRuntimeNode node)
        {
            if (!visited.Add(node.Snapshot.Manifest.Id))
                return;
            foreach (var providerId in node.Snapshot.Manifest.Dependencies.Keys
                         .OrderBy(static id => id, StringComparer.Ordinal))
            {
                if (map.TryGetValue(providerId, out var provider))
                    Visit(provider);
            }

            result.Add(node);
        }
    }

    private PluginRuntimeBlocker BuildAbsentProviderBlocker(string providerId, string requiredVersion)
    {
        if (!_effectivePlugins.TryGetValue(providerId, out var observed) || !observed.Installed)
            return DependencyUnsatisfied(providerId, requiredVersion, null, "missing");
        if (!PluginDotnetManifestAdmission.SatisfiesMinimum(requiredVersion, observed.Manifest.Version))
            return DependencyUnsatisfied(providerId, requiredVersion, observed.Manifest.Version, "versionUnsatisfied");
        if (!observed.Enabled)
            return DependencyUnsatisfied(providerId, requiredVersion, observed.Manifest.Version, "disabled");
        return DependencyUnsatisfied(providerId, requiredVersion, observed.Manifest.Version, "unavailable");
    }

    /// <summary>The one blocker for "the declared provider is not active", told apart by <c>reason</c>.</summary>
    private static PluginRuntimeBlocker DependencyUnsatisfied(
        string providerId,
        string requiredVersion,
        string? observedVersion,
        string reason) =>
        Blocker(
            "PluginDependencyUnsatisfied",
            DependencyUnsatisfiedMessage(providerId, requiredVersion, observedVersion, reason),
            ("providerId", providerId),
            ("requiredVersion", requiredVersion),
            ("observedVersion", observedVersion),
            ("reason", reason));

    private static string DependencyUnsatisfiedMessage(
        string providerId,
        string requiredVersion,
        string? observedVersion,
        string reason) => reason switch
    {
        "missing" => $"Required plugin '{providerId}' is missing; at least version '{requiredVersion}' is required.",
        "versionUnsatisfied" =>
            $"Required plugin '{providerId}' needs a compatible version at or above '{requiredVersion}', found '{observedVersion ?? "<missing>"}'.",
        "disabled" => $"Required plugin '{providerId}' is disabled.",
        _ => $"Required plugin '{providerId}' is not active ({reason})."
    };

    private void MarkCycle(
        string repeatedId,
        IReadOnlyList<string> stack,
        HashSet<string> cycleMembers)
    {
        var start = stack.ToList().FindIndex(id =>
            string.Equals(id, repeatedId, StringComparison.OrdinalIgnoreCase));
        var cycle = stack.Skip(start).ToArray();
        var canonicalStart = Array.FindIndex(cycle, id =>
            string.Equals(id, cycle.Min(StringComparer.Ordinal), StringComparison.Ordinal));
        var canonical = cycle.Skip(canonicalStart).Concat(cycle.Take(canonicalStart)).ToArray();
        foreach (var memberId in canonical)
        {
            cycleMembers.Add(memberId);
            var member = _nodes[memberId];
            member.State = PluginDotnetRuntimeState.Blocked;
            member.Blockers =
            [
                Blocker(
                    "PluginDependencyCycle",
                    "Plugin dependency cycle detected.",
                    ("cyclePluginIds", canonical))
            ];
        }
    }

    private static void CompleteVisit(
        string pluginId,
        HashSet<string> visiting,
        HashSet<string> visited,
        List<string> stack)
    {
        visiting.Remove(pluginId);
        visited.Add(pluginId);
        stack.RemoveAt(stack.Count - 1);
    }
}
