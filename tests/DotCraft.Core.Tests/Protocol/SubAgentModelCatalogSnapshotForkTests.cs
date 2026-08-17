using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using Xunit;
using TestableSessionService = DotCraft.Tests.Sessions.Protocol.AppServer.CoreTestableSessionService;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentModelCatalogSnapshotForkTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"subagent_model_fork_{Guid.NewGuid():N}");

    [Fact]
    public async Task FullHistoryFork_PreservesParentSnapshot()
    {
        var (service, context) = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            Options("all"),
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);

        var child = await service.GetThreadAsync(result.ChildThreadId);
        var parentSnapshot = Assert.IsType<SubAgentModelCatalogSnapshot>(
            context.ParentThread.Configuration?.SubAgentModelCatalogSnapshot);
        var childSnapshot = Assert.IsType<SubAgentModelCatalogSnapshot>(
            child.Configuration?.SubAgentModelCatalogSnapshot);
        Assert.Equal(parentSnapshot.Description, childSnapshot.Description);
        Assert.Equal(parentSnapshot.Models.Select(static model => model.Id),
            childSnapshot.Models.Select(static model => model.Id));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("2")]
    public async Task IndependentFork_ClearsCopiedParentSnapshot(string forkTurns)
    {
        var (service, context) = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            Options(forkTurns),
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);

        var child = await service.GetThreadAsync(result.ChildThreadId);
        Assert.Null(child.Configuration?.SubAgentModelCatalogSnapshot);
    }

    [Fact]
    public async Task FullHistoryFork_RejectsModelOverride()
    {
        var (_, context) = await CreateContextAsync();
        var options = Options("all");
        options.InvocationModelCatalogSnapshot =
            context.ParentThread.Configuration?.SubAgentModelCatalogSnapshot;
        options.InvocationModelOverride = new SubAgentInvocationModelOverride { Model = "model-a" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SpawnAgentAsync(
                context,
                options,
                waitForCompletion: false,
                coordinator: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task IndependentFork_RejectsUnsupportedEffortForResolvedChildModel()
    {
        var (_, context) = await CreateContextAsync();
        var options = Options("none");
        options.InvocationModelCatalogSnapshot =
            context.ParentThread.Configuration?.SubAgentModelCatalogSnapshot;
        options.InvocationModelOverride = new SubAgentInvocationModelOverride
        {
            Effort = ModelReasoningEffort.High
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SpawnAgentAsync(
                context,
                options,
                waitForCompletion: false,
                coordinator: null,
                CancellationToken.None));
    }

    private async Task<(TestableSessionService Service, SubAgentSessionContext Context)> CreateContextAsync()
    {
        Directory.CreateDirectory(_root);
        var service = new TestableSessionService(new ThreadStore(_root));
        var parent = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _root,
            UserId = "user",
            ChannelName = "test"
        });
        parent.Configuration = new ThreadConfiguration
        {
            ProviderId = "openai",
            Model = "parent-model",
            SubAgentModelCatalogSnapshot = Snapshot()
        };
        return (service, new SubAgentSessionContext
        {
            SessionService = service,
            ParentThread = parent,
            ParentTurnId = "turn_parent",
            RootThreadId = parent.Id,
            Depth = 0
        });
    }

    private static SubAgentSpawnOptions Options(string forkTurns) => new()
    {
        AgentPrompt = "inspect code",
        TaskName = $"inspect_{forkTurns}",
        ForkTurns = forkTurns,
        MaxDepth = 1
    };

    private static SubAgentModelCatalogSnapshot Snapshot() => new()
    {
        ProviderId = "openai",
        Description = "stable description",
        Models =
        [
            new SubAgentModelCatalogItem { Id = "model-a" },
            new SubAgentModelCatalogItem
            {
                Id = "parent-model",
                SupportedReasoningEfforts = [ModelReasoningEffort.Low]
            }
        ]
    };

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
