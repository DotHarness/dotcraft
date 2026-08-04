using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using DotCraft.Security;
using DotCraft.Tools;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Contract = DotCraft.Protocol.AppServer;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using Xunit;

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
                Request<Contract.AutomationTaskListParams>(new { }),
                CancellationToken.None);
            var listed = listResult.Tasks.Value!.Single();
            Assert.Equal("manual-run", listed.Id);

            var before = DateTimeOffset.UtcNow;
            var runResult = await harness.Handler.HandleTaskRunAsync(
                Request<Contract.AutomationTaskRunParams>(new { taskId = task.Id }),
                CancellationToken.None);
            var after = DateTimeOffset.UtcNow;

            var wire = runResult.Task.Value!;
            Assert.Equal("manual-run", wire.Id.Value);
            Assert.Equal("pending", wire.Status.Value);
            Assert.True(wire.NextRunAt.IsSet);
            Assert.InRange(wire.NextRunAt.Value!.Value, before.AddSeconds(-1), after);
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
                Request<Contract.AutomationTaskReadParams>(new { taskId = "read-me" }),
                CancellationToken.None);

            var wire = result;
            Assert.Equal("read-me", wire.Id);
            Assert.Equal("pending", wire.Status);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TaskCreate_PersistsWorktreeModeInWorkflowAndWire()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);

            var createResult = await harness.Handler.HandleTaskCreateAsync(
                Request<Contract.AutomationTaskCreateParams>(new
                {
                    title = "Build mini game",
                    description = "Create a tiny game",
                    workspaceMode = "worktree"
                }),
                CancellationToken.None);

            var created = createResult;
            var workflow = await File.ReadAllTextAsync(
                Path.Combine(created.TaskDirectory.Value!, "workflow.md"),
                CancellationToken.None);
            Assert.Contains("workspace: worktree", workflow);

            var readResult = await harness.Handler.HandleTaskReadAsync(
                Request<Contract.AutomationTaskReadParams>(new { taskId = created.TaskId.Value }),
                CancellationToken.None);
            var wire = readResult;
            Assert.Equal("worktree", wire.WorkspaceMode);
            Assert.False(wire.Worktree.IsSet);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("project", "worktree")]
    [InlineData("worktree", "project")]
    public async Task TaskCreate_TemplateWorkflowExplicitWorkspaceModeOverridesTemplate(
        string templateMode,
        string requestedMode)
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);

            var createResult = await harness.Handler.HandleTaskCreateAsync(
                Request<Contract.AutomationTaskCreateParams>(new
                {
                    title = "Override template workspace",
                    description = "Use the target picker value",
                    workflowTemplate = $"---\nworkspace: {templateMode}\nmax_rounds: 5\n---\nDo work",
                    workspaceMode = requestedMode
                }),
                CancellationToken.None);

            var created = createResult;
            var workflow = await File.ReadAllTextAsync(
                Path.Combine(created.TaskDirectory.Value!, "workflow.md"),
                CancellationToken.None);
            Assert.Contains($"workspace: {requestedMode}", workflow);
            Assert.DoesNotContain($"workspace: {templateMode}", workflow);
            Assert.Contains("max_rounds: 5", workflow);

            var readResult = await harness.Handler.HandleTaskReadAsync(
                Request<Contract.AutomationTaskReadParams>(new { taskId = created.TaskId.Value }),
                CancellationToken.None);
            var wire = readResult;
            Assert.Equal(requestedMode, wire.WorkspaceMode);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TaskCreate_TemplateWorkflowExplicitWorkspaceModeInsertsIntoFrontMatter()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);

            var createResult = await harness.Handler.HandleTaskCreateAsync(
                Request<Contract.AutomationTaskCreateParams>(new
                {
                    title = "Insert template workspace",
                    description = "Add missing workspace metadata",
                    workflowTemplate = "---\nmax_rounds: 3\n---\nDo work",
                    workspaceMode = "worktree"
                }),
                CancellationToken.None);

            var created = createResult;
            var workflow = await File.ReadAllTextAsync(
                Path.Combine(created.TaskDirectory.Value!, "workflow.md"),
                CancellationToken.None);
            Assert.True(
                workflow.StartsWith("---\nworkspace: worktree\nmax_rounds: 3\n---\n", StringComparison.Ordinal),
                workflow);

            var readResult = await harness.Handler.HandleTaskReadAsync(
                Request<Contract.AutomationTaskReadParams>(new { taskId = created.TaskId.Value }),
                CancellationToken.None);
            var wire = readResult;
            Assert.Equal("worktree", wire.WorkspaceMode);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TaskCreate_TemplateWorkflowExplicitWorkspaceModeWrapsBodyOnlyWorkflow()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);

            var createResult = await harness.Handler.HandleTaskCreateAsync(
                Request<Contract.AutomationTaskCreateParams>(new
                {
                    title = "Wrap body workflow",
                    description = "Preserve body workspace text",
                    workflowTemplate = "Do work\nworkspace: project in body",
                    workspaceMode = "worktree"
                }),
                CancellationToken.None);

            var created = createResult;
            var workflow = await File.ReadAllTextAsync(
                Path.Combine(created.TaskDirectory.Value!, "workflow.md"),
                CancellationToken.None);
            Assert.True(
                workflow.StartsWith("---\nworkspace: worktree\n---\n", StringComparison.Ordinal),
                workflow);
            Assert.Contains("Do work\nworkspace: project in body", workflow);

            var readResult = await harness.Handler.HandleTaskReadAsync(
                Request<Contract.AutomationTaskReadParams>(new { taskId = created.TaskId.Value }),
                CancellationToken.None);
            var wire = readResult;
            Assert.Equal("worktree", wire.WorkspaceMode);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }


    [Fact]
    public async Task TaskCreate_PersistsAndRoundTripsAgentProfileId()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);

            var createResult = await harness.Handler.HandleTaskCreateAsync(
                Request<Contract.AutomationTaskCreateParams>(new
                {
                    title = "Bound to a profile",
                    description = "Runs as a specific agent",
                    agentProfileId = "team-reviewer"
                }),
                CancellationToken.None);

            var created = createResult;
            var taskMd = await File.ReadAllTextAsync(
                Path.Combine(created.TaskDirectory.Value!, "task.md"),
                CancellationToken.None);
            Assert.Contains("agent_profile_id: \"team-reviewer\"", taskMd);

            var readResult = await harness.Handler.HandleTaskReadAsync(
                Request<Contract.AutomationTaskReadParams>(new { taskId = created.TaskId.Value }),
                CancellationToken.None);
            Assert.Equal("team-reviewer", readResult.AgentProfileId.Value);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TaskCreate_WithoutAgentProfile_LeavesBindingNull()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);

            var createResult = await harness.Handler.HandleTaskCreateAsync(
                Request<Contract.AutomationTaskCreateParams>(new
                {
                    title = "No profile",
                    description = "Runs with the default automation agent"
                }),
                CancellationToken.None);

            var created = createResult;
            var taskMd = await File.ReadAllTextAsync(
                Path.Combine(created.TaskDirectory.Value!, "task.md"),
                CancellationToken.None);
            Assert.DoesNotContain("agent_profile_id", taskMd);

            var readResult = await harness.Handler.HandleTaskReadAsync(
                Request<Contract.AutomationTaskReadParams>(new { taskId = created.TaskId.Value }),
                CancellationToken.None);
            Assert.False(readResult.AgentProfileId.IsSet);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TemplateSave_PersistsAndRoundTripsDefaultAgentProfileId()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);

            var saveResult = await harness.Handler.HandleTemplateSaveAsync(
                Request<Contract.AutomationTemplateSaveParams>(new
                {
                    title = "Profile-defaulting template",
                    workflowMarkdown = "---\nworkspace: project\n---\nDo work",
                    defaultAgentProfileId = "team-reviewer",
                    needsThreadBinding = false
                }),
                CancellationToken.None);

            var saved = saveResult.Template.Value!;
            Assert.Equal("team-reviewer", saved.DefaultAgentProfileId);

            // Reloading from disk on the next list must preserve the default.
            var listResult = await harness.Handler.HandleTemplateListAsync(
                Request<Contract.AutomationTemplateListParams>(new { }),
                CancellationToken.None);
            var reloaded = listResult.Templates.Value!
                .Single(t => t.Id.Value == saved.Id.Value);
            Assert.Equal("team-reviewer", reloaded.DefaultAgentProfileId);
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
                Request<Contract.AutomationTaskDiscardWorktreeParams>(new { taskId = task.Id }),
                CancellationToken.None);

            var wire = result.Task.Value!;
            Assert.Equal("discard-me", wire.Id);
            Assert.False(wire.Worktree.IsSet);
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

    private static TParams Request<TParams>(object parameters) where TParams : class =>
        JsonSerializer.Deserialize<TParams>(
            JsonSerializer.Serialize(parameters),
            DotCraft.Protocol.AppServerContractJson.Options)!;

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
            toolSources: Array.Empty<IToolSource>());
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
