using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_ProviderManagedContinuation_AppendsResponseAndContinues()
    {
        var inner = new ManagedContinuationFakeChatClient(pauseCount: 5, maximumContinuations: 5);
        var client = new StreamingFunctionInvokingChatClient(inner);

        var updates = await CollectAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "start")]));

        Assert.Equal(6, inner.Calls.Count);
        Assert.Equal(5, inner.Calls[5].Count(message => message.Role == ChatRole.Assistant));
        Assert.Equal(
            ["partial-1", "partial-2", "partial-3", "partial-4", "partial-5", "done"],
            updates.SelectMany(update => update.Contents).OfType<TextContent>().Select(content => content.Text));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ProviderManagedContinuation_ThrowsAtLimit()
    {
        var inner = new ManagedContinuationFakeChatClient(pauseCount: 3, maximumContinuations: 2);
        var client = new StreamingFunctionInvokingChatClient(inner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(
            client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.StartsWith("provider_continuation_limit:", exception.Message, StringComparison.Ordinal);
        Assert.Equal(3, inner.Calls.Count);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ProviderManagedContinuation_ZeroRejectsFirstContinuation()
    {
        var inner = new ManagedContinuationFakeChatClient(pauseCount: 1, maximumContinuations: 0);
        var client = new StreamingFunctionInvokingChatClient(inner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(
            client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.StartsWith("provider_continuation_limit:", exception.Message, StringComparison.Ordinal);
        Assert.Single(inner.Calls);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ProviderManagedContinuation_RejectsNegativePolicyBeforeSampling()
    {
        var inner = new ManagedContinuationFakeChatClient(pauseCount: 1, maximumContinuations: -1);
        var client = new StreamingFunctionInvokingChatClient(inner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(
            client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")])));

        Assert.StartsWith("provider_continuation_policy_invalid:", exception.Message, StringComparison.Ordinal);
        Assert.Empty(inner.Calls);
    }

    private sealed class ManagedContinuationFakeChatClient(
        int pauseCount,
        int maximumContinuations) : IChatClient
    {
        private static readonly ChatFinishReason PauseTurn = new("pause_turn");
        private readonly TestManagedContinuationPolicy _policy = new(maximumContinuations);

        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "unused")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            var call = Calls.Count;
            yield return new ChatResponseUpdate(ChatRole.Assistant, call <= pauseCount ? $"partial-{call}" : "done")
            {
                FinishReason = call <= pauseCount ? PauseTurn : ChatFinishReason.Stop
            };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(IProviderManagedContinuationPolicy)
                ? _policy
                : null;

        public void Dispose()
        {
        }
    }

    private sealed class TestManagedContinuationPolicy(int maximumContinuations)
        : IProviderManagedContinuationPolicy
    {
        private static readonly ChatFinishReason PauseTurn = new("pause_turn");

        public int MaximumContinuations => maximumContinuations;

        public bool ShouldContinue(ChatResponse response) => response.FinishReason == PauseTurn;
    }
}
