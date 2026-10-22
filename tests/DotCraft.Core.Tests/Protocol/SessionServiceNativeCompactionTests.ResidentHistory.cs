using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceNativeCompactionTests
{
    private const string ResidentThreadId = "resident-history";

    [Theory]
    [InlineData("plain", true)]
    [InlineData("pre-turn", true)]
    [InlineData("pre-turn", false)]
    [InlineData("post-turn", true)]
    [InlineData("mid-turn", true)]
    [InlineData("mid-turn", false)]
    [InlineData("reactive", true)]
    [InlineData("reactive", false)]
    [InlineData("manual", false)]
    [InlineData("failed", true)]
    [InlineData("cancelled", true)]
    [InlineData("rollback", true)]
    public async Task LoadedThread_NextRequestMatchesColdReplayWithoutReadingTheRollout(string scenario, bool oauth)
    {
        var postTurnThreshold = scenario == "post-turn" ? 50 : 0;
        await using var factory = CreateFactory(oauth, postTurnThreshold, smallTail: !oauth);
        var store = new ThreadStore(Path.Combine(_workspace, ".craft"));
        var service = CreateResidentService(factory, store);
        await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        }, threadId: ResidentThreadId);
        await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("committed-prefix")]));
        await RunScenarioAsync(service, scenario);
        var rolloutPath = Directory.GetFiles(_workspace, ResidentThreadId + ".jsonl", SearchOption.AllDirectories).Single();
        var coldStartLength = new FileInfo(rolloutPath).Length;
        if (scenario == "rollback")
            await service.RollbackThreadAsync(ResidentThreadId, 1);

        var reads = store.RolloutReplayCount;
        await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("next-request")]));
        Assert.Equal(reads, store.RolloutReplayCount);
        var warmRequest = _provider.Requests[^1];

        await store.FlushAndCloseAsync();
        await using (var rollout = new FileStream(rolloutPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            rollout.SetLength(coldStartLength);
        await using var coldFactory = CreateFactory(oauth, postTurnThreshold, smallTail: !oauth);
        var cold = CreateResidentService(coldFactory, new ThreadStore(Path.Combine(_workspace, ".craft")));
        await cold.ResumeThreadAsync(ResidentThreadId);
        if (scenario == "rollback")
            await cold.RollbackThreadAsync(ResidentThreadId, 1);
        await DrainAsync(cold.SubmitInputAsync(ResidentThreadId, [new TextContent("next-request")]));
        Assert.Equal(warmRequest, _provider.Requests[^1]);
    }

    [Fact]
    public async Task UnloadedThread_RebuildsHistoryOnceOnTheNextTurn()
    {
        await using var factory = CreateFactory();
        var store = new ThreadStore(Path.Combine(_workspace, ".craft"));
        var service = CreateResidentService(factory, store);
        await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        }, threadId: ResidentThreadId);
        await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("before-unload")]));
        var delay = SessionService.ThreadIdleUnloadDelay;
        Assert.Equal(1, await service.UnloadIdleThreadsAsync(DateTimeOffset.UtcNow + delay, delay));

        var beforeReload = store.RolloutReplayCount;
        await service.ResumeThreadAsync(ResidentThreadId);
        await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("after-reload")]));
        var afterReload = store.RolloutReplayCount;
        Assert.True(afterReload > beforeReload);
        await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("still-loaded")]));
        Assert.Equal(afterReload, store.RolloutReplayCount);
        AssertContainsOnce(_provider.Requests[^1], "before-unload");
        AssertContainsOnce(_provider.Requests[^1], "after-reload");
    }

    private async Task RunScenarioAsync(SessionService service, string scenario)
    {
        switch (scenario)
        {
            case "pre-turn":
                _provider.ReportHighUsageNextResponse = true;
                await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("high-usage")]));
                await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("compacted-before")]));
                break;
            case "post-turn":
                _provider.ReportHighUsageNextResponse = true;
                await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("compacted-after")]));
                break;
            case "mid-turn":
                _provider.ToolNextResponse = true;
                _provider.ReportHighUsageNextResponse = true;
                await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("check status")]));
                break;
            case "reactive":
                _provider.OverflowNextRequest = true;
                await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("overflow-request")]));
                break;
            case "manual":
                await CompactAsync(service, ResidentThreadId);
                break;
            case "failed":
            case "cancelled":
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _provider.BeforeNextResponse = async ct =>
                {
                    entered.SetResult();
                    if (scenario == "cancelled")
                        await Task.Delay(Timeout.Infinite, ct);
                    else
                        throw new InvalidDataException("fixture request failure");
                };
                var interrupted = DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("interrupted")]));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (scenario == "cancelled")
                {
                    var turnId = (await service.GetThreadAsync(ResidentThreadId)).Turns[^1].Id;
                    await service.CancelTurnAsync(ResidentThreadId, turnId);
                }
                await interrupted.WaitAsync(TimeSpan.FromSeconds(5));
                break;
            case "rollback":
                await DrainAsync(service.SubmitInputAsync(ResidentThreadId, [new TextContent("rolled-back")]));
                break;
        }
    }

    private static SessionService CreateResidentService(AgentFactory factory, ThreadStore store)
    {
        var agent = factory.CreateAgentWithTools(
            [AIFunctionFactory.Create(() => "tool-result-tail", name: "GetStatus")],
            null,
            factory.RuntimeContext,
            "Use the status tool.");
        return new SessionService(factory, agent, new SessionPersistenceService(store), new SessionGate());
    }
}
