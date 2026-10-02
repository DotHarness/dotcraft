using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class ProviderHostedCapabilityPlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-capability-plan-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Build_FreezesDeferredSearchModeAndProviderProtocol()
    {
        var native = ProviderHostedCapabilityPlanner.Build(CreateContext(ModelProviderProtocols.OpenAIResponses));
        Assert.Equal(DeferredToolLoadingMode.Native, native.DeferredToolSearch?.Mode);
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, native.DeferredToolSearch?.ProviderProtocol);

        var simulated = ProviderHostedCapabilityPlanner.Build(CreateContext(ModelProviderProtocols.OpenAIChatCompletions));
        Assert.Equal(DeferredToolLoadingMode.Simulated, simulated.DeferredToolSearch?.Mode);
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, simulated.DeferredToolSearch?.ProviderProtocol);
    }

    private AgentRuntimeContext CreateContext(string protocol)
    {
        var botPath = Path.Combine(_root, Guid.NewGuid().ToString("N"), ".craft");
        Directory.CreateDirectory(botPath);
        var config = new AppConfig
        {
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = "gpt-5" } }
        };
        config.Providers["openai"] = new AppConfig.ModelProviderConfig { Protocol = protocol, ApiKey = "sk-test" };
        return new AgentRuntimeContext
        {
            Config = config,
            ChatClientRegistry = TestModelProviderRegistry.Create(),
            WorkspacePath = Path.GetDirectoryName(botPath)!,
            BotPath = botPath,
            MemoryStore = new MemoryStore(botPath),
            SkillsLoader = new SkillsLoader(botPath),
            ContextPageManager = new ContextPageManager(),
            ApprovalService = new AutoApproveApprovalService(),
            PathBlacklist = new PathBlacklist([])
        };
    }
}
