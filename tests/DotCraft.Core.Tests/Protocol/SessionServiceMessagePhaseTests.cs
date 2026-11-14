using System.Runtime.CompilerServices;
using DotCraft.AppServer;
using DotCraft.Tools;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.AI;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceMessagePhaseTests : SessionServiceRuntimeSignalTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Response_StreamsUnknownPhaseAndCompletesOnceAsFinalRegardlessOfTrigger(bool subagent)
    {
        using var model = new BoundaryClient();
        await using var factory = CreateAgentFactory(model);
        using var service = CreateService(factory, model);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo
        {
            Kind = subagent ? "subagent" : "test",
            RefId = subagent ? "child-task" : null
        });
        await using var stream = service.SubmitInputAsync(
            thread.Id, [new TextContent("continue")], ct: timeout.Token).GetAsyncEnumerator();
        var events = await ReadPrefixAsync(stream);
        var started = Assert.Single(events, e => e.EventType == SessionEventType.ItemStarted
            && e.ItemPayload?.Type == ItemType.AgentMessage);
        Assert.Null(started.ItemPayload!.AsAgentMessage!.Phase);
        using var running = await service.ObserveThreadAsync(thread.Id, ThreadObservationView.Full, ct: timeout.Token);
        var prefix = Assert.Single(running.Snapshot.Turn!.Items, item => item.Type == ItemType.AgentMessage);
        Assert.Equal("prefix", prefix.AsAgentMessage!.Text);
        Assert.Null(prefix.AsAgentMessage.Phase);

        model.Release.SetResult();
        while (await stream.MoveNextAsync())
            events.Add(stream.Current);

        var completed = Assert.Single(CompletedMessages(events));
        Assert.Equal(started.ItemId, completed.ItemId);
        Assert.Equal("prefixsuffix", completed.ItemPayload!.AsAgentMessage!.Text);
        Assert.Equal("final_answer", completed.ItemPayload.AsAgentMessage.Phase);
        Assert.Null(completed.ItemPayload.AsAgentMessage.DeliveryMode);
        Assert.Equal("prefixsuffix", string.Concat(events.Select(e => e.DeltaPayload?.TextDelta)));
        AssertBeforeTerminal(events, completed);
        AssertContract(completed.ItemPayload, "final_answer", null);
        using var finished = await service.ObserveThreadAsync(thread.Id, ThreadObservationView.Full, ct: timeout.Token);
        Assert.Equal("final_answer", Assert.Single(finished.Snapshot.Turn!.Items,
            item => item.Type == ItemType.AgentMessage).AsAgentMessage!.Phase);
        Assert.Equal(subagent ? "subagent" : "test",
            (await service.GetThreadAsync(thread.Id)).Turns.Single().Input!.AsUserMessage!.TriggerKind);
    }

    [Theory]
    [InlineData("length", SessionEventType.TurnFailed)]
    [InlineData("error", SessionEventType.TurnFailed)]
    [InlineData("cancel", SessionEventType.TurnCancelled)]
    public async Task InterruptedResponse_CompletesOnceAsPartialBeforeTurnTerminal(
        string ending, SessionEventType terminal)
    {
        using var model = new BoundaryClient(ending);
        await using var factory = CreateAgentFactory(model);
        using var service = CreateService(factory, model);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var stream = service.SubmitInputAsync(
            thread.Id, [new TextContent("run")], ct: timeout.Token).GetAsyncEnumerator();
        var events = await ReadPrefixAsync(stream);
        if (ending == "cancel")
            await service.CancelTurnAsync(thread.Id, events.First(e => e.TurnId is not null).TurnId!);
        else
            model.Release.SetResult();
        while (await stream.MoveNextAsync())
            events.Add(stream.Current);

        var completed = Assert.Single(CompletedMessages(events));
        Assert.Equal("prefix", completed.ItemPayload!.AsAgentMessage!.Text);
        Assert.Equal("partial_answer", completed.ItemPayload.AsAgentMessage.Phase);
        Assert.Contains(events, e => e.EventType == terminal);
        AssertBeforeTerminal(events, completed);
        AssertContract(completed.ItemPayload, "partial_answer", null);
    }

    [Fact]
    public async Task ReasoningAndToolBoundaries_CompleteProgressWithoutReclassifyingItAtTurnEnd()
    {
        using var model = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, "checking"),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("reason")]),
            new ChatResponseUpdate(ChatRole.Assistant, "reading"),
            new ChatResponseUpdate(ChatRole.Assistant, [
                new FunctionCallContent("read-call", "ReadFile", new Dictionary<string, object?>())]),
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionResultContent("read-call", "file")]),
            new ChatResponseUpdate(ChatRole.Assistant, "answer")
        ]);
        await using var factory = CreateAgentFactory(model);
        using var service = CreateService(factory, model);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("inspect")]));

        var completed = CompletedMessages(events);
        Assert.Equal(["checking", "reading", "answer"],
            completed.Select(e => e.ItemPayload!.AsAgentMessage!.Text));
        Assert.Equal(["commentary", "commentary", "final_answer"],
            completed.Select(e => e.ItemPayload!.AsAgentMessage!.Phase));
        Assert.Equal(3, completed.Select(e => e.ItemId).Distinct().Count());
        Assert.All(completed, item => AssertBeforeTerminal(events, item));
        var turn = Assert.Single((await service.GetThreadAsync(thread.Id)).Turns);
        Assert.Equal(["commentary", "commentary", "final_answer"],
            turn.Items.Where(item => item.Type == ItemType.AgentMessage).Select(item => item.AsAgentMessage!.Phase));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NaturalAnswerBoundary_IsNotReclassifiedByALaterToolResult(bool explicitStop)
    {
        using var model = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [
                new FunctionCallContent("background-call", "ReadFile", new Dictionary<string, object?>())]),
            new ChatResponseUpdate(ChatRole.Assistant, "answer")
            {
                FinishReason = explicitStop ? ChatFinishReason.Stop : null
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionResultContent("background-call", "late result")])
        ]);
        await using var factory = CreateAgentFactory(model);
        using var service = CreateService(factory, model);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("continue")]));

        var answer = Assert.Single(CompletedMessages(events));
        Assert.Equal("final_answer", answer.ItemPayload!.AsAgentMessage!.Phase);
        var result = Assert.Single(events, e =>
            e.EventType == SessionEventType.ItemCompleted && e.ItemPayload?.Type == ItemType.ToolResult);
        if (explicitStop)
            Assert.True(events.IndexOf(answer) < events.IndexOf(result));
        else
            Assert.True(events.IndexOf(result) < events.IndexOf(answer));
        AssertBeforeTerminal(events, answer);
    }

    [Fact]
    public async Task ActualAsyncDelivery_RemainsSeparateFromTheNormalFinalResponse()
    {
        using var model = new CoordinationToolCallChatClient(
            nameof(UserCoordinationTools.SendUserMessageAsync),
            new Dictionary<string, object?> { ["message"] = "progress notice" });
        await using var factory = CreateAgentFactory(model);
        using var service = CreateService(factory, model, useStreamingFunctionInvoker: true);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        await service.RefreshThreadAgentAsync(thread.Id);

        var events = await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("continue")]));

        var completed = CompletedMessages(events);
        var asynchronous = Assert.Single(completed, e => e.ItemPayload!.AsAgentMessage!.DeliveryMode == "async");
        var final = Assert.Single(completed, e => e.ItemPayload!.AsAgentMessage!.DeliveryMode is null);
        Assert.Equal("progress notice", asynchronous.ItemPayload!.AsAgentMessage!.Text);
        Assert.Equal("final_answer", asynchronous.ItemPayload.AsAgentMessage.Phase);
        Assert.Equal("final_answer", final.ItemPayload!.AsAgentMessage!.Phase);
        Assert.Equal("done", final.ItemPayload.AsAgentMessage.Text);
        Assert.NotEqual(asynchronous.ItemId, final.ItemId);
        Assert.All(completed, item => AssertBeforeTerminal(events, item));
        AssertContract(asynchronous.ItemPayload, "final_answer", "async");
        AssertContract(final.ItemPayload, "final_answer", null);
    }

    private static async Task<List<SessionEvent>> ReadPrefixAsync(IAsyncEnumerator<SessionEvent> stream)
    {
        var events = new List<SessionEvent>();
        while (await stream.MoveNextAsync())
        {
            events.Add(stream.Current);
            if (stream.Current.DeltaPayload?.TextDelta == "prefix")
                return events;
        }
        throw new InvalidOperationException("The model did not stream its prefix.");
    }

    private static List<SessionEvent> CompletedMessages(List<SessionEvent> events) =>
        events.Where(e => e.EventType == SessionEventType.ItemCompleted
            && e.ItemPayload?.Type == ItemType.AgentMessage).ToList();

    private static void AssertBeforeTerminal(List<SessionEvent> events, SessionEvent item) =>
        Assert.True(events.IndexOf(item) < events.FindIndex(e =>
            e.EventType is SessionEventType.TurnCompleted or SessionEventType.TurnFailed or SessionEventType.TurnCancelled));

    private static void AssertContract(SessionItem item, string phase, string? delivery)
    {
        var contract = AppServerContractMapper.ToContract(item.ToWire());
        Assert.True(DotCraft.Protocol.SessionItemPayloadParser.Parse(contract)
            .TryGet<Contract.AgentMessagePayload>(out var payload));
        Assert.Equal(phase, payload!.Phase);
        Assert.Equal(delivery, payload.DeliveryMode);
    }

    private sealed class BoundaryClient(string ending = "natural") : IChatClient
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "prefix");
            await Release.Task.WaitAsync(cancellationToken);
            if (ending == "error")
                throw new InvalidOperationException("Provider failed after a partial response.");
            if (ending == "length")
                yield return new ChatResponseUpdate(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.Length };
            else
                yield return new ChatResponseUpdate(ChatRole.Assistant, "suffix") { FinishReason = ChatFinishReason.Stop };
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
