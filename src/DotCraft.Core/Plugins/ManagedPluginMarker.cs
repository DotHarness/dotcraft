namespace DotCraft.Plugins;

internal sealed record ManagedPluginMarker(string Fingerprint, string? MarketplaceName)
{
    public const string FileName = ".builtin";
    private const string MarketplacePrefix = "marketplace=";

    public string Format() =>
        MarketplaceName == null ? Fingerprint : $"{Fingerprint}\n{MarketplacePrefix}{MarketplaceName}";

    public static ManagedPluginMarker? Read(string pluginRoot)
    {
        var path = Path.Combine(pluginRoot, FileName);
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }

    public static ManagedPluginMarker? TryRead(string pluginRoot)
    {
        try
        {
            return Read(pluginRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ManagedPluginMarker Parse(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var marketplace = lines.Skip(1)
            .FirstOrDefault(static line => line.StartsWith(MarketplacePrefix, StringComparison.Ordinal))?[MarketplacePrefix.Length..];
        return new ManagedPluginMarker(lines.FirstOrDefault() ?? string.Empty, marketplace);
    }
}
