using DotCraft.Mcp;
using DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpOAuthBoundaryConformanceTests
{
    [Fact]
    public async Task PublicHttpServer_IsUnsupportedForOAuth_AndLoginIsRejectedWithoutNotification()
    {
        await using var manager = PublicHttpManager();
        await manager.ConnectAsync([PublicServer()]);
        await manager.WaitForStartupCompletionAsync();
        using var harness = new AppServerTestHarness(mcpClientManager: manager);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.McpServerStatusList,
            new { detail = "toolsAndAuthOnly" }));
        using var statusResponse = harness.Transport.TryReadSent()!;
        var status = statusResponse.RootElement.GetProperty("result").GetProperty("data")[0];
        Assert.Equal("unsupported", status.GetProperty("authStatus").GetString());
        Assert.Equal("notRequired", status.GetProperty("authState").GetString());
        Assert.Empty(status.GetProperty("resources").EnumerateArray());
        Assert.Empty(status.GetProperty("resourceTemplates").EnumerateArray());

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.McpServerOAuthLogin,
            new { name = "public-http" }));
        using var loginResponse = harness.Transport.TryReadSent()!;
        var error = loginResponse.RootElement.GetProperty("error");
        Assert.Equal(AppServerErrors.InvalidRequestCode, error.GetProperty("code").GetInt32());
        Assert.Contains(
            "does not currently require OAuth authentication",
            error.GetProperty("data").GetProperty("detail").GetString());
        Assert.Null(harness.Transport.TryReadSent());
    }

    private static McpClientManager PublicHttpManager() => new((_, _) => Task.FromResult(
        new McpConnectionResult(new FakeClient(), [], AuthStatus: "unsupported")));

    private static McpServerConfig PublicServer() => new()
    {
        Name = "public-http",
        Enabled = true,
        Transport = "streamableHttp",
        Url = "https://public.example.test/mcp"
    };

    private sealed class FakeClient : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
