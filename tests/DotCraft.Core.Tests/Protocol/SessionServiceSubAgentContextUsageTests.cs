using DotCraft.Agents;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceSubAgentContextUsageTests : SessionServiceRuntimeSignalTestBase
{
    [Theory]
    [InlineData(1_000, false)]
    [InlineData(9_000, true)]
    public async Task FullHistoryFork_UsesInheritedProviderUsageForFirstCompactionCheck(
        long providerTokens,
        bool shouldCompact)
    {
        IChatClient parentClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("parent answer")]),
            UsageUpdate(requestIndex: 1, input: providerTokens, output: 10, cachedInput: 0)
        ]);
        await using var parentFactory = CreateAgentFactory(parentClient, configureConfig: config =>
        {
            ConfigureSmallCompaction(config);
            config.Compaction.AutoCompactEnabled = false;
        });
        var parentService = CreateService(parentFactory, parentClient, useStreamingFunctionInvoker: true);
        var parent = await parentService.CreateThreadAsync(MakeIdentity());
        await DrainAsync(parentService.SubmitInputAsync(parent.Id, [new TextContent(new string('u', 40_000))]));
        var parentSnapshot = Assert.IsType<DotCraft.Context.PromptRequestSnapshot>(
            parentService.TryGetLastPromptRequestSnapshot(parent.Id));
        var child = await parentService.CreateThreadAsync(MakeIdentity(), source: ThreadSource.ForSubAgent(
            new SubAgentThreadSource
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

        var materializer = (INativeSubAgentForkMaterializationService)parentService;
        Assert.True(await materializer.MaterializeNativeSubAgentForkAsync(
            parent, child, parentSnapshot.Messages, CancellationToken.None));

        var childClient = new RecordingChatClient("child answer");
        await using var childFactory = CreateAgentFactory(
            childClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>compacted parent context</summary>"));
        var childService = CreateService(childFactory, childClient, useStreamingFunctionInvoker: true);
        await childService.ResumeThreadAsync(child.Id);
        var events = await CollectAsync(childService.SubmitInputAsync(child.Id, [new TextContent("inspect")]));

        Assert.Equal(shouldCompact, events.Any(evt => IsSystemEvent(evt, "compacting")));
        var completed = (await childService.GetThreadAsync(child.Id)).Turns[^1];
        Assert.True(completed.Status == TurnStatus.Completed, completed.Error);
    }
}
