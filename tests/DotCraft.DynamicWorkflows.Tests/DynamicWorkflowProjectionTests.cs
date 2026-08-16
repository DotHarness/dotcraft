using System.Text.Json.Nodes;

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
        Assert.Equal("replayed", view.Phases[0].Agents[0].Status);
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

    [Fact]
    public async Task Read_ProjectsJsonPhaseDetailsAndPreservesCancelledAgentStatus()
    {
        var store = new DynamicWorkflowStore(Path.Combine(_root, ".craft"));
        var run = new DynamicWorkflowRun
        {
            RunId = "run_projection_details",
            AttemptId = "attempt_details",
            Name = "detail-review",
            DeclaredPhases = ["inspect", "review", "verify", "publish", "future"],
            ParentThreadId = "thread_parent",
            ParentTurnId = "turn_details",
            ScriptPath = Path.Combine(store.GetRunDirectory("run_projection_details"), "script.js"),
            ScriptHash = new string('c', 64),
            Status = DynamicWorkflowStatuses.Succeeded,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        await store.CreateAsync(run, "return null;", CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "workflow.phase", new JsonObject
        {
            ["name"] = "inspect",
            ["detail"] = new JsonObject { ["branch"] = "feature", ["readOnly"] = true }
        }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "workflow.phase", new JsonObject
        {
            ["name"] = "review",
            ["detail"] = new JsonArray("one", 2)
        }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "agent.requested", new JsonObject
        {
            ["operationId"] = "cancelled",
            ["phase"] = "review"
        }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "agent.completed", new JsonObject
        {
            ["operationId"] = "cancelled",
            ["status"] = "cancelled"
        }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "workflow.phase", new JsonObject
        {
            ["name"] = "verify",
            ["detail"] = true
        }, CancellationToken.None);
        await store.AppendJournalAsync(run.RunId, "workflow.phase", new JsonObject
        {
            ["name"] = "publish",
            ["detail"] = null
        }, CancellationToken.None);

        var view = await new DynamicWorkflowProjectionService(store).ReadAsync("thread_parent", run.RunId, CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal("{\"branch\":\"feature\",\"readOnly\":true}", view.Phases[0].Detail);
        Assert.Equal("[\"one\",2]", view.Phases[1].Detail);
        Assert.Equal("true", view.Phases[2].Detail);
        Assert.Null(view.Phases[3].Detail);
        Assert.Equal("pending", view.Phases[4].Status);
        Assert.Equal("stopped", Assert.Single(view.Phases[1].Agents).Status);
        Assert.Equal("completed", view.Phases[1].Status);
        Assert.Equal(1, view.Totals.StoppedCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
