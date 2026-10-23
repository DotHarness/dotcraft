using System.Diagnostics;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Tools;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public abstract class SessionServiceWorktreeTestBase : IDisposable
{
    protected readonly string _tempDir;
    protected readonly ThreadStore _store;
    private readonly SessionPersistenceService _persistence;

    protected SessionServiceWorktreeTestBase()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dotcraft-session-worktree-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        RunGit("init");
        RunGit("config", "user.email", "test@example.com");
        RunGit("config", "user.name", "Test User");
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), ".craft/" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "initial" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "remove.txt"), "remove me" + Environment.NewLine);
        RunGit("add", ".gitignore", "README.md", "remove.txt");
        RunGit("commit", "-m", "init");

        _store = new ThreadStore(Path.Combine(_tempDir, ".craft"));
        _persistence = new SessionPersistenceService(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }
    protected static async Task<ThreadWorktreeInfo> HandoffAsync(SessionService service, string threadId, string branchName) =>
        (await service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = threadId,
            BranchName = branchName,
            CopyDirtyChanges = false
        })).Worktree!;

    protected SessionService CreateService(AgentFactory agentFactory)
    {
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        return new SessionService(agentFactory, defaultAgent, _persistence, new SessionGate());
    }

    protected AgentFactory CreateAgentFactory(IReadOnlyList<IToolSource>? toolProviders = null)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: toolProviders ?? Array.Empty<IToolSource>());
    }

    protected SessionIdentity MakeIdentity() =>
        new()
        {
            ChannelName = "test",
            UserId = "user",
            WorkspacePath = _tempDir
        };

    protected void RunGit(params string[] args) => _ = RunGitCapture(_tempDir, args);

    protected static string RunGitCapture(string workingDirectory, params string[] args)
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
        ConfigureGitEnvironment(psi, workingDirectory);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git setup command.");
        process.StandardInput.Close();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {string.Join(" ", args)} timed out.");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}");
        return stdout;
    }

    protected static int RunGitExitCode(string workingDirectory, params string[] args)
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
        ConfigureGitEnvironment(psi, workingDirectory);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git setup command.");
        process.StandardInput.Close();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {string.Join(" ", args)} timed out.");
        }

        return process.ExitCode;
    }

    private static void ConfigureGitEnvironment(ProcessStartInfo psi, string workingDirectory)
    {
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["GIT_PAGER"] = "cat";
        psi.Environment["PAGER"] = "cat";
        psi.Environment["PWD"] = workingDirectory;
    }

    protected sealed class RecordingToolProvider : IToolSource
    {
        public string SourceId => "recording";
        public int Priority => 10;

        public List<ToolPlanningContext> Contexts { get; } = [];

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);
        }
    }
}

public sealed class SessionServiceWorktreeCreateTests : SessionServiceWorktreeTestBase
{
    [Fact]
    public async Task CreateWorktreeAndForkAsync_BindsWorktreeAsExecutionWorkspaceWithoutMovingThreadState()
    {
        var recorder = new RecordingToolProvider();
        await using var agentFactory = CreateAgentFactory([recorder]);
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity(), displayName: "Source Branch");
        File.AppendAllText(Path.Combine(_tempDir, ".git", "info", "exclude"), ".env\nbuild.log\n.worktreeinclude\n");
        File.WriteAllText(Path.Combine(_tempDir, ".worktreeinclude"), ".env\n");
        File.WriteAllText(Path.Combine(_tempDir, ".env"), "TOKEN=local");
        File.WriteAllText(Path.Combine(_tempDir, "build.log"), "noise");

        var result = await service.CreateWorktreeAndForkAsync(new WorktreeCreateAndForkOptions
        {
            SourceThreadId = source.Id,
            CopyDirtyChanges = false
        });

        var thread = result.Thread;
        Assert.StartsWith("dotcraft/", result.Worktree.BranchName, StringComparison.Ordinal);
        Assert.Equal("Source Branch", thread.DisplayName);
        Assert.Equal(_tempDir, thread.WorkspacePath);
        Assert.Equal(source.Id, thread.ForkedFromId);
        Assert.Equal(result.Worktree, thread.Worktree);
        Assert.Equal(result.Worktree.Path, thread.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(_tempDir, Path.GetFullPath(result.Worktree.WorkspacePath));
        Assert.False(Directory.Exists(Path.Combine(result.Worktree.Path, ".craft", "threads")));
        Assert.Equal("TOKEN=local", File.ReadAllText(Path.Combine(result.Worktree.Path, ".env")));
        Assert.False(File.Exists(Path.Combine(result.Worktree.Path, "build.log")));

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.Equal(result.Worktree.Path, loaded?.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(result.Worktree.Path, loaded?.Worktree?.Path);
        var summary = Assert.Single(await _store.LoadIndexAsync(), s => s.Id == thread.Id);
        Assert.Equal(result.Worktree.Path, summary.Worktree?.Path);

        var status = await service.GetWorktreeStatusAsync(thread.Id);
        Assert.True(status.Exists);
        Assert.True(status.IsGitWorktree);
        Assert.Equal(result.Worktree.BranchName, status.BranchName);

        var seen = Assert.Single(recorder.Contexts, context => context.ThreadId == thread.Id);
        Assert.Equal(result.Worktree.Path, seen.WorkspacePath);
        var guard = new FileAccessGuard(seen.WorkspacePath, requireApprovalOutsideWorkspace: false);
        var worktreeFile = Path.Combine(result.Worktree.Path, "README.md");
        var projectFile = Path.Combine(_tempDir, "README.md");
        Assert.Null(await guard.ValidatePathAsync(worktreeFile, "read", worktreeFile));
        Assert.Contains(
            "outside workspace boundary",
            await guard.ValidatePathAsync(projectFile, "read", projectFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWorktreeAndForkAsync_DefaultDirtyHandoffCopiesTrackedUntrackedAndDeletedChanges()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity(), displayName: "Source");

        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "modified" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "new file" + Environment.NewLine);
        File.Delete(Path.Combine(_tempDir, "remove.txt"));

        var result = await service.CreateWorktreeAndForkAsync(new WorktreeCreateAndForkOptions
        {
            SourceThreadId = source.Id,
            BranchName = "dotcraft/worktree-dirty"
        });

        Assert.Equal("modified" + Environment.NewLine, File.ReadAllText(Path.Combine(result.Worktree.Path, "README.md")));
        Assert.Equal("new file" + Environment.NewLine, File.ReadAllText(Path.Combine(result.Worktree.Path, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(result.Worktree.Path, "remove.txt")));
        Assert.Equal("modified" + Environment.NewLine, File.ReadAllText(Path.Combine(_tempDir, "README.md")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "remove.txt")));

        Assert.Equal(WorktreeDirtyHandoffStatuses.Succeeded, result.Worktree.DirtyHandoff?.Status);
        Assert.True(result.Worktree.DirtyHandoff?.CopiedFileCount >= 2);
        Assert.True(result.Worktree.DirtyHandoff?.DeletedFileCount >= 1);

        var targetStatus = RunGitCapture(result.Worktree.Path, "status", "--porcelain=v1");
        Assert.Contains("M README.md", targetStatus);
        Assert.Contains("D remove.txt", targetStatus);
        Assert.Contains("?? notes.txt", targetStatus);
    }

    [Fact]
    public async Task CreateWorktreeAndStartAsync_StartsThreadInWorktreeWithoutMovingState()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);

        var result = await service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
        {
            Identity = MakeIdentity(),
            DisplayName = "Worktree Start",
            BranchName = "dotcraft/worktree-start",
            CopyDirtyChanges = false
        });

        Assert.Equal(_tempDir, result.Thread.WorkspacePath);
        Assert.Null(result.Thread.ForkedFromId);
        Assert.Equal(result.Worktree.Path, result.Thread.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(result.Worktree, result.Thread.Worktree);
        Assert.Empty(result.Thread.Turns);

        var loaded = await _store.LoadThreadAsync(result.Thread.Id);
        Assert.Equal(result.Worktree.Path, loaded?.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(result.Worktree.Path, loaded?.Worktree?.Path);
    }

    [Fact]
    public async Task ManagedWorktree_IsCreatedReusedTrackedRecreatedAndRemoved()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var thread = await service.CreateThreadAsync(MakeIdentity(), displayName: "Task worktree");
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "dirty local" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "local only" + Environment.NewLine);
        var path = Path.Combine(_tempDir, ".craft", "worktrees", "task-weekly-report");
        var options = new WorktreeEnsureOptions
        {
            ThreadId = thread.Id,
            BranchName = "dotcraft/task-weekly-report",
            Path = path,
            OwnerKind = "automationTask",
            OwnerId = "weekly-report"
        };

        var result = await service.EnsureManagedWorktreeAsync(options);

        Assert.Equal(path, result.Worktree.Path);
        Assert.Equal("dotcraft/task-weekly-report", result.Worktree.BranchName);
        Assert.NotEmpty(result.Worktree.BaseHead);
        Assert.Equal("automationTask", result.Worktree.OwnerKind);
        Assert.Equal("weekly-report", result.Worktree.OwnerId);
        Assert.Equal(path, result.Thread.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(path, result.Thread.Worktree?.Path);
        Assert.Equal("initial" + Environment.NewLine, File.ReadAllText(Path.Combine(path, "README.md")));
        Assert.False(File.Exists(Path.Combine(path, "notes.txt")));
        Assert.False(Directory.Exists(Path.Combine(path, ".craft", "threads")));
        var clean = await service.GetWorktreeStatusAsync(thread.Id);
        Assert.True(clean.Exists);
        Assert.False(clean.HasUncommittedChanges);
        Assert.False(clean.HasCommitsAheadOfBase);

        File.WriteAllText(Path.Combine(path, "marker.txt"), "kept" + Environment.NewLine);
        var reused = await service.EnsureManagedWorktreeAsync(options);

        Assert.True(reused.Reused);
        Assert.Equal("kept" + Environment.NewLine, File.ReadAllText(Path.Combine(path, "marker.txt")));
        Assert.Equal("automationTask", reused.Worktree.OwnerKind);
        var dirty = await service.GetWorktreeStatusAsync(thread.Id);
        Assert.True(dirty.HasUncommittedChanges);
        Assert.False(dirty.HasCommitsAheadOfBase);

        RunGitCapture(path, "add", "marker.txt");
        RunGitCapture(path, "commit", "-m", "marker");
        var committed = await service.GetWorktreeStatusAsync(thread.Id);
        Assert.False(committed.HasUncommittedChanges);
        Assert.True(committed.HasCommitsAheadOfBase);
        Assert.Equal(1, committed.AheadCount);
        Assert.Equal("weekly-report", committed.Worktree.OwnerId);

        RunGitCapture(_tempDir, "worktree", "remove", "--force", path);
        var recreated = await service.EnsureManagedWorktreeAsync(options);

        Assert.Equal("kept" + Environment.NewLine, File.ReadAllText(Path.Combine(recreated.Worktree.Path, "marker.txt")));
        Assert.Equal("dotcraft/task-weekly-report", RunGitCapture(recreated.Worktree.Path, "branch", "--show-current").Trim());

        await service.RemoveManagedWorktreeAsync(new WorktreeRemoveOptions
        {
            ThreadId = thread.Id,
            WorkspacePath = _tempDir,
            BranchName = "dotcraft/task-weekly-report",
            Path = path
        });

        Assert.False(Directory.Exists(path));
        Assert.NotEqual(0, RunGitExitCode(_tempDir, "rev-parse", "--verify", "refs/heads/dotcraft/task-weekly-report"));
        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.Null(loaded?.Worktree);
        Assert.Null(loaded?.Configuration?.ExecutionWorkspaceOverride);
    }
}

public sealed class SessionServiceWorktreeHandoffTests : SessionServiceWorktreeTestBase
{
    [Fact]
    public async Task HandoffThreadWorktreeAsync_MovesStagedUnstagedAndUntrackedChangesOutOfTheLocalCheckout()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "staged" + Environment.NewLine);
        RunGit("add", "README.md");
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "working" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "draft.txt"), "draft" + Environment.NewLine);
        var steps = new List<string>();

        var result = await service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = thread.Id,
            Progress = (step, status) => steps.Add($"{step}:{status}")
        });

        Assert.Equal(
            [
                "reuseWorktree:skipped",
                "stashTargetChanges:skipped",
                "createWorktree:started",
                "createWorktree:completed",
                "stashSourceChanges:started",
                "stashSourceChanges:completed",
                "applyChanges:started",
                "applyChanges:completed",
                "moveThread:started",
                "moveThread:completed"
            ],
            steps);
        var worktreePath = result.Worktree!.Path;
        Assert.Equal("staged", RunGitCapture(worktreePath, "show", ":README.md").Trim());
        Assert.Equal("working" + Environment.NewLine, File.ReadAllText(Path.Combine(worktreePath, "README.md")));
        Assert.True(File.Exists(Path.Combine(worktreePath, "draft.txt")));
        Assert.Equal("initial" + Environment.NewLine, File.ReadAllText(Path.Combine(_tempDir, "README.md")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "draft.txt")));
    }

    [Fact]
    public async Task HandoffThreadWorktreeAsync_MovesThreadToWorktreeAndDirtyChangesBackToLocal()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var thread = await service.CreateThreadAsync(MakeIdentity(), displayName: "Back");
        var worktreeResult = await service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = thread.Id,
            BranchName = "dotcraft/handoff-back",
            CopyDirtyChanges = false
        });
        var worktreePath = worktreeResult.Worktree!.Path;
        Assert.Equal(thread.Id, worktreeResult.Thread.Id);
        Assert.Equal(WorktreeHandoffModes.Worktree, worktreeResult.Mode);
        Assert.Equal(worktreePath, worktreeResult.Thread.Configuration?.ExecutionWorkspaceOverride);
        var moved = await _store.LoadThreadAsync(thread.Id);
        Assert.Equal(worktreePath, moved?.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(worktreePath, moved?.Worktree?.Path);

        File.WriteAllText(Path.Combine(worktreePath, "README.md"), "from worktree" + Environment.NewLine);
        File.WriteAllText(Path.Combine(worktreePath, "notes.txt"), "worktree note" + Environment.NewLine);
        File.Delete(Path.Combine(worktreePath, "remove.txt"));

        var localResult = await service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = thread.Id,
            Mode = WorktreeHandoffModes.Local
        });

        Assert.Equal(WorktreeHandoffModes.Local, localResult.Mode);
        Assert.Null(localResult.Thread.Worktree);
        Assert.Null(localResult.Thread.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal("from worktree" + Environment.NewLine, File.ReadAllText(Path.Combine(_tempDir, "README.md")));
        Assert.Equal("worktree note" + Environment.NewLine, File.ReadAllText(Path.Combine(_tempDir, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "remove.txt")));
        Assert.False(Directory.Exists(worktreePath));
        Assert.Equal("dotcraft/handoff-back", RunGitCapture(_tempDir, "branch", "--show-current").Trim());

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.Null(loaded?.Worktree);
        Assert.Null(loaded?.Configuration?.ExecutionWorkspaceOverride);
    }

    [Fact]
    public async Task HandoffThreadWorktreeAsync_RefusesToReturnToALocalCheckoutWithChanges()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var thread = await service.CreateThreadAsync(MakeIdentity(), displayName: "Conflict");
        var worktreeResult = await service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = thread.Id,
            BranchName = "dotcraft/handoff-conflict",
            CopyDirtyChanges = false
        });
        var worktreePath = worktreeResult.Worktree!.Path;

        File.WriteAllText(Path.Combine(worktreePath, "README.md"), "from worktree" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "remove.txt"), "staged" + Environment.NewLine);
        RunGit("add", "remove.txt");
        File.WriteAllText(Path.Combine(_tempDir, "remove.txt"), "working" + Environment.NewLine);

        var ex = await Assert.ThrowsAsync<WorktreeHandoffConflictException>(() =>
            service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
            {
                ThreadId = thread.Id,
                Mode = WorktreeHandoffModes.Local
            }));

        Assert.Equal(["remove.txt"], ex.ConflictPaths);
        Assert.Equal("staged", RunGitCapture(_tempDir, "show", ":remove.txt").Trim());
        Assert.Equal("working" + Environment.NewLine, File.ReadAllText(Path.Combine(_tempDir, "remove.txt")));
        Assert.Equal(worktreePath, (await _store.LoadThreadAsync(thread.Id))?.Worktree?.Path);
    }

    [Fact]
    public async Task HandoffThreadWorktreeAsync_RestoresTheSourceWhenChangesCannotMove()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        RunGit("switch", "-c", "other");
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "other" + Environment.NewLine);
        RunGit("commit", "-am", "other");
        RunGit("switch", "-");
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "staged" + Environment.NewLine);
        RunGit("add", "README.md");
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "working" + Environment.NewLine);

        await Assert.ThrowsAnyAsync<Exception>(() => service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = thread.Id,
            BaseRef = "other"
        }));

        Assert.Equal("staged", RunGitCapture(_tempDir, "show", ":README.md").Trim());
        Assert.Equal("working" + Environment.NewLine, File.ReadAllText(Path.Combine(_tempDir, "README.md")));
        Assert.Empty(await service.ListWorktreesAsync());
    }
}

public sealed class SessionServiceWorktreeSharingTests : SessionServiceWorktreeTestBase
{
    [Fact]
    public async Task HandoffThreadWorktreeAsync_ReusesAnUnboundWorktreeAndKeepsItsOwnChanges()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        var existingPath = Path.Combine(_tempDir, ".craft", "worktrees", "existing");
        RunGit("worktree", "add", "-b", "dotcraft/existing", existingPath);
        File.WriteAllText(Path.Combine(existingPath, "remove.txt"), "target edit" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "local edit" + Environment.NewLine);

        var result = await service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = thread.Id,
            Path = existingPath
        });

        Assert.Equal(Path.GetFullPath(existingPath), Path.GetFullPath(result.Worktree!.Path));
        Assert.Equal("dotcraft/existing", RunGitCapture(existingPath, "branch", "--show-current").Trim());
        Assert.Equal("local edit" + Environment.NewLine, File.ReadAllText(Path.Combine(existingPath, "README.md")));
        Assert.Equal("remove me" + Environment.NewLine, File.ReadAllText(Path.Combine(existingPath, "remove.txt")));
        Assert.True(result.DirtyHandoff?.TargetStashed);
        Assert.Contains("dotcraft-worktree-handoff:target", RunGitCapture(_tempDir, "stash", "list"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandoffThreadWorktreeAsync_LetsOnlyOneConversationClaimAnUnboundWorktree()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var first = await service.CreateThreadAsync(MakeIdentity());
        var second = await service.CreateThreadAsync(MakeIdentity());
        var existingPath = Path.Combine(_tempDir, ".craft", "worktrees", "shared");
        RunGit("worktree", "add", "-b", "dotcraft/shared", existingPath);
        var firstReused = new TaskCompletionSource();
        using var releaseFirst = new ManualResetEventSlim();

        var firstHandoff = Task.Run(() => service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = first.Id,
            Path = existingPath,
            CopyDirtyChanges = false,
            Progress = (step, status) =>
            {
                if (step != WorktreeHandoffSteps.ReuseWorktree || status != WorktreeHandoffStepStatuses.Completed)
                    return;
                firstReused.SetResult();
                releaseFirst.Wait();
            }
        }));
        await firstReused.Task;
        var secondHandoff = Task.Run(() => service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = second.Id,
            Path = existingPath,
            CopyDirtyChanges = false
        }));
        await Task.WhenAny(secondHandoff, Task.Delay(TimeSpan.FromMilliseconds(200)));
        releaseFirst.Set();

        await firstHandoff;
        await Assert.ThrowsAsync<InvalidOperationException>(() => secondHandoff);
        Assert.Equal(Path.GetFullPath(existingPath), Path.GetFullPath((await _store.LoadThreadAsync(first.Id))!.Worktree!.Path));
        Assert.Null((await _store.LoadThreadAsync(second.Id))?.Worktree);
    }

    [Fact]
    public async Task HandoffThreadWorktreeAsync_RefusesWhileInputIsQueued()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var thread = await service.CreateThreadAsync(MakeIdentity());
        thread.QueuedInputs.Add(new QueuedTurnInput { Id = "queued_1", ThreadId = thread.Id });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions { ThreadId = thread.Id }));
        Assert.Empty(await service.ListWorktreesAsync());
    }
}

public sealed class SessionServiceWorktreeCleanupTests : SessionServiceWorktreeTestBase
{
    [Fact]
    public async Task ArchiveThreadAsync_RemovesCleanWorktreesAndUnarchiveChecksThemOutAgain()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity());
        File.AppendAllText(Path.Combine(_tempDir, ".git", "info", "exclude"), ".env\n.worktreeinclude\n");
        File.WriteAllText(Path.Combine(_tempDir, ".worktreeinclude"), ".env\n");
        File.WriteAllText(Path.Combine(_tempDir, ".env"), "TOKEN=local");
        var worktree = (await service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = source.Id,
            BranchName = "dotcraft/archive-me",
            CopyDirtyChanges = false
        })).Worktree!;
        var fork = await service.CreateWorktreeAndForkAsync(new WorktreeCreateAndForkOptions
        {
            SourceThreadId = source.Id,
            BranchName = "dotcraft/fork-copy",
            CopyDirtyChanges = false
        });

        await service.ArchiveThreadAsync(source.Id);

        Assert.False(Directory.Exists(worktree.Path));
        Assert.Equal(0, RunGitExitCode(_tempDir, "rev-parse", "--verify", "refs/heads/dotcraft/archive-me"));

        await service.ArchiveThreadAsync(fork.Thread.Id);
        await service.UnarchiveThreadAsync(fork.Thread.Id);
        Assert.Equal("dotcraft/fork-copy", RunGitCapture(fork.Worktree.Path, "branch", "--show-current").Trim());

        await service.UnarchiveThreadAsync(source.Id);
        Assert.True(File.Exists(Path.Combine(worktree.Path, "README.md")));
        Assert.Equal("TOKEN=local", File.ReadAllText(Path.Combine(worktree.Path, ".env")));
        Assert.Equal("dotcraft/archive-me", RunGitCapture(worktree.Path, "branch", "--show-current").Trim());
    }

    [Fact]
    public async Task WorktreesWithUnsavedWork_SurviveThreadDeletionAndUnconfirmedRemoval()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var committed = await service.CreateThreadAsync(MakeIdentity());
        var dirty = await service.CreateThreadAsync(MakeIdentity());
        var committedWorktree = await HandoffAsync(service, committed.Id, "dotcraft/kept-on-delete");
        File.WriteAllText(Path.Combine(committedWorktree.Path, "README.md"), "committed" + Environment.NewLine);
        RunGitCapture(committedWorktree.Path, "commit", "-am", "work");
        var dirtyWorktree = await HandoffAsync(service, dirty.Id, "dotcraft/remove-me");
        File.WriteAllText(Path.Combine(dirtyWorktree.Path, "README.md"), "unsaved" + Environment.NewLine);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveWorktreeAsync(dirtyWorktree.Path, discardChanges: true));
        await service.ArchiveThreadAsync(committed.Id);
        await service.DeleteThreadPermanentlyAsync(committed.Id);
        await service.DeleteThreadPermanentlyAsync(dirty.Id);

        Assert.Equal(0, RunGitExitCode(_tempDir, "rev-parse", "--verify", "refs/heads/dotcraft/kept-on-delete"));
        Assert.Equal("unsaved" + Environment.NewLine, File.ReadAllText(Path.Combine(dirtyWorktree.Path, "README.md")));
        var listed = Assert.Single(await service.ListWorktreesAsync());
        Assert.Null(listed.ThreadId);
        Assert.True(listed.HasUncommittedChanges);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RemoveWorktreeAsync(dirtyWorktree.Path, discardChanges: false));

        await service.RemoveWorktreeAsync(dirtyWorktree.Path, discardChanges: true);

        Assert.False(Directory.Exists(dirtyWorktree.Path));
        Assert.NotEqual(0, RunGitExitCode(_tempDir, "rev-parse", "--verify", "refs/heads/dotcraft/remove-me"));
    }
}
