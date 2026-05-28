using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Lsp;

namespace DotCraft.Plugins;

/// <summary>
/// Loads LSP server declarations contributed by enabled plugins.
/// </summary>
public static class PluginLspServerLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new LspServerConfigListConverter() }
    };

    public static IReadOnlyList<LspServerConfig> LoadEnabledPluginServers(
        AppConfig config,
        string workspacePath,
        string botPath,
        out IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        var allDiagnostics = new List<PluginDiagnostic>();
        var discovery = new PluginDiscoveryService().Discover(config, workspacePath, botPath);
        allDiagnostics.AddRange(discovery.Diagnostics);

        var servers = new List<LspServerConfig>();
        foreach (var plugin in discovery.Plugins)
            servers.AddRange(LoadPluginServers(plugin, allDiagnostics));

        diagnostics = allDiagnostics;
        return servers;
    }

    public static IReadOnlyList<LspServerConfig> LoadPluginServers(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics)
    {
        var manifest = plugin.Manifest;
        var path = manifest.LspServersPath;
        if (string.IsNullOrWhiteSpace(path))
            return [];

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            root = doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginLspConfig",
                $"Failed to read plugin LSP config: {ex.Message}",
                manifest.Id,
                path: path));
            return [];
        }

        var serverRoot = TryGetObjectProperty(root, "lspServers", out var nested)
            ? nested
            : root;

        List<LspServerConfig>? parsed;
        try
        {
            parsed = serverRoot.Deserialize<List<LspServerConfig>>(JsonOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginLspConfig",
                $"Failed to parse plugin LSP config: {ex.Message}",
                manifest.Id,
                path: path));
            return [];
        }

        if (parsed is not { Count: > 0 })
            return [];

        var result = new List<LspServerConfig>();
        foreach (var server in parsed)
        {
            var declaredName = server.Name.Trim();
            if (string.IsNullOrWhiteSpace(declaredName))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "InvalidPluginLspServer",
                    "Plugin LSP server declaration is missing a name.",
                    manifest.Id,
                    path: path));
                continue;
            }

            if (string.IsNullOrWhiteSpace(server.Command))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "InvalidPluginLspServer",
                    $"Plugin LSP server '{declaredName}' is missing a command.",
                    manifest.Id,
                    path: path));
                continue;
            }

            if (server.ExtensionToLanguage.Count == 0)
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "InvalidPluginLspServer",
                    $"Plugin LSP server '{declaredName}' is missing extensionToLanguage mappings.",
                    manifest.Id,
                    path: path));
                continue;
            }

            if (!server.NormalizedTransport.Equals("stdio", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "UnsupportedPluginLspTransport",
                    $"Plugin LSP server '{declaredName}' uses unsupported transport '{server.Transport}'.",
                    manifest.Id,
                    path: path));
                continue;
            }

            var clone = server.Clone();
            var pluginDataPath = GetPluginDataPath(manifest.Id);
            clone.Name = $"{manifest.Id}:{declaredName}";
            clone.Transport = clone.NormalizedTransport;
            clone.Origin = LspServerOrigin.Plugin(
                manifest.Id,
                manifest.Interface?.DisplayName ?? manifest.DisplayName,
                declaredName);
            clone.Command = NormalizeExpandedCommand(
                ExpandPluginVariables(clone.Command, manifest.RootPath, pluginDataPath),
                manifest.RootPath);
            clone.Arguments = clone.Arguments
                .Select(arg => ExpandPluginVariables(arg, manifest.RootPath, pluginDataPath))
                .ToList();
            ExpandPluginEnvironment(clone, manifest.RootPath, pluginDataPath);
            if (!TryResolvePluginRelativeCommand(clone, manifest, path, diagnostics))
                continue;
            if (!string.IsNullOrWhiteSpace(clone.WorkspaceFolder) && !Path.IsPathRooted(clone.WorkspaceFolder))
                clone.WorkspaceFolder = Path.GetFullPath(Path.Combine(manifest.RootPath, clone.WorkspaceFolder));
            InjectPluginEnvironment(clone, manifest.RootPath, pluginDataPath);
            result.Add(clone);
        }

        return result;
    }

    private static void ExpandPluginEnvironment(LspServerConfig server, string pluginRoot, string pluginDataPath)
    {
        foreach (var key in server.EnvironmentVariables.Keys.ToList())
            server.EnvironmentVariables[key] = ExpandPluginVariables(
                server.EnvironmentVariables[key],
                pluginRoot,
                pluginDataPath);
    }

    private static bool TryResolvePluginRelativeCommand(
        LspServerConfig server,
        PluginManifest manifest,
        string lspConfigPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (!IsPluginRelativePath(server.Command))
            return true;

        var relativePath = server.Command[2..];
        var resolved = Path.GetFullPath(Path.Combine(manifest.RootPath, relativePath));
        if (!IsPathInsideDirectory(resolved, manifest.RootPath))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "InvalidPluginLspServer",
                $"Plugin LSP server '{server.Origin.DeclaredName}' command escapes the plugin root.",
                manifest.Id,
                path: lspConfigPath));
            return false;
        }

        server.Command = ProbeWindowsExecutableSuffix(resolved);
        return true;
    }

    private static string ExpandPluginVariables(string value, string pluginRoot, string pluginDataPath)
    {
        var separator = Path.DirectorySeparatorChar.ToString();
        return value
            .Replace("${DOTCRAFT_PLUGIN_ROOT}/", pluginRoot + separator, StringComparison.Ordinal)
            .Replace(@"${DOTCRAFT_PLUGIN_ROOT}\", pluginRoot + separator, StringComparison.Ordinal)
            .Replace("${DOTCRAFT_PLUGIN_DATA}/", pluginDataPath + separator, StringComparison.Ordinal)
            .Replace(@"${DOTCRAFT_PLUGIN_DATA}\", pluginDataPath + separator, StringComparison.Ordinal)
            .Replace("${DOTCRAFT_PLUGIN_ROOT}", pluginRoot, StringComparison.Ordinal)
            .Replace("${DOTCRAFT_PLUGIN_DATA}", pluginDataPath, StringComparison.Ordinal);
    }

    private static string NormalizeExpandedCommand(string command, string pluginRoot)
    {
        if (!Path.IsPathRooted(command))
            return command;

        var normalized = Path.GetFullPath(command);
        return IsPathInsideDirectory(normalized, pluginRoot)
            ? ProbeWindowsExecutableSuffix(normalized)
            : normalized;
    }

    private static bool IsPluginRelativePath(string path) =>
        path.StartsWith("./", StringComparison.Ordinal) || path.StartsWith(@".\", StringComparison.Ordinal);

    private static string ProbeWindowsExecutableSuffix(string path)
    {
        if (!OperatingSystem.IsWindows() || File.Exists(path) || Path.HasExtension(path))
            return path;

        foreach (var suffix in new[] { ".exe", ".cmd", ".bat" })
        {
            var candidate = path + suffix;
            if (File.Exists(candidate))
                return candidate;
        }

        return path;
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return normalizedPath.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void InjectPluginEnvironment(LspServerConfig server, string pluginRoot, string pluginDataPath)
    {
        server.EnvironmentVariables["DOTCRAFT_PLUGIN_ROOT"] = pluginRoot;
        server.EnvironmentVariables["DOTCRAFT_PLUGIN_DATA"] = pluginDataPath;
    }

    private static string GetPluginDataPath(string pluginId)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".craft");

        return Path.Combine(localData, "DotCraft", "plugins", pluginId, "data");
    }

    private static bool TryGetObjectProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name) || string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
