using System.Text.Json;
using DotCraft.Sessions;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceWorktreeEnvironmentTests : SessionServiceWorktreeTestBase
{
    private static readonly bool IsWindows = OperatingSystem.IsWindows();

    private static string HostPlatform =>
        IsWindows ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : "linux";

    [Fact]
    public async Task CreateWorktreeAndStartAsync_RunsSetupInTheWorktreeWithBothVariables()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var environmentPath = WriteEnvironment(new
        {
            version = 1,
            setup = new
            {
                script = IsWindows
                    ? "Set-Content -NoNewline -Path vars.txt -Value \"$env:DOTCRAFT_SOURCE_TREE_PATH|$env:DOTCRAFT_WORKTREE_PATH|$((Get-Location).Path)\""
                    : "printf '%s|%s|%s' \"$DOTCRAFT_SOURCE_TREE_PATH\" \"$DOTCRAFT_WORKTREE_PATH\" \"$PWD\" > vars.txt"
            }
        });
        var steps = new List<string>();

        var result = await service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
        {
            Identity = MakeIdentity(),
            BranchName = "dotcraft/env-setup",
            CopyDirtyChanges = false,
            EnvironmentPath = environmentPath,
            Progress = (step, status) => steps.Add($"{step}:{status}")
        });

        Assert.Equal(WorktreeSetupStatuses.Succeeded, result.Setup?.Status);
        Assert.Equal(0, result.Setup?.ExitCode);
        Assert.Equal(
            [
                "createWorktree:started",
                "createWorktree:completed",
                "copyFiles:started",
                "copyFiles:completed",
                "setupEnvironment:started",
                "setupEnvironment:completed"
            ],
            steps);
        var vars = File.ReadAllText(Path.Combine(result.Worktree.Path, "vars.txt")).Split('|');
        AssertSamePath(_tempDir, vars[0]);
        AssertSamePath(result.Worktree.Path, vars[1]);
        AssertSamePath(result.Worktree.Path, vars[2]);
        Assert.Equal(environmentPath, result.Worktree.EnvironmentPath);
        Assert.Equal(environmentPath, (await _store.LoadThreadAsync(result.Thread.Id))?.Worktree?.EnvironmentPath);
    }

    [Fact]
    public async Task CreateWorktreeAndForkAsync_RunsTheHostPlatformOverride()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var source = await service.CreateThreadAsync(MakeIdentity());
        var environmentPath = WriteEnvironment(new Dictionary<string, object>
        {
            ["setup"] = new Dictionary<string, object>
            {
                ["script"] = WriteMarker("default"),
                ["win32"] = new { script = WriteMarker("win32") },
                ["darwin"] = new { script = WriteMarker("darwin") },
                ["linux"] = new { script = WriteMarker("linux") }
            }
        });

        var result = await service.CreateWorktreeAndForkAsync(new WorktreeCreateAndForkOptions
        {
            SourceThreadId = source.Id,
            BranchName = "dotcraft/env-override",
            CopyDirtyChanges = false,
            EnvironmentPath = environmentPath
        });

        Assert.Equal(WorktreeSetupStatuses.Succeeded, result.Setup?.Status);
        Assert.Equal(HostPlatform, File.ReadAllText(Path.Combine(result.Worktree.Path, "marker.txt")).Trim());
    }

    [Fact]
    public async Task CreateWorktreeAndStartAsync_KeepsTheThreadAndWorktreeWhenSetupFails()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var environmentPath = WriteEnvironment(new
        {
            setup = new { script = IsWindows ? "Write-Output 'setup-started'\nexit 7" : "echo setup-started\nexit 7" }
        });

        var result = await service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
        {
            Identity = MakeIdentity(),
            BranchName = "dotcraft/env-fails",
            CopyDirtyChanges = false,
            EnvironmentPath = environmentPath
        });

        Assert.Equal(WorktreeSetupStatuses.Failed, result.Setup?.Status);
        Assert.Equal(7, result.Setup?.ExitCode);
        Assert.Contains("setup-started", result.Setup?.OutputTail, StringComparison.Ordinal);
        Assert.True(Directory.Exists(result.Worktree.Path));
        Assert.Equal(result.Worktree.Path, (await _store.LoadThreadAsync(result.Thread.Id))?.Worktree?.Path);
    }

    [Fact]
    public async Task CreateWorktreeAndStartAsync_ReportsSetupErrorsAsPlainText()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var environmentPath = WriteEnvironment(new
        {
            setup = new { script = IsWindows ? "Write-Error 'setup-broke'" : "echo setup-broke >&2\nexit 1" }
        });

        var result = await service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
        {
            Identity = MakeIdentity(),
            BranchName = "dotcraft/env-error-text",
            CopyDirtyChanges = false,
            EnvironmentPath = environmentPath
        });

        Assert.Equal(WorktreeSetupStatuses.Failed, result.Setup?.Status);
        Assert.Contains("setup-broke", result.Setup?.OutputTail, StringComparison.Ordinal);
        Assert.DoesNotContain("CLIXML", result.Setup?.OutputTail, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b", result.Setup?.OutputTail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWorktreeAndStartAsync_RejectsAnInvalidEnvironmentBeforeCreatingAnything()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var outside = Path.Combine(_tempDir, "environment.json");
        File.WriteAllText(outside, "{}");
        var wrongVersion = WriteEnvironment(new { version = 2 });

        foreach (var environmentPath in new[] { outside, wrongVersion, "relative/environment.json" })
        {
            var ex = await Assert.ThrowsAsync<WorktreeEnvironmentException>(() =>
                service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
                {
                    Identity = MakeIdentity(),
                    BranchName = "dotcraft/env-invalid",
                    CopyDirtyChanges = false,
                    EnvironmentPath = environmentPath
                }));
            Assert.Equal(WorktreeEnvironmentErrorCodes.EnvironmentInvalid, ex.Code);
        }

        AssertNothingCreated("dotcraft/env-invalid");
    }

    [Fact]
    public async Task CancelWorktreeOperation_StopsSetupAndRollsTheCreationBack()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var environmentPath = WriteEnvironment(new
        {
            setup = new { script = IsWindows ? "Start-Sleep -Seconds 120" : "sleep 120" }
        });
        var setupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var creation = service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
        {
            Identity = MakeIdentity(),
            BranchName = "dotcraft/env-cancel",
            CopyDirtyChanges = false,
            EnvironmentPath = environmentPath,
            OperationId = "op-cancel",
            Progress = (step, status) =>
            {
                if (step == WorktreeCreateSteps.SetupEnvironment && status == WorktreeHandoffStepStatuses.Started)
                    setupStarted.TrySetResult();
            }
        });
        await setupStarted.Task.WaitAsync(TimeSpan.FromSeconds(60));
        service.CancelWorktreeOperation("op-cancel");

        var ex = await Assert.ThrowsAsync<WorktreeEnvironmentException>(() => creation.WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.Equal(WorktreeEnvironmentErrorCodes.CreationCancelled, ex.Code);
        AssertNothingCreated("dotcraft/env-cancel");
        Assert.Empty(await _store.LoadIndexAsync());
    }

    [Fact]
    public async Task CreateWorktreeAndStartAsync_FailsSetupWhenTheEnvironmentRootIsMissingFromTheWorktree()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var project = Path.Combine(_tempDir, "app");
        var environmentPath = WriteEnvironment(new { setup = new { script = WriteMarker("ran") } }, project);

        var result = await service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
        {
            Identity = MakeIdentity() with { WorkspacePath = project },
            BranchName = "dotcraft/env-missing-root",
            CopyDirtyChanges = false,
            EnvironmentPath = environmentPath
        });

        Assert.Equal(WorktreeSetupStatuses.Failed, result.Setup?.Status);
        Assert.False(File.Exists(Path.Combine(result.Worktree.Path, "marker.txt")));
    }

    [Fact]
    public async Task CancelWorktreeOperation_WhileTheThreadIsCreatedRollsTheCreationBack()
    {
        var blocker = new BlockingWorktreeToolSource();
        await using var agentFactory = CreateAgentFactory([blocker]);
        var service = CreateService(agentFactory);

        var creation = service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
        {
            Identity = MakeIdentity(),
            BranchName = "dotcraft/env-cancel-thread",
            CopyDirtyChanges = false,
            OperationId = "op-cancel-thread"
        });
        await blocker.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));
        service.CancelWorktreeOperation("op-cancel-thread");

        var ex = await Assert.ThrowsAsync<WorktreeEnvironmentException>(() => creation.WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.Equal(WorktreeEnvironmentErrorCodes.CreationCancelled, ex.Code);
        AssertNothingCreated("dotcraft/env-cancel-thread");
        Assert.Empty(await _store.LoadIndexAsync());
    }

    [Fact]
    public async Task RemoveWorktreeAsync_RunsCleanupForAWorktreeKeptAfterItsThreadWasDeleted()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var marker = Path.Combine(_tempDir, ".craft", "cleanup-ran.txt");
        var environmentPath = WriteEnvironment(new
        {
            cleanup = new
            {
                script = IsWindows
                    ? $"Set-Content -NoNewline -Path '{marker}' -Value 'ran'"
                    : $"printf ran > '{marker}'"
            }
        });
        var result = await CreateWithEnvironmentAsync(service, "dotcraft/env-orphan", environmentPath);
        File.WriteAllText(Path.Combine(result.Worktree.Path, "unsaved.txt"), "keeps the worktree on delete");
        await service.DeleteThreadPermanentlyAsync(result.Thread.Id);
        Assert.True(Directory.Exists(result.Worktree.Path));
        Assert.False(File.Exists(marker));

        await service.RemoveWorktreeAsync(result.Worktree.Path, discardChanges: true);

        Assert.True(File.Exists(marker));
        Assert.False(Directory.Exists(result.Worktree.Path));
    }

    [Fact]
    public async Task RunWorktreeSetupAsync_RunsTheRecordedEnvironmentAsItIsOnDisk()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var environmentPath = WriteEnvironment(new { setup = new { script = WriteMarker("first") } });
        var result = await service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
        {
            Identity = MakeIdentity(),
            BranchName = "dotcraft/env-rerun",
            CopyDirtyChanges = false,
            EnvironmentPath = environmentPath
        });
        WriteEnvironment(new { setup = new { script = WriteMarker("second") } });

        var output = new List<string>();
        var rerun = await service.RunWorktreeSetupAsync(new WorktreeRunSetupOptions
        {
            ThreadId = result.Thread.Id,
            Output = output.Add
        });

        Assert.Equal(WorktreeSetupStatuses.Succeeded, rerun.Status);
        Assert.Equal("second", File.ReadAllText(Path.Combine(result.Worktree.Path, "marker.txt")).Trim());

        await service.SetWorktreeEnvironmentAsync(new WorktreeSetEnvironmentOptions { Path = result.Worktree.Path });
        Assert.Null((await _store.LoadThreadAsync(result.Thread.Id))?.Worktree?.EnvironmentPath);
        var ex = await Assert.ThrowsAsync<WorktreeEnvironmentException>(() =>
            service.RunWorktreeSetupAsync(new WorktreeRunSetupOptions { ThreadId = result.Thread.Id }));
        Assert.Equal(WorktreeEnvironmentErrorCodes.EnvironmentInvalid, ex.Code);
    }

    [Fact]
    public async Task RemoveWorktreeAsync_RunsCleanupInTheWorktreeBeforeRemovingIt()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var marker = Path.Combine(_tempDir, ".craft", "cleanup-ran.txt");
        var environmentPath = WriteEnvironment(new
        {
            cleanup = new
            {
                script = IsWindows
                    ? $"Set-Content -NoNewline -Path '{marker}' -Value (Get-Location).Path"
                    : $"printf '%s' \"$PWD\" > '{marker}'"
            }
        });
        var result = await CreateWithEnvironmentAsync(service, "dotcraft/env-cleanup", environmentPath);
        File.WriteAllText(Path.Combine(result.Worktree.Path, "unsaved.txt"), "keeps the worktree on archive");
        await service.ArchiveThreadAsync(result.Thread.Id);
        Assert.True(Directory.Exists(result.Worktree.Path));
        Assert.False(File.Exists(marker));

        await service.RemoveWorktreeAsync(result.Worktree.Path, discardChanges: true);

        AssertSamePath(result.Worktree.Path, File.ReadAllText(marker));
        Assert.False(Directory.Exists(result.Worktree.Path));
    }

    [Fact]
    public async Task FailingCleanup_KeepsTheWorktreeOnArchiveAndBlocksRemovalUnlessSkipped()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var environmentPath = WriteEnvironment(new
        {
            cleanup = new { script = IsWindows ? "Write-Output 'cleanup-broke'\nexit 5" : "echo cleanup-broke\nexit 5" }
        });
        var result = await CreateWithEnvironmentAsync(service, "dotcraft/env-cleanup-fails", environmentPath);

        await service.ArchiveThreadAsync(result.Thread.Id);

        Assert.Equal(ThreadStatus.Archived, (await _store.LoadThreadAsync(result.Thread.Id))?.Status);
        Assert.True(Directory.Exists(result.Worktree.Path));

        var ex = await Assert.ThrowsAsync<WorktreeEnvironmentException>(() =>
            service.RemoveWorktreeAsync(result.Worktree.Path, discardChanges: true));
        Assert.Equal(WorktreeEnvironmentErrorCodes.CleanupFailed, ex.Code);
        Assert.Equal(5, ex.ExitCode);
        Assert.Contains("cleanup-broke", ex.OutputTail, StringComparison.Ordinal);
        Assert.Equal(result.Worktree.Path, ex.WorktreePath);
        Assert.True(Directory.Exists(result.Worktree.Path));

        await service.RemoveWorktreeAsync(result.Worktree.Path, discardChanges: true, skipCleanup: true);

        Assert.False(Directory.Exists(result.Worktree.Path));
    }

    [Fact]
    public async Task HandoffToLocal_WithFailingCleanupChangesNothing()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var environmentPath = WriteEnvironment(new { cleanup = new { script = "exit 3" } });
        var result = await CreateWithEnvironmentAsync(service, "dotcraft/env-handoff", environmentPath);
        File.WriteAllText(Path.Combine(result.Worktree.Path, "README.md"), "worktree edit" + Environment.NewLine);
        var localBranch = RunGitCapture(_tempDir, "branch", "--show-current").Trim();
        var steps = new List<string>();

        var ex = await Assert.ThrowsAsync<WorktreeEnvironmentException>(() =>
            service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
            {
                ThreadId = result.Thread.Id,
                Mode = WorktreeHandoffModes.Local,
                Progress = (step, status) => steps.Add($"{step}:{status}")
            }));

        Assert.Equal(WorktreeEnvironmentErrorCodes.CleanupFailed, ex.Code);
        Assert.Equal(["cleanupEnvironment:started", "cleanupEnvironment:failed"], steps);
        Assert.Equal(localBranch, RunGitCapture(_tempDir, "branch", "--show-current").Trim());
        Assert.Equal("initial" + Environment.NewLine, File.ReadAllText(Path.Combine(_tempDir, "README.md")));
        Assert.Equal("worktree edit" + Environment.NewLine, File.ReadAllText(Path.Combine(result.Worktree.Path, "README.md")));
        Assert.Equal(result.Worktree.Path, (await _store.LoadThreadAsync(result.Thread.Id))?.Worktree?.Path);
    }

    [Fact]
    public async Task HandoffToLocal_WhileTheWorktreeFolderIsInUseChangesNothing()
    {
        if (!IsWindows)
            return;

        await using var agentFactory = CreateAgentFactory();
        var service = CreateService(agentFactory);
        var environmentPath = WriteEnvironment(new { cleanup = new { script = "exit 0" } });
        var result = await CreateWithEnvironmentAsync(service, "dotcraft/env-in-use", environmentPath);
        File.WriteAllText(Path.Combine(result.Worktree.Path, "README.md"), "worktree edit" + Environment.NewLine);
        var localBranch = RunGitCapture(_tempDir, "branch", "--show-current").Trim();

        WorktreeInUseException ex;
        using (File.Open(Path.Combine(result.Worktree.Path, "README.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ex = await Assert.ThrowsAsync<WorktreeInUseException>(() =>
                service.HandoffThreadWorktreeAsync(new WorktreeHandoffOptions
                {
                    ThreadId = result.Thread.Id,
                    Mode = WorktreeHandoffModes.Local
                }));
        }

        Assert.Equal(result.Worktree.Path, ex.Path);
        Assert.Equal(localBranch, RunGitCapture(_tempDir, "branch", "--show-current").Trim());
        Assert.Equal("initial" + Environment.NewLine, File.ReadAllText(Path.Combine(_tempDir, "README.md")));
        Assert.Equal("worktree edit" + Environment.NewLine, File.ReadAllText(Path.Combine(result.Worktree.Path, "README.md")));
        Assert.Contains(result.Worktree.Path.Replace('\\', '/'), RunGitCapture(_tempDir, "worktree", "list"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.Worktree.Path, (await _store.LoadThreadAsync(result.Thread.Id))?.Worktree?.Path);
    }

    private async Task<WorktreeCreateAndStartResult> CreateWithEnvironmentAsync(
        SessionService service,
        string branchName,
        string environmentPath) =>
        await service.CreateWorktreeAndStartAsync(new WorktreeCreateAndStartOptions
        {
            Identity = MakeIdentity(),
            BranchName = branchName,
            CopyDirtyChanges = false,
            EnvironmentPath = environmentPath
        });

    private string WriteEnvironment(object document, string? project = null)
    {
        var directory = Path.Combine(project ?? _tempDir, ".craft", "environments");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "environment.json");
        File.WriteAllText(path, JsonSerializer.Serialize(document));
        return path;
    }

    private static string WriteMarker(string value) => IsWindows
        ? $"Set-Content -NoNewline -Path marker.txt -Value '{value}'"
        : $"printf '{value}' > marker.txt";

    private void AssertNothingCreated(string branchName)
    {
        var worktrees = Path.Combine(_tempDir, ".craft", "worktrees");
        Assert.False(Directory.Exists(worktrees) && Directory.EnumerateFileSystemEntries(worktrees).Any());
        Assert.NotEqual(0, RunGitExitCode(_tempDir, "rev-parse", "--verify", $"refs/heads/{branchName}"));
    }

    private static void AssertSamePath(string expected, string actual) =>
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual.Trim())),
            ignoreCase: IsWindows);

    private sealed class BlockingWorktreeToolSource : IToolSource
    {
        public string SourceId => "blocking-worktree";

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.WorkspacePath.Contains("worktrees", StringComparison.OrdinalIgnoreCase))
            {
                Reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return [];
        }
    }
}
