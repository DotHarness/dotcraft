using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace DotCraft.Commands.Custom;

/// <summary>
/// Discovers, loads and expands custom commands defined as markdown files.
/// Commands are loaded from workspace (.craft/commands/) and user (~/.craft/commands/) directories.
/// </summary>
public sealed partial class CustomCommandLoader(string workspaceRoot)
{
    public string WorkspaceCommandsPath { get; } = Path.Combine(workspaceRoot, "commands");

    public string UserCommandsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".craft", "commands");

    /// <summary>
    /// Lists all available custom commands (workspace + user, workspace wins on conflict).
    /// </summary>
    public List<CustomCommandInfo> ListCommands()
    {
        var commands = new Dictionary<string, CustomCommandInfo>(StringComparer.OrdinalIgnoreCase);

        // User-level commands (lowest priority)
        if (Directory.Exists(UserCommandsPath))
            ScanDirectory(UserCommandsPath, "user", commands);

        // Workspace-level commands (highest priority, overwrites user)
        if (Directory.Exists(WorkspaceCommandsPath))
            ScanDirectory(WorkspaceCommandsPath, "workspace", commands);

        return commands.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Tries to resolve a slash command input to an expanded prompt.
    /// Returns null if the command name does not match any custom command file.
    /// </summary>
    /// <param name="rawInput">Full user input, e.g. "/code-review src/foo.cs"</param>
    public CustomCommandResult? TryResolve(string rawInput)
    {
        var trimmed = rawInput.Trim();
        if (!trimmed.StartsWith('/'))
            return null;

        var spaceIdx = trimmed.IndexOf(' ');
        var commandName = (spaceIdx > 0 ? trimmed[1..spaceIdx] : trimmed[1..]).ToLowerInvariant();
        var arguments = spaceIdx > 0 ? trimmed[(spaceIdx + 1)..].Trim() : string.Empty;

        var filePath = ResolveCommandFile(commandName);
        if (filePath == null)
            return null;

        var content = File.ReadAllText(filePath, Encoding.UTF8);
        var body = StripFrontmatter(content);
        var expanded = SubstitutePlaceholders(body, arguments);

        return new CustomCommandResult
        {
            CommandName = commandName,
            ExpandedPrompt = expanded
        };
    }

    /// <summary>
    /// Deploys built-in command files (embedded in the assembly) to the workspace commands directory.
    /// Skips files that already exist unless the assembly version has changed.
    /// </summary>
    /// <param name="resourceAssembly">
    /// The assembly that contains the embedded command resources.
    /// Pass <c>typeof(Program).Assembly</c> (or equivalent) from the host application.
    /// Falls back to the assembly containing <see cref="CustomCommandLoader"/> when null.
    /// </param>
    public void DeployBuiltInCommands(Assembly? resourceAssembly = null)
    {
        const string resourcePrefix = "DotCraft.Commands.Custom.BuiltIn.";
        const string markerFile = ".builtin_commands";

        var assembly = resourceAssembly ?? typeof(CustomCommandLoader).Assembly;
        var currentVersion = assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        var markerPath = Path.Combine(WorkspaceCommandsPath, markerFile);
        if (File.Exists(markerPath) && File.ReadAllText(markerPath).Trim() == currentVersion)
            return;

        var resources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(resourcePrefix, StringComparison.Ordinal))
            .ToList();

        if (resources.Count == 0)
            return;

        Directory.CreateDirectory(WorkspaceCommandsPath);

        foreach (var resourceName in resources)
        {
            var fileName = resourceName[resourcePrefix.Length..];
            var targetPath = Path.Combine(WorkspaceCommandsPath, fileName);

            // Don't overwrite user-customized files (no .builtin marker for individual files;
            // we only skip the entire deploy if version matches above)
            if (File.Exists(targetPath) && !File.Exists(markerPath))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var file = File.Create(targetPath);
            stream.CopyTo(file);
        }

        File.WriteAllText(markerPath, currentVersion);
    }

    /// <summary>
    /// Builds a summary of all custom commands for inclusion in the system prompt.
    /// </summary>
    public string BuildCommandsSummary()
    {
        var commands = ListCommands();
        if (commands.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Custom Commands");
        sb.AppendLine();
        sb.AppendLine("The following custom commands are available. Users can invoke them with `/command-name [args]`.");
        sb.AppendLine();

        foreach (var cmd in commands)
        {
            var desc = string.IsNullOrWhiteSpace(cmd.Description)
                ? "(no description)"
                : cmd.Description;
            sb.AppendLine($"- `/{cmd.Name}`: {desc}");
        }

        return sb.ToString();
    }

    private string? ResolveCommandFile(string commandName)
    {
        // Namespace separator: "frontend:component" -> "frontend/component.md"
        var relativePath = commandName.Replace(':', Path.DirectorySeparatorChar) + ".md";

        // Workspace takes priority
        var workspacePath = Path.Combine(WorkspaceCommandsPath, relativePath);
        if (File.Exists(workspacePath))
            return workspacePath;

        var userPath = Path.Combine(UserCommandsPath, relativePath);
        if (File.Exists(userPath))
            return userPath;

        return null;
    }

    private void ScanDirectory(string rootDir, string source, Dictionary<string, CustomCommandInfo> commands)
    {
        foreach (var filePath in Directory.EnumerateFiles(rootDir, "*.md", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (fileName.StartsWith('.'))
                continue;

            // Build command name from relative path (subdirectories become namespace with ':')
            var relDir = Path.GetDirectoryName(Path.GetRelativePath(rootDir, filePath)) ?? string.Empty;
            var commandName = string.IsNullOrEmpty(relDir)
                ? fileName
                : $"{relDir.Replace(Path.DirectorySeparatorChar, ':').Replace(Path.AltDirectorySeparatorChar, ':')}:{fileName}";

            var content = File.ReadAllText(filePath, Encoding.UTF8);
            var metadata = ParseFrontmatter(content);
            var description = metadata?.GetValueOrDefault("description", string.Empty) ?? string.Empty;

            commands[commandName.ToLowerInvariant()] = new CustomCommandInfo
            {
                Name = commandName.ToLowerInvariant(),
                Description = description,
                Path = filePath,
                Source = source
            };
        }
    }

    private static Dictionary<string, string>? ParseFrontmatter(string content)
    {
        if (!content.StartsWith("---"))
            return null;

        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
            return null;

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in match.Groups[1].Value.Split('\n'))
        {
            if (!line.Contains(':'))
                continue;
            var parts = line.Split(':', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim().Trim('"', '\'');
                metadata[key] = value;
            }
        }

        return metadata;
    }

    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---"))
            return content.Trim();

        var match = FrontmatterStripRegex().Match(content);
        return match.Success ? content[match.Length..].Trim() : content.Trim();
    }

    private static string SubstitutePlaceholders(string body, string arguments)
    {
        var result = body;

        // $ARGUMENTS -> full argument string
        result = result.Replace("$ARGUMENTS", arguments);

        // $1, $2, $3... -> positional arguments
        var positional = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < positional.Length && i < 9; i++)
        {
            result = result.Replace($"${i + 1}", positional[i]);
        }

        // Clean up unreplaced positional placeholders
        result = UnusedPositionalRegex().Replace(result, string.Empty);

        return result.Trim();
    }

    [GeneratedRegex(@"^---\r?\n(.*?)\r?\n---", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^---\r?\n.*?\r?\n---\r?\n", RegexOptions.Singleline)]
    private static partial Regex FrontmatterStripRegex();

    [GeneratedRegex(@"\$\d+")]
    private static partial Regex UnusedPositionalRegex();
}

/// <summary>
/// Metadata about a discovered custom command.
/// </summary>
public sealed class CustomCommandInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Result of resolving a custom command from user input.
/// </summary>
public sealed class CustomCommandResult
{
    public string CommandName { get; set; } = string.Empty;
    public string ExpandedPrompt { get; set; } = string.Empty;
}
