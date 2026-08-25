using System.Text.Json;
using DotCraft.Contributions;
using DotCraft.Plugins;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

/// <summary>The immutable, monotonically revised view of the runtime that clients reconcile against.</summary>
internal sealed partial class DotNetPluginRuntimeManager
{
    internal const int MaxDiagnosticsPerPlugin = 32;

    private void PublishSnapshot()
    {
        var plugins = _nodes.Values
            .OrderBy(static node => node.Snapshot.Manifest.Id, StringComparer.Ordinal)
            .Select(node =>
            {
                var pluginId = node.Snapshot.Manifest.Id;
                var leaked = _reclaim.OutstandingCount(pluginId);
                return new PluginDotnetRuntimeInfo(
                    pluginId,
                    node.Snapshot.Manifest.Version!,
                    node.State,
                    node.GenerationId,
                    node.Blockers.Select(static blocker => blocker with
                    {
                        Parameters = CloneParameters(blocker.Parameters)
                    }).ToArray(),
                    node.State == PluginDotnetRuntimeState.Active && node.GenerationId != null
                        ? ToolSource.DescribeTools(pluginId, node.GenerationId)
                        : [],
                    leaked,
                    leaked >= _options.LeakedGenerationRestartThreshold,
                    TrustStatusOf(node),
                    ProjectDependencyObservations(node));
            })
            .ToArray();
        var diagnostics = _nodes.Values
            .SelectMany(static node => node.Diagnostics)
            .Concat(ToolSource.Diagnostics)
            .Select(static diagnostic => diagnostic with
            {
                Parameters = CloneParameters(diagnostic.Parameters)
            })
            .ToArray();

        var semantic = JsonSerializer.SerializeToElement(new SnapshotContent(plugins, diagnostics));
        if (_snapshotSemantic is { } previousSemantic
            && JsonElement.DeepEquals(previousSemantic, semantic))
        {
            return;
        }

        var previous = _snapshot;
        var changedPluginIds = ChangedPluginIds(previous, plugins, diagnostics);
        var snapshot = new PluginRuntimeSnapshot(previous.Revision + 1, plugins, diagnostics);
        _snapshotSemantic = semantic;
        Volatile.Write(ref _snapshot, snapshot);
        NotifySnapshotChanged(snapshot, changedPluginIds);
    }

    private static IReadOnlyList<string> ChangedPluginIds(
        PluginRuntimeSnapshot previous,
        IReadOnlyList<PluginDotnetRuntimeInfo> plugins,
        IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var before = previous.Plugins.ToDictionary(
            static plugin => plugin.PluginId,
            static plugin => JsonSerializer.SerializeToElement(plugin),
            StringComparer.OrdinalIgnoreCase);
        var after = plugins.ToDictionary(
            static plugin => plugin.PluginId,
            static plugin => JsonSerializer.SerializeToElement(plugin),
            StringComparer.OrdinalIgnoreCase);

        foreach (var pluginId in before.Keys.Concat(after.Keys))
        {
            if (!before.TryGetValue(pluginId, out var oldValue)
                || !after.TryGetValue(pluginId, out var newValue)
                || !JsonElement.DeepEquals(oldValue, newValue))
            {
                changed.Add(pluginId);
            }
        }

        var oldDiagnostics = JsonSerializer.SerializeToElement(previous.Diagnostics);
        var newDiagnostics = JsonSerializer.SerializeToElement(diagnostics);
        if (!JsonElement.DeepEquals(oldDiagnostics, newDiagnostics))
        {
            foreach (var pluginId in previous.Diagnostics.Concat(diagnostics)
                         .Select(static diagnostic => diagnostic.PluginId)
                         .OfType<string>())
            {
                changed.Add(pluginId);
            }
        }

        return changed.OrderBy(static pluginId => pluginId, StringComparer.Ordinal).ToArray();
    }

    private void NotifySnapshotChanged(
        PluginRuntimeSnapshot snapshot,
        IReadOnlyList<string> changedPluginIds)
    {
        var handlers = SnapshotChanged;
        if (handlers == null)
            return;

        var args = new PluginRuntimeSnapshotChangedEventArgs(snapshot, changedPluginIds);
        foreach (EventHandler<PluginRuntimeSnapshotChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception)
            {
                _logger?.LogWarning("A .NET plugin runtime snapshot subscriber failed.");
            }
        }
    }

    private IReadOnlyList<PluginDependencyObservation> ProjectDependencyObservations(
        PluginRuntimeNode consumer) =>
        consumer.Snapshot.Manifest.Dependencies
            .OrderBy(static dependency => dependency.Key, StringComparer.Ordinal)
            .Select(dependency => ProjectDependencyObservation(dependency.Key, dependency.Value))
            .ToArray();

    private PluginDependencyObservation ProjectDependencyObservation(
        string providerId,
        string requiredVersion)
    {
        if (_nodes.TryGetValue(providerId, out var provider))
        {
            var observedVersion = provider.Snapshot.Manifest.Version;
            var availability = !PluginDotnetManifestAdmission.SatisfiesMinimum(requiredVersion, observedVersion)
                ? PluginDependencyAvailability.VersionUnsatisfied
                : provider.State switch
                {
                    PluginDotnetRuntimeState.Stopped => provider.Enabled
                        ? PluginDependencyAvailability.Unavailable
                        : PluginDependencyAvailability.Disabled,
                    PluginDotnetRuntimeState.Blocked => PluginDependencyAvailability.Blocked,
                    PluginDotnetRuntimeState.Activating => PluginDependencyAvailability.Activating,
                    PluginDotnetRuntimeState.Active => PluginDependencyAvailability.Active,
                    PluginDotnetRuntimeState.Deactivating => PluginDependencyAvailability.Deactivating,
                    PluginDotnetRuntimeState.Faulted => PluginDependencyAvailability.Faulted,
                    PluginDotnetRuntimeState.Reclaiming => PluginDependencyAvailability.Reclaiming,
                    _ => PluginDependencyAvailability.Unavailable
                };
            return new PluginDependencyObservation(
                providerId,
                requiredVersion,
                observedVersion,
                availability);
        }

        if (!_effectivePlugins.TryGetValue(providerId, out var discovered) || !discovered.Installed)
        {
            return new PluginDependencyObservation(
                providerId,
                requiredVersion,
                null,
                PluginDependencyAvailability.Missing);
        }

        var catalogVersion = PluginDotnetManifestAdmission.IsCanonicalVersion(discovered.Manifest.Version)
            ? discovered.Manifest.Version
            : null;
        var catalogAvailability = !PluginDotnetManifestAdmission.SatisfiesMinimum(requiredVersion, catalogVersion)
            ? PluginDependencyAvailability.VersionUnsatisfied
            : discovered.Enabled
                ? PluginDependencyAvailability.Unavailable
                : PluginDependencyAvailability.Disabled;
        return new PluginDependencyObservation(
            providerId,
            requiredVersion,
            catalogVersion,
            catalogAvailability);
    }

    /// <summary>Runs one projection pass so <see cref="DotNetPluginToolSource.DescribeTools"/> reflects the live generations.</summary>
    private async Task DescribeToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ToolSource.GetRegistrationsAsync(DescribePlanningContext(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning("Describing .NET plugin Tools failed.");
        }
    }

    /// <summary>The Host-owned planning inputs a description pass runs under: an internal helper thread, not a user one.</summary>
    private ToolPlanningContext DescribePlanningContext() =>
        new(
            threadId: "plugin-runtime",
            turnId: null,
            workspacePath: _paths.WorkspacePath,
            dataPath: _paths.Data.RootPath,
            mode: "default",
            profile: null,
            providerCapabilities: null,
            revision: _contributions.GetRevision<IToolSource>(),
            threadKind: ToolPlanningThreadKind.Internal);

    /// <summary>Records an activation attempt that produced no generation, as both a log line and a node
    /// diagnostic the snapshot carries to the client.</summary>
    private void ReportFailedActivation(PluginRuntimeNode node, string? generationId)
    {
        if (node.Blockers.Count == 0)
            return;

        var pluginId = node.Snapshot.Manifest.Id;
        var faulted = node.State == PluginDotnetRuntimeState.Faulted;
        var reported = node.Blockers.Select(blocker => PluginDiagnostic.Error(
            blocker.Code,
            blocker.Message,
            pluginId,
            parameters: new Dictionary<string, JsonElement>(CloneParameters(blocker.Parameters), StringComparer.Ordinal)
            {
                ["state"] = JsonSerializer.SerializeToElement(node.State.ToString().ToLowerInvariant()),
                ["generationId"] = JsonSerializer.SerializeToElement(generationId)
            })).ToArray();

        foreach (var diagnostic in reported)
        {
            _logger?.Log(
                faulted ? LogLevel.Error : LogLevel.Warning,
                "Plugin {PluginId} activation ended {State} ({GenerationId}): {BlockerCode} {BlockerMessage}",
                pluginId,
                node.State,
                generationId ?? "no generation",
                diagnostic.Code,
                diagnostic.Message);
        }

        AppendDiagnostics(node, reported);
    }

    private static void AddDiagnostic(
        PluginRuntimeNode node,
        string code,
        string message,
        string phase,
        string generationId) =>
        AppendDiagnostics(
            node,
            [PluginDiagnostic.Error(
                code,
                message,
                node.Snapshot.Manifest.Id,
                parameters: new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["phase"] = JsonSerializer.SerializeToElement(phase),
                    ["generationId"] = JsonSerializer.SerializeToElement(generationId)
                })]);

    internal static void AppendDiagnostics(
        PluginRuntimeNode node,
        IEnumerable<PluginDiagnostic> diagnostics)
    {
        var retained = node.Diagnostics.ToList();
        foreach (var diagnostic in diagnostics)
        {
            retained.RemoveAll(existing => SameDiagnosticSlot(existing, diagnostic));
            retained.Add(diagnostic);
        }

        if (retained.Count > MaxDiagnosticsPerPlugin)
            retained.RemoveRange(0, retained.Count - MaxDiagnosticsPerPlugin);
        node.Diagnostics = retained;
    }

    private static bool SameDiagnosticSlot(
        PluginDiagnostic left,
        PluginDiagnostic right) =>
        StringComparer.Ordinal.Equals(left.Code, right.Code)
        && StringComparer.Ordinal.Equals(DiagnosticParameter(left, "phase"), DiagnosticParameter(right, "phase"))
        && StringComparer.Ordinal.Equals(DiagnosticParameter(left, "generationId"), DiagnosticParameter(right, "generationId"));

    private static string? DiagnosticParameter(PluginDiagnostic diagnostic, string name) =>
        diagnostic.Parameters.TryGetValue(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
            : null;

    /// <summary>Returns the preflight findings that make a bundle inadmissible for activation.</summary>
    private static PluginRuntimeBlocker[] PreflightBlockers(PluginAcceptedSnapshot snapshot) =>
        snapshot.PreflightDiagnostics
            .Where(static diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error)
            .Select(ToBlocker)
            .ToArray();

    private static PluginRuntimeBlocker ToBlocker(PluginDiagnostic diagnostic) =>
        new(diagnostic.Code, diagnostic.Message, CloneParameters(diagnostic.Parameters));

    private static PluginRuntimeBlocker Blocker(
        string code,
        string message,
        params (string Name, object? Value)[] parameters) =>
        new(
            code,
            message,
            parameters.ToDictionary(
                static parameter => parameter.Name,
                static parameter => JsonSerializer.SerializeToElement(parameter.Value),
                StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, JsonElement> CloneParameters(
        IReadOnlyDictionary<string, JsonElement> parameters) =>
        parameters.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Clone(),
            StringComparer.Ordinal);

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DotNetPluginRuntimeManager));
    }

    private sealed record SnapshotContent(
        IReadOnlyList<PluginDotnetRuntimeInfo> Plugins,
        IReadOnlyList<PluginDiagnostic> Diagnostics);
}
