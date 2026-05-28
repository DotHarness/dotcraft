using DotCraft.AppServer;
using DotCraft.Configuration;

namespace DotCraft.Tests.AppServer;

public sealed class ManagedAppServerEnvironmentTests
{
    [Fact]
    public void ApplyTo_AppliesRuntimeOverridesWithoutPersistingConfig()
    {
        var config = new AppConfig();
        config.DashBoard.Host = "0.0.0.0";
        config.DashBoard.Port = 8080;

        var env = new Dictionary<string, string?>
        {
            [ManagedAppServerEnvironment.ManagedFlag] = "1",
            [ManagedAppServerEnvironment.WebSocketHost] = "127.0.0.1",
            [ManagedAppServerEnvironment.WebSocketPort] = "43101",
            [ManagedAppServerEnvironment.WebSocketToken] = "ws-token",
            [ManagedAppServerEnvironment.DashboardHost] = "127.0.0.1",
            [ManagedAppServerEnvironment.DashboardPort] = "43102"
        };

        WithEnvironment(env, () => ManagedAppServerEnvironment.ApplyTo(config));

        var appServer = config.GetSection<AppServerConfig>("AppServer");
        Assert.Equal(AppServerMode.StdioAndWebSocket, appServer.Mode);
        Assert.Equal("127.0.0.1", appServer.WebSocket.Host);
        Assert.Equal(43101, appServer.WebSocket.Port);
        Assert.Equal("ws-token", appServer.WebSocket.Token);
        Assert.Equal("127.0.0.1", config.DashBoard.Host);
        Assert.Equal(43102, config.DashBoard.Port);

        Assert.Empty(config.Providers);
    }

    private static void WithEnvironment(IReadOnlyDictionary<string, string?> values, Action action)
    {
        var previous = values.ToDictionary(
            pair => pair.Key,
            pair => Environment.GetEnvironmentVariable(pair.Key),
            StringComparer.Ordinal);
        try
        {
            foreach (var (key, value) in values)
                Environment.SetEnvironmentVariable(key, value);

            action();
        }
        finally
        {
            foreach (var (key, value) in previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
