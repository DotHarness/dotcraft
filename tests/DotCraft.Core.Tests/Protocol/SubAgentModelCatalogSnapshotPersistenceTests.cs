using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tests.Tools;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentModelCatalogSnapshotPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"subagent_model_snapshot_{Guid.NewGuid():N}");

    [Fact]
    public async Task ExistingThread_PersistsFirstSnapshotAcrossColdReload()
    {
        Directory.CreateDirectory(_root);
        var provider = new CatalogProvider("model-a");
        var thread = new SessionThread
        {
            Id = SessionIdGenerator.NewThreadId(),
            WorkspacePath = _root,
            OriginChannel = "test",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Configuration = new ThreadConfiguration
            {
                ProviderId = "openai",
                Model = "parent-model"
            }
        };
        await new ThreadStore(_root).SaveThreadAsync(thread);

        string firstDescription;
        await using (var factory = CreateAgentFactory(provider))
        {
            var tools = await CreateService(factory).GetEffectiveToolSnapshotAsync(thread.Id);
            firstDescription = SpawnAgentDescription(tools);
        }

        var persisted = Assert.IsType<SessionThread>(await new ThreadStore(_root).LoadThreadAsync(thread.Id));
        var persistedSnapshot = Assert.IsType<SubAgentModelCatalogSnapshot>(
            persisted.Configuration?.SubAgentModelCatalogSnapshot);
        Assert.Equal(["model-a"], persistedSnapshot.Models.Select(static model => model.Id));
        Assert.Contains(persistedSnapshot.Description, firstDescription, StringComparison.Ordinal);

        provider.Models = ["model-b"];
        await using (var factory = CreateAgentFactory(provider))
        {
            var tools = await CreateService(factory).GetEffectiveToolSnapshotAsync(thread.Id);
            Assert.Equal(firstDescription, SpawnAgentDescription(tools));
        }

        Assert.Equal(1, provider.FetchCount);
    }

    private AgentFactory CreateAgentFactory(CatalogProvider provider)
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "parent-model");
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
            // Best-effort cleanup.
        }
    }

    private sealed class CatalogProvider(params string[] models) : IModelProvider, IModelCatalogProvider
    {
        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAI];

        public IReadOnlyList<string> Models { get; set; } = models;

        public int FetchCount { get; private set; }

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) => new TestChatClient();

        public Task<ModelCatalogResult> FetchModelsAsync(
            EffectiveModelRuntime runtime,
            CancellationToken cancellationToken)
        {
            FetchCount++;
            return Task.FromResult(new ModelCatalogResult
            {
                Success = true,
                Models = Models.Select(static id => new ModelCatalogEntry { Id = id }).ToList()
            });
        }
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
