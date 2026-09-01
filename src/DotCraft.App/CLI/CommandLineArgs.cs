using DotCraft.Acp;
using DotCraft.AppServer;
using DotCraft.Configuration;

namespace DotCraft.CLI;

public sealed record CommandLineArgs
{
    public enum RunMode
    {
        Exec,
        AppServer,
        Acp,
        Setup,
        Hub,
        Dashboard,
        ModelCatalog,
        WorkflowWorker
    }

    public required RunMode Mode { get; init; }
    public string? ListenUrl { get; init; }
    public string? RemoteUrl { get; init; }
    public string? Token { get; init; }
    public string? ExecPrompt { get; init; }
    public bool ExecReadStdin { get; init; }
    public string? SetupModel { get; init; }
    public string? SetupPreferenceJson { get; init; }
    public string? SetupEndPoint { get; init; }
    public string? SetupApiKey { get; init; }
    public string? SetupProviderMode { get; init; }
    public string? SetupProviderId { get; init; }
    public string? SetupProviderDisplayName { get; init; }
    public string? SetupProviderProtocol { get; init; }
    public string? SetupProviderTimeoutSeconds { get; init; }
    public string? SetupAuthMethod { get; init; }
    public bool SaveUserConfig { get; init; }
    public bool PreferExistingUserConfig { get; init; }
    public bool SetupSetUserDefault { get; init; }
    public bool SetupSkipProvider { get; init; }
    public string? DashboardWorkspacePath { get; init; }
    public string? DashboardHost { get; init; }
    public int? DashboardPort { get; init; }
    public bool ModelCatalogReadStdin { get; init; }
    public bool ReservesStdout { get; init; }

    public void ApplyTo(AppConfig config)
    {
        switch (Mode)
        {
            case RunMode.Acp:
                {
                    var acp = new AcpConfig { Enabled = true };
                    if (!string.IsNullOrWhiteSpace(RemoteUrl))
                    {
                        acp.AppServerUrl = RemoteUrl;
                        acp.AppServerToken = Token;
                    }

                    config.SetSection("Acp", acp);
                    config.DashBoard.Enabled = false;
                    break;
                }
            case RunMode.AppServer:
                ApplyAppServerConfig(config);
                break;
            case RunMode.Dashboard:
                ApplyDashboardConfig(config);
                break;
            case RunMode.Exec:
                ApplyCliConfig(config);
                break;
            case RunMode.Setup:
            case RunMode.Hub:
            case RunMode.ModelCatalog:
            case RunMode.WorkflowWorker:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ApplyDashboardConfig(AppConfig config)
    {
        if (!string.IsNullOrWhiteSpace(DashboardHost))
            config.DashBoard.Host = DashboardHost.Trim();
        if (DashboardPort.HasValue)
            config.DashBoard.Port = DashboardPort.Value;
    }

    private void ApplyAppServerConfig(AppConfig config)
    {
        var (mode, host, port) = ParseListenUrl(ListenUrl);
        var appServer = new AppServerConfig { Mode = mode };
        if (mode is AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket)
        {
            appServer.WebSocket = new WebSocketServerConfig
            {
                Host = host ?? "127.0.0.1",
                Port = port ?? 9100,
                Token = Token
            };
        }
        config.SetSection("AppServer", appServer);
    }

    private void ApplyCliConfig(AppConfig config)
    {
        if (RemoteUrl is null)
            return;
        var cli = config.GetSection<CliConfig>("CLI");
        cli.AppServerUrl = RemoteUrl;
        if (Token is not null)
            cli.AppServerToken = Token;
        config.SetSection("CLI", cli);
    }

    internal static (AppServerMode Mode, string? Host, int? Port) ParseListenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url == "stdio://")
            return (AppServerMode.Stdio, null, null);
        if (url.StartsWith("ws+stdio://", StringComparison.Ordinal))
        {
            var (host, port) = ParseHostPort(url["ws+stdio://".Length..]);
            return (AppServerMode.StdioAndWebSocket, host, port);
        }
        if (url.StartsWith("ws://", StringComparison.Ordinal))
        {
            var (host, port) = ParseHostPort(url["ws://".Length..]);
            return (AppServerMode.WebSocket, host, port);
        }
        if (url.StartsWith("wss://", StringComparison.Ordinal))
            throw new ArgumentException("The wss:// scheme is not supported. Use ws:// or terminate TLS in front of AppServer.");
        throw new ArgumentException("--listen must use stdio://, ws://, or ws+stdio://.");
    }

    private static (string Host, int? Port) ParseHostPort(string hostPort)
    {
        var pathIndex = hostPort.IndexOf('/');
        if (pathIndex >= 0)
            hostPort = hostPort[..pathIndex];
        var colonIndex = hostPort.LastIndexOf(':');
        if (colonIndex < 0)
            return (hostPort, null);
        var host = hostPort[..colonIndex];
        return int.TryParse(hostPort[(colonIndex + 1)..], out var port)
            ? (host, port)
            : (hostPort, null);
    }
}
