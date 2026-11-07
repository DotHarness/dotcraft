using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadObservationStateTests
{
    [Fact]
    public void Capture_SeparatesViewsAndIsolatesStreamingSnapshots()
    {
        var state = new ThreadObservationState();
        var input = Item("input", ItemType.UserMessage, new UserMessagePayload { Text = "question" });
        var answer = Item("answer", ItemType.AgentMessage, new AgentMessagePayload { Text = "prefix " });
        var reasoningPrefix = new string('r', 8192);
        var turn = Turn();
        turn.Input = input;
        turn.Items.AddRange([
            input,
            answer,
            Item("reasoning", ItemType.ReasoningContent, new ReasoningContentPayload { Text = reasoningPrefix }),
            Item("command", ItemType.CommandExecution, new CommandExecutionPayload())
        ]);
        var thread = Header();
        thread.Turns.Add(turn);
        state.Initialize(thread);
        answer.Payload = new AgentMessagePayload { Text = "changed source shell" };
        for (var i = 0; i < 1024; i++)
        {
            Delta(state, "answer", new AgentMessageDelta { TextDelta = "answer " });
            Delta(state, "reasoning", new ReasoningContentDelta { TextDelta = "reason " });
            Delta(state, "command", new CommandExecutionOutputDelta { TextDelta = "output " });
        }

        var metadata = Capture(state, ThreadObservationView.Metadata);
        Assert.Equal(4, metadata.ItemCount);
        Assert.Empty(metadata.Turn!.Items);
        Assert.Null(metadata.Turn.Input);
        var summary = Capture(state, ThreadObservationView.Summary);
        Assert.Equal(["input", "answer"], summary.Turn!.Items.Select(item => item.Id));
        Assert.Equal("prefix " + string.Concat(Enumerable.Repeat("answer ", 1024)), summary.Turn.Items[1].AsAgentMessage!.Text);

        var full = Capture(state);
        Assert.Equal(reasoningPrefix + string.Concat(Enumerable.Repeat("reason ", 1024)), full.Turn!.Items[2].AsReasoningContent!.Text);
        Assert.Equal(string.Concat(Enumerable.Repeat("output ", 1024)), full.Turn.Items[3].AsCommandExecution!.AggregatedOutput);
        Assert.Same(full.Turn.Items[0], full.Turn.Input);
        var largeOutput = new string('x', TerminalOutputBuffer.CapacityBytes * 2) + "retained tail";
        Delta(state, "command", new CommandExecutionOutputDelta { TextDelta = largeOutput });
        var command = Capture(state).Turn!.Items[3].AsCommandExecution!;
        Assert.StartsWith("output output ", command.AggregatedOutput);
        Assert.EndsWith("retained tail", command.AggregatedOutput);
        Assert.Equal(1024 * "output ".Length + largeOutput.Length, command.OriginalOutputChars);
        Assert.True(command.Truncated);
        Assert.True(command.AggregatedOutput.Length < largeOutput.Length);
        Assert.Empty(Capture(state, ThreadObservationView.Metadata).Turn!.Items);
        state.Apply(Event(SessionEventType.ItemCompleted,
            Item("command", ItemType.CommandExecution, new CommandExecutionPayload(), status: ItemStatus.Completed)));
        Delta(state, "command", new CommandExecutionOutputDelta { TextDelta = "ignored late output" });
        Assert.Equal(command, Capture(state).Turn!.Items[3].AsCommandExecution);
        full.Turn.Items[1].Payload = new AgentMessagePayload { Text = "changed externally" };
        full.Turn.Items.Clear();
        Delta(state, "answer", new AgentMessageDelta { TextDelta = "suffix" });
        Assert.EndsWith("answer suffix", Capture(state).Turn!.Items[1].AsAgentMessage!.Text);
        Assert.DoesNotContain("suffix", summary.Turn.Items[1].AsAgentMessage!.Text);
    }

    [Fact]
    public void TerminalMerge_PreservesPublishedContentAndRetainsOnlyLatestHead()
    {
        var state = CreateState();
        PublishItem(state, Item("answer", ItemType.AgentMessage, new AgentMessagePayload()));
        Delta(state, "answer", new AgentMessageDelta { TextDelta = "complete answer" });
        var terminal = Turn(status: TurnStatus.Completed);
        terminal.Items.Add(Item("answer", ItemType.AgentMessage, new AgentMessagePayload(), status: ItemStatus.Completed));
        state.Apply(Event(SessionEventType.TurnCompleted, terminal));
        Assert.Equal("complete answer", Capture(state).Turn!.Items[0].AsAgentMessage!.Text);
        Assert.False(Capture(state).Runtime.Running);

        var next = Turn("turn_002");
        next.StartedAt = terminal.StartedAt.AddMinutes(1);
        state.Apply(Event(SessionEventType.TurnStarted, next));
        state.Apply(Event(SessionEventType.TurnCompleted, terminal));
        PublishItem(state, Item("old", ItemType.AgentMessage,
            new AgentMessagePayload { Text = "late old item" }));
        var current = Capture(state);
        Assert.Equal(next.Id, current.Turn!.Id);
        Assert.Empty(current.Turn.Items);
        Assert.True(current.Runtime.Running);
        state.Clear();
        Assert.Null(Capture(state).Turn);
        Assert.False(Capture(state).Runtime.Running);
    }

    [Fact]
    public void Capture_TracksArgumentsProgressAndUnresolvedRequestIdentities()
    {
        var state = CreateState();
        Delta(state, "unknown", new ToolCallArgumentsDelta { Delta = "ignored" });
        Delta(state, "unknown", new ToolExecutionProgressPayload { Progress = new JsonObject() });
        Assert.Empty(Capture(state).ToolArguments);
        Assert.Empty(Capture(state).ToolProgress);
        PublishItem(state, Item("call", ItemType.ToolCall,
            new ToolCallPayload { CallId = "call-1", ToolName = "Work" }));
        Delta(state, "call", new ToolCallArgumentsDelta { Delta = "{\"value\":" });
        Delta(state, "call", new ToolCallArgumentsDelta { Delta = "7}" });
        var progress = new JsonObject { ["step"] = 1 };
        Delta(state, "call", new ToolExecutionProgressPayload { CallId = "call-1", Progress = progress });
        var first = Capture(state);
        Delta(state, "call", new ToolExecutionProgressPayload
        {
            CallId = "call-1", Progress = new JsonObject { ["step"] = 2 }
        });
        Assert.Equal("{\"value\":7}", first.ToolArguments["call"]);
        Assert.Equal(1, JsonNode.Parse(JsonSerializer.Serialize(first.ToolProgress["call"].Progress))!["step"]!.GetValue<int>());
        Assert.Empty(Capture(state, ThreadObservationView.Metadata).ToolArguments);
        Assert.Empty(Capture(state, ThreadObservationView.Metadata).ToolProgress);
        var unrelated = state.Capture(Header(), null, ThreadObservationView.Full, [],
            new ThreadObservationItemSelection { ItemIds = new HashSet<string> { "unrelated" } });
        Assert.Empty(unrelated.Turn!.Items);
        Assert.Empty(unrelated.ToolArguments);
        Assert.Empty(unrelated.ToolProgress);
        state.Apply(Event(SessionEventType.ItemCompleted, Item("call", ItemType.ToolCall,
            new ToolCallPayload { CallId = "call-1", ToolName = "Work" }, status: ItemStatus.Completed)));
        Delta(state, "call", new ToolCallArgumentsDelta { Delta = "ignored" });
        Delta(state, "call", new ToolExecutionProgressPayload { Progress = new JsonObject() });
        Assert.Empty(Capture(state).ToolArguments);
        Assert.Empty(Capture(state).ToolProgress);

        PublishItem(state, Item("approval", ItemType.ApprovalRequest,
            new ApprovalRequestPayload { RequestId = "approval-1" }));
        PublishItem(state, Item("input", ItemType.UserInputRequest,
            new UserInputRequestPayload { RequestId = "input-1", IsBlocking = true }));
        var waiting = Capture(state, ThreadObservationView.Metadata);
        Assert.True(waiting.Runtime.WaitingOnApproval);
        Assert.Equal(2, waiting.PendingRequests.Count);
        Assert.Contains(waiting.PendingRequests, request =>
            request.RequestId == "approval-1" && request.ItemId == "approval" && request.Type == ItemType.ApprovalRequest);

        PublishItem(state, Item("approved", ItemType.ApprovalResponse,
            new ApprovalResponsePayload { RequestId = "approval-1" }));
        Assert.True(Capture(state).Runtime.WaitingOnInput);
        PublishItem(state, Item("answered", ItemType.UserInputResponse,
            new UserInputResponsePayload { RequestId = "input-1" }));
        Assert.Empty(Capture(state).PendingRequests);
        Assert.False(Capture(state).Runtime.WaitingOnInput);
        Assert.Equal(2, waiting.PendingRequests.Count);
        PublishItem(state, Item("optional-input", ItemType.UserInputRequest,
            new UserInputRequestPayload { RequestId = "optional-1", IsBlocking = false }));
        var optional = Capture(state, ThreadObservationView.Metadata);
        Assert.False(optional.Runtime.WaitingOnInput);
        Assert.True(optional.Runtime.Running);
        Assert.False(Assert.Single(optional.PendingRequests).IsBlocking);
    }

    [Fact]
    public void Summary_KeepsPinnedToolPairsFileChangesAndCompletedPresentations()
    {
        var state = CreateState();
        var items = new[]
        {
            Item("pinned-call", ItemType.ToolCall, new ToolCallPayload { Namespace = "work", ToolName = "Keep", CallId = "pinned" }),
            Item("pinned-result", ItemType.ToolResult, new ToolResultPayload { CallId = "pinned", Success = true }),
            Item("file-call", ItemType.ToolCall, new ToolCallPayload { ToolName = "Edit", CallId = "file" }),
            Item("file-result", ItemType.ToolResult, new ToolResultPayload { CallId = "file", StructuredContent = new JsonObject { ["kind"] = "fileChange" } }),
            Item("hidden", ItemType.ToolCall, new ToolCallPayload { ToolName = "Other", CallId = "other" }),
            Item("image", ItemType.ImageGeneration, new ImageGenerationPayload { Status = "completed" }),
            Item("pending-image", ItemType.ImageGeneration, new ImageGenerationPayload()),
            Item("app", ItemType.McpToolCall, new McpToolCallPayload { McpAppResourceUri = "ui://app" }, status: ItemStatus.Completed),
            Item("dynamic", ItemType.DynamicToolCall, new DynamicToolCallPayload { Namespace = "work", ToolName = "Keep" }),
            Item("execution", ItemType.ToolExecution, new ToolExecutionPayload { CallId = "other" })
        };
        foreach (var item in items)
            PublishItem(state, item);
        var snapshot = Capture(state, ThreadObservationView.Summary, [new ToolName("work", "Keep")]);
        Assert.Equal(
            ["pinned-call", "pinned-result", "file-call", "file-result", "image", "app", "dynamic"],
            snapshot.Turn!.Items.Select(item => item.Id));
        var metadata = Capture(state, ThreadObservationView.Metadata);
        Assert.Equal(items.Length, metadata.ItemCount);
        Assert.Equal(1, metadata.ToolExecutionCount);
        var selected = state.Capture(Header(), null, ThreadObservationView.Full, [],
            new ThreadObservationItemSelection
            {
                ItemIds = new HashSet<string> { "file-result" },
                Limit = 1,
                IncludeToolExecutions = false
            });
        Assert.Equal("file-result", Assert.Single(selected.Turn!.Items).Id);
        Assert.Equal(items.Length, selected.ItemCount);
        Assert.Equal(1, selected.ToolExecutionCount);
        snapshot.Turn.Items[3].AsToolResult!.StructuredContent!["kind"] = "changed";
        Assert.Equal("fileChange", Capture(state).Turn!.Items[3].AsToolResult!.StructuredContent!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void Capture_DerivesPlanAndUsageFromCurrentHeadWithoutChangingHeader()
    {
        var state = CreateState();
        state.Apply(Event(SessionEventType.UsageDelta, new UsageDeltaPayload
        {
            InputTokens = 10, OutputTokens = 3, TurnInputTokens = 10, TurnOutputTokens = 3,
            ContextUsage = new ContextUsageSnapshot { Tokens = 500 }
        }));
        state.Apply(Event(SessionEventType.UsageDelta, new UsageDeltaPayload
        {
            InputTokens = 20, OutputTokens = 4, TurnInputTokens = 30, TurnOutputTokens = 7
        }));
        state.Apply(Event(SessionEventType.TurnDiffUpdated, new TurnDiffUpdatedPayload { Diff = "current diff" }));
        var oldUsage = Event(SessionEventType.SystemEvent, new SystemEventPayload
        {
            Kind = "compacted", ContextUsage = new ContextUsageSnapshot { Tokens = 100 }
        });
        oldUsage.TurnId = "turn_000";
        state.Apply(oldUsage);
        Assert.Equal(500, Capture(state).ContextUsage!.Tokens);
        var threadUsage = Event(SessionEventType.SystemEvent, new SystemEventPayload
        {
            Kind = "compacted", ContextUsage = new ContextUsageSnapshot { Tokens = 400 }
        });
        threadUsage.TurnId = null;
        state.Apply(threadUsage);
        var running = Capture(state);
        Assert.Equal(30, running.Turn!.TokenUsage!.InputTokens);
        Assert.Equal(7, running.Turn.TokenUsage.OutputTokens);
        state.SeedContextUsageIfMissing(new ContextUsageSnapshot { Tokens = 100 });
        Assert.Equal(400, running.ContextUsage!.Tokens);
        Assert.Equal(400, Capture(state).ContextUsage!.Tokens);
        Assert.Equal("current diff", running.Diff);

        PublishItem(state, Item("plan-call", ItemType.ToolCall,
            new ToolCallPayload { ToolName = "CreatePlan", CallId = "plan" }));
        PublishItem(state, Item("plan-result", ItemType.ToolResult,
            new ToolResultPayload { CallId = "plan", Success = true }));
        state.Apply(Event(SessionEventType.TurnCompleted, Turn(status: TurnStatus.Completed)));
        Assert.Equal(30, Capture(state).Turn!.TokenUsage!.InputTokens);
        var header = Header("plan");
        var snapshot = state.Capture(header, "compaction", ThreadObservationView.Metadata, []);
        Assert.Equal("thread_001", snapshot.ThreadId);
        Assert.Equal("plan", snapshot.Mode);
        Assert.Equal("turn_001", snapshot.PlanTurnId);
        Assert.True(snapshot.Runtime.WaitingOnPlanConfirmation);
        Assert.True(snapshot.Runtime.Busy);
        Assert.Equal("compaction", snapshot.Runtime.MaintenanceKind);
        Assert.Empty(header.Turns);
        Assert.Empty(snapshot.Turn!.Items);
        header.Configuration!.Mode = "agent";
        Assert.Equal("plan", snapshot.Mode);
        Assert.Null(state.Capture(header, null, ThreadObservationView.Metadata, []).PlanTurnId);
    }

    private static ThreadObservationState CreateState()
    {
        var state = new ThreadObservationState();
        var thread = Header();
        thread.Turns.Add(Turn());
        state.Initialize(thread);
        return state;
    }

    private static SessionThread Header(string mode = "agent") => new()
    {
        Id = "thread_001",
        Configuration = new ThreadConfiguration { Mode = mode }
    };

    private static SessionTurn Turn(string id = "turn_001", TurnStatus status = TurnStatus.Running) => new()
    {
        Id = id,
        ThreadId = "thread_001",
        Status = status,
        StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
    };

    private static SessionItem Item(
        string id, ItemType type, object payload, string turnId = "turn_001", ItemStatus status = ItemStatus.Started) => new()
    {
        Id = id, TurnId = turnId, Type = type, Payload = payload, Status = status
    };

    private static SessionEvent Event(SessionEventType type, object payload) => new()
    {
        EventType = type,
        ThreadId = "thread_001",
        TurnId = payload is SessionTurn turn ? turn.Id : "turn_001",
        ItemId = (payload as SessionItem)?.Id,
        Payload = payload
    };

    private static void PublishItem(ThreadObservationState state, SessionItem item) =>
        state.Apply(Event(SessionEventType.ItemStarted, item));

    private static void Delta(ThreadObservationState state, string itemId, object payload)
    {
        var evt = Event(SessionEventType.ItemDelta, payload);
        evt.ItemId = itemId;
        state.Apply(evt);
    }

    private static ThreadObservationSnapshot Capture(
        ThreadObservationState state,
        ThreadObservationView view = ThreadObservationView.Full,
        IReadOnlyList<ToolName>? tools = null) => state.Capture(Header(), null, view, tools ?? []);
}
