using DotCraft.Agents;
using DotCraft.Context;
using Microsoft.Extensions.AI;

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
        Assert.Contains("## Runtime Context", block, StringComparison.Ordinal);
        Assert.Contains("CurrentMode: Plan", block, StringComparison.Ordinal);
        Assert.Contains("AllowedActionProfile: ReadOnlyObserve", block, StringComparison.Ordinal);
        Assert.Contains("PlanState: Absent", block, StringComparison.Ordinal);
        Assert.Contains("## Mode Action", block, StringComparison.Ordinal);
        Assert.Contains("use `RequestUserInput` if available instead of plain text questions", block, StringComparison.Ordinal);
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
        Assert.Contains("PlanState: Active", runtimeText, StringComparison.Ordinal);
        Assert.False(modeManager.JustSwitchedFromPlan);
    }
}
