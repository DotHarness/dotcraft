using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Skills;

namespace DotCraft.AppBinding;

/// <summary>
/// Loads and validates plugin app descriptor contributions for a workspace.
/// </summary>
public static partial class AppBindingCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppCatalogSnapshot Discover(
        AppConfig config,
        string workspacePath,
        string workspaceCraftPath,
        SkillsLoader? skillsLoader = null,
        IReadOnlyList<string>? builtInPluginSourceRoots = null)
    {
        var discovery = PluginRuntimeConfigurator.ConfigureSkillsLoader(
            skillsLoader,
            config,
            workspacePath,
            workspaceCraftPath,
            PluginDiagnosticsStore.Shared,
            builtInPluginSourceRoots);

        var diagnostics = discovery.Diagnostics.ToList();
        var candidates = new List<AppCatalogEntry>();
        foreach (var plugin in discovery.Plugins)
        {
            var appDiagnostics = new List<PluginDiagnostic>();
            foreach (var descriptor in LoadPluginAppDescriptors(plugin, appDiagnostics))
                candidates.Add(new AppCatalogEntry(descriptor, plugin, []));
            diagnostics.AddRange(appDiagnostics);
        }

        var accepted = RejectCollisions(candidates, diagnostics).ToList();
        return new AppCatalogSnapshot(accepted, diagnostics) { Plugins = discovery.Plugins };
    }

    /// <summary>
    /// Loads and validates App Binding descriptors contributed by one plugin.
    /// </summary>
    public static IReadOnlyList<AppDescriptor> LoadPluginAppDescriptors(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(plugin.Manifest.AppsPath))
            return [];

        var accepted = new List<AppDescriptor>();
        foreach (var descriptor in LoadContribution(plugin, diagnostics))
        {
            var descriptorDiagnostics = ValidateDescriptor(plugin, descriptor);
            diagnostics.AddRange(descriptorDiagnostics);
            if (descriptorDiagnostics.Any(d => d.Severity == PluginDiagnosticSeverity.Error))
                continue;

            accepted.Add(CloneDescriptorWithResolvedPaths(plugin, descriptor));
        }

        return accepted;
    }

    private static IReadOnlyList<AppDescriptor> LoadContribution(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics)
    {
        var path = plugin.Manifest.AppsPath;
        if (string.IsNullOrWhiteSpace(path))
            return [];

        if (!File.Exists(path))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginAppsMissing",
                $"Plugin app contribution file '{path}' does not exist.",
                plugin.Manifest.Id,
                path: path));
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            using var raw = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            if (ContainsV1ExecutionFields(raw.RootElement))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "AppBindingUpgradeRequired",
                    "App Binding descriptors must not declare toolNamespace, scopes, toolCatalog, or dynamicToolCatalog.",
                    plugin.Manifest.Id,
                    path: path));
                return [];
            }

            var document = JsonSerializer.Deserialize<AppDescriptorDocument>(
                json,
                JsonOptions);
            if (document?.Apps is not { Count: > 0 })
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "PluginAppsEmpty",
                    "Plugin app contribution file must contain at least one app descriptor.",
                    plugin.Manifest.Id,
                    path: path));
                return [];
            }

            return document.Apps;
        }
        catch (JsonException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginAppsJson",
                $"Failed to parse plugin app contribution JSON: {ex.Message}",
                plugin.Manifest.Id,
                path: path));
            return [];
        }
        catch (IOException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginAppsReadFailed",
                $"Failed to read plugin app contribution file: {ex.Message}",
                plugin.Manifest.Id,
                path: path));
            return [];
        }
    }

    private static bool ContainsV1ExecutionFields(JsonElement root)
    {
        if (!root.TryGetProperty("apps", out var apps) || apps.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var app in apps.EnumerateArray())
        {
            if (app.ValueKind != JsonValueKind.Object) continue;
            foreach (var property in app.EnumerateObject())
            {
                if (string.Equals(property.Name, "toolNamespace", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "scopes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "toolCatalog", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "dynamicToolCatalog", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<PluginDiagnostic> ValidateDescriptor(
        DiscoveredPlugin plugin,
        AppDescriptor descriptor)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var path = plugin.Manifest.AppsPath;

        if (!IsValidAppId(descriptor.AppId))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidAppId",
                "App descriptor appId must be lowercase reverse-DNS with at least three labels.",
                plugin.Manifest.Id,
                path: path));
        }

        if (string.IsNullOrWhiteSpace(descriptor.DisplayName)
            || string.IsNullOrWhiteSpace(descriptor.DeveloperName)
            || string.IsNullOrWhiteSpace(descriptor.Description))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidAppDisplayMetadata",
                "App descriptor displayName, developerName, and description are required.",
                plugin.Manifest.Id,
                path: path));
        }

        if (descriptor.Connection?.HandoffModes is not { Count: > 0 })
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "MissingAppHandoffModes",
                "App descriptor connection.handoffModes must not be empty.",
                plugin.Manifest.Id,
                path: path));
        }
        else
        {
            foreach (var handoff in descriptor.Connection.HandoffModes)
                ValidateHandoff(plugin, path, handoff, diagnostics);
        }

        var usesCustomProtocol = descriptor.Connection?.HandoffModes
            .Any(mode => string.Equals(mode.Mode, "customProtocol", StringComparison.Ordinal))
            == true;
        if (descriptor.NativeApplication == null
            || string.IsNullOrWhiteSpace(descriptor.NativeApplication.DisplayName)
            || (usesCustomProtocol && string.IsNullOrWhiteSpace(descriptor.NativeApplication.Protocol)))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidAppNativeApplication",
                usesCustomProtocol
                    ? "App descriptor nativeApplication.displayName and nativeApplication.protocol are required for customProtocol handoffs."
                    : "App descriptor nativeApplication.displayName is required.",
                plugin.Manifest.Id,
                path: path));
        }

        return diagnostics;
    }

    private static void ValidateHandoff(
        DiscoveredPlugin plugin,
        string? path,
        AppHandoffModeDescriptor handoff,
        List<PluginDiagnostic> diagnostics)
    {
        if (handoff.Mode is not ("url" or "customProtocol"))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidAppHandoffMode",
                $"Unsupported app handoff mode '{handoff.Mode}'.",
                plugin.Manifest.Id,
                path: path));
            return;
        }

        if (handoff.Mode is "url" or "customProtocol")
        {
            if (string.IsNullOrWhiteSpace(handoff.UriTemplate))
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidAppHandoffUri",
                    "URL and customProtocol app handoff modes require uriTemplate.",
                    plugin.Manifest.Id,
                path: path));
            }
        }
    }

    private static void ValidateToolCatalogEntry(
        DiscoveredPlugin plugin,
        string? path,
        AppDescriptor descriptor,
        HashSet<string> scopeIds,
        HashSet<string> toolNames,
        AppToolCatalogEntry tool,
        List<PluginDiagnostic> diagnostics)
    {
        if (!PluginManifestParser.IsValidFunctionName(tool.Name)
            || string.IsNullOrWhiteSpace(tool.Scope)
            || !AppBindingRisks.IsKnown(tool.Risk)
            || !AppBindingExposures.IsKnown(tool.DefaultExposure))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidAppToolCatalogEntry",
                "Each app tool catalog entry requires a valid name, scope, risk, and defaultExposure.",
                plugin.Manifest.Id,
                tool.Name,
                path));
            return;
        }

        if (!toolNames.Add(tool.Name))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "DuplicateAppTool",
                $"App tool '{tool.Name}' is declared more than once.",
                plugin.Manifest.Id,
                tool.Name,
                path));
        }

        if (!scopeIds.Contains(tool.Scope))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "UnknownAppToolScope",
                $"App tool '{tool.Name}' references unknown scope '{tool.Scope}'.",
                plugin.Manifest.Id,
                tool.Name,
                path));
            return;
        }

        var scope = descriptor.Scopes.First(s => string.Equals(s.Id, tool.Scope, StringComparison.Ordinal));
        if (AppBindingRisks.Rank(tool.Risk) < AppBindingRisks.Rank(scope.Risk))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidAppToolRisk",
                $"App tool '{tool.Name}' risk must not be lower than scope '{tool.Scope}' risk.",
                plugin.Manifest.Id,
                tool.Name,
                path));
        }
    }

    private static IReadOnlyList<AppCatalogEntry> RejectCollisions(
        IReadOnlyList<AppCatalogEntry> candidates,
        List<PluginDiagnostic> diagnostics)
    {
        var duplicateAppIds = candidates
            .GroupBy(entry => entry.Descriptor.AppId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (duplicateAppIds.Count == 0)
            return candidates;

        var accepted = new List<AppCatalogEntry>();
        foreach (var entry in candidates)
        {
            var rejected = false;
            if (duplicateAppIds.Contains(entry.Descriptor.AppId))
            {
                rejected = true;
                diagnostics.Add(PluginDiagnostic.Error(
                    "DuplicateAppId",
                    $"App descriptor '{entry.Descriptor.AppId}' was rejected because another plugin declares the same appId.",
                    entry.Plugin.Manifest.Id,
                    path: entry.Plugin.Manifest.AppsPath));
            }

            if (!rejected)
                accepted.Add(entry);
        }

        return accepted;
    }

    private static AppDescriptor CloneDescriptorWithResolvedPaths(
        DiscoveredPlugin plugin,
        AppDescriptor source)
    {
        var clone = JsonSerializer.Deserialize<AppDescriptor>(
            JsonSerializer.Serialize(source, JsonOptions),
            JsonOptions) ?? new AppDescriptor();
        if (!string.IsNullOrWhiteSpace(clone.Icon))
            clone.Icon = ResolveOptionalPluginPath(plugin.Manifest.RootPath, clone.Icon);

        return clone;
    }

    private static string ResolveOptionalPluginPath(string pluginRoot, string value)
    {
        if (!value.StartsWith("./", StringComparison.Ordinal))
            return value;

        var relative = value[2..];
        if (relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == ".."))
            return value;

        var candidate = Path.GetFullPath(Path.Combine(pluginRoot, relative));
        var root = Path.GetFullPath(pluginRoot);
        var back = Path.GetRelativePath(root, candidate);
        return Path.IsPathRooted(back) || back.StartsWith("..", StringComparison.Ordinal)
            ? value
            : candidate;
    }

    public static bool IsValidAppId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && AppIdRegex().IsMatch(value);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?){2,}$")]
    private static partial Regex AppIdRegex();
}
