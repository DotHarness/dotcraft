using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.Automations.Templates;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Hosting;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
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

    [Theory]
    [InlineData("worktree")]
    [InlineData("isolated")]
    public async Task TaskCreate_NormalizesWorktreeModeInWorkflowAndWire(string requestedMode)
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);

            var createResult = await harness.Handler.HandleTaskCreateAsync(
                Request(AppServerMethods.AutomationTaskCreate, new
                {
                    title = "Build mini game",
                    description = "Create a tiny game",
                    workspaceMode = requestedMode
                }),
                CancellationToken.None);

            var created = Assert.IsType<AutomationTaskCreateResult>(createResult);
            var workflow = await File.ReadAllTextAsync(
                Path.Combine(created.TaskDirectory, "workflow.md"),
                CancellationToken.None);
            Assert.Contains("workspace: worktree", workflow);
            Assert.DoesNotContain("workspace: isolated", workflow);

            var readResult = await harness.Handler.HandleTaskReadAsync(
                Request(AppServerMethods.AutomationTaskRead, new { taskId = created.TaskId }),
                CancellationToken.None);
            var wire = Assert.IsType<AutomationTaskWire>(readResult);
            Assert.Equal("worktree", wire.WorkspaceMode);
            Assert.Null(wire.Worktree);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TemplateSave_NormalizesLegacyWorkspaceMode()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);

            var result = await harness.Handler.HandleTemplateSaveAsync(
                Request(AppServerMethods.AutomationTemplateSave, new
                {
                    title = "Legacy worktree",
                    workflowMarkdown = "---\nworkspace: \"isolated\"\n---\nDo work",
                    defaultWorkspaceMode = "isolated",
                    needsThreadBinding = false
                }),
                CancellationToken.None);

            var wire = Assert.IsType<AutomationTemplateSaveResult>(result).Template;
            Assert.Equal("worktree", wire.DefaultWorkspaceMode);
            Assert.Contains("workspace: worktree", wire.WorkflowMarkdown);
            Assert.DoesNotContain("isolated", wire.WorkflowMarkdown);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TaskRead_NormalizesLegacyIsolatedWorkflowWithoutMigratingFile()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);
            var task = await CreateTaskAsync(harness.FileStore, "legacy-isolated", AutomationTaskStatus.Pending, schedule: null);
            await File.WriteAllTextAsync(
                task.WorkflowFilePath,
                "---\nworkspace: isolated\n---\nLegacy workflow",
                CancellationToken.None);

            var result = await harness.Handler.HandleTaskReadAsync(
                Request(AppServerMethods.AutomationTaskRead, new { taskId = task.Id }),
                CancellationToken.None);

            var wire = Assert.IsType<AutomationTaskWire>(result);
            Assert.Equal("worktree", wire.WorkspaceMode);
            Assert.Contains("workspace: isolated", await File.ReadAllTextAsync(task.WorkflowFilePath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TaskDiscardWorktree_RemovesManagedWorktreeAndKeepsTask()
    {
        var root = CreateTestRoot();
        try
        {
            InitGitRepository(root);
            using var harness = CreateHarness(root);
            await using var agentFactory = CreateAgentFactory(root);
            var sessionService = CreateSessionService(root, agentFactory);
            var sessionClient = new AutomationSessionClient(sessionService, harness.Paths);
            harness.Orchestrator.SetSessionClient(sessionClient);

            var task = await CreateTaskAsync(
                harness.FileStore,
                "discard-me",
                AutomationTaskStatus.Completed,
                schedule: null);
            await File.WriteAllTextAsync(
                task.WorkflowFilePath,
                "---\nworkspace: worktree\n---\nDo work",
                CancellationToken.None);

            var thread = await sessionService.CreateThreadAsync(
                new SessionIdentity
                {
                    ChannelName = "automations",
                    UserId = "task:discard-me",
                    WorkspacePath = root
                },
                displayName: "discard-me");
            task.ThreadId = thread.Id;
            await harness.FileStore.SaveAsync(task, CancellationToken.None);
            var worktree = await sessionClient.EnsureTaskWorktreeAsync(
                thread.Id,
                task.Id,
                CancellationToken.None);

            Assert.True(Directory.Exists(worktree.Path));
            Assert.Equal(0, RunGitExitCode(root, "rev-parse", "--verify", "refs/heads/dotcraft/task-discard-me"));

            var result = await harness.Handler.HandleTaskDiscardWorktreeAsync(
                Request(AppServerMethods.AutomationTaskDiscardWorktree, new { taskId = task.Id }),
                CancellationToken.None);

            var wire = Assert.IsType<AutomationTaskDiscardWorktreeResult>(result).Task;
            Assert.Equal("discard-me", wire.Id);
            Assert.Null(wire.Worktree);
            Assert.True(File.Exists(task.TaskFilePath));
            Assert.False(Directory.Exists(worktree.Path));
            Assert.NotEqual(0, RunGitExitCode(root, "rev-parse", "--verify", "refs/heads/dotcraft/task-discard-me"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task WorktreeRetentionSweep_RemovesOnlyIdleCleanTaskWorktrees()
    {
        var root = CreateTestRoot();
        try
        {
            InitGitRepository(root);
            using var harness = CreateHarness(
                root,
                config =>
                {
                    config.WorktreeRetentionEnabled = true;
                    config.WorktreeRetentionIdlePeriod = TimeSpan.FromDays(14);
                });
            await using var agentFactory = CreateAgentFactory(root);
            var sessionService = CreateSessionService(root, agentFactory);
            var sessionClient = new AutomationSessionClient(sessionService, harness.Paths);
            harness.Orchestrator.SetSessionClient(sessionClient);

            var clean = await CreateTaskWithWorktreeAsync(
                harness,
                sessionService,
                sessionClient,
                "clean-idle");
            var ahead = await CreateTaskWithWorktreeAsync(
                harness,
                sessionService,
                sessionClient,
                "ahead-idle");

            var aheadPath = sessionClient.GetTaskWorktreePath(ahead.Id);
            await File.WriteAllTextAsync(
                Path.Combine(aheadPath, "ahead.txt"),
                "keep me" + Environment.NewLine,
                CancellationToken.None);
            RunGit(aheadPath, "add", "ahead.txt");
            RunGit(aheadPath, "commit", "-m", "ahead");

            var idleSince = DateTimeOffset.UtcNow.AddDays(-30);
            await SetTaskUpdatedAtAsync(clean, idleSince);
            await SetTaskUpdatedAtAsync(ahead, idleSince);

            await harness.Orchestrator.TriggerImmediatePollAsync(CancellationToken.None);

            Assert.False(Directory.Exists(sessionClient.GetTaskWorktreePath(clean.Id)));
            Assert.NotEqual(0, RunGitExitCode(root, "rev-parse", "--verify", "refs/heads/dotcraft/task-clean-idle"));
            Assert.True(Directory.Exists(aheadPath));
            Assert.Equal(0, RunGitExitCode(root, "rev-parse", "--verify", "refs/heads/dotcraft/task-ahead-idle"));
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

    private static async Task<LocalAutomationTask> CreateTaskWithWorktreeAsync(
        TestHarness harness,
        SessionService sessionService,
        AutomationSessionClient sessionClient,
        string taskId)
    {
        var task = await CreateTaskAsync(
            harness.FileStore,
            taskId,
            AutomationTaskStatus.Completed,
            schedule: null);
        await File.WriteAllTextAsync(
            task.WorkflowFilePath,
            "---\nworkspace: worktree\n---\nDo work",
            CancellationToken.None);

        var thread = await sessionService.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = "automations",
                UserId = $"task:{taskId}",
                WorkspacePath = harness.Paths.WorkspacePath
            },
            displayName: taskId);
        task.ThreadId = thread.Id;
        await harness.FileStore.SaveAsync(task, CancellationToken.None);
        await sessionClient.EnsureTaskWorktreeAsync(thread.Id, task.Id, CancellationToken.None);
        return task;
    }

    private static Task SetTaskUpdatedAtAsync(LocalAutomationTask task, DateTimeOffset updatedAt)
    {
        var content = File.ReadAllText(task.TaskFilePath);
        var replacement = $"updated_at: {updatedAt:O}";
        var updated = Regex.Replace(
            content,
            @"^updated_at:\s*.*$",
            replacement,
            RegexOptions.Multiline);
        if (string.Equals(content, updated, StringComparison.Ordinal))
            throw new InvalidOperationException("Task file did not contain updated_at.");
        File.WriteAllText(task.TaskFilePath, updated);
        return Task.CompletedTask;
    }

    private static TestHarness CreateHarness(string root, Action<AutomationsConfig>? configure = null)
    {
        var config = new AutomationsConfig
        {
            PollingInterval = TimeSpan.FromSeconds(30),
            MaxConcurrentTasks = 1
        };
        configure?.Invoke(config);
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

        return new TestHarness(source, fileStore, handler, orchestrator, paths);
    }

    private static AgentFactory CreateAgentFactory(string root)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return new AgentFactory(
            dotcraftPath: root,
            workspacePath: root,
            config: config,
            memoryStore: new MemoryStore(root),
            skillsLoader: new SkillsLoader(root),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: Array.Empty<IAgentToolProvider>());
    }

    private static SessionService CreateSessionService(string root, AgentFactory agentFactory)
    {
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var store = new ThreadStore(root);
        var persistence = new SessionPersistenceService(store);
        return new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());
    }

    private static void InitGitRepository(string root)
    {
        RunGit(root, "init");
        RunGit(root, "config", "user.email", "test@example.com");
        RunGit(root, "config", "user.name", "Test User");
        File.WriteAllText(Path.Combine(root, ".gitignore"), ".craft/" + Environment.NewLine);
        File.WriteAllText(Path.Combine(root, "README.md"), "initial" + Environment.NewLine);
        RunGit(root, "add", ".gitignore", "README.md");
        RunGit(root, "commit", "-m", "init");
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var exitCode = RunGitExitCode(workingDirectory, args, throwOnFailure: true);
        Assert.Equal(0, exitCode);
    }

    private static int RunGitExitCode(
        string workingDirectory,
        params string[] args) =>
        RunGitExitCode(workingDirectory, args, throwOnFailure: false);

    private static int RunGitExitCode(
        string workingDirectory,
        string[] args,
        bool throwOnFailure)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git command.");
        process.StandardInput.Close();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {string.Join(" ", args)} timed out.");
        }

        if (throwOnFailure && process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}");
        }

        return process.ExitCode;
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
        if (!Directory.Exists(path))
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(entry, FileAttributes.Normal); }
            catch { /* best-effort cleanup for git object files on Windows */ }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch when (attempt < 2)
            {
                Thread.Sleep(100);
            }
        }
    }

    private sealed record TestHarness(
        LocalAutomationSource Source,
        LocalTaskFileStore FileStore,
        AutomationsRequestHandler Handler,
        AutomationOrchestrator Orchestrator,
        DotCraftPaths Paths) : IDisposable
    {
        public void Dispose() => Source.Dispose();
    }
}
