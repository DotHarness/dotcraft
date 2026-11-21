using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceForkContextUsageTests : SessionServiceRuntimeSignalTestBase
{
    [Theory]
    [InlineData(ThreadForkPurpose.SideChat, 1_000, false)]
    [InlineData(null, 1_000, false)]
    [InlineData(null, 8_800, true)]
    [InlineData(ThreadForkPurpose.PromptSuggestion, 8_800, false)]
    public async Task ForkFirstRequest_UsesMeasuredUsageAndItsCompactionPolicy(
        ThreadForkPurpose? purpose, long providerTokens, bool shouldCompact)
    {
        var parentClient = new FakeChatClient(
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
        var parentInput = new string('u', 40_000);
        await DrainAsync(parentService.SubmitInputAsync(parent.Id, [new TextContent(parentInput)]));

        var childClient = new RecordingChatClient("fork answer");
        await using var childFactory = CreateAgentFactory(
            childClient,
            configureConfig: ConfigureSmallCompaction,
            compactionChatClient: new SummaryChatClient("<summary>compacted parent context</summary>"));
        var service = CreateService(childFactory, childClient, useStreamingFunctionInvoker: true);
        await service.ResumeThreadAsync(parent.Id);
        var child = await service.ForkThreadAsync(parent.Id, new ThreadForkOptions
        {
            Ephemeral = purpose is not null,
            Purpose = purpose,
            ForkPoint = purpose == ThreadForkPurpose.PromptSuggestion
                ? new ThreadForkPoint { TurnId = parent.Turns[^1].Id }
                : null
        });
        var events = await CollectAsync(service.SubmitInputAsync(child.Id, [new TextContent("inspect")]));

        Assert.Equal(shouldCompact, events.Any(evt => IsSystemEvent(evt, "compacting")));
        Assert.Contains(childClient.LastMessages, message => MessageText(message) == "inspect");
        if (!shouldCompact)
            Assert.Contains(childClient.LastMessages, message => MessageText(message).Contains(parentInput, StringComparison.Ordinal));
        var completed = (await service.GetThreadAsync(child.Id)).Turns[^1];
        Assert.True(completed.Status == TurnStatus.Completed, completed.Error);
    }
}
