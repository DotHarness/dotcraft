using System.Text;
using DotCraft.Persistence;
using DotCraft.Sessions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class ThreadProjectionMetadataTests : IDisposable
{
    private const string ThreadId = "thread_projection_metadata";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ThreadProjectionMetadataTests_" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task MixedWritesRollbackAndReplacement_ProjectWhatAFullReplayDerives()
    {
        await using var store = new ThreadStore(_root);
        var kept = CreateAttachment("kept.png");
        var replaced = CreateAttachment("replaced.png");
        var rolledBack = CreateAttachment("rolled-back.png");
        var queued = CreateAttachment("queued.png");
        var thread = CreateThread();
        thread.Turns.Add(CreateTurn(1, "  first request  ", kept, toolCall: true, tokens: 10));
        thread.Turns.Add(CreateTurn(2, "second", replaced, toolCall: false, tokens: 20));
        await store.SaveThreadAsync(thread);

        var second = thread.Turns[1];
        second.Items.RemoveAll(item => item.Type == ItemType.UserMessage);
        second.Items.Add(CreateUserItem(second.Id, 9, "replacement", attachment: null));
        second.Input = second.Items[^1];
        await store.SaveTurnAsync(thread, second);

        thread.Turns.Add(CreateTurn(3, "third", rolledBack, toolCall: true, tokens: 40));
        thread.QueuedInputs.Add(new QueuedTurnInput
        {
            Id = "queued_1",
            ThreadId = ThreadId,
            DisplayText = "later",
            NativeInputParts = [ImagePart(queued)]
        });
        await store.SaveThreadAsync(thread);
        thread.Turns.RemoveAt(2);
        thread.LastActiveAt = thread.LastActiveAt.AddMinutes(1);
        await store.RollbackThreadAsync(thread, 1);

        var replayed = (await new ThreadStore(_root).LoadThreadAsync(ThreadId))!;
        var summary = Assert.Single(await store.LoadIndexAsync());
        Assert.Equal(replayed.Turns.Count, summary.TurnCount);
        Assert.Equal(ExpectedFirstUserMessage(replayed), ReadFirstUserMessage());
        Assert.Equal(ExpectedReferences(replayed), ReadReferences());
        var aggregates = await store.ReadHistoryAggregatesAsync(ThreadId);
        Assert.Equal(replayed.Turns.Sum(turn => turn.TokenUsage?.TotalTokens ?? 0), aggregates.TokenUsage.TotalTokens);
        Assert.Equal(replayed.Turns.SelectMany(turn => turn.Items).Count(item => item.IsToolInvocation), aggregates.ToolCallCount);
        Assert.True(File.Exists(kept));
        Assert.True(File.Exists(queued));
        Assert.False(File.Exists(replaced));
        Assert.False(File.Exists(rolledBack));
    }

    [Fact]
    public async Task SavingOnlyRecentTurns_KeepsOlderTurnsAndTheirAttachments()
    {
        var older = CreateAttachment("older.png");
        var thread = CreateThread();
        thread.Turns.Add(CreateTurn(1, "older", older, toolCall: false, tokens: 5));
        thread.Turns.Add(CreateTurn(2, "recent", attachment: null, toolCall: false, tokens: 5));
        await using (var writer = new ThreadStore(_root))
            await writer.SaveThreadAsync(thread);

        await using var store = new ThreadStore(_root);
        var loaded = (await store.LoadThreadAsync(ThreadId))!;
        loaded.Turns.RemoveAt(0);
        loaded.Turns[0].Items.Add(CreateUserItem(loaded.Turns[0].Id, 5, "follow-up", attachment: null));
        loaded.DisplayName = "Renamed";
        await store.SaveThreadAsync(loaded);

        await using var unseeded = new ThreadStore(_root);
        SessionThread recentOnly;
        await using (var reader = new ThreadStore(_root))
            recentOnly = (await reader.LoadThreadAsync(ThreadId))!;
        recentOnly.Turns.RemoveAt(0);
        recentOnly.LastActiveAt = recentOnly.LastActiveAt.AddMinutes(5);
        await unseeded.SaveThreadAsync(recentOnly);

        Assert.True(File.Exists(older));
        Assert.Contains(older, ReadReferences().Select(reference => reference.Path));
        var summary = Assert.Single(await unseeded.LoadIndexAsync());
        Assert.Equal(2, summary.TurnCount);
        Assert.Equal("Renamed", summary.DisplayName);
        var replayed = (await new ThreadStore(_root).LoadThreadAsync(ThreadId))!;
        Assert.Equal(["turn_001", "turn_002"], replayed.Turns.Select(turn => turn.Id));
        Assert.Equal(["item_002_001", "item_002_005"], replayed.Turns[1].Items.Select(item => item.Id));
    }

    [Fact]
    public async Task WriteToALongThread_DoesNotReplayTheRollout()
    {
        var thread = CreateThread();
        for (var index = 1; index <= 40; index++)
            thread.Turns.Add(CreateTurn(index, $"request {index}", attachment: null, toolCall: true, tokens: 1));
        await using (var writer = new ThreadStore(_root))
            await writer.SaveThreadAsync(thread);
        var path = RolloutPath();
        var lines = File.ReadAllLines(path).Length;

        await using var store = new ThreadStore(_root);
        thread.DisplayName = "Renamed";
        await store.SaveThreadAsync(thread);
        thread.Turns[^1].Items.Add(CreateUserItem(thread.Turns[^1].Id, 7, "late", attachment: null));
        await store.SaveThreadAsync(thread);

        Assert.Equal(0, store.RolloutReplayCount);
        Assert.Equal(lines + 2, File.ReadAllLines(path).Length);
        var replayed = (await new ThreadStore(_root).LoadThreadAsync(ThreadId))!;
        Assert.Equal("Renamed", replayed.DisplayName);
        Assert.Equal(40, replayed.Turns.Count);
        Assert.Contains(replayed.Turns[^1].Items, item => item.Id == "item_040_007");
    }

    [Fact]
    public async Task DeletingStateDatabase_ListingRebuildsIdenticalSummariesAndReferences()
    {
        var attachment = CreateAttachment("rebuilt.png");
        List<ThreadSummary> before;
        IReadOnlyList<(string? TurnId, string? ItemId, string Path)> referencesBefore;
        string? firstUserMessageBefore;
        await using (var store = new ThreadStore(_root))
        {
            var thread = CreateThread();
            thread.DisplayName = "Rebuilt";
            thread.Turns.Add(CreateTurn(1, "keep me", attachment, toolCall: true, tokens: 3));
            thread.Turns.Add(CreateTurn(2, "drop me", attachment: null, toolCall: false, tokens: 3));
            await store.SaveThreadAsync(thread);
            thread.Turns.RemoveAt(1);
            await store.RollbackThreadAsync(thread, 1);
            before = await store.LoadIndexAsync();
            referencesBefore = ReadReferences();
            firstUserMessageBefore = ReadFirstUserMessage();
        }

        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(_root, "state.db*"))
            File.Delete(file);

        await using var rebuilt = new ThreadStore(_root);
        var after = await rebuilt.LoadIndexAsync();

        Assert.Equal(Describe(before), Describe(after));
        Assert.Equal(referencesBefore, ReadReferences());
        Assert.Equal(firstUserMessageBefore, ReadFirstUserMessage());
        Assert.True(File.Exists(attachment));
    }

    [Fact]
    public async Task ExternalAppend_ProjectsOnlyTheAppendedSuffix()
    {
        await using var store = new ThreadStore(_root);
        var thread = CreateThread();
        thread.Turns.Add(CreateTurn(1, "request", attachment: null, toolCall: false, tokens: 1));
        await store.SaveThreadAsync(thread);
        await store.FlushAndCloseAsync();
        var suffix = "{\"kind\":\"thread_name_updated\",\"timestamp\":\"2026-01-01T00:10:00Z\","
                     + $"\"threadNameUpdated\":{{\"threadId\":\"{ThreadId}\",\"displayName\":\"Appended\"}}}}\n";
        await File.AppendAllTextAsync(RolloutPath(), suffix, new UTF8Encoding(false));
        var projectedBefore = store.ProjectedRolloutBytes;

        var summary = Assert.Single(await store.LoadIndexAsync());

        Assert.Equal("Appended", summary.DisplayName);
        Assert.Equal(Encoding.UTF8.GetByteCount(suffix), store.ProjectedRolloutBytes - projectedBefore);
    }

    private static string? ExpectedFirstUserMessage(SessionThread thread) =>
        thread.Turns
            .SelectMany(turn => turn.Items)
            .Select(item => item.AsUserMessage?.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
            ?.Trim();

    private static List<(string? TurnId, string? ItemId, string Path)> ExpectedReferences(SessionThread thread) =>
        thread.Turns
            .SelectMany(turn => turn.Items
                .Where(item => item.AsUserMessage is not null)
                .SelectMany(item => item.AsUserMessage!.NativeInputParts!.Concat(item.AsUserMessage.MaterializedInputParts ?? [])
                    .Select(part => ((string?)turn.Id, (string?)item.Id, part.Path!))))
            .Concat(thread.QueuedInputs.SelectMany(queued => queued.NativeInputParts.Concat(queued.MaterializedInputParts)
                .Select(part => ((string?)queued.Id, (string?)null, part.Path!))))
            .Distinct()
            .OrderBy(reference => reference.Item3, StringComparer.Ordinal)
            .ToList();

    private List<(string? TurnId, string? ItemId, string Path)> ReadReferences()
    {
        using var connection = new WorkspaceStateDatabase(_root).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT turn_id, item_id, path FROM thread_attachments ORDER BY path";
        using var reader = command.ExecuteReader();
        var references = new List<(string?, string?, string)>();
        while (reader.Read())
            references.Add((reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2)));
        return references;
    }

    private string? ReadFirstUserMessage()
    {
        using var connection = new WorkspaceStateDatabase(_root).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT first_user_message FROM threads WHERE thread_id = $id";
        command.Parameters.AddWithValue("$id", ThreadId);
        return command.ExecuteScalar() as string;
    }

    private static IEnumerable<string> Describe(IEnumerable<ThreadSummary> summaries) =>
        summaries.Select(summary =>
            $"{summary.Id}|{summary.DisplayName}|{summary.Status}|{summary.TurnCount}|{summary.CreatedAt:O}|{summary.LastActiveAt:O}|{summary.OriginChannel}|{summary.UserId}");

    private string RolloutPath() => new ThreadRolloutStore(_root).ResolveExistingPath(ThreadId)!;

    private string CreateAttachment(string name)
    {
        var directory = Path.Combine(_root, "attachments", "images");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private static SessionThread CreateThread()
    {
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        return new SessionThread
        {
            Id = ThreadId,
            UserId = "local",
            OriginChannel = "test",
            Source = ThreadSource.User(),
            Status = ThreadStatus.Active,
            CreatedAt = createdAt,
            LastActiveAt = createdAt,
            HistoryMode = HistoryMode.Server,
            ProviderHistorySchemaVersion = 1
        };
    }

    private static SessionTurn CreateTurn(int index, string text, string? attachment, bool toolCall, long tokens)
    {
        var turnId = $"turn_{index:000}";
        var startedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z").AddSeconds(index);
        var user = CreateUserItem(turnId, 1, text, attachment);
        var turn = new SessionTurn
        {
            Id = turnId,
            ThreadId = ThreadId,
            Status = TurnStatus.Completed,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMilliseconds(500),
            TokenUsage = new TokenUsageInfo { InputTokens = tokens, OutputTokens = tokens, TotalTokens = tokens * 2, LlmCallCount = 1 },
            Input = user,
            Items = [user]
        };
        if (toolCall)
        {
            turn.Items.Add(new SessionItem
            {
                Id = $"item_{index:000}_002",
                TurnId = turnId,
                Type = ItemType.ToolCall,
                Status = ItemStatus.Completed,
                CreatedAt = startedAt,
                CompletedAt = startedAt,
                Payload = new ToolCallPayload { CallId = $"call_{index}", ToolName = "ReadFile" }
            });
        }

        return turn;
    }

    private static SessionItem CreateUserItem(string turnId, int index, string text, string? attachment) => new()
    {
        Id = $"item_{turnId[5..]}_{index:000}",
        TurnId = turnId,
        Type = ItemType.UserMessage,
        Status = ItemStatus.Completed,
        CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        CompletedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
        Payload = new UserMessagePayload
        {
            Text = text,
            NativeInputParts = attachment is null ? [] : [ImagePart(attachment)]
        }
    };

    private static SessionInputPart ImagePart(string path) =>
        new() { Type = "localImage", Path = path, MimeType = "image/png", FileName = Path.GetFileName(path) };
}
