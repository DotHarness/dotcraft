namespace DotCraft.Mcp;

internal static class McpStdioEnvironment
{
    public static Dictionary<string, string?> CreateOverlay(McpServerConfig config)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var environment = new Dictionary<string, string?>(comparer);
        foreach (var name in config.EnvVars.Where(static name => !string.IsNullOrWhiteSpace(name)))
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
                environment[name] = value;
        }
        foreach (var (name, value) in config.EnvironmentVariables)
            environment[name] = value;
        return environment;
    }
}
