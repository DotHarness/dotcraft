using DotCraft.Sdk.Wire;
using Xunit;

namespace DotCraft.Sdk.Tests;

public sealed class ServerRequestLifecycleTests
{
    [Fact]
    public async Task ConcurrentInputs_AnswerInReverseOrderWithoutBlockingNotifications()
    {
        await using var transport = new TestJsonRpcTransport();
        await using var wire = new DotCraftWireClient(transport);
        var first = new TaskCompletionSource<object?>();
        var second = new TaskCompletionSource<object?>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.RegisterServerRequestHandlerRaw("mcpServer/elicitation/request", (request, _) =>
        {
            if (request.Id.GetInt64() == 1) return first.Task;
            started.TrySetResult();
            return second.Task;
        });
        wire.RegisterNotificationHandlerRaw(notification =>
        {
            if (notification.Method == "fixture/progress") notified.TrySetResult();
        });
        wire.Start();
        await transport.PushInboundAsync(Input(1, "A"));
        await transport.PushInboundAsync(Input(2, "B"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await transport.PushInboundAsync(new { method = "fixture/progress", @params = new { } });
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        second.SetResult(new { action = "decline" });
        using (var response = await transport.ReadOutboundAsync())
            Assert.Equal(2, response.RootElement.GetProperty("id").GetInt64());
        first.SetResult(new { action = "accept" });
        using (var response = await transport.ReadOutboundAsync())
            Assert.Equal(1, response.RootElement.GetProperty("id").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolved_CancelsOnlyMatchingHandlerAndSuppressesLateSuccessOrError(bool fail)
    {
        await using var transport = new TestJsonRpcTransport();
        await using var wire = new DotCraftWireClient(transport);
        var late = new TaskCompletionSource<object?>();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.RegisterServerRequestHandlerRaw("mcpServer/elicitation/request", (request, token) =>
        {
            if (request.Id.GetInt64() != 1)
            {
                Assert.False(token.IsCancellationRequested);
                return Task.FromResult<object?>(new { action = "decline" });
            }
            token.Register(() => cancelled.TrySetResult());
            started.TrySetResult(token);
            return late.Task;
        });
        wire.Start();
        await transport.PushInboundAsync(Input(1));
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await transport.PushInboundAsync(new
        {
            method = "serverRequest/resolved", @params = new { threadId = "A", requestId = 1 }
        });
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(token.IsCancellationRequested);
        if (fail) late.SetException(new InvalidOperationException("late failure"));
        else late.SetResult(new { action = "accept" });
        await transport.PushInboundAsync(Input(2));
        using var response = await transport.ReadOutboundAsync();
        Assert.Equal(2, response.RootElement.GetProperty("id").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconnect_CancelsOldHandlerAndNeverSendsItsReplyOnNewConnection(bool fail)
    {
        await using var transport = new TestJsonRpcTransport();
        await using var wire = new DotCraftWireClient(transport, new DotCraftWireClientOptions
        {
            AutoReconnect = true,
            ReconnectInitialDelay = TimeSpan.Zero,
            ReconnectMaxDelay = TimeSpan.Zero
        });
        var late = new TaskCompletionSource<object?>();
        var replacement = new TaskCompletionSource<object?>();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        wire.StateChanged += (state, _) =>
        {
            if (state == WireConnectionState.Disconnected) disconnected.TrySetResult();
        };
        wire.RegisterServerRequestHandlerRaw("mcpServer/elicitation/request", (request, token) =>
        {
            if (request.Id.GetInt64() != 1)
                return Task.FromResult<object?>(new { action = "decline" });
            if (first)
            {
                first = false;
                started.TrySetResult(token);
                return late.Task;
            }
            replacementStarted.TrySetResult(token);
            return replacement.Task;
        });
        wire.Start();
        var initialize = wire.InitializeAsync(new DotCraftClientOptions { ClientName = "test", ClientVersion = "1" });
        await CompleteHandshakeAsync(transport);
        await initialize;
        await transport.PushInboundAsync(Input(1));
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await transport.PushDisconnectAsync();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(token.IsCancellationRequested);
        await CompleteHandshakeAsync(transport);
        await transport.PushInboundAsync(Input(1));
        var replacementToken = await replacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (fail) late.SetException(new InvalidOperationException("late failure"));
        else late.SetResult(new { action = "accept" });
        var replacementCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = replacementToken.Register(() => replacementCancelled.TrySetResult());
        await transport.PushInboundAsync(new
        {
            method = "serverRequest/resolved", @params = new { threadId = "A", requestId = 1 }
        });
        await replacementCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        replacement.SetResult(new { action = "accept" });
        await transport.PushInboundAsync(Input(2));
        using var response = await transport.ReadOutboundAsync();
        Assert.Equal(2, response.RootElement.GetProperty("id").GetInt64());
        Assert.Equal(1, transport.ReconnectCount);
    }

    private static object Input(long id, string threadId = "A") => new
    {
        id, method = "mcpServer/elicitation/request",
        @params = new { threadId, serverName = "fixture", mode = "form" }
    };

    private static async Task CompleteHandshakeAsync(TestJsonRpcTransport transport)
    {
        using var initialize = await transport.ReadOutboundAsync();
        Assert.Equal("initialize", initialize.RootElement.GetProperty("method").GetString());
        await transport.PushInboundAsync(new
        {
            id = initialize.RootElement.GetProperty("id").GetInt64(),
            result = new { serverInfo = new { name = "fixture", version = "1", protocolVersion = "1" }, capabilities = new { } }
        });
        using var initialized = await transport.ReadOutboundAsync();
        Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());
    }
}
