namespace DotCraft.Plugins;

internal sealed record ManagedPluginMarker(
    string Fingerprint,
    string? MarketplaceName,
    string? SourceIdentity = null,
    string? ChannelInstallationId = null)
{
    public const string FileName = ".builtin";
    private const string MarketplacePrefix = "marketplace=";
    private const string SourcePrefix = "source=";
    private const string ChannelInstallationPrefix = "channel-installation=";

    public string Format()
    {
        var lines = new List<string> { Fingerprint };
        if (MarketplaceName != null)
            lines.Add(MarketplacePrefix + MarketplaceName);
        if (!string.IsNullOrWhiteSpace(SourceIdentity))
            lines.Add(SourcePrefix + SourceIdentity);
        if (ChannelInstallationId != null)
            lines.Add(ChannelInstallationPrefix + ChannelInstallationId);
        return string.Join('\n', lines);
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
        string? channelInstallationId = null;
        foreach (var line in lines.Skip(1))
        {
            if (line.StartsWith(MarketplacePrefix, StringComparison.Ordinal))
                marketplace = line[MarketplacePrefix.Length..];
            else if (line.StartsWith(SourcePrefix, StringComparison.Ordinal))
                source = line[SourcePrefix.Length..];
            else if (line.StartsWith(ChannelInstallationPrefix, StringComparison.Ordinal))
                channelInstallationId = line[ChannelInstallationPrefix.Length..];
        }

        return new ManagedPluginMarker(lines.FirstOrDefault() ?? string.Empty, marketplace, source, channelInstallationId);
    }
}
