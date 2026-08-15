using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using SystemNoticePayload = DotCraft.Sessions.SystemNoticePayload;
using Xunit;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceMemoryConsolidationTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceMemoryConsolidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MemoryConsolidation_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task SubmitInputAsync_WhenConsolidationIsSkipped_EmitsSkippedWithoutConsolidated()
    {
        var consolidator = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("save_memory_not_called"));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var subscription = CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(IsConsolidationTerminal));

        var turnEvents = await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("remember blue")]));
        var threadEvents = await subscription;

        Assert.Contains(turnEvents, e => IsSystemEvent(e, "consolidating"));
        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidationSkipped"));
        Assert.DoesNotContain(threadEvents, e => IsSystemEvent(e, "consolidated"));
    }

    [Fact]
    public async Task SubmitInputAsync_ForInternalMaintenanceThread_DoesNotScheduleMemoryConsolidation()
    {
        var consolidator = new FakeMemoryConsolidator(
            MemoryConsolidationResult.Succeeded(memoryWritten: true, historyWritten: true));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = DreamsConstants.ChannelName,
            UserId = DreamsConstants.InternalUserId,
            WorkspacePath = _tempDir
        });
        thread.Metadata[ThreadVisibility.InternalMetadataKey] = DreamsConstants.InternalMetadataValue;

        var turnEvents = await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("organize dreams")]));

        Assert.Equal(0, consolidator.Calls);
        Assert.DoesNotContain(turnEvents, e => IsSystemEvent(e, "consolidating"));
    }

    [Fact]
    public async Task SubmitInputAsync_WhenConsolidationSucceeds_EmitsConsolidatedAndPersistentNotice()
    {
        var startSawPersistedTurn = false;
        var threadStore = new ThreadStore(_tempDir);
        string? completedThreadId = null;
        var consolidator = new FakeMemoryConsolidator(
            MemoryConsolidationResult.Succeeded(memoryWritten: true, historyWritten: true),
            async () =>
            {
                var persisted = completedThreadId == null
                    ? null
                    : await threadStore.LoadThreadAsync(completedThreadId);
                startSawPersistedTurn = persisted?.Turns.SingleOrDefault()?.Status == TurnStatus.Completed;
            });
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var runtimeSignals = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                runtimeSignals.Add(signal);
        };
        completedThreadId = thread.Id;

        var subscription = CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(e => IsSystemEvent(e, "consolidated"))
                && events.Any(e => IsMemoryNotice(e)));

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("remember blue")]));
        var threadEvents = await subscription;

        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidated"));
        Assert.Contains(threadEvents, IsMemoryNotice);
        Assert.True(startSawPersistedTurn);
        Assert.Contains(SessionThreadRuntimeSignal.MemoryConsolidated, runtimeSignals);

        var reloaded = await svc.GetThreadAsync(thread.Id);
        var notice = Assert.Single(reloaded.Turns.Single().Items, item => item.Type == ItemType.SystemNotice);
        Assert.Equal("memoryConsolidated", notice.AsSystemNotice?.Kind);
    }

    [Fact]
    public async Task AutoConsolidation_DoesNotBlockDirectInputWhileRunning()
    {
        var consolidator = new BlockingMemoryConsolidator(MemoryConsolidationResult.Skipped("no changes"));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var identity = MakeIdentity();
        var thread = await svc.CreateThreadAsync(identity);
        var runtimeSignals = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                runtimeSignals.Add(signal);
        };

        var firstEvents = await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        await consolidator.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var duringAutoConsolidation = await svc.GetThreadAsync(thread.Id);
        Assert.Single(duringAutoConsolidation.Turns);
        Assert.Empty(duringAutoConsolidation.QueuedInputs);
        var runtimeDuringAutoConsolidation = svc.GetThreadRuntimeSnapshot(duringAutoConsolidation);
        Assert.False(runtimeDuringAutoConsolidation.Busy);
        Assert.Null(runtimeDuringAutoConsolidation.MaintenanceKind);
        var summaryDuringMaintenance = Assert.Single(await svc.FindThreadsAsync(identity));
        Assert.Null(summaryDuringMaintenance.Runtime?.MaintenanceKind);
        Assert.False(summaryDuringMaintenance.Runtime?.Busy == true);
        Assert.DoesNotContain(SessionThreadRuntimeSignal.MaintenanceConsolidatingStarted, runtimeSignals);
        Assert.Contains(firstEvents, e => IsSystemEvent(e, "consolidating") && e.TurnId == thread.Turns[0].Id);

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("second")]));
        Assert.Equal(2, thread.Turns.Count);
        Assert.All(thread.Turns, turn => Assert.Equal(TurnStatus.Completed, turn.Status));

        consolidator.Release();
        await WaitUntilAsync(() => consolidator.Calls >= 2);

        var runtimeAfterAutoConsolidation = svc.GetThreadRuntimeSnapshot(await svc.GetThreadAsync(thread.Id));
        Assert.Null(runtimeAfterAutoConsolidation.MaintenanceKind);
    }

    [Fact]
    public async Task AutoConsolidation_WhenNextTurnRunsBeforeNoticePersists_CompletesWithoutFailure()
    {
        var consolidator = new BlockingMemoryConsolidator(
            MemoryConsolidationResult.Succeeded(memoryWritten: true, historyWritten: true));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var threadEventsTask = CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(e => IsSystemEvent(e, "consolidated"))
                || events.Any(e => IsSystemEvent(e, "consolidationFailed")));

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        await consolidator.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var runtimeDuringAutoConsolidation = svc.GetThreadRuntimeSnapshot(await svc.GetThreadAsync(thread.Id));
        Assert.False(runtimeDuringAutoConsolidation.Busy);
        Assert.Null(runtimeDuringAutoConsolidation.MaintenanceKind);

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("second")]));
        consolidator.Release();

        var threadEvents = await threadEventsTask;
        Assert.DoesNotContain(threadEvents, e => IsSystemEvent(e, "consolidationFailed"));
        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidated"));

        await WaitUntilAsync(() =>
        {
            var reloaded = new ThreadStore(_tempDir).LoadThreadAsync(thread.Id).GetAwaiter().GetResult();
            return reloaded?.Turns.Count >= 2
                && reloaded.Turns.Take(2).All(turn => turn.Status == TurnStatus.Completed)
                && reloaded.Turns[0].Items.Any(item =>
                    item.Type == ItemType.SystemNotice
                    && item.AsSystemNotice?.Kind == "memoryConsolidated");
        });

        var persisted = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        Assert.NotNull(persisted);
        Assert.Equal(2, persisted.Turns.Count);
        Assert.Contains(
            persisted.Turns[0].Items,
            item => item.Type == ItemType.SystemNotice
                && item.AsSystemNotice?.Kind == "memoryConsolidated");
    }

    [Fact]
    public async Task ManualConsolidation_DrainsQueuedInputsInReorderedOrder()
    {
        var consolidator = new BlockingMemoryConsolidator(MemoryConsolidationResult.Skipped("no changes"));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            consolidator,
            config => config.Memory.AutoConsolidateEnabled = false);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        var consolidationTask = svc.ConsolidateThreadMemoryAsync(thread.Id);
        await consolidator.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var second = await svc.EnqueueTurnInputAsync(thread.Id, [new TextContent("second")]);
        var third = await svc.EnqueueTurnInputAsync(thread.Id, [new TextContent("third")]);
        await svc.ReorderQueuedTurnInputsAsync(thread.Id, [third.Id, second.Id]);

        consolidator.Release();
        await consolidationTask;

        await WaitUntilAsync(() =>
            thread.Turns.Count >= 3
            && thread.Turns.Take(3).All(turn => turn.Status == TurnStatus.Completed)
            && thread.QueuedInputs.Count == 0);

        Assert.Equal(
            ["first", "third", "second"],
            thread.Turns.Take(3).Select(turn => turn.Input?.AsUserMessage?.Text ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task TryStartNextQueuedTurnAsync_LegacyRemoteImageBecomesModelPlaceholder()
    {
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("no changes")),
            config => config.Memory.AutoConsolidateEnabled = false);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.EnqueueTurnInputAsync(
            thread.Id,
            [new TextContent("snapshot")],
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "text", Text = "legacy image" }],
                MaterializedInputParts =
                [
                    new SessionInputPart { Type = "text", Text = "before" },
                    new SessionInputPart { Type = "image", Url = "http://127.0.0.1:1/image.png" },
                    new SessionInputPart { Type = "text", Text = "after" }
                ],
                DisplayText = "legacy image"
            });

        await service.TryStartNextQueuedTurnAsync(thread.Id);
        await WaitUntilAsync(() =>
            thread.Turns.Count == 1
            && thread.Turns[0].Status == TurnStatus.Completed
            && thread.QueuedInputs.Count == 0);

        var request = Assert.Single(chatClient.CapturedRequests);
        var userMessage = request.Last(message => message.Role == ChatRole.User);
        Assert.Equal(
            ["before", SessionInputPartResolver.RemoteImageOmittedText, "after"],
            userMessage.Contents
                .OfType<TextContent>()
                .Take(3)
                .Select(content => content.Text)
                .ToArray());
    }

    [Fact]
    public async Task TryStartNextQueuedTurnAsync_InlineImageRestoresDataContent()
    {
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("no changes")),
            config => config.Memory.AutoConsolidateEnabled = false);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.EnqueueTurnInputAsync(
            thread.Id,
            [new DataContent(new byte[] { 1, 2, 3 }, "image/png")],
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "image", Url = "data:image/png;base64,AQID" }],
                MaterializedInputParts = [new SessionInputPart { Type = "image", Url = "data:image/png;base64,AQID" }],
                DisplayText = "inline image"
            });

        await service.TryStartNextQueuedTurnAsync(thread.Id);
        await WaitUntilAsync(() =>
            thread.Turns.Count == 1
            && thread.Turns[0].Status == TurnStatus.Completed
            && thread.QueuedInputs.Count == 0);

        var request = Assert.Single(chatClient.CapturedRequests);
        var userMessage = request.Last(message => message.Role == ChatRole.User);
        var image = Assert.Single(userMessage.Contents.OfType<DataContent>());
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal([1, 2, 3], image.Data.ToArray());
    }

    [Fact]
    public async Task SubmitInputAsync_GuidancePendingLegacyRemoteImageBecomesModelPlaceholder()
    {
        var chatClient = new GuidanceDrainingChatClient();
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("no changes")),
            config => config.Memory.AutoConsolidateEnabled = false);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        var turnTask = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("start")]));
        await chatClient.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var queued = await service.EnqueueTurnInputAsync(
            thread.Id,
            [new TextContent("snapshot")],
            inputSnapshot: new SessionInputSnapshot
            {
                NativeInputParts = [new SessionInputPart { Type = "text", Text = "legacy guidance" }],
                MaterializedInputParts =
                [
                    new SessionInputPart { Type = "text", Text = "before" },
                    new SessionInputPart { Type = "image", Url = "https://127.0.0.1:1/image.png" },
                    new SessionInputPart { Type = "text", Text = "after" }
                ],
                DisplayText = "legacy guidance"
            });
        var activeTurn = Assert.Single(thread.Turns);
        await service.UpdateQueuedTurnInputAsync(
            thread.Id,
            queued.Id,
            activeTurn.Id,
            "guidancePending");

        chatClient.Release();
        await turnTask;

        var guidance = chatClient.GuidanceMessage;
        Assert.NotNull(guidance);
        Assert.Equal(ChatRole.User, guidance.Role);
        Assert.Equal(
            ["before", SessionInputPartResolver.RemoteImageOmittedText, "after"],
            guidance.Contents
                .OfType<TextContent>()
                .Select(content => content.Text)
                .ToArray());
        Assert.Empty(thread.QueuedInputs);
        Assert.Contains(
            activeTurn.Items,
            item => item.AsUserMessage is { DeliveryMode: "guidance", QueuedInputId: var queuedInputId }
                && string.Equals(queuedInputId, queued.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManualConsolidation_PreservesQueuedTriggerMetadataWhenDequeued()
    {
        var consolidator = new BlockingMemoryConsolidator(MemoryConsolidationResult.Skipped("no changes"));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            consolidator,
            config => config.Memory.AutoConsolidateEnabled = false);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        var consolidationTask = svc.ConsolidateThreadMemoryAsync(thread.Id);
        await consolidator.Started.WaitAsync(TimeSpan.FromSeconds(5));

        using (TurnTriggerScope.Set(new TurnTriggerInfo
               {
                   Kind = "team",
                   Label = "Worker assignment",
                   RefId = "task-42"
               }))
        {
            await svc.EnqueueTurnInputAsync(thread.Id, [new TextContent("second")]);
        }

        consolidator.Release();
        await consolidationTask;

        await WaitUntilAsync(() =>
            thread.Turns.Count >= 2
            && thread.Turns[1].Status == TurnStatus.Completed
            && thread.QueuedInputs.Count == 0);

        var input = thread.Turns[1].Input?.AsUserMessage;
        Assert.NotNull(input);
        Assert.Equal("queued", input.DeliveryMode);
        Assert.Equal("team", input.TriggerKind);
        Assert.Equal("Worker assignment", input.TriggerLabel);
        Assert.Equal("task-42", input.TriggerRefId);
    }

    [Fact]
    public async Task AutoConsolidation_SerializesRepeatedTriggersPerThread()
    {
        var consolidator = new BlockingMemoryConsolidator(MemoryConsolidationResult.Skipped("no changes"));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        await consolidator.Started.WaitAsync(TimeSpan.FromSeconds(5));

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("second")]));
        Assert.Equal(1, consolidator.Calls);

        consolidator.Release();
        await WaitUntilAsync(() => consolidator.Calls == 2);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenManualConsolidationActive_IsRejectedAsBusy()
    {
        var consolidator = new BlockingMemoryConsolidator(MemoryConsolidationResult.Skipped("no changes"));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            consolidator,
            config => config.Memory.AutoConsolidateEnabled = false);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        var consolidationTask = svc.ConsolidateThreadMemoryAsync(thread.Id);
        await consolidator.Started.WaitAsync(TimeSpan.FromSeconds(5));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.SubmitInputAsync(thread.Id, [new TextContent("second")]));

        Assert.Contains("active thread maintenance", ex.Message);
        await svc.CancelThreadMaintenanceAsync(thread.Id);
        await consolidationTask;
    }

    [Fact]
    public async Task CancelThreadMaintenanceAsync_ForManualConsolidation_EmitsCancelledWithoutNoticeAndDrainsQueuedInput()
    {
        var consolidator = new BlockingMemoryConsolidator(
            MemoryConsolidationResult.Succeeded(memoryWritten: true, historyWritten: true));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            consolidator,
            config => config.Memory.AutoConsolidateEnabled = false);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var subscription = CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(e => IsSystemEvent(e, "consolidationCancelled")));

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        var consolidationTask = svc.ConsolidateThreadMemoryAsync(thread.Id);
        await consolidator.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await svc.EnqueueTurnInputAsync(thread.Id, [new TextContent("second")]);

        await svc.CancelThreadMaintenanceAsync(thread.Id);
        var result = await consolidationTask;
        var threadEvents = await subscription;

        Assert.Equal("cancelled", result.Outcome);
        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidationCancelled"));
        Assert.DoesNotContain(threadEvents, e => IsSystemEvent(e, "consolidated"));
        Assert.DoesNotContain(threadEvents, IsMemoryNotice);

        await WaitUntilAsync(() =>
            thread.Turns.Count >= 2
            && thread.Turns[1].Status == TurnStatus.Completed
            && thread.QueuedInputs.Count == 0);

        var reloaded = await svc.GetThreadAsync(thread.Id);
        Assert.DoesNotContain(
            reloaded.Turns.SelectMany(turn => turn.Items),
            item => item.Payload is SystemNoticePayload { Kind: "memoryConsolidated" });
    }

    [Theory]
    [InlineData(MemoryConsolidationOutcome.Skipped)]
    [InlineData(MemoryConsolidationOutcome.Failed)]
    public async Task SubmitInputAsync_WhenConsolidationDoesNotWriteMemory_DoesNotEmitMemoryConsolidatedSignal(
        MemoryConsolidationOutcome outcome)
    {
        var result = outcome == MemoryConsolidationOutcome.Skipped
            ? MemoryConsolidationResult.Skipped("no changes")
            : MemoryConsolidationResult.Failed("provider unavailable");
        var consolidator = new FakeMemoryConsolidator(result);
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var runtimeSignals = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                runtimeSignals.Add(signal);
        };

        var subscription = CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(IsConsolidationTerminal));

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("remember blue")]));
        await subscription;

        Assert.DoesNotContain(SessionThreadRuntimeSignal.MemoryConsolidated, runtimeSignals);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenConsolidationFails_EmitsFailedWithoutNotice()
    {
        var consolidator = new FakeMemoryConsolidator(MemoryConsolidationResult.Failed("provider unavailable"));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var subscription = CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(IsConsolidationTerminal));

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("remember blue")]));
        var threadEvents = await subscription;

        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidationFailed"));
        Assert.DoesNotContain(threadEvents, IsMemoryNotice);
    }

    [Fact]
    public async Task ConsolidateThreadMemoryAsync_WhenSuccessful_EmitsEventsPersistsNoticeAndReturnsFlags()
    {
        var consolidator = new FakeMemoryConsolidator(
            MemoryConsolidationResult.Succeeded(memoryWritten: true, historyWritten: true));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            consolidator,
            config => config.Memory.AutoConsolidateEnabled = false);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("remember blue")]));

        var result = await svc.ConsolidateThreadMemoryAsync(thread.Id);
        var threadEvents = await CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(e => IsSystemEvent(e, "consolidated"))
                && events.Any(IsMemoryNotice));

        Assert.Equal("succeeded", result.Outcome);
        Assert.True(result.MemoryWritten);
        Assert.True(result.HistoryWritten);
        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidating"));
        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidated"));
        Assert.Contains(threadEvents, IsMemoryNotice);

        var reloaded = await svc.GetThreadAsync(thread.Id);
        var notice = Assert.Single(reloaded.Turns.Single().Items, item => item.Type == ItemType.SystemNotice);
        Assert.Equal("memoryConsolidated", notice.AsSystemNotice?.Kind);
    }

    [Theory]
    [InlineData(MemoryConsolidationOutcome.Skipped, "no changes", "consolidationSkipped")]
    [InlineData(MemoryConsolidationOutcome.Failed, "provider unavailable", "consolidationFailed")]
    public async Task ConsolidateThreadMemoryAsync_WhenNotSuccessful_EmitsTerminalEventWithoutNotice(
        MemoryConsolidationOutcome outcome,
        string message,
        string terminalKind)
    {
        var consolidationResult = outcome == MemoryConsolidationOutcome.Skipped
            ? MemoryConsolidationResult.Skipped(message)
            : MemoryConsolidationResult.Failed(message);
        var consolidator = new FakeMemoryConsolidator(consolidationResult);
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            consolidator,
            config => config.Memory.AutoConsolidateEnabled = false);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("remember blue")]));

        var result = await svc.ConsolidateThreadMemoryAsync(thread.Id);
        var threadEvents = await CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(e => IsSystemEvent(e, terminalKind)));

        Assert.Equal(outcome == MemoryConsolidationOutcome.Skipped ? "skipped" : "failed", result.Outcome);
        Assert.Equal(message, result.Message);
        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidating"));
        Assert.Contains(threadEvents, e => IsSystemEvent(e, terminalKind));
        Assert.DoesNotContain(threadEvents, IsMemoryNotice);
    }

    [Fact]
    public async Task ConsolidateThreadMemoryAsync_RejectsEmptyClientManagedAndActiveThreads()
    {
        var consolidator = new FakeMemoryConsolidator(
            MemoryConsolidationResult.Succeeded(memoryWritten: true, historyWritten: true));
        var blockingChat = new BlockingChatClient();
        await using var agentFactory = CreateAgentFactory(
            blockingChat,
            consolidator,
            config => config.Memory.AutoConsolidateEnabled = false);
        var svc = CreateService(agentFactory, blockingChat);

        var empty = await svc.CreateThreadAsync(MakeIdentity(), threadId: "thread-empty");
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConsolidateThreadMemoryAsync(empty.Id));

        var clientManaged = await svc.CreateThreadAsync(
            MakeIdentity(),
            historyMode: HistoryMode.Client,
            threadId: "thread-client");
        clientManaged.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = clientManaged.Id,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConsolidateThreadMemoryAsync(clientManaged.Id));

        var running = await svc.CreateThreadAsync(MakeIdentity(), threadId: "thread-running");
        _ = Task.Run(async () =>
        {
            await foreach (var _ in svc.SubmitInputAsync(running.Id, [new TextContent("keep running")]))
            {
            }
        });
        await WaitUntilAsync(() => running.Turns.Any(t => t.Status == TurnStatus.Running));

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConsolidateThreadMemoryAsync(running.Id));
        blockingChat.Release();
    }

    private SessionService CreateService(AgentFactory agentFactory, IChatClient chatClient)
    {
        var defaultAgent = chatClient.AsAIAgent();
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
    }

    private AgentFactory CreateAgentFactory(
        IChatClient chatClient,
        IMemoryConsolidator consolidator,
        Action<AppConfig>? configureConfig = null)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Memory.ConsolidateEveryNTurns = 1;
        configureConfig?.Invoke(config);
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: chatClient,
            toolSources: Array.Empty<IToolSource>(),
            memoryConsolidator: consolidator);
    }

    private SessionIdentity MakeIdentity() => new()
    {
        ChannelName = "test",
        UserId = "u",
        WorkspacePath = _tempDir
    };

    private static async Task<List<SessionEvent>> DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        var collected = new List<SessionEvent>();
        await foreach (var evt in events)
            collected.Add(evt);
        return collected;
    }

    private static async Task<List<SessionEvent>> CollectThreadEventsAsync(
        ISessionService svc,
        string threadId,
        Func<List<SessionEvent>, bool> done)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var collected = new List<SessionEvent>();
        await foreach (var evt in svc.SubscribeThreadAsync(threadId, replayRecent: true, cts.Token))
        {
            collected.Add(evt);
            if (done(collected))
                break;
        }

        return collected;
    }

    private static bool IsConsolidationTerminal(SessionEvent evt) =>
        IsSystemEvent(evt, "consolidated")
        || IsSystemEvent(evt, "consolidationSkipped")
        || IsSystemEvent(evt, "consolidationFailed")
        || IsSystemEvent(evt, "consolidationCancelled");

    private static bool IsSystemEvent(SessionEvent evt, string kind) =>
        evt.EventType == SessionEventType.SystemEvent
        && evt.Payload is SystemEventPayload payload
        && payload.Kind == kind;

    private static bool IsMemoryNotice(SessionEvent evt) =>
        evt.EventType == SessionEventType.ItemCompleted
        && evt.Payload is SessionItem { Payload: SystemNoticePayload { Kind: "memoryConsolidated" } };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(25, cts.Token);
        }
    }

    private sealed class FakeMemoryConsolidator(
        MemoryConsolidationResult result,
        Func<Task>? onStart = null) : IMemoryConsolidator
    {
        public int Calls { get; private set; }

        public async Task<MemoryConsolidationResult> ConsolidateAsync(
            IReadOnlyList<ChatMessage> messagesToArchive,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (onStart != null)
                await onStart();
            return result;
        }
    }

    private sealed class BlockingMemoryConsolidator(MemoryConsolidationResult result) : IMemoryConsolidator
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async Task<MemoryConsolidationResult> ConsolidateAsync(
            IReadOnlyList<ChatMessage> messagesToArchive,
            CancellationToken cancellationToken = default)
        {
            var calls = Interlocked.Increment(ref _calls);
            if (calls > 1)
                return MemoryConsolidationResult.Skipped("already_tested");

            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class StaticChatClient(string responseText) : IChatClient
    {
        public List<IReadOnlyList<ChatMessage>> CapturedRequests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CapturedRequests.Add(chatMessages.ToList());
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedRequests.Add(chatMessages.ToList());
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class GuidanceDrainingChatClient : IChatClient
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public ChatMessage? GuidanceMessage { get; private set; }

        public void Release() => _release.TrySetResult();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            var guidanceRuntime = TurnGuidanceRuntimeScope.Current
                ?? throw new InvalidOperationException("Expected an active turn guidance runtime.");
            GuidanceMessage = await guidanceRuntime.TryDrainGuidanceMessageAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => Release();
    }

    private sealed class BlockingChatClient : IChatClient
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await _release.Task.WaitAsync(cancellationToken);
            return new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await _release.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            Release();
        }
    }
}
