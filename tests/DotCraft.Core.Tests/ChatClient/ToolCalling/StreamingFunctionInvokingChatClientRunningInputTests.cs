using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_DrainsRunningInputAfterPreSamplingCompaction()
    {
        var inner = new RoundTripFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [AIFunctionFactory.Create(() => "tool ok", name: "GetStatus")]
        };
        var prepared = new List<List<ChatMessage>>();
        using var sampling = StreamingSamplingRuntimeScope.Set((messages, _, _) =>
        {
            prepared.Add(messages.ToList());
            return Task.FromResult(prepared.Count == 2
                ? new StreamingSamplingPreparation([new ChatMessage(ChatRole.User, "summary")], false, true)
                : new StreamingSamplingPreparation(messages, false, false));
        });
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            DrainAsync = (boundary, _) => boundary == StreamingGuidanceBoundary.AfterTools
                ? RunningInput(new ChatMessage(ChatRole.User, "steer"))
                : NoRunningInput()
        });

        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(2, inner.Calls.Count);
        Assert.DoesNotContain(prepared[1], message => message.Text == "steer");
        Assert.Equal(["user:summary", "user:steer"], inner.Calls[1].Select(message => $"{message.Role}:{message.Text}"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_AnswerBoundaryEndsWithoutSamplingWhenDrainAdmitsNothing()
    {
        var inner = new SingleReplyFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);
        var preparations = 0;
        var boundaries = new List<StreamingGuidanceBoundary>();
        using var sampling = StreamingSamplingRuntimeScope.Set((messages, _, _) =>
        {
            preparations++;
            return Task.FromResult(new StreamingSamplingPreparation(messages, false, false));
        });
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            DrainAsync = (boundary, _) =>
            {
                boundaries.Add(boundary);
                return NoRunningInput();
            },
            HasPendingGuidanceAsync = _ => Task.FromResult(true)
        });

        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Single(inner.Calls);
        Assert.Equal(2, preparations);
        Assert.Equal([StreamingGuidanceBoundary.TurnStart, StreamingGuidanceBoundary.AnswerBoundary], boundaries);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_StreamReissueDoesNotDrainRunningInput()
    {
        var inner = new ScriptedStreamChatClient(
            ([ToolCall("call-1", "GetWeather")], null),
            ([Text("partial")], new IOException("connection reset")),
            ([Text("done")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 1))
        {
            AdditionalTools = [AIFunctionFactory.Create(() => "sunny", name: "GetWeather")]
        };
        var boundaries = new List<StreamingGuidanceBoundary>();
        using var guidance = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            DrainAsync = (boundary, _) =>
            {
                boundaries.Add(boundary);
                return boundary == StreamingGuidanceBoundary.AfterTools
                    ? RunningInput(new ChatMessage(ChatRole.User, "steer"))
                    : NoRunningInput();
            }
        });

        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "weather?")]));

        Assert.Equal(3, inner.Requests.Count);
        Assert.Equal([StreamingGuidanceBoundary.TurnStart, StreamingGuidanceBoundary.AfterTools], boundaries);
        Assert.Single(inner.Requests[2], message => message.Text == "steer");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UserSteerBeforeOutputReissuesTheRequestWithIt()
    {
        var firstRequestBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequestCancelled = false;
        var inner = new GatedStreamChatClient(async (index, ct, emit) =>
        {
            if (index > 0)
            {
                await emit(Text("done"));
                return;
            }

            await emit(new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("") { ProtectedData = "abandoned" }]));
            firstRequestBlocked.TrySetResult();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
            catch (OperationCanceledException)
            {
                firstRequestCancelled = true;
                throw;
            }
            await emit(Text("not interrupted"));
        });
        var client = new StreamingFunctionInvokingChatClient(inner);
        var steer = new InstantSteer();
        using var guidance = StreamingGuidanceRuntimeScope.Set(steer.Context);
        var history = new HistoryRecorder();
        using var historyScope = AgentHistoryRuntimeScope.Set(new AgentInvocationHistory([], history));

        var run = CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));
        await firstRequestBlocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        steer.Submit("steer");
        var updates = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(firstRequestCancelled);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal(["user:start", "user:steer"], inner.Requests[1].Select(message => $"{message.Role}:{message.Text}"));
        Assert.Equal([StreamingGuidanceBoundary.TurnStart, StreamingGuidanceBoundary.AfterTools], steer.Boundaries);
        Assert.DoesNotContain(updates.SelectMany(update => update.Contents), content => content is TextReasoningContent);
        Assert.Equal(["user:steer", "assistant:done"], history.Messages.Select(message => $"{message.Role}:{message.Text}"));
        Assert.DoesNotContain(history.Messages.SelectMany(message => message.Contents), content => content is TextReasoningContent);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UserSteerAfterOutputWaitsForTheNextBoundary()
    {
        var outputDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRequestCancelled = false;
        var inner = new GatedStreamChatClient(async (index, ct, emit) =>
        {
            if (index > 0)
            {
                await emit(Text("done"));
                return;
            }

            await emit(Text("partial"));
            try
            {
                await release.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                firstRequestCancelled = true;
                throw;
            }
            await emit(Text(" answer"));
        });
        var client = new StreamingFunctionInvokingChatClient(inner);
        var steer = new InstantSteer();
        using var guidance = StreamingGuidanceRuntimeScope.Set(steer.Context);

        async Task ConsumeAsync()
        {
            await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
            {
                if (update.Text == "partial")
                    outputDelivered.TrySetResult();
            }
        }

        var run = ConsumeAsync();
        await outputDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        steer.Submit("steer");
        await Task.Delay(200);
        release.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(firstRequestCancelled);
        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal(
            ["user:start", "assistant:partial answer", "user:steer"],
            inner.Requests[1].Select(message => $"{message.Role}:{message.Text}"));
        Assert.Equal([StreamingGuidanceBoundary.TurnStart, StreamingGuidanceBoundary.AnswerBoundary], steer.Boundaries);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UserSteerDuringRetryBackoffReissuesImmediatelyWithIt()
    {
        var failure = new ProviderFailure(ProviderFailureKind.InternalServerError, 503, ServerRetryAfter: TimeSpan.FromSeconds(30));
        var inner = new ScriptedStreamChatClient(
            ([Text("partial")], new ProviderFailureException(failure, new IOException("wire"))),
            ([Text("done")], null));
        var client = new StreamingFunctionInvokingChatClient(new RetryBudgetChatClient(inner, 1));
        var steer = new InstantSteer();
        using var guidance = StreamingGuidanceRuntimeScope.Set(steer.Context);
        using var retry = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext
        {
            NotifyRetry = _ => steer.Submit("steer")
        });

        await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, inner.Requests.Count);
        Assert.Equal(
            ["user:start", "assistant:partial", "user:steer"],
            inner.Requests[1].Select(message => $"{message.Role}:{message.Text}"));
        Assert.Equal([StreamingGuidanceBoundary.TurnStart, StreamingGuidanceBoundary.AfterTools], steer.Boundaries);
    }

    private sealed class InstantSteer
    {
        private readonly Lock _gate = new();
        private TaskCompletionSource _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _text;

        public InstantSteer() =>
            Context = new StreamingGuidanceRuntimeContext
            {
                DrainAsync = DrainAsync,
                HasPendingGuidanceAsync = _ => Task.FromResult(Pending.IsCompleted),
                WaitForInstantInterruptAsync = ct => Pending.WaitAsync(ct)
            };

        public StreamingGuidanceRuntimeContext Context { get; }

        public List<StreamingGuidanceBoundary> Boundaries { get; } = [];

        private Task Pending
        {
            get
            {
                lock (_gate)
                    return _pending.Task;
            }
        }

        public void Submit(string text)
        {
            lock (_gate)
            {
                _text = text;
                _pending.TrySetResult();
            }
        }

        private Task<IReadOnlyList<ChatMessage>> DrainAsync(StreamingGuidanceBoundary boundary, CancellationToken _)
        {
            lock (_gate)
            {
                Boundaries.Add(boundary);
                if (boundary == StreamingGuidanceBoundary.TurnStart || _text is null)
                    return NoRunningInput();

                var message = new ChatMessage(ChatRole.User, _text);
                _text = null;
                _pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                return RunningInput(message);
            }
        }
    }

    private sealed class HistoryRecorder : IAgentHistoryObserver
    {
        public List<ChatMessage> Messages { get; } = [];

        public ValueTask OnHistoryChangedAsync(AgentHistoryUpdate update, CancellationToken cancellationToken)
        {
            Messages.AddRange(update.Messages);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GatedStreamChatClient(
        Func<int, CancellationToken, Func<ChatResponseUpdate, Task>, Task> respond) : IChatClient
    {
        public List<List<ChatMessage>> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var index = Requests.Count;
            Requests.Add([.. chatMessages]);
            var channel = Channel.CreateUnbounded<ChatResponseUpdate>();
            var producer = Task.Run(async () =>
            {
                try
                {
                    await respond(index, cancellationToken, update => channel.Writer.WriteAsync(update).AsTask());
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            }, CancellationToken.None);
            await foreach (var update in channel.Reader.ReadAllAsync(CancellationToken.None))
                yield return update;
            await producer;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
