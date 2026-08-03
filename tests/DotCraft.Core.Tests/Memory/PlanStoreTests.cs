using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Persistence;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;
using PlanTodo = DotCraft.Memory.PlanTodo;
using Xunit;

namespace DotCraft.Tests.Memory;

public sealed class PlanStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlanStoreTests_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly WorkspaceStateDatabase _stateRuntime;
    private readonly ThreadStore _threadStore;
    private readonly PlanStore _planStore;

    public PlanStoreTests()
    {
        _stateRuntime = new WorkspaceStateDatabase(_root);
        _threadStore = new ThreadStore(_root, _stateRuntime);
        _planStore = new PlanStore(_root, _stateRuntime);
    }

    [Fact]
    public async Task SaveStructuredPlanAsync_RoundTripsStructuredPlan()
    {
        var thread = CreateThread("thread_plan_db");
        await _threadStore.SaveThreadAsync(thread);

        await _planStore.SaveStructuredPlanAsync(thread.Id, new StructuredPlan
        {
            Title = "DB plan",
            Overview = "Stored in SQLite",
            Content = "Implementation details",
            Todos =
            [
                new PlanTodo { Id = "do-work", Content = "Do the work", Status = PlanTodoStatus.Pending }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var loaded = await _planStore.LoadStructuredPlanAsync(thread.Id);

        Assert.NotNull(loaded);
        Assert.Equal("DB plan", loaded.Title);
    }

    [Fact]
    public async Task DeleteThread_CascadesPlanButArchiveKeepsIt()
    {
        var thread = CreateThread("thread_plan_lifecycle");
        await _threadStore.SaveThreadAsync(thread);
        await _planStore.SaveStructuredPlanAsync(thread.Id, new StructuredPlan
        {
            Title = "Keep while archived",
            Overview = "Lifecycle",
            Content = "The plan follows the thread.",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        thread.Status = ThreadStatus.Archived;
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _threadStore.SaveThreadAsync(thread);

        Assert.True(_planStore.StructuredPlanExists(thread.Id));

        _threadStore.DeleteThread(thread.Id);

        Assert.False(_planStore.StructuredPlanExists(thread.Id));
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
            // Test cleanup is best-effort on Windows.
        }
    }

    private static SessionThread CreateThread(string id) => new()
    {
        Id = id,
        WorkspacePath = Path.Combine(Path.GetTempPath(), "workspace"),
        UserId = "local",
        OriginChannel = "test",
        Status = ThreadStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow,
        HistoryMode = HistoryMode.Server
    };
}
