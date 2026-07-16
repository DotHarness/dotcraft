using System.Text.Json;
using DotCraft.Sdk.AppServer;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.Tests;

public sealed class WireClientTests
{
    [Fact]
    public async Task SendRequestAsync_CorrelatesResponse()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var wire = new DotCraftWireClient(transport);
        wire.Start();

        var requestTask = wire.SendRequestAsync("thread/list", new { includeArchived = false });
        using var outbound = await transport.ReadOutboundAsync();
        var id = outbound.RootElement.GetProperty("id").GetInt64();
        Assert.Equal("thread/list", outbound.RootElement.GetProperty("method").GetString());

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id,
            result = new { data = Array.Empty<object>() }
        });

        var result = await requestTask;
        Assert.Equal(JsonValueKind.Array, result.GetProperty("data").ValueKind);
    }

    [Fact]
    public async Task ReadNotificationsAsync_YieldsServerNotifications()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var wire = new DotCraftWireClient(transport);
        wire.Start();

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            method = "turn/completed",
            @params = new { threadId = "thread_1" }
        });

        await using var enumerator = wire.ReadNotificationsAsync().GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("turn/completed", enumerator.Current.Method);
        Assert.Equal("thread_1", enumerator.Current.Params.GetProperty("threadId").GetString());
    }

    [Fact]
    public async Task ServerRequestHandler_ReturnsResultResponse()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var wire = new DotCraftWireClient(transport);
        wire.Start();
        wire.RegisterServerRequestHandler("item/tool/call", (_, _) =>
            Task.FromResult<object?>(new DynamicToolResult(true, [new ToolContentItem("text", "ok")])));

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = 7,
            method = "item/tool/call",
            @params = new { threadId = "thread_1", tool = "Echo", arguments = new { } }
        });

        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal(7, outbound.RootElement.GetProperty("id").GetInt32());
        Assert.True(outbound.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task DotCraftClient_MapsDynamicToolCallsToRegisteredHandler()
    {
        await using var transport = new TestJsonRpcTransport();
        var connectTask = DotCraftClient.ConnectAsync(transport, new DotCraftClientOptions
        {
            ClientName = "test",
            ClientVersion = "0.1"
        });

        using var init = await transport.ReadOutboundAsync();
        var initId = init.RootElement.GetProperty("id").GetInt64();
        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = initId,
            result = new
            {
                serverInfo = new { name = "dotcraft", version = "test", protocolVersion = "1" },
                capabilities = new { dynamicToolRebind = true, appBinding = true }
            }
        });
        using var initialized = await transport.ReadOutboundAsync();
        Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());

        await using var client = await connectTask;
        client.RegisterDynamicToolHandler("thread_1", "sample", "Echo", (call, _) =>
            Task.FromResult(new DynamicToolResult(
                true,
                [new ToolContentItem("text", "Echo completed")],
                StructuredContent: new { call.Tool })));

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = 99,
            method = "item/tool/call",
            @params = new
            {
                threadId = "thread_1",
                @namespace = "sample",
                tool = "Echo",
                arguments = new { message = "hello" }
            }
        });

        using var response = await transport.ReadOutboundAsync();
        Assert.True(response.RootElement.GetProperty("result").GetProperty("success").GetBoolean());
        Assert.Equal("Echo", response.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("tool").GetString());
    }

    [Fact]
    public async Task DotCraftThreadClient_ReadAsync_SendsTurnPaginationParams()
    {
        await using var transport = new TestJsonRpcTransport();
        var connectTask = DotCraftClient.ConnectAsync(transport, new DotCraftClientOptions
        {
            ClientName = "test",
            ClientVersion = "0.1"
        });

        using var init = await transport.ReadOutboundAsync();
        var initId = init.RootElement.GetProperty("id").GetInt64();
        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = initId,
            result = new
            {
                serverInfo = new { name = "dotcraft", version = "test", protocolVersion = "1" },
                capabilities = new { threadManagement = true }
            }
        });
        using var initialized = await transport.ReadOutboundAsync();
        Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());

        await using var client = await connectTask;
        var readTask = client.Threads.ReadAsync("thread_1", includeTurns: true, turnLimit: 2, cursor: "cursor-1");

        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal("thread/read", outbound.RootElement.GetProperty("method").GetString());
        var @params = outbound.RootElement.GetProperty("params");
        Assert.Equal("thread_1", @params.GetProperty("threadId").GetString());
        Assert.True(@params.GetProperty("includeTurns").GetBoolean());
        Assert.Equal(2, @params.GetProperty("turnLimit").GetInt32());
        Assert.Equal("cursor-1", @params.GetProperty("cursor").GetString());

        var id = outbound.RootElement.GetProperty("id").GetInt64();
        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id,
            result = new
            {
                thread = new { id = "thread_1", status = "active" },
                turnPage = new { totalTurns = 4, nextCursor = "cursor-2" }
            }
        });

        var result = await readTask;
        Assert.Equal("thread_1", result.ThreadId);
        Assert.NotNull(result.TurnPage);
        Assert.Equal("cursor-2", result.TurnPage.Value.GetProperty("nextCursor").GetString());
    }
}
