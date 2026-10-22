using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceNativeCompactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstructionsChangedAfterNativeCompaction_PreservesGenerationAndDoesNotReplayTools(
        bool coldResume)
    {
        await using var factory = CreateFactory();
        var agent = factory.CreateAgentWithTools(
            [AIFunctionFactory.Create(() => "old tool result marker", name: "GetStatus")],
            null, factory.RuntimeContext, "Use the status tool.");
        var service = new SessionService(factory, agent,
            new SessionPersistenceService(new ThreadStore(Path.Combine(_workspace, ".craft"))), new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        });
        _provider.ToolNextResponse = true;
        _provider.ReasoningNextResponse = true;
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("check status")]));
        var history = await new SessionPersistenceService(new ThreadStore(Path.Combine(_workspace, ".craft")))
            .LoadModelHistoryAsync(thread.Id, CancellationToken.None);
        Assert.Contains(history, message => message.Contents.OfType<FunctionResultContent>().Any());

        File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "replacement project convention");
        await CompactAsync(service, thread.Id);
        var compacted = (await ReadRecordsAsync(thread.Id)).Last(record => record.ProviderHistoryReplaced != null)
            .ProviderHistoryReplaced!;

        await using var coldFactory = coldResume ? CreateFactory() : null;
        if (coldFactory != null)
        {
            service = CreateService(coldFactory);
            await service.ResumeThreadAsync(thread.Id);
        }
        var before = (await ReadRecordsAsync(thread.Id)).Length;
        await service.GetInstructionSourcesAsync(thread.Id);
        await service.GetInstructionSourcesAsync(thread.Id);
        Assert.Equal(before, (await ReadRecordsAsync(thread.Id)).Length);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("after compaction")]));
        Assert.Contains("encrypted-test", _provider.Requests[^1]);
        AssertContainsOnce(_provider.Requests[^1], "replacement project convention");
        Assert.DoesNotContain("old tool result marker", _provider.Requests[^1]);
        Assert.DoesNotContain("old-reasoning-cache", _provider.Requests[^1]);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("unchanged instructions")]));
        AssertContainsOnce(_provider.Requests[^1], "replacement project convention");
        var records = await ReadRecordsAsync(thread.Id);
        Assert.DoesNotContain(records, record => record.Kind == RolloutKinds.ContextCompacted);
        Assert.Equal(compacted.GenerationId, records.Last(record => record.ProviderHistoryReplaced != null)
            .ProviderHistoryReplaced!.GenerationId);
    }

    [Fact]
    public async Task InstructionUpdateRollbackAndFork_KeepNativeCompaction()
    {
        await using var factory = CreateFactory();
        var service = CreateService(factory);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        });
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("initial")]));
        File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "latest project rules");
        await CompactAsync(service, thread.Id);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("rolled-back input")]));
        await service.RollbackThreadAsync(thread.Id, 1);
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("replacement input")]));
        Assert.Contains("encrypted-test", _provider.Requests[^1]);
        AssertContainsOnce(_provider.Requests[^1], "latest project rules");
        Assert.DoesNotContain("rolled-back input", _provider.Requests[^1]);

        var child = await service.ForkThreadAsync(thread.Id, new ThreadForkOptions());
        await DrainAsync(service.SubmitInputAsync(child.Id, [new TextContent("fork continuation")]));
        Assert.Contains("encrypted-test", _provider.Requests[^1]);
        var firstRequest = _provider.Requests[^1];
        await DrainAsync(service.SubmitInputAsync(child.Id, [new TextContent("fork follow-up")]));
        Assert.Equal(firstRequest.Split("latest project rules").Length,
            _provider.Requests[^1].Split("latest project rules").Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InstructionUpdate_FailedOrCancelledRequestCanContinueWithoutRepeatingUpdate(bool cancel)
    {
        await using var factory = CreateFactory();
        var service = CreateService(factory);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
        });
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("initial")]));
        File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "recoverable project convention");
        await CompactAsync(service, thread.Id);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.BeforeNextResponse = async ct =>
        {
            entered.SetResult();
            if (cancel)
                await Task.Delay(Timeout.Infinite, ct);
            else
                throw new InvalidDataException("fixture request failure");
        };
        var failedTurn = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("interrupted request")]));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel)
            await service.CancelTurnAsync(thread.Id, thread.Turns[^1].Id);
        await failedTurn.WaitAsync(TimeSpan.FromSeconds(5));
        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("retry")]));
        Assert.Contains("encrypted-test", _provider.Requests[^1]);
        AssertContainsOnce(_provider.Requests[^1], "recoverable project convention");
    }

    [Fact]
    public async Task NeutralCompaction_InstructionBaselineSurvivesColdResumeWithoutDuplicate()
    {
        string threadId;
        await using (var factory = CreateFactory(oauth: false))
        {
            var service = CreateService(factory);
            var thread = await service.CreateThreadAsync(new SessionIdentity
            {
                WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
            });
            threadId = thread.Id;
            await DrainAsync(service.SubmitInputAsync(threadId, [new TextContent("initial")]));
            File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "cold neutral convention");
            await CompactAsync(service, threadId);
        }
        await using var coldFactory = CreateFactory(oauth: false);
        var coldService = CreateService(coldFactory);
        await coldService.ResumeThreadAsync(threadId);
        await coldService.GetInstructionSourcesAsync(threadId);
        await DrainAsync(coldService.SubmitInputAsync(threadId, [new TextContent("continued")]));
        AssertContainsOnce(_provider.Requests[^1], "cold neutral convention");
        var history = await new SessionPersistenceService(new ThreadStore(Path.Combine(_workspace, ".craft")))
            .LoadModelHistoryAsync(threadId, CancellationToken.None);
        Assert.Single(history, AgentInstructionsHistory.IsInstructions);
    }

    [Fact]
    public async Task MissingWorldStateRecord_ReestablishesInstructionsWithoutReplacingNativeGeneration()
    {
        string threadId;
        await using (var factory = CreateFactory())
        {
            var service = CreateService(factory);
            var thread = await service.CreateThreadAsync(new SessionIdentity
            {
                WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
            });
            threadId = thread.Id;
            await DrainAsync(service.SubmitInputAsync(threadId, [new TextContent("initial")]));
            File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "durable instruction update");
            await CompactAsync(service, threadId);
            await DrainAsync(service.SubmitInputAsync(threadId, [new TextContent("updated")]));
        }

        var records = await ReadRecordsAsync(threadId);
        var generation = records.Last(record => record.ProviderHistoryReplaced != null).ProviderHistoryReplaced!.GenerationId;
        var path = Directory.GetFiles(_workspace, threadId + ".jsonl", SearchOption.AllDirectories).Single();
        await File.WriteAllLinesAsync(path, records.Where(record => record.Kind != RolloutKinds.WorldState)
            .Select(record => System.Text.Json.JsonSerializer.Serialize(record, SessionJsonOptions.Default)));

        await using var coldFactory = CreateFactory();
        var cold = CreateService(coldFactory);
        await cold.ResumeThreadAsync(threadId);
        await DrainAsync(cold.SubmitInputAsync(threadId, [new TextContent("recover baseline")]));
        Assert.Contains("encrypted-test", _provider.Requests[^1]);
        Assert.Contains("durable instruction update", _provider.Requests[^1]);
        var first = _provider.Requests[^1].Split("durable instruction update").Length;
        await DrainAsync(cold.SubmitInputAsync(threadId, [new TextContent("unchanged after recovery")]));
        Assert.Equal(first, _provider.Requests[^1].Split("durable instruction update").Length);
        Assert.Equal(generation, (await ReadRecordsAsync(threadId))
            .Last(record => record.ProviderHistoryReplaced != null).ProviderHistoryReplaced!.GenerationId);
    }
}
