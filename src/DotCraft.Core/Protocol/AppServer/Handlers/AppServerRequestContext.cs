namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Request-scoped context for AppServer JSON-RPC handling. Hosts set
/// <see cref="CurrentTransport"/> around <see cref="AppServerRequestHandler.HandleRequestAsync"/>
/// so callbacks (e.g. <c>thread/started</c> broadcast) can skip the transport that initiated the request.
/// </summary>
public static class AppServerRequestContext
{
    private static readonly AsyncLocal<IAppServerTransport?> _currentTransport = new();
    private static readonly AsyncLocal<AppServerConnection?> _currentConnection = new();
    private static readonly AsyncLocal<string?> _currentMethod = new();

    /// <summary>
    /// The transport currently executing an incoming request, or null when not in a handler
    /// (e.g. cron, CLI, or code paths that do not wrap <c>HandleRequestAsync</c>).
    /// </summary>
    public static IAppServerTransport? CurrentTransport
    {
        get => _currentTransport.Value;
        set => _currentTransport.Value = value;
    }

    /// <summary>
    /// The AppServer connection currently executing an incoming request, or null outside request handling.
    /// </summary>
    public static AppServerConnection? CurrentConnection
    {
        get => _currentConnection.Value;
        set => _currentConnection.Value = value;
    }

    /// <summary>
    /// The JSON-RPC method currently executing, or null outside request handling.
    /// </summary>
    public static string? CurrentMethod
    {
        get => _currentMethod.Value;
        set => _currentMethod.Value = value;
    }
}
