using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.DynamicWorkflows;

public sealed record DynamicWorkflowDefinition(
    string Name,
    string Command,
    string Description,
    string? WhenToUse,
    string ScriptPath,
    string Source,
    string? PluginId = null);

public sealed class DynamicWorkflowCatalog(
    string workspacePath,
    string dataPath,
    string? userDataPath,
    AppConfig config,
    DynamicWorkflowParser parser,
    PluginDiscoveryService pluginDiscovery) : IPluginWorkflowSummaryProvider
{
    public IReadOnlyList<string> Diagnostics { get; private set; } = [];

    public IReadOnlyList<DynamicWorkflowDefinition> List()
    {
        var diagnostics = new List<string>();
        var definitions = new Dictionary<string, DynamicWorkflowDefinition>(StringComparer.OrdinalIgnoreCase);
        if (userDataPath is not null)
            AddScope(Path.Combine(userDataPath, "workflows"), "personal", null, definitions, diagnostics);
        AddScope(Path.Combine(dataPath, "workflows"), "workspace", null, definitions, diagnostics, overwrite: true);

        var plugins = pluginDiscovery.Discover(config, workspacePath, dataPath);
        foreach (var diagnostic in plugins.Diagnostics.Where(static item => item.Severity == PluginDiagnosticSeverity.Error))
            diagnostics.Add(diagnostic.Message);
        foreach (var plugin in plugins.Plugins)
        {
            if (plugin.Manifest.WorkflowsPath is { } root)
                AddScope(root, "plugin", plugin.Manifest.Id, definitions, diagnostics);
        }

        Diagnostics = diagnostics;
        return definitions.Values.OrderBy(static item => item.Command, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public DynamicWorkflowDefinition? FindByName(string name)
    {
        var normalized = name.Trim().TrimStart('/');
        return normalized.Contains(':', StringComparison.Ordinal)
            ? List().FirstOrDefault(item => string.Equals(item.Command.TrimStart('/'), normalized, StringComparison.OrdinalIgnoreCase))
            : List().FirstOrDefault(item => item.PluginId == null && string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public DynamicWorkflowDefinition? FindByPath(string path)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(workspacePath, path));
        return List().FirstOrDefault(item => PathsEqual(item.ScriptPath, full));
    }

    public IReadOnlyList<PluginWorkflowSummary> ListForPlugin(string pluginId)
    {
        var definitions = new Dictionary<string, DynamicWorkflowDefinition>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();
        var plugin = pluginDiscovery.DiscoverAll(config, workspacePath, dataPath).Plugins
            .FirstOrDefault(item => item.Installed && string.Equals(item.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (plugin?.Manifest.WorkflowsPath is { } root)
            AddScope(root, "plugin", plugin.Manifest.Id, definitions, diagnostics);
        return definitions.Values
            .Select(static item => new PluginWorkflowSummary(item.Name, item.Command, item.Description, item.WhenToUse))
            .ToArray();
    }

    private void AddScope(
        string root,
        string source,
        string? pluginId,
        Dictionary<string, DynamicWorkflowDefinition> definitions,
        List<string> diagnostics,
        bool overwrite = false)
    {
        if (!Directory.Exists(root)) return;
        var scopeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(root, "*.js", SearchOption.TopDirectoryOnly).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var safePath = ValidateContainedPath(root, path);
                var parsed = parser.Parse(File.ReadAllText(safePath));
                var key = pluginId == null ? parsed.Metadata.Name : $"{pluginId}:{parsed.Metadata.Name}";
                if (!scopeNames.Add(key))
                {
                    diagnostics.Add($"Duplicate workflow name '{key}' in {source} scope.");
                    continue;
                }
                var definition = new DynamicWorkflowDefinition(
                    parsed.Metadata.Name,
                    pluginId == null ? $"/{parsed.Metadata.Name}" : $"/{pluginId}:{parsed.Metadata.Name}",
                    parsed.Metadata.Description,
                    parsed.Metadata.WhenToUse,
                    safePath,
                    source,
                    pluginId);
                if (overwrite || !definitions.ContainsKey(key)) definitions[key] = definition;
            }
            catch (Exception ex)
            {
                diagnostics.Add($"Invalid workflow '{path}': {ex.Message}");
            }
        }
    }

    private static string ValidateContainedPath(string root, string path)
    {
        var fullRootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var fullRoot = fullRootPath + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(fullRoot, comparison))
            throw new InvalidOperationException("Workflow path escapes its discovery root.");

        var rootTarget = new DirectoryInfo(fullRootPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullRootPath;
        var canonicalRoot = Path.GetFullPath(rootTarget).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var relative = Path.GetRelativePath(fullRootPath, fullPath);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = Path.GetFullPath(rootTarget);
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var directory = new DirectoryInfo(Path.Combine(current, segments[index]));
            current = Path.GetFullPath(directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName);
            if (!(current + Path.DirectorySeparatorChar).StartsWith(canonicalRoot, comparison))
                throw new InvalidOperationException("Workflow symlink escapes its discovery root.");
        }
        var final = new FileInfo(Path.Combine(current, segments[^1]));
        var resolved = Path.GetFullPath(final.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? final.FullName);
        if (!resolved.StartsWith(canonicalRoot, comparison))
            throw new InvalidOperationException("Workflow symlink escapes its discovery root.");
        return fullPath;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
