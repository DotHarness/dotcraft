using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Xunit;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SubAgentSessionControlTests
{
    [Theory]
    [InlineData("none", "anthropic", "claude-test")]
    [InlineData("2", "anthropic", "claude-test")]
    [InlineData("all", "openai", "parent-model")]
    public async Task SpawnAgent_DefaultOnAnotherProvider_SwitchesOnlyIndependentChildren(
        string forkTurns,
        string expectedProvider,
        string expectedModel)
    {
        var context = await CreateContextAsync(new ThreadConfiguration { ProviderId = "openai", Model = "parent-model" });

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = forkTurns,
                SubAgentPreference = SubAgentModelPreference.From("anthropic", ModelPreferenceRules.CreateManual("claude-test")),
                RuntimeConfig = TwoProviderConfig()
            },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);
        Assert.Equal(expectedProvider, child.Configuration?.ProviderId);
        Assert.Equal(expectedModel, child.Configuration?.Model);
    }

    [Fact]
    public async Task SpawnAgent_InvocationEntryOnAnotherProvider_RunsOnThatProvider()
    {
        var context = await CreateContextAsync(new ThreadConfiguration { ProviderId = "openai", Model = "parent-model" });
        var snapshot = new SubAgentModelCatalogSnapshot
        {
            ProviderId = "openai",
            Models = [new SubAgentModelCatalogItem { Id = "anthropic/claude-test", ProviderId = "anthropic", Model = "claude-test" }]
        };

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = "none",
                RuntimeConfig = TwoProviderConfig(),
                InvocationModelOverride = SubAgentModelCatalogSnapshots.ResolveInvocationOverride(snapshot, "anthropic/claude-test", null),
                InvocationModelCatalogSnapshot = snapshot
            },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);
        Assert.Equal("anthropic", child.Configuration?.ProviderId);
        Assert.Equal("claude-test", child.Configuration?.Model);
    }

    [Fact]
    public async Task SpawnAgent_TargetProviderWithoutCredentials_CreatesNoChild()
    {
        var context = await CreateContextAsync(new ThreadConfiguration { ProviderId = "openai", Model = "parent-model" });
        var config = TwoProviderConfig();
        config.Providers["anthropic"].ApiKey = string.Empty;

        await Assert.ThrowsAsync<InvalidOperationException>(() => SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = "none",
                SubAgentPreference = SubAgentModelPreference.From("anthropic", ModelPreferenceRules.CreateManual("claude-test")),
                RuntimeConfig = config
            },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None));

        Assert.Empty(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id));
    }

    [Fact]
    public async Task ModelChoiceSnapshot_ListsUsableConfiguredChoicesInOrder()
    {
        var config = TwoProviderConfig();
        config.Providers["offline"] = new AppConfig.ModelProviderConfig { Protocol = ModelProviderProtocols.OpenAI };
        config.SubAgent.Models =
        [
            new SubAgentModelChoice { ProviderId = "anthropic", Model = "claude-test" },
            new SubAgentModelChoice { ProviderId = "offline", Model = "local-model" },
            new SubAgentModelChoice { ProviderId = "removed", Model = "gone-model" },
            new SubAgentModelChoice { ProviderId = "openai", Model = "openrouter/style-id" }
        ];

        var snapshot = await SubAgentModelCatalogSnapshots.CreateAsync(
            config,
            new ModelProviderRegistry([]),
            "openai",
            CancellationToken.None);

        Assert.Equal(["anthropic/claude-test", "openai/openrouter/style-id"], snapshot.Models.Select(model => model.Id));
        var resolved = SubAgentModelCatalogSnapshots.ResolveInvocationOverride(snapshot, "openai/openrouter/style-id", null);
        Assert.Equal("openai", resolved?.ProviderId);
        Assert.Equal("openrouter/style-id", resolved?.Model);
    }

    private static AppConfig TwoProviderConfig()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "parent-model");
        var anthropic = AppConfigTestFactory.CreateAnthropic(model: "claude-test");
        config.Providers["anthropic"] = anthropic.Providers["anthropic"];
        return config;
    }
}
