namespace DotCraft.Satellite.Services;

internal static class SatelliteDeepLink
{
    private const string Scheme = "dotcraft";
    private const string Host = "satellite";
    private const string JoinPath = "/join";
    private const int MaxLength = 2048;

    /// <summary>
    /// Every link that is not a join invitation, including Desktop's own
    /// <c>dotcraft://workspace/open</c>, is refused.
    /// </summary>
    public static bool TryParse(string? link, out string inviteUrl)
    {
        inviteUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(link) || link.Length > MaxLength || link.Any(char.IsControl))
            return false;

        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is "http" or "https")
        {
            inviteUrl = uri.ToString();
            return true;
        }

        if (!string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.AbsolutePath.TrimEnd('/'), JoinPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var invite = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .FirstOrDefault(parts => parts.Length == 2
                                     && string.Equals(parts[0], "invite", StringComparison.Ordinal));
        if (invite is null)
            return false;

        var decoded = Uri.UnescapeDataString(invite[1]);
        if (decoded.Length == 0 || decoded.Any(char.IsControl))
            return false;

        inviteUrl = decoded;
        return true;
    }
}
