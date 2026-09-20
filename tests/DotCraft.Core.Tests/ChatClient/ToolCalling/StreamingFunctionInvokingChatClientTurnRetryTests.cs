using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_WhenStreamBreaksAfterText_ReissuesFromGrownHistory()
    {
        var inner = new ScriptedStreamChatClient(
            ([Text("Let me ch")], new IOException("connection reset")),
            ([Text("ecked.")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 1));

        var updates = await CollectAsync(
            client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "status?")]));

        Assert.Equal(2, inner.Requests.Count);
        var reissued = inner.Requests[1];
        Assert.Contains(reissued, message => message.Role == ChatRole.Assistant && message.Text == "Let me ch");
        Assert.Equal("Let me checked.", Text(updates));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenStreamBreaks_DoesNotReyieldDeliveredText()
    {
        var inner = new ScriptedStreamChatClient(
            ([Text("Let me ch")], new IOException("connection reset")),
            ([Text("ecked.")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 1));

        var updates = await CollectAsync(
            client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "status?")]));

        var delivered = updates
            .SelectMany(update => update.Contents)
            .OfType<TextContent>()
            .Count(content => content.Text == "Let me ch");
        Assert.Equal(1, delivered);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenStreamBreaksAfterToolCall_DispatchesToolOnceBeforeReissue()
    {
        var invocations = 0;
        var tool = AIFunctionFactory.Create(() => "sunny", name: "GetWeather");
        var inner = new ScriptedStreamChatClient(
            ([ToolCall("call-1", "GetWeather")], new IOException("connection reset")),
            ([Text("It is sunny.")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 1))
        {
            AdditionalTools = [tool],
            FunctionInvoker = (_, _) =>
            {
                invocations++;
                return ValueTask.FromResult<object?>("sunny");
            }
        };

        var updates = await CollectAsync(
            client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "weather?")]));

        Assert.Equal(1, invocations);
        Assert.Equal(2, inner.Requests.Count);

        var reissued = inner.Requests[1];
        var callIndex = reissued.FindIndex(message =>
            message.Contents.OfType<FunctionCallContent>().Any(call => call.CallId == "call-1"));
        var resultIndex = reissued.FindIndex(message =>
            message.Contents.OfType<FunctionResultContent>().Any(result => result.CallId == "call-1"));
        Assert.True(callIndex >= 0, "The reissued request must carry the truncated tool call.");
        Assert.Equal(callIndex + 1, resultIndex);
        Assert.Equal("It is sunny.", Text(updates));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenToolCallCannotBeDispatched_ClosesThePairItself()
    {
        var inner = new ScriptedStreamChatClient(
            ([ToolCall("call-1", "HostedSearch")], new IOException("connection reset")),
            ([Text("done")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 1))
        {
            TerminateOnUnknownCalls = true
        };

        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "search")]));

        Assert.Equal(2, inner.Requests.Count);
        var reissued = inner.Requests[1];
        var result = reissued
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .SingleOrDefault(content => content.CallId == "call-1");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenReissueBudgetIsExhausted_ThrowsTheFinalFailure()
    {
        var inner = new ScriptedStreamChatClient(
            ([Text("first")], new IOException("first break")),
            ([Text("second")], new IOException("second break")));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 1));

        var failure = await Assert.ThrowsAsync<IOException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])));

        Assert.Equal("second break", failure.Message);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WithoutRetryBudget_PreservesTerminalFailure()
    {
        var inner = new ScriptedStreamChatClient(
            ([Text("partial")], new IOException("connection reset")),
            ([Text("unused")], null));
        var client = new StreamingFunctionInvokingChatClient(inner);

        await Assert.ThrowsAsync<IOException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])));

        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenCallerCancels_DoesNotReissue()
    {
        using var cancellation = new CancellationTokenSource();
        var inner = new ScriptedStreamChatClient(
            ([Text("partial")], new OperationCanceledException()),
            ([Text("unused")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 3));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CollectAsync(client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "hi")],
                cancellationToken: cancellation.Token)));

        Assert.Empty(inner.Requests);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ReadsTheBudgetThroughTheTransportRetryClient()
    {
        var inner = new ScriptedStreamChatClient(
            ([Text("Let me ch")], new IOException("connection reset")),
            ([Text("ecked.")], null));
        IChatClient transport = new StreamRetryingChatClient(
            inner,
            new StreamRetryOptions(1, TimeSpan.FromSeconds(30)));
        var client = new StreamingFunctionInvokingChatClient(new PassthroughChatClient(transport));

        var updates = await CollectAsync(
            client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "status?")]));

        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal("Let me checked.", Text(updates));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenFailureCarriesNoWireStatus_DoesNotReissue()
    {
        var inner = new ScriptedStreamChatClient(
            ([Text("partial")], new InvalidOperationException("something local went wrong")),
            ([Text("unused")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 3));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])));

        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_WhenContextOverflows_DoesNotReissue()
    {
        var inner = new ScriptedStreamChatClient(
            ([Text("partial")], new InvalidOperationException("prompt is too long: 250000 tokens > 200000 maximum")),
            ([Text("unused")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 3));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")])));

        Assert.Single(inner.Requests);
    }

    private static ChatResponseUpdate Text(string text) => new(ChatRole.Assistant, text);

    private static string Text(IEnumerable<ChatResponseUpdate> updates) =>
        string.Concat(updates.SelectMany(update => update.Contents).OfType<TextContent>()
            .Select(content => content.Text));

    private static ChatResponseUpdate ToolCall(string callId, string name) =>
        new(ChatRole.Assistant, [new FunctionCallContent(callId, name, new Dictionary<string, object?>())]);

    private sealed class PassthroughChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient);

    private sealed class RetryBudgetChatClient(IChatClient innerClient, int maxStreamRetries)
        : DelegatingChatClient(innerClient), IStreamRetryBudget
    {
        public int MaxStreamRetries => maxStreamRetries;
    }

    private sealed class ScriptedStreamChatClient(
        params (ChatResponseUpdate[] Updates, Exception? Failure)[] script) : IChatClient
    {
        public List<List<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var index = Requests.Count;
            Requests.Add([.. chatMessages]);
            var step = index < script.Length ? script[index] : script[^1];
            return StreamAsync(step.Updates, step.Failure);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
            ChatResponseUpdate[] updates,
            Exception? failure)
        {
            foreach (var update in updates)
            {
                await Task.Yield();
                yield return update;
            }

            if (failure != null)
                throw failure;
        }
    }
}
