using System.Text.Json;

namespace DotCraft.Plugins;

/// <summary>
/// Loads and validates trusted Desktop extension descriptors contributed by plugins.
/// </summary>
public static class PluginDesktopExtensionCatalog
{
    private static readonly HashSet<string> KnownSurfaceTypes = new(StringComparer.Ordinal)
    {
        "mainView",
        "pluginDetail",
        "detailPanel",
        "composerAction",
        "conversationRenderer",
        "settingsPanel"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Loads accepted Desktop extension descriptors for one discovered plugin.
    /// </summary>
    public static IReadOnlyList<PluginDesktopExtensionDescriptor> LoadPluginDesktopExtensions(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(plugin.Manifest.DesktopExtensionsPath))
            return [];

        var document = LoadContribution(plugin, diagnostics);
        if (document?.Extensions is not { Count: > 0 })
            return [];

        var accepted = new List<PluginDesktopExtensionDescriptor>();
        var extensionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in document.Extensions)
        {
            NormalizeDescriptorCollections(descriptor);
            var descriptorDiagnostics = ValidateDescriptor(plugin, descriptor, extensionIds);
            diagnostics.AddRange(descriptorDiagnostics);
            if (descriptorDiagnostics.Any(d => d.Severity == PluginDiagnosticSeverity.Error))
                continue;

            accepted.Add(CloneDescriptorWithResolvedPaths(plugin, descriptor));
        }

        return accepted;
    }

    private static PluginDesktopExtensionDocument? LoadContribution(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics)
    {
        var path = plugin.Manifest.DesktopExtensionsPath;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (!File.Exists(path))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginDesktopExtensionsMissing",
                $"Plugin Desktop extension contribution file '{path}' does not exist.",
                plugin.Manifest.Id,
                path: path));
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<PluginDesktopExtensionDocument>(
                File.ReadAllText(path),
                JsonOptions);
            if (document?.Extensions is not { Count: > 0 })
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "PluginDesktopExtensionsEmpty",
                    "Plugin Desktop extension contribution file must contain at least one extension descriptor.",
                    plugin.Manifest.Id,
                    path: path));
                return null;
            }

            return document;
        }
        catch (JsonException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginDesktopExtensionsJson",
                $"Failed to parse plugin Desktop extension contribution JSON: {ex.Message}",
                plugin.Manifest.Id,
                path: path));
            return null;
        }
        catch (IOException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "PluginDesktopExtensionsReadFailed",
                $"Failed to read plugin Desktop extension contribution file: {ex.Message}",
                plugin.Manifest.Id,
                path: path));
            return null;
        }
    }

    private static IReadOnlyList<PluginDiagnostic> ValidateDescriptor(
        DiscoveredPlugin plugin,
        PluginDesktopExtensionDescriptor descriptor,
        HashSet<string> extensionIds)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var path = plugin.Manifest.DesktopExtensionsPath;

        if (!PluginManifestParser.IsValidPluginId(descriptor.Id))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidDesktopExtensionId",
                "Desktop extension id is required and may contain only ASCII letters, digits, '.', '_', '-', or ':'.",
                plugin.Manifest.Id,
                path: path));
        }
        else if (!extensionIds.Add(descriptor.Id))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "DuplicateDesktopExtensionId",
                $"Desktop extension '{descriptor.Id}' is declared more than once.",
                plugin.Manifest.Id,
                path: path));
        }

        if (string.IsNullOrWhiteSpace(descriptor.DisplayName))
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "MissingDesktopExtensionDisplayName",
                "Desktop extension displayName is required.",
                plugin.Manifest.Id,
                descriptor.Id,
                path));
        }

        if (string.IsNullOrWhiteSpace(descriptor.Entry)
            || ResolveOptionalPluginPath(plugin.Manifest.RootPath, descriptor.Entry) == null)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidDesktopExtensionEntry",
                "Desktop extension entry must be a manifest-relative path that stays inside the plugin root.",
                plugin.Manifest.Id,
                descriptor.Id,
                path));
        }

        foreach (var style in descriptor.Styles)
        {
            if (ResolveOptionalPluginPath(plugin.Manifest.RootPath, style) == null)
            {
                diagnostics.Add(PluginDiagnostic.Error(
                    "InvalidDesktopExtensionStyle",
                    "Desktop extension styles must be manifest-relative paths that stay inside the plugin root.",
                    plugin.Manifest.Id,
                    descriptor.Id,
                    path));
            }
        }

        if (descriptor.Surfaces.Count == 0)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "MissingDesktopExtensionSurfaces",
                "Desktop extension surfaces must not be empty.",
                plugin.Manifest.Id,
                descriptor.Id,
                path));
        }

        foreach (var surface in descriptor.Surfaces)
        {
            if (string.IsNullOrWhiteSpace(surface.Type) || !KnownSurfaceTypes.Contains(surface.Type))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "UnknownDesktopExtensionSurface",
                    $"Desktop extension surface type '{surface.Type}' is not supported.",
                    plugin.Manifest.Id,
                    descriptor.Id,
                    path));
            }
        }

        return diagnostics;
    }

    private static void NormalizeDescriptorCollections(PluginDesktopExtensionDescriptor descriptor)
    {
        descriptor.Styles ??= [];
        descriptor.Surfaces ??= [];
        descriptor.RequiredAppIds ??= [];
        descriptor.ConnectOrigins ??= [];
        descriptor.SurfaceWriteScopes ??= [];
    }

    private static PluginDesktopExtensionDescriptor CloneDescriptorWithResolvedPaths(
        DiscoveredPlugin plugin,
        PluginDesktopExtensionDescriptor source)
    {
        var clone = JsonSerializer.Deserialize<PluginDesktopExtensionDescriptor>(
            JsonSerializer.Serialize(source, JsonOptions),
            JsonOptions) ?? new PluginDesktopExtensionDescriptor();
        NormalizeDescriptorCollections(clone);
        clone.Entry = ResolveOptionalPluginPath(plugin.Manifest.RootPath, clone.Entry) ?? clone.Entry;
        clone.Styles = clone.Styles
            .Select(style => ResolveOptionalPluginPath(plugin.Manifest.RootPath, style) ?? style)
            .ToList();
        return clone;
    }

    private static string? ResolveOptionalPluginPath(string pluginRoot, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("./", StringComparison.Ordinal))
            return null;

        var relative = value[2..];
        if (string.IsNullOrWhiteSpace(relative))
            return Path.GetFullPath(pluginRoot);

        if (relative
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == ".."))
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(pluginRoot, relative));
        var root = Path.GetFullPath(pluginRoot);
        var back = Path.GetRelativePath(root, candidate);
        return Path.IsPathRooted(back)
               || back.Equals("..", StringComparison.Ordinal)
               || back.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               || back.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            ? null
            : candidate;
    }
}

public sealed class PluginDesktopExtensionDocument
{
    public List<PluginDesktopExtensionDescriptor> Extensions { get; set; } = [];
}

public sealed class PluginDesktopExtensionDescriptor
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Entry { get; set; } = string.Empty;

    public List<string> Styles { get; set; } = [];

    public List<PluginDesktopExtensionSurfaceDescriptor> Surfaces { get; set; } = [];

    public List<string> RequiredAppIds { get; set; } = [];

    public List<string> ConnectOrigins { get; set; } = [];

    /// <summary>
    /// App Binding mutate scope ids this extension may exercise through the scoped
    /// write transport (<c>host.network.postJson</c>). Empty means read-only.
    /// </summary>
    public List<string> SurfaceWriteScopes { get; set; } = [];
}

public sealed class PluginDesktopExtensionSurfaceDescriptor
{
    public string Type { get; set; } = string.Empty;

    public string? ViewId { get; set; }

    public string? Label { get; set; }

    /// <summary>
    /// Optional per-locale overrides for <see cref="Label"/>, keyed by app locale
    /// (for example <c>en</c>, <c>zh-Hans</c>). The Desktop client resolves the
    /// active locale and falls back to <see cref="Label"/> when a locale is absent.
    /// </summary>
    public Dictionary<string, string>? LocalizedLabel { get; set; }

    /// <summary>
    /// Optional named glyph the host renders for this surface (for example a sidebar
    /// nav entry). Resolved by the Desktop client to a built-in icon; unknown or
    /// omitted values fall back to the generic extension icon.
    /// </summary>
    public string? Icon { get; set; }

    public string? Placement { get; set; }

    public int? Order { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? Slot { get; set; }

    public string? RendererId { get; set; }

    public string? ActionId { get; set; }

    public string? SettingsId { get; set; }
}
