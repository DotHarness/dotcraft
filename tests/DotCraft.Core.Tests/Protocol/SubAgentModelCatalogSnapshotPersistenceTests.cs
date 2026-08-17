using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentModelCatalogSnapshotPersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"subagent_model_snapshot_{Guid.NewGuid():N}");

    [Fact]
    public async Task ThreadStore_RoundTripsModelCatalogSnapshot()
    {
        Directory.CreateDirectory(_root);
        var store = new ThreadStore(_root);
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
                Model = "parent-model",
                SubAgentModelCatalogSnapshot = new SubAgentModelCatalogSnapshot
                {
                    ProviderId = "openai",
                    Description = "persisted rendered description",
                    Models =
                    [
                        new SubAgentModelCatalogItem
                        {
                            Id = "model-a",
                            SupportedReasoningEfforts =
                                [ModelReasoningEffort.Low, ModelReasoningEffort.High],
                            DefaultReasoningEffort = ModelReasoningEffort.High
                        }
                    ]
                }
            }
        };

        await store.SaveThreadAsync(thread);
        var loaded = Assert.IsType<SessionThread>(await new ThreadStore(_root).LoadThreadAsync(thread.Id));

        var snapshot = Assert.IsType<SubAgentModelCatalogSnapshot>(
            loaded.Configuration?.SubAgentModelCatalogSnapshot);
        Assert.Equal("openai", snapshot.ProviderId);
        Assert.Equal("persisted rendered description", snapshot.Description);
        var model = Assert.Single(snapshot.Models);
        Assert.Equal("model-a", model.Id);
        Assert.Equal([ModelReasoningEffort.Low, ModelReasoningEffort.High], model.SupportedReasoningEfforts);
        Assert.Equal(ModelReasoningEffort.High, model.DefaultReasoningEffort);
    }

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
}
