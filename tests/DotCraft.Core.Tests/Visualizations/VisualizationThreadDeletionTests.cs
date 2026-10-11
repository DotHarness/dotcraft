using DotCraft.Agents;
using DotCraft.Contributions;
using DotCraft.Visualizations;
using DotCraft.Sessions;
using DotCraft.Tests.Contributions;
using Microsoft.Extensions.AI;
using Xunit;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.Core.Tests.Visualizations;

public sealed class VisualizationThreadDeletionTests : IAsyncLifetime
{
    private readonly ContributionAgentHost _host = new("VisualizationThreadDeletion");
    private ThreadStore _store = null!;

    public Task InitializeAsync()
    {
        _store = new ThreadStore(Path.Combine(_host.WorkspacePath, ".craft"));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DeleteThreadPermanentlyAsync_DeletesOnlyThatThreadsVisualizationFilesAndUiState()
    {
        var chatClient = new ContributionChatClient();
        var service = new SessionService(
            _host.CreateFactory(new ContributionRegistry(), chatClient),
            chatClient.AsAIAgent(),
            new SessionPersistenceService(_store),
            new SessionGate());
        var deleted = await CreateThreadAsync(service);
        var kept = await CreateThreadAsync(service);
        var deletedDirectory = WriteVisualization(deleted.Id);
        var keptDirectory = WriteVisualization(kept.Id);
        var state = new VisualizationWidgetState("{\"selected\":\"north\"}", null);
        service.SetVisualizationWidgetState(deleted.Id, Path.Combine(deletedDirectory, "chart.html"), state);
        service.SetVisualizationWidgetState(kept.Id, Path.Combine(keptDirectory, "chart.html"), state);
        service.SetGenerativeUiState(deleted.Id, "turn_a", "item_a", 0, "{\"tab\":\"a\"}");
        service.SetGenerativeUiState(kept.Id, "turn_a", "item_a", 0, "{\"tab\":\"b\"}");

        await service.DeleteThreadPermanentlyAsync(deleted.Id);

        Assert.False(Directory.Exists(deletedDirectory));
        Assert.True(File.Exists(Path.Combine(keptDirectory, "chart.html")));
        Assert.Null(_store.VisualizationWidgetStates.Load(deleted.Id, Path.Combine(deletedDirectory, "chart.html")));
        Assert.Equal(state, _store.VisualizationWidgetStates.Load(kept.Id, Path.Combine(keptDirectory, "chart.html")));
        Assert.Empty(_store.GenerativeUiStates.Load(deleted.Id));
        Assert.Equal("{\"tab\":\"b\"}", Assert.Single(_store.GenerativeUiStates.Load(kept.Id)).StateJson);
    }

    private string WriteVisualization(string threadId)
    {
        var directory = Path.Combine(_host.WorkspacePath, ".craft", "visualizations", threadId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "chart.html"), "<div>chart</div>");
        return directory;
    }

    private Task<SessionThread> CreateThreadAsync(SessionService service) =>
        service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _host.WorkspacePath
        });

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _host.Dispose();
    }
}
