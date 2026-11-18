namespace DotCraft.Plugins;

public static class PluginIds
{
    public const string Browser = "browser";
    public const string Chrome = "chrome";
    public const string Computer = "computer";

    public static string Canonicalize(string pluginId) => pluginId;

    public static bool EqualsCanonical(string left, string right) =>
        string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.OrdinalIgnoreCase);

    public static string Key(string pluginId, string? marketplaceName) =>
        string.IsNullOrEmpty(marketplaceName) ? pluginId : $"{pluginId}@{marketplaceName}";

    // Plugin ids never contain '@', so the first one separates the marketplace.
    public static (string PluginId, string? MarketplaceName) ParseKey(string key)
    {
        var separator = key.IndexOf('@');
        return separator < 0 ? (key, null) : (key[..separator], key[(separator + 1)..]);
    }

    public static bool KeysEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
