using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Configuration;

namespace DotCraft.Plugins;

internal sealed record PluginRegistrySource(
    string Name,
    string Url,
    string MarketplacePath,
    bool IsDefault);

internal static class PluginSourceRegistryCatalog
{
    public const string DefaultRegistryUrlEnvironmentVariableName = "DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL";
    public const string AdditionalRegistriesEnvironmentVariableName = "DOTCRAFT_PLUGIN_REGISTRIES";
    public const string DefaultMarketplacePath = ".craft/plugins/marketplace.json";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<BuiltInPluginSource> Discover(
        AppConfig.PluginsConfig? pluginsConfig,
        List<PluginDiagnostic> diagnostics)
    {
        var sources = ResolveSources(pluginsConfig, diagnostics);
        if (sources.Count == 0)
            return [];

        var plugins = new List<BuiltInPluginSource>();
        foreach (var source in sources)
        {
            var snapshotRoot = ResolveSnapshotRoot(source, diagnostics);
            if (snapshotRoot == null)
                continue;

            var registryRoot = ResolveRegistryRoot(snapshotRoot, source.MarketplacePath, diagnostics, source);
            if (registryRoot == null)
                continue;

            var marketplacePath = Path.GetFullPath(Path.Combine(registryRoot, NormalizeRelativePath(source.MarketplacePath)));
            PluginRegistryDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<PluginRegistryDocument>(
                    File.ReadAllText(marketplacePath),
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginRegistryMarketplaceJson",
                    $"Failed to parse plugin registry marketplace JSON: {ex.Message}",
                    path: marketplacePath));
                continue;
            }
            catch (IOException ex)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "PluginRegistryMarketplaceReadFailed",
                    $"Failed to read plugin registry marketplace: {ex.Message}",
                    path: marketplacePath));
                continue;
            }

            if (document?.Plugins is not { Count: > 0 })
                continue;

            foreach (var entry in document.Plugins)
            {
                var plugin = BuildPluginSource(source, registryRoot, marketplacePath, entry, diagnostics);
                if (plugin != null)
                    plugins.Add(plugin);
            }
        }

        return plugins;
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
                        "Plugin registry source was skipped because url is missing."));
                    continue;
                }

                sources.Add(new PluginRegistrySource(
                    NormalizeOptional(configured.Name) ?? configured.Url.Trim(),
                    configured.Url.Trim(),
                    NormalizeOptional(configured.MarketplacePath) ?? DefaultMarketplacePath,
                    IsDefault: false));
            }
        }

        return sources;
    }

    private static BuiltInPluginSource? BuildPluginSource(
        PluginRegistrySource source,
        string registryRoot,
        string marketplacePath,
        PluginRegistryEntry entry,
        List<PluginDiagnostic> diagnostics)
    {
        var name = NormalizeOptional(entry.Name);
        if (!PluginManifestParser.IsValidPluginId(name))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginRegistryEntryName",
                "Plugin registry entry name is invalid.",
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
                "Plugin registry entry source.source must be local.",
                pluginId,
                path: marketplacePath));
            return null;
        }

        var entryPath = NormalizeOptional(entrySource.Path);
        if (entryPath == null)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginRegistryEntryPath",
                "Plugin registry entry source.path is required.",
                pluginId,
                path: marketplacePath));
            return null;
        }

        var relativePath = ResolveRegistryRelativePath(entryPath, out var pathError);
        if (relativePath == null)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginRegistryEntryPath",
                $"Plugin registry entry source.path is invalid: {pathError}",
                pluginId,
                path: marketplacePath));
            return null;
        }

        if (!string.Equals(entry.Policy?.Installation, "AVAILABLE", StringComparison.Ordinal))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "PluginRegistryEntryNotAvailable",
                $"Plugin registry entry '{pluginId}' was skipped because it is not available for installation.",
                pluginId,
                path: marketplacePath));
            return null;
        }

        var pluginRoot = Path.GetFullPath(Path.Combine(registryRoot, relativePath));
        if (!IsPathWithin(pluginRoot, registryRoot))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginRegistryEntryPath",
                "Plugin registry entry source.path must stay within the registry snapshot.",
                pluginId,
                path: marketplacePath));
            return null;
        }

        if (!Directory.Exists(pluginRoot))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginRegistryPluginMissing",
                $"Plugin registry entry '{pluginId}' points to a missing plugin directory.",
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
                $"Plugin registry entry name '{pluginId}' does not match plugin manifest id '{parse.Manifest.Id}'.",
                pluginId,
                path: parse.Manifest.ManifestPath));
            return null;
        }

        return new BuiltInPluginSource(parse.Manifest, pluginRoot, registryRoot);
    }

    private static string? ResolveSnapshotRoot(
        PluginRegistrySource source,
        List<PluginDiagnostic> diagnostics)
    {
        if (Directory.Exists(source.Url))
            return Path.GetFullPath(source.Url);

        if (File.Exists(source.Url))
            return ExtractArchiveToCache(source, File.ReadAllBytes(source.Url), diagnostics, isRefresh: true)
                   ?? ResolveCachedSnapshot(source, diagnostics, warnWhenMissing: false);

        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "InvalidPluginRegistrySourceUrl",
                $"Plugin registry source '{source.Name}' must be an HTTPS archive URL or a local archive/directory path.",
                path: source.Url));
            return null;
        }

        var cacheRoot = CacheRootFor(source);
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
                    $"Plugin registry source '{source.Name}' download failed with HTTP {(int)response.StatusCode}.",
                    path: source.Url));
                return ResolveCachedSnapshot(source, diagnostics, warnWhenMissing: false);
            }

            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return ExtractArchiveToCache(source, bytes, diagnostics, isRefresh: true)
                   ?? ResolveCachedSnapshot(source, diagnostics, warnWhenMissing: false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "PluginRegistryDownloadFailed",
                $"Plugin registry source '{source.Name}' download failed: {ex.Message}",
                path: source.Url));
            return ResolveCachedSnapshot(source, diagnostics, warnWhenMissing: false);
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

    private static string? ResolveCachedSnapshot(
        PluginRegistrySource source,
        List<PluginDiagnostic> diagnostics,
        bool warnWhenMissing)
    {
        var snapshotRoot = Path.Combine(CacheRootFor(source), "snapshot");
        if (Directory.Exists(snapshotRoot))
            return snapshotRoot;

        if (warnWhenMissing)
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "PluginRegistryCacheMissing",
                $"Plugin registry source '{source.Name}' has no cached snapshot.",
                path: source.Url));
        }

        return null;
    }

    private static string? ExtractArchiveToCache(
        PluginRegistrySource source,
        byte[] bytes,
        List<PluginDiagnostic> diagnostics,
        bool isRefresh)
    {
        var cacheRoot = CacheRootFor(source);
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
                    throw new InvalidDataException($"Zip entry '{entry.FullName}' escapes the registry snapshot root.");

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
                isRefresh ? "PluginRegistryExtractFailed" : "PluginRegistryCacheRefreshFailed",
                $"Plugin registry source '{source.Name}' archive could not be extracted: {ex.Message}",
                path: source.Url));
            return null;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string? ResolveRegistryRoot(
        string snapshotRoot,
        string marketplacePath,
        List<PluginDiagnostic> diagnostics,
        PluginRegistrySource source)
    {
        if (!IsSafeRelativePath(marketplacePath))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "InvalidPluginRegistryMarketplacePath",
                $"Plugin registry source '{source.Name}' marketplace path must be relative and stay within the registry snapshot.",
                path: marketplacePath));
            return null;
        }

        var relativeMarketplacePath = NormalizeRelativePath(marketplacePath);
        var direct = Path.Combine(snapshotRoot, relativeMarketplacePath);
        if (File.Exists(direct))
            return Path.GetFullPath(snapshotRoot);

        var children = Directory.GetDirectories(snapshotRoot);
        if (children.Length == 1)
        {
            var nested = Path.Combine(children[0], relativeMarketplacePath);
            if (File.Exists(nested))
                return Path.GetFullPath(children[0]);
        }

        diagnostics.Add(PluginDiagnostic.Warning(
            "PluginRegistryMarketplaceMissing",
            $"Plugin registry source '{source.Name}' does not contain marketplace '{marketplacePath}'.",
            path: snapshotRoot));
        return null;
    }

    private static string CacheRootFor(PluginRegistrySource source)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            home = Path.GetTempPath();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            source.Url + "\n" + source.MarketplacePath))).ToLowerInvariant();
        return Path.Combine(home, ".craft", "cache", "plugin-registries", key);
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        return normalized.Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsSafeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized))
            return false;
        return normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment != "..");
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

    private sealed class PluginRegistryDocument
    {
        public string? Name { get; set; }

        [JsonPropertyName("interface")]
        public PluginRegistryInterface? Interface { get; set; }

        public List<PluginRegistryEntry> Plugins { get; set; } = [];
    }

    private sealed class PluginRegistryInterface
    {
        public string? DisplayName { get; set; }
    }

    private sealed class PluginRegistryEntry
    {
        public string? Name { get; set; }

        public PluginRegistryEntrySource? Source { get; set; }

        public PluginRegistryEntryPolicy? Policy { get; set; }

        public string? Category { get; set; }
    }

    private sealed class PluginRegistryEntrySource
    {
        public string? Source { get; set; }

        public string? Path { get; set; }
    }

    private sealed class PluginRegistryEntryPolicy
    {
        public string? Installation { get; set; }

        public string? Authentication { get; set; }
    }
}
