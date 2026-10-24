using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed record WorktreeEnvironment(
    string FilePath,
    string RepositoryRoot,
    string Root,
    string SetupScript,
    string CleanupScript)
{
    private const int MaxLevels = 50;
    private static readonly string[] Platforms = ["win32", "darwin", "linux"];

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static string HostPlatform =>
        OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : "linux";

    public static WorktreeEnvironment Require(string? environmentPath, string sourceWorkspace) =>
        Read(environmentPath, sourceWorkspace)
        ?? throw Invalid($"Environment file '{environmentPath}' does not exist.");

    public static WorktreeEnvironment? Read(string? environmentPath, string sourceWorkspace)
    {
        var trimmed = environmentPath?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !Path.IsPathFullyQualified(trimmed))
            throw Invalid("environmentPath must be an absolute path.");

        var filePath = Path.GetFullPath(trimmed);
        if (!filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw Invalid("The environment file must be a .json file.");

        var folders = EnvironmentFolders(sourceWorkspace);
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var root = folders.FirstOrDefault(folder =>
            SamePath(Path.Combine(folder, ".craft", "environments"), directory));
        if (root == null)
        {
            throw Invalid(
                "The environment file must be in a .craft/environments folder of the workspace or of a parent folder up to the repository root.");
        }

        if (!File.Exists(filePath))
            return null;

        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Invalid($"Failed to read: {ex.Message}");
        }

        var (setup, cleanup) = Parse(content);
        return new WorktreeEnvironment(filePath, RepositoryRootOf(folders), root, setup, cleanup);
    }

    public string RootIn(string worktreePath)
    {
        var relative = Path.GetRelativePath(RepositoryRoot, Root);
        if (relative == ".")
            return worktreePath;
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return worktreePath;
        }

        var candidate = Path.GetFullPath(Path.Combine(worktreePath, relative));
        return Directory.Exists(candidate) ? candidate : worktreePath;
    }

    private static (string Setup, string Cleanup) Parse(string content)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content.TrimStart('﻿'));
        }
        catch (JsonException ex)
        {
            throw Invalid($"Failed to parse: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Invalid("The file must contain a JSON object");
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetDouble(out var number) || number != 1))
            {
                throw Invalid("\"version\" must be 1");
            }

            if (root.TryGetProperty("name", out var name) && name.ValueKind != JsonValueKind.String)
                throw Invalid("\"name\" must be a string");

            var setup = Script(root, "setup");
            var cleanup = Script(root, "cleanup");
            ValidateActions(root);
            return (setup, cleanup);
        }
    }

    private static string Script(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var value))
            return string.Empty;
        var script = ScriptText(value) ?? throw Invalid($"\"{key}\" must be an object with a string \"script\"");

        string? selected = null;
        foreach (var platform in Platforms)
        {
            if (!value.TryGetProperty(platform, out var overrideValue))
                continue;
            var overrideScript = ScriptText(overrideValue)
                                 ?? throw Invalid($"\"{key}.{platform}\" must be an object with a string \"script\"");
            if (platform == HostPlatform && overrideScript.Length > 0)
                selected = overrideScript;
        }

        return selected ?? script;
    }

    private static string? ScriptText(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty("script", out var script)
        && script.ValueKind == JsonValueKind.String
            ? script.GetString()!.Trim()
            : null;

    private static void ValidateActions(JsonElement root)
    {
        if (!root.TryGetProperty("actions", out var actions))
            return;
        if (actions.ValueKind != JsonValueKind.Array)
            throw Invalid("\"actions\" must be an array");

        var index = 0;
        foreach (var entry in actions.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw Invalid($"actions[{index}] must be an object");
            if (entry.TryGetProperty("platform", out var platform)
                && (platform.ValueKind != JsonValueKind.String || !Platforms.Contains(platform.GetString()!.Trim())))
            {
                throw Invalid($"actions[{index}].platform must be win32, darwin, or linux");
            }

            index++;
        }
    }

    private static List<string> EnvironmentFolders(string workspacePath)
    {
        var folders = new List<string>();
        var folder = Path.GetFullPath(workspacePath);
        for (var level = 0; level < MaxLevels; level++)
        {
            folders.Add(folder);
            if (HasGit(folder))
                break;
            var parent = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent) || SamePath(parent, folder))
                break;
            folder = parent;
        }

        return folders;
    }

    private static string RepositoryRootOf(List<string> folders) =>
        HasGit(folders[^1]) ? folders[^1] : folders[0];

    private static bool HasGit(string folder)
    {
        var git = Path.Combine(folder, ".git");
        return Directory.Exists(git) || File.Exists(git);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            PathComparison);

    private static WorktreeEnvironmentException Invalid(string message) =>
        new(WorktreeEnvironmentErrorCodes.EnvironmentInvalid, message);
}
