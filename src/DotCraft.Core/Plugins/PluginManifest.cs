using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DotCraft.Hooks;

namespace DotCraft.Plugins;

/// <summary>
/// Parsed DotCraft plugin manifest.
/// </summary>
public sealed record PluginManifest
{
    public required int SchemaVersion { get; init; }

    public required string Id { get; init; }

    public string? Version { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyDictionary<string, string> Paths { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("interface")]
    public PluginInterfaceMetadata? Interface { get; init; }

    public string? SkillsPath { get; init; }

    public string? McpServersPath { get; init; }

    public string? LspServersPath { get; init; }

    public string? AppsPath { get; init; }

    public string? DesktopExtensionsPath { get; init; }

    public string? WorkflowsPath { get; init; }

    public PluginManifestHooks? Hooks { get; init; }

    public required string RootPath { get; init; }

    public required string ManifestPath { get; init; }
}

public sealed record PluginInterfaceMetadata
{
    public string? DisplayName { get; init; }

    public string? ShortDescription { get; init; }

    public string? LongDescription { get; init; }

    public string? DeveloperName { get; init; }

    public string? Category { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public string? DefaultPrompt { get; init; }

    public string? BrandColor { get; init; }

    public string? ComposerIcon { get; init; }

    public string? Logo { get; init; }

    public string? WebsiteUrl { get; init; }

    public string? PrivacyPolicyUrl { get; init; }

    public string? TermsOfServiceUrl { get; init; }
}

/// <summary>
/// Hook declarations contributed by a plugin manifest.
/// </summary>
public sealed record PluginManifestHooks
{
    public IReadOnlyList<string> Paths { get; init; } = [];

    public IReadOnlyList<HooksFileConfig> Inline { get; init; } = [];

    public bool HasAny => Paths.Count > 0 || Inline.Count > 0;
}

/// <summary>
/// Result of parsing one DotCraft plugin manifest.
/// </summary>
public sealed record PluginManifestParseResult(
    PluginManifest? Manifest,
    IReadOnlyList<PluginDiagnostic> Diagnostics);

/// <summary>
/// Parser for <c>.craft-plugin/plugin.json</c>.
/// </summary>
public static partial class PluginManifestParser
{
    public const int SupportedSchemaVersion = 1;
    public const string ManifestRelativePath = ".craft-plugin/plugin.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Loads and validates a plugin manifest from a plugin root.
    /// </summary>
    public static PluginManifestParseResult Load(string pluginRoot)
    {
        var manifestPath = Path.Combine(pluginRoot, ".craft-plugin", "plugin.json");
        var diagnostics = new List<PluginDiagnostic>();
        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "PluginManifestMissing",
                "Plugin manifest is missing.",
                path: manifestPath));
            return new PluginManifestParseResult(null, diagnostics);
        }

        RawPluginManifest? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawPluginManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginManifestJson",
                $"Failed to parse plugin manifest JSON: {ex.Message}",
                path: manifestPath));
            return new PluginManifestParseResult(null, diagnostics);
        }
        catch (IOException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginManifestReadFailed",
                $"Failed to read plugin manifest: {ex.Message}",
                path: manifestPath));
            return new PluginManifestParseResult(null, diagnostics);
        }

        if (raw == null)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginManifest",
                "Plugin manifest is empty.",
                path: manifestPath));
            return new PluginManifestParseResult(null, diagnostics);
        }

        if (raw.SchemaVersion != SupportedSchemaVersion)
            diagnostics.Add(PluginDiagnostic.Error(
                "UnsupportedPluginManifestVersion",
                $"Unsupported plugin manifest schemaVersion '{raw.SchemaVersion}'.",
                raw.Id,
                path: manifestPath));

        if (!IsValidPluginId(raw.Id))
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginId",
                "Plugin id is required and may contain only ASCII letters, digits, '.', '_', '-', or ':'.",
                raw.Id,
                path: manifestPath));

        if (string.IsNullOrWhiteSpace(raw.DisplayName))
            diagnostics.Add(PluginDiagnostic.Error(
                "MissingPluginDisplayName",
                "Plugin displayName is required.",
                raw.Id,
                path: manifestPath));

        var resolvedPaths = ResolvePaths(pluginRoot, raw.Paths, raw.Id, manifestPath, diagnostics);
        var skillsPath = ResolveOptionalManifestPath(
            pluginRoot,
            raw.Skills,
            "skills",
            raw.Id,
            manifestPath,
            diagnostics);
        var mcpServersPath = ResolveOptionalMcpServersPath(
            pluginRoot,
            raw.McpServers,
            raw.Id,
            manifestPath,
            diagnostics);
        var lspServersPath = ResolveOptionalLspServersPath(
            pluginRoot,
            raw.LspServers,
            raw.Id,
            manifestPath,
            diagnostics);
        var appsPath = ResolveOptionalManifestPath(
            pluginRoot,
            raw.Apps,
            "apps",
            raw.Id,
            manifestPath,
            diagnostics);
        var desktopExtensionsPath = ResolveOptionalManifestPath(
            pluginRoot,
            raw.DesktopExtensions,
            "desktopExtensions",
            raw.Id,
            manifestPath,
            diagnostics);
        var workflowsPath = ResolveOptionalManifestPath(
            pluginRoot,
            raw.Workflows ?? (Directory.Exists(Path.Combine(pluginRoot, "workflows")) ? "./workflows" : null),
            "workflows",
            raw.Id,
            manifestPath,
            diagnostics);
        var hooks = ParseManifestHooks(pluginRoot, raw.Hooks, raw.Id, manifestPath, diagnostics);
        var interfaceMetadata = ParseInterface(
            pluginRoot,
            raw.Interface,
            raw.Id,
            manifestPath,
            diagnostics);
        AddUnsupportedNativeToolsDiagnostics(raw, manifestPath, diagnostics);
        if (skillsPath == null
            && mcpServersPath == null
            && lspServersPath == null
            && appsPath == null
            && desktopExtensionsPath == null
            && workflowsPath == null
            && hooks?.HasAny != true
            && interfaceMetadata == null)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "MissingPluginCapabilities",
                "Plugin manifest must declare skills, mcpServers, lspServers, apps, desktopExtensions, workflows, hooks, or interface metadata.",
                raw.Id,
                path: manifestPath));
        }

        if (diagnostics.Any(d => d.Severity == PluginDiagnosticSeverity.Error))
            return new PluginManifestParseResult(null, diagnostics);

        var manifest = new PluginManifest
        {
            SchemaVersion = raw.SchemaVersion,
            Id = raw.Id!.Trim(),
            Version = NormalizeOptional(raw.Version),
            DisplayName = raw.DisplayName!.Trim(),
            Description = NormalizeOptional(raw.Description),
            Capabilities = raw.Capabilities
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Select(capability => capability.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Paths = resolvedPaths,
            Interface = interfaceMetadata,
            SkillsPath = skillsPath,
            McpServersPath = mcpServersPath,
            LspServersPath = lspServersPath,
            AppsPath = appsPath,
            DesktopExtensionsPath = desktopExtensionsPath,
            WorkflowsPath = workflowsPath,
            Hooks = hooks,
            RootPath = Path.GetFullPath(pluginRoot),
            ManifestPath = Path.GetFullPath(manifestPath)
        };

        return new PluginManifestParseResult(manifest, diagnostics);
    }

    /// <summary>
    /// Returns whether the directory contains a DotCraft plugin manifest.
    /// </summary>
    public static bool IsValidPluginRoot(string path) =>
        File.Exists(Path.Combine(path, ".craft-plugin", "plugin.json"));

    /// <summary>
    /// Returns whether a string is a valid plugin id.
    /// </summary>
    public static bool IsValidPluginId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && PluginIdRegex().IsMatch(value);

    /// <summary>
    /// Returns whether a string is a valid model-visible function name.
    /// </summary>
    public static bool IsValidFunctionName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && FunctionNameRegex().IsMatch(value);

    private static void AddUnsupportedNativeToolsDiagnostics(
        RawPluginManifest raw,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        var unsupportedFields = new List<string>();
        if (HasDeclaredValue(raw.Tools))
            unsupportedFields.Add("tools");
        if (HasDeclaredValue(raw.Functions))
            unsupportedFields.Add("functions");
        if (HasDeclaredValue(raw.Processes))
            unsupportedFields.Add("processes");

        if (unsupportedFields.Count == 0)
            return;

        diagnostics.Add(PluginDiagnostic.Warning(
            "UnsupportedPluginNativeTools",
            "Plugin manifest native tool fields are no longer supported and will be ignored: "
            + string.Join(", ", unsupportedFields)
            + ". Use plugin-bundled MCP servers for reusable tools or AppServer runtime Dynamic Tools for thread-scoped callbacks.",
            raw.Id,
            path: manifestPath));
    }

    private static bool HasDeclaredValue(JsonNode? node)
        => node switch
        {
            JsonArray array => array.Count > 0,
            JsonObject obj => obj.Count > 0,
            JsonValue => true,
            _ => false
        };

    private static string? ResolveOptionalMcpServersPath(
        string pluginRoot,
        string? relativePath,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
            return ResolveOptionalManifestPath(pluginRoot, relativePath, "mcpServers", pluginId, manifestPath, diagnostics);

        var defaultPath = Path.Combine(pluginRoot, ".mcp.json");
        return File.Exists(defaultPath) ? Path.GetFullPath(defaultPath) : null;
    }

    private static string? ResolveOptionalLspServersPath(
        string pluginRoot,
        string? relativePath,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
            return ResolveOptionalManifestPath(pluginRoot, relativePath, "lspServers", pluginId, manifestPath, diagnostics);

        var defaultPath = Path.Combine(pluginRoot, ".lsp.json");
        return File.Exists(defaultPath) ? Path.GetFullPath(defaultPath) : null;
    }

    private static IReadOnlyDictionary<string, string> ResolvePaths(
        string pluginRoot,
        Dictionary<string, string>? paths,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (paths == null)
            return resolved;

        foreach (var (name, value) in paths)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var normalized = ResolveManifestPath(pluginRoot, value, out var error);
            if (normalized == null)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidPluginManifestPath",
                    $"Manifest path '{name}' is invalid: {error}",
                    pluginId,
                    path: manifestPath));
                continue;
            }

            resolved[name] = normalized;
        }

        return resolved;
    }

    private static string? ResolveOptionalManifestPath(
        string pluginRoot,
        string? value,
        string fieldName,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = ResolveManifestPath(pluginRoot, value, out var error);
        if (normalized != null)
            return normalized;

        diagnostics.Add(PluginDiagnostic.Error(
            "InvalidPluginManifestPath",
            $"Manifest path '{fieldName}' is invalid: {error}",
            pluginId,
            path: manifestPath));
        return null;
    }

    private static PluginManifestHooks? ParseManifestHooks(
        string pluginRoot,
        JsonNode? rawHooks,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (rawHooks == null)
        {
            var defaultPath = Path.Combine(pluginRoot, "hooks", "hooks.json");
            if (File.Exists(defaultPath))
                return new PluginManifestHooks { Paths = [Path.GetFullPath(defaultPath)] };

            var topLevelPath = Path.Combine(pluginRoot, "hooks.json");
            return File.Exists(topLevelPath)
                ? new PluginManifestHooks { Paths = [Path.GetFullPath(topLevelPath)] }
                : null;
        }

        var paths = new List<string>();
        var inline = new List<HooksFileConfig>();
        switch (rawHooks)
        {
            case JsonValue value when value.TryGetValue<string>(out var path):
                AddHookPath(paths, pluginRoot, path, "hooks", pluginId, manifestPath, diagnostics);
                break;

            case JsonArray array:
                ParseHookArray(paths, inline, pluginRoot, array, pluginId, manifestPath, diagnostics);
                break;

            case JsonObject obj:
                AddInlineHook(inline, obj, pluginId, manifestPath, diagnostics);
                break;

            default:
                diagnostics.Add(PluginDiagnostic.Warning(
                    "InvalidPluginHooks",
                    "Plugin hooks must be a path string, path array, object, or object array.",
                    pluginId,
                    path: manifestPath));
                break;
        }

        return paths.Count == 0 && inline.Count == 0
            ? null
            : new PluginManifestHooks
            {
                Paths = paths,
                Inline = inline
            };
    }

    private static void ParseHookArray(
        List<string> paths,
        List<HooksFileConfig> inline,
        string pluginRoot,
        JsonArray array,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        foreach (var item in array)
        {
            switch (item)
            {
                case JsonValue value when value.TryGetValue<string>(out var path):
                    AddHookPath(paths, pluginRoot, path, "hooks", pluginId, manifestPath, diagnostics);
                    break;

                case JsonObject obj:
                    AddInlineHook(inline, obj, pluginId, manifestPath, diagnostics);
                    break;

                case null:
                    break;

                default:
                    diagnostics.Add(PluginDiagnostic.Warning(
                        "InvalidPluginHooks",
                        "Plugin hooks array entries must be path strings or hook objects.",
                        pluginId,
                        path: manifestPath));
                    break;
            }
        }
    }

    private static void AddHookPath(
        List<string> paths,
        string pluginRoot,
        string? value,
        string fieldName,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        var path = ResolveOptionalManifestPath(pluginRoot, value, fieldName, pluginId, manifestPath, diagnostics);
        if (!string.IsNullOrWhiteSpace(path))
            paths.Add(path);
    }

    private static void AddInlineHook(
        List<HooksFileConfig> inline,
        JsonObject obj,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        try
        {
            var parsed = obj.Deserialize<HooksFileConfig>(JsonOptions);
            if (parsed?.Hooks.Count > 0)
                inline.Add(NormalizeHookConfig(parsed));
        }
        catch (JsonException ex)
        {
            diagnostics.Add(PluginDiagnostic.Warning(
                "InvalidPluginHooks",
                $"Failed to parse inline plugin hooks: {ex.Message}",
                pluginId,
                path: manifestPath));
        }
    }

    private static HooksFileConfig NormalizeHookConfig(HooksFileConfig config)
    {
        var normalized = new HooksFileConfig();
        foreach (var (eventName, groups) in config.Hooks)
            normalized.Hooks[eventName] = groups ?? [];
        return normalized;
    }

    private static PluginInterfaceMetadata? ParseInterface(
        string pluginRoot,
        RawPluginInterface? raw,
        string? pluginId,
        string manifestPath,
        List<PluginDiagnostic> diagnostics)
    {
        if (raw == null)
            return null;

        var composerIcon = ResolveOptionalManifestPath(
            pluginRoot,
            raw.ComposerIcon,
            "interface.composerIcon",
            pluginId,
            manifestPath,
            diagnostics);
        var logo = ResolveOptionalManifestPath(
            pluginRoot,
            raw.Logo,
            "interface.logo",
            pluginId,
            manifestPath,
            diagnostics);

        return new PluginInterfaceMetadata
        {
            DisplayName = NormalizeOptional(raw.DisplayName),
            ShortDescription = NormalizeOptional(raw.ShortDescription),
            LongDescription = NormalizeOptional(raw.LongDescription),
            DeveloperName = NormalizeOptional(raw.DeveloperName),
            Category = NormalizeOptional(raw.Category),
            Capabilities = raw.Capabilities
                .Where(capability => !string.IsNullOrWhiteSpace(capability))
                .Select(capability => capability.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DefaultPrompt = NormalizeOptional(raw.DefaultPrompt),
            BrandColor = NormalizeOptional(raw.BrandColor),
            ComposerIcon = composerIcon,
            Logo = logo,
            WebsiteUrl = NormalizeOptional(raw.WebsiteUrl),
            PrivacyPolicyUrl = NormalizeOptional(raw.PrivacyPolicyUrl),
            TermsOfServiceUrl = NormalizeOptional(raw.TermsOfServiceUrl)
        };
    }

    private static string? ResolveManifestPath(string pluginRoot, string value, out string error)
    {
        error = string.Empty;
        if (!value.StartsWith("./", StringComparison.Ordinal))
        {
            error = "path must start with './'";
            return null;
        }

        var relative = value[2..];
        if (string.IsNullOrWhiteSpace(relative))
            return Path.GetFullPath(pluginRoot);

        if (relative
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == ".."))
        {
            error = "path must not contain '..'";
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(pluginRoot, relative));
        var root = Path.GetFullPath(pluginRoot);
        var relativeBack = Path.GetRelativePath(root, candidate);
        if (relativeBack.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relativeBack))
        {
            error = "path must stay within the plugin root";
            return null;
        }

        return candidate;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$")]
    private static partial Regex PluginIdRegex();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex FunctionNameRegex();

    private sealed class RawPluginManifest
    {
        public int SchemaVersion { get; set; }

        public string? Id { get; set; }

        public string? Version { get; set; }

        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        public List<string> Capabilities { get; set; } = [];

        public Dictionary<string, string>? Paths { get; set; }

        public string? Skills { get; set; }

        public string? McpServers { get; set; }

        public string? LspServers { get; set; }

        public string? Apps { get; set; }

        public string? DesktopExtensions { get; set; }

        public string? Workflows { get; set; }

        public JsonNode? Hooks { get; set; }

        [JsonPropertyName("interface")]
        public RawPluginInterface? Interface { get; set; }

        public JsonNode? Functions { get; set; }

        public JsonNode? Tools { get; set; }

        public JsonNode? Processes { get; set; }
    }

    private sealed class RawPluginInterface
    {
        public string? DisplayName { get; set; }

        public string? ShortDescription { get; set; }

        public string? LongDescription { get; set; }

        public string? DeveloperName { get; set; }

        public string? Category { get; set; }

        public List<string> Capabilities { get; set; } = [];

        public string? DefaultPrompt { get; set; }

        public string? BrandColor { get; set; }

        public string? ComposerIcon { get; set; }

        public string? Logo { get; set; }

        public string? WebsiteUrl { get; set; }

        public string? PrivacyPolicyUrl { get; set; }

        public string? TermsOfServiceUrl { get; set; }
    }

}
