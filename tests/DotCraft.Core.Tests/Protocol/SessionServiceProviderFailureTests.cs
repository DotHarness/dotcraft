using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Context.Compaction;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceProviderFailureTests : SessionServiceRuntimeSignalTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidRequest_PreservesProviderReasonAcrossEventsAndReloadWithoutRetry(bool sdkIncludesBody)
    {
        var reason = sdkIncludesBody
            ? "context_length_exceeded and slow_down are unsupported parameters."
            : "This model does not support the requested parameter.";
        var inner = new ThrowingThenUpdatesChatClient(
            Rejection("invalid_prompt", reason, sdkIncludesBody),
            [new ChatResponseUpdate(ChatRole.Assistant, "unexpected retry")]);
        using var client = WithProviderFailures(inner);
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Equal(1, inner.Calls);
        Assert.DoesNotContain(events, evt => IsSystemEvent(evt, "streamError") || IsSystemEvent(evt, "compacting"));
        var turn = (await service.GetThreadAsync(thread.Id)).Turns.Single();
        Assert.Equal(reason, turn.Error);
        Assert.Equal("badRequest", turn.ProviderError);
        Assert.Equal(400, turn.HttpStatus);
        await AssertFailureSurvivesReloadAsync(thread.Id, events, turn.Error!);
    }

    [Fact]
    public async Task ExhaustedRetries_RetainsSafeProviderReasonInTerminalFailure()
    {
        const string reason = "The service could not process this request.";
        var body = JsonSerializer.Serialize(new
        {
            error = new { code = "server_error", message = reason },
            privateData = "not for display"
        });
        var inner = new ThrowingThenUpdatesChatClient(
            new ClientResultException(body, new ErrorResponse(body, 500)), []);
        using var client = WithProviderFailures(inner, maxRetries: 0);
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Equal(1, inner.Calls);
        var turn = (await service.GetThreadAsync(thread.Id)).Turns.Single();
        Assert.Equal(reason, turn.Error);
        Assert.Equal("responseTooManyFailedAttempts", turn.ProviderError);
        Assert.Equal(500, turn.HttpStatus);
        await AssertFailureSurvivesReloadAsync(thread.Id, events, reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextOverflow_OnlyInRawResponse_UsesReactiveCompactionWithoutResending(bool compactionFails)
    {
        var thread = await SeedCompactableThreadAsync(forceHighEstimate: false);
        var error = Rejection("context_length_exceeded", "The input exceeds the permitted size.");
        Assert.False(CompactionErrors.IsPromptTooLong(error));
        var inner = new ThrowingThenUpdatesChatClient(error,
            [new ChatResponseUpdate(ChatRole.Assistant, "unexpected retry")]);
        using var client = WithProviderFailures(inner);
        IChatClient summarizer = compactionFails
            ? new ThrowingChatClient(new InvalidOperationException("summary unavailable"))
            : new SummaryChatClient("<summary>compacted previous context</summary>");
        await using var factory = CreateAgentFactory(client,
            configureConfig: ConfigureSmallCompaction, compactionChatClient: summarizer);
        var service = CreateService(factory, client, useStreamingFunctionInvoker: true);
        await service.ResumeThreadAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("continue")]));

        Assert.Equal(1, inner.Calls);
        Assert.Contains(events, evt => IsSystemEvent(evt, "compacting"));
        Assert.Contains(events, evt => IsSystemEvent(evt, compactionFails ? "compactFailed" : "compacted"));
        Assert.DoesNotContain(events, evt => IsSystemEvent(evt, "streamError") || evt.EventType == SessionEventType.TurnCompleted);
        var turn = (await service.GetThreadAsync(thread.Id)).Turns.Last();
        Assert.Equal("contextWindowExceeded", turn.ProviderError);
        Assert.Equal(400, turn.HttpStatus);
        if (compactionFails)
            Assert.Contains("The input exceeds the permitted size.", turn.Error);
        else
            Assert.Contains("History has been compacted", turn.Error);
        await AssertFailureSurvivesReloadAsync(thread.Id, events, turn.Error!);
    }

    private async Task AssertFailureSurvivesReloadAsync(string threadId, List<SessionEvent> events, string message)
    {
        var failed = Assert.Single(events, evt => evt.EventType == SessionEventType.TurnFailed).TurnFailedPayload!;
        Assert.Equal(message, failed.Error);
        Assert.Equal(message, failed.Turn.Error);
        var item = Assert.Single(events, evt => evt.EventType == SessionEventType.ItemCompleted
            && evt.ItemPayload?.Type == ItemType.Error).ItemPayload!;
        Assert.Equal(message, Assert.IsType<ErrorPayload>(item.Payload).Message);
        var reloaded = Assert.IsType<SessionThread>(await new ThreadStore(_tempDir).LoadThreadAsync(threadId));
        var persisted = reloaded.Turns.Last();
        Assert.Equal(TurnStatus.Failed, persisted.Status);
        Assert.Equal(message, persisted.Error);
        Assert.Equal(failed.Turn.ProviderError, persisted.ProviderError);
        Assert.Equal(failed.Turn.HttpStatus, persisted.HttpStatus);
        var error = Assert.Single(persisted.Items, candidate => candidate.Type == ItemType.Error);
        Assert.Equal(message, Assert.IsType<ErrorPayload>(error.Payload).Message);
    }

    private static IChatClient WithProviderFailures(IChatClient inner, int maxRetries = 2) => new StreamRetryingChatClient(
        new ProviderServiceChatClient(inner, new Dictionary<Type, object>
        {
            [typeof(IProviderFailureClassifier)] = new OpenAIProviderFailureClassifier(false)
        }),
        new StreamRetryOptions(maxRetries, TimeSpan.FromSeconds(30)));

    private static ClientResultException Rejection(string code, string message, bool sdkIncludesBody = false)
    {
        var body = JsonSerializer.Serialize(new { error = new { code, message } });
        return new ClientResultException(
            sdkIncludesBody ? body : "Service request failed. Status: 400 (Bad Request)",
            new ErrorResponse(body));
    }

    private sealed class ErrorResponse(string body, int status = 400) : PipelineResponse
    {
        private readonly BinaryData _content = BinaryData.FromString(body);
        public override int Status => status;
        public override string ReasonPhrase => string.Empty;
        public override BinaryData Content => _content;
        public override Stream? ContentStream { get; set; }
        protected override PipelineResponseHeaders HeadersCore { get; } = new EmptyHeaders();
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => _content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_content);
        public override void Dispose() { }
    }

    private sealed class EmptyHeaders : PipelineResponseHeaders
    {
        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
            Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
        public override bool TryGetValue(string name, out string? value) { value = null; return false; }
        public override bool TryGetValues(string name, out IEnumerable<string>? values) { values = null; return false; }
    }
}
