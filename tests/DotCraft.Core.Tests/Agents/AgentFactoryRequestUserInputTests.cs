using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AgentFactoryRequestUserInputTests : IDisposable
{
    private readonly string _tempDir;

    public AgentFactoryRequestUserInputTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AFRUI_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task RequestUserInputTool_IsStableAcrossAgentAndPlanModes()
    {
        await using var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: AppConfigTestFactory.CreateOpenAI(),
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolSources: []);

        var agentTools = agentFactory.CreateToolsForMode(AgentMode.Agent).Select(tool => tool.Name).ToArray();
        var planTools = agentFactory.CreateToolsForMode(AgentMode.Plan).Select(tool => tool.Name).ToArray();

        Assert.Contains(nameof(RequestUserInputTools.RequestUserInput), agentTools);
        Assert.Contains(nameof(RequestUserInputTools.RequestUserInput), planTools);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
