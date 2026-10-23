using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Contributions;
using DotCraft.Memory.Extraction;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Memory.Extraction;

public sealed class MemoryExtractionSchedulerTests : IDisposable
{
    private readonly ConcurrentDictionary<string, SessionThread> _threads = new(StringComparer.Ordinal);
    private readonly FakeRunner _runner = new();
    private readonly MemoryExtractionScheduler _scheduler;

    public MemoryExtractionSchedulerTests()
    {
        _scheduler = new MemoryExtractionScheduler(
            (threadId, _) => Task.FromResult(_threads[threadId]),
            (thread, _) => Enumerable.Reverse(thread.Turns).ToArray().ToAsyncEnumerable(),
            _runner);
    }

    public void Dispose() => _scheduler.Dispose();

    [Fact]
    public async Task RunsOnTheSeventhEligibleTurn_OverTheTurnsSinceFirstObservation()
    {
        var thread = AddThread();
        AddTurn(thread);
        AddTurn(thread);

        for (var i = 0; i < MemoryExtractionScheduler.Cadence - 1; i++)
            await CompleteTurnAsync(thread);
        Assert.Empty(_runner.Calls);

        await CompleteTurnAsync(thread);

        var call = Assert.Single(_runner.Calls);
        Assert.Equal((thread.Id, "turn_002"), call);
    }

    [Fact]
    public async Task TurnsWithASuccessfulMemoryUpdate_AreSkippedAndAdvanceTheCursor()
    {
        var thread = AddThread();
        for (var i = 0; i < 3; i++)
            await CompleteTurnAsync(thread);
        await CompleteTurnAsync(thread, MemoryWrite.Succeeded);
        for (var i = 0; i < MemoryExtractionScheduler.Cadence - 4; i++)
            await CompleteTurnAsync(thread);
        Assert.Empty(_runner.Calls);

        await CompleteTurnAsync(thread);

        Assert.Equal([(thread.Id, "turn_004")], _runner.Calls);
    }

    [Fact]
    public async Task FailedMemoryWrite_DoesNotCountAsAnUpdate()
    {
        var thread = AddThread();
        await CompleteTurnAsync(thread, MemoryWrite.Failed);
        for (var i = 0; i < MemoryExtractionScheduler.Cadence - 1; i++)
            await CompleteTurnAsync(thread);

        Assert.Equal([(thread.Id, (string?)null)], _runner.Calls);
    }

    public static TheoryData<string> IneligibleThreads => new()
    {
        "subagent",
        "ephemeral",
        "internal",
        "memoryDisabled",
        "extractionDisabled",
        "extractionNotCaptured"
    };

    [Theory]
    [MemberData(nameof(IneligibleThreads))]
    public async Task IneligibleThreads_NeverRunExtraction(string kind)
    {
        var thread = AddThread(kind);
        for (var i = 0; i < MemoryExtractionScheduler.Cadence * 2; i++)
            await CompleteTurnAsync(thread);

        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task UnsuccessfulTurns_AreNotCounted()
    {
        var thread = AddThread();
        for (var i = 0; i < MemoryExtractionScheduler.Cadence - 1; i++)
            await CompleteTurnAsync(thread);
        foreach (var status in new[] { TurnStatus.Failed, TurnStatus.Cancelled })
        {
            var turn = AddTurn(thread, status: status);
            await EndTurnAsync(thread, turn);
        }
        Assert.Empty(_runner.Calls);

        await CompleteTurnAsync(thread);

        Assert.Single(_runner.Calls);
    }

    [Fact]
    public async Task RequestsDuringARun_CollapseIntoOneTrailingRunAfterTheAdvancedCursor()
    {
        var thread = AddThread();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runner.Behavior = async (call, _, _) =>
        {
            if (call == 0)
                await release.Task;
            return thread.Turns[call == 0 ? 6 : ^1].Id;
        };

        for (var i = 0; i < MemoryExtractionScheduler.Cadence - 1; i++)
            await CompleteTurnAsync(thread);
        await PostCompletedTurnsAsync(thread, 1);
        await WaitForCallsAsync(1);
        await PostCompletedTurnsAsync(thread, MemoryExtractionScheduler.Cadence * 2);
        Assert.Single(_runner.Calls);

        release.SetResult();
        await _scheduler.WhenIdleAsync();

        Assert.Equal([(thread.Id, (string?)null), (thread.Id, "turn_007")], _runner.Calls);
    }

    [Fact]
    public async Task FailedRun_KeepsTheCursorForTheNextRun()
    {
        var thread = AddThread();
        _runner.Behavior = (call, _, _) => call switch
        {
            0 => Task.FromResult<string?>(null),
            1 => throw new InvalidOperationException("provider down"),
            _ => Task.FromResult<string?>(thread.Turns[^1].Id)
        };

        for (var i = 0; i < MemoryExtractionScheduler.Cadence * 4; i++)
            await CompleteTurnAsync(thread);

        Assert.Equal(
            [(thread.Id, (string?)null), (thread.Id, null), (thread.Id, null), (thread.Id, "turn_021")],
            _runner.Calls);
    }

    private SessionThread AddThread(string kind = "eligible")
    {
        var thread = new SessionThread
        {
            Id = $"thread_{Guid.NewGuid():N}",
            WorkspacePath = "workspace",
            OriginChannel = "test",
            Source = kind == "subagent"
                ? new ThreadSource { Kind = ThreadSourceKinds.SubAgent }
                : ThreadSource.User(),
            Ephemeral = kind == "ephemeral",
            Configuration = new ThreadConfiguration
            {
                MemoryEnabled = kind != "memoryDisabled",
                MemoryAutoExtract = kind switch
                {
                    "extractionDisabled" => false,
                    "extractionNotCaptured" => null,
                    _ => true
                }
            }
        };
        if (kind == "internal")
            thread.Metadata[ThreadVisibility.InternalMetadataKey] = "helper";
        _threads[thread.Id] = thread;
        return thread;
    }

    private async Task CompleteTurnAsync(SessionThread thread, MemoryWrite write = MemoryWrite.None)
    {
        await EndTurnAsync(thread, AddTurn(thread, write));
    }

    private async Task PostCompletedTurnsAsync(SessionThread thread, int count)
    {
        var turns = Enumerable.Range(0, count).Select(_ => AddTurn(thread)).ToArray();
        foreach (var turn in turns)
            await _scheduler.OnTurnEndedAsync(new TurnLifecycleContext(thread.Id, turn.Id) { Status = TurnStatus.Completed });
    }

    private async Task EndTurnAsync(SessionThread thread, SessionTurn turn)
    {
        await _scheduler.OnTurnEndedAsync(new TurnLifecycleContext(thread.Id, turn.Id) { Status = turn.Status });
        await _scheduler.WhenIdleAsync();
    }

    private static SessionTurn AddTurn(
        SessionThread thread,
        MemoryWrite write = MemoryWrite.None,
        TurnStatus status = TurnStatus.Completed)
    {
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1),
            ThreadId = thread.Id,
            Status = status,
            StartedAt = DateTimeOffset.UtcNow
        };
        if (write != MemoryWrite.None)
        {
            turn.Items.Add(new SessionItem
            {
                Id = "item_001",
                TurnId = turn.Id,
                Type = ItemType.ToolCall,
                Payload = new ToolCallPayload
                {
                    ToolName = "WriteFile",
                    ProviderFlatName = "WriteFile",
                    CallId = "call-1",
                    Arguments = new JsonObject { ["path"] = ".craft/memory/fact.md" },
                    Presentation = new ToolPresentationPayload
                    {
                        PresentationId = "core.file-write",
                        Options = new JsonObject { ["memory"] = "update" }
                    }
                }
            });
            turn.Items.Add(new SessionItem
            {
                Id = "item_002",
                TurnId = turn.Id,
                Type = ItemType.ToolResult,
                Payload = new ToolResultPayload
                {
                    ToolName = "WriteFile",
                    ProviderFlatName = "WriteFile",
                    CallId = "call-1",
                    Success = write == MemoryWrite.Succeeded
                }
            });
        }

        thread.Turns.Add(turn);
        return turn;
    }

    private async Task WaitForCallsAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (_runner.Calls.Count < count)
            await Task.Delay(10, timeout.Token);
    }

    private enum MemoryWrite
    {
        None,
        Succeeded,
        Failed
    }

    private sealed class FakeRunner : IMemoryExtractionRunner
    {
        private readonly List<(string ThreadId, string? Cursor)> _calls = [];

        public Func<int, string, string?, Task<string?>>? Behavior { get; set; }

        public IReadOnlyList<(string ThreadId, string? Cursor)> Calls
        {
            get
            {
                lock (_calls)
                    return _calls.ToArray();
            }
        }

        public Task<string?> RunAsync(string threadId, string? cursorTurnId, CancellationToken cancellationToken)
        {
            int call;
            lock (_calls)
            {
                call = _calls.Count;
                _calls.Add((threadId, cursorTurnId));
            }

            return Behavior?.Invoke(call, threadId, cursorTurnId) ?? Task.FromResult<string?>(null);
        }
    }
}
