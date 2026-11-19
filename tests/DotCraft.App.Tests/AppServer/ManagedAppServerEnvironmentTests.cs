using DotCraft.AppServer;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class ManagedAppServerEnvironmentTests
{
    [Fact]
    public void ApplyTo_ConfiguresManagedWebSocket()
    {
        var config = new AppConfig();
        var env = new Dictionary<string, string?>
        {
            [ManagedAppServerEnvironment.ManagedFlag] = "1",
            [ManagedAppServerEnvironment.WebSocketHost] = "127.0.0.1",
            [ManagedAppServerEnvironment.WebSocketPort] = "43101",
            [ManagedAppServerEnvironment.WebSocketToken] = "ws-token"
        };

        WithEnvironment(env, () => ManagedAppServerEnvironment.ApplyTo(config));

        var appServer = config.GetSection<AppServerConfig>("AppServer");
        Assert.Equal(AppServerMode.StdioAndWebSocket, appServer.Mode);
        Assert.Equal("127.0.0.1", appServer.WebSocket.Host);
        Assert.Equal(43101, appServer.WebSocket.Port);
        Assert.Equal("ws-token", appServer.WebSocket.Token);
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
