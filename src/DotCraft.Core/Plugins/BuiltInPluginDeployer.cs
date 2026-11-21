using System.Buffers;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Configuration;

namespace DotCraft.Plugins;

/// <summary>
/// Copies catalog plugin directories into a plugin root.
/// </summary>
public sealed class BuiltInPluginDeployer(
    string pluginsPath,
    IReadOnlyList<string>? sourceRoots = null,
    AppConfig.PluginsConfig? pluginsConfig = null,
    string? userDataPath = null,
    ILogger? logger = null)
{
    public const string MarkerFile = ManagedPluginMarker.FileName;

    public IReadOnlyList<PluginDiagnostic> Deploy()
        => DeployCore(targetKeys: null, updateOnly: false, requireSourceIdentity: false).Diagnostics;

    public IReadOnlyList<PluginDiagnostic> DeployPlugin(string pluginKey)
        => DeployPlugins([pluginKey]);

    public IReadOnlyList<PluginDiagnostic> DeployPlugins(IReadOnlyCollection<string> pluginKeys)
    {
        var targets = pluginKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return targets.Count == 0
            ? []
            : DeployCore(targets, updateOnly: false, requireSourceIdentity: false).Diagnostics;
    }

    public bool RefreshPlugin(
        string pluginKey,
        List<PluginDiagnostic> diagnostics,
        bool requireSourceIdentity = false,
        CancellationToken cancellationToken = default)
    {
        var outcome = DeployCore(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { pluginKey },
            updateOnly: true,
            requireSourceIdentity,
            cancellationToken);
        diagnostics.AddRange(outcome.Diagnostics);
        return outcome.Updated;
    }

    public IReadOnlyList<string> GetPendingUpdates(
        List<PluginDiagnostic> diagnostics,
        string? marketplaceName = null,
        bool requireSourceIdentity = false)
    {
        var sources = BuiltInPluginSourceResolver.Discover(sourceRoots, diagnostics, pluginsConfig, userDataPath);

        var pending = new List<string>();
        foreach (var source in sources)
        {
            if (marketplaceName != null
                && !string.Equals(source.MarketplaceName, marketplaceName, StringComparison.OrdinalIgnoreCase))
                continue;

            var targetRoot = Path.Combine(pluginsPath, source.Manifest.Id);
            try
            {
                var marker = ManagedPluginMarker.Read(targetRoot);
                if (marker == null || !MatchesInstalledSource(marker, source, requireSourceIdentity))
                    continue;

                var installed = PluginManifestParser.Load(targetRoot);
                diagnostics.AddRange(installed.Diagnostics);
                if (installed.Manifest == null || installed.Manifest.Dotnet != null || source.Manifest.Dotnet != null)
                    continue;

                if (source.Manifest.Channel?.GetCompatibilityError() == null && marker != BuildMarker(source, marker))
                    pending.Add(source.Key);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                diagnostics.Add(DeploymentFailure(source.Manifest.Id, targetRoot, "check", exception));
            }
        }

        return pending;
    }

    public static bool InstallChannelSeed(
        string sourceRoot, string pluginsRoot, string pluginId, AppConfig.PluginsConfig config)
    {
        var source = PluginManifestParser.Load(sourceRoot).Manifest;
        if (source?.Channel == null || !PluginIds.EqualsCanonical(source.Id, pluginId))
            return false;

        return WithPluginsRootLock(pluginsRoot, () =>
        {
            var destination = Path.Combine(pluginsRoot, PluginIds.Canonicalize(pluginId));
            if (Directory.Exists(destination))
                return false;
            var official = PluginSourceRegistryCatalog.ArchiveSourcesFor(config).FirstOrDefault(s => s.IsDefault);
            var marker = new ManagedPluginMarker(
                BuildFingerprint(sourceRoot, channel: true),
                official == null ? null : "dotcraft-official",
                official?.CanonicalSourceIdentity(),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pluginsRoot);
            ReplacePluginDirectory(sourceRoot, destination, marker.Format(), CancellationToken.None, channel: true);
            return true;
        });
    }

    public static bool IsManagedBuiltInPluginRoot(string pluginRoot) =>
        File.Exists(Path.Combine(pluginRoot, MarkerFile));

    internal static T WithPluginsRootLock<T>(
        string pluginsRoot,
        Func<T> action,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pluginsRoot));
        if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
        using var mutex = new Mutex(false, "DotCraft.PluginDeploy." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
        var timer = Stopwatch.StartNew();
        var acquired = false;
        try
        {
            if (cancellationToken.CanBeCanceled)
            {
                while (!mutex.WaitOne(TimeSpan.FromMilliseconds(100)))
                    cancellationToken.ThrowIfCancellationRequested();
                acquired = true;
            }
            else
            {
                mutex.WaitOne();
                acquired = true;
            }
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger?.LogInformation("Startup stage {Stage} completed in {ElapsedMs}ms",
                "plugin-deploy-mutex-wait", timer.Elapsed.TotalMilliseconds);
            return action();
        }
        finally
        {
            if (acquired)
                mutex.ReleaseMutex();
        }
    }

    private (bool Updated, IReadOnlyList<PluginDiagnostic> Diagnostics) DeployCore(
        IReadOnlySet<string>? targetKeys,
        bool updateOnly,
        bool requireSourceIdentity,
        CancellationToken cancellationToken = default) =>
        WithPluginsRootLock(
            pluginsPath,
            () => DeployCoreLocked(targetKeys, updateOnly, requireSourceIdentity, cancellationToken),
            logger,
            cancellationToken);

    private (bool Updated, IReadOnlyList<PluginDiagnostic> Diagnostics) DeployCoreLocked(
        IReadOnlySet<string>? targetKeys,
        bool updateOnly,
        bool requireSourceIdentity,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var fingerprintMs = 0d;
        var replaceMs = 0d;
        var fingerprintCount = 0;
        var replaceCount = 0;
        var diagnostics = new List<PluginDiagnostic>();
        var sources = BuiltInPluginSourceResolver.Discover(
            sourceRoots,
            diagnostics,
            pluginsConfig,
            userDataPath);
        logger?.LogInformation("Startup stage {Stage} completed in {ElapsedMs}ms",
            "plugin-deploy-source-discovery", timer.Elapsed.TotalMilliseconds);
        var pending = targetKeys == null
            ? null
            : new HashSet<string>(targetKeys, StringComparer.OrdinalIgnoreCase);

        if (!updateOnly)
            Directory.CreateDirectory(pluginsPath);
        foreach (var source in sources)
        {
            if (pending != null && !pending.Remove(source.Key))
                continue;

            var pluginId = source.Manifest.Id;
            var pluginDir = Path.Combine(pluginsPath, pluginId);
            var markerPath = Path.Combine(pluginDir, MarkerFile);
            if (Directory.Exists(pluginDir) && !File.Exists(markerPath))
            {
                if (!updateOnly)
                {
                    diagnostics.Add(PluginDiagnostic.Info(
                        "BuiltInPluginUserOwned",
                        $"Built-in plugin '{pluginId}' was not deployed because the target directory is user-owned.",
                        pluginId,
                        path: pluginDir));
                }
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var installedMarker = ManagedPluginMarker.Read(pluginDir);
                if (updateOnly)
                {
                    if (installedMarker == null
                        || !MatchesInstalledSource(installedMarker, source, requireSourceIdentity))
                        continue;

                    var installed = PluginManifestParser.Load(pluginDir);
                    diagnostics.AddRange(installed.Diagnostics);
                    if (installed.Manifest == null || installed.Manifest.Dotnet != null || source.Manifest.Dotnet != null)
                        continue;
                    if (source.Manifest.Channel?.GetCompatibilityError() is { } reason)
                    {
                        diagnostics.Add(PluginDiagnostic.Warning(reason,
                            "The channel plugin update is not compatible with this host.", pluginId));
                        continue;
                    }
                }
                else if (installedMarker != null
                    && !MatchesInstalledSource(installedMarker, source, requireSourceIdentity: false))
                {
                    diagnostics.Add(PluginDiagnostic.Error(
                        "PluginInstalledFromOtherSource",
                        $"Plugin '{pluginId}' is already installed from another source. Remove it before installing '{source.Key}'.",
                        pluginId,
                        path: pluginDir));
                    continue;
                }

                timer.Restart();
                var marker = BuildMarker(source, installedMarker);
                fingerprintMs += timer.Elapsed.TotalMilliseconds;
                fingerprintCount++;
                if (installedMarker == marker)
                    continue;

                timer.Restart();
                ReplacePluginDirectory(source.PluginRoot, pluginDir, marker.Format(), cancellationToken, source.Manifest.Channel != null);
                replaceMs += timer.Elapsed.TotalMilliseconds;
                replaceCount++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                diagnostics.Add(DeploymentFailure(pluginId, pluginDir, "replace", exception));
            }
        }

        if (!updateOnly)
        {
            foreach (var missing in (pending ?? []).OrderBy(key => key, StringComparer.Ordinal))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "BuiltInPluginNotFound",
                    $"Built-in plugin '{missing}' was not found.",
                    PluginIds.IdFromKey(missing)));
            }
        }

        logger?.LogInformation(
            "Startup stage {Stage} completed in {ElapsedMs}ms across {Count} plugins",
            "plugin-deploy-fingerprint", fingerprintMs, fingerprintCount);
        logger?.LogInformation(
            "Startup stage {Stage} completed in {ElapsedMs}ms across {Count} plugins",
            "plugin-deploy-replace", replaceMs, replaceCount);
        return (replaceCount > 0, diagnostics);
    }

    private static bool MatchesInstalledSource(
        ManagedPluginMarker marker,
        BuiltInPluginSource source,
        bool requireSourceIdentity)
    {
        if (!PluginIds.KeysEqual(PluginIds.Key(source.Manifest.Id, marker.MarketplaceName), source.Key))
            return false;

        if (source.MarketplaceName == null)
            return true;

        if (string.IsNullOrWhiteSpace(marker.SourceIdentity))
            return !requireSourceIdentity;

        return string.Equals(marker.SourceIdentity, source.SourceIdentity, StringComparison.Ordinal);
    }

    private static PluginDiagnostic DeploymentFailure(string pluginId, string path, string phase, Exception exception) =>
        PluginDiagnostic.Error(
            "BuiltInPluginDeployFailed",
            $"Plugin '{pluginId}' could not be deployed: {exception.Message}",
            pluginId,
            path: path,
            parameters: new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["phase"] = System.Text.Json.JsonSerializer.SerializeToElement(phase),
                ["failureType"] = System.Text.Json.JsonSerializer.SerializeToElement(exception.GetType().Name)
            });

    private static void ReplacePluginDirectory(
        string sourceRoot,
        string targetRoot,
        string markerText,
        CancellationToken cancellationToken,
        bool channel)
    {
        var parent = Path.GetDirectoryName(targetRoot)!;
        var tempRoot = Path.Combine(parent, $".{Path.GetFileName(targetRoot)}.{Guid.NewGuid():N}.tmp");
        var backupRoot = Path.Combine(parent, $".{Path.GetFileName(targetRoot)}.{Guid.NewGuid():N}.bak");
        var committed = false;
        try
        {
            if (channel)
                PluginContentTree.CopyAndFingerprint(sourceRoot, tempRoot, [], IncludeChannelFile);
            else
                CopyDirectory(sourceRoot, tempRoot, cancellationToken);
            File.WriteAllText(Path.Combine(tempRoot, MarkerFile), markerText);
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(targetRoot))
                PluginDirectoryMover.Move(targetRoot, backupRoot);
            try
            {
                PluginDirectoryMover.Move(tempRoot, targetRoot);
                committed = true;
            }
            catch
            {
                if (!Directory.Exists(targetRoot) && Directory.Exists(backupRoot))
                    PluginDirectoryMover.Move(backupRoot, targetRoot);
                throw;
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
            if (committed)
                TryDeleteDirectory(backupRoot);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static void CopyDirectory(string sourceRoot, string targetRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var sourceDirectory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceRoot, sourceDirectory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relativePath));
        }

        foreach (var sourceFile in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFileName(sourceFile), MarkerFile, StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            var targetFile = Path.Combine(targetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: true);
        }
    }

    private static ManagedPluginMarker BuildMarker(BuiltInPluginSource source, ManagedPluginMarker? existing) =>
        new(BuildFingerprint(source.PluginRoot, source.Manifest.Channel != null), source.MarketplaceName, source.SourceIdentity,
            source.Manifest.Channel == null ? null : existing?.ChannelInstallationId ?? Guid.NewGuid().ToString("N"));

    private static bool IncludeChannelFile(string relativePath) => relativePath != MarkerFile && relativePath != ChannelPluginInstallation.FileName;

    private static string BuildFingerprint(string sourceRoot, bool channel)
    {
        if (channel)
            return "filesystem;sha256:" + PluginContentTree.Fingerprint(sourceRoot, [], IncludeChannelFile);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var sourceFile in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
                     .Where(file => !string.Equals(Path.GetFileName(file), MarkerFile, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(file => NormalizeRelativePath(sourceRoot, file), StringComparer.Ordinal))
        {
            var relativePath = NormalizeRelativePath(sourceRoot, sourceFile);
            AppendUtf8(hash, relativePath);
            AppendFileBytes(hash, sourceFile);
        }

        return $"filesystem;sha256:{Convert.ToHexString(hash.GetHashAndReset())}";
    }

    private static string NormalizeRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

    private static void AppendUtf8(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static void AppendFileBytes(IncrementalHash hash, string path)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            using var stream = File.OpenRead(path);
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer.AsSpan(0, bytesRead));
            hash.AppendData([0]);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
