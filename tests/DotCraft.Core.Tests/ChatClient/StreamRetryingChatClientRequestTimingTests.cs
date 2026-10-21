using DotCraft.Agents;
using DotCraft.Sessions;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class StreamRetryingChatClientRequestTimingTests
{
    [Fact]
    public async Task UsageCarriesTimingFromTheFirstProviderUpdate()
    {
        var clock = new ManualTimeProvider();
        var inner = new ScriptedChatClient(clock, null,
        [
            [(800, Empty()), (3_000, Text("hello")), (1_000, Usage())]
        ]);
        var client = new StreamRetryingChatClient(inner, new StreamRetryOptions(0, TimeSpan.FromSeconds(30)), clock);

        var timing = await ReadTimingAsync(client);

        Assert.Equal(new TokenUsageRequestTiming(800, 4_000), timing);
    }

    [Fact]
    public async Task OutputStartClassifierSkipsRequestAcknowledgements()
    {
        var clock = new ManualTimeProvider();
        var inner = new ScriptedChatClient(clock, new AcknowledgementClassifier(),
        [
            [(100, Acknowledgement()), (900, Empty()), (2_000, Text("hello")), (500, Usage())]
        ]);
        var client = new StreamRetryingChatClient(inner, new StreamRetryOptions(0, TimeSpan.FromSeconds(30)), clock);

        var timing = await ReadTimingAsync(client);

        Assert.Equal(new TokenUsageRequestTiming(1_000, 2_500), timing);
    }

    [Fact]
    public async Task RetriedAttemptRestartsTiming()
    {
        var clock = new ManualTimeProvider();
        var inner = new ScriptedChatClient(clock, null,
        [
            [(5_000, null)],
            [(500, Text("hello")), (1_000, Usage())]
        ]);
        var client = new StreamRetryingChatClient(inner, new StreamRetryOptions(1, TimeSpan.FromSeconds(30)), clock);

        using var scope = ModelStreamRetryRuntimeScope.Set(new ModelStreamRetryRuntimeContext { NotifyRetry = _ => { } });
        var timing = await ReadTimingAsync(client);

        Assert.Equal(new TokenUsageRequestTiming(500, 1_000), timing);
    }

    private static async Task<TokenUsageRequestTiming?> ReadTimingAsync(IChatClient client)
    {
        TokenUsageRequestTiming? timing = null;
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
        {
            foreach (var usage in update.Contents.OfType<UsageContent>())
                timing = TokenUsageRequestTiming.TryGet(usage);
        }
        return timing;
    }

    private static ChatResponseUpdate Empty() => new(ChatRole.Assistant, (IList<AIContent>?)null);

    private static ChatResponseUpdate Acknowledgement() =>
        new(ChatRole.Assistant, (IList<AIContent>?)null) { RawRepresentation = AcknowledgementClassifier.Marker };

    private static ChatResponseUpdate Text(string text) => new(ChatRole.Assistant, text);

    private static ChatResponseUpdate Usage() =>
        new(ChatRole.Assistant, [new UsageContent(new UsageDetails { OutputTokenCount = 100 })]);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(int milliseconds) => _ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
    }

    private sealed class AcknowledgementClassifier : IModelStreamOutputStartClassifier
    {
        public static readonly object Marker = new();

        public bool IsOutputStart(ChatResponseUpdate update) => !ReferenceEquals(update.RawRepresentation, Marker);
    }

    /// <summary>Each attempt advances the clock before each step; a null update ends the attempt with a transport failure.</summary>
    private sealed class ScriptedChatClient(
        ManualTimeProvider clock,
        IModelStreamOutputStartClassifier? classifier,
        IReadOnlyList<IReadOnlyList<(int DelayMs, ChatResponseUpdate? Update)>> attempts) : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            StreamAsync(attempts[_calls++]);

        private async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(
            IReadOnlyList<(int DelayMs, ChatResponseUpdate? Update)> steps)
        {
            foreach (var (delayMs, update) in steps)
            {
                await Task.Yield();
                clock.Advance(delayMs);
                if (update is null)
                    throw new IOException("stream closed before completion");
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IModelStreamOutputStartClassifier) ? classifier : null;

        public void Dispose()
        {
        }
    }
}
