using DotCraft.AppServer;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.AppServer;

internal static class WebSocketAppServerTestHost
{
    internal static Task<AppServerProcess> StartAsync(
        string workspace,
        string profile,
        CancellationToken ct) =>
        AppServerProcess.StartAsync(
            dotcraftBin: typeof(AppServerHost).Assembly.Location,
            workspacePath: workspace,
            listenUrl: "ws+stdio://127.0.0.1:0",
            environmentVariables: new Dictionary<string, string?>
            {
                ["HOME"] = profile,
                ["USERPROFILE"] = profile,
                ["DOTCRAFT_MANAGED_APP_SERVER"] = "0"
            },
            createNoWindow: true,
            ct: ct);

    internal static Uri ReadEndpoint(string workspace, AppServerProcess process)
    {
        var info = AppServerWorkspaceLock.TryRead(
            AppServerWorkspaceLock.GetLockFilePath(Path.Combine(workspace, ".craft")));
        Assert.NotNull(info);
        Assert.Equal(process.ProcessId, info.Pid);
        Assert.True(info.Endpoints.TryGetValue("appServerWebSocket", out var address));
        var endpoint = new Uri(address);
        Assert.Equal("ws", endpoint.Scheme);
        Assert.Equal("127.0.0.1", endpoint.Host);
        Assert.InRange(endpoint.Port, 1, 65535);
        return endpoint;
    }
}
