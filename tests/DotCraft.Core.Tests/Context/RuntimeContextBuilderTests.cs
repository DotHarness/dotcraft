using DotCraft.Agents;
using DotCraft.Context;
using Microsoft.Extensions.AI;
using TurnInitiatorContext = DotCraft.Sessions.TurnInitiatorContext;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class RuntimeContextBuilderTests
{
    [Fact]
    public void BuildBlock_WrapsRuntimeContextInSystemReminder()
    {
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);

        var block = RuntimeContextBuilder.BuildBlock(
            modeManager: modeManager,
            workspacePath: Directory.GetCurrentDirectory());

        Assert.StartsWith("<system-reminder>", block, StringComparison.Ordinal);
        Assert.EndsWith("</system-reminder>", block, StringComparison.Ordinal);
        Assert.Contains("## Environment", block, StringComparison.Ordinal);
        Assert.Contains("## Mode", block, StringComparison.Ordinal);
        Assert.Contains("CurrentMode: Plan", block, StringComparison.Ordinal);
        Assert.DoesNotContain("ModeTransition: None", block, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowedActionProfile", block, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanState", block, StringComparison.Ordinal);
        Assert.Contains("## Mode Action", block, StringComparison.Ordinal);
        Assert.DoesNotContain("[Runtime Context]", block, StringComparison.Ordinal);
        Assert.DoesNotContain("<dotcraft_", block, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendRuntimeContext_AcknowledgesPlanToAgentTransitionAfterAppending()
    {
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);
        modeManager.SwitchMode(AgentMode.Agent);
        var contents = new List<AIContent> { new TextContent("hello") };

        contents.AppendRuntimeContext(modeManager: modeManager, workspacePath: Directory.GetCurrentDirectory(), hasActivePlan: true);

        var runtimeText = Assert.IsType<TextContent>(contents[^1]).Text;
        Assert.Contains("ModeTransition: PlanToAgent", runtimeText, StringComparison.Ordinal);
        Assert.Contains("## Mode Transition", runtimeText, StringComparison.Ordinal);
        Assert.Contains("Plan: Active", runtimeText, StringComparison.Ordinal);
        Assert.False(modeManager.JustSwitchedFromPlan);
    }

    [Fact]
    public void BuildBlock_AgentModeIncludesCurrentMode()
    {
        var modeManager = new AgentModeManager();

        var block = RuntimeContextBuilder.BuildBlock(
            modeManager: modeManager,
            workspacePath: Directory.GetCurrentDirectory());

        Assert.Contains("CurrentMode: Agent", block, StringComparison.Ordinal);
        Assert.DoesNotContain("ModeTransition: None", block, StringComparison.Ordinal);
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

        Assert.DoesNotContain("## Request Source", block, StringComparison.Ordinal);
        Assert.DoesNotContain("ChannelContext", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Channel: cli", block, StringComparison.Ordinal);
        Assert.DoesNotContain("dotcraft-desktop", block, StringComparison.Ordinal);
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
