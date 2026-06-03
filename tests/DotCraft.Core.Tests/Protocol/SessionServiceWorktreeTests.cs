using System.Diagnostics;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceWorktreeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly SessionPersistenceService _persistence;

    public SessionServiceWorktreeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SSWorktree_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        RunGit("init");
        RunGit("config", "user.email", "test@example.com");
        RunGit("config", "user.name", "Test User");
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), ".craft/" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "initial" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "remove.txt"), "remove me" + Environment.NewLine);
        RunGit("add", ".gitignore", "README.md", "remove.txt");
        RunGit("commit", "-m", "init");

        _store = new ThreadStore(_tempDir);
        _persistence = new SessionPersistenceService(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task CreateWorktreeAndForkAsync_DefaultBranchUsesDotCraftPrefix()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity(), displayName: "Source Branch");

        var result = await service.CreateWorktreeAndForkAsync(new WorktreeCreateAndForkOptions
        {
            SourceThreadId = source.Id,
            CopyDirtyChanges = false
        });

        Assert.StartsWith("dotcraft/", result.Worktree.BranchName, StringComparison.Ordinal);
        Assert.Equal("Source Branch", result.Thread.DisplayName);
    }

    [Fact]
    public async Task CreateWorktreeAndForkAsync_BindsExecutionWorkspaceWithoutMovingThreadState()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity(), displayName: "Source");
        AddCompletedTurn(source, "turn_001", "first", "answer one");

        var result = await service.CreateWorktreeAndForkAsync(new WorktreeCreateAndForkOptions
        {
            SourceThreadId = source.Id,
            BranchName = "dotcraft/worktree-basic",
            DisplayName = "Worktree Branch",
            CopyDirtyChanges = false
        });

        var thread = result.Thread;
        Assert.Equal(_tempDir, thread.WorkspacePath);
        Assert.Equal(source.Id, thread.ForkedFromId);
        Assert.Equal(result.Worktree, thread.Worktree);
        Assert.Equal(result.Worktree.Path, thread.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(_tempDir, Path.GetFullPath(result.Worktree.WorkspacePath));
        Assert.True(File.Exists(Path.Combine(result.Worktree.Path, ".git")) || Directory.Exists(Path.Combine(result.Worktree.Path, ".git")));
        Assert.False(Directory.Exists(Path.Combine(result.Worktree.Path, ".craft", "threads")));

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(result.Worktree.Path, loaded!.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(result.Worktree.Path, loaded.Worktree?.Path);

        var summary = Assert.Single(await _store.LoadIndexAsync(), s => s.Id == thread.Id);
        Assert.Equal(result.Worktree.Path, summary.Worktree?.Path);

        var status = await service.GetWorktreeStatusAsync(thread.Id);
        Assert.True(status.Exists);
        Assert.True(status.IsGitWorktree);
        Assert.Equal("dotcraft/worktree-basic", status.BranchName);
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
    public async Task CreateWorktreeAndForkAsync_ToolContextUsesWorktreeButKeepsStateStores()
    {
        var recorder = new RecordingToolProvider();
        await using var agentFactory = CreateAgentFactory([recorder]);
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity(), displayName: "Source");

        var result = await service.CreateWorktreeAndForkAsync(new WorktreeCreateAndForkOptions
        {
            SourceThreadId = source.Id,
            BranchName = "dotcraft/worktree-context"
        });

        var seen = Assert.Single(recorder.Contexts, context => context.CurrentThreadId == result.Thread.Id);
        Assert.Equal(result.Worktree.Path, seen.WorkspacePath);
        Assert.Equal(_tempDir, seen.BotPath);
        Assert.StartsWith(Path.Combine(_tempDir, "memory"), seen.MemoryStore.MemoryDirectoryPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(_tempDir, "skills"), seen.SkillsLoader.WorkspaceSkillsPath);
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
        Assert.NotNull(loaded);
        Assert.Equal(result.Worktree.Path, loaded!.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(result.Worktree.Path, loaded.Worktree?.Path);
    }

    [Fact]
    public async Task HandoffThreadWorktreeAsync_MovesExistingThreadToWorktree()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var thread = await service.CreateThreadAsync(MakeIdentity(), displayName: "Handoff");

        var result = await service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
        {
            ThreadId = thread.Id,
            BranchName = "dotcraft/handoff-worktree",
            CopyDirtyChanges = false
        });

        Assert.Equal(thread.Id, result.Thread.Id);
        Assert.Equal(WorktreeHandoffModes.Worktree, result.Mode);
        Assert.NotNull(result.Worktree);
        Assert.Equal(result.Worktree!.Path, result.Thread.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(result.Worktree.Path, result.Thread.Worktree?.Path);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.Equal(result.Worktree.Path, loaded?.Configuration?.ExecutionWorkspaceOverride);
        Assert.Equal(result.Worktree.Path, loaded?.Worktree?.Path);
    }

    [Fact]
    public async Task HandoffThreadWorktreeAsync_MovesDirtyChangesBackToLocalAndRemovesWorktree()
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

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.Null(loaded?.Worktree);
        Assert.Null(loaded?.Configuration?.ExecutionWorkspaceOverride);
    }

    [Fact]
    public async Task HandoffThreadWorktreeAsync_RejectsCopyBackWhenLocalDirtyPathConflicts()
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
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "from local" + Environment.NewLine);

        var ex = await Assert.ThrowsAsync<WorktreeHandoffConflictException>(() =>
            service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
            {
                ThreadId = thread.Id,
                Mode = WorktreeHandoffModes.Local
            }));

        Assert.Contains("README.md", ex.ConflictPaths);
        Assert.True(Directory.Exists(worktreePath));
        Assert.Equal(worktreePath, (await _store.LoadThreadAsync(thread.Id))?.Worktree?.Path);
    }

    private SessionService CreateService(AgentFactory agentFactory)
    {
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        return new SessionService(agentFactory, defaultAgent, _persistence, new SessionGate());
    }

    private AgentFactory CreateAgentFactory(IReadOnlyList<IAgentToolProvider>? toolProviders = null)
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
            toolProviders: toolProviders ?? Array.Empty<IAgentToolProvider>());
    }

    private SessionIdentity MakeIdentity() =>
        new()
        {
            ChannelName = "test",
            UserId = "user",
            WorkspacePath = _tempDir
        };

    private static void AddCompletedTurn(SessionThread thread, string turnId, string userText, string agentText)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(thread.Turns.Count);
        var user = new SessionItem
        {
            Id = $"{turnId}_user",
            TurnId = turnId,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = userText }
        };
        var agent = new SessionItem
        {
            Id = $"{turnId}_agent",
            TurnId = turnId,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now.AddMilliseconds(1),
            CompletedAt = now.AddMilliseconds(1),
            Payload = new AgentMessagePayload { Text = agentText }
        };
        thread.Turns.Add(new SessionTurn
        {
            Id = turnId,
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            Input = user,
            Items = [user, agent],
            StartedAt = now,
            CompletedAt = now.AddMilliseconds(2)
        });
    }

    private void RunGit(params string[] args) => _ = RunGitCapture(_tempDir, args);

    private static string RunGitCapture(string workingDirectory, params string[] args)
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

    private sealed class RecordingToolProvider : IAgentToolProvider
    {
        public int Priority => 10;

        public List<ToolProviderContext> Contexts { get; } = [];

        public IEnumerable<AITool> CreateTools(ToolProviderContext context)
        {
            Contexts.Add(context);
            return [];
        }
    }
}
