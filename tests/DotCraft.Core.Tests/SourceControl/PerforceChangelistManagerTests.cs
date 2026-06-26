using DotCraft.SourceControl;

namespace DotCraft.Tests.SourceControl;

public sealed class PerforceChangelistManagerTests
{
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
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "reopen", "-c", "321", "--", file]))
                return Ok("//depot/src/main.cs#1 - reopened; change 321\n");
            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(runner, workspace.Path);
        var result = await manager.PrepareAsync(["src/main.cs"], "default", "Thread result");

        Assert.Equal("ok", result.Status);
        Assert.True(result.Created);
        Assert.Equal("321", result.Changelist);
        Assert.Equal([file], result.MovedPaths);
    }

    [Fact]
    public async Task PrepareAsync_NumberedTarget_SkipsFilesAlreadyOpenedInOtherChangelist()
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
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "change", "-i"]))
                return Ok("Change 555 updated.\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", keep]))
                return Ok("//depot/keep.cs#1 - edit change 777 (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", move]))
                return Ok("//depot/move.cs#1 - edit default change (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "reopen", "-c", "555", "--", move]))
                return Ok("");
            return Fail("unexpected " + string.Join(" ", args));
        });

        var manager = CreateManager(runner, workspace.Path);
        var result = await manager.PrepareAsync([keep, move], "555", "Updated");

        Assert.Equal("ok", result.Status);
        Assert.Equal([move], result.MovedPaths);
        Assert.Equal([keep], result.SkippedPaths);
        Assert.Contains(result.Warnings, w => w.Code == PerforceChangelistCodes.FileAlreadyInOtherChangelist);
    }

    private static PerforceChangelistManager CreateManager(FakeRunner runner, string? workspacePath = null) =>
        new(runner, new PerforceWorkspaceCommandOptions
        {
            WorkspacePath = workspacePath ?? "C:\\workspace",
            ConnectionMode = SourceControlConnectionModes.Manual,
            Port = "ssl:p4:1666",
            Client = "client",
            User = "alice"
        });

    private static PerforceCommandResult Ok(string stdout = "") => new(0, stdout, string.Empty, false, false);

    private static PerforceCommandResult Fail(string stderr) => new(1, string.Empty, stderr, false, false);

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
