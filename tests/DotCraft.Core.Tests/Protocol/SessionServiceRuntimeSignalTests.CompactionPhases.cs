using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Context.Compaction;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceRuntimeSignalTests
{
    [Fact]
    public async Task SubmitInputAsync_PreTurnCompaction_CoversPreviousTerminalTurnAndAppendsInputBehindCheckpoint()
    {
        var thread = await SeedCompactableThreadAsync(forceHighEstimate: true);
        var previousTerminalTurnId = thread.Turns[^1].Id;
        var chatClient = new RecordingChatClient("compact answer");
        await using var factory = CreateAgentFactory(
            chatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>compacted old context</summary>"));
        var service = CreateService(factory, chatClient, useStreamingFunctionInvoker: true);
        await service.ResumeThreadAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(
            thread.Id,
            [new TextContent("pending input after compact")]));

        Assert.Contains(events, evt => IsSystemEvent(evt, "compacted"));
        var requestHistory = chatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(requestHistory, text => text.Contains("compacted old context", StringComparison.Ordinal));
        Assert.Equal(1, requestHistory.Count(text => text.Contains("pending input after compact", StringComparison.Ordinal)));
        Assert.DoesNotContain(requestHistory, text => text.Contains("seed 0", StringComparison.Ordinal));

        var compactTurnId = (await service.GetThreadAsync(thread.Id)).Turns[^1].Id;
        var records = await ReadRolloutRecordsAsync(thread.Id);
        var checkpointIndex = Array.FindIndex(records, record => record.Kind == RolloutKinds.ContextCompacted);
        Assert.True(checkpointIndex >= 0);
        var checkpoint = records[checkpointIndex].ContextCompacted!;
        Assert.Equal("auto", checkpoint.Trigger);
        Assert.Equal(previousTerminalTurnId, checkpoint.CoveredThroughTurnId);
        Assert.DoesNotContain(
            "pending input after compact",
            JsonSerializer.Serialize(checkpoint.ReplacementHistory, SessionJsonOptions.Default));
        Assert.Contains(records.Skip(checkpointIndex + 1), record =>
            record.Kind == RolloutKinds.ModelHistoryMessagesAppended
            && record.ModelHistoryMessagesAppended!.TurnId == compactTurnId
            && JsonSerializer.Serialize(record.ModelHistoryMessagesAppended.Messages, SessionJsonOptions.Default)
                .Contains("pending input after compact", StringComparison.Ordinal));

        var replayed = await new SessionPersistenceService(new ThreadStore(_tempDir))
            .LoadModelHistoryAsync(thread.Id, CancellationToken.None);
        var replayedText = replayed.Select(MessageText).ToList();
        Assert.Contains(replayedText, text => text.Contains("compacted old context", StringComparison.Ordinal));
        Assert.Equal(1, replayedText.Count(text => text.Contains("pending input after compact", StringComparison.Ordinal)));
        Assert.DoesNotContain(replayedText, text => text.Contains("seed 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RollbackThreadAsync_AfterPreTurnCompaction_KeepsCompactedHistoryAndEstimate()
    {
        var thread = await SeedCompactableThreadAsync(forceHighEstimate: true);
        var persistence = new SessionPersistenceService(new ThreadStore(_tempDir));
        var fullHistoryEstimate = MessageTokenEstimator.Estimate(
            await persistence.LoadModelHistoryAsync(thread.Id, CancellationToken.None));
        var chatClient = new RecordingChatClient("compact answer");
        await using var factory = CreateAgentFactory(
            chatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>compacted old context</summary>"));
        var service = CreateService(factory, chatClient, useStreamingFunctionInvoker: true);
        await service.ResumeThreadAsync(thread.Id);
        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("mistyped input")]));
        Assert.Contains(events, evt => IsSystemEvent(evt, "compacted"));

        await service.RollbackThreadAsync(thread.Id, 1);

        var snapshot = service.TryGetContextUsageSnapshot(thread.Id);
        Assert.NotNull(snapshot);
        Assert.Equal("history_estimate", snapshot!.Source);
        Assert.True(snapshot.Tokens < fullHistoryEstimate, $"{snapshot.Tokens} >= {fullHistoryEstimate}");

        var followUpChatClient = new RecordingChatClient("follow answer");
        await using var followUpFactory = CreateAgentFactory(followUpChatClient, configureConfig: ConfigureSmallCompaction);
        var followUpService = CreateService(followUpFactory, followUpChatClient, useStreamingFunctionInvoker: true);
        await followUpService.ResumeThreadAsync(thread.Id);
        var followUpEvents = await CollectAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("edited input")]));

        Assert.DoesNotContain(followUpEvents, evt => IsSystemEvent(evt, "compacting"));
        var followUpHistory = followUpChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(followUpHistory, text => text.Contains("compacted old context", StringComparison.Ordinal));
        Assert.Contains(followUpHistory, text => text.Contains("edited input", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("seed 0", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("mistyped input", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("compact answer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SubmitInputAsync_PostTurnCompaction_CommitsCheckpointWithCompletedTurn()
    {
        var thread = await SeedCompactableThreadAsync(forceHighEstimate: false);
        IChatClient usageChatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("post turn answer")]),
            UsageUpdate(requestIndex: 1, input: 5_000, output: 100, cachedInput: 0)
        ]);
        await using var factory = CreateAgentFactory(
            usageChatClient,
            configureConfig: ConfigurePostTurnCompaction,
            compactionChatClient: new SummaryChatClient("<summary>post turn summary</summary>"));
        var service = CreateService(factory, usageChatClient, useStreamingFunctionInvoker: true);
        await service.ResumeThreadAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("trigger turn end")]));

        Assert.Contains(events, evt => IsSystemEvent(evt, "compacted"));
        var completedTurn = (await service.GetThreadAsync(thread.Id)).Turns[^1];
        Assert.Equal(TurnStatus.Completed, completedTurn.Status);
        var records = await ReadRolloutRecordsAsync(thread.Id);
        var turnStateIndex = Array.FindLastIndex(records, record =>
            record.Kind == RolloutKinds.TurnStateReplaced
            && record.TurnStateReplaced!.Turn.Id == completedTurn.Id);
        var checkpointIndex = Array.FindIndex(records, record => record.Kind == RolloutKinds.ContextCompacted);
        Assert.Equal(TurnStatus.Completed, records[turnStateIndex].TurnStateReplaced!.Turn.Status);
        Assert.Equal(turnStateIndex + 1, checkpointIndex);
        var checkpoint = records[checkpointIndex].ContextCompacted!;
        Assert.Equal("auto", checkpoint.Trigger);
        Assert.Equal(completedTurn.Id, checkpoint.CoveredThroughTurnId);
        Assert.Contains("post turn answer", JsonSerializer.Serialize(checkpoint.ReplacementHistory, SessionJsonOptions.Default));
        Assert.Equal("compacted_estimate", service.TryGetContextUsageSnapshot(thread.Id)?.Source);

        var followUpChatClient = new RecordingChatClient("follow answer");
        await using var followUpFactory = CreateAgentFactory(followUpChatClient, configureConfig: ConfigurePostTurnCompaction);
        var followUpService = CreateService(followUpFactory, followUpChatClient, useStreamingFunctionInvoker: true);
        await followUpService.ResumeThreadAsync(thread.Id);
        var followUpEvents = await CollectAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("after turn end")]));

        Assert.DoesNotContain(followUpEvents, evt => IsSystemEvent(evt, "compacting"));
        var followUpHistory = followUpChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(followUpHistory, text => text.Contains("post turn summary", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("seed 0", StringComparison.Ordinal));
        Assert.Single(await ReadRolloutRecordsAsync(thread.Id), record => record.Kind == RolloutKinds.ContextCompacted);

        await followUpService.RollbackThreadAsync(thread.Id, 1);
        var resendChatClient = new RecordingChatClient("resend answer");
        await using var resendFactory = CreateAgentFactory(resendChatClient, configureConfig: ConfigurePostTurnCompaction);
        var resendService = CreateService(resendFactory, resendChatClient, useStreamingFunctionInvoker: true);
        await resendService.ResumeThreadAsync(thread.Id);
        await DrainAsync(resendService.SubmitInputAsync(thread.Id, [new TextContent("resend after rollback")]));

        var resendHistory = resendChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(resendHistory, text => text.Contains("post turn summary", StringComparison.Ordinal));
        Assert.DoesNotContain(resendHistory, text => text.Contains("after turn end", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SubmitInputAsync_PostTurnCompactionFailure_KeepsTurnCompleted()
    {
        var thread = await SeedCompactableThreadAsync(forceHighEstimate: false);
        IChatClient usageChatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("post turn answer")]),
            UsageUpdate(requestIndex: 1, input: 5_000, output: 100, cachedInput: 0)
        ]);
        await using var factory = CreateAgentFactory(
            usageChatClient,
            configureConfig: ConfigurePostTurnCompaction,
            compactionChatClient: new ThrowingChatClient(new InvalidOperationException("summary unavailable")));
        var service = CreateService(factory, usageChatClient, useStreamingFunctionInvoker: true);
        await service.ResumeThreadAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("trigger turn end")]));

        Assert.Contains(events, evt => IsSystemEvent(evt, "compactFailed"));
        var completedTurn = (await service.GetThreadAsync(thread.Id)).Turns[^1];
        Assert.Equal(TurnStatus.Completed, completedTurn.Status);
        Assert.Contains(completedTurn.Items, item => item.Type == ItemType.AgentMessage);
        Assert.DoesNotContain(await ReadRolloutRecordsAsync(thread.Id), record => record.Kind == RolloutKinds.ContextCompacted);
    }

    [Fact]
    public async Task SubmitInputAsync_PostTurnCompaction_SkipsWhenInputIsQueued()
    {
        var thread = await SeedCompactableThreadAsync(forceHighEstimate: false);
        var gatedChatClient = new GatedUsageChatClient("gated answer", contextTokens: 5_000);
        await using var factory = CreateAgentFactory(
            gatedChatClient,
            configureConfig: ConfigurePostTurnCompaction,
            compactionChatClient: new SummaryChatClient("<summary>post turn summary</summary>"));
        var service = CreateService(factory, gatedChatClient, useStreamingFunctionInvoker: true);
        await service.ResumeThreadAsync(thread.Id);

        var eventsTask = CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("gated turn")]));
        await gatedChatClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var gatedTurnId = Assert.Single((await service.GetThreadAsync(thread.Id)).Turns, turn => turn.Status == TurnStatus.Running).Id;
        await service.EnqueueTurnInputAsync(thread.Id, [new TextContent("queued follow-up")]);
        gatedChatClient.Release.TrySetResult();
        var events = await eventsTask;

        Assert.DoesNotContain(events, evt => IsSystemEvent(evt, "compacting"));
        await WaitForIdleThreadAsync(service, thread.Id, expectedTurnCount: thread.Turns.Count + 2);
        var records = await ReadRolloutRecordsAsync(thread.Id);
        Assert.DoesNotContain(records, record =>
            record.Kind == RolloutKinds.ContextCompacted
            && record.ContextCompacted!.CoveredThroughTurnId == gatedTurnId);
    }

    [Fact]
    public async Task RollbackThreadAsync_BroadcastsHistoryRolledBackInsteadOfTurnCompletion()
    {
        var chatClient = new RecordingChatClient("answer");
        await using var factory = CreateAgentFactory(chatClient);
        var service = CreateService(factory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        var signals = new List<SessionThreadRuntimeSignal>();
        service.ThreadRuntimeSignalForBroadcast = (_, signal, _) => signals.Add(signal);

        await service.RollbackThreadAsync(thread.Id, 1);

        Assert.Equal([SessionThreadRuntimeSignal.HistoryRolledBack], signals);
    }

    private static void ConfigurePostTurnCompaction(AppConfig config)
    {
        ConfigureSmallCompaction(config);
        config.Compaction.PostTurnCompactThresholdPercent = 50;
    }

    private async Task<SessionThread> SeedCompactableThreadAsync(bool forceHighEstimate)
    {
        var seedChatClient = new RecordingChatClient("seed answer");
        await using var seedFactory = CreateAgentFactory(seedChatClient, configureConfig: ConfigureSmallCompaction);
        var seedService = CreateService(seedFactory, seedChatClient);
        var thread = await seedService.CreateThreadAsync(MakeIdentity());
        for (var i = 0; i < 4; i++)
        {
            await DrainAsync(seedService.SubmitInputAsync(
                thread.Id,
                [new TextContent($"seed {i} " + new string('u', 1200))]));
        }

        if (forceHighEstimate)
        {
            await new ThreadStore(_tempDir).SaveContextUsageTokensAsync(
                thread.Id,
                9_500,
                source: "history_estimate",
                isEstimate: true);
        }

        return await seedService.GetThreadAsync(thread.Id);
    }

    private async Task<ThreadRolloutRecord[]> ReadRolloutRecordsAsync(string threadId)
    {
        var path = Directory.GetFiles(_tempDir, threadId + ".jsonl", SearchOption.AllDirectories).Single();
        var lines = await File.ReadAllLinesAsync(path);
        return lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<ThreadRolloutRecord>(line, SessionJsonOptions.Default)!)
            .ToArray();
    }

    private static async Task WaitForIdleThreadAsync(SessionService service, string threadId, int expectedTurnCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var thread = await service.GetThreadAsync(threadId);
            if (thread.Turns.Count >= expectedTurnCount && thread.Turns.All(turn => turn.Status != TurnStatus.Running))
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException("The queued turn did not finish.");
    }

    private sealed class GatedUsageChatClient(string responseText, long contextTokens) : IChatClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            yield return UsageUpdate(requestIndex: 1, input: contextTokens, output: 10, cachedInput: 0);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
