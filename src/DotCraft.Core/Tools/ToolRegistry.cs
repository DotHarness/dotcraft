using System.Reflection;

namespace DotCraft.Tools;

/// <summary>
/// Display metadata for surfaces that know only a tool name, such as CLI events, tracing and the
/// dashboard. Generated assemblies supply icons and call formatters; channel tools register theirs.
/// </summary>
public static class ToolRegistry
{
    private static readonly Dictionary<string, string> ToolIcons = new();
    private static readonly Dictionary<string, Func<IDictionary<string, object?>?, string>> DisplayFormatters = new();
    private static readonly Dictionary<string, string> DisplayTitles = new();
    private static readonly Dictionary<string, string> DisplaySubtitles = new();

    private static readonly Lock LockObject = new();
    private static readonly HashSet<Assembly> ScannedAssemblies = [];

    private const string DefaultIcon = "🔧";

    public static void ScanAssembly(Assembly assembly)
    {
        lock (LockObject)
        {
            if (ScannedAssemblies.Add(assembly))
                RegisterGenerated(ReadGeneratedDescriptors(assembly));
        }
    }

    /// <summary>
    /// Scans multiple assemblies for tool metadata.
    /// </summary>
    public static void ScanAssemblies(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
            ScanAssembly(assembly);
    }

    /// <summary>
    /// Returns the icon for a tool, or the default wrench emoji if none is registered.
    /// </summary>
    public static string GetToolIcon(string toolName)
        => ToolIcons.GetValueOrDefault(toolName, DefaultIcon);

    /// <summary>
    /// Returns a human-readable description for a tool call, or null if no formatter is registered.
    /// Callers should fall back to showing the raw tool name when this returns null.
    /// </summary>
    public static string? FormatToolCall(string toolName, IDictionary<string, object?>? args)
    {
        if (DisplayFormatters.TryGetValue(toolName, out var formatter))
        {
            try { return formatter(args); }
            catch { /* ignore formatter errors; caller uses fallback */ }
        }

        if (DisplayTitles.TryGetValue(toolName, out var title) && !string.IsNullOrWhiteSpace(title))
            return title;

        if (DisplaySubtitles.TryGetValue(toolName, out var subtitle) && !string.IsNullOrWhiteSpace(subtitle))
            return subtitle;

        return null;
    }

    /// <summary>
    /// Overload that accepts a <see cref="System.Text.Json.Nodes.JsonObject"/> argument map,
    /// converting each value to <c>object?</c> before delegating to the primary overload.
    /// </summary>
    public static string? FormatToolCall(string toolName, System.Text.Json.Nodes.JsonObject? args)
    {
        if (args == null)
            return FormatToolCall(toolName, (IDictionary<string, object?>?)null);

        var dict = new Dictionary<string, object?>(args.Count);
        foreach (var kv in args)
            dict[kv.Key] = kv.Value;
        return FormatToolCall(toolName, dict);
    }

    public static void RegisterDisplay(string toolName, string? title = null, string? subtitle = null, string? icon = null)
    {
        if (!string.IsNullOrWhiteSpace(icon))
            ToolIcons[toolName] = icon;

        if (!string.IsNullOrWhiteSpace(title))
            DisplayTitles[toolName] = title;
        else
            DisplayTitles.Remove(toolName);

        if (!string.IsNullOrWhiteSpace(subtitle))
            DisplaySubtitles[toolName] = subtitle;
        else
            DisplaySubtitles.Remove(toolName);
    }

    internal static void RegisterGenerated(IEnumerable<GeneratedToolDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (!string.IsNullOrEmpty(descriptor.Icon))
                ToolIcons[descriptor.Name] = descriptor.Icon;

            if (descriptor.DisplayFormatter != null)
                DisplayFormatters[descriptor.Name] = descriptor.DisplayFormatter;
        }
    }

    public static IReadOnlyDictionary<string, string> GetAllToolIcons()
        => ToolIcons;

    internal static IReadOnlyList<GeneratedToolDescriptor> ReadGeneratedDescriptors(Assembly assembly)
    {
        var descriptors = new List<GeneratedToolDescriptor>();
        foreach (var attribute in assembly.GetCustomAttributes<GeneratedToolCatalogAttribute>())
        {
            var property = attribute.ProviderType.GetProperty(
                "Descriptors",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property?.GetValue(null) is IEnumerable<GeneratedToolDescriptor> generated)
                descriptors.AddRange(generated);
        }

        return descriptors;
    }
}
