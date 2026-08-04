using DotCraft.Protocol.AppServer;
using DotCraft.Sdk.Wire;
using Xunit;

namespace DotCraft.Sdk.Tests;

public sealed class GeneratedRpcBindingTests
{
    [Fact]
    public async Task TypedRequest_SerializesContractParams_AndDeserializesResult()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var client = new DotCraftWireClient(transport);
        client.Start();

        var pending = client.ThreadListAsync(new ThreadListParams
        {
            Identity = new SessionIdentity { ChannelName = "fixture", UserId = "user-1" },
            IncludeArchived = false
        });
        using var outbound = await transport.ReadOutboundAsync();
        Assert.Equal("thread/list", outbound.RootElement.GetProperty("method").GetString());
        Assert.Equal("fixture", outbound.RootElement.GetProperty("params").GetProperty("identity").GetProperty("channelName").GetString());
        Assert.False(outbound.RootElement.GetProperty("params").GetProperty("includeArchived").GetBoolean());

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            id = outbound.RootElement.GetProperty("id").GetInt64(),
            result = new { data = Array.Empty<object>(), totalMatched = 0 }
        });

        var result = await pending;
        Assert.Empty(result.Data);
        Assert.Equal(0, result.TotalMatched);
    }

    [Fact]
    public async Task TypedNotificationHandler_FiltersAndDeserializesItsMethod()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var client = new DotCraftWireClient(transport);
        client.Start();
        ThreadDeletedNotification? received = null;
        using var registration = client.RegisterThreadDeletedHandler(value => received = value);

        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            method = "thread/updated",
            @params = new { thread = new { id = "ignored" } }
        });
        await transport.PushInboundAsync(new
        {
            jsonrpc = "2.0",
            method = "thread/deleted",
            @params = new { threadId = "thread-1" }
        });

        await Task.Delay(50);
        Assert.Equal("thread-1", received?.ThreadId);
    }
}
