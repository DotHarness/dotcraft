using DotCraft.SourceControl;

namespace DotCraft.Tests.SourceControl;

public sealed class PerforceChangelistManagerTests
{
    public static IEnumerable<object[]> ChangeReadFailures()
    {
        yield return [Fail("Perforce password (P4PASSWD) invalid or unset."), PerforceChangelistCodes.LoginRequired];
        yield return [TimedOut(), PerforceChangelistCodes.Timeout];
        yield return [MissingExecutable(), PerforceChangelistCodes.P4ExecutableNotFound];
        yield return [Fail("Connect to server failed; check $P4PORT."), PerforceChangelistCodes.P4CommandFailed];
        yield return [Fail("No such changelist 555."), PerforceChangelistCodes.ChangelistNotFound];
    }

    public static IEnumerable<object[]> OpenedFailures()
    {
        yield return [Fail("Perforce password (P4PASSWD) invalid or unset."), PerforceChangelistCodes.LoginRequired];
        yield return [TimedOut(), PerforceChangelistCodes.Timeout];
        yield return [MissingExecutable(), PerforceChangelistCodes.P4ExecutableNotFound];
    }

    [Fact]
    public async Task ListAsync_ReturnsDefaultAndPendingNumberedChanges()
    {
        var runner = new FakeRunner((args, _) =>
        {
            Assert.Equal(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "-ztag", "changes", "-s", "pending", "-u", "alice", "-c", "client"], args);
            return Ok("""
... change 123
... user alice
... client client
... status pending
... desc Fix gameplay loop
... change 456
... user alice
... client client
... status pending
... desc Polish UI
""");
        });

        var manager = CreateManager(runner);
        var list = await manager.ListAsync();

        Assert.Equal(["default", "123", "456"], list.Select(c => c.Id));
        Assert.True(list[0].IsDefault);
        Assert.Equal("Fix gameplay loop", list[1].Description);
    }

    [Fact]
    public async Task ListAsync_P4Config_ResolvesUserAndClientBeforeListingChanges()
    {
        var runner = new FakeRunner((args, _) =>
        {
            if (args.SequenceEqual(["info"]))
            {
                return Ok("""
User name: alice
Client name: game-main
""");
            }

            if (args.SequenceEqual(["-ztag", "changes", "-s", "pending", "-u", "alice", "-c", "game-main"]))
            {
                return Ok("""
... change 123
... user alice
... client game-main
... status pending
... desc Fix gameplay loop
""");
            }

            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(
            runner,
            connectionMode: SourceControlConnectionModes.P4Config,
            client: "",
            user: "");
        var list = await manager.ListAsync();

        Assert.Equal(["info"], runner.Calls[0]);
        Assert.Equal(["-ztag", "changes", "-s", "pending", "-u", "alice", "-c", "game-main"], runner.Calls[1]);
        Assert.Equal(["default", "123"], list.Select(c => c.Id));
        Assert.Equal("alice", list[0].User);
        Assert.Equal("game-main", list[0].Client);
    }

    [Fact]
    public async Task ListAsync_WhenInfoDoesNotResolveIdentity_DoesNotRunUnscopedChanges()
    {
        var runner = new FakeRunner((args, _) =>
        {
            if (args.SequenceEqual(["info"]))
                return Ok("Server address: ssl:p4:1666\n");
            return Fail("unexpected " + string.Join(" ", args));
        });
        var manager = CreateManager(
            runner,
            connectionMode: SourceControlConnectionModes.P4Config,
            client: "",
            user: "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ListAsync());

        Assert.Single(runner.Calls);
        Assert.DoesNotContain(runner.Calls, args => args.Contains("changes"));
    }

    [Fact]
    public async Task CreateAsync_CallsChangeInputAndParsesCreatedId()
    {
        var runner = new FakeRunner((args, stdin) =>
        {
            Assert.Equal(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-i"], args);
            Assert.Contains("Description:", stdin, StringComparison.Ordinal);
            Assert.Contains("\tReady for review", stdin, StringComparison.Ordinal);
            return Ok("Change 789 created.\n");
        });

        var manager = CreateManager(runner);
        var created = await manager.CreateAsync("Ready for review");

        Assert.Equal("789", created.Id);
        Assert.Equal("Ready for review", created.Description);
    }

    [Fact]
    public async Task PrepareAsync_DefaultTarget_CreatesNumberedChangeAndMovesDefaultFiles()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "src", "main.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "content");

        var runner = new FakeRunner((args, stdin) =>
        {
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-i"]))
                return Ok("Change 321 created.\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", file]))
                return Ok("//depot/src/main.cs#1 - edit default change (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "reopen", "-c", "321", file]))
                return Ok("//depot/src/main.cs#1 - reopened; change 321\n");
            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(runner, workspace.Path);
        var result = await manager.PrepareAsync(["src/main.cs"], "default", "Thread result");

        Assert.Equal("ok", result.Status);
        Assert.True(result.Created);
        Assert.Equal("321", result.Changelist);
        Assert.Equal([file], result.MovedPaths);
        var reopenCall = Assert.Single(runner.Calls, args => args.Contains("reopen"));
        Assert.DoesNotContain("--", reopenCall);
    }

    [Fact]
    public async Task PrepareAsync_DefaultTarget_AllFilesInOtherChangelists_CreatesNumberedChangeAndMovesFiles()
    {
        using var workspace = new TempWorkspace();
        var keepA = Path.Combine(workspace.Path, "keep-a.cs");
        var keepB = Path.Combine(workspace.Path, "keep-b.cs");
        await File.WriteAllTextAsync(keepA, "");
        await File.WriteAllTextAsync(keepB, "");

        var runner = new FakeRunner((args, _) =>
        {
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", keepA]))
                return Ok("//depot/keep-a.cs#1 - edit change 777 (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", keepB]))
                return Ok("//depot/keep-b.cs#1 - edit change 888 (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-i"]))
                return Ok("Change 321 created.\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "reopen", "-c", "321", keepA, keepB]))
                return Ok("//depot/keep-a.cs#1 - reopened; change 321\n//depot/keep-b.cs#1 - reopened; change 321\n");
            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(runner, workspace.Path);
        var result = await manager.PrepareAsync([keepA, keepB], "default", "Thread result");

        Assert.Equal("ok", result.Status);
        Assert.True(result.Created);
        Assert.Equal("321", result.Changelist);
        Assert.Equal([keepA, keepB], result.MovedPaths);
        Assert.Empty(result.SkippedPaths);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [MemberData(nameof(OpenedFailures))]
    public async Task PrepareAsync_DefaultTarget_OpenedFailure_ReturnsStableErrorWithoutCreatingChange(
        PerforceCommandResult openedFailure,
        string expectedCode)
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "src", "main.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "content");

        var runner = new FakeRunner((args, _) =>
        {
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", file]))
                return openedFailure;
            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(runner, workspace.Path);
        var result = await manager.PrepareAsync(["src/main.cs"], "default", "Thread result");

        Assert.Equal("error", result.Status);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal("default", result.Changelist);
        Assert.False(result.Created);
        Assert.Empty(result.MovedPaths);
        Assert.DoesNotContain(runner.Calls, args => args.Contains("change"));
    }

    [Fact]
    public async Task PrepareAsync_NumberedTarget_MovesFilesAlreadyOpenedInOtherChangelist()
    {
        using var workspace = new TempWorkspace();
        var keep = Path.Combine(workspace.Path, "keep.cs");
        var move = Path.Combine(workspace.Path, "move.cs");
        await File.WriteAllTextAsync(keep, "");
        await File.WriteAllTextAsync(move, "");

        var runner = new FakeRunner((args, stdin) =>
        {
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-o", "555"]))
                return Ok("Change:\t555\n\nDescription:\n\tOld\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", keep]))
                return Ok("//depot/keep.cs#1 - edit change 777 (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", move]))
                return Ok("//depot/move.cs#1 - edit default change (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "reopen", "-c", "555", keep, move]))
                return Ok("");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-i"]))
                return Ok("Change 555 updated.\n");
            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(runner, workspace.Path);
        var result = await manager.PrepareAsync([keep, move], "555", "Updated");

        Assert.Equal("ok", result.Status);
        Assert.Equal([keep, move], result.MovedPaths);
        Assert.Empty(result.SkippedPaths);
        Assert.Empty(result.Warnings);
        Assert.Equal("change", runner.Calls[0][6]);
        Assert.Equal("opened", runner.Calls[1][6]);
        Assert.Equal("opened", runner.Calls[2][6]);
        Assert.Equal("reopen", runner.Calls[3][6]);
        Assert.Equal("change", runner.Calls[4][6]);
        Assert.Equal("change", runner.Calls[5][6]);
        Assert.DoesNotContain("--", runner.Calls[3]);
    }

    [Fact]
    public async Task PrepareAsync_NumberedTarget_ReopenFailure_DoesNotUpdateDescription()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "main.cs");
        await File.WriteAllTextAsync(file, "");

        var runner = new FakeRunner((args, _) =>
        {
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-o", "555"]))
                return Ok("Change:\t555\n\nDescription:\n\tOld\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", file]))
                return Ok("//depot/main.cs#1 - edit default change (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "reopen", "-c", "555", file]))
                return Fail("Usage: reopen [-c changelist#] [-t filetype | -Si] file...\n");
            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(runner, workspace.Path);
        var result = await manager.PrepareAsync([file], "555", "Updated");

        Assert.Equal("error", result.Status);
        Assert.Equal(PerforceChangelistCodes.P4CommandFailed, result.Code);
        Assert.Empty(result.MovedPaths);
        Assert.DoesNotContain(runner.Calls, args => args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-i"]));
        Assert.Single(runner.Calls, args => args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-o", "555"]));
    }

    [Fact]
    public async Task PrepareAsync_NumberedTarget_DescriptionUpdateFailure_ReturnsMovedPaths()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "main.cs");
        await File.WriteAllTextAsync(file, "");

        var runner = new FakeRunner((args, _) =>
        {
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-o", "555"]))
                return Ok("Change:\t555\n\nDescription:\n\tOld\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", file]))
                return Ok("//depot/main.cs#1 - edit default change (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "reopen", "-c", "555", file]))
                return Ok("//depot/main.cs#1 - reopened; change 555\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-i"]))
                return Fail("Change 555 not updated.");
            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(runner, workspace.Path);
        var result = await manager.PrepareAsync([file], "555", "Updated");

        Assert.Equal("error", result.Status);
        Assert.Equal(PerforceChangelistCodes.P4CommandFailed, result.Code);
        Assert.Equal("555", result.Changelist);
        Assert.Equal([file], result.MovedPaths);
    }

    [Theory]
    [MemberData(nameof(ChangeReadFailures))]
    public async Task PrepareAsync_NumberedTarget_ChangeReadFailure_ReturnsStableError(
        PerforceCommandResult changeReadFailure,
        string expectedCode)
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Path, "main.cs");
        await File.WriteAllTextAsync(file, "");
        var runner = new FakeRunner((args, _) =>
        {
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-o", "555"]))
                return changeReadFailure;
            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(runner, workspace.Path);
        var result = await manager.PrepareAsync([file], "555", "Updated");

        Assert.Equal("error", result.Status);
        Assert.Equal(expectedCode, result.Code);
        Assert.Equal("555", result.Changelist);
        Assert.Empty(result.MovedPaths);
        Assert.DoesNotContain(runner.Calls, args => args.Contains("opened"));
        Assert.DoesNotContain(runner.Calls, args => args.Contains("reopen"));
    }

    private static PerforceChangelistManager CreateManager(
        FakeRunner runner,
        string? workspacePath = null,
        string connectionMode = SourceControlConnectionModes.Manual,
        string client = "client",
        string user = "alice") =>
        new(runner, new PerforceWorkspaceCommandOptions
        {
            WorkspacePath = workspacePath ?? "C:\\workspace",
            ConnectionMode = connectionMode,
            Port = "ssl:p4:1666",
            Client = client,
            User = user
        });

    private static PerforceCommandResult Ok(string stdout = "") => new(0, stdout, string.Empty, false, false);

    private static PerforceCommandResult Fail(string stderr) => new(1, string.Empty, stderr, false, false);

    private static PerforceCommandResult MissingExecutable() => new(-1, string.Empty, "p4 not found", true, false);

    private static PerforceCommandResult TimedOut() => new(-1, string.Empty, string.Empty, false, true);

    private sealed class FakeRunner(Func<IReadOnlyList<string>, string?, PerforceCommandResult> handler) : IPerforceCommandRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<PerforceCommandResult> RunAsync(IReadOnlyList<string> args, string? stdinInput, CancellationToken ct)
        {
            Calls.Add(args.ToArray());
            return Task.FromResult(handler(args, stdinInput));
        }
    }

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "p4-cl-tests", Guid.NewGuid().ToString("N"));

        public TempWorkspace() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
