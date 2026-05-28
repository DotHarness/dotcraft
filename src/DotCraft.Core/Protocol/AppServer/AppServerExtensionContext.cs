using DotCraft.Context;

namespace DotCraft.Protocol.AppServer;

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
    CancellationToken cancellationToken)
{
    public AppServerConnection Connection { get; } = connection;

    public IAppServerTransport Transport { get; } = transport;

    public ISessionService SessionService { get; } = sessionService;

    public string? WorkspaceCraftPath { get; } = workspaceCraftPath;

    public string? HostWorkspacePath { get; } = hostWorkspacePath;

    public IContextPageManager? ContextPageManager { get; } = contextPageManager;

    public CancellationToken CancellationToken { get; } = cancellationToken;
}
