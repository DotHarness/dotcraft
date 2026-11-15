using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceNativeCompactionTests
{
    [Fact]
    public async Task FullHistorySubAgent_AfterCompactionAndProviderUsage_KeepsNativePrefixAcrossColdResume()
    {
        string childId;
        string firstChildRequest;
        string[] forkPrefix = [];
        await using (var factory = CreateFactory())
        {
            var service = CreateService(factory);
            var identity = new SessionIdentity
            {
                WorkspacePath = _workspace, ChannelName = "test", UserId = "user"
            };
            var parent = await service.CreateThreadAsync(identity);
            _provider.ReasoningNextResponse = true;
            _provider.ToolNextResponse = true;
            await DrainAsync(service.SubmitInputAsync(parent.Id, [new TextContent("parent task")]));
            await CompactAsync(service, parent.Id);
            _provider.InputTokensNextResponse = 1_000;
            await DrainAsync(service.SubmitInputAsync(parent.Id, [new TextContent("after compaction")]));
            Assert.Equal(1_000, parent.Turns[^1].TokenUsage?.InputTokens);

            SessionThread? child = null;
            _provider.BeforeNextResponse = async ct =>
            {
                using var request = JsonDocument.Parse(_provider.Requests[^1]);
                forkPrefix = request.RootElement.GetProperty("input").EnumerateArray().Skip(1)
                    .Select(item => item.GetRawText()).ToArray();
                var snapshot = Assert.IsType<PromptRequestSnapshot>(
                    service.TryGetLastPromptRequestSnapshot(parent.Id));
                child = await service.CreateThreadAsync(identity, parent.Configuration,
                    source: ThreadSource.ForSubAgent(new SubAgentThreadSource
                    {
                        ParentThreadId = parent.Id,
                        ParentTurnId = parent.Turns[^1].Id,
                        RootThreadId = parent.Id,
                        Depth = 1,
                        AgentPath = "/root/inspect",
                        TaskName = "inspect",
                        RuntimeType = NativeSubAgentRuntime.RuntimeTypeName,
                        ForkTurns = "all"
                    }));
                child.Turns.Add(new SessionTurn
                {
                    Id = parent.Turns[^1].Id,
                    ThreadId = child.Id,
                    Status = TurnStatus.Completed,
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow
                });
                child.TurnSequenceHighWatermark = parent.TurnSequenceHighWatermark;
                Assert.True(await ((INativeSubAgentForkMaterializationService)service)
                    .MaterializeNativeSubAgentForkAsync(parent, child, snapshot.Messages, ct));
            };
            await DrainAsync(service.SubmitInputAsync(parent.Id, [new TextContent("delegate inspection")]));
            Assert.NotNull(child);
            childId = child.Id;

            await DrainAsync(service.SubmitInputAsync(childId, [new TextContent("inspect-child")]));
            firstChildRequest = _provider.Requests[^1];
        }

        await using var coldFactory = CreateFactory();
        var coldService = CreateService(coldFactory);
        await coldService.ResumeThreadAsync(childId);
        await DrainAsync(coldService.SubmitInputAsync(childId, [new TextContent("continue-child")]));

        foreach (var request in new[] { firstChildRequest, _provider.Requests[^1] })
        {
            using var document = JsonDocument.Parse(request);
            Assert.Equal(forkPrefix, document.RootElement.GetProperty("input").EnumerateArray()
                .Skip(1).Take(forkPrefix.Length).Select(item => item.GetRawText()));
            Assert.Contains("encrypted-test", request);
            Assert.DoesNotContain("old-reasoning-cache", request);
            Assert.DoesNotContain("call_test", request);
            AssertContainsOnce(request, "inspect-child");
        }
        Assert.Single(_provider.Compactions);
    }
}
