using DotCraft.Context;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

/// <summary>
/// Context passed to module-provided AppServer protocol extensions.
/// </summary>
public sealed class AppServerExtensionContext(
    AppServerConnection connection,
    IAppServerTransport transport,
    ISessionService sessionService,
    string? workspaceCraftPath,
    string? hostWorkspacePath,
    IContextPageManager? contextPageManager,
    Action<string, string, object?>? notifyAppPrincipal,
    Action<string, object?>? broadcastTrustedNotification,
    CancellationToken cancellationToken)
{
    public AppServerConnection Connection { get; } = connection;

    public IAppServerTransport Transport { get; } = transport;

    public ISessionService SessionService { get; } = sessionService;

    public string? WorkspaceCraftPath { get; } = workspaceCraftPath;

    public string? HostWorkspacePath { get; } = hostWorkspacePath;

    public IContextPageManager? ContextPageManager { get; } = contextPageManager;

    public Action<string, string, object?>? NotifyAppPrincipal { get; } = notifyAppPrincipal;

    public Action<string, object?>? BroadcastTrustedNotification { get; } = broadcastTrustedNotification;

    public CancellationToken CancellationToken { get; } = cancellationToken;
}
