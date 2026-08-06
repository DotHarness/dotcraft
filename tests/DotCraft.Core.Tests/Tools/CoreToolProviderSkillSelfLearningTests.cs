using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class CoreToolProviderSkillSelfLearningTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-coretoolprovider-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateTools_SelfLearningDisabled_DoesNotExposeSkillMutationTools()
    {
        var tools = await CreateToolsAsync(new AppConfig.SelfLearningConfig { Enabled = false });

        Assert.DoesNotContain(tools, tool => string.Equals(tool.Name.Name, "SkillManage", StringComparison.Ordinal));
        Assert.Contains(tools, tool => string.Equals(tool.Name.Name, "SkillView", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateTools_SelfLearningEnabled_ExposesSkillViewAndSkillManageTools()
    {
        var tools = await CreateToolsAsync(new AppConfig.SelfLearningConfig { Enabled = true });
        var skillTools = tools
            .Where(tool => tool.Name.Name.StartsWith("Skill", StringComparison.Ordinal))
            .Select(tool => tool.Name.Name)
            .ToArray();

        Assert.Equal(["SkillManage", "SkillView"], skillTools);
    }

    [Fact]
    public async Task CreateTools_SubAgentChild_PreservesSubAgentControlSchema()
    {
        var rootTools = await CreateToolsAsync(new AppConfig.SelfLearningConfig { Enabled = false });
        var childTools = await CreateToolsAsync(
            new AppConfig.SelfLearningConfig { Enabled = false },
            providerCapabilities: ["subagent-child"]);
        var rootNames = rootTools.Select(tool => tool.Name.Name).Order(StringComparer.Ordinal).ToArray();
        var childNames = childTools.Select(tool => tool.Name.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(rootNames, childNames);
        Assert.All(AgentControlToolPolicy.AllToolNames, toolName => Assert.Contains(toolName, childNames));
    }

    [Fact]
    public async Task CreateTools_MainThread_ExposesSubAgentControlTools()
    {
        var tools = await CreateToolsAsync(new AppConfig.SelfLearningConfig { Enabled = false });
        var toolNames = tools.Select(tool => tool.Name.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(AgentControlToolPolicy.AllToolNames, toolName => Assert.Contains(toolName, toolNames));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test directories.
        }
    }

    private async Task<List<ToolDefinition>> CreateToolsAsync(
        AppConfig.SelfLearningConfig selfLearning,
        IReadOnlyList<string>? providerCapabilities = null)
    {
        Directory.CreateDirectory(_tempRoot);
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Skills = new AppConfig.SkillsConfig
        {
            SelfLearning = selfLearning
        };
        config.Tools = new AppConfig.ToolsConfig
        {
            Sandbox = new AppConfig.SandboxConfig
            {
                Enabled = false,
                IdleTimeoutSeconds = 0
            }
        };
        var skillsLoader = new SkillsLoader(_tempRoot);
        var chatClientRegistry = new ChatClientRegistry();
        var source = new CoreToolSource(
            config,
            chatClientRegistry,
            skillsLoader,
            new AutoApproveApprovalService(),
            new StubBackgroundTerminalService(),
            skillMutationApplier: new WorkspaceFileSkillMutationApplier(skillsLoader));
        var registrations = await source.GetRegistrationsAsync(new ToolPlanningContext(
            "thread_parent",
            null,
            _tempRoot,
            "agent",
            null,
            providerCapabilities ?? [],
            1));
        return registrations.Select(registration => registration.Definition).ToList();
    }
}
