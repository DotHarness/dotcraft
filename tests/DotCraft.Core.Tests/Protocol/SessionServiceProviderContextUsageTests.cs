using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceProviderContextUsageTests : SessionServiceRuntimeSignalTestBase
{
    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    [InlineData(0)]
    public async Task AggregateBillingAcrossToolRoundsDoesNotDriveContextCompaction(int measuredRounds)
    {
        using var model = new AggregateUsageClient(measuredRounds);
        await using var factory = CreateAgentFactory(
            model,
            toolProviders: [new ProjectionTestToolSource()],
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>unexpected compaction</summary>"));
        var service = new SessionService(factory, null, new SessionPersistenceService(new ThreadStore(_tempDir)), new SessionGate());
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Read the fixture.")]));

        var turn = Assert.Single(thread.Turns);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.DoesNotContain(events, e => e.SystemEventPayload?.Kind is "compacting" or "compacted");
        var usage = events.Select(e => e.UsageDeltaPayload).OfType<UsageDeltaPayload>().ToArray();
        Assert.Equal(42_000, usage.Sum(x => x.InputTokens));
        Assert.Equal(90, usage.Sum(x => x.OutputTokens));
        Assert.Equal(40_000, usage.Sum(x => x.CachedInputTokens));
        Assert.Equal(2, usage.Sum(x => x.LlmCallDelta));
        Assert.Equal(42_000, turn.TokenUsage!.InputTokens);

        var latest = Assert.IsType<DotCraft.Sessions.Wire.ContextUsageSnapshot>(service.TryGetContextUsageSnapshot(thread.Id));
        if (measuredRounds == 2)
        {
            Assert.Equal([2_400, 2_400, 3_200, 3_200], usage.Select(x => x.ContextUsage!.Tokens));
            Assert.Equal(new[] { (0L, 0L, 0), (20_000L, 40L, 1), (0L, 0L, 0), (22_000L, 50L, 1) },
                usage.Select(x => (x.InputTokens, x.OutputTokens, x.LlmCallDelta)).ToArray());
            Assert.Equal([2_350, 2_350, 3_100, 3_100], usage.Select(x => x.ContextInputTokens));
            Assert.All(usage, x => Assert.False(x.ContextUsage!.IsEstimate));
            Assert.Equal(3_200, latest.Tokens);
        }
        else if (measuredRounds == 1)
        {
            var last = usage[^1];
            Assert.Null(last.ContextInputTokens);
            Assert.True(last.ContextUsage!.IsEstimate);
            Assert.InRange(last.ContextUsage.Tokens, 2_350, 7_999);
            await using var store = new ThreadStore(_tempDir);
            Assert.Equal(2_350, store.LoadContextUsageAnchor(thread.Id)!.Tokens);
        }
        else
        {
            Assert.All(usage, x => Assert.Null(x.ContextInputTokens));
            Assert.All(usage, x => Assert.True(x.ContextUsage!.IsEstimate));
            Assert.True(latest.IsEstimate);
            Assert.InRange(latest.Tokens, 1, 7_999);
        }
    }

    [Fact]
    public async Task OccupancyWithoutInputTokensDoesNotCountTheResponseTwice()
    {
        const long occupiedTokens = 8_400;
        var answer = string.Join(' ', Enumerable.Range(0, 500).Select(i => $"detail{i}"));
        var model = new FakeChatClient(
        [
            new(ChatRole.Assistant, [new TextContent(answer)]),
            Usage(new UsageDetails(), new(occupiedTokens))
        ]);
        await using var factory = CreateAgentFactory(model, configureConfig: config =>
        {
            ConfigureSmallCompaction(config);
            config.Compaction.PostTurnCompactThresholdPercent = 85;
        }, compactionChatClient: new SummaryChatClient("<summary>unexpected compaction</summary>"));
        var service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Read the fixture.")]));

        Assert.DoesNotContain(events, e => e.SystemEventPayload?.Kind is "compacting" or "compacted");
        var snapshot = Assert.IsType<DotCraft.Sessions.Wire.ContextUsageSnapshot>(service.TryGetContextUsageSnapshot(thread.Id));
        Assert.Equal(occupiedTokens, snapshot.Tokens);
        Assert.False(snapshot.IsEstimate);
        await using var store = new ThreadStore(_tempDir);
        Assert.Null(store.LoadContextUsageAnchor(thread.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OccupancyWithoutInputTokensAllowsHistoryCompaction(bool coldResume)
    {
        const long occupiedTokens = 2_400;
        var thread = await SeedCompactableThreadAsync(forceHighEstimate: false);
        var answer = string.Join(' ', Enumerable.Repeat("fixture", 2_800));
        var model = new FakeChatClient(
        [
            new(ChatRole.Assistant, [new TextContent(answer)]),
            Usage(new UsageDetails(), new(occupiedTokens))
        ]);
        await using var factory = CreateAgentFactory(model, configureConfig: config =>
        {
            ConfigureSmallCompaction(config);
            config.Compaction.PostTurnCompactThresholdPercent = coldResume ? 0 : 85;
        }, compactionChatClient: new SummaryChatClient("<summary>Earlier fixture history.</summary>"));
        var service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        await service.ResumeThreadAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Read the fixture.")]));
        Assert.Contains(events, e => e.UsageDeltaPayload?.ContextUsage is { Tokens: occupiedTokens, IsEstimate: false });

        if (coldResume)
        {
            Assert.DoesNotContain(events, e => e.SystemEventPayload?.Kind == "compacted");
            Assert.Equal(occupiedTokens, service.TryGetContextUsageSnapshot(thread.Id)?.Tokens);
            var next = new RecordingChatClient("Continued.");
            await using var nextFactory = CreateAgentFactory(next, configureConfig: ConfigureSmallCompaction,
                compactionChatClient: new SummaryChatClient("<summary>Earlier fixture history.</summary>"));
            var resumed = CreateService(nextFactory, next, useStreamingFunctionInvoker: true);
            await resumed.ResumeThreadAsync(thread.Id);
            events = await CollectAsync(resumed.SubmitInputAsync(thread.Id, [new TextContent("Continue.")]));

            await resumed.RollbackThreadAsync(thread.Id, 1);
            await using var store = new ThreadStore(_tempDir);
            var history = await new SessionPersistenceService(store).LoadModelHistoryAsync(thread.Id, CancellationToken.None);
            Assert.Contains(history, message => message.Text.Contains("Earlier fixture history.", StringComparison.Ordinal));
        }

        Assert.Contains(events, e => e.SystemEventPayload?.Kind == "compacted");
    }

    [Fact]
    public async Task UnknownOccupancyDiscardsAnOldBillingDerivedAnchor()
    {
        var legacy = new FakeChatClient(
        [
            new(ChatRole.Assistant, [new TextContent("Seed answer")]),
            new(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 5_000, OutputTokenCount = 10 })])
        ]);
        await using var legacyFactory = CreateAgentFactory(legacy, configureConfig: ConfigureSmallCompaction);
        var seedService = CreateService(legacyFactory, legacy, useStreamingFunctionInvoker: true);
        var thread = await seedService.CreateThreadAsync(MakeIdentity());
        await DrainAsync(seedService.SubmitInputAsync(thread.Id, [new TextContent("Seed request")]));
        await using var store = new ThreadStore(_tempDir);
        Assert.NotNull(store.LoadContextUsageAnchor(thread.Id));

        var aggregate = new FakeChatClient(
        [
            new(ChatRole.Assistant, [new TextContent("Next answer")]),
            Billing(25_000, 50, 23_000)
        ]);
        await using var nextFactory = CreateAgentFactory(aggregate, configureConfig: ConfigureSmallCompaction);
        var service = CreateService(nextFactory, aggregate, useStreamingFunctionInvoker: true);
        await service.ResumeThreadAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("Next request")]));

        Assert.DoesNotContain(events, e => e.SystemEventPayload?.Kind is "compacting" or "compacted");
        Assert.Null(store.LoadContextUsageAnchor(thread.Id));
        var recorded = Assert.Single(events.Select(e => e.UsageDeltaPayload).OfType<UsageDeltaPayload>());
        Assert.Equal(25_000, recorded.InputTokens);
        Assert.Null(recorded.ContextInputTokens);
        Assert.True(recorded.ContextUsage!.IsEstimate);
        Assert.InRange(recorded.ContextUsage.Tokens, 1, 4_999);
    }

    private static ChatResponseUpdate Billing(long input, long output, long cached) =>
        Usage(new UsageDetails { InputTokenCount = input, OutputTokenCount = output, CachedInputTokenCount = cached }, null);

    private static ChatResponseUpdate Usage(UsageDetails billing, ProviderContextUsage? measurement)
    {
        var usage = new UsageContent(billing);
        ProviderContextUsage.Attach(usage, measurement);
        return new ChatResponseUpdate(ChatRole.Assistant, [usage]);
    }

    private sealed class AggregateUsageClient(int measuredRounds) : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var first = ++_calls == 1;
            if (_calls <= measuredRounds)
                yield return Usage(new UsageDetails(), first ? new(2_400, 2_350) : new(3_200, 3_100));

            yield return first ? Billing(20_000, 40, 19_000) : Billing(22_000, 50, 21_000);
            if (first)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("fixture", "ProjectionRead")])
                {
                    FinishReason = ChatFinishReason.ToolCalls
                };
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Done.")])
                {
                    FinishReason = ChatFinishReason.Stop
                };
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
