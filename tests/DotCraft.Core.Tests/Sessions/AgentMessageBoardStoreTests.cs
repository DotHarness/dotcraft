using System.Text.Json;
using DotCraft.Persistence;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Core.Tests.Sessions;

public sealed class AgentMessageBoardStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "board-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceStateDatabase _database;
    private readonly AgentMessageBoardStore _store;
    private readonly BoardActor _root = new("root-a", "root-a", "/root");
    private readonly BoardActor _worker = new("root-a", "worker", "/root/worker");
    private readonly BoardActor _reviewer = new("root-a", "reviewer", "/root/reviewer");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AgentMessageBoardStoreTests()
    {
        _database = new(_directory);
        _store = new(_database);
    }

    [Fact]
    public void ExplicitOptOutSurvivesParticipationAndReopen_OneTimeNoticesDoNotSubscribe()
    {
        _store.CreateChannel(_root, new("createChannel", ChannelName: "work"));
        var first = _store.Post(_worker, new("post", ChannelName: "work", Text: "topic"), "1", [_root.Path, _root.Path]);
        Assert.Equal([_root.Path], first.Recipients);
        _store.ChangeSubscription(_root, new("subscribe", ThreadId: first.Metadata.MessageId, Subscribe: false), _root.Path);
        _store.Post(_root, new("post", ThreadId: first.Metadata.MessageId, Text: "reply"), "2", []);
        var reopened = new AgentMessageBoardStore(_database);
        var reply = reopened.Post(_worker, new("post", ThreadId: first.Metadata.MessageId, Text: "reply"), "3", []);
        Assert.Empty(reply.Recipients);
        var explicitReply = reopened.Post(_worker, new("post", ThreadId: first.Metadata.MessageId, Text: "explicit"), "4", [_root.Path]);
        Assert.Equal([_root.Path], explicitReply.Recipients);
        Assert.Empty(reopened.Post(_worker, new("post", ThreadId: first.Metadata.MessageId, Text: "later"), "5", []).Recipients);
        reopened.ChangeSubscription(_root, new("subscribe", ThreadId: first.Metadata.MessageId), _root.Path);
        Assert.Equal([_root.Path], reopened.Post(_worker, new("post", ThreadId: first.Metadata.MessageId, Text: "subscribed"), "6", []).Recipients);
    }

    [Fact]
    public async Task ConcurrentPostsAndRetriesAreAtomic()
    {
        _store.CreateChannel(_root, new("createChannel", ChannelName: "work"));
        var request = new BoardRequest("post", ChannelName: "work", Text: "same call");
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            new AgentMessageBoardStore(_database).Post(_worker, request, "same", []))));
        Assert.Single(attempts, x => x.Created);
        Assert.Single(attempts.Select(x => x.Metadata.MessageId).Distinct());
        Assert.All(attempts.Where(x => !x.Created), x => Assert.Empty(x.Recipients));
        Assert.Throws<BoardException>(() => _store.Post(_worker, request with { Text = "different" }, "same", []));
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => _store.Post(_worker, request, "distinct-" + i, []))));
        Assert.Equal(13, Node(_store.Read(_root, new("searchPosts"))).GetProperty("nReturned").GetInt32());
    }

    [Fact]
    public void SearchPaginationAndUnicodeSlicesPreservePostContent()
    {
        var first = _store.Post(_root, new("post", NewChannelName: "Research", Text: "Hello 😀世界"), "1", []);
        _store.Post(_worker, new("post", ChannelName: "Research", Text: "HELLO two"), "2", []);
        _store.Post(_reviewer, new("post", ChannelName: "Research", Text: "hello three"), "3", []);
        var page = Node(_store.Read(_root, new("searchPosts", Query: "hElLo", Limit: 2)));
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        var next = Node(_store.Read(_root, new("searchPosts", Query: "hElLo", Limit: 2, Cursor: page.GetProperty("nextCursor").GetString())));
        Assert.Equal(first.Metadata.MessageId, next.GetProperty("results")[0].GetProperty("messageId").GetString());
        var slice = Node(_store.Read(_root, new("readPost", MessageId: first.Metadata.MessageId, OffsetChars: 6, LimitChars: 2)));
        Assert.Equal("😀世", slice.GetProperty("text").GetString());
        Assert.Equal(9, slice.GetProperty("nChars").GetInt32());
        Assert.Equal(8, slice.GetProperty("nextOffsetChars").GetInt32());
        Assert.Equal(2, Node(_store.Read(_root, new("searchPosts", AfterMessageId: first.Metadata.MessageId))).GetProperty("nReturned").GetInt32());
        Assert.Single(Node(_store.Read(_root, new("getChannels", Query: "SEARCH"))).GetProperty("results").EnumerateArray());
        Assert.Single(Node(_store.Read(_root, new("searchPosts", Author: _worker.Path))).GetProperty("results").EnumerateArray());
        Assert.Throws<BoardException>(() => _store.Read(_root, new("searchPosts", Cursor: "bad cursor")));
    }

    [Fact]
    public void RootDeletionPreventsResurrectionButChildDeletionKeepsPosts()
    {
        var first = _store.Post(_root, new("post", NewChannelName: "work", Text: "retained"), "1", []);
        _store.Delete(_worker.ThreadId);
        Assert.Equal("retained", Node(_store.Read(_root, new("readPost", MessageId: first.Metadata.MessageId))).GetProperty("text").GetString());
        _store.Delete(_root.RootId);
        _store.Delete(_root.RootId);
        Assert.Equal("board_deleted", Assert.Throws<BoardException>(() => _store.Post(_worker,
            new("post", NewChannelName: "work", Text: "late"), "2", [])).Code);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM agent_board_posts";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public void InvalidWritesDoNotLeavePartialChannelsOrReplies()
    {
        Assert.Throws<BoardException>(() => _store.Post(_root, new("post", NewChannelName: "bad", Text: new string('x', 65537)), "1", []));
        Assert.Throws<BoardException>(() => _store.Post(_root, new("post", NewChannelName: " invalid", Text: "test"), "invalid-channel", []));
        Assert.Empty(Node(_store.Read(_root, new("getChannels"))).GetProperty("results").EnumerateArray());
        var topic = _store.Post(_root, new("post", NewChannelName: "work", Text: "topic"), "2", []);
        var reply = _store.Post(_worker, new("post", ThreadId: topic.Metadata.MessageId, Text: "reply"), "3", []);
        Assert.Throws<BoardException>(() => _store.Post(_root, new("post", ThreadId: reply.Metadata.MessageId, Text: "nested"), "4", []));
        var topics = Node(_store.Read(_root, new("listThreads", ChannelName: "work", Sort: "activity")));
        Assert.Equal(1, topics.GetProperty("results")[0].GetProperty("replyCount").GetInt32());
    }

    [Fact]
    public async Task DeletionSerializesWithConcurrentWriters()
    {
        _store.CreateChannel(_root, new("createChannel", ChannelName: "work"));
        var writers = Enumerable.Range(0, 12).Select(i => Task.Run(() =>
        {
            try { _store.Post(_worker, new("post", ChannelName: "work", Text: "race"), i.ToString(), []); }
            catch (BoardException ex) { Assert.Equal("board_deleted", ex.Code); }
        })).ToArray();
        await Task.Run(() => _store.Delete(_root.RootId));
        await Task.WhenAll(writers);
        Assert.Throws<BoardException>(() => _store.Post(_root, new("post", NewChannelName: "new", Text: "late"), "later", []));
    }

    private static JsonElement Node(object value) => JsonSerializer.SerializeToElement(value, Json);
    public void Dispose()
    {
        _database.Dispose();
        Directory.Delete(_directory, true);
    }
}
