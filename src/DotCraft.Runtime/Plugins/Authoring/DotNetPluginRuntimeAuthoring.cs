using DotCraft.Plugins;

namespace DotCraft.Runtime;

internal sealed partial class DotNetPluginRuntimeManager
{
    private const string AuthoringPublicationFailedCode = "DotNetPluginAuthoringPublicationFailed";
    private const string AuthoringCandidateInvalidCode = "DotNetPluginAuthoringCandidateInvalid";

    private readonly Dictionary<string, (string Fingerprint, DiscoveredPlugin Plugin)> _authoringBuilds =
        new(StringComparer.OrdinalIgnoreCase);

    internal async Task<DotNetPluginAuthoringApplyResult> ApplyAuthoringBuildAsync(
        string pluginId,
        DotNetPluginBuildPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (!preparation.Succeeded
            || string.IsNullOrWhiteSpace(preparation.BundlePath)
            || string.IsNullOrWhiteSpace(preparation.Fingerprint)
            || string.IsNullOrWhiteSpace(preparation.ProjectPluginRoot)
            || string.IsNullOrWhiteSpace(preparation.ProjectWorkspaceRoot))
        {
            throw new ArgumentException("A successful .NET plugin build preparation is required.", nameof(preparation));
        }

        ThrowIfDisposed();
        var canonicalId = PluginIds.Canonicalize(pluginId);
        var pluginRoot = preparation.ProjectPluginRoot;

        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_authoringBuilds.TryGetValue(canonicalId, out var activeBuild)
                && string.Equals(activeBuild.Fingerprint, preparation.Fingerprint, StringComparison.Ordinal)
                && PathsEqual(activeBuild.Plugin.Manifest.RootPath, pluginRoot)
                && _nodes.TryGetValue(canonicalId, out var activeNode)
                && string.Equals(activeNode.Snapshot.Fingerprint, preparation.Fingerprint, StringComparison.Ordinal))
            {
                return AuthoringResult(
                    PluginRuntimeMutationOutcome.NoChange,
                    canonicalId,
                    []);
            }

            var affected = ReplanIdsFor(canonicalId);
            _nodes.TryGetValue(canonicalId, out var oldNode);
            if (!await StopClosureAsync(canonicalId, oldNode, cancellationToken).ConfigureAwait(false))
            {
                return AuthoringResult(
                    PluginRuntimeMutationOutcome.NotApplied,
                    canonicalId,
                    [PluginDiagnostic.Error(
                        "PluginOperationIncomplete",
                        "Plugin cleanup is still in progress.",
                        canonicalId)]);
            }

            try
            {
                PublishAuthoringBundle(preparation.BundlePath, pluginRoot);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await ReplanAllAsync(affected, CancellationToken.None).ConfigureAwait(false);
                PublishSnapshot();
                return AuthoringResult(
                    PluginRuntimeMutationOutcome.NotApplied,
                    canonicalId,
                    [PluginDiagnostic.Error(
                        AuthoringPublicationFailedCode,
                        "The compiled plugin bundle could not be published.",
                        canonicalId)]);
            }

            var parsed = PluginManifestParser.Load(pluginRoot);
            var manifest = parsed.Manifest;
            PluginAcceptedSnapshot? candidate = null;
            DiscoveredPlugin? authoringPlugin = null;
            if (manifest?.Dotnet != null && PluginIds.EqualsCanonical(manifest.Id, canonicalId))
            {
                authoringPlugin = new DiscoveredPlugin(
                    manifest,
                    PluginDiscoverySourceKind.Workspace,
                    Path.GetDirectoryName(pluginRoot)!,
                    Enabled: true);
                try
                {
                    candidate = _bundleStore.Accept(authoringPlugin);
                    if (!string.Equals(
                            candidate.Fingerprint,
                            preparation.Fingerprint,
                            StringComparison.Ordinal))
                    {
                        _bundleStore.DeleteAccepted(candidate.ContentRoot);
                        candidate = null;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    candidate = null;
                }
            }

            if (oldNode != null)
            {
                _bundleStore.DeleteAccepted(oldNode.Snapshot.ContentRoot);
                _nodes.Remove(canonicalId);
            }
            _authoringBuilds.Remove(canonicalId);

            if (candidate == null || authoringPlugin == null)
            {
                RefreshEffectivePlugins(DiscoverCurrent());
                PublishSnapshot();
                return AuthoringResult(
                    PluginRuntimeMutationOutcome.NotApplied,
                    canonicalId,
                    [.. parsed.Diagnostics, PluginDiagnostic.Error(
                        AuthoringCandidateInvalidCode,
                        "The published plugin bundle could not be admitted.",
                        canonicalId)]);
            }

            _authoringBuilds[canonicalId] = (preparation.Fingerprint, authoringPlugin);
            _nodes[canonicalId] = new PluginRuntimeNode(
                candidate,
                enabled: true,
                workspaceRoot: preparation.ProjectWorkspaceRoot);
            RefreshEffectivePlugins(DiscoverCurrent());

            await ReplanAllAsync(affected, cancellationToken).ConfigureAwait(false);
            PublishSnapshot();
            return AuthoringResult(
                PluginRuntimeMutationOutcome.Applied,
                canonicalId,
                []);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    private DotNetPluginAuthoringApplyResult AuthoringResult(
        PluginRuntimeMutationOutcome outcome,
        string pluginId,
        IReadOnlyList<PluginDiagnostic> diagnostics) =>
        new(
            outcome,
            Snapshot.Plugins.FirstOrDefault(plugin =>
                PluginIds.EqualsCanonical(plugin.PluginId, pluginId)),
            diagnostics);

    private static void PublishAuthoringBundle(string stagedRoot, string destination)
    {
        var parent = Path.GetDirectoryName(destination)
                     ?? throw new InvalidOperationException("The authoring plugin root needs a parent directory.");
        Directory.CreateDirectory(parent);
        var backup = Path.Combine(parent, $".plugin-old-{Guid.NewGuid():N}");
        var hadDestination = Directory.Exists(destination);
        if (hadDestination)
            Directory.Move(destination, backup);

        try
        {
            Directory.Move(stagedRoot, destination);
        }
        catch
        {
            if (hadDestination && !Directory.Exists(destination))
                Directory.Move(backup, destination);
            throw;
        }

        if (hadDestination)
        {
            try
            {
                Directory.Delete(backup, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Publication already committed; a stale backup does not change the authoritative root.
            }
        }
    }

    private bool HasAuthoringExecutionQualification(string pluginId, string fingerprint) =>
        _authoringBuilds.TryGetValue(pluginId, out var build)
        && string.Equals(build.Fingerprint, fingerprint, StringComparison.Ordinal);

    private bool HasExecutionQualification(PluginRuntimeNode node) =>
        HasAuthoringExecutionQualification(
            node.Snapshot.Manifest.Id,
            node.Snapshot.Fingerprint)
        || TrustStatusOf(node) == PluginDotnetTrustStatus.Trusted;

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

internal sealed record DotNetPluginAuthoringApplyResult(
    PluginRuntimeMutationOutcome Outcome,
    PluginDotnetRuntimeInfo? Runtime,
    IReadOnlyList<PluginDiagnostic> Diagnostics);
