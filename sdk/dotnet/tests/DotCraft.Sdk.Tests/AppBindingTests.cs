using DotCraft.Sdk.AppBinding;
using DotCraft.Sdk.AppServer;
using System.Text.Json;

namespace DotCraft.Sdk.Tests;

public sealed class AppBindingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void CanonicalV2Fixture_IsStable()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "specs", "protocols", "fixtures", "app-binding-v2.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            directory!.FullName, "specs", "protocols", "fixtures", "app-binding-v2.json")));
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("version").GetInt32());
        Assert.Equal(
            ["connecting", "syncing", "active", "offline", "needsConfirmation", "revoked", "failed", "cancelled"],
            root.GetProperty("states").EnumerateArray().Select(item => item.GetString()!).ToArray());
        Assert.Equal("AppBindingUpgradeRequired", root.GetProperty("errors").GetProperty("upgradeRequired").GetString());
    }

    [Fact]
    public async Task ActivateAsync_UsesBindingMcpEndpointAndBearer()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        var activateTask = client.AppBindings.ActivateAsync(
            "bind_req_1", "https://app.example/mcp", "one-time-bearer");

        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("app/binding/activate", outbound.RootElement.GetProperty("method").GetString());
            var @params = outbound.RootElement.GetProperty("params");
            Assert.Equal("bind_req_1", @params.GetProperty("bindingRequestId").GetString());
            Assert.Equal("https://app.example/mcp", @params.GetProperty("endpoint").GetString());
            Assert.Equal("one-time-bearer", @params.GetProperty("bearer").GetString());

            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    bindingId = "bind_1",
                    threadId = "thread_1",
                    appId = "com.example.board",
                    state = "active"
                }
            });
        }

        var result = await activateTask.WaitAsync(Timeout);
        Assert.Equal("bind_1", result.GetProperty("bindingId").GetString());
        Assert.Equal("active", result.GetProperty("state").GetString());
    }

    [Fact]
    public async Task SurfaceMethods_UseTypedContracts()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        var publishTask = client.AppBindings.PublishSurfaceAsync(
            "board", "http://127.0.0.1:43120/", "surface-secret");

        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("app/surface/publish", outbound.RootElement.GetProperty("method").GetString());
            var @params = outbound.RootElement.GetProperty("params");
            Assert.Equal("board", @params.GetProperty("surfaceId").GetString());
            Assert.Equal("http://127.0.0.1:43120/", @params.GetProperty("endpoint").GetString());
            Assert.Equal("surface-secret", @params.GetProperty("bearer").GetString());
            Assert.Equal(3, @params.EnumerateObject().Count());

            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = outbound.RootElement.GetProperty("id").GetInt64(),
                result = new
                {
                    appId = "com.example.board",
                    surfaceId = "board",
                    endpoint = "http://127.0.0.1:43120/",
                    bearer = "surface-secret",
                    expiresAt = "2026-07-16T12:02:00Z"
                }
            });
        }

        var published = await publishTask.WaitAsync(Timeout);
        Assert.Equal("com.example.board", published.AppId);
        Assert.Equal("board", published.SurfaceId);
        Assert.Equal("http://127.0.0.1:43120/", published.Endpoint);
        Assert.Equal("surface-secret", published.Bearer);
        Assert.Equal("2026-07-16T12:02:00Z", published.ExpiresAt);

        var resolveTask = client.AppBindings.ResolveSurfaceAsync("com.example.board", "board");
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("app/surface/resolve", outbound.RootElement.GetProperty("method").GetString());
            var @params = outbound.RootElement.GetProperty("params");
            Assert.Equal("com.example.board", @params.GetProperty("appId").GetString());
            Assert.Equal("board", @params.GetProperty("surfaceId").GetString());
            Assert.Equal(2, @params.EnumerateObject().Count());

            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = outbound.RootElement.GetProperty("id").GetInt64(),
                result = new
                {
                    appId = "com.example.board",
                    surfaceId = "board",
                    endpoint = "http://127.0.0.1:43120/",
                    bearer = "surface-secret",
                    expiresAt = "2026-07-16T12:02:00Z"
                }
            });
        }

        var resolved = await resolveTask.WaitAsync(Timeout);
        Assert.Equal(published, resolved);
    }

    [Fact]
    public async Task ListThreadBindingsAsync_ParsesBindings()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        var listTask = client.AppBindings.ListThreadBindingsAsync("thread_1");
        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("thread/appBindings/list", outbound.RootElement.GetProperty("method").GetString());
            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    bindings = new object[]
                    {
                        new { bindingId = "bind_1", threadId = "thread_1", appId = "app", state = "active", authorityRevision = 3, approvedCapabilityRevision = 2 }
                    }
                }
            });
        }

        var bindings = await listTask.WaitAsync(Timeout);
        Assert.Single(bindings);
        Assert.Equal("bind_1", bindings[0].BindingId);
        Assert.Equal(3, bindings[0].AuthorityRevision);
    }

    private static async Task<(DotCraftClient client, TestJsonRpcTransport transport)> ConnectAsync()
    {
        var transport = new TestJsonRpcTransport();
        var connectTask = DotCraftClient.ConnectAsync(transport, new DotCraftClientOptions { ClientName = "t", ClientVersion = "0.1" });
        using (var init = await transport.ReadOutboundAsync())
        {
            var id = init.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = new { serverInfo = new { name = "d", version = "1", protocolVersion = "1" }, capabilities = new { appBinding = true } }
            });
        }

        using (await transport.ReadOutboundAsync())
        {
        }

        return (await connectTask, transport);
    }

    [Fact]
    public void Handoff_Parse_ReadsConnectionFields()
    {
        var handoff = AppBindingHandoff.Parse(
            "board-example://dotcraft/bind?app=com.example.board&request=bind_req_1&token=tok&endpoint=ws%3A%2F%2F127.0.0.1%3A9100%2Fws",
            expectedScheme: "board-example",
            expectedAppId: "com.example.board");

        Assert.Equal("bind", handoff.Operation);
        Assert.Equal("bind_req_1", handoff.RequestId);
        Assert.Equal("tok", handoff.RequestToken);
        Assert.Equal("ws://127.0.0.1:9100/ws", handoff.AppServerUrl);
    }

    [Fact]
    public void Handoff_Parse_RejectsAlternateQueryNames()
    {
        Assert.Throws<FormatException>(() => AppBindingHandoff.Parse(
            "board-example://dotcraft/bind?appId=com.example.board&requestId=bind_req_1&requestToken=tok"));
    }

    [Fact]
    public void ToolError_UsesStandardShape()
    {
        var result = DotCraftAppBindingClient.ToolError(AppBindingErrorCodes.Offline, "App is offline.");

        Assert.False(result.Success);
        Assert.Equal(AppBindingErrorCodes.Offline, result.ErrorCode);
        Assert.Contains(AppBindingErrorCodes.Offline, result.ContentItems![0].Text);
    }
}
