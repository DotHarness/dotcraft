using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentModelCatalogSnapshotTests
{
    [Fact]
    public async Task CreateAsync_DeduplicatesInCatalogOrderAndCapsOverrides()
    {
        var provider = new CatalogProvider(
            "model-c", "model-a", "MODEL-C", "model-b", "model-d", "model-e", "model-f");

        var snapshot = await SubAgentModelCatalogSnapshots.CreateAsync(
            AppConfigTestFactory.CreateOpenAI(),
            new ModelProviderRegistry([provider]),
            "openai",
            CancellationToken.None);

        Assert.Equal(
            ["model-c", "model-a", "model-b", "model-d", "model-e"],
            snapshot.Models.Select(static model => model.Id));
        Assert.Contains("model-c", snapshot.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("model-f", snapshot.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_CatalogFailureCreatesEmptyOverrideSnapshot()
    {
        var snapshot = await SubAgentModelCatalogSnapshots.CreateAsync(
            AppConfigTestFactory.CreateOpenAI(),
            new ModelProviderRegistry([new CatalogProvider(new InvalidOperationException("offline"))]),
            "openai",
            CancellationToken.None);

        Assert.Empty(snapshot.Models);
        Assert.Throws<InvalidOperationException>(() =>
        {
            SubAgentModelCatalogSnapshots.ResolveInvocationOverride(snapshot, "model-a", null);
        });
    }

    [Fact]
    public async Task CreateAsync_RendersKnownReasoningEffortsAndDefault()
    {
        var snapshot = await SubAgentModelCatalogSnapshots.CreateAsync(
            AppConfigTestFactory.CreateOpenAI(model: "model-a"),
            new ModelProviderRegistry([new CatalogProvider("model-a")]),
            "openai",
            CancellationToken.None);

        var model = Assert.Single(snapshot.Models);
        Assert.Contains(ModelReasoningEffort.ExtraHigh, model.SupportedReasoningEfforts);
        Assert.Equal(ModelReasoningEffort.Medium, model.DefaultReasoningEffort);
        Assert.Contains("xhigh", snapshot.Description, StringComparison.Ordinal);
        Assert.Contains("medium (default)", snapshot.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveInvocationOverride_CanonicalizesModelAndValidatesEffort()
    {
        var snapshot = Snapshot(
            new SubAgentModelCatalogItem
            {
                Id = "model-a",
                SupportedReasoningEfforts = [ModelReasoningEffort.Low, ModelReasoningEffort.High],
                DefaultReasoningEffort = ModelReasoningEffort.High
            });

        var resolved = Assert.IsType<SubAgentInvocationModelOverride>(
            SubAgentModelCatalogSnapshots.ResolveInvocationOverride(snapshot, "MODEL-A", "high"));

        Assert.Equal("model-a", resolved.Model);
        Assert.Equal(ModelReasoningEffort.High, resolved.Effort);
        Assert.Throws<InvalidOperationException>(() =>
            SubAgentModelCatalogSnapshots.ResolveInvocationOverride(snapshot, "model-a", "medium"));
        Assert.Throws<InvalidOperationException>(() =>
            SubAgentModelCatalogSnapshots.ResolveInvocationOverride(snapshot, "model-a", "999"));
        Assert.Throws<InvalidOperationException>(() =>
            SubAgentModelCatalogSnapshots.ResolveInvocationOverride(snapshot, "model-b", null));
    }

    private static SubAgentModelCatalogSnapshot Snapshot(params SubAgentModelCatalogItem[] models) => new()
    {
        ProviderId = "openai",
        Description = "stable snapshot description",
        Models = [.. models]
    };

    private sealed class CatalogProvider : IModelProvider, IModelCatalogProvider
    {
        private readonly IReadOnlyList<string> _models;
        private readonly Exception? _exception;

        public CatalogProvider(params string[] models) => _models = models;

        public CatalogProvider(Exception exception)
        {
            _models = [];
            _exception = exception;
        }

        public IReadOnlyCollection<string> Protocols => [ModelProviderProtocols.OpenAI];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) =>
            throw new NotSupportedException();

        public Task<ModelCatalogResult> FetchModelsAsync(
            EffectiveModelRuntime runtime,
            CancellationToken cancellationToken)
        {
            if (_exception != null)
                throw _exception;

            return Task.FromResult(new ModelCatalogResult
            {
                Success = true,
                Models = _models.Select(static id => new ModelCatalogEntry { Id = id }).ToList()
            });
        }
    }
}
