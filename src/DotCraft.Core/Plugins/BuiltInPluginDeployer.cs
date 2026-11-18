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
        => DeployCore(targetKeys: null);

    public IReadOnlyList<PluginDiagnostic> DeployPlugin(string pluginKey)
        => DeployPlugins([pluginKey]);

    public IReadOnlyList<PluginDiagnostic> DeployPlugins(IReadOnlyCollection<string> pluginKeys)
    {
        var targets = pluginKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return targets.Count == 0 ? [] : DeployCore(targets);
    }

    public IReadOnlyList<string> GetPendingUpdates(
        List<PluginDiagnostic> diagnostics,
        string? marketplaceName = null)
    {
        var sources = BuiltInPluginSourceResolver.Discover(sourceRoots, diagnostics, pluginsConfig, userDataPath);

        var pending = new List<string>();
        foreach (var source in sources)
        {
            if (marketplaceName != null
                && !string.Equals(source.MarketplaceName, marketplaceName, StringComparison.OrdinalIgnoreCase))
                continue;

            var targetRoot = Path.Combine(pluginsPath, source.Manifest.Id);
            ManagedPluginMarker? marker;
            try
            {
                marker = ManagedPluginMarker.Read(targetRoot);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(DeploymentFailure(source.Manifest.Id, targetRoot, "check", exception));
                continue;
            }

            if (marker == null || !PluginIds.KeysEqual(PluginIds.Key(source.Manifest.Id, marker.MarketplaceName), source.Key))
                continue;

            var installed = PluginManifestParser.Load(targetRoot);
            diagnostics.AddRange(installed.Diagnostics);
            if (installed.Manifest == null || installed.Manifest.Dotnet != null || source.Manifest.Dotnet != null)
                continue;

            if (marker != BuildMarker(source))
                pending.Add(source.Key);
        }

        return pending;
    }

    public static bool IsManagedBuiltInPluginRoot(string pluginRoot) =>
        File.Exists(Path.Combine(pluginRoot, MarkerFile));

    private IReadOnlyList<PluginDiagnostic> DeployCore(IReadOnlySet<string>? targetKeys)
    {
        var identity = Path.GetFullPath(pluginsPath);
        if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
        using var mutex = new Mutex(false, "DotCraft.PluginDeploy." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
        var timer = Stopwatch.StartNew();
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
        try
        {
            logger?.LogInformation("Startup stage {Stage} completed in {ElapsedMs}ms",
                "plugin-deploy-mutex-wait", timer.Elapsed.TotalMilliseconds);
            return DeployCoreLocked(targetKeys);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private IReadOnlyList<PluginDiagnostic> DeployCoreLocked(IReadOnlySet<string>? targetKeys)
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
                diagnostics.Add(PluginDiagnostic.Info(
                    "BuiltInPluginUserOwned",
                    $"Built-in plugin '{pluginId}' was not deployed because the target directory is user-owned.",
                    pluginId,
                    path: pluginDir));
                continue;
            }

            try
            {
                var installedMarker = ManagedPluginMarker.Read(pluginDir);
                if (installedMarker != null
                    && !PluginIds.KeysEqual(PluginIds.Key(pluginId, installedMarker.MarketplaceName), source.Key))
                {
                    diagnostics.Add(PluginDiagnostic.Error(
                        "PluginInstalledFromOtherSource",
                        $"Plugin '{pluginId}' is already installed from another source. Remove it before installing '{source.Key}'.",
                        pluginId,
                        path: pluginDir));
                    continue;
                }

                timer.Restart();
                var marker = BuildMarker(source);
                fingerprintMs += timer.Elapsed.TotalMilliseconds;
                fingerprintCount++;
                if (installedMarker == marker)
                    continue;

                timer.Restart();
                ReplacePluginDirectory(source.PluginRoot, pluginDir, marker.Format());
                replaceMs += timer.Elapsed.TotalMilliseconds;
                replaceCount++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(DeploymentFailure(pluginId, pluginDir, "replace", exception));
            }
        }

        foreach (var missing in (pending ?? []).OrderBy(key => key, StringComparer.Ordinal))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "BuiltInPluginNotFound",
                $"Built-in plugin '{missing}' was not found.",
                PluginIds.IdFromKey(missing)));
        }

        logger?.LogInformation(
            "Startup stage {Stage} completed in {ElapsedMs}ms across {Count} plugins",
            "plugin-deploy-fingerprint", fingerprintMs, fingerprintCount);
        logger?.LogInformation(
            "Startup stage {Stage} completed in {ElapsedMs}ms across {Count} plugins",
            "plugin-deploy-replace", replaceMs, replaceCount);
        return diagnostics;
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

    private static void ReplacePluginDirectory(string sourceRoot, string targetRoot, string markerText)
    {
        var parent = Path.GetDirectoryName(targetRoot)!;
        var tempRoot = Path.Combine(parent, $".{Path.GetFileName(targetRoot)}.{Guid.NewGuid():N}.tmp");
        var backupRoot = Path.Combine(parent, $".{Path.GetFileName(targetRoot)}.{Guid.NewGuid():N}.bak");
        var committed = false;
        try
        {
            CopyDirectory(sourceRoot, tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, MarkerFile), markerText);

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

    private static void CopyDirectory(string sourceRoot, string targetRoot)
    {
        Directory.CreateDirectory(targetRoot);
        foreach (var sourceDirectory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceDirectory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relativePath));
        }

        foreach (var sourceFile in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(sourceFile), MarkerFile, StringComparison.OrdinalIgnoreCase))
                continue;

            var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
            var targetFile = Path.Combine(targetRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: true);
        }
    }

    private static ManagedPluginMarker BuildMarker(BuiltInPluginSource source) =>
        new(BuildFingerprint(source.PluginRoot), source.MarketplaceName);

    private static string BuildFingerprint(string sourceRoot)
    {
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
