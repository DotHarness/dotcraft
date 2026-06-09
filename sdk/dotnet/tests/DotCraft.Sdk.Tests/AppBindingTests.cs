using DotCraft.Sdk.AppBinding;
using DotCraft.Sdk.AppServer;

namespace DotCraft.Sdk.Tests;

public sealed class AppBindingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task AcceptBindingAsync_SendsTypedRequestAndParsesBinding()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        var acceptTask = client.AppBindings.AcceptBindingAsync(new AcceptBindingRequest(
            BindingRequestId: "bind_req_1",
            RequestToken: "tok",
            GrantId: "grant_1",
            GrantedScopes: ["board.read"],
            ApprovalMode: "appAccepted",
            ApprovedBy: "alice"));

        using (var outbound = await transport.ReadOutboundAsync())
        {
            Assert.Equal("app/binding/accept", outbound.RootElement.GetProperty("method").GetString());
            var @params = outbound.RootElement.GetProperty("params");
            Assert.Equal("bind_req_1", @params.GetProperty("bindingRequestId").GetString());
            Assert.Equal("appAccepted", @params.GetProperty("approvalMode").GetString());
            Assert.Equal("board.read", @params.GetProperty("grantedScopes")[0].GetString());

            var id = outbound.RootElement.GetProperty("id").GetInt64();
            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    binding = new
                    {
                        bindingId = "bind_1",
                        threadId = "thread_1",
                        appId = "com.dotharness.oratorio",
                        state = "active",
                        grantedScopes = new[] { "board.read" },
                        attachedToolCount = 0
                    }
                }
            });
        }

        var result = await acceptTask.WaitAsync(Timeout);
        Assert.Equal("bind_1", result.Binding.BindingId);
        Assert.Equal("active", result.Binding.State);
        Assert.Equal("board.read", result.Binding.GrantedScopes[0]);
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
                        new { bindingId = "bind_1", threadId = "thread_1", appId = "app", state = "active", grantedScopes = new[] { "board.read" }, attachedToolCount = 2 }
                    }
                }
            });
        }

        var bindings = await listTask.WaitAsync(Timeout);
        Assert.Single(bindings);
        Assert.Equal("bind_1", bindings[0].BindingId);
        Assert.Equal(2, bindings[0].AttachedToolCount);
    }

    [Fact]
    public async Task ResourceHandler_RespondsToServerResourceRead()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        using var registration = client.RegisterResourceHandler((request, _) =>
            Task.FromResult(new ResourceReadResult(new[]
            {
                new ResourceContent(request.Uri, "text/html;profile=mcp-app", "<!doctype html><body>board</body>")
            })));

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = 99,
            method = "item/resource/read",
            @params = new { threadId = "thread_1", @namespace = "oratorio", uri = "ui://oratorio/board" }
        });

        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal(99, outbound.RootElement.GetProperty("id").GetInt64());
        var contents = outbound.RootElement.GetProperty("result").GetProperty("contents");
        Assert.Equal("ui://oratorio/board", contents[0].GetProperty("uri").GetString());
        Assert.Equal("text/html;profile=mcp-app", contents[0].GetProperty("mimeType").GetString());
        Assert.Contains("board", contents[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ResourceRead_WithoutHandler_ReturnsEmptyContents()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = 100,
            method = "item/resource/read",
            @params = new { threadId = "thread_1", uri = "ui://oratorio/board" }
        });

        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal(100, outbound.RootElement.GetProperty("id").GetInt64());
        Assert.Empty(outbound.RootElement.GetProperty("result").GetProperty("contents").EnumerateArray());
    }

    [Fact]
    public async Task ServeStaticUiResources_ServesFolderFilesByUri()
    {
        var (client, transport) = await ConnectAsync();
        await using var _ = client;

        var folder = Path.Combine(Path.GetTempPath(), "ui_static_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "board.html"), "<!doctype html><body>board folder</body>");
            using var registration = client.ServeStaticUiResources("ui://oratorio", folder);

            await transport.PushInboundAsync(new
            {
                jsonrpc = "2.0",
                id = 101,
                method = "item/resource/read",
                @params = new { threadId = "thread_1", @namespace = "oratorio", uri = "ui://oratorio/board.html" }
            });

            using var outbound = await transport.ReadOutboundAsync();
            Assert.Equal(101, outbound.RootElement.GetProperty("id").GetInt64());
            var contents = outbound.RootElement.GetProperty("result").GetProperty("contents");
            Assert.Equal("ui://oratorio/board.html", contents[0].GetProperty("uri").GetString());
            Assert.Equal("text/html;profile=mcp-app", contents[0].GetProperty("mimeType").GetString());
            Assert.Contains("board folder", contents[0].GetProperty("text").GetString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
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
            "oratorio://dotcraft/bind?app=com.dotharness.oratorio&request=bind_req_1&token=tok&endpoint=ws%3A%2F%2F127.0.0.1%3A9100%2Fws",
            expectedScheme: "oratorio",
            expectedAppId: "com.dotharness.oratorio");

        Assert.Equal("bind", handoff.Operation);
        Assert.Equal("bind_req_1", handoff.RequestId);
        Assert.Equal("tok", handoff.RequestToken);
        Assert.Equal("ws://127.0.0.1:9100/ws", handoff.AppServerUrl);
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
