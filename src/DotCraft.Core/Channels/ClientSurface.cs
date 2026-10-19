namespace DotCraft.Channels;

public enum ClientSurfaceKind
{
    DesktopApp,
    MobileApp,
    Terminal,
    Editor,
    Chat,
    Service,
    Api
}

/// <summary>The client a request came from, resolved by the server from the connection rather than from turn input.</summary>
public sealed record ClientSurface(string Id, ClientSurfaceKind Kind, bool RemoteHost = false);
