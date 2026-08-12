using System.Text.Json.Nodes;
using DotCraft.DynamicWorkflows;
using DotCraft.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed class DynamicWorkflowStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-workflow-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartAsync_ConvertsAbandonedRunningStateToInterrupted()
    {
        var craft = Path.Combine(_root, ".craft");
        var store = new DynamicWorkflowStore(craft);
        var run = new DynamicWorkflowRun
        {
            RunId = "run_test_000001",
            AttemptId = "attempt_001",
            Name = "test",
            ParentThreadId = "thread_test",
            ParentTurnId = "turn_001",
            ScriptPath = Path.Combine(craft, "workflows", "runs", "run_test_000001", "script.js"),
            ScriptHash = new string('a', 64),
            Status = DynamicWorkflowStatuses.Running,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await store.CreateAsync(run, "export const meta = { name: 'test', description: 'test' }; return null;", CancellationToken.None);
        var service = new DynamicWorkflowService(
            _root,
            store,
            new DynamicWorkflowParser(),
            new StructuredWorkflowResultRegistry(),
            new ManagedChildProcessFactory(),
            new DotCraft.Configuration.AppConfig(),
            [],
            NullLogger<DynamicWorkflowService>.Instance);

        await service.StartAsync();

        var loaded = await store.ReadStateAsync(run.RunId, CancellationToken.None);
        Assert.Equal(DynamicWorkflowStatuses.Interrupted, loaded!.Status);
        Assert.NotNull(loaded.CompletedAt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
