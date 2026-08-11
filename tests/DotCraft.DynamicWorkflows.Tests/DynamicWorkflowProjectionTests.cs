using System.Text.Json.Nodes;
using DotCraft.DynamicWorkflows;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed class DynamicWorkflowProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-workflow-projection-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Read_ProjectsDeclaredDiscoveredAndUnphasedAgentsWithReplayMetrics()
    {
        var store = new DynamicWorkflowStore(Path.Combine(_root, ".craft"));
        var run = new DynamicWorkflowRun
        {
            RunId = "run_projection_001",
            AttemptId = "attempt_001",
            Name = "release-review",
            Description = "Review a proposed change.",
            DeclaredPhases = ["inspect", "review"],
            ParentThreadId = "thread_parent",
            ParentTurnId = "turn_001",
            ScriptPath = Path.Combine(store.GetRunDirectory("run_projection_001"), "script.js"),
            ScriptHash = new string('a', 64),
            Status = DynamicWorkflowStatuses.Running,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await store.CreateAsync(run, "export const meta = { name: 'release-review', description: 'Review a proposed change.' }; return null;", CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "workflow.phase", new JsonObject { ["name"] = "inspect", ["detail"] = "Collect context" }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "agent.requested", new JsonObject { ["operationId"] = "inventory", ["label"] = "Inventory", ["phase"] = "inspect" }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "agent.replayed", new JsonObject { ["operationId"] = "inventory", ["phase"] = "inspect" }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "workflow.phase", new JsonObject { ["name"] = "verify", ["detail"] = "Verify behavior" }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "agent.requested", new JsonObject { ["operationId"] = "compat", ["label"] = "Compatibility" }, CancellationToken.None);

        var projection = new DynamicWorkflowProjectionService(store);
        var view = await projection.ReadAsync("thread_parent", run.RunId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(["inspect", "review", "verify"], view.Phases.Select(static phase => phase.Name));
        Assert.True(view.Phases[0].Agents[0].Replayed);
        Assert.Equal("completed", view.Phases[0].Agents[0].Status);
        Assert.Single(view.UnphasedAgents);
        Assert.Equal(2, view.Totals.AgentCount);
        Assert.Equal(1, view.Totals.ReplayedCount);
        Assert.True(view.Controls.CanPause);
        Assert.Null(await projection.ReadAsync("thread_other", run.RunId, CancellationToken.None));
    }

    [Fact]
    public async Task Read_MarksUnfinishedAgentsStoppedWhenRunIsStopped()
    {
        var store = new DynamicWorkflowStore(Path.Combine(_root, ".craft"));
        var run = new DynamicWorkflowRun
        {
            RunId = "run_projection_stopped",
            AttemptId = "attempt_stopped",
            Name = "release-review",
            ParentThreadId = "thread_parent",
            ParentTurnId = "turn_stopped",
            ScriptPath = Path.Combine(store.GetRunDirectory("run_projection_stopped"), "script.js"),
            ScriptHash = new string('b', 64),
            Status = DynamicWorkflowStatuses.Running,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await store.CreateAsync(run, "return null;", CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "agent.requested", new JsonObject { ["operationId"] = "queued" }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "agent.requested", new JsonObject { ["operationId"] = "active" }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "agent.started", new JsonObject { ["operationId"] = "active", ["childThreadId"] = "child_active" }, CancellationToken.None);
        run = run with { Status = DynamicWorkflowStatuses.Stopped, CompletedAt = DateTimeOffset.UtcNow };
        await store.WriteStateAsync(run, CancellationToken.None);

        var view = await new DynamicWorkflowProjectionService(store).ReadAsync("thread_parent", run.RunId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.All(view.UnphasedAgents, static agent => Assert.Equal("stopped", agent.Status));
        Assert.Equal(2, view.Totals.StoppedCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
