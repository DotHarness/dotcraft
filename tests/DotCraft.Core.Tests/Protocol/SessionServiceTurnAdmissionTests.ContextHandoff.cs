using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.AI;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceTurnAdmissionTests
{
    [Fact]
    public async Task ContextHandoff_UsesOnlySampledSettingsAcrossReloadAndPagedHistory()
    {
        await using var h = new AdmissionHarness();
        var thread = await h.CreateThreadAsync();

        var first = await StartAsync(h.Service, thread.Id, "first");
        Assert.DoesNotContain(await DrainAsync(first.Events!), IsHandoff);
        await h.Service.UpdateThreadConfigurationAsync(thread.Id,
            new ThreadConfiguration { ProviderId = "source", Model = "unused-model" });
        Assert.Single(thread.Turns);

        await using var coldStore = new ThreadStore(h.Root);
        var coldService = h.CreateService(coldStore);
        await coldService.ResumeThreadAsync(thread.Id);
        var switched = await StartAsync(coldService, thread.Id, "switch model", new() { Model = "next-model" });

        var changed = AssertHandoff(await DrainAsync(switched.Events!), "source", "initial-model", "source", "next-model");
        var history = await coldService.ListThreadItemsAsync(thread.Id, switched.Turn.Id, null, 20,
            ThreadHistorySortDirection.Ascending);
        var persisted = Assert.Single(history.Data, entry => entry.Item.AsSystemNotice?.Kind == "contextHandoff");
        Assert.Equal(changed.Id, persisted.Item.Id);

        var contract = AppServerContractMapper.ToContract(persisted.Item.ToWire());
        Assert.True(DotCraft.Protocol.SessionItemPayloadParser.Parse(contract)
            .TryGet<Contract.SystemNoticePayload>(out var payload));
        var data = payload!.ExtensionData!;
        Assert.Equal("source", data["from"].GetProperty("providerId").GetString());
        Assert.Equal("initial-model", data["from"].GetProperty("model").GetString());
        Assert.Equal("source", data["to"].GetProperty("providerId").GetString());
        Assert.Equal("next-model", data["to"].GetProperty("model").GetString());

        var rerouted = await StartAsync(coldService, thread.Id, "switch provider",
            new() { ProviderId = "target", Model = "next-model" });
        AssertHandoff(await DrainAsync(rerouted.Events!), "source", "next-model", "target", "next-model");

        var tuned = await StartAsync(coldService, thread.Id, "same provider and model",
            new() { Reasoning = new() { Effort = ModelReasoningEffort.High } });
        Assert.DoesNotContain(await DrainAsync(tuned.Events!), IsHandoff);
    }

    [Fact]
    public async Task ContextHandoff_CancelWithoutResultLeavesBaselineButProviderFailureChangesIt()
    {
        await using var h = new AdmissionHarness();
        var thread = await h.CreateThreadAsync();

        var first = await StartAsync(h.Service, thread.Id, "first");
        await DrainAsync(first.Events!);

        var timeout = TimeSpan.FromSeconds(10);
        h.Provider.BlockedModel = "cancelled-model";
        var cancelled = await StartAsync(h.Service, thread.Id, "stop before a reply", new() { Model = "cancelled-model" });
        try
        {
            await h.Provider.Blocked.Task.WaitAsync(timeout);
            await h.Service.CancelTurnAsync(thread.Id, cancelled.Turn.Id).WaitAsync(timeout);
        }
        finally
        {
            h.Provider.Release.TrySetResult();
        }

        var cancelledEvents = await cancelled.Events!.ToListAsync().AsTask().WaitAsync(timeout);
        Assert.Contains(cancelledEvents, e => e.EventType == SessionEventType.TurnCancelled);
        Assert.DoesNotContain(cancelledEvents, IsHandoff);

        h.Provider.FailingModel = "failed-model";
        var failed = await StartAsync(h.Service, thread.Id, "provider receives request", new() { Model = "failed-model" });
        var events = await failed.Events!.ToListAsync();
        Assert.Contains(events, e => e.EventType == SessionEventType.TurnFailed);
        AssertHandoff(events, "source", "initial-model", "source", "failed-model");

        var next = await StartAsync(h.Service, thread.Id, "after failure", new() { Model = "next-model" });
        AssertHandoff(await DrainAsync(next.Events!), "source", "failed-model", "source", "next-model");
    }

    [Fact]
    public async Task ContextHandoff_ForkAndRollbackCompareWithSurvivingHistory()
    {
        await using var h = new AdmissionHarness();
        var thread = await h.CreateThreadAsync();

        var first = await StartAsync(h.Service, thread.Id, "first");
        await DrainAsync(first.Events!);
        var second = await StartAsync(h.Service, thread.Id, "switch", new() { Model = "next-model" });
        var events = await DrainAsync(second.Events!);
        var userItemId = Assert.Single(events,
            e => e.EventType == SessionEventType.ItemCompleted && e.ItemPayload?.Type == ItemType.UserMessage).ItemId;

        var fork = await h.Service.ForkThreadAsync(thread.Id, new ThreadForkOptions
        {
            ForkPoint = new ThreadForkPoint { TurnId = second.Turn.Id, ItemId = userItemId }
        });
        var forkTurn = await StartAsync(h.Service, fork.Id, "start from copied input");
        AssertHandoff(await DrainAsync(forkTurn.Events!), "source", "initial-model", "source", "next-model");

        await h.Service.RollbackThreadAsync(thread.Id, 1);
        var replacement = await StartAsync(h.Service, thread.Id, "replacement");
        AssertHandoff(await DrainAsync(replacement.Events!), "source", "initial-model", "source", "next-model");
    }

    private static Task<TurnInputAdmission> StartAsync(
        SessionService service, string threadId, string text, TurnSettingsOverrides? settings = null) =>
        service.StartOrSteerTurnAsync(threadId, [new TextContent(text)], settings: settings);

    private static bool IsHandoff(SessionEvent e) => e.ItemPayload?.AsSystemNotice?.Kind == "contextHandoff";

    private static SessionItem AssertHandoff(List<SessionEvent> events,
        string beforeProvider, string beforeModel, string afterProvider, string afterModel)
    {
        var started = Assert.Single(events, e => e.EventType == SessionEventType.ItemStarted && IsHandoff(e));
        var completed = Assert.Single(events, e => e.EventType == SessionEventType.ItemCompleted && IsHandoff(e));
        Assert.Equal(started.ItemId, completed.ItemId);
        Assert.Equal(ItemStatus.Started, started.ItemPayload!.Status);
        var notice = completed.ItemPayload!;
        Assert.Equal(new ContextHandoffEndpoint(beforeProvider, beforeModel), notice.AsSystemNotice!.From);
        Assert.Equal(new ContextHandoffEndpoint(afterProvider, afterModel), notice.AsSystemNotice.To);
        return notice;
    }
}
