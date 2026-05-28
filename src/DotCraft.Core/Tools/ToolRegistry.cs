using System.Reflection;
using DotCraft.Cron;

namespace DotCraft.Tools;

/// <summary>
/// Registry for tool metadata discovered via <see cref="ToolAttribute"/>.
/// Stores icons and optional display formatter delegates for each tool method.
/// </summary>
public static class ToolRegistry
{
    private static readonly Dictionary<string, string> ToolIcons = new();
    private static readonly Dictionary<string, Func<IDictionary<string, object?>?, string>> DisplayFormatters = new();
    private static readonly Dictionary<string, string> DisplayTitles = new();
    private static readonly Dictionary<string, string> DisplaySubtitles = new();
    private static readonly Dictionary<string, int> MaxResultCharsByTool = new();

    private static readonly Lock LockObject = new();
    private static readonly HashSet<Assembly> ScannedAssemblies = [];

    private const string DefaultIcon = "🔧";

    /// <summary>
    /// Scans an assembly for tool methods decorated with <see cref="ToolAttribute"/>.
    /// Registers icons and resolves display formatter delegates.
    /// </summary>
    public static void ScanAssembly(Assembly assembly)
    {
        lock (LockObject)
        {
            if (!ScannedAssemblies.Add(assembly))
                return;

            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition || !type.IsClass)
                    continue;

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    var attr = method.GetCustomAttribute<ToolAttribute>();
                    if (attr == null)
                        continue;

                    if (!string.IsNullOrEmpty(attr.Icon))
                        ToolIcons[method.Name] = attr.Icon;

                    if (attr.DisplayType != null && !string.IsNullOrEmpty(attr.DisplayMethod))
                        TryRegisterDisplayFormatter(method.Name, attr.DisplayType, attr.DisplayMethod);

                    if (attr.MaxResultChars != -1)
                        MaxResultCharsByTool[method.Name] = attr.MaxResultChars;
                }
            }
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
    /// Returns the per-tool maximum result length in characters when set on <see cref="ToolAttribute"/>,
    /// or <see langword="null"/> to use the global default from configuration.
    /// </summary>
    public static int? GetMaxResultChars(string toolName)
        => MaxResultCharsByTool.TryGetValue(toolName, out var n) ? n : null;

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

    /// <summary>
    /// Returns structured human-readable lines for a tool result, or null if no formatter
    /// is available for this tool. Callers should fall back to generic truncation when null.
    /// </summary>
    public static IReadOnlyList<string>? FormatToolResult(string? toolName, string? result)
    {
        return toolName switch
        {
            "SearchTools" => CoreToolDisplays.SearchToolsResult(result),
            "WebSearch" => CoreToolDisplays.WebSearchResult(result),
            "WebFetch" => CoreToolDisplays.WebFetchResult(result),
            "ReadFile" => CoreToolDisplays.ReadFileResult(result),
            "WriteFile" => CoreToolDisplays.WriteFileResult(result),
            "EditFile" => CoreToolDisplays.EditFileResult(result),
            "GrepFiles" => CoreToolDisplays.GrepFilesResult(result),
            "FindFiles" => CoreToolDisplays.FindFilesResult(result),
            "Exec" => CoreToolDisplays.ExecResult(result),
            "LSP" => CoreToolDisplays.LspResult(result),
            "Cron" => CronToolDisplays.CronResult(result),
            _ => null
        };
    }

    public static void RegisterIcon(string toolName, string icon)
        => ToolIcons[toolName] = icon;

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

    public static void RegisterDisplayFormatter(string toolName, Func<IDictionary<string, object?>?, string> formatter)
        => DisplayFormatters[toolName] = formatter;

    public static void RegisterDisplayFormatter(string toolName, Type displayType, string methodName)
        => TryRegisterDisplayFormatter(toolName, displayType, methodName);

    public static void RegisterMaxResultChars(string toolName, int maxResultChars)
        => MaxResultCharsByTool[toolName] = maxResultChars;

    public static bool IsToolIconRegistered(string toolName)
        => ToolIcons.ContainsKey(toolName);

    public static IReadOnlyDictionary<string, string> GetAllToolIcons()
        => ToolIcons;

    /// <summary>
    /// Resets all registry state. Used in tests only.
    /// </summary>
    internal static void Reset()
    {
        lock (LockObject)
        {
            ToolIcons.Clear();
            DisplayFormatters.Clear();
            DisplayTitles.Clear();
            DisplaySubtitles.Clear();
            MaxResultCharsByTool.Clear();
            ScannedAssemblies.Clear();
        }
    }

    private static void TryRegisterDisplayFormatter(string toolName, Type displayType, string methodName)
    {
        var method = displayType.GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Static,
            [typeof(IDictionary<string, object?>)]);

        if (method == null)
            return;

        var del = (Func<IDictionary<string, object?>?, string>)Delegate.CreateDelegate(
            typeof(Func<IDictionary<string, object?>?, string>),
            method,
            throwOnBindFailure: false)!;

        if (del != null)
            DisplayFormatters[toolName] = del;
    }
}
