using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceNativeCompactionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FullHistorySubAgent_KeepsNativePrefixAndCompactionState(bool compactParent, bool ephemeralChild)
    {
        string childId;
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
            if (compactParent)
                await CompactAsync(service, parent.Id);
            _provider.InputTokensNextResponse = 1_000;
            await DrainAsync(service.SubmitInputAsync(parent.Id, [new TextContent("before delegation")]));

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
                    }), ephemeral: ephemeralChild);
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
            AssertForkPrefix(_provider.Requests[^1], forkPrefix, compactParent);
            var childSnapshot = Assert.IsType<PromptRequestSnapshot>(service.TryGetLastPromptRequestSnapshot(childId));
            Assert.Equal(compactParent, Assert.IsType<OpaqueProviderHistorySnapshot>(childSnapshot.ProviderHistory).IsNativeCompacted);

            await DrainAsync(service.SubmitInputAsync(parent.Id, [new TextContent("parent-only-after-fork")]));
            Assert.DoesNotContain("inspect-child", _provider.Requests[^1]);
            using var parentRequest = JsonDocument.Parse(_provider.Requests[^1]);
            Assert.Equal(forkPrefix, parentRequest.RootElement.GetProperty("input").EnumerateArray()
                .Skip(1).Take(forkPrefix.Length).Select(item => item.GetRawText()));

            if (ephemeralChild)
            {
                await DrainAsync(service.SubmitInputAsync(childId, [new TextContent("continue-child")]));
                AssertForkPrefix(_provider.Requests[^1], forkPrefix, compactParent);
            }
        }

        if (!ephemeralChild)
        {
            await using var coldFactory = CreateFactory();
            var coldService = CreateService(coldFactory);
            await coldService.ResumeThreadAsync(childId);
            await DrainAsync(coldService.SubmitInputAsync(childId, [new TextContent("continue-child")]));
            AssertForkPrefix(_provider.Requests[^1], forkPrefix, compactParent);
            var snapshot = Assert.IsType<PromptRequestSnapshot>(coldService.TryGetLastPromptRequestSnapshot(childId));
            Assert.Equal(compactParent, Assert.IsType<OpaqueProviderHistorySnapshot>(snapshot.ProviderHistory).IsNativeCompacted);
        }
    }

    private static void AssertForkPrefix(string request, string[] prefix, bool compactParent)
    {
        using var document = JsonDocument.Parse(request);
        Assert.Equal(prefix, document.RootElement.GetProperty("input").EnumerateArray()
            .Skip(1).Take(prefix.Length).Select(item => item.GetRawText()));
        Assert.Contains(compactParent ? "encrypted-test" : "old-reasoning-cache", request);
        AssertContainsOnce(request, "delegate inspection");
        AssertContainsOnce(request, "inspect-child");
        Assert.DoesNotContain("parent-only-after-fork", request);
    }
}
