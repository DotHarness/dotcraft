namespace DotCraft.Plugins;

public static class PluginIds
{
    public const string AgentTeams = "agent-teams";
    public const string Browser = "browser";
    public const string Chrome = "chrome";

    public static string Canonicalize(string pluginId) => pluginId;

    public static bool EqualsCanonical(string left, string right) =>
        string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.OrdinalIgnoreCase);
}
