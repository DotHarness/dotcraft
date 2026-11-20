using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context.WorldState;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tests.Tools;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentModelChoicesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"subagent_model_choices_{Guid.NewGuid():N}");

    [Fact]
    public async Task ConfigurationChange_RefreshesChoicesWithoutChangingSpawnAgentSchema()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "parent-model");
        config.SubAgent.Models = [new SubAgentModelChoice { ProviderId = "openai", Model = "model-a" }];
        await using var factory = CreateAgentFactory(new CatalogProvider("model-a", "model-b"), config);
        var service = CreateService(factory);
        var thread = await service.CreateThreadAsync(
            new SessionIdentity { WorkspacePath = _root, UserId = "test", ChannelName = "test" });

        var description = SpawnAgentDescription(await service.GetEffectiveToolSnapshotAsync(thread.Id));
        var initial = Choices(await service.GetThreadAsync(thread.Id));
        Assert.Equal("openai/model-a", Assert.Single(initial.Models).Id);
        Assert.Null(initial.DefaultModel);

        config.SubAgent.Models = [new SubAgentModelChoice { ProviderId = "openai", Model = "model-b" }];
        config.SubAgent.ProviderPreferences["openai"] = new SubAgentModelPreference
        {
            ProviderId = "deepseek",
            Model = "deepseek-flash"
        };
        service.InvalidateThreadAgents();

        Assert.Equal(description, SpawnAgentDescription(await service.GetEffectiveToolSnapshotAsync(thread.Id)));
        var refreshed = Choices(await service.GetThreadAsync(thread.Id));
        Assert.Equal("openai/model-b", Assert.Single(refreshed.Models).Id);
        Assert.Equal("deepseek/deepseek-flash", refreshed.DefaultModel);
    }

    [Fact]
    public void WorldState_RestatesChoicesOnlyWhenTheyChange()
    {
        var world = new WorldState();
        world.AddSection(new SubAgentModelsSection());
        var thread = new SessionThread { Id = "thread-1", OriginChannel = "test" };
        WorldStateSnapshot? baseline = null;

        bool Sent(SubAgentModelCatalogSnapshot? choices)
        {
            var context = new WorldStateContext { Thread = thread, SubAgentModels = choices };
            var render = baseline == null
                ? world.RenderFull(context)
                : world.Render(context, baseline, sectionIdsInHistory: null);
            baseline = render.Snapshot;
            return render.Fragments.Count > 0;
        }

        var choices = new SubAgentModelCatalogSnapshot
        {
            ProviderId = "openai",
            Models = [new SubAgentModelCatalogItem { Id = "openai/model-a", ProviderId = "openai", Model = "model-a" }]
        };
        Assert.True(Sent(choices));
        Assert.False(Sent(SubAgentModelCatalogSnapshots.Clone(choices)));
        Assert.False(Sent(null));

        var changed = SubAgentModelCatalogSnapshots.Clone(choices);
        changed.DefaultModel = "deepseek/deepseek-flash";
        Assert.True(Sent(changed));
    }

    private static SubAgentModelCatalogSnapshot Choices(SessionThread thread) =>
        Assert.IsType<SubAgentModelCatalogSnapshot>(thread.Configuration?.SubAgentModelCatalogSnapshot);

    private AgentFactory CreateAgentFactory(CatalogProvider provider, AppConfig? config = null)
    {
        config ??= AppConfigTestFactory.CreateOpenAI(model: "parent-model");
        var registry = new ChatClientRegistry(new ModelProviderRegistry([provider]));
        var skills = new SkillsLoader(_root);
        var source = new CoreToolSource(
            config,
            registry,
            skills,
            new AutoApproveApprovalService(),
            new StubBackgroundTerminalService());
        return new AgentFactory(
            dotcraftPath: _root,
            workspacePath: _root,
            config: config,
            memoryStore: new MemoryStore(_root),
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: registry,
            toolSources: [source]);
    }

    private SessionService CreateService(AgentFactory factory) => new(
        factory,
        factory.CreateAgentForMode(AgentMode.Agent),
        new SessionPersistenceService(new ThreadStore(_root)),
        new SessionGate());

    private static string SpawnAgentDescription(EffectiveToolSnapshot snapshot) =>
        Assert.Single(snapshot.ModelVisibleDefinitions, definition =>
            string.Equals(definition.Name.Name, nameof(AgentTools.SpawnAgent), StringComparison.Ordinal))
            .Description;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class CatalogProvider(params string[] models) : IModelProvider, IModelCatalogProvider
    {
        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAI];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new TestChatClient();

        public Task<ModelCatalogResult> FetchModelsAsync(
            EffectiveModelRuntime runtime,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ModelCatalogResult
            {
                Success = true,
                Models = models.Select(static id => new ModelCatalogEntry { Id = id }).ToList()
            });
    }

    private sealed class TestChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([]));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Empty();

        private static async IAsyncEnumerable<ChatResponseUpdate> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
