using System.Runtime.CompilerServices;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests;

public sealed class AuxiliaryProviderRequestTests
{
    [Theory]
    [InlineData(ProviderRequestKind.Compaction)]
    public async Task AuxiliaryToolLoopCannotUseParentHistoryOrSamplingCallbacks(ProviderRequestKind kind)
    {
        var identity = new ProviderConversationIdentity("thread", "thread", null, null, "turn", "window",
            ProviderRequestKind.Turn, 0, "user", null);
        var state = new ProviderConversationState(identity);
        state.TryCaptureContinuationState("main-state");
        using var parent = ProviderRequestContextScope.Push(new ProviderRequestContext(identity, ConversationState: state));
        var observer = new HistoryObserver();
        using var history = AgentHistoryRuntimeScope.Set(new AgentInvocationHistory([], observer));
        using var sampling = StreamingSamplingRuntimeScope.Set((_, _, _) =>
            throw new InvalidOperationException("Parent sampling callback leaked."));
        var client = new ToolClient();
        using var invoking = new StreamingFunctionInvokingChatClient(client);
        var executions = 0;

        using (new AuxiliaryProviderRequestScope(identity with { RequestKind = kind }))
        {
            var result = await invoking.GetResponseAsync([new ChatMessage(ChatRole.User, "maintenance")],
                new ChatOptions { Tools = [AIFunctionFactory.Create(() => { executions++; return "saved"; }, "remember")] });
            Assert.Equal("done", result.Text);
            Assert.Null(ProviderRequestContextScope.Current!.ConversationState!.ContinuationState);
            ProviderRequestContextScope.Current.ConversationState.TryCaptureContinuationState("auxiliary-state");
        }

        Assert.Equal(1, executions);
        Assert.Equal(0, client.HistoryLookups);
        Assert.Equal(0, observer.Updates);
        Assert.Same(state, ProviderRequestContextScope.Current!.ConversationState);
        Assert.Equal("main-state", state.ContinuationState);
        Assert.Equal(ProviderRequestKind.Turn, state.Identity.RequestKind);
    }

    private sealed class HistoryObserver : IAgentHistoryObserver
    {
        public int Updates { get; private set; }
        public ValueTask OnHistoryChangedAsync(AgentHistoryUpdate update, CancellationToken cancellationToken)
        {
            Updates++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ToolClient : IChatClient
    {
        private int _calls;
        public int HistoryLookups { get; private set; }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return ++_calls == 1
                ? new ChatResponseUpdate(ChatRole.Assistant,
                    [new FunctionCallContent("call_memory", "remember", new Dictionary<string, object?>())])
                : new ChatResponseUpdate(ChatRole.Assistant, "done");
        }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);
        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(IProviderConversationHistory))
            {
                HistoryLookups++;
                throw new InvalidOperationException("Auxiliary request queried the parent history service.");
            }
            return null;
        }
        public void Dispose() { }
    }
}
