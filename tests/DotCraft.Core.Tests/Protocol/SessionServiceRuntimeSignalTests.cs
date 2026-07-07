using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Agents.AI;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Verifies that <see cref="SessionService"/> emits runtime broadcast signals for turn lifecycle transitions.
/// </summary>
public sealed class SessionServiceRuntimeSignalTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceRuntimeSignalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RuntimeSignal_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task SubmitInputAsync_EmitsTurnStartedThenCompleted()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var seen = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                seen.Add(signal);
        };

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Equal(
            [SessionThreadRuntimeSignal.TurnStarted, SessionThreadRuntimeSignal.TurnCompleted],
            seen);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenAgentThrows_EmitsTurnStartedThenFailed()
    {
        IChatClient chatClient = new ThrowingChatClient(new InvalidOperationException("boom"));
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var seen = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                seen.Add(signal);
        };

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Equal(
            [SessionThreadRuntimeSignal.TurnStarted, SessionThreadRuntimeSignal.TurnFailed],
            seen);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenAgentThrowsAfterPartialResponse_RebuildsSessionFromFailedTurn()
    {
        var seedChatClient = new RecordingChatClient("first answer");
        await using var seedFactory = CreateAgentFactory(seedChatClient);
        var seedService = CreateService(seedFactory, seedChatClient);
        var thread = await seedService.CreateThreadAsync(MakeIdentity());
        await DrainAsync(seedService.SubmitInputAsync(thread.Id, [new TextContent("first")]));

        IChatClient firstChatClient = new ThrowingAfterUpdatesChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial answer")])],
            new InvalidOperationException("boom"));
        await using var firstFactory = CreateAgentFactory(firstChatClient);
        var firstService = CreateService(firstFactory, firstChatClient);
        await firstService.ResumeThreadAsync(thread.Id);

        await DrainAsync(firstService.SubmitInputAsync(thread.Id, [new TextContent("fail now")]));

        var failedThread = await firstService.GetThreadAsync(thread.Id);
        var failedTurn = failedThread.Turns.Last();
        Assert.Equal(TurnStatus.Failed, failedTurn.Status);
        Assert.True(new ThreadStore(_tempDir).SessionFileExists(thread.Id));

        var secondChatClient = new RecordingChatClient("second answer");
        await using var secondFactory = CreateAgentFactory(secondChatClient);
        var secondService = CreateService(secondFactory, secondChatClient);
        await secondService.ResumeThreadAsync(thread.Id);

        await DrainAsync(secondService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        Assert.Equal(
            [
                "user:first",
                "assistant:first answer",
                "user:fail now",
                "assistant:partial answer",
                "user:follow up"
            ],
            secondChatClient.LastMessages.Select(FormatMessageWithContents).ToList());
    }

    [Fact]
    public async Task SubmitInputAsync_WhenSdkNetworkTimeoutCancellationOccurs_MarksTurnFailed()
    {
        const string timeoutMessage =
            "The operation was cancelled because it exceeded the configured timeout of 0:01:40. " +
            "The default timeout can be adjusted by passing a custom ClientPipelineOptions.NetworkTimeout value " +
            "to the client's constructor.";
        IChatClient chatClient = new ThrowingChatClient(new OperationCanceledException(timeoutMessage));
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var seen = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                seen.Add(signal);
        };

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Equal(
            [SessionThreadRuntimeSignal.TurnStarted, SessionThreadRuntimeSignal.TurnFailed],
            seen);
        var updatedThread = await svc.GetThreadAsync(thread.Id);
        var turn = Assert.Single(updatedThread.Turns);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal(timeoutMessage, turn.Error);
        var errorItem = Assert.Single(turn.Items, item => item.Type == ItemType.Error);
        var payload = Assert.IsType<ErrorPayload>(errorItem.Payload);
        Assert.Equal("agent_error", payload.Code);
        Assert.True(payload.Fatal);
        Assert.Equal(timeoutMessage, payload.Message);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenStreamRetrySucceeds_EmitsStreamErrorAndCompletes()
    {
        var inner = new ThrowingThenUpdatesChatClient(
            new IOException("stream closed before completion"),
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        IChatClient chatClient = new StreamRetryingChatClient(
            inner,
            new StreamRetryOptions(1, TimeSpan.FromSeconds(30)));
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Equal(2, inner.Calls);
        var streamError = Assert.Single(
            events.Select(evt => evt.SystemEventPayload),
            payload => payload?.Kind == "streamError");
        Assert.Equal("Reconnecting... 1/1", streamError?.Message);
        Assert.Contains(events, evt => evt.EventType == SessionEventType.TurnCompleted);
        var updatedThread = await svc.GetThreadAsync(thread.Id);
        Assert.Equal(TurnStatus.Completed, Assert.Single(updatedThread.Turns).Status);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenRetryableStreamFailsAfterVisibleUpdate_DoesNotEmitStreamErrorAndFails()
    {
        IChatClient inner = new ThrowingAfterUpdatesChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial")])],
            new IOException("stream closed after partial output"));
        IChatClient chatClient = new StreamRetryingChatClient(
            inner,
            new StreamRetryOptions(1, TimeSpan.FromSeconds(30)));
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.DoesNotContain(events, evt => evt.SystemEventPayload?.Kind == "streamError");
        Assert.Contains(events, evt => evt.EventType == SessionEventType.TurnFailed);
        var updatedThread = await svc.GetThreadAsync(thread.Id);
        Assert.Equal(TurnStatus.Failed, Assert.Single(updatedThread.Turns).Status);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenModelFinishesWithLength_MarksTurnFailed()
    {
        IChatClient chatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial answer")]),
            new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.Length
            }
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var seen = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                seen.Add(signal);
        };

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Equal(
            [SessionThreadRuntimeSignal.TurnStarted, SessionThreadRuntimeSignal.TurnFailed],
            seen);
        Assert.DoesNotContain(events, evt => evt.EventType == SessionEventType.TurnCompleted);
        Assert.Contains(events, evt => evt.EventType == SessionEventType.TurnFailed);
        var updatedThread = await svc.GetThreadAsync(thread.Id);
        var turn = Assert.Single(updatedThread.Turns);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        Assert.Equal(
            "The model response was truncated because it reached the provider output token limit.",
            turn.Error);
        var messageItem = Assert.Single(turn.Items, item => item.Type == ItemType.AgentMessage);
        var messagePayload = Assert.IsType<AgentMessagePayload>(messageItem.Payload);
        Assert.Equal("partial answer", messagePayload.Text);
        var errorItem = Assert.Single(turn.Items, item => item.Type == ItemType.Error);
        var errorPayload = Assert.IsType<ErrorPayload>(errorItem.Payload);
        Assert.Equal("agent_length_limit", errorPayload.Code);
        Assert.True(errorPayload.Fatal);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenProviderStreamIsEmpty_MarksTurnFailed()
    {
        IChatClient chatClient = new FakeChatClient([]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient, useStreamingFunctionInvoker: true);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.DoesNotContain(events, evt => evt.EventType == SessionEventType.TurnCompleted);
        Assert.Contains(events, evt => evt.EventType == SessionEventType.TurnFailed);
        var updatedThread = await svc.GetThreadAsync(thread.Id);
        var turn = Assert.Single(updatedThread.Turns);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        var errorItem = Assert.Single(turn.Items, item => item.Type == ItemType.Error);
        var errorPayload = Assert.IsType<ErrorPayload>(errorItem.Payload);
        Assert.Equal("agent_empty_response", errorPayload.Code);
        Assert.True(errorPayload.Fatal);
        Assert.Contains("empty streaming response", errorPayload.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(turn.Items, item => item.Type == ItemType.AgentMessage);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenProviderStreamHasOnlyUsage_MarksTurnFailed()
    {
        IChatClient chatClient = new FakeChatClient(
        [
            UsageUpdate(requestIndex: 1, input: 10_000, output: 0, cachedInput: 9_000)
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient, useStreamingFunctionInvoker: true);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.DoesNotContain(events, evt => evt.EventType == SessionEventType.TurnCompleted);
        Assert.Contains(events, evt => evt.EventType == SessionEventType.TurnFailed);
        var updatedThread = await svc.GetThreadAsync(thread.Id);
        var turn = Assert.Single(updatedThread.Turns);
        Assert.Equal(TurnStatus.Failed, turn.Status);
        var errorItem = Assert.Single(turn.Items, item => item.Type == ItemType.Error);
        var errorPayload = Assert.IsType<ErrorPayload>(errorItem.Payload);
        Assert.Equal("agent_empty_response", errorPayload.Code);
        Assert.True(errorPayload.Fatal);
        Assert.DoesNotContain(turn.Items, item => item.Type == ItemType.AgentMessage);
    }

    [Fact]
    public async Task SubmitInputAsync_PassesCapturedPromptRequestSnapshotToMemoryForkConsolidator()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        var consolidator = new CapturingForkConsolidator();
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            configureConfig: config => config.Memory.ConsolidateEveryNTurns = 1,
            memoryConsolidator: consolidator);
        var defaultAgent = new StreamingFunctionInvokingChatClient(chatClient).AsAIAgent(
            new ChatClientAgentOptions
            {
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions
                {
                    Instructions = "stable base",
                    ModelId = "gpt-test"
                }
            });
        var svc = new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        var snapshot = await consolidator.WaitForSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.Equal(thread.Id, snapshot.ThreadId);
        Assert.Equal("agent", snapshot.Mode);
        Assert.Equal("stable base", snapshot.BaseInstructions);
        Assert.Equal("gpt-test", snapshot.ModelId);
        Assert.Contains(snapshot.Messages, message => message.Role == ChatRole.User && message.Text.Contains("hello", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ManualCompactionInvalidatesSnapshotBeforeExplicitMemoryConsolidation()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        var consolidator = new CapturingForkConsolidator();
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            configureConfig: config =>
            {
                ConfigureSmallCompaction(config);
                config.Memory.AutoConsolidateEnabled = false;
            },
            memoryConsolidator: consolidator,
            compactionChatClient: new SummaryChatClient("<summary>compacted history</summary>"));
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("first " + new string('u', 600))]));
        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("second " + new string('u', 600))]));
        var compactResult = await svc.CompactThreadAsync(thread.Id);
        Assert.Equal("partial", compactResult.Outcome);

        await svc.ConsolidateThreadMemoryAsync(thread.Id);
        var snapshot = await consolidator.WaitForSnapshotAsync();

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task SubmitInputAsync_ColdCacheMicroCompactionDoesNotPersistSystemNotice()
    {
        var updates = new List<ChatResponseUpdate>();
        for (var i = 0; i < 4; i++)
        {
            var callId = $"call-{i}";
            updates.Add(new ChatResponseUpdate(ChatRole.Assistant, [
                new FunctionCallContent(callId, "ReadFile", new Dictionary<string, object?>())
            ]));
            updates.Add(new ChatResponseUpdate(ChatRole.Assistant, [
                new FunctionResultContent(callId, new string('r', 30_000))
            ]));
        }
        updates.Add(new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("done")]));

        static void ConfigureAnthropicProvider(AppConfig config)
        {
            config.ProviderId = "anthropic";
            config.Model = "claude-test";
            config.Providers.Clear();
            config.Providers["anthropic"] = new AppConfig.ModelProviderConfig
            {
                DisplayName = "Anthropic",
                Protocol = ModelProviderProtocols.Anthropic,
                ApiKey = "sk-ant-test",
                EndPoint = "https://api.anthropic.com"
            };
        }

        IChatClient seedChatClient = new FakeChatClient([.. updates]);
        await using var seedFactory = CreateAgentFactory(
            seedChatClient,
            configureConfig: config =>
            {
                ConfigureAnthropicProvider(config);
                config.Memory.AutoConsolidateEnabled = false;
            });
        var seedService = CreateService(seedFactory, seedChatClient);
        var thread = await seedService.CreateThreadAsync(MakeIdentity());
        await DrainAsync(seedService.SubmitInputAsync(thread.Id, [new TextContent("seed tools")]));

        IChatClient compactChatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("after micro")])
        ]);
        await using var agentFactory = CreateAgentFactory(
            compactChatClient,
            configureConfig: config =>
            {
                ConfigureAnthropicProvider(config);
                config.Memory.AutoConsolidateEnabled = false;
                config.CompactionContextWindowExplicit = true;
                config.Compaction.ContextWindow = 50_000;
                config.Compaction.SummaryReserveTokens = 5_000;
                config.Compaction.AutoCompactBufferTokens = 1_000;
                config.Compaction.WarningBufferTokens = 1_000;
                config.Compaction.ErrorBufferTokens = 500;
                config.Compaction.KeepRecentMinTokens = 1;
                config.Compaction.KeepRecentMinGroups = 1;
                config.Compaction.KeepRecentMaxTokens = 1_000;
                config.Compaction.MicrocompactEnabled = true;
                config.Compaction.MicrocompactTriggerCount = 1;
                config.Compaction.MicrocompactKeepRecent = 1;
                config.Compaction.MicrocompactGapMinutes = 5;
            },
            compactionChatClient: new SummaryChatClient("<summary>should not be needed</summary>"));
        var svc = CreateService(agentFactory, compactChatClient, useStreamingFunctionInvoker: true);
        await svc.ResumeThreadAsync(thread.Id);
        await new ThreadStore(_tempDir).SaveContextUsageTokensAsync(
            thread.Id,
            50_000,
            source: "history_estimate",
            isEstimate: true);
        var liveThread = await svc.GetThreadAsync(thread.Id);
        liveThread.LastActiveAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("continue")]));

        var compacting = Assert.Single(
            events.Select(evt => evt.SystemEventPayload).OfType<SystemEventPayload>(),
            payload => payload.Kind == "compacting");
        Assert.Null(compacting.ContextUsage);
        Assert.Contains(events, evt => IsSystemEvent(evt, "compacted"));
        var compacted = Assert.Single(
            events.Select(evt => evt.SystemEventPayload).OfType<SystemEventPayload>(),
            payload => payload.Kind == "compacted");
        Assert.NotNull(compacted.ContextUsage);
        Assert.DoesNotContain(events, evt =>
            (evt.EventType == SessionEventType.ItemStarted ||
                evt.EventType == SessionEventType.ItemCompleted) &&
            evt.ItemPayload?.Type == ItemType.SystemNotice);
        var loaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        Assert.DoesNotContain(
            loaded!.Turns.SelectMany(turn => turn.Items),
            item => item.Type == ItemType.SystemNotice &&
                item.Payload is SystemNoticePayload { Kind: "compacted" });
    }

    [Fact]
    public async Task SubmitInputAsync_WhenPreSamplingCompactionFailsAboveBlockingLimit_MarksTurnFailed()
    {
        IChatClient seedChatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("seed ok")])
        ]);
        await using var seedFactory = CreateAgentFactory(seedChatClient, configureConfig: ConfigureSmallCompaction);
        var seedService = CreateService(seedFactory, seedChatClient);
        var thread = await seedService.CreateThreadAsync(MakeIdentity());
        await DrainAsync(seedService.SubmitInputAsync(thread.Id, [new TextContent("seed " + new string('u', 2_000))]));

        var mainChatClient = new RecordingChatClient("main should not run");
        await using var failingFactory = CreateAgentFactory(
            mainChatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new ThrowingChatClient(new InvalidOperationException("summary failed")));
        var svc = CreateService(failingFactory, mainChatClient, useStreamingFunctionInvoker: true);
        await svc.ResumeThreadAsync(thread.Id);
        await new ThreadStore(_tempDir).SaveContextUsageTokensAsync(
            thread.Id,
            50_000,
            source: "history_estimate",
            isEstimate: true);

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("continue")]));

        Assert.Contains(events, evt => IsSystemEvent(evt, "compacting"));
        Assert.Contains(events, evt => IsSystemEvent(evt, "compactFailed"));
        Assert.DoesNotContain(events, evt => evt.EventType == SessionEventType.TurnCompleted);
        Assert.Contains(events, evt => evt.EventType == SessionEventType.TurnFailed);
        Assert.Empty(mainChatClient.LastMessages);
        var updatedThread = await svc.GetThreadAsync(thread.Id);
        var turn = updatedThread.Turns.Last();
        Assert.Equal(TurnStatus.Failed, turn.Status);
        var errorItem = Assert.Single(turn.Items, item => item.Type == ItemType.Error);
        var errorPayload = Assert.IsType<ErrorPayload>(errorItem.Payload);
        Assert.Equal("agent_context_compaction_failed", errorPayload.Code);
        Assert.Contains("Context compaction failed", errorPayload.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitInputAsync_UnverifiedProviderContextDoesNotAutoCompact()
    {
        IChatClient chatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer without compact")])
        ]);
        await using var agentFactory = CreateAgentFactory(
            chatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>unexpected</summary>"));
        var svc = CreateService(agentFactory, chatClient, useStreamingFunctionInvoker: true);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await new ThreadStore(_tempDir).SaveContextUsageTokensAsync(
            thread.Id,
            10_000,
            source: "provider_context",
            isEstimate: false);
        agentFactory.GetOrCreateTokenTracker(thread.Id).Update(10_000, 0);

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("continue")]));

        Assert.DoesNotContain(events, evt => IsSystemEvent(evt, "compacting"));
        Assert.DoesNotContain(events, evt => IsSystemEvent(evt, "compacted"));
        Assert.Contains(events, evt =>
            evt.EventType == SessionEventType.ItemCompleted &&
            evt.ItemPayload?.Type == ItemType.AgentMessage &&
            evt.ItemPayload.AsAgentMessage?.Text == "answer without compact");
    }

    [Fact]
    public async Task SubmitInputAsync_SubAgentJsonStringResult_PersistsSuccessfulToolResult()
    {
        const string resultJson = "{\"childThreadId\":\"thread_child\",\"status\":\"running\",\"profileName\":\"native\"}";
        IChatClient chatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionResultContent("call-1", resultJson)])
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var loaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        var turn = Assert.Single(loaded!.Turns);
        var resultItem = Assert.Single(turn.Items, item => item.Type == ItemType.ToolResult);
        var payload = Assert.IsType<ToolResultPayload>(resultItem.Payload);
        Assert.True(payload.Success);
        Assert.Equal("call-1", payload.CallId);
        Assert.Equal(resultJson, payload.Result);
        using var doc = JsonDocument.Parse(payload.Result);
        Assert.Equal("thread_child", doc.RootElement.GetProperty("childThreadId").GetString());
    }

    [Fact]
    public async Task SubmitInputAsync_ImageToolResult_PersistsAndEmitsContentItems()
    {
        var imageBytes = "abc"u8.ToArray();
        IChatClient chatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [
                new FunctionResultContent(
                    "call-image",
                    (IList<AIContent>)
                    [
                        new TextContent("Image: sample.png (3 bytes, image/png)"),
                        new DataContent(imageBytes, "image/png")
                    ])
            ])
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var loaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        var turn = Assert.Single(loaded!.Turns);
        var resultItem = Assert.Single(turn.Items, item => item.Type == ItemType.ToolResult);
        var payload = Assert.IsType<ToolResultPayload>(resultItem.Payload);
        Assert.True(payload.Success);
        Assert.Equal("call-image", payload.CallId);
        Assert.Contains("Image: sample.png", payload.Result, StringComparison.Ordinal);
        Assert.Contains("[Image (image/png), 3 bytes]", payload.Result, StringComparison.Ordinal);
        var contentItems = Assert.IsAssignableFrom<IReadOnlyList<DotCraft.Plugins.PluginFunctionContentItem>>(payload.ContentItems);
        Assert.Equal(2, contentItems.Count);
        Assert.Equal("Image: sample.png (3 bytes, image/png)", contentItems[0].Text);
        Assert.Equal("image", contentItems[1].Type);
        Assert.Equal("image/png", contentItems[1].MediaType);
        Assert.Equal(Convert.ToBase64String(imageBytes), contentItems[1].DataBase64);

        var completedEvent = Assert.Single(
            events,
            e => e.EventType == SessionEventType.ItemCompleted
                && e.ItemPayload?.Type == ItemType.ToolResult);
        var eventPayload = Assert.IsType<ToolResultPayload>(completedEvent.ItemPayload!.Payload);
        var eventContentItems = Assert.IsAssignableFrom<IReadOnlyList<DotCraft.Plugins.PluginFunctionContentItem>>(eventPayload.ContentItems);
        Assert.Equal("image/png", eventContentItems[1].MediaType);
        Assert.Equal(Convert.ToBase64String(imageBytes), eventContentItems[1].DataBase64);
    }

    [Fact]
    public async Task SubmitInputAsync_HostedImageGeneration_PersistsAsToolResultContentItems()
    {
        var imageBytes = "png-bytes"u8.ToArray();
        IChatClient chatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [
                new HostedImageGenerationContent
                {
                    Id = "ig_123",
                    RevisedPrompt = "A red square",
                    ImageBytes = imageBytes
                }
            ])
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var loaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        var turn = Assert.Single(loaded!.Turns);
        var callItem = Assert.Single(turn.Items, item => item.Type == ItemType.ToolCall);
        var callPayload = Assert.IsType<ToolCallPayload>(callItem.Payload);
        Assert.Equal("image_generation", callPayload.ToolName);
        Assert.Equal("ig_123", callPayload.CallId);

        var resultItem = Assert.Single(turn.Items, item => item.Type == ItemType.ToolResult);
        var payload = Assert.IsType<ToolResultPayload>(resultItem.Payload);
        Assert.True(payload.Success);
        Assert.Equal("ig_123", payload.CallId);
        Assert.Equal("A red square", payload.Result);
        var contentItems = Assert.IsAssignableFrom<IReadOnlyList<DotCraft.Plugins.PluginFunctionContentItem>>(payload.ContentItems);
        Assert.Equal("image", contentItems[1].Type);
        Assert.Equal("image/png", contentItems[1].MediaType);
        Assert.Equal(Convert.ToBase64String(imageBytes), contentItems[1].DataBase64);

        var savedPath = Path.Combine(_tempDir, ".craft", "generated_images", thread.Id, "ig_123.png");
        Assert.True(File.Exists(savedPath));
        Assert.Equal(imageBytes, await File.ReadAllBytesAsync(savedPath));

        var completedEvent = Assert.Single(
            events,
            e => e.EventType == SessionEventType.ItemCompleted
                && e.ItemPayload?.Type == ItemType.ToolResult);
        var eventPayload = Assert.IsType<ToolResultPayload>(completedEvent.ItemPayload!.Payload);
        var eventContentItems = Assert.IsAssignableFrom<IReadOnlyList<DotCraft.Plugins.PluginFunctionContentItem>>(eventPayload.ContentItems);
        Assert.Equal(Convert.ToBase64String(imageBytes), eventContentItems[1].DataBase64);
    }

    [Fact]
    public async Task SubmitInputAsync_SpawnAgentArgumentsDelta_CompletesSameToolCallItem()
    {
        IChatClient chatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new ToolCallArgumentsDeltaContent
            {
                ToolCallIndex = 0,
                ToolName = "SpawnAgent",
                CallId = "call-spawn",
                ArgumentsDelta = "{\"agentPrompt\":\"Inspect tests"
            }]),
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(
                callId: "call-spawn",
                name: "SpawnAgent",
                arguments: new Dictionary<string, object?>
                {
                    ["agentPrompt"] = "Inspect tests",
                    ["agentNickname"] = "tester"
                })]),
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionResultContent(
                "call-spawn",
                "{\"childThreadId\":\"thread_child\",\"status\":\"running\"}")])
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var deltaEvent = Assert.Single(events, e => e.ToolCallArgumentsDeltaPayload != null);
        Assert.Equal("SpawnAgent", deltaEvent.ToolCallArgumentsDeltaPayload!.ToolName);
        Assert.Equal("call-spawn", deltaEvent.ToolCallArgumentsDeltaPayload.CallId);

        var completedToolCall = Assert.Single(
            events,
            e => e.EventType == SessionEventType.ItemCompleted
                && e.ItemPayload?.Type == ItemType.ToolCall
                && e.ItemPayload.Payload is ToolCallPayload { ToolName: "SpawnAgent" });
        Assert.Equal(deltaEvent.ItemId, completedToolCall.ItemId);
        var payload = Assert.IsType<ToolCallPayload>(completedToolCall.ItemPayload!.Payload);
        Assert.Equal("tester", payload.Arguments?["agentNickname"]?.ToString());
    }

    [Fact]
    public async Task SubmitInputAsync_SpawnAgentMessageArguments_TracksTaskNameProgressLabel()
    {
        SubAgentProgressBridge.Remove("inspect");
        try
        {
            IChatClient chatClient = new FakeChatClient([
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(
                    callId: "call-spawn",
                    name: "SpawnAgent",
                    arguments: new Dictionary<string, object?>
                    {
                        ["message"] = "Inspect tests",
                        ["taskName"] = "inspect"
                    })]),
                new ChatResponseUpdate(ChatRole.Assistant, [new FunctionResultContent(
                    "call-spawn",
                    "{\"childThreadId\":\"thread_child\",\"status\":\"running\"}")])
            ]);
            await using var agentFactory = CreateAgentFactory(chatClient);
            var svc = CreateService(agentFactory, chatClient);
            var thread = await svc.CreateThreadAsync(MakeIdentity());

            var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

            var progressEvent = events.LastOrDefault(e => e.EventType == SessionEventType.SubAgentProgress);
            Assert.NotNull(progressEvent);
            var entry = Assert.Single(progressEvent!.SubAgentProgressPayload!.Entries);
            Assert.Equal("inspect", entry.Label);
        }
        finally
        {
            SubAgentProgressBridge.Remove("inspect");
        }
    }

    [Fact]
    public async Task SubmitInputAsync_ReasoningAroundTool_PersistsSeparateReasoningItems()
    {
        IChatClient chatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("I need ")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("a tool")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new ToolCallArgumentsDeltaContent
            {
                ToolCallIndex = 0,
                ToolName = "ExampleTool",
                CallId = "call-1",
                ArgumentsDelta = "{\"path\":\"a.txt\"}"
            }]),
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(
                callId: "call-1",
                name: "ExampleTool",
                arguments: new Dictionary<string, object?> { ["path"] = "a.txt" })]),
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionResultContent("call-1", "tool result")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("Now ")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("answer")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("done")])
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var loaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        var turn = Assert.Single(loaded!.Turns);
        var assistantItems = turn.Items.Where(item => item.Type != ItemType.UserMessage).ToList();

        Assert.Equal(
            [
                ItemType.ReasoningContent,
                ItemType.ToolCall,
                ItemType.ToolResult,
                ItemType.ReasoningContent,
                ItemType.AgentMessage
            ],
            assistantItems.Select(item => item.Type));

        Assert.Equal("I need a tool", Assert.IsType<ReasoningContentPayload>(assistantItems[0].Payload).Text);
        Assert.Equal("Now answer", Assert.IsType<ReasoningContentPayload>(assistantItems[3].Payload).Text);
        Assert.Equal("done", Assert.IsType<AgentMessagePayload>(assistantItems[4].Payload).Text);
        Assert.All(
            assistantItems.Where(item => item.Type == ItemType.ReasoningContent),
            item => Assert.NotNull(item.CompletedAt));
    }

    [Fact]
    public async Task SubmitInputAsync_ReasoningBeforeAgentMessage_FinalizesBeforeMessage()
    {
        IChatClient chatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("First ")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("thought")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")])
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var loaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        var turn = Assert.Single(loaded!.Turns);
        var assistantItems = turn.Items.Where(item => item.Type != ItemType.UserMessage).ToList();

        Assert.Equal([ItemType.ReasoningContent, ItemType.AgentMessage], assistantItems.Select(item => item.Type));
        Assert.Equal("First thought", Assert.IsType<ReasoningContentPayload>(assistantItems[0].Payload).Text);
        Assert.Equal("answer", Assert.IsType<AgentMessagePayload>(assistantItems[1].Payload).Text);
        Assert.NotNull(assistantItems[0].CompletedAt);
    }

    [Fact]
    public async Task SubAgentEdgeChanges_InvokeGraphChangedBroadcastHook()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var parent = await svc.CreateThreadAsync(MakeIdentity(), threadId: "parent-1");
        var child = await svc.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = SubAgentThreadOrigin.ChannelName,
                UserId = "u",
                ChannelContext = parent.Id,
                WorkspacePath = _tempDir
            },
            threadId: "child-1",
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parent.Id,
                RootThreadId = parent.Id,
                Depth = 1
            }));
        var seen = new List<(string parentThreadId, string childThreadId)>();
        svc.SubAgentGraphChangedForBroadcast = (parentThreadId, childThreadId) =>
            seen.Add((parentThreadId, childThreadId));

        await svc.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = parent.Id,
            ChildThreadId = child.Id,
            Status = ThreadSpawnEdgeStatus.Open
        });
        await svc.SetThreadSpawnEdgeStatusAsync(
            parent.Id,
            child.Id,
            ThreadSpawnEdgeStatus.Closed);

        Assert.Equal(
            [("parent-1", "child-1"), ("parent-1", "child-1")],
            seen);
    }

    [Fact]
    public async Task CreateThreadAsync_TopLevelThread_SetsAgentControlToolsFullInToolContext()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        var recorder = new RecordingToolProvider();
        await using var agentFactory = CreateAgentFactory(chatClient, [recorder]);
        var svc = CreateService(agentFactory, chatClient);

        var thread = await svc.CreateThreadAsync(
            MakeIdentity(),
            config: new ThreadConfiguration(),
            threadId: "top-policy");

        var seen = Assert.Single(recorder.Contexts, context => context.CurrentThreadId == thread.Id);
        Assert.Equal(AgentControlToolAccess.Full, seen.AgentControlToolAccess);
    }

    [Fact]
    public async Task CreateThreadAsync_SubAgentThread_SetsAgentControlToolsDisabledInToolContext()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        var recorder = new RecordingToolProvider();
        await using var agentFactory = CreateAgentFactory(chatClient, [recorder]);
        var svc = CreateService(agentFactory, chatClient);

        var child = await svc.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = SubAgentThreadOrigin.ChannelName,
                UserId = "u",
                ChannelContext = "parent-policy",
                WorkspacePath = _tempDir
            },
            config: new ThreadConfiguration(),
            threadId: "child-policy",
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = "parent-policy",
                RootThreadId = "parent-policy",
                Depth = 1
            }));

        var seen = Assert.Single(recorder.Contexts, context => context.CurrentThreadId == child.Id);
        Assert.Equal(AgentControlToolAccess.Disabled, seen.AgentControlToolAccess);
    }

    [Fact]
    public async Task SubmitInputAsync_RecordsServerManagedUsage_InTokenUsageStore()
    {
        IChatClient chatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]),
            UsageUpdate(requestIndex: 1, input: 12_000, output: 1, cachedInput: 8_000),
            UsageUpdate(requestIndex: 2, input: 20_000, output: 2, cachedInput: 18_000),
            UsageUpdate(requestIndex: 3, input: 41_000, output: 8, cachedInput: 40_000)
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var tokenUsageStore = new TokenUsageStore(_tempDir);
        var svc = CreateService(agentFactory, chatClient, tokenUsageStore);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(
            thread.Id,
            [new TextContent("hello")],
            new SenderContext
            {
                SenderId = "user-42",
                SenderName = "Alice",
                GroupId = "group-9"
            }));

        var summary = Assert.Single(tokenUsageStore.GetSourceSummaries());
        Assert.Equal("test", summary.SourceId);
        Assert.Equal(TokenUsageSourceModes.ServerManaged, summary.SourceMode);
        Assert.Equal(TokenUsageSubjectKinds.User, summary.SubjectKind);
        Assert.Equal(TokenUsageContextKinds.Group, summary.ContextKind);
        Assert.Equal(73_000, summary.TotalInputTokens);
        Assert.Equal(66_000, summary.TotalCachedInputTokens);
        Assert.Equal(7_000, summary.TotalFreshInputTokens);
        Assert.Equal(11, summary.TotalOutputTokens);
        Assert.Equal(73_011, summary.TotalTokens);
        Assert.Equal(3, summary.LlmCallCount);

        var contextUsage = svc.TryGetContextUsageSnapshot(thread.Id);
        Assert.Equal(41_008, contextUsage?.Tokens);

        var subject = Assert.Single(tokenUsageStore.GetSubjectBreakdown("test"));
        Assert.Equal("user-42", subject.Id);
        Assert.Equal("Alice", subject.Label);
        Assert.Equal(73_011, subject.TotalTokens);

        var context = Assert.Single(tokenUsageStore.GetContextBreakdown("test"));
        Assert.Equal("group-9", context.Id);
        Assert.Equal("group-9", context.Label);
        Assert.Equal(1, context.RelatedSubjectCount);
    }

    [Fact]
    public async Task SubmitInputAsync_BaseInstructionDriftKeepsProviderAnchorForAutoCompactDecision()
    {
        const string oldInstructions = "stable memory";
        var newInstructions = "stable memory\n" + new string('m', 1_000);
        var largePersistedUserMessage = new string('u', 480_000);

        static void ConfigureRegressionWindow(AppConfig config)
        {
            config.Memory.AutoConsolidateEnabled = false;
            config.CompactionContextWindowExplicit = true;
            config.Compaction.ContextWindow = 130_000;
            config.Compaction.SummaryReserveTokens = 0;
            config.Compaction.AutoCompactBufferTokens = 20_000;
            config.Compaction.WarningBufferTokens = 500;
            config.Compaction.ErrorBufferTokens = 250;
            config.Compaction.KeepRecentMinTokens = 1;
            config.Compaction.KeepRecentMinGroups = 1;
            config.Compaction.KeepRecentMaxTokens = 1_000;
            config.Compaction.MicrocompactEnabled = false;
        }

        IChatClient seedChatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("seed ok")]),
            UsageUpdate(requestIndex: 1, input: 103_000, output: 0, cachedInput: 100_000)
        ]);
        await using var seedFactory = CreateAgentFactory(seedChatClient, configureConfig: ConfigureRegressionWindow);
        var seedService = CreateStreamingServiceWithInstructions(seedFactory, seedChatClient, oldInstructions);
        var thread = await seedService.CreateThreadAsync(MakeIdentity());

        await DrainAsync(seedService.SubmitInputAsync(thread.Id, [new TextContent(largePersistedUserMessage)]));

        var store = new ThreadStore(_tempDir);
        var persistedAnchor = store.LoadContextUsageAnchor(thread.Id);
        Assert.NotNull(persistedAnchor);
        Assert.Equal(103_000, persistedAnchor!.Tokens);
        Assert.NotNull(persistedAnchor.ContextUsageFingerprint);
        Assert.NotNull(persistedAnchor.BaseInstructionsTokenEstimate);

        IChatClient followUpChatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("follow ok")])
        ]);
        await using var followUpFactory = CreateAgentFactory(
            followUpChatClient,
            configureConfig: ConfigureRegressionWindow,
            compactionChatClient: new SummaryChatClient("<summary>unexpected compact</summary>"));
        var followUpService = CreateStreamingServiceWithInstructions(followUpFactory, followUpChatClient, newInstructions);
        await followUpService.ResumeThreadAsync(thread.Id);

        var events = await CollectAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("continue")]));

        Assert.DoesNotContain(events, evt => IsSystemEvent(evt, "compacting"));
        Assert.DoesNotContain(events, evt => IsSystemEvent(evt, "compacted"));
    }

    private static ChatResponseUpdate UsageUpdate(int requestIndex, long input, long output, long cachedInput)
        => new()
        {
            Role = ChatRole.Assistant,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [TokenUsageRequestMetadata.RequestIndexKey] = requestIndex
            },
            Contents =
            [
                new UsageContent(new UsageDetails
                {
                    InputTokenCount = input,
                    OutputTokenCount = output,
                    CachedInputTokenCount = cachedInput
                })
            ]
        };

    [Fact]
    public async Task SubmitInputAsync_AppendsSenderRuntimeContext_AndPersistsInitiator()
    {
        var chatClient = new RecordingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "qq",
            UserId = "10001",
            ChannelContext = "group:123456",
            WorkspacePath = _tempDir
        });

        await DrainAsync(svc.SubmitInputAsync(
            thread.Id,
            [new TextContent("hello")],
            new SenderContext
            {
                SenderId = "10001",
                SenderName = "Alice",
                SenderRole = "admin",
                GroupId = "123456"
            }));

        var userMessage = Assert.Single(chatClient.LastMessages, message => message.Role == ChatRole.User);
        var modelInput = string.Concat(userMessage.Contents.OfType<TextContent>().Select(content => content.Text));
        Assert.Contains("<system-reminder>", modelInput);
        Assert.Contains("## Environment", modelInput);
        Assert.Contains("## Mode", modelInput);
        Assert.Contains("CurrentMode: Agent", modelInput);
        Assert.Contains("## Request Source", modelInput);
        Assert.Contains("Channel: qq", modelInput);
        Assert.Contains("Conversation: group:123456", modelInput);
        Assert.Contains("SenderName: Alice", modelInput);
        Assert.DoesNotContain("SenderId: 10001", modelInput);
        var systemInstructions = chatClient.LastOptions?.Instructions ?? string.Empty;
        Assert.DoesNotContain("SenderName: Alice", systemInstructions);
        Assert.DoesNotContain("SenderId: 10001", systemInstructions);

        var persistedThread = await svc.GetThreadAsync(thread.Id);
        var turn = Assert.Single(persistedThread.Turns);
        Assert.Equal("qq", turn.Initiator?.ChannelName);
        Assert.Equal("10001", turn.Initiator?.UserId);
        Assert.Equal("Alice", turn.Initiator?.UserName);
        Assert.Equal("group:123456", turn.Initiator?.ChannelContext);
        Assert.Equal("123456", turn.Initiator?.GroupId);

        Assert.NotNull(turn.Input);
        var payload = turn.Input!.AsUserMessage;
        Assert.NotNull(payload);
        Assert.Equal("10001", payload!.SenderId);
        Assert.Equal("Alice", payload.SenderName);
        Assert.Equal("admin", payload.SenderRole);
        Assert.Equal("group:123456", payload.ChannelContext);
        Assert.Equal("123456", payload.GroupId);
    }

    [Fact]
    public async Task CancelTurnAsync_EmitsTurnStartedThenCancelled()
    {
        IChatClient chatClient = new BlockingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var seen = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                seen.Add(signal);
        };

        var drainTask = DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        await Task.Delay(50);
        await svc.CancelTurnAsync(thread.Id, "turn_001");
        await drainTask;

        Assert.Equal(
            [SessionThreadRuntimeSignal.TurnStarted, SessionThreadRuntimeSignal.TurnCancelled],
            seen);
    }

    [Fact]
    public async Task SubmitInputAsync_InterruptApprovalPolicy_ReturnsToolDenialWithoutCancellingTurn()
    {
        var approvalService = new SessionScopedApprovalService(new AutoApproveApprovalService());
        var chatClient = new ApprovalRequestingChatClient(approvalService);
        await using var agentFactory = CreateAgentFactory(chatClient, approvalService: approvalService);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        thread.Configuration ??= new ThreadConfiguration();
        thread.Configuration.ApprovalPolicy = ApprovalPolicy.Interrupt;
        var seen = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                seen.Add(signal);
        };

        var events = await CollectAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var failed = events.FirstOrDefault(evt => evt.EventType == SessionEventType.TurnFailed);
        Assert.True(failed == null, failed?.TurnFailedPayload?.Error);
        Assert.DoesNotContain(events, evt => evt.EventType == SessionEventType.TurnCancelled);
        Assert.Contains(SessionThreadRuntimeSignal.TurnCompleted, seen);
        var loaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        var turn = Assert.Single(loaded!.Turns);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        var message = Assert.Single(turn.Items, item => item.Type == ItemType.AgentMessage);
        Assert.Contains("approval=False", Assert.IsType<AgentMessagePayload>(message.Payload).Text);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenCancelledAfterPreSamplingCompaction_PreservesCompactedSession()
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

        var store = new ThreadStore(_tempDir);
        await store.SaveContextUsageTokensAsync(
            thread.Id,
            9_500,
            source: "history_estimate",
            isEstimate: true);

        var blockingChatClient = new RecordingBlockingChatClient();
        await using var compactFactory = CreateAgentFactory(
            blockingChatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>compacted old context</summary>"));
        var compactService = CreateService(compactFactory, blockingChatClient, useStreamingFunctionInvoker: true);
        await compactService.ResumeThreadAsync(thread.Id);

        var cancelEventsTask = CollectAsync(compactService.SubmitInputAsync(
            thread.Id,
            [new TextContent("cancel after compact")]));
        await blockingChatClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(blockingChatClient.LastMessages.Select(MessageText), text =>
            text.Contains("compacted old context", StringComparison.Ordinal));
        var runningThread = await compactService.GetThreadAsync(thread.Id);
        var runningTurn = Assert.Single(runningThread.Turns, turn => turn.Status == TurnStatus.Running);

        await compactService.CancelTurnAsync(thread.Id, runningTurn.Id);
        var cancelEvents = await cancelEventsTask;
        Assert.Contains(cancelEvents, evt => IsSystemEvent(evt, "compacted"));

        var followUpChatClient = new RecordingChatClient("follow answer");
        await using var followUpFactory = CreateAgentFactory(followUpChatClient, configureConfig: ConfigureSmallCompaction);
        var followUpService = CreateService(followUpFactory, followUpChatClient);
        await followUpService.ResumeThreadAsync(thread.Id);

        await DrainAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        var followUpHistory = followUpChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(followUpHistory, text => text.Contains("compacted old context", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("seed 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SubmitInputAsync_WhenCancelledAfterPreviousCompaction_PreservesCompactedSession()
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

        var compactMainChatClient = new RecordingChatClient("unused");
        await using var compactFactory = CreateAgentFactory(
            compactMainChatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>compacted previous context</summary>"));
        var compactService = CreateService(compactFactory, compactMainChatClient);
        await compactService.ResumeThreadAsync(thread.Id);

        var compactResult = await compactService.CompactThreadAsync(thread.Id);
        Assert.Equal("partial", compactResult.Outcome);

        var blockingChatClient = new RecordingBlockingChatClient();
        await using var cancelFactory = CreateAgentFactory(blockingChatClient, configureConfig: ConfigureSmallCompaction);
        var cancelService = CreateService(cancelFactory, blockingChatClient);
        await cancelService.ResumeThreadAsync(thread.Id);

        var cancelEventsTask = CollectAsync(cancelService.SubmitInputAsync(
            thread.Id,
            [new TextContent("cancel after previous compact")]));
        await blockingChatClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var runningThread = await cancelService.GetThreadAsync(thread.Id);
        var runningTurn = Assert.Single(runningThread.Turns, turn => turn.Status == TurnStatus.Running);

        await cancelService.CancelTurnAsync(thread.Id, runningTurn.Id);
        await cancelEventsTask;

        var followUpChatClient = new RecordingChatClient("follow answer");
        await using var followUpFactory = CreateAgentFactory(followUpChatClient);
        var followUpService = CreateService(followUpFactory, followUpChatClient);
        await followUpService.ResumeThreadAsync(thread.Id);

        await DrainAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        var followUpHistory = followUpChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(followUpHistory, text => text.Contains("compacted previous context", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("seed 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SubmitInputAsync_WhenAgentFailsAfterPreviousCompaction_PreservesCompactedSessionAndFailedTurnTail()
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

        var compactMainChatClient = new RecordingChatClient("unused");
        await using var compactFactory = CreateAgentFactory(
            compactMainChatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>compacted previous context</summary>"));
        var compactService = CreateService(compactFactory, compactMainChatClient);
        await compactService.ResumeThreadAsync(thread.Id);

        var compactResult = await compactService.CompactThreadAsync(thread.Id);
        Assert.Equal("partial", compactResult.Outcome);

        IChatClient failingChatClient = new FakeChatClient(
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial failure")]),
                new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("fail-call", "ReadFile", new Dictionary<string, object?>())
                ]),
                new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionResultContent("fail-call", "tool ok")
                ]),
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.Length
                }
            ]);
        await using var failFactory = CreateAgentFactory(failingChatClient, configureConfig: ConfigureSmallCompaction);
        var failService = CreateService(failFactory, failingChatClient);
        await failService.ResumeThreadAsync(thread.Id);

        await DrainAsync(failService.SubmitInputAsync(thread.Id, [new TextContent("fail after compact")]));

        var failedThread = await failService.GetThreadAsync(thread.Id);
        var failedTurn = failedThread.Turns.Last();
        Assert.Equal(TurnStatus.Failed, failedTurn.Status);

        var followUpChatClient = new RecordingChatClient("follow answer");
        await using var followUpFactory = CreateAgentFactory(followUpChatClient);
        var followUpService = CreateService(followUpFactory, followUpChatClient);
        await followUpService.ResumeThreadAsync(thread.Id);

        await DrainAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        var followUpHistory = followUpChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(followUpHistory, text => text.Contains("compacted previous context", StringComparison.Ordinal));
        Assert.Contains(followUpHistory, text => text.Contains("fail after compact", StringComparison.Ordinal));
        Assert.Contains(followUpHistory, text => text.Contains("partial failure", StringComparison.Ordinal));
        Assert.Contains(followUpHistory, text => text.Contains("function_call:ReadFile:fail-call", StringComparison.Ordinal));
        Assert.Contains(followUpHistory, text => text.Contains("function_result:fail-call:tool ok", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("seed 0", StringComparison.Ordinal));
        Assert.True(followUpHistory.Count < 10, string.Join(Environment.NewLine, followUpHistory));
    }

    [Fact]
    public async Task SubmitInputAsync_WhenProviderThrowsAfterPreviousCompaction_PreservesCompactedSessionAndPartialAssistant()
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

        var compactMainChatClient = new RecordingChatClient("unused");
        await using var compactFactory = CreateAgentFactory(
            compactMainChatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>compacted previous context</summary>"));
        var compactService = CreateService(compactFactory, compactMainChatClient);
        await compactService.ResumeThreadAsync(thread.Id);

        var compactResult = await compactService.CompactThreadAsync(thread.Id);
        Assert.Equal("partial", compactResult.Outcome);

        IChatClient failingChatClient = new ThrowingAfterUpdatesChatClient(
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial provider failure")])],
            new InvalidOperationException("provider boom"));
        await using var failFactory = CreateAgentFactory(failingChatClient, configureConfig: ConfigureSmallCompaction);
        var failService = CreateService(failFactory, failingChatClient);
        await failService.ResumeThreadAsync(thread.Id);

        await DrainAsync(failService.SubmitInputAsync(thread.Id, [new TextContent("provider fail after compact")]));

        var followUpChatClient = new RecordingChatClient("follow answer");
        await using var followUpFactory = CreateAgentFactory(followUpChatClient);
        var followUpService = CreateService(followUpFactory, followUpChatClient);
        await followUpService.ResumeThreadAsync(thread.Id);

        await DrainAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        var followUpHistory = followUpChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(followUpHistory, text => text.Contains("compacted previous context", StringComparison.Ordinal));
        Assert.Contains(followUpHistory, text => text.Contains("provider fail after compact", StringComparison.Ordinal));
        Assert.Contains(followUpHistory, text => text.Contains("partial provider failure", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("seed 0", StringComparison.Ordinal));
        Assert.True(followUpHistory.Count < 10, string.Join(Environment.NewLine, followUpHistory));
    }

    [Fact]
    public async Task RollbackThreadAsync_AfterCompaction_TrimsSessionTailWithoutRestoringOldHistory()
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

        var compactMainChatClient = new RecordingChatClient("unused");
        await using var compactFactory = CreateAgentFactory(
            compactMainChatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>compacted previous context</summary>"));
        var compactService = CreateService(compactFactory, compactMainChatClient);
        await compactService.ResumeThreadAsync(thread.Id);
        var compactResult = await compactService.CompactThreadAsync(thread.Id);
        Assert.Equal("partial", compactResult.Outcome);

        var rollbackChatClient = new RecordingChatClient("rolled back answer");
        await using var rollbackFactory = CreateAgentFactory(rollbackChatClient);
        var rollbackService = CreateService(rollbackFactory, rollbackChatClient);
        await rollbackService.ResumeThreadAsync(thread.Id);
        await DrainAsync(rollbackService.SubmitInputAsync(thread.Id, [new TextContent("rolled back request")]));
        await rollbackService.RollbackThreadAsync(thread.Id, 1);

        var followUpChatClient = new RecordingChatClient("follow answer");
        await using var followUpFactory = CreateAgentFactory(followUpChatClient);
        var followUpService = CreateService(followUpFactory, followUpChatClient);
        await followUpService.ResumeThreadAsync(thread.Id);
        await DrainAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        var followUpHistory = followUpChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(followUpHistory, text => text.Contains("compacted previous context", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("seed 0", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("rolled back request", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("rolled back answer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RollbackThreadAsync_RecordsThreadRollbackTraceEvent()
    {
        var traceStore = new TraceStore();
        var traceCollector = new TraceCollector(traceStore);
        var chatClient = new RecordingChatClient("answer");
        await using var factory = CreateAgentFactory(chatClient);
        var service = CreateService(factory, chatClient, traceCollector: traceCollector);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("request")]));
        await service.RollbackThreadAsync(thread.Id, 1);

        var evt = Assert.Single(traceStore.GetEvents(thread.Id), e => e.Type == TraceEventType.ThreadRollback);
        Assert.Equal("Rollback removed 1 turn(s)", evt.Content);
        using var metadata = JsonDocument.Parse(evt.MetadataJson!);
        Assert.Equal(thread.Id, metadata.RootElement.GetProperty("threadId").GetString());
        Assert.Equal(1, metadata.RootElement.GetProperty("numTurns").GetInt32());
        Assert.Equal(0, metadata.RootElement.GetProperty("remainingTurns").GetInt32());
    }

    [Fact]
    public async Task RollbackThreadAsync_RemovesProviderTokenTrackerAndSavesHistoryEstimate()
    {
        IChatClient chatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")]),
            UsageUpdate(requestIndex: 1, input: 103_000, output: 200, cachedInput: 100_000)
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first " + new string('u', 1_000))]));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("second " + new string('u', 1_000))]));

        var trackerBeforeRollback = agentFactory.TryGetTokenTracker(thread.Id);
        Assert.NotNull(trackerBeforeRollback);
        Assert.Equal(103_200, trackerBeforeRollback!.LastContextTokens);
        var providerSnapshot = service.TryGetContextUsageSnapshot(thread.Id);
        Assert.NotNull(providerSnapshot);
        Assert.Equal(103_200, providerSnapshot!.Tokens);
        Assert.Equal("provider_context", providerSnapshot.Source);
        Assert.False(providerSnapshot.IsEstimate);

        await service.RollbackThreadAsync(thread.Id, 1);

        Assert.Null(agentFactory.TryGetTokenTracker(thread.Id));
        var rollbackSnapshot = service.TryGetContextUsageSnapshot(thread.Id);
        Assert.NotNull(rollbackSnapshot);
        Assert.Equal("history_estimate", rollbackSnapshot!.Source);
        Assert.True(rollbackSnapshot.IsEstimate);
        Assert.NotEqual(103_200, rollbackSnapshot.Tokens);
        Assert.Null(new ThreadStore(_tempDir).LoadContextUsageAnchor(thread.Id));
    }

    [Fact]
    public async Task RollbackThreadAsync_WhenRejected_DoesNotRecordThreadRollbackTraceEvent()
    {
        var traceStore = new TraceStore();
        var traceCollector = new TraceCollector(traceStore);
        var chatClient = new RecordingChatClient("answer");
        await using var factory = CreateAgentFactory(chatClient);
        var service = CreateService(factory, chatClient, traceCollector: traceCollector);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RollbackThreadAsync(thread.Id, 0));

        Assert.DoesNotContain(traceStore.GetEvents(thread.Id), e => e.Type == TraceEventType.ThreadRollback);
    }

    [Fact]
    public async Task RollbackThreadAsync_WithOnlyCompactedSessionCheckpointMissing_TrimsPersistedSessionTail()
    {
        var seedChatClient = new RecordingChatClient("seed answer");
        await using var seedFactory = CreateAgentFactory(seedChatClient);
        var seedService = CreateService(seedFactory, seedChatClient);
        var thread = await seedService.CreateThreadAsync(MakeIdentity());

        await DrainAsync(seedService.SubmitInputAsync(thread.Id, [new TextContent("seed request")]));
        var rolledBackTurnChatClient = new RecordingChatClient("rolled back answer");
        await using var rolledBackTurnFactory = CreateAgentFactory(rolledBackTurnChatClient);
        var rolledBackTurnService = CreateService(rolledBackTurnFactory, rolledBackTurnChatClient);
        await rolledBackTurnService.ResumeThreadAsync(thread.Id);
        await DrainAsync(rolledBackTurnService.SubmitInputAsync(thread.Id, [new TextContent("rolled back request")]));
        await SaveSyntheticSessionAsync(
            thread.Id,
            [
                new ChatMessage(ChatRole.Assistant, "<summary>legacy compacted context</summary>"),
                new ChatMessage(ChatRole.User, "rolled back request"),
                new ChatMessage(ChatRole.Assistant, "rolled back answer")
            ]);

        var rollbackChatClient = new RecordingChatClient("unused");
        await using var rollbackFactory = CreateAgentFactory(rollbackChatClient);
        var rollbackService = CreateService(rollbackFactory, rollbackChatClient);
        await rollbackService.ResumeThreadAsync(thread.Id);
        await rollbackService.RollbackThreadAsync(thread.Id, 1);

        var followUpChatClient = new RecordingChatClient("follow answer");
        await using var followUpFactory = CreateAgentFactory(followUpChatClient);
        var followUpService = CreateService(followUpFactory, followUpChatClient);
        await followUpService.ResumeThreadAsync(thread.Id);
        await DrainAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        var followUpHistory = followUpChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(followUpHistory, text => text.Contains("legacy compacted context", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("seed request", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("rolled back request", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("rolled back answer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SubmitInputAsync_WhenSessionRowMissingAfterCompaction_RebuildsFromCheckpoint()
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

        var compactMainChatClient = new RecordingChatClient("unused");
        await using var compactFactory = CreateAgentFactory(
            compactMainChatClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>checkpoint compacted context</summary>"));
        var compactService = CreateService(compactFactory, compactMainChatClient);
        await compactService.ResumeThreadAsync(thread.Id);
        var compactResult = await compactService.CompactThreadAsync(thread.Id);
        Assert.Equal("partial", compactResult.Outcome);

        DeleteSessionRow(thread.Id);

        var followUpChatClient = new RecordingChatClient("follow answer");
        await using var followUpFactory = CreateAgentFactory(followUpChatClient);
        var followUpService = CreateService(followUpFactory, followUpChatClient);
        await followUpService.ResumeThreadAsync(thread.Id);
        await DrainAsync(followUpService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        var followUpHistory = followUpChatClient.LastMessages.Select(MessageText).ToList();
        Assert.Contains(followUpHistory, text => text.Contains("checkpoint compacted context", StringComparison.Ordinal));
        Assert.DoesNotContain(followUpHistory, text => text.Contains("seed 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeleteThreadPermanentlyAsync_DuringRunningTurn_DoesNotRecreateThreadArtifacts()
    {
        IChatClient chatClient = new BlockingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        Assert.Equal(1, svc.DebugRuntimeCount);

        var drainTask = DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        await Task.Delay(50);

        await svc.DeleteThreadPermanentlyAsync(thread.Id);
        await drainTask;
        Assert.Equal(0, svc.DebugRuntimeCount);

        var store = new ThreadStore(_tempDir);
        Assert.Null(await store.LoadThreadAsync(thread.Id));
        Assert.DoesNotContain(await store.LoadIndexAsync(), summary => summary.Id == thread.Id);
        Assert.False(File.Exists(Path.Combine(_tempDir, "threads", "active", $"{thread.Id}.jsonl")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "threads", "archived", $"{thread.Id}.jsonl")));
    }

    [Fact]
    public async Task SubmitInputAsync_FirstCompletedTurn_PersistsThreadBeforeSessionRow()
    {
        var firstChatClient = new RecordingChatClient("first answer");
        await using var firstFactory = CreateAgentFactory(firstChatClient);
        var firstService = CreateService(firstFactory, firstChatClient);
        var thread = await firstService.CreateThreadAsync(MakeIdentity());

        await DrainAsync(firstService.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var store = new ThreadStore(_tempDir);
        Assert.NotNull(await store.LoadThreadAsync(thread.Id));
        Assert.True(store.SessionFileExists(thread.Id));
        Assert.True(ThreadRowExists(thread.Id));
    }

    [Fact]
    public async Task SubmitInputAsync_AcrossFreshServiceInstance_RestoresPriorConversation()
    {
        var firstChatClient = new RecordingChatClient("first answer");
        await using var firstFactory = CreateAgentFactory(firstChatClient);
        var firstService = CreateService(firstFactory, firstChatClient);
        var thread = await firstService.CreateThreadAsync(MakeIdentity());

        await DrainAsync(firstService.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var secondChatClient = new RecordingChatClient("second answer");
        await using var secondFactory = CreateAgentFactory(secondChatClient);
        var secondService = CreateService(secondFactory, secondChatClient);
        await secondService.ResumeThreadAsync(thread.Id);

        await DrainAsync(secondService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        Assert.Equal(
            ["user:hello", "assistant:first answer", "user:follow up"],
            secondChatClient.LastMessages.Select(FormatMessage).ToList());
    }

    [Fact]
    public async Task SubmitInputAsync_WhenLegacyThreadSessionRowIsMissing_RebuildsHistoryFromRollout()
    {
        var firstChatClient = new RecordingChatClient("first answer");
        await using var firstFactory = CreateAgentFactory(firstChatClient);
        var firstService = CreateService(firstFactory, firstChatClient);
        var thread = await firstService.CreateThreadAsync(MakeIdentity());

        await DrainAsync(firstService.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        DeleteSessionRow(thread.Id);

        var secondChatClient = new RecordingChatClient("second answer");
        await using var secondFactory = CreateAgentFactory(secondChatClient);
        var secondService = CreateService(secondFactory, secondChatClient);
        await secondService.ResumeThreadAsync(thread.Id);

        await DrainAsync(secondService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        Assert.Equal(
            ["user:hello", "assistant:first answer", "user:follow up"],
            secondChatClient.LastMessages.Select(FormatMessage).ToList());
    }

    [Fact]
    public async Task QueuedInputOperations_AreSerializedPerThread()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        var queuedInputs = new List<QueuedTurnInput>();
        for (var i = 0; i < 40; i++)
        {
            var text = $"queued {i}";
            queuedInputs.Add(await svc.EnqueueTurnInputAsync(
                thread.Id,
                [new TextContent(text)],
                inputSnapshot: new SessionInputSnapshot
                {
                    NativeInputParts = [new SessionWireInputPart { Type = "text", Text = text }],
                    MaterializedInputParts = [new SessionWireInputPart { Type = "text", Text = text }],
                    DisplayText = text
                }));
        }

        var operations = queuedInputs.Select(async (queued, index) =>
        {
            if (index % 2 == 0)
                await svc.RemoveQueuedTurnInputAsync(thread.Id, queued.Id);
            else
                await svc.SteerTurnAsync(thread.Id, "turn_001", queued.Id);
        }).ToArray();

        await Task.WhenAll(operations);

        var reloaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(20, reloaded.QueuedInputs.Count);
        Assert.Equal(20, reloaded.QueuedInputs.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(reloaded.QueuedInputs, queued => Assert.Equal("guidancePending", queued.Status));
        Assert.Equal(
            queuedInputs.Where((_, index) => index % 2 == 1).Select(q => q.Id).ToArray(),
            reloaded.QueuedInputs.Select(q => q.Id).ToArray());
    }

    private SessionService CreateService(
        AgentFactory agentFactory,
        IChatClient chatClient,
        TokenUsageStore? tokenUsageStore = null,
        bool useStreamingFunctionInvoker = false,
        TraceCollector? traceCollector = null)
    {
        var defaultAgent = useStreamingFunctionInvoker
            ? new StreamingFunctionInvokingChatClient(chatClient).AsAIAgent(
                new ChatClientAgentOptions { UseProvidedChatClientAsIs = true })
            : chatClient.AsAIAgent(new ChatClientAgentOptions());
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            traceCollector: traceCollector,
            tokenUsageStore: tokenUsageStore);
    }

    private SessionService CreateStreamingServiceWithInstructions(
        AgentFactory agentFactory,
        IChatClient chatClient,
        string instructions)
    {
        var defaultAgent = new StreamingFunctionInvokingChatClient(chatClient).AsAIAgent(
            new ChatClientAgentOptions
            {
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions { Instructions = instructions }
            });
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
    }

    private AgentFactory CreateAgentFactory(
        IChatClient chatClientFactory,
        IReadOnlyList<IAgentToolProvider>? toolProviders = null,
        Action<AppConfig>? configureConfig = null,
        IMemoryConsolidator? memoryConsolidator = null,
        IChatClient? compactionChatClient = null,
        IApprovalService? approvalService = null)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        configureConfig?.Invoke(config);
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: approvalService ?? new SessionScopedApprovalService(new AutoApproveApprovalService()),
            blacklist: null,
            toolProviders: toolProviders ?? Array.Empty<IAgentToolProvider>(),
            memoryConsolidator: memoryConsolidator,
            compactionChatClient: compactionChatClient);
    }

    private SessionIdentity MakeIdentity() => new()
    {
        ChannelName = "test",
        UserId = "u",
        WorkspacePath = _tempDir
    };

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private static async Task<List<SessionEvent>> CollectAsync(IAsyncEnumerable<SessionEvent> events)
    {
        var collected = new List<SessionEvent>();
        await foreach (var evt in events)
            collected.Add(evt);
        return collected;
    }

    private static string FormatMessage(ChatMessage message)
    {
        var text = string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));
        var runtimeContextIndex = text.IndexOf("\n[Runtime Context]", StringComparison.Ordinal);
        if (runtimeContextIndex >= 0)
            text = text[..runtimeContextIndex];
        text = StripSystemReminderBlocks(text);
        return $"{message.Role}:{text.Trim()}";
    }

    private static string FormatMessageWithContents(ChatMessage message)
    {
        var parts = message.Contents.Select(content => content switch
        {
            TextContent text => text.Text,
            FunctionCallContent call => $"function_call:{call.Name}:{call.CallId}",
            FunctionResultContent result => $"function_result:{result.CallId}:{result.Result}",
            _ => content.ToString() ?? string.Empty
        });
        var text = StripSystemReminderBlocks(string.Concat(parts));
        var runtimeContextIndex = text.IndexOf("\n[Runtime Context]", StringComparison.Ordinal);
        if (runtimeContextIndex >= 0)
            text = text[..runtimeContextIndex];
        return $"{message.Role}:{text.Trim()}";
    }

    private static string MessageText(ChatMessage message) =>
        StripSystemReminderBlocks(string.Concat(message.Contents.Select(content => content switch
        {
            TextContent text => text.Text,
            FunctionCallContent call => $"function_call:{call.Name}:{call.CallId}",
            FunctionResultContent result => $"function_result:{result.CallId}:{result.Result}",
            _ => content.ToString() ?? string.Empty
        })));

    private static bool IsSystemEvent(SessionEvent evt, string kind) =>
        evt.EventType == SessionEventType.SystemEvent
        && evt.Payload is SystemEventPayload payload
        && payload.Kind == kind;

    private static string StripSystemReminderBlocks(string input)
    {
        const string openTag = "<system-reminder>";
        const string closeTag = "</system-reminder>";
        var text = input;
        while (true)
        {
            var open = text.IndexOf(openTag, StringComparison.Ordinal);
            if (open < 0)
                return text.TrimEnd();

            var close = text.IndexOf(closeTag, open + openTag.Length, StringComparison.Ordinal);
            if (close < 0)
                return text[..open].TrimEnd();

            text = (text[..open] + text[(close + closeTag.Length)..]).TrimEnd();
        }
    }

    private bool ThreadRowExists(string threadId)
    {
        using var connection = OpenStateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM threads WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteScalar() != null;
    }

    private static void ConfigureSmallCompaction(AppConfig config)
    {
        config.CompactionContextWindowExplicit = true;
        config.Memory.AutoConsolidateEnabled = false;
        config.Compaction.ContextWindow = 10_000;
        config.Compaction.SummaryReserveTokens = 1_000;
        config.Compaction.AutoCompactBufferTokens = 500;
        config.Compaction.WarningBufferTokens = 500;
        config.Compaction.ErrorBufferTokens = 250;
        config.Compaction.KeepRecentMinTokens = 1;
        config.Compaction.KeepRecentMinGroups = 1;
        config.Compaction.KeepRecentMaxTokens = 500;
        config.Compaction.MicrocompactEnabled = false;
    }

    private void DeleteSessionRow(string threadId)
    {
        using var connection = OpenStateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM thread_sessions WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.ExecuteNonQuery();
    }

    private async Task SaveSyntheticSessionAsync(string threadId, IReadOnlyList<ChatMessage> history)
    {
        var agent = new RecordingChatClient("unused").AsAIAgent(new ChatClientAgentOptions());
        var session = await agent.CreateSessionAsync();
        session.SetInMemoryChatHistory([.. history], jsonSerializerOptions: SessionPersistenceJsonOptions.Default);
        await new ThreadStore(_tempDir).SaveSessionAsync(agent, session, threadId);
    }

    private SqliteConnection OpenStateConnection()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_tempDir, "state.db"),
                Mode = SqliteOpenMode.ReadWrite
            }.ToString());
        connection.Open();
        return connection;
    }

    private sealed class FakeChatClient(ChatResponseUpdate[] streamUpdates) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in streamUpdates)
                yield return update;
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Environment.TickCount < 0)
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(string.Empty)]);
            await Task.Yield();
            throw exception;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingAfterUpdatesChatClient(
        ChatResponseUpdate[] streamUpdates,
        Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in streamUpdates)
                yield return update;
            await Task.Yield();
            throw exception;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingThenUpdatesChatClient(
        Exception firstException,
        ChatResponseUpdate[] retryUpdates) : IChatClient
    {
        private int _calls;

        public int Calls => _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            await Task.Yield();
            if (call == 1)
                throw firstException;

            foreach (var update in retryUpdates)
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class BlockingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ApprovalRequestingChatClient(IApprovalService approvalService) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var approved = await approvalService.RequestShellApprovalAsync(
                "dotnet test",
                Directory.GetCurrentDirectory());
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent($"approval={approved}")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingBlockingChatClient : IChatClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingChatClient(string responseText) : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            LastOptions = options;
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            LastOptions = options;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class SummaryChatClient(string responseText) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingToolProvider : IAgentToolProvider
    {
        public int Priority => 10;

        public List<ToolProviderContext> Contexts { get; } = [];

        public IEnumerable<AITool> CreateTools(ToolProviderContext context)
        {
            Contexts.Add(context);
            return [];
        }
    }

    private sealed class CapturingForkConsolidator : IMemoryForkConsolidator
    {
        private readonly TaskCompletionSource<PromptRequestSnapshot?> _snapshotSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MemoryConsolidationResult> ConsolidateAsync(
            IReadOnlyList<ChatMessage> messagesToArchive,
            CancellationToken cancellationToken = default)
        {
            _snapshotSource.TrySetResult(null);
            return Task.FromResult(MemoryConsolidationResult.Skipped("legacy_path"));
        }

        public Task<MemoryConsolidationResult> ConsolidateAsync(
            IReadOnlyList<ChatMessage> messagesToArchive,
            PromptRequestSnapshot? snapshot,
            CancellationToken cancellationToken = default)
        {
            _snapshotSource.TrySetResult(snapshot);
            return Task.FromResult(MemoryConsolidationResult.Skipped("captured"));
        }

        public async Task<PromptRequestSnapshot?> WaitForSnapshotAsync()
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var registration = cts.Token.Register(
                static state => ((TaskCompletionSource<PromptRequestSnapshot?>)state!).TrySetCanceled(),
                _snapshotSource);
            return await _snapshotSource.Task;
        }
    }
}
