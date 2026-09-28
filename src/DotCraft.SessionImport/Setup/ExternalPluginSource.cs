using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DotCraft.SessionImport;

internal sealed record ExternalPluginSource(string Id, string Name, string ManifestPath, string? LocalRoot,
    string? Repository = null, string? Ref = null, string? MarketplaceManifest = null, string? ErrorCode = null)
{
    public static IReadOnlyList<ExternalPluginSource> Discover(string source, string root)
    {
        var result = new List<ExternalPluginSource>();
        if (source == "cursor")
        {
            var markets = Path.Combine(root, "plugins", "marketplaces");
            if (!Directory.Exists(markets)) return result;
            foreach (var market in Directory.EnumerateDirectories(markets))
            {
                var manifest = Path.Combine(market, ".cursor-plugin", "marketplace.json");
                var document = SetupImportScanner.ReadJson(manifest);
                if (document["plugins"] is not JsonArray entries) continue;
                var marketName = document["name"]?.GetValue<string>() ?? Path.GetFileName(market);
                foreach (var entry in entries.OfType<JsonObject>())
                {
                    var name = entry["name"]?.GetValue<string>();
                    if (name == null) continue;
                    var cache = Path.Combine(root, "plugins", "cache", Path.GetFileName(market), Segment(name));
                    if (!Directory.Exists(cache)) continue;
                    result.Add(Local(source, name + "@" + marketName, ActiveRoot(cache), name));
                }
            }
            return result;
        }

        var settings = source == "codex" ? SetupImportScanner.ReadToml(Path.Combine(root, "config.toml")) : SetupImportScanner.ClaudeSettings(root);
        var enabled = settings[source == "codex" ? "plugins" : "enabledPlugins"] as JsonObject;
        if (enabled == null) return result;
        var installed = SetupImportScanner.ReadJson(Path.Combine(root, "plugins", "installed_plugins.json"))["plugins"] as JsonObject;
        var marketsDocument = SetupImportScanner.ReadJson(Path.Combine(root, "plugins", "known_marketplaces.json"));
        foreach (var (key, value) in enabled)
        {
            try
            {
                if (source == "codex" ? value?["enabled"]?.GetValue<bool>() == false : value?.GetValue<bool>() != true) continue;
                var parts = key.Split('@');
                if (parts.Length != 2) throw new NotSupportedException("import_plugin_identity_unsupported");
                var name = Segment(parts[0]);
                var marketplace = Segment(parts[1]);
                if (source == "codex")
                {
                    var cache = Path.Combine(root, "plugins", "cache", marketplace, name);
                    if (!Directory.Exists(cache)) throw new NotSupportedException("import_plugin_content_missing");
                    result.Add(Local(source, key, ActiveRoot(cache), name));
                    continue;
                }
                if (installed?[key] is JsonArray installs)
                {
                    var userInstall = installs.OfType<JsonObject>().FirstOrDefault(i => i["scope"]?.GetValue<string>() == "user");
                    if (userInstall?["installPath"]?.GetValue<string>() is { } local)
                    {
                        result.Add(Local(source, key, Path.GetFullPath(local), name));
                        continue;
                    }
                }
                var market = marketsDocument[marketplace] as JsonObject;
                if (market?["source"] is not JsonObject descriptor) throw new NotSupportedException("import_plugin_source_missing");
                var kind = descriptor["source"]?.GetValue<string>();
                if (kind is "directory" or "local")
                {
                    var directory = descriptor["path"]?.GetValue<string>() ?? throw new NotSupportedException("import_plugin_source_missing");
                    var manifest = Path.Combine(directory, ".claude-plugin", "marketplace.json");
                    result.Add(Local(source, key, ResolveEntry(directory, manifest, name), name));
                }
                else if (kind is "github" or "git")
                {
                    var repository = kind == "github" ? "https://github.com/" + descriptor["repo"]!.GetValue<string>() + ".git" : descriptor["url"]!.GetValue<string>();
                    result.Add(new ExternalPluginSource(PluginId(source, key), name, Path.Combine(root, "plugins", "known_marketplaces.json"), null,
                        repository, descriptor["ref"]?.GetValue<string>(), ".claude-plugin/marketplace.json"));
                }
                else throw new NotSupportedException("import_plugin_source_unsupported");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                var identity = source + ".unavailable." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
                result.Add(new ExternalPluginSource(identity, key, root, null,
                    ErrorCode: ex is NotSupportedException ? ex.Message : "import_plugin_source_invalid"));
            }
        }
        return result;
    }

    private static ExternalPluginSource Local(string source, string key, string root, string name) =>
        new(PluginId(source, key), name, Path.Combine(root, source switch { "claude-code" => ".claude-plugin", "cursor" => ".cursor-plugin", _ => ".codex-plugin" }, "plugin.json"), root);

    internal static string ResolveEntry(string root, string manifest, string name)
    {
        var entries = SetupImportScanner.ReadJson(manifest)["plugins"] as JsonArray;
        var entry = entries?.OfType<JsonObject>().SingleOrDefault(e => e["name"]?.GetValue<string>() == name)
            ?? throw new InvalidDataException("Plugin is not in the marketplace.");
        var relative = entry["source"] is JsonValue value ? value.GetValue<string>()
            : entry["source"]?["path"]?.GetValue<string>() ?? throw new NotSupportedException("import_plugin_source_unsupported");
        return Confined(root, relative);
    }

    internal static string Confined(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Plugin paths must be relative.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("Plugin path escapes its root.");
        return path;
    }

    private static string ActiveRoot(string cache)
    {
        var versions = Directory.GetDirectories(cache);
        return versions.FirstOrDefault(v => Path.GetFileName(v) == "1.0.0")
            ?? versions.OrderBy(v => Version.TryParse(Path.GetFileName(v), out var version) ? version : new Version())
                .ThenBy(v => Path.GetFileName(v), StringComparer.Ordinal).LastOrDefault()
            ?? throw new InvalidDataException("Plugin has no installed version.");
    }

    private static string PluginId(string source, string key) => Segment(source + "." + key.Replace('@', '.'));
    private static string Segment(string text) => Regex.IsMatch(text, @"^[A-Za-z0-9][A-Za-z0-9._-]*$") && !text.Contains("..")
        ? text : throw new InvalidDataException("Invalid plugin identity.");
}
