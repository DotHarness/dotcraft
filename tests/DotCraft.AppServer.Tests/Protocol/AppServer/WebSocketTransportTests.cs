using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class WebSocketTransportTests : IAsyncDisposable
{
    private readonly WebSocket _serverWs;
    private readonly WebSocket _clientWs;

    public WebSocketTransportTests()
    {
        (_serverWs, _clientWs) = WebSocketTestHelper.CreateWebSocketPair();
    }

    [Fact]
    public async Task Transport_WritesTextFramesReadsMultiFrameRequestsAndAnswersMalformedJson()
    {
        await using var transport = new WebSocketTransport(_serverWs);
        transport.Start();

        var message = new { jsonrpc = "2.0", method = "ping", @params = new { } };
        await transport.WriteMessageAsync(message);
        Assert.Equal(JsonSerializer.Serialize(message, SessionWireJsonOptions.Default), await ReadSingleTextFrameAsync(_clientWs));

        var bytes = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":2,"method":"chunked","params":{}}""");
        var half = bytes.Length / 2;
        await _clientWs.SendAsync(new ArraySegment<byte>(bytes, 0, half), WebSocketMessageType.Text, endOfMessage: false, CancellationToken.None);
        await _clientWs.SendAsync(new ArraySegment<byte>(bytes, half, bytes.Length - half), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        var msg = await transport.ReadMessageAsync(TimeoutCt(5));
        Assert.NotNull(msg);
        Assert.Equal("chunked", msg!.Method);
        Assert.True(msg.IsRequest);

        await SendTextFrameAsync(_clientWs, "not valid json {{{");
        using var doc = JsonDocument.Parse(await ReadSingleTextFrameAsync(_clientWs, TimeoutCt(5)));
        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(AppServerErrors.ParseErrorCode, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task SendClientRequestAsync_ClientResponds_ReturnsResponse()
    {
        await using var transport = new WebSocketTransport(_serverWs);
        transport.Start();

        var serverRequestTask = transport.SendClientRequestAsync(
            "item/approval/request",
            new { requestId = "req_001", approvalType = "shell" },
            ct: TimeoutCt(10));

        var serverRequest = await ReadSingleTextFrameAsync(_clientWs, TimeoutCt(5));
        var reqDoc = JsonDocument.Parse(serverRequest);
        var requestId = reqDoc.RootElement.GetProperty("id").GetInt32();

        var response = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = requestId,
            result = new { decision = "accept" }
        });
        await SendTextFrameAsync(_clientWs, response);

        var result = await serverRequestTask;
        Assert.NotNull(result.Result);
        Assert.Equal("accept", result.Result!.Value.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task DisposeAsync_UnblocksReadMessageAsync()
    {
        var transport = new WebSocketTransport(_serverWs);
        transport.Start();

        var readTask = transport.ReadMessageAsync(CancellationToken.None);

        await transport.DisposeAsync();

        var result = await readTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result);
    }

    public ValueTask DisposeAsync()
    {
        _clientWs.Dispose();
        _serverWs.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<string> ReadSingleTextFrameAsync(
        WebSocket ws, CancellationToken ct = default)
    {
        var buf = new byte[64 * 1024];
        int total = 0;
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(
                new ArraySegment<byte>(buf, total, buf.Length - total), ct);
            total += result.Count;
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(buf, 0, total);
    }

    private static Task SendTextFrameAsync(WebSocket ws, string text, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct);
    }

    private static CancellationToken TimeoutCt(int seconds)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        return cts.Token;
    }
}
