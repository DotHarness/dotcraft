namespace DotCraft.AppServer;

internal static class ClientSurfaceResolver
{
    public const string ApiSurfaceId = "api";

    private static readonly Dictionary<string, (string Id, ClientSurfaceKind Kind)> FirstPartyClients =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["dotcraft-desktop"] = ("dotcraft-desktop", ClientSurfaceKind.DesktopApp),
            ["dotcraft-mobile"] = ("dotcraft-mobile", ClientSurfaceKind.MobileApp),
            ["dotcraft-cli"] = ("cli", ClientSurfaceKind.Terminal),
            ["dotcraft-acp"] = ("acp", ClientSurfaceKind.Editor),
            ["oratorio"] = ("oratorio", ClientSurfaceKind.Service)
        };

    public static ClientSurface Resolve(ClientConnectionInfo? info, ClientConnectionCapabilities? caps)
    {
        if (caps?.ChannelAdapter is { } adapter)
            return new ClientSurface(adapter.ChannelName, ClientSurfaceKind.Chat, RemoteHost: true);

        var remoteHost = caps?.RemoteHost == true;
        if (caps?.AcpExtensions != null)
            return new ClientSurface("acp", ClientSurfaceKind.Editor, RemoteHost: remoteHost);

        if (info?.Name is { } name && FirstPartyClients.TryGetValue(name, out var known))
        {
            return new ClientSurface(
                known.Id,
                known.Kind,
                RemoteHost: remoteHost || known.Kind == ClientSurfaceKind.MobileApp);
        }

        return new ClientSurface(ApiSurfaceId, ClientSurfaceKind.Api, remoteHost);
    }
}
