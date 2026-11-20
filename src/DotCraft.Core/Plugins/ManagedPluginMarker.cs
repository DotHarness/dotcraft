namespace DotCraft.Plugins;

internal sealed record ManagedPluginMarker(
    string Fingerprint,
    string? MarketplaceName,
    string? SourceIdentity = null)
{
    public const string FileName = ".builtin";
    private const string MarketplacePrefix = "marketplace=";
    private const string SourcePrefix = "source=";

    public string Format()
    {
        if (MarketplaceName == null)
            return Fingerprint;

        return string.IsNullOrWhiteSpace(SourceIdentity)
            ? $"{Fingerprint}\n{MarketplacePrefix}{MarketplaceName}"
            : $"{Fingerprint}\n{MarketplacePrefix}{MarketplaceName}\n{SourcePrefix}{SourceIdentity}";
    }

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
        string? marketplace = null;
        string? source = null;
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith(MarketplacePrefix, StringComparison.Ordinal))
                marketplace = line[MarketplacePrefix.Length..];
            else if (line.StartsWith(SourcePrefix, StringComparison.Ordinal))
                source = line[SourcePrefix.Length..];
        }

        return new ManagedPluginMarker(lines.FirstOrDefault() ?? string.Empty, marketplace, source);
    }
}
