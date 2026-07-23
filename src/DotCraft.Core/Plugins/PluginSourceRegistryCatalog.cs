using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Configuration;
using DotCraft.Plugins.Marketplaces;

namespace DotCraft.Plugins;

internal sealed record PluginRegistrySource(
    string Name,
    MarketplaceSourceKind Kind,
    string Url,
    string MarketplacePath,
    bool IsDefault);

/// <summary>
/// Discovers installable plugins from configured marketplaces.
/// Reads materialized roots and cached snapshots only; a repository fetch happens exclusively
/// through the explicit marketplace add and refresh operations.
/// </summary>
internal static class PluginSourceRegistryCatalog
{
    public const string DefaultRegistryUrlEnvironmentVariableName = "DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL";
    public const string AdditionalRegistriesEnvironmentVariableName = "DOTCRAFT_PLUGIN_REGISTRIES";
    public const string DefaultMarketplacePath = MarketplaceDocumentLoader.DefaultMarketplacePath;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(2);

    public static IReadOnlyList<BuiltInPluginSource> Discover(
        AppConfig.PluginsConfig? pluginsConfig,
        List<PluginDiagnostic> diagnostics,
        string? craftHome = null)
    {
        var sources = ResolveSources(pluginsConfig, diagnostics);
        if (sources.Count == 0)
            return [];

        var resolvedCraftHome = string.IsNullOrWhiteSpace(craftHome)
            ? MarketplacePaths.DefaultCraftHome()
            : Path.GetFullPath(craftHome);

        var plugins = new List<BuiltInPluginSource>();
        foreach (var source in sources)
        {
            var snapshotRoot = ResolveSnapshotRoot(source, diagnostics, resolvedCraftHome);
            if (snapshotRoot == null)
                continue;

            var registryRoot = MarketplaceDocumentLoader.ResolveRoot(snapshotRoot, source.MarketplacePath);
            if (registryRoot == null)
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "PluginRegistryMarketplaceMissing",
                    $"Plugin marketplace '{source.Name}' does not contain '{source.MarketplacePath}'.",
                    path: snapshotRoot));
                continue;
            }

            var marketplacePath = Path.GetFullPath(Path.Combine(
                registryRoot,
                MarketplaceDocumentLoader.NormalizeRelativePath(source.MarketplacePath)));
            var document = MarketplaceDocumentLoader.TryLoad(marketplacePath, out var loadError);
            if (document == null)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginRegistryMarketplaceJson",
                    loadError,
                    path: marketplacePath));
                continue;
            }

            if (document.Plugins.Count == 0)
                continue;

            var marketplaceName = string.IsNullOrWhiteSpace(document.Name) ? source.Name : document.Name!.Trim();
            foreach (var entry in document.Plugins)
            {
                var plugin = BuildPluginSource(marketplaceName, registryRoot, marketplacePath, entry, diagnostics);
                if (plugin != null)
                    plugins.Add(plugin);
            }
        }

        return plugins;
    }

    /// <summary>
    /// Drops the freshness marker for an archive source so the next discovery pass re-downloads it.
    /// </summary>
    public static void InvalidateArchiveCache(string url, string marketplacePath)
    {
        var markerPath = Path.Combine(CacheRootFor(url, marketplacePath), "updatedAt.txt");
        try
        {
            if (File.Exists(markerPath))
                File.Delete(markerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale marker only delays the next refresh; it is not worth failing the request.
        }
    }

    private static IReadOnlyList<PluginRegistrySource> ResolveSources(
        AppConfig.PluginsConfig? pluginsConfig,
        List<PluginDiagnostic> diagnostics)
    {
        var sources = new List<PluginRegistrySource>();
        var defaultUrl = Environment.GetEnvironmentVariable(DefaultRegistryUrlEnvironmentVariableName);
        if (pluginsConfig?.DisableDefaultPluginRegistry != true
            && !string.IsNullOrWhiteSpace(defaultUrl))
        {
            sources.Add(new PluginRegistrySource(
                "DotCraft Official Plugins",
                MarketplaceSourceKind.Archive,
                defaultUrl.Trim(),
                DefaultMarketplacePath,
                IsDefault: true));
        }

        var additional = Environment.GetEnvironmentVariable(AdditionalRegistriesEnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(additional))
        {
            foreach (var url in additional.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                sources.Add(new PluginRegistrySource(
                    url,
                    MarketplaceSourceKind.Archive,
                    url,
                    DefaultMarketplacePath,
                    IsDefault: false));
            }
        }

        if (pluginsConfig?.PluginRegistries is { Count: > 0 })
        {
            foreach (var configured in pluginsConfig.PluginRegistries)
            {
                if (string.IsNullOrWhiteSpace(configured.Url))
                {
                    diagnostics.Add(PluginDiagnostic.Warning(
                        "PluginRegistryUrlMissing",
                        "Plugin marketplace source was skipped because url is missing."));
                    continue;
                }

                MarketplaceSource source;
                try
                {
                    source = MarketplaceSourceParser.FromConfigured(
                        configured.SourceType,
                        configured.Url,
                        configured.Ref,
                        configured.SparsePaths);
                }
                catch (MarketplaceException ex)
                {
                    diagnostics.Add(PluginDiagnostic.Warning(
                        "InvalidPluginRegistrySource",
                        $"Plugin marketplace source was skipped: {ex.Message}",
                        path: configured.Url));
                    continue;
                }

                sources.Add(new PluginRegistrySource(
                    NormalizeOptional(configured.Name) ?? configured.Url.Trim(),
                    source.Kind,
                    source.Value,
                    NormalizeOptional(configured.MarketplacePath) ?? DefaultMarketplacePath,
                    IsDefault: false));
            }
        }

        return sources;
    }

    private static BuiltInPluginSource? BuildPluginSource(
        string marketplaceName,
        string registryRoot,
        string marketplacePath,
        MarketplaceDocumentEntry entry,
        List<PluginDiagnostic> diagnostics)
    {
        var name = NormalizeOptional(entry.Name);
        if (!PluginManifestParser.IsValidPluginId(name))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginRegistryEntryName",
                "Plugin marketplace entry name is invalid.",
                name,
                path: marketplacePath));
            return null;
        }

        var pluginId = name!;
        var entrySource = entry.Source;
        if (entrySource == null || !string.Equals(entrySource.Source, "local", StringComparison.Ordinal))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginRegistryEntrySource",
                "Plugin marketplace entry source.source must be local.",
                pluginId,
                path: marketplacePath));
            return null;
        }

        var entryPath = NormalizeOptional(entrySource.Path);
        if (entryPath == null)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginRegistryEntryPath",
                "Plugin marketplace entry source.path is required.",
                pluginId,
                path: marketplacePath));
            return null;
        }

        var relativePath = ResolveRegistryRelativePath(entryPath, out var pathError);
        if (relativePath == null)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginRegistryEntryPath",
                $"Plugin marketplace entry source.path is invalid: {pathError}",
                pluginId,
                path: marketplacePath));
            return null;
        }

        if (!string.Equals(entry.Policy?.Installation, "AVAILABLE", StringComparison.Ordinal))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "PluginRegistryEntryNotAvailable",
                $"Plugin marketplace entry '{pluginId}' was skipped because it is not available for installation.",
                pluginId,
                path: marketplacePath));
            return null;
        }

        var pluginRoot = Path.GetFullPath(Path.Combine(registryRoot, relativePath));
        if (!IsPathWithin(pluginRoot, registryRoot))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginRegistryEntryPath",
                "Plugin marketplace entry source.path must stay within the marketplace root.",
                pluginId,
                path: marketplacePath));
            return null;
        }

        if (!Directory.Exists(pluginRoot))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginRegistryPluginMissing",
                $"Plugin marketplace entry '{pluginId}' points to a missing plugin directory.",
                pluginId,
                path: pluginRoot));
            return null;
        }

        var parse = PluginManifestParser.Load(pluginRoot);
        diagnostics.AddRange(parse.Diagnostics);
        if (parse.Manifest == null)
            return null;

        if (!PluginIds.EqualsCanonical(parse.Manifest.Id, pluginId))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginRegistryManifestIdMismatch",
                $"Plugin marketplace entry name '{pluginId}' does not match plugin manifest id '{parse.Manifest.Id}'.",
                pluginId,
                path: parse.Manifest.ManifestPath));
            return null;
        }

        return new BuiltInPluginSource(parse.Manifest, pluginRoot, registryRoot, marketplaceName);
    }

    private static string? ResolveSnapshotRoot(
        PluginRegistrySource source,
        List<PluginDiagnostic> diagnostics,
        string craftHome)
    {
        if (source.Kind == MarketplaceSourceKind.Git)
            return ResolveMaterializedRoot(source, diagnostics, craftHome);

        if (Directory.Exists(source.Url))
            return Path.GetFullPath(source.Url);

        if (source.Kind == MarketplaceSourceKind.Local)
        {
            if (!File.Exists(source.Url))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "PluginRegistrySourceMissing",
                    $"Plugin marketplace '{source.Name}' is not available on this machine.",
                    path: source.Url));
                return null;
            }

            return ExtractArchiveToCache(source, File.ReadAllBytes(source.Url), diagnostics)
                   ?? ResolveCachedSnapshot(source);
        }

        return ResolveArchiveSnapshotRoot(source, diagnostics);
    }

    // Repository marketplaces are materialized by the explicit add and refresh operations.
    // Discovery reads whatever is already on disk and never starts a fetch of its own.
    private static string? ResolveMaterializedRoot(
        PluginRegistrySource source,
        List<PluginDiagnostic> diagnostics,
        string craftHome)
    {
        string root;
        try
        {
            root = MarketplaceStore.ResolveRoot(craftHome, source.Name);
        }
        catch (MarketplaceException ex)
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "InvalidPluginRegistrySource",
                ex.Message,
                path: source.Url));
            return null;
        }

        if (Directory.Exists(root))
            return root;

        diagnostics.Add(PluginDiagnostic.Warning(
            "PluginRegistrySnapshotMissing",
            $"Plugin marketplace '{source.Name}' has not been fetched yet; refresh it to install its plugins.",
            path: root));
        return null;
    }

    private static string? ResolveArchiveSnapshotRoot(
        PluginRegistrySource source,
        List<PluginDiagnostic> diagnostics)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "InvalidPluginRegistrySourceUrl",
                $"Plugin marketplace '{source.Name}' must be an HTTPS archive URL or a local archive/directory path.",
                path: source.Url));
            return null;
        }

        var cacheRoot = CacheRootFor(source.Url, source.MarketplacePath);
        var snapshotRoot = Path.Combine(cacheRoot, "snapshot");
        if (Directory.Exists(snapshotRoot) && !ShouldRefresh(cacheRoot))
            return snapshotRoot;

        try
        {
            using var client = new HttpClient { Timeout = DownloadTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DotCraft");
            using var response = client.GetAsync(uri).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "PluginRegistryDownloadFailed",
                    $"Plugin marketplace '{source.Name}' download failed with HTTP {(int)response.StatusCode}.",
                    path: source.Url));
                return ResolveCachedSnapshot(source);
            }

            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return ExtractArchiveToCache(source, bytes, diagnostics) ?? ResolveCachedSnapshot(source);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "PluginRegistryDownloadFailed",
                $"Plugin marketplace '{source.Name}' download failed: {ex.Message}",
                path: source.Url));
            return ResolveCachedSnapshot(source);
        }
    }

    private static bool ShouldRefresh(string cacheRoot)
    {
        var markerPath = Path.Combine(cacheRoot, "updatedAt.txt");
        if (!File.Exists(markerPath))
            return true;
        var updatedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(markerPath), TimeSpan.Zero);
        return DateTimeOffset.UtcNow - updatedAt > RefreshInterval;
    }

    private static string? ResolveCachedSnapshot(PluginRegistrySource source)
    {
        var snapshotRoot = Path.Combine(CacheRootFor(source.Url, source.MarketplacePath), "snapshot");
        return Directory.Exists(snapshotRoot) ? snapshotRoot : null;
    }

    private static string? ExtractArchiveToCache(
        PluginRegistrySource source,
        byte[] bytes,
        List<PluginDiagnostic> diagnostics)
    {
        var cacheRoot = CacheRootFor(source.Url, source.MarketplacePath);
        var parent = Path.GetDirectoryName(cacheRoot)!;
        var tempRoot = Path.Combine(parent, $".{Path.GetFileName(cacheRoot)}.{Guid.NewGuid():N}.tmp");
        var tempSnapshot = Path.Combine(tempRoot, "snapshot");
        try
        {
            Directory.CreateDirectory(tempSnapshot);
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullName))
                    continue;

                var destination = Path.GetFullPath(Path.Combine(tempSnapshot, entry.FullName));
                if (!IsPathWithin(destination, tempSnapshot))
                    throw new InvalidDataException($"Zip entry '{entry.FullName}' escapes the marketplace snapshot root.");

                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                    || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }

            Directory.CreateDirectory(parent);
            if (Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
            Directory.Move(tempRoot, cacheRoot);
            File.WriteAllText(Path.Combine(cacheRoot, "updatedAt.txt"), DateTimeOffset.UtcNow.ToString("O"));
            return Path.Combine(cacheRoot, "snapshot");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "PluginRegistryExtractFailed",
                $"Plugin marketplace '{source.Name}' archive could not be extracted: {ex.Message}",
                path: source.Url));
            return null;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CacheRootFor(string url, string marketplacePath)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            url + "\n" + marketplacePath))).ToLowerInvariant();
        return Path.Combine(MarketplacePaths.DefaultCraftHome(), "cache", "plugin-registries", key);
    }

    private static string? ResolveRegistryRelativePath(string path, out string error)
    {
        error = string.Empty;
        if (!path.StartsWith("./", StringComparison.Ordinal))
        {
            error = "path must start with './'";
            return null;
        }

        var relative = path[2..];
        if (string.IsNullOrWhiteSpace(relative))
            return string.Empty;
        if (relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
        {
            error = "path must not contain '..'";
            return null;
        }

        return relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private static bool IsPathWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
