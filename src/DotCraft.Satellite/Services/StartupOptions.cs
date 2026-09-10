namespace DotCraft.Satellite.Services;

internal sealed record StartupOptions(
    string? Url,
    bool Background,
    bool Uninstall,
    string? PreviewIsland = null,
    string? PreviewConsent = null)
{
    public string? Preview => PreviewIsland ?? PreviewConsent;

    public static StartupOptions Parse(IReadOnlyList<string> arguments)
    {
        string? url = null;
        string? previewIsland = null;
        string? previewConsent = null;
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
                case "--preview-island" when index + 1 < arguments.Count:
                    previewIsland = arguments[++index];
                    break;
                case "--preview-consent" when index + 1 < arguments.Count:
                    previewConsent = arguments[++index];
                    break;
                default:
                    // A protocol handler may hand the link over without the flag.
                    url ??= SatelliteDeepLink.TryParse(arguments[index], out _) ? arguments[index] : null;
                    break;
            }
        }
        return new StartupOptions(url, background, uninstall, previewIsland, previewConsent);
    }
}
