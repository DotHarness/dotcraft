using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class ThreadListPagingTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ThreadListPaging_" + Guid.NewGuid().ToString("N"));
    private readonly ThreadStore _store;
    private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    public ThreadListPagingTests() => _store = new ThreadStore(_root);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        _store.StateDatabase.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Page_DecodesOnlyReturnedRowsAndCountsBeyondThePage()
    {
        await SaveAsync("newest", "First page", Timestamp.AddMinutes(1));
        await SaveAsync("oldest", "Not on first page", Timestamp);
        using (var connection = _store.StateDatabase.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                UPDATE threads SET created_at = 'not-a-timestamp' WHERE thread_id = 'oldest';
                """;
            command.ExecuteNonQuery();
        }

        var query = WorkspaceQuery() with { Limit = 1 };
        var first = await _store.ListThreadsAsync(query);
        Assert.Equal("newest", Assert.Single(first.Items).Id);
        Assert.Equal(2, first.TotalMatched);

        var empty = await _store.ListThreadsAsync(query with { Cursor = new ThreadListCursor(Timestamp.AddDays(-1), "before_all") });
        Assert.Empty(empty.Items);
        Assert.Equal(2, empty.TotalMatched);
    }

    [Fact]
    public async Task Pages_ApplyVisibilityIdentityAndLiteralFiltersBeforeCountingAndLimiting()
    {
        await SaveAsync("thread_a", "Résumé_%[a]", Timestamp, channel: "CUSTOM");
        await SaveAsync("thread_b", "Résumé_%[a]", Timestamp, channel: "custom");
        await SaveAsync("wrong_user", "Résumé_%[a]", Timestamp.AddHours(1), user: "other", channel: "custom");
        await SaveAsync("wrong_context", "Résumé_%[a]", Timestamp.AddHours(1), context: "other", channel: "custom");
        await SaveAsync("internal", "Résumé_%[a]", Timestamp.AddHours(1), channel: "custom", internalThread: true);
        await SaveAsync("archived", "Résumé_%[a]", Timestamp.AddHours(1), channel: "custom", archived: true);
        await SaveAsync("different_title", "Résumé_plain", Timestamp.AddHours(1), channel: "custom");

        var query = new ThreadListQuery
        {
            Identity = Identity(),
            ChannelName = "CuStOm",
            Query = "RÉSUMÉ_%[A]",
            Limit = 1
        };
        var first = await _store.ListThreadsAsync(query);
        Assert.Equal("thread_b", Assert.Single(first.Items).Id);
        Assert.Equal(2, first.TotalMatched);
        var second = await _store.ListThreadsAsync(query with { Cursor = CursorAfter(first.Items[^1]) });
        Assert.Equal("thread_a", Assert.Single(second.Items).Id);
        Assert.Equal(2, second.TotalMatched);

        var workspace = await _store.ListThreadsAsync(query with { Scope = ThreadDiscoveryScope.Workspace, Limit = 20 });
        Assert.Equal(4, workspace.TotalMatched);
        Assert.DoesNotContain(workspace.Items, summary => summary.Id is "internal" or "archived");
        var archive = await _store.ListThreadsAsync(query with { IncludeArchived = true, Limit = 20 });
        Assert.Equal(3, archive.TotalMatched);
    }

    [Fact]
    public async Task LiveSummaries_OverrideStoredFiltersAndOrderingWithoutDuplicates()
    {
        var first = await SaveAsync("stored_first", "Old title", Timestamp.AddHours(1));
        var second = await SaveAsync("stored_second", "Old title", Timestamp);
        first.DisplayName = "No longer matches";
        second.DisplayName = "Current title";
        second.LastActiveAt = Timestamp.AddHours(2);
        var added = new ThreadSummary
        {
            Id = "new_live",
            UserId = "local",
            OriginChannel = "cli",
            DisplayName = "Current title",
            LastActiveAt = Timestamp.AddHours(3)
        };
        var ephemeral = new ThreadSummary
        {
            Id = "ephemeral",
            Ephemeral = true,
            DisplayName = "Current title",
            LastActiveAt = Timestamp.AddHours(4)
        };
        var live = new[] { ThreadSummary.FromThread(first), ThreadSummary.FromThread(second), added, ephemeral };
        var query = WorkspaceQuery() with { Query = "Current title", Limit = 1 };
        var page = await _store.ListThreadsAsync(query, live);
        Assert.Equal("new_live", Assert.Single(page.Items).Id);
        Assert.Equal(2, page.TotalMatched);
        var next = await _store.ListThreadsAsync(query with { Cursor = CursorAfter(page.Items[^1]) }, live);
        Assert.Equal(second.Id, Assert.Single(next.Items).Id);
        Assert.Equal(2, next.TotalMatched);
    }

    [Fact]
    public async Task Pages_HideChildrenOfArchivedParentsIncludingLiveParentChanges()
    {
        var parent = await SaveAsync("parent", "Parent", Timestamp);
        var child = await SaveAsync("child", "Child", Timestamp.AddMinutes(1), user: "local", context: parent.Id,
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource { ParentThreadId = parent.Id, RootThreadId = parent.Id }));
        var query = WorkspaceQuery() with { IncludeSubAgents = true, Limit = 20 };
        Assert.Equal(2, (await _store.ListThreadsAsync(query)).TotalMatched);

        parent.Status = ThreadStatus.Archived;
        var page = await _store.ListThreadsAsync(query, [ThreadSummary.FromThread(parent)]);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalMatched);

        var archived = await _store.ListThreadsAsync(query with { IncludeArchived = true },
            [ThreadSummary.FromThread(parent)]);
        Assert.Equal(2, archived.TotalMatched);
        Assert.Contains(archived.Items, summary => summary.Id == child.Id);

        await _store.SaveThreadAsync(parent);
        Assert.Empty((await _store.ListThreadsAsync(query)).Items);
    }

    [Fact]
    public async Task CrossChannelOrigins_PreserveSyntheticUserDiscoveryAndInternalFiltering()
    {
        await SaveAsync("identity", "Native", Timestamp);
        await SaveAsync("automation", "Scheduled", Timestamp.AddMinutes(1), user: "automation:run", context: "run", channel: "automations");
        await SaveAsync("hidden", "Helper", Timestamp.AddMinutes(2), user: "internal", channel: "automations", internalThread: true);
        var query = new ThreadListQuery
        {
            Identity = Identity(),
            CrossChannelOrigins = ["AUTOMATIONS"],
            Limit = 1
        };
        var page = await _store.ListThreadsAsync(query);
        Assert.Equal("automation", Assert.Single(page.Items).Id);
        Assert.Equal(2, page.TotalMatched);
        Assert.Equal(3, (await _store.ListThreadsAsync(query with { IncludeInternal = true })).TotalMatched);
    }

    [Fact]
    public async Task UnavailableRollouts_AreExcludedBeforePageCountWithoutDeletingStoredRows()
    {
        await SaveAsync("missing", "Missing", Timestamp.AddHours(1));
        await SaveAsync("invalid", "Invalid", Timestamp.AddHours(1));
        await SaveAsync("present", "Present", Timestamp);
        await _store.FlushAndCloseAsync();
        File.Delete(Path.Combine(_root, "threads", "active", "missing.jsonl"));
        var invalidPath = Path.Combine(_root, "threads", "active", "invalid.jsonl");
        var lines = await File.ReadAllLinesAsync(invalidPath);
        lines[0] = lines[0].Replace("invalid", "another_thread", StringComparison.Ordinal);
        await File.WriteAllLinesAsync(invalidPath, lines);

        var page = await _store.ListThreadsAsync(WorkspaceQuery() with { Limit = 1 });
        Assert.Equal("present", Assert.Single(page.Items).Id);
        Assert.Equal(1, page.TotalMatched);
        using var connection = _store.StateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM threads";
        Assert.Equal(3L, command.ExecuteScalar());
    }

    [Fact]
    public async Task Continuation_DoesNotSkipOlderRowsWhenEarlierRowsAreDeletedOrArchived()
    {
        var oldest = await SaveAsync("oldest", "Oldest", Timestamp);
        var middle = await SaveAsync("middle", "Middle", Timestamp.AddMinutes(1));
        await SaveAsync("newest", "Newest", Timestamp.AddMinutes(2));
        var query = WorkspaceQuery() with { Limit = 1 };
        var first = await _store.ListThreadsAsync(query);
        Assert.Equal("newest", Assert.Single(first.Items).Id);
        Assert.True(first.HasMore);

        _store.DeleteThread("newest");
        await SaveAsync("inserted", "New activity", Timestamp.AddMinutes(3));
        var second = await _store.ListThreadsAsync(query with { Cursor = CursorAfter(first.Items[^1]) });
        Assert.Equal(middle.Id, Assert.Single(second.Items).Id);

        middle.Status = ThreadStatus.Archived;
        await _store.SaveThreadAsync(middle);
        var third = await _store.ListThreadsAsync(query with { Cursor = CursorAfter(second.Items[^1]) });
        Assert.Equal(oldest.Id, Assert.Single(third.Items).Id);
        Assert.False(third.HasMore);
        Assert.Equal(2, third.TotalMatched);
    }

    private static ThreadListCursor CursorAfter(ThreadSummary summary) => new(summary.LastActiveAt, summary.Id);

    private static SessionIdentity Identity() => new()
    {
        ChannelName = "desktop",
        UserId = "local",
        WorkspacePath = "unused"
    };

    private static ThreadListQuery WorkspaceQuery() => new()
    {
        Identity = Identity(),
        Scope = ThreadDiscoveryScope.Workspace
    };

    private async Task<SessionThread> SaveAsync(
        string id,
        string name,
        DateTimeOffset lastActiveAt,
        string user = "local",
        string? context = null,
        string channel = "cli",
        bool archived = false,
        bool internalThread = false,
        ThreadSource? source = null)
    {
        var thread = new SessionThread
        {
            Id = id,
            UserId = user,
            ChannelContext = context,
            OriginChannel = channel,
            WorkspacePath = Path.GetDirectoryName(_root)!,
            DisplayName = name,
            CreatedAt = Timestamp,
            LastActiveAt = lastActiveAt,
            Status = archived ? ThreadStatus.Archived : ThreadStatus.Active,
            Source = source ?? ThreadSource.User()
        };
        if (internalThread)
            thread.Metadata[ThreadVisibility.InternalMetadataKey] = "helper";
        await _store.SaveThreadAsync(thread);
        return thread;
    }
}
