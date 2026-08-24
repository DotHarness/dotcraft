using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Sdk.AppBinding;
using DotCraft.Sdk;
using ContractAppBinding = DotCraft.Protocol.AppServer.AppBinding;
using Xunit;

namespace DotCraft.Sdk.Tests;

public sealed class AppBindingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset ExpiresAt = new(2026, 8, 3, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActivateAndSurfaceMethodsUseGeneratedTypedBindings()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var activate = client.AppBindings.ActivateAsync(new AppBindingActivateParams
        {
            BindingRequestId = "bind_req_1", Endpoint = "https://app.example/mcp", Bearer = "one-time-bearer"
        });
        await RespondAsync(transport, "app/binding/activate", new ContractAppBinding
        {
            BindingId = "bind_1", ThreadId = "thread_1", AppId = "com.example.board", State = "active", UpdatedAt = ExpiresAt
        });
        Assert.Equal("bind_1", (await activate.WaitAsync(Timeout)).BindingId.Value);

        var publish = client.AppBindings.PublishSurfaceAsync(new AppSurfacePublishParams
        {
            SurfaceId = "board", Endpoint = "http://127.0.0.1:43120/", Bearer = "surface-secret"
        });
        await RespondAsync(transport, "app/surface/publish", Surface());
        var published = await publish.WaitAsync(Timeout);
        Assert.Equal("com.example.board", published.AppId.Value);
        Assert.Equal(ExpiresAt, published.ExpiresAt.Value);

        var resolve = client.AppBindings.ResolveSurfaceAsync(new AppSurfaceResolveParams
        {
            AppId = "com.example.board", SurfaceId = "board"
        });
        await RespondAsync(transport, "app/surface/resolve", Surface());
        Assert.Equal("board", (await resolve.WaitAsync(Timeout)).SurfaceId.Value);
    }

    [Fact]
    public async Task ListThreadBindingsReturnsContractsResult()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;
        var list = client.AppBindings.ListThreadBindingsAsync(new ThreadAppBindingsListParams { ThreadId = "thread_1" });
        await RespondAsync(transport, "thread/appBindings/list", new ThreadAppBindingsListResult
        {
            Bindings = new List<ContractAppBinding>
            {
                new() { BindingId = "bind_1", ThreadId = "thread_1", AppId = "app", State = "active", AuthorityRevision = 3 }
            }
        });
        var result = await list.WaitAsync(Timeout);
        var binding = Assert.Single(result.Bindings.Value!);
        Assert.Equal("bind_1", binding.BindingId.Value);
        Assert.Equal(3, binding.AuthorityRevision.Value);

        var principalList = client.AppBindings.ListBindingsAsync();
        await RespondAsync(transport, "app/bindings/list", new AppBindingsListResult
        {
            Bindings = new List<ContractAppBinding>
            {
                new() { BindingId = "bind_2", ThreadId = "thread_2", AppId = "app", State = "offline", AuthorityRevision = 5 }
            }
        });
        var principalBinding = Assert.Single((await principalList.WaitAsync(Timeout)).Bindings.Value!);
        Assert.Equal("bind_2", principalBinding.BindingId.Value);
        Assert.Equal(5, principalBinding.AuthorityRevision.Value);
    }

    [Fact]
    public void HandoffAndToolErrorRetainHighLevelHelpers()
    {
        var handoff = AppBindingHandoff.Parse(
            "board-example://dotcraft/bind?app=com.example.board&request=bind_req_1&token=tok&endpoint=ws%3A%2F%2F127.0.0.1%3A9100%2Fws",
            expectedScheme: "board-example",
            expectedAppId: "com.example.board");
        Assert.Equal("bind_req_1", handoff.RequestId);
        Assert.Equal("ws://127.0.0.1:9100/ws", handoff.AppServerUrl);

        var managed = AppBindingHandoff.Parse(
            "oratorio://dotcraft/connect?app=com.dotharness.oratorio&request=req_1&token=tok&workspace=%2Fexample%2Fworkspace&identity=local%3A%2Fexample%2Fworkspace",
            expectedScheme: "oratorio",
            expectedAppId: "com.dotharness.oratorio");
        Assert.Equal("/example/workspace", managed.WorkspacePath);
        Assert.Equal("local:/example/workspace", managed.AppServerIdentity);

        var error = DotCraftAppBindingClient.ToolError(AppBindingErrorCodes.Offline, "App is offline.");
        Assert.False(error.Success);
        Assert.Equal(AppBindingErrorCodes.Offline, error.ErrorCode);
        Assert.Contains(AppBindingErrorCodes.Offline, error.ContentItems![0].Text);
    }

    private static AppSurface Surface() => new()
    {
        AppId = "com.example.board",
        SurfaceId = "board",
        Endpoint = "http://127.0.0.1:43120/",
        Bearer = "surface-secret",
        ExpiresAt = ExpiresAt
    };

    private static async Task<(DotCraftClient client, TestJsonRpcTransport transport)> ConnectAsync()
    {
        var transport = new TestJsonRpcTransport();
        var connect = DotCraftClient.ConnectAsync(transport, new DotCraftClientOptions { ClientName = "test", ClientVersion = "0.1" });
        using (var init = await transport.ReadOutboundAsync())
        {
            var id = init.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0", id, result = new InitializeResult
                {
                    ServerInfo = new ServerInfo { Name = "dotcraft", Version = "test", ProtocolVersion = "1" },
                    Capabilities = new ServerCapabilities()
                }
            });
        }
        using (await transport.ReadOutboundAsync())
        {
        }
        return (await connect, transport);
    }

    private static async Task RespondAsync(TestJsonRpcTransport transport, string method, object result)
    {
        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal(method, outbound.RootElement.GetProperty("method").GetString());
        var id = outbound.RootElement.GetProperty("id").GetInt64();
        await transport.PushInboundAsync(new { jsonrpc = "2.0", id, result });
    }
}
