using System.Diagnostics;
using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.SourceControl;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class CommitMessageSuggestServiceTests : IDisposable
{
    private readonly string _workspacePath;
    private readonly string _craftPath;
    private readonly ThreadStore _threadStore;
    private readonly TestableSessionService _sessionService;

    public CommitMessageSuggestServiceTests()
    {
        _workspacePath = Path.Combine(Path.GetTempPath(), "commit-message-suggest-tests", Guid.NewGuid().ToString("N"));
        _craftPath = Path.Combine(_workspacePath, ".craft");
        Directory.CreateDirectory(_craftPath);
        RunGitSetup(_workspacePath, "init");

        _threadStore = new ThreadStore(_craftPath);
        _sessionService = new TestableSessionService(_threadStore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("git")]
    public async Task SuggestAsync_WithGitProvider_UsesNoIndexFallbackAndReturnsToolMessage(string provider)
    {
        var targetPath = Path.Combine(_workspacePath, "test-write-demo.txt");
        await File.WriteAllTextAsync(targetPath, "line one\nline two\n");

        var sourceThread = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "dotcraft-desktop",
            UserId = "local",
            WorkspacePath = _workspacePath
        });

        AddUserMessageTurn(sourceThread, "Please generate a commit message.");
        await _threadStore.SaveThreadAsync(sourceThread);

        _sessionService.SubmitInputHandler = (threadId, _, _) =>
        {
            return
            [
                new SessionEvent
                {
                    EventId = "evt_1",
                    EventType = SessionEventType.ItemCompleted,
                    ThreadId = threadId,
                    TurnId = "turn_001",
                    ItemId = "item_001",
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = new SessionItem
                    {
                        Id = "item_001",
                        TurnId = "turn_001",
                        Type = ItemType.ToolCall,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new ToolCallPayload
                        {
                            ToolName = CommitSuggestMethods.ToolName,
                            CallId = "call_001",
                            Arguments = new JsonObject
                            {
                                ["summary"] = "test: add demo file"
                            }
                        }
                    }
                }
            ];
        };

        var service = new CommitMessageSuggestService(
            _sessionService,
            _workspacePath,
            NullLogger<CommitMessageSuggestService>.Instance);

        var result = await service.SuggestAsync(new CommitMessageSuggestionRequest
        {
            ThreadId = sourceThread.Id,
            Paths = ["test-write-demo.txt"],
            Provider = provider
        });

        Assert.Equal("test: add demo file", result.Message);

        var submittedPrompt = string.Concat(_sessionService.LastSubmittedContent
            .OfType<TextContent>()
            .Select(c => c.Text));
        Assert.Contains("--- untracked: test-write-demo.txt ---", submittedPrompt, StringComparison.Ordinal);
        Assert.Contains("diff --git", submittedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(
            await _threadStore.LoadIndexAsync(),
            summary => string.Equals(summary.OriginChannel, CommitMessageSuggestConstants.ChannelName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuggestAsync_WithPerforceProvider_UsesPerforceOpenedAndDiffContext()
    {
        var fullPath = Path.Combine(_workspacePath, "src", "main.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "new line\n");

        var sourceThread = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "dotcraft-desktop",
            UserId = "local",
            WorkspacePath = _workspacePath
        });
        AddUserMessageTurn(sourceThread, "Prepare this work for Perforce.");
        await _threadStore.SaveThreadAsync(sourceThread);

        _sessionService.SubmitInputHandler = (threadId, _, _) =>
        {
            return
            [
                new SessionEvent
                {
                    EventId = "evt_1",
                    EventType = SessionEventType.ItemCompleted,
                    ThreadId = threadId,
                    TurnId = "turn_001",
                    ItemId = "item_001",
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = new SessionItem
                    {
                        Id = "item_001",
                        TurnId = "turn_001",
                        Type = ItemType.ToolCall,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new ToolCallPayload
                        {
                            ToolName = CommitSuggestMethods.ToolName,
                            CallId = "call_001",
                            Arguments = new JsonObject
                            {
                                ["summary"] = "Update Perforce workflow"
                            }
                        }
                    }
                }
            ];
        };

        var runner = new FakePerforceRunner((args, _) =>
        {
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", fullPath]))
                return P4Ok("//depot/src/main.cs#1 - edit change 777 (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "diff", "-du", fullPath]))
            {
                return P4Ok(
                    "==== //depot/src/main.cs#1 - src/main.cs ====\n" +
                    "@@ -1 +1 @@\n" +
                    "-old line\n" +
                    "+new line\n");
            }
            return P4Fail("unexpected " + string.Join(" ", args));
        });
        var service = CreatePerforceSuggestService(runner);

        var result = await service.SuggestAsync(new CommitMessageSuggestionRequest
        {
            ThreadId = sourceThread.Id,
            Paths = ["src/main.cs"],
            Provider = "perforce"
        });

        Assert.Equal("Update Perforce workflow", result.Message);
        var submittedPrompt = string.Concat(_sessionService.LastSubmittedContent
            .OfType<TextContent>()
            .Select(c => c.Text));
        Assert.Contains("Produce a Perforce pending changelist description", submittedPrompt, StringComparison.Ordinal);
        Assert.Contains("--- p4 opened ---", submittedPrompt, StringComparison.Ordinal);
        Assert.Contains("--- p4 diff -du ---", submittedPrompt, StringComparison.Ordinal);
        Assert.Contains("+new line", submittedPrompt, StringComparison.Ordinal);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task SuggestAsync_WithPerforceProvider_RejectsOfflineBinding()
    {
        var sourceThread = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "dotcraft-desktop",
            UserId = "local",
            WorkspacePath = _workspacePath
        });

        var service = new CommitMessageSuggestService(
            _sessionService,
            _workspacePath,
            NullLogger<CommitMessageSuggestService>.Instance,
            () => new SourceControlConfig
            {
                Provider = SourceControlProviders.Perforce,
                Perforce = new PerforceConnectionConfig { Online = false }
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SuggestAsync(new CommitMessageSuggestionRequest
        {
            ThreadId = sourceThread.Id,
            Paths = ["src/main.cs"],
            Provider = "perforce"
        }));
        Assert.Contains("offline", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_sessionService.LastSubmittedContent);
    }

    [Fact]
    public async Task SuggestAsync_WithPerforceProvider_RejectsEmptyDiff()
    {
        var fullPath = Path.Combine(_workspacePath, "src", "main.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "same\n");

        var sourceThread = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "dotcraft-desktop",
            UserId = "local",
            WorkspacePath = _workspacePath
        });
        await _threadStore.SaveThreadAsync(sourceThread);

        var runner = new FakePerforceRunner((args, _) =>
        {
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "opened", fullPath]))
                return P4Ok("//depot/src/main.cs#1 - edit change 777 (text)\n");
            if (args.SequenceEqual(["-p", "ssl:p4:1666", "-c", "client", "-u", "alice", "diff", "-du", fullPath]))
                return P4Ok("");
            return P4Fail("unexpected " + string.Join(" ", args));
        });
        var service = CreatePerforceSuggestService(runner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SuggestAsync(new CommitMessageSuggestionRequest
        {
            ThreadId = sourceThread.Id,
            Paths = ["src/main.cs"],
            Provider = "perforce"
        }));
        Assert.Contains("No Perforce diff", ex.Message, StringComparison.Ordinal);
        Assert.Empty(_sessionService.LastSubmittedContent);
    }

    private static void AddUserMessageTurn(SessionThread thread, string message)
    {
        var turnId = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1);
        thread.Turns.Add(new SessionTurn
        {
            Id = turnId,
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Items =
            [
                new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(1),
                    TurnId = turnId,
                    Type = ItemType.UserMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Payload = new UserMessagePayload { Text = message }
                }
            ]
        });
    }

    private static void RunGitSetup(string workingDirectory, params string[] args)
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

        var stderr = process.StandardError.ReadToEnd();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}");
    }

    private CommitMessageSuggestService CreatePerforceSuggestService(FakePerforceRunner runner) =>
        new(
            _sessionService,
            _workspacePath,
            NullLogger<CommitMessageSuggestService>.Instance,
            () => new SourceControlConfig
            {
                Provider = SourceControlProviders.Perforce,
                ConnectionMode = SourceControlConnectionModes.Manual,
                Perforce = new PerforceConnectionConfig
                {
                    Port = "ssl:p4:1666",
                    Client = "client",
                    User = "alice",
                    Online = true
                }
            },
            (_, _, _, _, _) => runner);

    private static PerforceCommandResult P4Ok(string stdout = "") => new(0, stdout, string.Empty, false, false);

    private static PerforceCommandResult P4Fail(string stderr) => new(1, string.Empty, stderr, false, false);

    private sealed class FakePerforceRunner(Func<IReadOnlyList<string>, string?, PerforceCommandResult> handler) : IPerforceCommandRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<PerforceCommandResult> RunAsync(IReadOnlyList<string> args, string? stdinInput, CancellationToken ct)
        {
            Calls.Add(args.ToArray());
            return Task.FromResult(handler(args, stdinInput));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
