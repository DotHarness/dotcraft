using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.Sandbox;

namespace DotCraft.Tests.Tools;

public sealed class CoreToolProviderSkillSelfLearningTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-coretoolprovider-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateTools_SelfLearningDisabled_DoesNotExposeSkillMutationTools()
    {
        var tools = CreateTools(new AppConfig.SelfLearningConfig { Enabled = false });

        Assert.DoesNotContain(tools, tool => string.Equals(tool.Name, "SkillManage", StringComparison.Ordinal));
        Assert.Contains(tools, tool => string.Equals(tool.Name, "SkillView", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateTools_SelfLearningEnabled_ExposesSkillViewAndSkillManageTools()
    {
        var tools = CreateTools(new AppConfig.SelfLearningConfig { Enabled = true });
        var skillTools = tools
            .Where(tool => tool.Name.StartsWith("Skill", StringComparison.Ordinal))
            .Select(tool => tool.Name)
            .ToArray();

        Assert.Equal(["SkillView", "SkillManage"], skillTools);
    }

    [Fact]
    public void CreateTools_AgentControlToolsDisabled_DoesNotExposeSubAgentControlTools()
    {
        var tools = CreateTools(
            new AppConfig.SelfLearningConfig { Enabled = false },
            agentControlToolAccess: AgentControlToolAccess.Disabled);
        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(AgentControlToolPolicy.AllToolNames, toolName => Assert.DoesNotContain(toolName, toolNames));
        Assert.Contains("ReadFile", toolNames);
    }

    [Fact]
    public void CreateTools_AgentControlToolsFull_ExposesSubAgentControlTools()
    {
        var tools = CreateTools(
            new AppConfig.SelfLearningConfig { Enabled = false },
            agentControlToolAccess: AgentControlToolAccess.Full);
        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(AgentControlToolPolicy.AllToolNames, toolName => Assert.Contains(toolName, toolNames));
    }

    [Fact]
    public void CreateTools_AgentControlToolsAllowList_ExposesOnlyAllowedControlTools()
    {
        var tools = CreateTools(
            new AppConfig.SelfLearningConfig { Enabled = false },
            agentControlToolAccess: AgentControlToolAccess.AllowList,
            allowedAgentControlTools: new HashSet<string>(["WaitAgent", "CloseAgent"], StringComparer.Ordinal));
        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("SpawnAgent", toolNames);
        Assert.DoesNotContain("SendMessage", toolNames);
        Assert.DoesNotContain("FollowupTask", toolNames);
        Assert.Contains("WaitAgent", toolNames);
        Assert.DoesNotContain("ListAgents", toolNames);
        Assert.Contains("CloseAgent", toolNames);
    }

    [Fact]
    public void SandboxCreateTools_AgentControlToolsDisabled_DoesNotExposeSubAgentControlTools()
    {
        var context = CreateContext(
            new AppConfig.SelfLearningConfig { Enabled = false },
            agentControlToolAccess: AgentControlToolAccess.Disabled,
            sandboxEnabled: true);
        var tools = new SandboxToolProvider().CreateTools(context).ToList();
        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(AgentControlToolPolicy.AllToolNames, toolName => Assert.DoesNotContain(toolName, toolNames));
        Assert.Contains("ReadFile", toolNames);
        Assert.Contains("Exec", toolNames);
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

    private List<Microsoft.Extensions.AI.AITool> CreateTools(
        AppConfig.SelfLearningConfig selfLearning,
        AgentControlToolAccess agentControlToolAccess = AgentControlToolAccess.Full,
        IReadOnlySet<string>? allowedAgentControlTools = null)
    {
        var context = CreateContext(selfLearning, agentControlToolAccess, allowedAgentControlTools);
        return new CoreToolProvider().CreateTools(context).ToList();
    }

    private ToolProviderContext CreateContext(
        AppConfig.SelfLearningConfig selfLearning,
        AgentControlToolAccess agentControlToolAccess = AgentControlToolAccess.Full,
        IReadOnlySet<string>? allowedAgentControlTools = null,
        bool sandboxEnabled = false)
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
                Enabled = sandboxEnabled,
                IdleTimeoutSeconds = 0
            }
        };
        var skillsLoader = new SkillsLoader(_tempRoot);
        var chatClientRegistry = new ChatClientRegistry();
        var mainRuntime = chatClientRegistry.ResolveMainRuntime(config);
        var mainModel = mainRuntime.Model;
        return new ToolProviderContext
        {
            Config = config,
            ChatClient = chatClientRegistry.GetChatClient(mainRuntime),
            ChatClientRegistry = chatClientRegistry,
            EffectiveProviderId = mainRuntime.ProviderId,
            EffectiveProviderProtocol = mainRuntime.Protocol,
            EffectiveMainModel = mainModel,
            WorkspacePath = _tempRoot,
            BotPath = _tempRoot,
            MemoryStore = new MemoryStore(_tempRoot),
            SkillsLoader = skillsLoader,
            SkillMutationApplier = new WorkspaceFileSkillMutationApplier(skillsLoader),
            ApprovalService = new AutoApproveApprovalService(),
            CurrentThreadId = "thread_parent",
            CurrentThreadSource = ThreadSource.User(),
            CurrentOriginChannel = "dotcraft-desktop",
            CurrentChannelContext = "workspace:test",
            AgentControlToolAccess = agentControlToolAccess,
            AllowedAgentControlTools = allowedAgentControlTools
        };
    }
}
