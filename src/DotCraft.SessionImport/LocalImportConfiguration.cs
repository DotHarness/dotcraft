using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.SessionImport;

internal sealed record LocalImportHostOptions(
    string ProjectsPath,
    string ChatPath,
    string HomePath,
    IReadOnlyDictionary<string, string> SourceRoots,
    string? CursorWorkspaceStorage);

internal static class LocalImportConfiguration
{
    public static SessionImportConfig ReadLimits(string dataPath)
    {
        var limits = new SessionImportConfig();
        if (ReadSection(Path.Combine(dataPath, "config.json"), "AgentImport") is not JsonObject section)
            return limits;

        if (AtomicConfigDocument.Value(section, nameof(limits.Enabled)) is JsonValue enabled
            && enabled.TryGetValue<bool>(out var isEnabled))
            limits.Enabled = isEnabled;
        if (AtomicConfigDocument.Value(section, nameof(limits.MaxSessionAgeDays)) is JsonValue days
            && days.TryGetValue<int>(out var maxDays) && maxDays > 0)
            limits.MaxSessionAgeDays = maxDays;
        if (AtomicConfigDocument.Value(section, nameof(limits.MaxSessionsPerSource)) is JsonValue sessions
            && sessions.TryGetValue<int>(out var maxSessions) && maxSessions > 0)
            limits.MaxSessionsPerSource = maxSessions;
        return limits;
    }

    public static IReadOnlyList<string> ReadRegisteredProjects(string path)
    {
        if (ReadSection(path, "projects") is not JsonArray projects) return [];
        return projects.OfType<JsonObject>()
            .Select(p => AtomicConfigDocument.Value(p, "path"))
            .OfType<JsonValue>()
            .Select(p => p.TryGetValue<string>(out var value) ? value : null)
            .OfType<string>()
            .ToArray();
    }

    public static IReadOnlyList<string> ReadSourceProjectPaths(IReadOnlyDictionary<string, string> roots, string? cursorWorkspaceStorage)
    {
        var paths = new List<string>();
        if (roots.TryGetValue(SessionImportSources.ClaudeCode, out var claude) && Path.GetDirectoryName(claude) is { } claudeHome)
        {
            if (ReadSection(Path.Combine(claudeHome, ".claude.json"), "projects") is JsonObject claudeProjects)
                paths.AddRange(claudeProjects.Select(p => p.Key));
        }

        if (roots.TryGetValue(SessionImportSources.Codex, out var codex))
        {
            try
            {
                if (SetupImportScanner.ReadToml(Path.Combine(codex, "config.toml"))["projects"] is JsonObject codexProjects)
                    paths.AddRange(codexProjects.Select(p => p.Key));
            }
            catch (Exception ex) when (IsUnavailable(ex) || ex is Tomlyn.TomlException) { }

            if (ReadSection(Path.Combine(codex, ".codex-global-state.json"), "electron-saved-workspace-roots") is JsonArray saved)
                paths.AddRange(saved.OfType<JsonValue>().Select(p => p.TryGetValue<string>(out var value) ? value : null).OfType<string>());
        }

        if (cursorWorkspaceStorage is not null && roots.ContainsKey(SessionImportSources.Cursor))
        {
            foreach (var storage in SessionFileWindow.Directories(cursorWorkspaceStorage))
            {
                if (ReadSection(Path.Combine(storage, "workspace.json"), "folder") is JsonValue folder
                    && folder.TryGetValue<string>(out var value) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                    && uri.IsFile && !uri.IsUnc)
                    paths.Add(uri.LocalPath);
            }
        }

        return paths.Where(p => WorkspacePathMatcher.Normalize(p) != null).ToArray();
    }

    public static string CurrentCursorWorkspaceStorage()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Application Support")
            : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(home, ".config");
        return Path.Combine(appData, "Cursor", "User", "workspaceStorage");
    }

    public static bool IsUnavailable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException or ArgumentException;

    private static JsonNode? ReadSection(string path, string key)
    {
        try
        {
            return AtomicConfigDocument.Value(AtomicConfigDocument.Read(path), key);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return null;
        }
    }
}
