using DotCraft.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Unit tests for <see cref="SubAgentProgressChatClient"/>.
/// Validates that token usage from LLM responses is correctly
/// accumulated into the <see cref="SubAgentProgressBridge.ProgressEntry"/>,
/// both in non-streaming and streaming modes.
/// </summary>
public sealed class SubAgentProgressChatClientTests
{
    // -------------------------------------------------------------------------
    // Non-streaming: GetResponseAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetResponseAsync_WithUsage_WritesTokensToProgressEntry()
    {
        var entry = new SubAgentProgressBridge.ProgressEntry();
        var inner = new FakeChatClient(
            response: new ChatResponse([new ChatMessage(ChatRole.Assistant, "hello")])
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 100,
                    OutputTokenCount = 50
                }
            });

        var client = new SubAgentProgressChatClient(inner, entry);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(100, entry.InputTokens);
        Assert.Equal(50, entry.OutputTokens);
        Assert.Equal(1, entry.LlmCallCount);
    }

    [Fact]
    public async Task GetResponseAsync_WithoutUsage_DoesNotModifyTokens()
    {
        var entry = new SubAgentProgressBridge.ProgressEntry();
        var inner = new FakeChatClient(
            response: new ChatResponse([new ChatMessage(ChatRole.Assistant, "hello")]));

        var client = new SubAgentProgressChatClient(inner, entry);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(0, entry.InputTokens);
        Assert.Equal(0, entry.OutputTokens);
    }

    [Fact]
    public async Task GetResponseAsync_MultipleRounds_TokensAccumulate()
    {
        var entry = new SubAgentProgressBridge.ProgressEntry();
        var roundCount = 0;
        var inner = new FakeChatClient(responseFactory: () =>
        {
            roundCount++;
            return new ChatResponse([new ChatMessage(ChatRole.Assistant, $"round {roundCount}")])
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 100 * roundCount,
                    OutputTokenCount = 50 * roundCount
                }
            };
        });

        var client = new SubAgentProgressChatClient(inner, entry);

        // Simulate 3 LLM rounds (like tool call iterations)
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "round 1")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "round 2")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "round 3")]);

        // Each GetResponseAsync call is a separate LLM request, so request inputs sum directly.
        Assert.Equal(600, entry.InputTokens);
        Assert.Equal(300, entry.OutputTokens);
        Assert.Equal(3, entry.LlmCallCount);
    }

    // -------------------------------------------------------------------------
    // Streaming: GetStreamingResponseAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStreamingResponseAsync_UsageInLastChunk_WritesTokensAfterStreamEnds()
    {
        var entry = new SubAgentProgressBridge.ProgressEntry();
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "Hello"),
            new ChatResponseUpdate(ChatRole.Assistant, " world"),
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new UsageContent(new UsageDetails
                {
                    InputTokenCount = 200,
                    OutputTokenCount = 80
                })]
            }
        };

        var inner = new FakeChatClient(streamUpdates: updates);
        var client = new SubAgentProgressChatClient(inner, entry);

        // During streaming, tokens should NOT be written until the stream ends
        var allUpdates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            allUpdates.Add(update);
        }

        // All updates should be passed through
        Assert.Equal(3, allUpdates.Count);

        // After stream ends, tokens should be written
        Assert.Equal(200, entry.InputTokens);
        Assert.Equal(80, entry.OutputTokens);
        Assert.Equal(1, entry.LlmCallCount);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_NoUsageContent_DoesNotModifyTokens()
    {
        var entry = new SubAgentProgressBridge.ProgressEntry();
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "Hello"),
            new ChatResponseUpdate(ChatRole.Assistant, " world"),
        };

        var inner = new FakeChatClient(streamUpdates: updates);
        var client = new SubAgentProgressChatClient(inner, entry);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            // consume
        }

        Assert.Equal(0, entry.InputTokens);
        Assert.Equal(0, entry.OutputTokens);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_MultipleStreams_TokensAccumulate()
    {
        var entry = new SubAgentProgressBridge.ProgressEntry();
        var callCount = 0;

        var inner = new FakeChatClient(streamFactory: () =>
        {
            callCount++;
            return
            [
                new ChatResponseUpdate(ChatRole.Assistant, $"stream {callCount}"),
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new UsageContent(new UsageDetails
                    {
                        InputTokenCount = 100 * callCount,
                        OutputTokenCount = 40 * callCount
                    })]
                }
            ];
        });

        var client = new SubAgentProgressChatClient(inner, entry);

        // First streaming call
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "1")]))
        { }
        // Second streaming call
        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "2")]))
        { }

        // Each stream is a separate LLM request, so request inputs sum directly.
        Assert.Equal(300, entry.InputTokens);
        Assert.Equal(120, entry.OutputTokens);
        Assert.Equal(2, entry.LlmCallCount);
    }

    // -------------------------------------------------------------------------
    // Fake inner client
    // -------------------------------------------------------------------------

    private sealed class FakeChatClient : IChatClient
    {
        private readonly Func<ChatResponse>? _responseFactory;
        private readonly Func<ChatResponseUpdate[]>? _streamFactory;
        private readonly ChatResponse? _response;
        private readonly ChatResponseUpdate[]? _streamUpdates;

        public FakeChatClient(
            ChatResponse? response = null,
            Func<ChatResponse>? responseFactory = null,
            ChatResponseUpdate[]? streamUpdates = null,
            Func<ChatResponseUpdate[]>? streamFactory = null)
        {
            _response = response;
            _responseFactory = responseFactory;
            _streamUpdates = streamUpdates;
            _streamFactory = streamFactory;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var resp = _responseFactory?.Invoke() ?? _response
                ?? new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]);
            return Task.FromResult(resp);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            var updates = _streamFactory?.Invoke() ?? _streamUpdates ?? [];
            foreach (var update in updates)
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
