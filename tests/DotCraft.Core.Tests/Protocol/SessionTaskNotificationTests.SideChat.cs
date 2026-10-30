using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionTaskNotificationTests
{
    [Fact]
    public async Task SideChatMessage_WakesTheIdleMainThread()
    {
        await using var harness = await CreateHarnessAsync();
        var sideChat = await SideChatContextAsync(harness);

        var result = await SubAgentSessionControl.SendAgentMessageAsync(sideChat, "main", "Use port 8080", coordinator: null, CancellationToken.None);

        Assert.Equal("started", result.Status);
        var thread = await WaitForThreadAsync(harness, t => t.Turns.Count == 1 && t.Turns[0].Status == TurnStatus.Completed);
        var input = thread.Turns[0].Input!.AsUserMessage!;
        Assert.Equal("sideChat", input.TriggerKind);
        Assert.Equal(sideChat.ParentThread.Id, input.TriggerRefId);
        Assert.Equal("Use port 8080", input.Text);
        var modelText = string.Concat(input.MaterializedInputParts!.Select(part => part.Text));
        Assert.Contains("<side_chat_message", modelText);
        Assert.Contains("Use port 8080", modelText);
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task SideChatMessage_DrainsIntoTheRunningMainTurn()
    {
        await using var harness = await CreateHarnessAsync();
        var sideChat = await SideChatContextAsync(harness);
        harness.Client.HoldFirstRequest();
        var turn = harness.RunUserTurnAsync("work");
        await harness.Client.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(10));

        var result = await SubAgentSessionControl.SendAgentMessageAsync(sideChat, "main", "The user wants tests too", coordinator: null, CancellationToken.None);
        harness.Client.ReleaseFirstRequest();
        await turn;

        Assert.Equal("guidancePending", result.Status);
        var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
        var only = Assert.Single(thread.Turns);
        var guidance = Assert.Single(only.Items, item => item.AsUserMessage?.TriggerRefId == sideChat.ParentThread.Id).AsUserMessage!;
        Assert.Equal("guidance", guidance.DeliveryMode);
        Assert.Equal("sideChat", guidance.TriggerKind);
        Assert.Empty(thread.QueuedInputs);
    }

    [Theory]
    [InlineData("/root")]
    [InlineData("worker")]
    public async Task SideChatMessage_RejectsTargetsOtherThanMain(string target)
    {
        await using var harness = await CreateHarnessAsync();
        var sideChat = await SideChatContextAsync(harness);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SendAgentMessageAsync(sideChat, target, "hello", coordinator: null, CancellationToken.None));

        var thread = await harness.Service.GetThreadAsync(harness.ThreadId);
        Assert.Empty(thread.Turns);
        Assert.Empty(thread.QueuedInputs);
    }

    [Fact]
    public async Task SideChatMessage_FailsWhenTheMainThreadIsGone()
    {
        await using var harness = await CreateHarnessAsync();
        var sideChat = await SideChatContextAsync(harness);
        await harness.Service.DeleteThreadPermanentlyAsync(harness.ThreadId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SendAgentMessageAsync(sideChat, "main", "hello", coordinator: null, CancellationToken.None));
    }

    private static async Task<SubAgentSessionContext> SideChatContextAsync(Harness harness)
    {
        var sideChat = await harness.Service.ForkThreadAsync(
            harness.ThreadId,
            new ThreadForkOptions { Ephemeral = true, Purpose = ThreadForkPurpose.SideChat });
        return new SubAgentSessionContext
        {
            SessionService = harness.Service,
            ParentThread = sideChat,
            ParentTurnId = "side-turn",
            RootThreadId = sideChat.Id
        };
    }
}
