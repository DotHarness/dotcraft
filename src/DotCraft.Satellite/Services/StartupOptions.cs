namespace DotCraft.Satellite.Services;

internal sealed record StartupOptions(string? Url, bool Background, bool Uninstall)
{
    public static StartupOptions Parse(IReadOnlyList<string> arguments)
    {
        string? url = null;
        var background = false;
        var uninstall = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--url" when index + 1 < arguments.Count:
                    url = arguments[++index];
                    break;
                case "--background":
                    background = true;
                    break;
                case "--uninstall":
                    uninstall = true;
                    break;
                default:
                    // A protocol handler may hand the link over without the flag.
                    url ??= SatelliteDeepLink.TryParse(arguments[index], out _) ? arguments[index] : null;
                    break;
            }
        }
        return new StartupOptions(url, background, uninstall);
    }
}
