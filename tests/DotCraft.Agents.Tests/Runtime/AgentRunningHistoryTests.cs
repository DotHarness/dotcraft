using System.Runtime.CompilerServices;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents.Runtime;

public sealed class AgentRunningHistoryTests
{
    [Fact]
    public async Task Running_input_is_committed_in_order_without_becoming_response_output()
    {
        using var model = new BoundaryClient();
        using var loop = new StreamingFunctionInvokingChatClient(model);
        var agent = new ChatClientAgent(loop, new ChatOptions { Tools = [AIFunctionFactory.Create(() => "result", "probe")] });
        var history = new List<ChatMessage>();
        var pending = new Queue<ChatMessage>([new(ChatRole.User, "guidance") { MessageId = "input-1" }]);
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            TryDrainGuidanceMessageAsync = _ => Task.FromResult(pending.TryDequeue(out var message) ? message : null)
        });
        var output = new List<ChatResponseUpdate>();
        await foreach (var update in agent.RunStreamingAsync("initial", history))
        {
            Assert.Empty(history);
            output.Add(update);
        }
        Assert.Equal(new[] { ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User, ChatRole.Assistant }, history.Select(m => m.Role));
        Assert.Equal("guidance", history[3].Text);
        Assert.Equal("input-1", history[3].MessageId);
        Assert.DoesNotContain(output, update => update.Text.Contains("guidance", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_observer_receives_running_input_before_sampling_and_owns_the_history()
    {
        var observer = new Recorder();
        using var model = new BoundaryClient(() => Assert.Contains(observer.Messages, m => m.Text == "guidance"));
        using var loop = new StreamingFunctionInvokingChatClient(model);
        var agent = new ChatClientAgent(loop, new ChatOptions { Tools = [AIFunctionFactory.Create(() => "result", "probe")] });
        var history = new List<ChatMessage>();
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            TryDrainGuidanceMessageAsync = _ => Task.FromResult<ChatMessage?>(new(ChatRole.User, "guidance"))
        });
        await foreach (var _ in agent.RunStreamingAsync("initial", history, new ChatClientAgentRunOptions { HistoryObserver = observer })) { }
        Assert.Empty(history);
        Assert.Equal(5, observer.Messages.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_does_not_restore_compacted_history_or_persist_request_projection(bool neutral)
    {
        using var model = new BoundaryClient();
        using var loop = new StreamingFunctionInvokingChatClient(model);
        var agent = new ChatClientAgent(loop, new ChatOptions { Tools = [AIFunctionFactory.Create(() => "result", "probe")] });
        var history = new List<ChatMessage> { new(ChatRole.User, "old history") };
        var preparations = 0;
        using var preparation = StreamingSamplingRuntimeScope.Set((messages, _, _) =>
        {
            if (++preparations != 2) return Task.FromResult(new StreamingSamplingPreparation(messages, false, false));
            return Task.FromResult(new StreamingSamplingPreparation([new(ChatRole.User, "request projection")], neutral, true)
            {
                NeutralHistoryReplacement = neutral ? [new(ChatRole.User, "canonical summary")] : null
            });
        });
        await foreach (var _ in agent.RunStreamingAsync("initial", history)) { }
        Assert.DoesNotContain(history, message => message.Text == "request projection");
        if (neutral)
            Assert.Equal(new[] { "canonical summary", "done" }, history.Select(message => message.Text));
        else
        {
            Assert.Contains(history, message => message.Text == "old history");
            Assert.Single(history.SelectMany(message => message.Contents).OfType<FunctionCallContent>());
            Assert.Single(history.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partial_failure_keeps_incremental_history_but_preserves_standalone_success_only_commit(bool observe)
    {
        var recorder = new Recorder();
        using var model = new PartialFailureClient();
        using var loop = new StreamingFunctionInvokingChatClient(model);
        var history = new List<ChatMessage> { new(ChatRole.User, "prior") };
        var agent = new ChatClientAgent(loop);
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("initial", history,
                new ChatClientAgentRunOptions { HistoryObserver = observe ? recorder : null })) { }
        });
        Assert.Equal("prior", Assert.Single(history).Text);
        if (observe) Assert.Equal(new[] { "initial", "partial" }, recorder.Messages.Select(message => message.Text));
        else Assert.Empty(recorder.Messages);
    }

    [Fact]
    public async Task Multiple_inputs_preserve_multimodal_content_identity_and_model_metadata()
    {
        using var model = new BoundaryClient();
        using var loop = new StreamingFunctionInvokingChatClient(model);
        var agent = new ChatClientAgent(loop, new ChatOptions { Tools = [AIFunctionFactory.Create(() => "result", "probe")] });
        var history = new List<ChatMessage>();
        var image = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var pending = new Queue<ChatMessage>([
            new(ChatRole.User, [new TextContent("first"), image]) { MessageId = "one", AdditionalProperties = new() { ["original"] = "metadata" } },
            new(ChatRole.User, "second") { MessageId = "two" }
        ]);
        Task<ChatMessage?> Drain(CancellationToken _) => Task.FromResult(pending.TryDequeue(out var message) ? message : null);
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            TryDrainGuidanceMessageAsync = Drain, TryDrainAnswerBoundaryMessageAsync = Drain
        });
        await foreach (var _ in agent.RunStreamingAsync("initial", history)) { }
        var first = Assert.Single(history, message => message.MessageId == "one");
        Assert.Equal("metadata", first.AdditionalProperties!["original"]);
        Assert.Equal(image.Data.ToArray(), Assert.Single(first.Contents.OfType<DataContent>()).Data.ToArray());
        Assert.Equal(new[] { "one", "two" }, history.Where(message => message.MessageId is "one" or "two").Select(message => message.MessageId));
        Assert.Equal(new[] { ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.User, ChatRole.Assistant, ChatRole.User, ChatRole.Assistant }, history.Select(message => message.Role));
    }

    private sealed class PartialFailureClient : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            throw new IOException("provider disconnected");
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Failed_input_history_barrier_stops_before_the_next_model_call()
    {
        using var model = new BoundaryClient(() => Assert.Fail("The model ran after its input history barrier failed."));
        using var loop = new StreamingFunctionInvokingChatClient(model);
        var agent = new ChatClientAgent(loop, new ChatOptions { Tools = [AIFunctionFactory.Create(() => "result", "probe")] });
        var history = new List<ChatMessage>();
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            TryDrainGuidanceMessageAsync = _ => Task.FromResult<ChatMessage?>(new(ChatRole.User, "guidance"))
        });
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("initial", history,
                new ChatClientAgentRunOptions { HistoryObserver = new FailingHistoryObserver() })) { }
        });
        Assert.Empty(history);
    }

    private sealed class FailingHistoryObserver : IAgentHistoryObserver
    {
        public ValueTask OnHistoryChangedAsync(AgentHistoryUpdate update, CancellationToken cancellationToken)
        {
            if (update.Messages.Any(message => message.Text == "guidance")) throw new IOException("history write failed");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Recorder : IAgentHistoryObserver
    {
        public List<ChatMessage> Messages { get; } = [];
        public ValueTask OnHistoryChangedAsync(AgentHistoryUpdate update, CancellationToken cancellationToken)
        {
            if (update.IsReplacement) Messages.Clear();
            Messages.AddRange(update.Messages);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BoundaryClient(Action? beforeFinal = null) : IChatClient
    {
        private int _calls;
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (++_calls == 1)
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe")]);
            else
            {
                beforeFinal?.Invoke();
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
