using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context.WorldState;
using DotCraft.Sessions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class WorldStatePersistenceTests : IDisposable
{
    private readonly string _root;
    private readonly ThreadStore _store;

    public WorldStatePersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WorldState_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _store = new ThreadStore(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task Replay_AppliesPatchesOnTopOfTheFullSnapshot()
    {
        var thread = await CreateThreadWithTurnsAsync(2);

        await AppendWorldStateAsync(thread, thread.Turns[0].Id, full: true, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/a" },
            ["mode"] = new JsonObject { ["mode"] = "Agent" }
        });
        await AppendWorldStateAsync(thread, thread.Turns[1].Id, full: false, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/b" }
        });

        var baseline = await LoadBaselineAsync(thread);

        Assert.NotNull(baseline);
        Assert.Equal("/b", WorkingDirectory(baseline));
        Assert.True(baseline.TryGetSection("mode", out _));
    }

    [Fact]
    public async Task Replay_RemovesASectionTheMergePatchDeletes()
    {
        var thread = await CreateThreadWithTurnsAsync(2);

        await AppendWorldStateAsync(thread, thread.Turns[0].Id, full: true, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/a" },
            ["remote_tool_host"] = new JsonObject { ["host"] = "abc" }
        });
        await AppendWorldStateAsync(thread, thread.Turns[1].Id, full: false, new JsonObject
        {
            ["remote_tool_host"] = null
        });

        var baseline = await LoadBaselineAsync(thread);

        Assert.NotNull(baseline);
        Assert.False(baseline.TryGetSection("remote_tool_host", out _));
        Assert.True(baseline.TryGetSection("environment", out _));
    }

    [Fact]
    public async Task Replay_IgnoresAPatchWithoutAFullSnapshotToStandOn()
    {
        var thread = await CreateThreadWithTurnsAsync(1);

        await AppendWorldStateAsync(thread, thread.Turns[0].Id, full: false, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/a" }
        });

        Assert.Null(await LoadBaselineAsync(thread));
    }

    [Fact]
    public async Task Replay_DropsRecordsFromRolledBackTurns()
    {
        var thread = await CreateThreadWithTurnsAsync(2);

        await AppendWorldStateAsync(thread, thread.Turns[0].Id, full: true, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/a" }
        });
        await AppendWorldStateAsync(thread, thread.Turns[1].Id, full: false, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/rolled-back" }
        });

        await _store.RollbackThreadAsync(thread, numTurns: 1);
        var reloaded = await _store.LoadThreadAsync(thread.Id, CancellationToken.None);

        var baseline = await LoadBaselineAsync(reloaded!);

        Assert.NotNull(baseline);
        Assert.Equal("/a", WorkingDirectory(baseline));
    }

    [Fact]
    public async Task Replay_RestartsAtTheNewestCompactionCheckpoint()
    {
        var thread = await CreateThreadWithTurnsAsync(2);

        await AppendWorldStateAsync(thread, thread.Turns[0].Id, full: true, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/before-compaction" }
        });
        await _store.AppendCompactionCheckpointAsync(
            thread.Id,
            thread.Turns[1].Id,
            [new ChatMessage(ChatRole.User, "summary")],
            trigger: "auto",
            mode: "full",
            tokensBefore: 10,
            tokensAfter: 5);

        Assert.Null(await LoadBaselineAsync(thread));
    }

    [Fact]
    public async Task Replay_SkipsAMalformedRecordAndKeepsTheSurroundingOnes()
    {
        var thread = await CreateThreadWithTurnsAsync(2);

        await AppendWorldStateAsync(thread, thread.Turns[0].Id, full: true, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/a" }
        });
        await AppendRawAsync(thread, NullStateRecord(thread, thread.Turns[1].Id, full: false));
        await AppendWorldStateAsync(thread, thread.Turns[1].Id, full: false, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/b" }
        });

        var replay = await ReplayAsync(thread);

        Assert.Equal("/b", WorkingDirectory(Assert.IsType<WorldStateSnapshot>(replay.WorldState)));
        Assert.Equal(1, replay.RejectedRecords);
        Assert.Contains(replay.Warnings!, warning => warning.Code == "malformed_record");
    }

    [Fact]
    public async Task Replay_KeepsModelHistoryLoadableWhenAWorldStateRecordIsMalformed()
    {
        var thread = await CreateThreadWithTurnsAsync(1);

        await AppendRawAsync(thread, NullStateRecord(thread, thread.Turns[0].Id, full: true));

        var history = await _store.LoadModelHistoryAsync(thread.Id, CancellationToken.None);

        Assert.Contains(history, message => message.Text.Contains("ask ", StringComparison.Ordinal));
        Assert.Null(await LoadBaselineAsync(thread));
    }

    [Fact]
    public async Task Replay_KeepsExactTurnHistoryWhenAWorldStateRecordIsUnreadable()
    {
        var thread = await CreateThreadWithTurnsAsync(2);

        await AppendWorldStateAsync(thread, thread.Turns[0].Id, full: true, new JsonObject
        {
            ["environment"] = new JsonObject { ["workingDirectory"] = "/a" }
        });
        await AppendRawAsync(thread, UnreadableStateRecord(thread, thread.Turns[1].Id));

        var replay = await ReplayAsync(thread);

        Assert.DoesNotContain(thread.Turns[1].Id, replay.FallbackTurnIds ?? new HashSet<string>());
        Assert.Contains(
            replay.Messages,
            message => message.Text.Contains($"ask {thread.Turns[1].Id}", StringComparison.Ordinal));
        Assert.Equal("/a", WorkingDirectory(Assert.IsType<WorldStateSnapshot>(replay.WorldState)));
    }

    private static string UnreadableStateRecord(SessionThread thread, string turnId) =>
        JsonSerializer.Serialize(new
        {
            kind = RolloutKinds.WorldState,
            timestamp = DateTimeOffset.UnixEpoch,
            worldState = new
            {
                threadId = thread.Id,
                turnId,
                full = true,
                state = new[] { 1, 2 }
            }
        }, SessionJsonOptions.Default);

    private static string NullStateRecord(SessionThread thread, string turnId, bool full) =>
        JsonSerializer.Serialize(new
        {
            kind = RolloutKinds.WorldState,
            timestamp = DateTimeOffset.UnixEpoch,
            worldState = new
            {
                threadId = thread.Id,
                turnId,
                full,
                state = (JsonObject?)null
            }
        }, SessionJsonOptions.Default);

    private Task AppendRawAsync(SessionThread thread, string line) =>
        File.AppendAllTextAsync(
            Path.Combine(_root, "threads", "active", thread.Id + ".jsonl"),
            line.Trim() + Environment.NewLine);

    private Task<ModelHistoryReplayResult> ReplayAsync(SessionThread thread) =>
        new RolloutReplayer().ReplayModelHistoryAsync(
            Path.Combine(_root, "threads", "active", thread.Id + ".jsonl"),
            thread.Turns,
            excludedTurnId: null,
            CancellationToken.None,
            thread.Id);

    private static string? WorkingDirectory(WorldStateSnapshot baseline) =>
        baseline.TryGetSection("environment", out var environment)
            ? environment["workingDirectory"]?.GetValue<string>()
            : null;

    private Task<WorldStateSnapshot?> LoadBaselineAsync(SessionThread thread) =>
        _store.LoadWorldStateBaselineAsync(thread, CancellationToken.None);

    private Task AppendWorldStateAsync(SessionThread thread, string turnId, bool full, JsonObject state) =>
        _store.AppendWorldStateAsync(thread.Id, turnId, full, state, CancellationToken.None);

    private async Task<SessionThread> CreateThreadWithTurnsAsync(int turnCount)
    {
        var thread = new SessionThread
        {
            Id = SessionIdGenerator.NewThreadId(),
            WorkspacePath = "/workspace",
            UserId = "user1",
            OriginChannel = "console",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            HistoryMode = HistoryMode.Server
        };

        for (var index = 0; index < turnCount; index++)
        {
            thread.Turns.Add(new SessionTurn
            {
                Id = SessionIdGenerator.NewTurnId(index + 1),
                ThreadId = thread.Id,
                Status = TurnStatus.Completed,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });
        }

        await _store.SaveThreadAsync(thread, CancellationToken.None);
        foreach (var turn in thread.Turns)
        {
            await _store.AppendModelHistoryAsync(
                thread.Id,
                [new ChatMessage(ChatRole.User, $"ask {turn.Id}"), new ChatMessage(ChatRole.Assistant, "ok")],
                turn.Id,
                CancellationToken.None);
        }

        return thread;
    }
}
