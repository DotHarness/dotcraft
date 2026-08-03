using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.AppServer;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Unit tests for <see cref="WebSocketTransport"/>.
/// Uses <see cref="WebSocketTestHelper.CreateWebSocketPair"/> to create an in-process
/// WebSocket pair (no network) for fast, isolated tests.
/// </summary>
public sealed class WebSocketTransportTests : IAsyncDisposable
{
    private readonly WebSocket _serverWs;
    private readonly WebSocket _clientWs;

    public WebSocketTransportTests()
    {
        (_serverWs, _clientWs) = WebSocketTestHelper.CreateWebSocketPair();
    }

    // -------------------------------------------------------------------------
    // WriteMessageAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WriteMessageAsync_SendsTextFrame_ReceivedByClient()
    {
        await using var transport = new WebSocketTransport(_serverWs);
        transport.Start();

        var message = new { jsonrpc = "2.0", method = "ping", @params = new { } };
        await transport.WriteMessageAsync(message);

        // Read the frame on the raw client WebSocket
        var received = await ReadSingleTextFrameAsync(_clientWs);
        Assert.Equal(JsonSerializer.Serialize(message, SessionWireJsonOptions.Default), received);
        var doc = JsonDocument.Parse(received);
        Assert.Equal("ping", doc.RootElement.GetProperty("method").GetString());
    }

    // -------------------------------------------------------------------------
    // ReadMessageAsync (client-initiated request)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReadMessageAsync_ClientSendsRequest_TransportDequeuesIt()
    {
        await using var transport = new WebSocketTransport(_serverWs);
        transport.Start();

        var json = """{"jsonrpc":"2.0","id":1,"method":"test/method","params":{}}""";
        await SendTextFrameAsync(_clientWs, json);

        var msg = await transport.ReadMessageAsync(TimeoutCt(5));
        Assert.NotNull(msg);
        Assert.Equal("test/method", msg!.Method);
        Assert.True(msg.IsRequest);
    }

    // -------------------------------------------------------------------------
    // Multi-frame message accumulation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReadMessageAsync_MultiFrameMessage_AccumulatesCorrectly()
    {
        await using var transport = new WebSocketTransport(_serverWs);
        transport.Start();

        // Send the JSON payload split across two frames (endOfMessage = false on the first)
        var payload = """{"jsonrpc":"2.0","id":2,"method":"chunked","params":{}}""";
        var bytes = Encoding.UTF8.GetBytes(payload);
        var half = bytes.Length / 2;

        // First frame: not end of message
        await _clientWs.SendAsync(
            new ArraySegment<byte>(bytes, 0, half),
            WebSocketMessageType.Text,
            endOfMessage: false,
            CancellationToken.None);

        // Second frame: end of message
        await _clientWs.SendAsync(
            new ArraySegment<byte>(bytes, half, bytes.Length - half),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

        var msg = await transport.ReadMessageAsync(TimeoutCt(5));
        Assert.NotNull(msg);
        Assert.Equal("chunked", msg!.Method);
    }

    // -------------------------------------------------------------------------
    // Parse error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReadMessageAsync_MalformedJson_SendsParseErrorResponse()
    {
        await using var transport = new WebSocketTransport(_serverWs);
        transport.Start();

        // Send invalid JSON
        await SendTextFrameAsync(_clientWs, "not valid json {{{");

        // The transport should auto-send a parse error response
        var errorFrame = await ReadSingleTextFrameAsync(_clientWs, TimeoutCt(5));
        var doc = JsonDocument.Parse(errorFrame);
        var root = doc.RootElement;
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.True(root.TryGetProperty("error", out var error));
        Assert.Equal(AppServerErrors.ParseErrorCode, error.GetProperty("code").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Bidirectional: SendClientRequestAsync (approval flow pattern)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendClientRequestAsync_ClientResponds_ReturnsResponse()
    {
        await using var transport = new WebSocketTransport(_serverWs);
        transport.Start();

        // Server sends request to client; client responds concurrently
        var serverRequestTask = transport.SendClientRequestAsync(
            "item/approval/request",
            new { requestId = "req_001", approvalType = "shell" },
            ct: TimeoutCt(10));

        // Simulate the client reading the server's request and responding
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

        // The SendClientRequestAsync should now complete
        var result = await serverRequestTask;
        Assert.NotNull(result.Result);
        Assert.Equal("accept", result.Result!.Value.GetProperty("decision").GetString());
    }

    // -------------------------------------------------------------------------
    // Dispose
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsync_UnblocksReadMessageAsync()
    {
        var transport = new WebSocketTransport(_serverWs);
        transport.Start();

        // Start a pending read — it should unblock when transport is disposed
        var readTask = transport.ReadMessageAsync(CancellationToken.None);

        await transport.DisposeAsync();

        var result = await readTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public ValueTask DisposeAsync()
    {
        // Just dispose both WebSocket instances; CloseOutputAsync (not CloseAsync) was
        // used by the transport so no bidirectional handshake is expected here.
        _clientWs.Dispose();
        _serverWs.Dispose();
        return ValueTask.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

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
