using System.Buffers;
using System.IO.Compression;
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
    AppConfig.PluginsConfig? pluginsConfig = null)
{
    private static readonly Lock DeploymentLock = new();

    public const string MarkerFile = ".builtin";

    /// <summary>
    /// Deploys configured built-in plugins into the workspace plugin directory.
    /// </summary>
    public IReadOnlyList<PluginDiagnostic> Deploy()
        => DeployCore(targetPluginId: null);

    /// <summary>
    /// Deploys one configured built-in plugin into the workspace plugin directory.
    /// </summary>
    public IReadOnlyList<PluginDiagnostic> DeployPlugin(string pluginId)
        => DeployCore(PluginIds.Canonicalize(pluginId));

    public static bool IsManagedBuiltInPluginRoot(string pluginRoot) =>
        File.Exists(Path.Combine(pluginRoot, MarkerFile));

    private IReadOnlyList<PluginDiagnostic> DeployCore(string? targetPluginId)
    {
        lock (DeploymentLock)
        {
            return DeployCoreLocked(targetPluginId);
        }
    }

    private IReadOnlyList<PluginDiagnostic> DeployCoreLocked(string? targetPluginId)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var sources = BuiltInPluginSourceResolver.Discover(sourceRoots, diagnostics, pluginsConfig);
        var foundTarget = string.IsNullOrWhiteSpace(targetPluginId);

        Directory.CreateDirectory(workspacePluginsPath);
        foreach (var source in sources)
        {
            if (!string.IsNullOrWhiteSpace(targetPluginId)
                && !PluginIds.EqualsCanonical(source.Manifest.Id, targetPluginId))
            {
                continue;
            }

            foundTarget = true;
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

            var markerText = source.RemotePackage == null
                ? BuildMarkerText(source.PluginRoot)
                : BuildRemoteMarkerText(source.RemotePackage);
            if (File.Exists(markerPath)
                && string.Equals(File.ReadAllText(markerPath).Trim(), markerText, StringComparison.Ordinal))
            {
                continue;
            }

            if (source.RemotePackage == null)
                ReplacePluginDirectory(source.PluginRoot, pluginDir, markerText);
            else
                DeployRemotePlugin(source, pluginDir, markerText, diagnostics);
        }

        if (!foundTarget && !string.IsNullOrWhiteSpace(targetPluginId))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "BuiltInPluginNotFound",
                $"Built-in plugin '{targetPluginId}' was not found.",
                targetPluginId));
        }

        return diagnostics;
    }

    private static void ReplacePluginDirectory(string sourceRoot, string targetRoot, string markerText)
    {
        var parent = Path.GetDirectoryName(targetRoot)!;
        var tempRoot = Path.Combine(parent, $".{Path.GetFileName(targetRoot)}.{Guid.NewGuid():N}.tmp");
        try
        {
            CopyDirectory(sourceRoot, tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, MarkerFile), markerText);

            if (Directory.Exists(targetRoot))
                Directory.Delete(targetRoot, recursive: true);

            Directory.Move(tempRoot, targetRoot);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void DeployRemotePlugin(
        BuiltInPluginSource source,
        string pluginDir,
        string markerText,
        List<PluginDiagnostic> diagnostics)
    {
        var package = source.RemotePackage!;
        var parent = Path.GetDirectoryName(pluginDir)!;
        var extractRoot = Path.Combine(parent, $".{Path.GetFileName(pluginDir)}.{Guid.NewGuid():N}.extract");
        try
        {
            var zipBytes = DownloadRemotePackage(source, package, diagnostics);
            if (zipBytes == null)
                return;

            ExtractZipSafely(zipBytes, extractRoot);
            var pluginRoot = ResolveExtractedPluginRoot(extractRoot, source.Manifest.Id, diagnostics);
            if (pluginRoot == null)
                return;

            ReplacePluginDirectory(pluginRoot, pluginDir, markerText);
        }
        catch (InvalidDataException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "RemotePluginPackageInvalid",
                $"Remote plugin '{source.Manifest.Id}' package is invalid: {ex.Message}",
                source.Manifest.Id,
                path: package.Url));
        }
        catch (IOException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "RemotePluginInstallFailed",
                $"Remote plugin '{source.Manifest.Id}' could not be installed: {ex.Message}",
                source.Manifest.Id,
                path: package.Url));
        }
        finally
        {
            if (Directory.Exists(extractRoot))
                Directory.Delete(extractRoot, recursive: true);
        }
    }

    private static byte[]? DownloadRemotePackage(
        BuiltInPluginSource source,
        RemoteBuiltInPluginPackage package,
        List<PluginDiagnostic> diagnostics)
    {
        try
        {
            using var client = new HttpClient();
            using var response = client.GetAsync(package.Url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "RemotePluginDownloadFailed",
                    $"Remote plugin '{source.Manifest.Id}' package download failed with HTTP {(int)response.StatusCode}.",
                    source.Manifest.Id,
                    path: package.Url));
                return null;
            }

            var zipBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var actual = Convert.ToHexString(SHA256.HashData(zipBytes));
            if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "RemotePluginSha256Mismatch",
                    $"Remote plugin '{source.Manifest.Id}' package checksum mismatch.",
                    source.Manifest.Id,
                    path: package.Url));
                return null;
            }

            return zipBytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "RemotePluginDownloadFailed",
                $"Remote plugin '{source.Manifest.Id}' package download failed: {ex.Message}",
                source.Manifest.Id,
                path: package.Url));
            return null;
        }
    }

    private static void ExtractZipSafely(byte[] zipBytes, string extractRoot)
    {
        Directory.CreateDirectory(extractRoot);
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
                continue;

            var destination = Path.GetFullPath(Path.Combine(extractRoot, entry.FullName));
            if (!IsPathWithin(destination, extractRoot))
                throw new InvalidDataException($"Zip entry '{entry.FullName}' escapes the plugin package root.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private static string? ResolveExtractedPluginRoot(
        string extractRoot,
        string expectedPluginId,
        List<PluginDiagnostic> diagnostics)
    {
        var candidates = new List<string>();
        if (PluginManifestParser.IsValidPluginRoot(extractRoot))
            candidates.Add(extractRoot);
        candidates.AddRange(Directory.GetDirectories(extractRoot)
            .Where(PluginManifestParser.IsValidPluginRoot));

        foreach (var candidate in candidates)
        {
            var parse = PluginManifestParser.Load(candidate);
            diagnostics.AddRange(parse.Diagnostics);
            if (parse.Manifest == null)
                continue;

            if (PluginIds.EqualsCanonical(parse.Manifest.Id, expectedPluginId))
                return candidate;

            diagnostics.Add(PluginDiagnostic.Error(
                "RemotePluginManifestIdMismatch",
                $"Remote plugin package manifest id '{parse.Manifest.Id}' does not match expected id '{expectedPluginId}'.",
                expectedPluginId,
                path: parse.Manifest.ManifestPath));
        }

        diagnostics.Add(PluginDiagnostic.Error(
            "RemotePluginManifestMissing",
            $"Remote plugin package for '{expectedPluginId}' does not contain a matching .craft-plugin/plugin.json.",
            expectedPluginId,
            path: extractRoot));
        return null;
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

    private static string BuildRemoteMarkerText(RemoteBuiltInPluginPackage package) =>
        $"githubRelease;version:{package.Version};sha256:{package.Sha256}";

    private static bool IsPathWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
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
