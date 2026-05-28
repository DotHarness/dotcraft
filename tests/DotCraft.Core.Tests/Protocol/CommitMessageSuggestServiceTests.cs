using System.Diagnostics;
using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;

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

    [Fact]
    public async Task SuggestAsync_WithUntrackedPath_UsesNoIndexFallbackAndReturnsToolMessage()
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

        var result = await service.SuggestAsync(new WorkspaceCommitMessageSuggestParams
        {
            ThreadId = sourceThread.Id,
            Paths = ["test-write-demo.txt"]
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
