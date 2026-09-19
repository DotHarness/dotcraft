using DotCraft.Context;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using TurnInitiatorContext = DotCraft.Sessions.TurnInitiatorContext;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class RuntimeContextBuilderTests
{
    [Fact]
    public void AppendRuntimeContext_AddsNoContentWhenThereIsNothingToSay()
    {
        var contents = new List<AIContent> { new TextContent("hello") };

        contents.AppendRuntimeContext(workspacePath: Directory.GetCurrentDirectory());

        Assert.Equal("hello", Assert.IsType<TextContent>(Assert.Single(contents)).Text);
    }

    [Fact]
    public void BuildBlock_LeavesDurableStateToWorldState()
    {
        var block = RuntimeContextBuilder.BuildBlock(
            initiator: new TurnInitiatorContext { ChannelName = "qq", UserId = "10001", UserName = "Alice" },
            workspacePath: Directory.GetCurrentDirectory());

        Assert.StartsWith("<system-reminder>", block, StringComparison.Ordinal);
        Assert.EndsWith("</system-reminder>", block, StringComparison.Ordinal);
        Assert.DoesNotContain("## Environment", block, StringComparison.Ordinal);
        Assert.DoesNotContain("## Mode", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBlock_RendersGoalCountersThatMoveEveryTurn()
    {
        var block = RuntimeContextBuilder.BuildBlock(threadGoal: new ThreadGoal
        {
            ThreadId = "thread_test",
            GoalId = "goal_1",
            Objective = "ship the refactor",
            Status = ThreadGoalStatus.Active,
            TokenBudget = 1000,
            TokensUsed = new TokenUsageInfo { InputTokens = 250, TotalTokens = 250 },
            TimeUsedSeconds = 42,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch
        });

        Assert.Contains("## Thread Goal Usage", block, StringComparison.Ordinal);
        Assert.Contains("TokensUsed: 250", block, StringComparison.Ordinal);
        Assert.Contains("RemainingTokens: 750", block, StringComparison.Ordinal);
        Assert.Contains("ElapsedSeconds: 42", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<untrusted_objective>", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildBlock_OmitsWorkspaceRequestSourceForLocalDesktopInitiator()
    {
        var workspace = Path.GetFullPath(Directory.GetCurrentDirectory());
        var block = RuntimeContextBuilder.BuildBlock(
            initiator: new TurnInitiatorContext
            {
                ChannelName = "cli",
                UserId = "dotcraft-desktop",
                ChannelContext = $"workspace:{workspace}"
            },
            workspacePath: workspace);

        Assert.Null(block);
    }

    [Fact]
    public void BuildBlock_IncludesSemanticRequestSourceForSocialInitiator()
    {
        var block = RuntimeContextBuilder.BuildBlock(
            initiator: new TurnInitiatorContext
            {
                ChannelName = "qq",
                UserId = "10001",
                UserName = "Alice",
                UserRole = "admin",
                ChannelContext = "group:123456",
                GroupId = "123456"
            },
            workspacePath: Directory.GetCurrentDirectory());

        Assert.Contains("## Request Source", block, StringComparison.Ordinal);
        Assert.Contains("Channel: qq", block, StringComparison.Ordinal);
        Assert.Contains("Conversation: group:123456", block, StringComparison.Ordinal);
        Assert.Contains("SenderName: Alice", block, StringComparison.Ordinal);
        Assert.Contains("SenderRole: admin", block, StringComparison.Ordinal);
        Assert.Contains("GroupChatId: 123456", block, StringComparison.Ordinal);
        Assert.DoesNotContain("SenderId: 10001", block, StringComparison.Ordinal);
    }
}
