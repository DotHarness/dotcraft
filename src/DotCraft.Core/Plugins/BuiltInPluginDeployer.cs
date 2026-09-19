using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Configuration;

namespace DotCraft.Plugins;

/// <summary>
/// Copies desktop-bundled built-in plugin directories into a workspace.
/// </summary>
public sealed class BuiltInPluginDeployer(
    string workspacePluginsPath,
    IReadOnlyList<string>? sourceRoots = null,
    AppConfig.PluginsConfig? pluginsConfig = null,
    string? userDataPath = null)
{
    private static readonly Lock DeploymentLock = new();

    public const string MarkerFile = ".builtin";

    /// <summary>
    /// Deploys configured built-in plugins into the workspace plugin directory.
    /// </summary>
    public IReadOnlyList<PluginDiagnostic> Deploy()
        => DeployCore(targetPluginIds: null);

    /// <summary>
    /// Deploys one configured built-in plugin into the workspace plugin directory.
    /// </summary>
    public IReadOnlyList<PluginDiagnostic> DeployPlugin(string pluginId)
        => DeployPlugins([pluginId]);

    /// <summary>
    /// Deploys the named built-in plugins in one pass, so the sources behind them resolve once.
    /// </summary>
    public IReadOnlyList<PluginDiagnostic> DeployPlugins(IReadOnlyCollection<string> pluginIds)
    {
        var targets = pluginIds
            .Select(PluginIds.Canonicalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return targets.Count == 0 ? [] : DeployCore(targets);
    }

    public static bool IsManagedBuiltInPluginRoot(string pluginRoot) =>
        File.Exists(Path.Combine(pluginRoot, MarkerFile));

    private IReadOnlyList<PluginDiagnostic> DeployCore(IReadOnlySet<string>? targetPluginIds)
    {
        lock (DeploymentLock)
        {
            return DeployCoreLocked(targetPluginIds);
        }
    }

    private IReadOnlyList<PluginDiagnostic> DeployCoreLocked(IReadOnlySet<string>? targetPluginIds)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var sources = BuiltInPluginSourceResolver.Discover(
            sourceRoots,
            diagnostics,
            pluginsConfig,
            userDataPath);
        var pending = targetPluginIds == null
            ? null
            : new HashSet<string>(targetPluginIds, StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(workspacePluginsPath);
        foreach (var source in sources)
        {
            if (pending != null && !pending.Remove(PluginIds.Canonicalize(source.Manifest.Id)))
                continue;

            var pluginId = source.Manifest.Id;
            var pluginDir = Path.Combine(workspacePluginsPath, pluginId);
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

            var markerText = BuildMarkerText(source.PluginRoot);
            if (File.Exists(markerPath)
                && string.Equals(File.ReadAllText(markerPath).Trim(), markerText, StringComparison.Ordinal))
            {
                continue;
            }

            ReplacePluginDirectory(source.PluginRoot, pluginDir, markerText);
        }

        foreach (var missing in (pending ?? []).OrderBy(id => id, StringComparer.Ordinal))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "BuiltInPluginNotFound",
                $"Built-in plugin '{missing}' was not found.",
                missing));
        }

        return diagnostics;
    }

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
                Directory.Move(targetRoot, backupRoot);
            try
            {
                Directory.Move(tempRoot, targetRoot);
                committed = true;
            }
            catch
            {
                if (!Directory.Exists(targetRoot) && Directory.Exists(backupRoot))
                    Directory.Move(backupRoot, targetRoot);
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
            // The committed target remains authoritative; stale staging is harmless.
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

    private static string BuildMarkerText(string sourceRoot)
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
