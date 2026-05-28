using System.Security.Cryptography;
using DotCraft.Configuration;

namespace DotCraft.AppServer;

/// <summary>
/// Environment-variable contract used by Hub-managed AppServer processes.
/// </summary>
public static class ManagedAppServerEnvironment
{
    public const string ManagedFlag = "DOTCRAFT_MANAGED_APP_SERVER";
    public const string HubApiBaseUrl = "DOTCRAFT_HUB_API_BASE_URL";
    public const string HubToken = "DOTCRAFT_HUB_TOKEN";
    public const string WebSocketHost = "DOTCRAFT_MANAGED_APPSERVER_WS_HOST";
    public const string WebSocketPort = "DOTCRAFT_MANAGED_APPSERVER_WS_PORT";
    public const string WebSocketToken = "DOTCRAFT_MANAGED_APPSERVER_WS_TOKEN";
    public const string DashboardHost = "DOTCRAFT_MANAGED_DASHBOARD_HOST";
    public const string DashboardPort = "DOTCRAFT_MANAGED_DASHBOARD_PORT";

    /// <summary>
    /// Returns whether the current AppServer process was launched by Hub.
    /// </summary>
    public static bool IsManaged =>
        string.Equals(Environment.GetEnvironmentVariable(ManagedFlag), "1", StringComparison.Ordinal);

    /// <summary>
    /// Applies Hub-provided runtime overrides to in-memory configuration only.
    /// </summary>
    public static void ApplyTo(AppConfig config)
    {
        if (!IsManaged)
            return;

        var wsHost = GetString(WebSocketHost) ?? "127.0.0.1";
        var wsPort = GetPort(WebSocketPort) ?? 9100;
        var wsToken = GetString(WebSocketToken) ?? CreateToken();

        config.SetSection("AppServer", new AppServerConfig
        {
            Mode = AppServerMode.StdioAndWebSocket,
            WebSocket = new WebSocketServerConfig
            {
                Host = wsHost,
                Port = wsPort,
                Token = wsToken
            }
        });

        ApplyDashboard(config);
    }

    private static void ApplyDashboard(AppConfig config)
    {
        var host = GetString(DashboardHost);
        var port = GetPort(DashboardPort);
        if (host is null && port is null)
            return;

        if (host is not null)
            config.DashBoard.Host = host;
        if (port is not null)
            config.DashBoard.Port = port.Value;
    }

    private static string? GetString(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? GetPort(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!int.TryParse(value, out var port) || port <= 0 || port > 65535)
            return null;
        return port;
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }
}
