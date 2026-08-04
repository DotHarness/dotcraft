namespace DotCraft.Hub;

/// <summary>
/// Resolves and initializes the default local workspace used by lightweight Chat entry points.
/// </summary>
internal static class DefaultChatWorkspace
{
    public static string Ensure(HubPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return Ensure(paths.DefaultChatWorkspacePath);
    }

    public static string Ensure(string workspacePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        var fullPath = Path.GetFullPath(workspacePath);
        var craftPath = Path.Combine(fullPath, ".craft");

        Directory.CreateDirectory(fullPath);
        Directory.CreateDirectory(craftPath);
        Directory.CreateDirectory(Path.Combine(craftPath, "memory"));
        Directory.CreateDirectory(Path.Combine(craftPath, "skills"));
        Directory.CreateDirectory(Path.Combine(craftPath, "security"));

        var configPath = Path.Combine(craftPath, "config.json");
        if (!File.Exists(configPath))
            File.WriteAllText(configPath, "{}" + Environment.NewLine);

        return fullPath;
    }
}
