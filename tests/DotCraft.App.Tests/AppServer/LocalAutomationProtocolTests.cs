using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.Automations.Templates;
using DotCraft.Cron;
using DotCraft.Hosting;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Tests.AppServer;

public sealed class LocalAutomationProtocolTests
{
    [Fact]
    public async Task TaskList_And_TaskRun_DoNotUseSourceName()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);
            var task = await CreateTaskAsync(
                harness.FileStore,
                "manual-run",
                AutomationTaskStatus.Completed,
                new CronSchedule { Kind = "daily", DailyHour = 9, DailyMinute = 0 });
            task.NextRunAt = DateTimeOffset.UtcNow.AddDays(1);
            await harness.FileStore.SaveAsync(task, CancellationToken.None);

            var listResult = await harness.Handler.HandleTaskListAsync(
                Request(AppServerMethods.AutomationTaskList, new { }),
                CancellationToken.None);
            var listed = Assert.IsType<AutomationTaskListResult>(listResult).Tasks.Single();
            Assert.Equal("manual-run", listed.Id);

            var before = DateTimeOffset.UtcNow;
            var runResult = await harness.Handler.HandleTaskRunAsync(
                Request(AppServerMethods.AutomationTaskRun, new { taskId = task.Id }),
                CancellationToken.None);
            var after = DateTimeOffset.UtcNow;

            var wire = Assert.IsType<AutomationTaskRunResult>(runResult).Task;
            Assert.Equal("manual-run", wire.Id);
            Assert.Equal("pending", wire.Status);
            Assert.NotNull(wire.NextRunAt);
            Assert.InRange(wire.NextRunAt!.Value, before.AddSeconds(-1), after);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TaskRead_ResolvesByTaskIdOnly()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);
            await CreateTaskAsync(harness.FileStore, "read-me", AutomationTaskStatus.Pending, schedule: null);

            var result = await harness.Handler.HandleTaskReadAsync(
                Request(AppServerMethods.AutomationTaskRead, new { taskId = "read-me" }),
                CancellationToken.None);

            var wire = Assert.IsType<AutomationTaskWire>(result);
            Assert.Equal("read-me", wire.Id);
            Assert.Equal("pending", wire.Status);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task<LocalAutomationTask> CreateTaskAsync(
        LocalTaskFileStore fileStore,
        string id,
        AutomationTaskStatus status,
        CronSchedule? schedule)
    {
        var taskDirectory = Path.Combine(fileStore.TasksRoot, id);
        Directory.CreateDirectory(taskDirectory);

        var task = new LocalAutomationTask
        {
            TaskDirectory = taskDirectory,
            Id = id,
            Title = id,
            Status = status,
            Description = "Local automation protocol test",
            Schedule = schedule,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        await fileStore.SaveAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(task.WorkflowFilePath, "Manual run workflow", CancellationToken.None);
        return task;
    }

    private static AppServerIncomingMessage Request(string method, object parameters)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(parameters));
        return new AppServerIncomingMessage
        {
            JsonRpc = "2.0",
            Method = method,
            Params = doc.RootElement.Clone()
        };
    }

    private static TestHarness CreateHarness(string root)
    {
        var config = new AutomationsConfig
        {
            PollingInterval = TimeSpan.FromSeconds(30),
            MaxConcurrentTasks = 1
        };
        var paths = new DotCraftPaths
        {
            WorkspacePath = root,
            CraftPath = Path.Combine(root, ".craft")
        };
        Directory.CreateDirectory(Path.Combine(paths.CraftPath, "tasks"));

        var fileStore = new LocalTaskFileStore(config, paths, NullLogger<LocalTaskFileStore>.Instance);
        var workflowLoader = new LocalWorkflowLoader(NullLogger<LocalWorkflowLoader>.Instance);
        var source = new LocalAutomationSource(
            fileStore,
            workflowLoader,
            NullLoggerFactory.Instance,
            NullLogger<LocalAutomationSource>.Instance);
        var orchestrator = new AutomationOrchestrator(
            config,
            workflowLoader,
            new ToolProfileRegistry(),
            source,
            NullLogger<AutomationOrchestrator>.Instance);
        var userTemplateStore = new UserTemplateFileStore(
            config,
            paths,
            NullLogger<UserTemplateFileStore>.Instance);
        var handler = new AutomationsRequestHandler(
            orchestrator,
            fileStore,
            userTemplateStore);

        return new TestHarness(source, fileStore, handler);
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotcraft-local-automation-protocol-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed record TestHarness(
        LocalAutomationSource Source,
        LocalTaskFileStore FileStore,
        AutomationsRequestHandler Handler) : IDisposable
    {
        public void Dispose() => Source.Dispose();
    }
}
