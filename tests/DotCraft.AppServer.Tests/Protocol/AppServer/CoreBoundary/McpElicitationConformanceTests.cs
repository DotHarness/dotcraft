using System.Text.Json;
using System.Threading.Channels;
using DotCraft.AppServer;
using DotCraft.Mcp;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpElicitationConformanceTests
{
    [Theory]
    [InlineData("accept")]
    [InlineData("decline")]
    [InlineData("cancel")]
    public async Task FormAnswer_UsesOriginalThreadTurnAndRequestId(string action)
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var peer = new Peer(subscribers, "thread-a");
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var answer = handler.HandleAsync(Context("thread-a", "turn-a"), "reviews", FormRequest(), default).AsTask();
        var prompt = await peer.ReadPromptAsync();
        Assert.Equal("thread-a", prompt.Parameters.GetProperty("threadId").GetString());
        Assert.Equal("turn-a", prompt.Parameters.GetProperty("turnId").GetString());
        prompt.Answer(action, action == "accept" ? new { project = "sample-project" } : null);
        var result = await answer.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(action, result.Action);
        if (action == "accept")
            Assert.Equal("sample-project", result.Content!["project"].GetString());
        else
            Assert.Null(result.Content);
        await peer.AssertResolvedAsync(prompt.Id, "thread-a");
    }

    [Fact]
    public async Task ParallelThreads_ReverseAnswersStayWithTheirInvocation()
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var peer = new Peer(subscribers, "thread-a", "thread-b");
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var a = handler.HandleAsync(Context("thread-a", "turn-a"), "reviews", FormRequest(), default).AsTask();
        var b = handler.HandleAsync(Context("thread-b", null), "reviews", FormRequest(), default).AsTask();
        var first = await peer.ReadPromptAsync();
        var second = await peer.ReadPromptAsync();
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("thread-a", first.Parameters.GetProperty("threadId").GetString());
        Assert.Equal("thread-b", second.Parameters.GetProperty("threadId").GetString());
        Assert.True(!second.Parameters.TryGetProperty("turnId", out var turn) || turn.ValueKind == JsonValueKind.Null);
        second.Answer("accept", new { project = "b" });
        Assert.Equal("b", (await b.WaitAsync(TimeSpan.FromSeconds(10))).Content!["project"].GetString());
        Assert.False(a.IsCompleted);
        await peer.AssertResolvedAsync(second.Id, "thread-b");
        first.Answer("accept", new { project = "a" });
        Assert.Equal("a", (await a.WaitAsync(TimeSpan.FromSeconds(10))).Content!["project"].GetString());
        await peer.AssertResolvedAsync(first.Id, "thread-a");
    }

    [Fact]
    public async Task InvalidRecipientAnswer_DoesNotResolveAnotherRecipientsPrompt()
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var first = new Peer(subscribers, "thread-a");
        await using var second = new Peer(subscribers, "thread-a");
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var answer = handler.HandleAsync(Context("thread-a", "turn-a"), "reviews", FormRequest(), default).AsTask();
        var invalid = await first.ReadPromptAsync();
        var valid = await second.ReadPromptAsync();
        invalid.Answer("accept", new { unexpected = true });
        await first.AssertResolvedAsync(invalid.Id, "thread-a");
        Assert.False(answer.IsCompleted);
        valid.Answer("accept", new { project = "accepted" });
        Assert.Equal("accepted", (await answer.WaitAsync(TimeSpan.FromSeconds(10))).Content!["project"].GetString());
        await second.AssertResolvedAsync(valid.Id, "thread-a");
    }

    [Fact]
    public async Task FirstValidAnswer_WithdrawsOtherRecipients()
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var first = new Peer(subscribers, "thread-a");
        await using var second = new Peer(subscribers, "thread-a");
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var answer = handler.HandleAsync(Context("thread-a", "turn-a"), "reviews", FormRequest(), default).AsTask();
        var a = await first.ReadPromptAsync();
        var b = await second.ReadPromptAsync();
        b.Answer("decline");
        Assert.Equal("decline", (await answer.WaitAsync(TimeSpan.FromSeconds(10))).Action);
        await first.AssertResolvedAsync(a.Id, "thread-a");
        await second.AssertResolvedAsync(b.Id, "thread-a");
    }

    [Fact]
    public async Task CancelOneInvocation_WithdrawsOnlyItsInput()
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var peer = new Peer(subscribers, "thread-a");
        var handler = new AppServerMcpElicitationHandler(subscribers);
        using var cancelA = new CancellationTokenSource();
        var a = handler.HandleAsync(Context("thread-a", "turn-a"), "reviews", FormRequest(), cancelA.Token).AsTask();
        var b = handler.HandleAsync(Context("thread-a", "turn-a"), "reviews", FormRequest(), default).AsTask();
        var first = await peer.ReadPromptAsync();
        var second = await peer.ReadPromptAsync();
        await cancelA.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.WaitAsync(TimeSpan.FromSeconds(10)));
        await peer.AssertResolvedAsync(first.Id, "thread-a");
        Assert.False(b.IsCompleted);
        second.Answer("accept", new { project = "still-running" });
        Assert.Equal("accept", (await b.WaitAsync(TimeSpan.FromSeconds(10))).Action);
        await peer.AssertResolvedAsync(second.Id, "thread-a");
    }

    [Fact]
    public async Task DisconnectOneRecipient_KeepsAnotherRecipientEligible()
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var first = new Peer(subscribers, "thread-a");
        await using var second = new Peer(subscribers, "thread-a");
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var answer = handler.HandleAsync(Context("thread-a", "turn-a"), "reviews", FormRequest(), default).AsTask();
        await first.ReadPromptAsync();
        var valid = await second.ReadPromptAsync();
        first.Connection.MarkClosed();
        valid.Answer("accept", new { project = "connected" });
        Assert.Equal("accept", (await answer.WaitAsync(TimeSpan.FromSeconds(10))).Action);
        await second.AssertResolvedAsync(valid.Id, "thread-a");
    }

    [Fact]
    public async Task LosingAllRecipients_IsUnavailableNotUserDecline()
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var peer = new Peer(subscribers, "thread-a");
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var answer = handler.HandleAsync(Context("thread-a", "turn-a"), "reviews", FormRequest(), default).AsTask();
        await peer.ReadPromptAsync();
        peer.Connection.MarkClosed();
        var error = await Assert.ThrowsAsync<McpElicitationUnavailableException>(() => answer.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("disconnected", error.Reason);
    }

    [Fact]
    public async Task WrongThreadAndIncapableConnections_DoNotReceiveInput()
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var other = new Peer(subscribers, "thread-b");
        await using var incapable = new Peer(subscribers, false, "thread-a");
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var error = await Assert.ThrowsAsync<McpElicitationUnavailableException>(async () =>
            await handler.HandleAsync(Context("thread-a", "turn-a"), "reviews", FormRequest(), default));
        Assert.Equal("no_client", error.Reason);
        Assert.Null(other.Transport.TryReadSent());
        Assert.Null(incapable.Transport.TryReadSent());
    }

    [Theory]
    [InlineData("https://example.com/verify", true)]
    [InlineData("http://localhost/verify", true)]
    [InlineData("http://example.com/verify", false)]
    [InlineData("file:///secret", false)]
    public async Task UrlInput_OnlySupportedUrlsReachClient(string url, bool supported)
    {
        var subscribers = new AppServerThreadSubscribers();
        await using var peer = new Peer(subscribers, "thread-a");
        var handler = new AppServerMcpElicitationHandler(subscribers);
        var answer = handler.HandleAsync(Context("thread-a", null), "reviews",
            new ElicitRequestParams { Mode = "url", Url = url, ElicitationId = "verify", Message = "Verify" }, default).AsTask();
        if (supported)
        {
            var prompt = await peer.ReadPromptAsync();
            Assert.Equal(url, prompt.Parameters.GetProperty("url").GetString());
            prompt.Answer("accept");
            Assert.Equal("accept", (await answer.WaitAsync(TimeSpan.FromSeconds(10))).Action);
            await peer.AssertResolvedAsync(prompt.Id, "thread-a");
        }
        else
        {
            Assert.Equal("decline", (await answer).Action);
            Assert.Null(peer.Transport.TryReadSent());
        }
    }

    private static ToolInvocationContext Context(string thread, string? turn) => new(
        thread, turn, Guid.NewGuid().ToString("N"), ToolInvocationAudience.Model,
        new ToolName("reviews", "create"),
        new ToolDefinitionId(ToolSourceKind.Mcp, "reviews", new SourceToolId("create")),
        new RuntimeBindingId("reviews:1"), 1, DateTimeOffset.UtcNow);

    private static ElicitRequestParams FormRequest() => new()
    {
        Mode = "form", Message = "Choose a project",
        RequestedSchema = JsonSerializer.Deserialize<ElicitRequestParams.RequestSchema>("""
            { "type": "object", "properties": { "project": { "type": "string" } }, "required": ["project"] }
            """, SessionWireJsonOptions.Default)!
    };

    private sealed record Prompt(int Id, JsonElement Parameters, TaskCompletionSource<AppServerIncomingMessage> Response)
    {
        public void Answer(string action, object? content = null) =>
            Response.TrySetResult(InMemoryTransport.BuildClientResponse(Id, new { action, content }));
    }

    private sealed class Peer : IAsyncDisposable
    {
        private readonly Channel<TaskCompletionSource<AppServerIncomingMessage>> _responses =
            Channel.CreateUnbounded<TaskCompletionSource<AppServerIncomingMessage>>();
        public InMemoryTransport Transport { get; } = new();
        public AppServerConnection Connection { get; } = new();

        public Peer(AppServerThreadSubscribers subscribers, params string[] threads) : this(subscribers, true, threads) { }

        public Peer(AppServerThreadSubscribers subscribers, bool capable, params string[] threads)
        {
            Connection.TryMarkInitialized(new ClientConnectionInfo { Name = "test", Version = "1" },
                new ClientConnectionCapabilities { McpElicitation = capable });
            Connection.MarkClientReady();
            foreach (var thread in threads)
                Connection.TryAddSubscription(thread, new CancellationTokenSource());
            subscribers.Register(Transport, Connection);
            Transport.ApprovalHandlerAsync = (_, _) =>
            {
                var response = new TaskCompletionSource<AppServerIncomingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                _responses.Writer.TryWrite(response);
                return response.Task;
            };
        }

        public async Task<Prompt> ReadPromptAsync()
        {
            using var message = await Transport.ReadNextSentAsync();
            Assert.Equal("mcpServer/elicitation/request", message.RootElement.GetProperty("method").GetString());
            var response = await _responses.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            return new Prompt(message.RootElement.GetProperty("id").GetInt32(),
                message.RootElement.GetProperty("params").Clone(), response);
        }

        public async Task AssertResolvedAsync(int id, string thread)
        {
            using var message = await Transport.ReadNextSentAsync();
            Assert.Equal("serverRequest/resolved", message.RootElement.GetProperty("method").GetString());
            Assert.Equal(id, message.RootElement.GetProperty("params").GetProperty("requestId").GetInt32());
            Assert.Equal(thread, message.RootElement.GetProperty("params").GetProperty("threadId").GetString());
        }

        public async ValueTask DisposeAsync()
        {
            Connection.MarkClosed();
            await Transport.DisposeAsync();
        }
    }
}
