using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Plugins;

internal sealed record RemoteBuiltInPlugin(
    PluginManifest Manifest,
    RemoteBuiltInPluginPackage Package,
    string CatalogPath);

internal sealed record RemoteBuiltInPluginPackage(
    string Kind,
    string Url,
    string Version,
    string Sha256);

internal static class RemoteBuiltInPluginCatalog
{
    public const string EnvironmentVariableName = "DOTCRAFT_BUILTIN_PLUGIN_CATALOGS";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<RemoteBuiltInPlugin> Discover(List<PluginDiagnostic> diagnostics)
    {
        var catalogs = ResolveEnvironmentCatalogs();
        if (catalogs.Count == 0)
            return [];

        var plugins = new List<RemoteBuiltInPlugin>();
        foreach (var catalogPath in catalogs)
        {
            if (!File.Exists(catalogPath))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "RemotePluginCatalogMissing",
                    $"Remote plugin catalog '{catalogPath}' does not exist.",
                    path: catalogPath));
                continue;
            }

            RemotePluginCatalogDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<RemotePluginCatalogDocument>(
                    File.ReadAllText(catalogPath),
                    JsonOptions);
            }
            catch (JsonException ex)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidRemotePluginCatalogJson",
                    $"Failed to parse remote plugin catalog JSON: {ex.Message}",
                    path: catalogPath));
                continue;
            }
            catch (IOException ex)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "RemotePluginCatalogReadFailed",
                    $"Failed to read remote plugin catalog: {ex.Message}",
                    path: catalogPath));
                continue;
            }

            if (document?.Plugins is not { Count: > 0 })
                continue;

            foreach (var entry in document.Plugins)
            {
                var remote = BuildRemotePlugin(catalogPath, entry, diagnostics);
                if (remote != null)
                    plugins.Add(remote);
            }
        }

        return plugins;
    }

    private static RemoteBuiltInPlugin? BuildRemotePlugin(
        string catalogPath,
        RemotePluginCatalogEntry entry,
        List<PluginDiagnostic> diagnostics)
    {
        var id = entry.Id?.Trim() ?? string.Empty;
        var displayName = entry.DisplayName?.Trim() ?? string.Empty;
        var package = entry.Package;

        if (!PluginManifestParser.IsValidPluginId(id))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidRemotePluginId",
                "Remote plugin catalog entry id is invalid.",
                id,
                path: catalogPath));
            return null;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "MissingRemotePluginDisplayName",
                "Remote plugin catalog entry displayName is required.",
                id,
                path: catalogPath));
            return null;
        }

        if (package == null
            || !string.Equals(package.Kind, "githubRelease", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(package.Url)
            || string.IsNullOrWhiteSpace(package.Version)
            || string.IsNullOrWhiteSpace(package.Sha256))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidRemotePluginPackage",
                "Remote plugin package must declare kind=githubRelease, url, version, and sha256.",
                id,
                path: catalogPath));
            return null;
        }

        if (!Uri.TryCreate(package.Url, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidRemotePluginPackageUrl",
                "Remote plugin package url must be an https://github.com/ release asset URL.",
                id,
                path: catalogPath));
            return null;
        }

        if (!IsHexSha256(package.Sha256))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidRemotePluginPackageSha256",
                "Remote plugin package sha256 must be a 64-character hex digest.",
                id,
                path: catalogPath));
            return null;
        }

        var packageKind = package.Kind!.Trim();
        var packageUrl = package.Url!.Trim();
        var packageVersion = package.Version!.Trim();
        var packageSha256 = package.Sha256!.Trim().ToUpperInvariant();

        var manifest = new PluginManifest
        {
            SchemaVersion = PluginManifestParser.SupportedSchemaVersion,
            Id = id,
            Version = NormalizeOptional(entry.Version) ?? packageVersion,
            DisplayName = displayName,
            Description = NormalizeOptional(entry.Description),
            Capabilities = (entry.Capabilities ?? [])
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Select(capability => capability.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Paths = new Dictionary<string, string>(),
            Interface = entry.Interface == null ? null : new PluginInterfaceMetadata
            {
                DisplayName = NormalizeOptional(entry.Interface.DisplayName),
                ShortDescription = NormalizeOptional(entry.Interface.ShortDescription),
                LongDescription = NormalizeOptional(entry.Interface.LongDescription),
                DeveloperName = NormalizeOptional(entry.Interface.DeveloperName),
                Category = NormalizeOptional(entry.Interface.Category),
                Capabilities = (entry.Interface.Capabilities ?? [])
                    .Where(capability => !string.IsNullOrWhiteSpace(capability))
                    .Select(capability => capability.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                DefaultPrompt = NormalizeOptional(entry.Interface.DefaultPrompt),
                BrandColor = NormalizeOptional(entry.Interface.BrandColor),
                WebsiteUrl = NormalizeOptional(entry.Interface.WebsiteUrl),
                PrivacyPolicyUrl = NormalizeOptional(entry.Interface.PrivacyPolicyUrl),
                TermsOfServiceUrl = NormalizeOptional(entry.Interface.TermsOfServiceUrl)
            },
            RootPath = string.Empty,
            ManifestPath = catalogPath
        };

        return new RemoteBuiltInPlugin(
            manifest,
            new RemoteBuiltInPluginPackage(
                packageKind,
                packageUrl,
                packageVersion,
                packageSha256),
            catalogPath);
    }

    private static IReadOnlyList<string> ResolveEnvironmentCatalogs()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .ToArray();
    }

    private static bool IsHexSha256(string value) =>
        value.Trim().Length == 64
        && value.Trim().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private sealed class RemotePluginCatalogDocument
    {
        public List<RemotePluginCatalogEntry> Plugins { get; set; } = [];
    }

    private sealed class RemotePluginCatalogEntry
    {
        public string? Id { get; set; }

        public string? Version { get; set; }

        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        public List<string> Capabilities { get; set; } = [];

        [JsonPropertyName("interface")]
        public RemotePluginInterface? Interface { get; set; }

        public RemotePluginPackage? Package { get; set; }
    }

    private sealed class RemotePluginPackage
    {
        public string? Kind { get; set; }

        public string? Url { get; set; }

        public string? Version { get; set; }

        public string? Sha256 { get; set; }
    }

    private sealed class RemotePluginInterface
    {
        public string? DisplayName { get; set; }

        public string? ShortDescription { get; set; }

        public string? LongDescription { get; set; }

        public string? DeveloperName { get; set; }

        public string? Category { get; set; }

        public List<string> Capabilities { get; set; } = [];

        public string? DefaultPrompt { get; set; }

        public string? BrandColor { get; set; }

        public string? WebsiteUrl { get; set; }

        public string? PrivacyPolicyUrl { get; set; }

        public string? TermsOfServiceUrl { get; set; }
    }
}
