namespace DotCraft.Plugins;

public static class PluginIds
{
    public const string Browser = "browser";
    public const string Chrome = "chrome";
    public const string Computer = "computer";
    public const string BundledMarketplace = "dotcraft-bundled";

    public static string Canonicalize(string pluginId) => pluginId;

    public static bool EqualsCanonical(string left, string right) =>
        string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.OrdinalIgnoreCase);

    public static string Key(string pluginId, string? marketplaceName) =>
        $"{pluginId}@{marketplaceName ?? BundledMarketplace}";

    // Plugin ids never contain '@', so the first one ends the id.
    public static string IdFromKey(string key)
    {
        var separator = key.IndexOf('@');
        return separator < 0 ? key : key[..separator];
    }

    public static bool KeysEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
